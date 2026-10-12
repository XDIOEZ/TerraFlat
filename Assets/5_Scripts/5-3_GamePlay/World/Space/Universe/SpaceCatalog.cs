using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using FlatWorld.WorldModel;
using Newtonsoft.Json;
using UnityEngine;

namespace FlatWorld.Spaceflight
{
    [Serializable]
    public sealed class SurfaceProfile
    {
        #region 独立地表与环境配置
        public string Id;
        public double TemperatureCelsius = 26d;
        public double PressureKPa = 100d;
        public Dictionary<string, decimal> AtmosphereComposition = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, double> NumericParameters = new(StringComparer.Ordinal);
        public Dictionary<string, string> TextParameters = new(StringComparer.Ordinal);
        public Dictionary<string, string> TileParameters = new(StringComparer.Ordinal);
        public Dictionary<string, double> CaveNumericParameters = new(StringComparer.Ordinal);
        public List<string> EcologyRuleIds;
        public List<string> CaveResourceRuleIds;
        public double EcologyMultiplier = 1d;
        #endregion
    }

    public sealed class SpaceCatalog
    {
        #region 星系配置读取
        public const string DefaultConfigPath = "GameConfig/Space/solar-system.json";
        private static SpaceCatalog current;
        private readonly SpaceCatalogConfig config;
        private readonly Dictionary<string, BodyState> bodies = new(StringComparer.Ordinal);
        private readonly Dictionary<string, SurfaceProfile> surfaces = new(StringComparer.Ordinal);
        private readonly Dictionary<SpaceSectorId, SpaceSectorSystemDefinition> systems = new();
        private readonly List<SpaceSectorId> configuredSectors = new() { default };
        public string TemplateId => config.TemplateId;
        public IReadOnlyList<BodyState> Bodies => config.Bodies;
        public IReadOnlyList<SpaceSectorId> ConfiguredSectors => configuredSectors;

        public SpaceCatalog(string json)
        {
            config = JsonConvert.DeserializeObject<SpaceCatalogConfig>(json,
                new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error }) ??
                throw new InvalidDataException("星系配置为空");
            if (config.SchemaVersion != 1 || string.IsNullOrWhiteSpace(config.TemplateId) || config.Surfaces == null || config.SectorSystems == null)
                throw new InvalidDataException("星系模板版本、身份或地表列表无效");
            foreach (SurfaceProfile surface in config.Surfaces)
            {
                if (surface == null || string.IsNullOrWhiteSpace(surface.Id) || !surfaces.TryAdd(surface.Id, surface) ||
                    !SpaceVector2.Finite(surface.TemperatureCelsius) || surface.TemperatureCelsius <= -273.15d ||
                    !SpaceVector2.Finite(surface.PressureKPa) || surface.PressureKPa < 0d ||
                    !SpaceVector2.Finite(surface.EcologyMultiplier) || surface.EcologyMultiplier < 0d ||
                    surface.NumericParameters == null || surface.TextParameters == null ||
                    surface.TileParameters == null || surface.CaveNumericParameters == null ||
                    surface.AtmosphereComposition == null)
                    throw new InvalidDataException("星体地表、气压或温度配置无效");
                decimal fraction = 0m;
                foreach (var gas in surface.AtmosphereComposition)
                {
                    if (string.IsNullOrWhiteSpace(gas.Key) || gas.Value < 0m || gas.Value > 1m)
                        throw new InvalidDataException($"星体大气成分无效：{surface.Id}");
                    fraction += gas.Value;
                }
                if (surface.PressureKPa > 0d ? fraction != 1m : fraction != 0m)
                    throw new InvalidDataException($"星体大气组分之和无效：{surface.Id}");
                ValidateNumbers(surface.NumericParameters); ValidateNumbers(surface.CaveNumericParameters);
            }
            var validation = new UniverseSimulation(new UniverseState
            {
                TemplateId = config.TemplateId, FixedStepSeconds = config.FixedStepSeconds,
                MetersPerUnityUnit = config.MetersPerUnityUnit, OrbitTimeScale = config.OrbitTimeScale, Bodies = config.Bodies
            });
            foreach (BodyState body in validation.State.Bodies)
            {
                if (string.IsNullOrWhiteSpace(body.SurfaceProfileId) || !surfaces.ContainsKey(body.SurfaceProfileId))
                    throw new InvalidDataException($"星体缺少独立地表配置：{body.BodyId}");
                bodies.Add(body.BodyId, body);
            }
            foreach (SpaceSectorSystemDefinition system in config.SectorSystems)
            {
                if (system == null || system.Sector == default || string.IsNullOrWhiteSpace(system.TemplateId) ||
                    system.Bodies == null || !systems.TryAdd(system.Sector, system))
                    throw new InvalidDataException("星区目录的坐标、模板身份或星体列表无效");
                _ = new UniverseSimulation(new UniverseState { Sector = system.Sector, Bodies = system.Bodies });
                configuredSectors.Add(system.Sector);
                foreach (BodyState body in system.Bodies)
                    if (string.IsNullOrWhiteSpace(body.SurfaceProfileId) || !surfaces.ContainsKey(body.SurfaceProfileId))
                        throw new InvalidDataException($"星区 {system.Sector} 的星体缺少地表配置：{body.BodyId}");
            }
        }

