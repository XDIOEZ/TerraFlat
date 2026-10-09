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
        public string TemplateId => config.TemplateId;
        public IReadOnlyList<BodyState> Bodies => config.Bodies;

        public SpaceCatalog(string json)
        {
            config = JsonConvert.DeserializeObject<SpaceCatalogConfig>(json,
                new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error }) ??
                throw new InvalidDataException("星系配置为空");
            if (config.SchemaVersion != 1 || string.IsNullOrWhiteSpace(config.TemplateId) || config.Surfaces == null)
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
                MetersPerUnityUnit = config.MetersPerUnityUnit, Bodies = config.Bodies
            });
            foreach (BodyState body in validation.State.Bodies)
            {
                if (string.IsNullOrWhiteSpace(body.SurfaceProfileId) || !surfaces.ContainsKey(body.SurfaceProfileId))
                    throw new InvalidDataException($"星体缺少独立地表配置：{body.BodyId}");
                bodies.Add(body.BodyId, body);
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
        private static void ValidateNumbers(Dictionary<string, double> values)
        {
            foreach (var value in values)
                if (string.IsNullOrWhiteSpace(value.Key) || !SpaceVector2.Finite(value.Value))
                    throw new InvalidDataException("星体生成参数不是有限数值");
        }
        #endregion

        #region 程序化星系与稳定身份
        public UniverseState Generate(int seed)
        {
            var state = new UniverseState
            {
                TemplateId = config.TemplateId, Seed = seed, FixedStepSeconds = config.FixedStepSeconds,
                MetersPerUnityUnit = config.MetersPerUnityUnit,
                Bodies = JsonConvert.DeserializeObject<List<BodyState>>(JsonConvert.SerializeObject(config.Bodies))
            };
            foreach (BodyState body in state.Bodies)
            {
                body.Seed = DeriveSeed(seed, body.BodyId);
                if (config.RandomizeOrbitalPhases && !string.IsNullOrWhiteSpace(body.ParentBodyId))
                    body.InitialPhaseRadians = ((uint)body.Seed / (double)uint.MaxValue) * Math.PI * 2d;
            }
            _ = new UniverseSimulation(state);
            return state;
        }

        public string ResolveBodyId(string planetId)
        {
            foreach (BodyState body in config.Bodies)
                if (string.Equals(body.PlanetId, planetId, StringComparison.Ordinal) ||
                    string.Equals(body.DisplayName, planetId, StringComparison.Ordinal)) return body.BodyId;
            return null;
        }

        public BodyState GetBodyDefinition(string bodyId) => bodies.TryGetValue(bodyId ?? string.Empty, out var body) ? body :
            throw new KeyNotFoundException($"星系模板中不存在星体：{bodyId}");
        public SurfaceProfile GetSurfaceProfile(string bodyId) => surfaces[GetBodyDefinition(bodyId).SurfaceProfileId];
        public SurfaceProfile GetSurfaceProfileById(string id) => surfaces.TryGetValue(id ?? string.Empty, out var surface) ? surface :
            throw new KeyNotFoundException($"星体地表配置不存在：{id}");

        public static int DeriveSeed(int seed, string bodyId)
        {
            unchecked
            {
                uint hash = (uint)seed ^ 2166136261u;
                foreach (char character in bodyId) { hash ^= character; hash *= 16777619u; }
                return (int)hash;
            }
        }
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
            planet.PrefabName = body.PrefabId; planet.OrbitCenterBodyId = body.ParentBodyId;
            planet.SurfaceGenerationProfileId = body.SurfaceProfileId;
            planet.PhysicalRadiusMeters = body.RadiusMeters; planet.SurfaceGravityMetersPerSecondSquared = body.SurfaceGravity;
            planet.OrbitRadius = (float)body.OrbitRadiusMeters;
            planet.OrbitAngularSpeed = body.OrbitalPeriodSeconds > 0d ? (float)(360d / body.OrbitalPeriodSeconds) : 0f;
            planet.OrbitStartAngle = (float)(body.InitialPhaseRadians * 180d / Math.PI);
            planet.OrbitClockwise = body.OrbitClockwise;
            planet.SelfRotateSpeed = Math.Abs(body.RotationPeriodSeconds) > 1e-12d ? (float)(360d / body.RotationPeriodSeconds) : 0f;
            if (!initializeEnvironment && planet.Atmosphere != null) return;
            SurfaceProfile surface = GetSurfaceProfileById(body.SurfaceProfileId);
            // 温度真值由地理气候层提供，星球全局量保留零额外修正。
            planet.GlobalTemperature = PlanetData.DefaultGlobalTemperature;
            planet.AtmosphereProfileId = surface.Id;
            var gases = new List<AtmosphereCompositionDto>();
            foreach (var gas in surface.AtmosphereComposition)
                gases.Add(new AtmosphereCompositionDto { FluidId = gas.Key, Fraction = gas.Value });
            decimal capacity = surface.PressureKPa > 0d ? 1000000000000000m : 0m;
            planet.Atmosphere = new AtmosphereDefinition(new AtmosphereDefinitionDto
            {
                Id = surface.Id, InitialStandardCubicMeters = capacity, CapacityStandardCubicMeters = capacity,
                ReferenceStandardCubicMeters = capacity, ReferencePressureKPa = surface.PressureKPa,
                TemperatureKelvin = surface.TemperatureCelsius + 273.15d, Composition = gases
            }, FluidCatalog.Default).CreateState();
        }
        #endregion

        #region 冻结星体生成 Profile
        public ChunkGenerationProfileSnapshot ApplySurfaceProfile(string bodyId, ChunkGenerationProfileSnapshot original)
        {
            if (original == null) throw new ArgumentNullException(nameof(original));
            if (string.IsNullOrWhiteSpace(bodyId) || !bodies.ContainsKey(bodyId)) return original;
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
        public bool RandomizeOrbitalPhases = true;
        public List<BodyState> Bodies;
        public List<SurfaceProfile> Surfaces;
        #endregion
    }
}