        public static SpaceCatalog LoadDefault()
        {
            if (current != null) return current;
            string path = StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath, DefaultConfigPath);
            return current = new SpaceCatalog(StreamingAssetsTextLoader.ReadAllText(path));
        }

        public static IEnumerator LoadDefaultAsync(Action<SpaceCatalog> completed, Action<Exception> failed)
        {
            if (current != null) { completed?.Invoke(current); yield break; }
            string json = null; Exception error = null;
            string path = StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath, DefaultConfigPath);
            yield return StreamingAssetsTextLoader.ReadAllTextAsync(path, value => json = value, value => error = value);
            if (error != null) { failed?.Invoke(error); yield break; }
            SpaceCatalog loaded;
            try { loaded = new SpaceCatalog(json); }
            catch (Exception exception) { failed?.Invoke(exception); yield break; }
            current = loaded;
            completed?.Invoke(loaded);
        }

        public static void Reset() => current = null;

        public void RebindUniverse(UniverseState state)
        {
            if (state?.Bodies == null) throw new ArgumentNullException(nameof(state));
            // 存档保留身份、种子、初相位和时钟，物理规则重新绑定当前内容目录。
            state.FixedStepSeconds = config.FixedStepSeconds;
            state.MetersPerUnityUnit = config.MetersPerUnityUnit;
            state.OrbitTimeScale = config.OrbitTimeScale;
            foreach (BodyState body in state.Bodies)
            {
                string localId = string.IsNullOrWhiteSpace(body.TemplateBodyId) ? body.BodyId : body.TemplateBodyId;
                BodyState definition = FindDefinition(state.Sector, localId);
                body.TemplateBodyId = definition.BodyId; body.Sector = state.Sector;
                body.DisplayName = definition.DisplayName; body.PrefabId = definition.PrefabId;
                body.SurfaceProfileId = definition.SurfaceProfileId;
                body.ParentBodyId = string.IsNullOrWhiteSpace(definition.ParentBodyId) ? string.Empty :
                    SpaceSectorCoordinates.BodyId(state.Sector, definition.ParentBodyId);
                body.RadiusMeters = definition.RadiusMeters; body.SurfaceGravity = definition.SurfaceGravity;
                body.PlacementDistanceMeters = definition.PlacementDistanceMeters;
                body.OrbitalPeriodSeconds = definition.OrbitalPeriodSeconds;
                body.OrbitalEccentricity = definition.OrbitalEccentricity;
                body.OrbitalPeriapsisRadians = definition.OrbitalPeriapsisRadians;
                body.RotationPeriodSeconds = definition.RotationPeriodSeconds;
            }
        }

        private static void ValidateNumbers(Dictionary<string, double> values)
        {
            foreach (var value in values)
                if (string.IsNullOrWhiteSpace(value.Key) || !SpaceVector2.Finite(value.Value))
                    throw new InvalidDataException("星体生成参数不是有限数值");
        }
        #endregion

        #region 程序化星系与稳定身份
        public UniverseState Generate(int seed) => GenerateSector(seed, default);

        public UniverseState GenerateSector(int seed, SpaceSectorId sector)
        {
            IReadOnlyList<BodyState> definitions = GetDefinitions(sector);
            var template = new UniverseState
            {
                TemplateId = sector == default ? config.TemplateId :
                    systems.TryGetValue(sector, out SpaceSectorSystemDefinition system) ? system.TemplateId : "core:empty-space",
                FixedStepSeconds = config.FixedStepSeconds,
                MetersPerUnityUnit = config.MetersPerUnityUnit, OrbitTimeScale = config.OrbitTimeScale,
                Bodies = new List<BodyState>(definitions)
            };
            return UniverseSectorFactory.Generate(template, seed, sector, config.RandomizeBodyPlacements);
        }

        public string ResolveBodyId(string planetId)
        {
            SpaceSectorId sector = default;
            if (SpaceSectorCoordinates.TryParseIdentity(planetId, out SpaceSectorId parsed, out string localId))
            { sector = parsed; planetId = localId; }
            foreach (BodyState body in GetDefinitions(sector))
                if (string.Equals(body.PlanetId, planetId, StringComparison.Ordinal) ||
                    string.Equals(body.DisplayName, planetId, StringComparison.Ordinal) ||
                    string.Equals(body.BodyId, planetId, StringComparison.Ordinal)) return SpaceSectorCoordinates.BodyId(sector, body.BodyId);
            return null;
        }

        private IReadOnlyList<BodyState> GetDefinitions(SpaceSectorId sector)
            => sector == default ? config.Bodies : systems.TryGetValue(sector, out SpaceSectorSystemDefinition system) ?
                system.Bodies : Array.Empty<BodyState>();
        private BodyState FindDefinition(SpaceSectorId sector, string localId)
        {
            foreach (BodyState body in GetDefinitions(sector)) if (body.BodyId == localId) return body;
            throw new KeyNotFoundException($"星区 {sector} 的模板中不存在星体：{localId}");
        }
        public BodyState GetBodyDefinition(string bodyId)
            => SpaceSectorCoordinates.TryParseIdentity(bodyId, out SpaceSectorId sector, out string localId)
                ? FindDefinition(sector, localId) : FindDefinition(default, bodyId);
        public bool TryGetBodyDefinition(string bodyId, out BodyState definition)
        {
            definition = null;
            SpaceSectorId sector = default;
            if (SpaceSectorCoordinates.TryParseIdentity(bodyId, out SpaceSectorId parsed, out string localId))
            { sector = parsed; bodyId = localId; }
            foreach (BodyState body in GetDefinitions(sector)) if (body.BodyId == bodyId) { definition = body; return true; }
            return false;
        }
        public SurfaceProfile GetSurfaceProfile(string bodyId) => surfaces[GetBodyDefinition(bodyId).SurfaceProfileId];
        public SurfaceProfile GetSurfaceProfileById(string id) => surfaces.TryGetValue(id ?? string.Empty, out var surface) ? surface :
            throw new KeyNotFoundException($"星体地表配置不存在：{id}");

        public static int DeriveSeed(int seed, string bodyId) => UniverseSectorFactory.DeriveSeed(seed, bodyId);
        #endregion

        #region 星球持久数据与真实大气
        public PlanetData CreatePlanetData(string bodyId, PlanetData earthTemplate = null)
        {
            BodyState body = GetBodyDefinition(bodyId);
            PlanetData planet = earthTemplate == null ? new PlanetData() : FastCloner.FastCloner.DeepClone(earthTemplate);
            // 新星体只继承用户的世界尺寸，地图、生态和大气都独立创建。
            planet.MapData_Dict = new Dictionary<string, MapSave>();
            planet.Ecology = new EcologyWorldSaveData();
            planet.SeasonalSnow = new SnowCoverState();
            planet.Atmosphere = null;
            ApplyPlanetMetadata(body, planet, true);
            return planet;
        }

        public void ApplyPlanetMetadata(BodyState body, PlanetData planet, bool initializeEnvironment = false)
        {
            if (body == null || planet == null) throw new ArgumentNullException(nameof(planet));
            planet.BodyId = body.BodyId; planet.Name = body.PlanetId; planet.name = body.DisplayName;
            planet.PrefabName = body.PrefabId;
            planet.SurfaceGenerationProfileId = body.SurfaceProfileId;
            planet.PhysicalRadiusMeters = body.RadiusMeters; planet.SurfaceGravityMetersPerSecondSquared = body.SurfaceGravity;
            planet.SelfRotateSpeed = Math.Abs(body.RotationPeriodSeconds) > 1e-12d ? (float)(360d / body.RotationPeriodSeconds) : 0f;
            bool repairInvalidAtmosphere = false;
            string invalidAtmosphereDetails = null;
            if (!initializeEnvironment && planet.Atmosphere != null)
            {
                try
                {
                    AtmosphereService.Validate(planet.Atmosphere, FluidCatalog.Default);
                    return;
                }
                catch (InvalidDataException exception)
                {
                    repairInvalidAtmosphere = true;
                    invalidAtmosphereDetails = $"{exception.Message} [容量={planet.Atmosphere.CapacityMoles}mol, " +
                        $"基准={planet.Atmosphere.ReferenceMoles}mol, 压力={planet.Atmosphere.ReferencePressureKPa}kPa, " +
                        $"温度={planet.Atmosphere.TemperatureKelvin}K, 成分数={planet.Atmosphere.Gases?.Count ?? -1}]";
                }
            }
            SurfaceProfile surface = GetSurfaceProfileById(body.SurfaceProfileId);
            // 温度真值由地理气候层提供，星球全局量保留零额外修正。
            if (!repairInvalidAtmosphere) planet.GlobalTemperature = PlanetData.DefaultGlobalTemperature;
            planet.AtmosphereProfileId = surface.Id;
            var gases = new List<AtmosphereCompositionDto>();
            foreach (var gas in surface.AtmosphereComposition)
                gases.Add(new AtmosphereCompositionDto { FluidId = gas.Key, Fraction = gas.Value });
            decimal capacity = surface.PressureKPa > 0d ? 1000000000000000m : 0m;
            AtmosphereState initialAtmosphere = new AtmosphereDefinition(new AtmosphereDefinitionDto
            {
                Id = surface.Id, InitialStandardCubicMeters = capacity, CapacityStandardCubicMeters = capacity,
                ReferenceStandardCubicMeters = capacity, ReferencePressureKPa = surface.PressureKPa,
                TemperatureKelvin = surface.TemperatureCelsius + 273.15d, Composition = gases
            }, FluidCatalog.Default).CreateState();
            if (!repairInvalidAtmosphere)
            {
                planet.Atmosphere = initialAtmosphere;
                return;
            }

            // 载入时修复异常的大气基准，避免玩家生存模块和 HUD 每帧重复抛错。
            planet.Atmosphere = AtmosphereService.RepairInvalidSnapshot(planet.Atmosphere, initialAtmosphere,
                out bool preservedInventory);
            Debug.LogWarning($"[Atmosphere] 星球 {body.PlanetId} 的大气快照无效：{invalidAtmosphereDetails}；已按地表配置修复" +
                             (preservedInventory ? "，保留现有气体和逸散量。" : "；原库存不可读取，已恢复初始大气。"));
        }
        #endregion

        #region 冻结星体生成 Profile
        public ChunkGenerationProfileSnapshot ApplySurfaceProfile(string bodyId, ChunkGenerationProfileSnapshot original)
        {
            if (original == null) throw new ArgumentNullException(nameof(original));
            if (string.IsNullOrWhiteSpace(bodyId) || !TryGetBodyDefinition(bodyId, out _)) return original;
            if (original.TextParameters.TryGetValue("space.bodyId", out string applied) && applied == bodyId) return original;
            SurfaceProfile surface = GetSurfaceProfile(bodyId);
            bool cave = original.Settings.Mode == ChunkGenerationMode.Cave;
            var numbers = new Dictionary<string, double>(original.NumericParameters, StringComparer.Ordinal);
            var texts = new Dictionary<string, string>(original.TextParameters, StringComparer.Ordinal);
            foreach (var value in cave ? surface.CaveNumericParameters : surface.NumericParameters) numbers[value.Key] = value.Value;
            if (!cave)
            {
                foreach (var value in surface.TextParameters) texts[value.Key] = value.Value;
                foreach (var value in surface.TileParameters)
                {
                    GameRes resources = GameRes.ExistingInstance;
                    if (resources == null || !resources.TileBlockDict.TryGetValue(value.Value, out RuntimeTileDefinition tile))
                        throw new InvalidDataException($"星体 {bodyId} 引用未知地块：{value.Value}");
                    numbers[value.Key] = tile.RuntimeTileId;
                    texts[$"tile.block.{tile.RuntimeTileId}"] = tile.Id;
                }
            }
            texts["space.bodyId"] = bodyId;
            texts["space.surfaceProfileId"] = surface.Id;
            texts["space.surfaceSeedScope"] = bodyId;
            numbers["space.surfaceTemperatureCelsius"] = surface.TemperatureCelsius;
            numbers["space.surfacePressureKPa"] = surface.PressureKPa;
            var ecology = surface.EcologyRuleIds == null || cave ? new List<EcologySpawnRuleSnapshot>(original.EcologyRules) :
                ResolveEcology(surface.EcologyRuleIds);
            var minerals = surface.CaveResourceRuleIds == null ? new List<CaveResourceRuleSnapshot>(original.CaveResourceRules) :
                ResolveMinerals(surface.CaveResourceRuleIds);
            return new ChunkGenerationProfileSnapshot(cave ? original.ProfileId + "." + bodyId : surface.Id,
                original.Signature + 1000, original.Width, original.Height, numbers, texts,
                original.EcologyGlobalMultiplier * surface.EcologyMultiplier, ecology, minerals, original.PortalPairing);
        }

        private static List<EcologySpawnRuleSnapshot> ResolveEcology(List<string> ids)
        {
            var result = new List<EcologySpawnRuleSnapshot>();
            if (ids.Count == 0) return result;
            NaturalGenerationRuleCatalog catalog = NaturalGenerationRuleCatalogService.RequireCatalog();
            foreach (string id in ids) result.Add(catalog.GetEcologyRule(id));
            return result;
        }

        private static List<CaveResourceRuleSnapshot> ResolveMinerals(List<string> ids)
        {
            var result = new List<CaveResourceRuleSnapshot>();
            if (ids.Count == 0) return result;
            NaturalGenerationRuleCatalog catalog = NaturalGenerationRuleCatalogService.RequireCatalog();
            foreach (string id in ids) result.Add(catalog.GetCaveResourceRule(id));
            return result;
        }
        #endregion
    }

    [Serializable]
    internal sealed class SpaceCatalogConfig
    {
        #region 模板序列化
        public int SchemaVersion;
        public string TemplateId;
        public double FixedStepSeconds = 0.02d;
        public double MetersPerUnityUnit = 1d;
        public double OrbitTimeScale = 1d;
        public bool RandomizeBodyPlacements = true;
        public List<SpaceSectorSystemDefinition> SectorSystems = new();
        public List<BodyState> Bodies;
        public List<SurfaceProfile> Surfaces;
        #endregion
    }

    [Serializable]
    internal sealed class SpaceSectorSystemDefinition
    {
        #region 可扩展星区内容目录
        public SpaceSectorId Sector;
        public string TemplateId;
        public List<BodyState> Bodies;
        #endregion
    }
}
