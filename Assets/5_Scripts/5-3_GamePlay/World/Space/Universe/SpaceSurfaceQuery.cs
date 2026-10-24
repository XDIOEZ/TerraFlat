using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using Unity.Profiling;
using UnityEngine;
using RuntimeAddress = FlatWorld.WorldModel.WorldAddress;

namespace FlatWorld.Spaceflight
{
    public struct LandingSurfaceSample
    {
        #region 落点真实地表快照
        public bool IsWater;
        public int GroundTileId;
        public int BlockingTileId;
        public int SupportTileId;
        public string LiquidId;
        public int LiquidLevel;
        public float LiquidDepth => LiquidCellValue.ToNormalizedDepth(LiquidLevel);
        public int BiomeId;
        public double TemperatureCelsius;
        public double PressureKPa;
        public Vector2Int Cell;
        #endregion
    }

    public static class SpaceSurfaceQuery
    {
        #region 地表结构冲击入口
        public static SpaceSurfaceImpactResult ApplyStructureDamageInRadius(string worldKey, Vector2 center, float radius, float peak,
            bool environmentalDamage = false, ulong eventId = 0)
            => SaveDataMgr.Instance.ApplySpaceSurfaceImpact(global::WorldAddress.FromWorldKey(worldKey), center, radius, peak,
                environmentalDamage, eventId);

        /// <summary>环境范围伤害复用地表防御、建筑库存掉落与冷热差量提交。</summary>
        public static SpaceSurfaceImpactResult ApplyEnvironmentalDamageInRadius(string worldKey, Vector2 center, float radius, float peak,
            ulong eventId)
            => ApplyStructureDamageInRadius(worldKey, center, radius, peak, true, eventId);

        // 冷地表也按真实格容量接纳液体，调用者只扣除返回的实际泄出量。
        public static int ReleaseLiquidInWorld(string worldKey, Vector2 center, string liquidId, int level)
        {
            if (!GameNetwork.HasStateAuthority || level <= 0 || string.IsNullOrWhiteSpace(worldKey) ||
                worldKey.StartsWith("ship:", StringComparison.Ordinal) ||
                !global::WorldAddress.FromWorldKey(worldKey).IsSurface || GameRes.ExistingInstance == null ||
                !GameRes.ExistingInstance.TryGetLiquidDefinition(liquidId, out LiquidDefinition definition) || definition.WorldWater == null)
                return 0;
            LandingSurfaceSample sample = GetLandingSurface(worldKey, center);
            if (sample.BlockingTileId != 0 || sample.SupportTileId != 0 ||
                (sample.LiquidLevel > 0 && !string.Equals(sample.LiquidId, liquidId, StringComparison.OrdinalIgnoreCase))) return 0;
            int accepted = Math.Min(level, LiquidCellValue.MaximumLevel - sample.LiquidLevel);
            int next = sample.LiquidLevel + accepted;
            if (accepted <= 0) return 0;
            PlanetData planet = SaveDataMgr.Instance.SaveData.PlanetData_Dict[worldKey];
            Vector2Int size = PlanetData.NormalizeChunkSize(planet.ChunkSize);
            int originX = (int)Math.Floor(sample.Cell.x / (double)size.x) * size.x;
            int originY = (int)Math.Floor(sample.Cell.y / (double)size.y) * size.y;
            var address = new RuntimeAddress(global::WorldAddress.SurfaceDimensionId, new Int2(originX, originY));
            ChunkTerrainData loaded = GetLoadedTerrain(worldKey, address);
            if (loaded != null)
                return WorldLiquidSystem.TryPour(new Vector2(sample.Cell.x + .5f, sample.Cell.y + .5f), liquidId, accepted, out int added) ? added : 0;
            if (!caches.TryGetValue(worldKey, out SurfaceCache cache) || !cache.World.TryGetChunkTerrain(address, out ChunkTerrainData baseline))
                throw new InvalidOperationException("冷液体写回前缺少地表生成基线");
            int x = sample.Cell.x - originX, y = sample.Cell.y - originY;
            SaveDataMgr.Instance.RecordSpaceLiquidChange(worldKey, sample.Cell, liquidId, next,
                baseline.GetLiquidId(x, y), baseline.GetLiquidLevel(x, y));
            return accepted;
        }

        public static bool TrySampleLiquid(string worldKey, Vector2 point, out LiquidDefinition liquid, out int level)
        {
            bool found = TryReadLiquidSurface(worldKey, point, out LandingSurfaceSample sample, out liquid);
            level = found ? sample.LiquidLevel : 0;
            return found;
        }

        // 同一抽液事务共用液体校验与地表采样，避免重复读取冷区块和存档差量。
        private static bool TryReadLiquidSurface(string worldKey, Vector2 point, out LandingSurfaceSample sample, out LiquidDefinition liquid)
        {
            sample = default;
            liquid = null;
            GameRes resources = GameRes.ExistingInstance;
            if (string.IsNullOrWhiteSpace(worldKey) || worldKey.StartsWith("ship:", StringComparison.Ordinal) ||
                !global::WorldAddress.FromWorldKey(worldKey).IsSurface || !float.IsFinite(point.x) || !float.IsFinite(point.y) ||
                resources == null || !TryGetLandingSurface(worldKey, point, out sample) ||
                sample.LiquidDepth <= 0f || !resources.TryGetLiquidDefinition(sample.LiquidId, out liquid) || liquid.WorldWater == null)
            { liquid = null; return false; }
            return true;
        }

        // 抽取冷世界真实有限库存，平台只遮断浸水而不复制或删除底部液体。
        public static bool TryPumpLiquidInWorld(string worldKey, Vector2 point, int requestedLevel, out string liquidId, out int removedLevel)
        {
            liquidId = null; removedLevel = 0;
            if (!GameNetwork.HasStateAuthority || requestedLevel <= 0 ||
                !TryReadLiquidSurface(worldKey, point, out LandingSurfaceSample sample, out _)) return false;
            PlanetData planet = SaveDataMgr.Instance.SaveData.PlanetData_Dict[worldKey];
            Vector2Int size = PlanetData.NormalizeChunkSize(planet.ChunkSize);
            int originX = (int)Math.Floor(sample.Cell.x / (double)size.x) * size.x;
            int originY = (int)Math.Floor(sample.Cell.y / (double)size.y) * size.y;
            var address = new RuntimeAddress(global::WorldAddress.SurfaceDimensionId, new Int2(originX, originY));
            if (GetLoadedTerrain(worldKey, address) != null)
                return WorldLiquidSystem.TryPump(new Vector2(sample.Cell.x + .5f, sample.Cell.y + .5f), requestedLevel, out liquidId, out removedLevel);
            if (!caches.TryGetValue(worldKey, out SurfaceCache cache) || !cache.World.TryGetChunkTerrain(address, out ChunkTerrainData baseline))
                throw new InvalidOperationException("冷液体抽取前缺少地表生成基线");
            removedLevel = Math.Min(requestedLevel, sample.LiquidLevel);
            int next = sample.LiquidLevel - removedLevel;
            if (removedLevel <= 0) return false;
            liquidId = sample.LiquidId;
            SaveDataMgr.Instance.RecordSpaceLiquidChange(worldKey, sample.Cell, next > 0 ? liquidId : null, next,
                baseline.GetLiquidId(sample.Cell.x - originX, sample.Cell.y - originY),
                baseline.GetLiquidLevel(sample.Cell.x - originX, sample.Cell.y - originY));
            return true;
        }
        #endregion

        #region 不依赖活动场景的地表查询
        private const int MaximumCachedChunksPerPlanet = 32;
        private static GameSaveData owner;
        private static readonly Dictionary<string, SurfaceCache> caches = new(StringComparer.Ordinal);
        private static readonly Dictionary<PlanetData, ChunkGenerationProfileSnapshot> frozenProfiles = new();
        private static GameRes resourceOwner;
        private static DimensionCatalogSO generationCatalog;
        private static readonly Dictionary<(ChunkGenerationProfileSnapshot Profile, int Seed, int X, int Y,
            bool Wrapped, Int2 Min, Int2 Span), float> temperatureSamples = new();
        private static readonly ProfilerMarker coldChunkGenerationMarker = new("SpaceSurfaceQuery.ColdChunk.Generate");
        private static readonly ProfilerMarker coldTemperatureMarker = new("SpaceSurfaceQuery.Temperature.Sample");

        #region 冷地表固定风力缓存
        // 仅保存生成时的不可变天气汇总，不保留被逐出的完整地形。
        internal sealed class ColdWindStrengthCache
        {
            private readonly int capacity;
            private readonly Dictionary<Int2, float> values = new();
            private readonly Queue<Int2> order = new();

            public ColdWindStrengthCache(int capacity) => this.capacity = Math.Max(1, capacity);
            public int Count => values.Count;
            public bool TryGet(Int2 origin, out float strength) => values.TryGetValue(origin, out strength);

            public void Store(Int2 origin, float strength)
            {
                if (values.ContainsKey(origin)) { values[origin] = strength; return; }
                if (order.Count >= capacity) values.Remove(order.Dequeue());
                order.Enqueue(origin);
                values.Add(origin, strength);
            }
        }
        #endregion

        private sealed class SurfaceCache : IDisposable
        {
            public readonly WorldRuntime World;
            public readonly DeterministicChunkGenerator Generator;
            public readonly Queue<RuntimeAddress> Order = new();
            public readonly ColdWindStrengthCache WindStrength = new(128);
            public readonly ulong Fingerprint;
            public readonly int Seed;
            public SurfaceCache(string planetId, ChunkGenerationProfileSnapshot profile, int seed, LiquidTypeCatalog liquids)
            {
                World = new WorldRuntime(planetId, 1);
                Generator = new DeterministicChunkGenerator(liquids, SpaceSurfaceTerrain.Apply);
                Fingerprint = profile.GenerationFingerprint; Seed = seed;
            }
            public void Dispose() { Generator.BeginNewWorldHydrologyEpoch(1); World.Dispose(); }
        }

        // 无人船落地也从同一冻结生成规则与已保存差量读取，查询不会加载场景。
        public static LandingSurfaceSample GetLandingSurface(string planetId, Vector2 position)
            => GetLandingSurface(planetId, position, out _, out _);

        private static LandingSurfaceSample GetLandingSurface(string planetId, Vector2 position,
            out ChunkTerrainData sampledTerrain, out Vector2Int sampledLocalCell)
        {
            sampledTerrain = null; sampledLocalCell = default;
            SaveDataMgr saves = SaveDataMgr.Instance;
            GameSaveData save = saves?.SaveData;
            if (save?.PlanetData_Dict == null || !save.PlanetData_Dict.TryGetValue(planetId, out PlanetData planet))
                throw new KeyNotFoundException($"降落星球尚未创建：{planetId}");
            if (!float.IsFinite(position.x) || !float.IsFinite(position.y)) throw new ArgumentOutOfRangeException(nameof(position));
            if (!ReferenceEquals(owner, save)) { Reset(); owner = save; }
            BindResourceReload();
            ChunkGenerationProfileSnapshot profile = GetFrozenProfile(planet);
            ChunkGenerationTopologySnapshot topology = WorldTopologyBounds.TryCreate(planet, out WorldTopologyBounds bounds)
                ? new ChunkGenerationTopologySnapshot(new Int2(bounds.Min.x, bounds.Min.y), new Int2(bounds.Span.x, bounds.Span.y)) : default;
            int cellX = topology.NormalizeX(Mathf.FloorToInt(position.x)), cellY = topology.NormalizeY(Mathf.FloorToInt(position.y));
            int originX = (int)Math.Floor(cellX / (double)profile.Width) * profile.Width;
            int originY = (int)Math.Floor(cellY / (double)profile.Height) * profile.Height;
            var address = new RuntimeAddress(WorldAddress.SurfaceDimensionId, new Int2(originX, originY));
            int localX = cellX - originX, localY = cellY - originY;
            ChunkTerrainData terrain = GetLoadedTerrain(planetId, address);
            bool loaded = terrain != null;
            if (terrain == null)
            {
                int seed = GetGenerationSeed(save.Seed, planetId);
                if (!caches.TryGetValue(planetId, out SurfaceCache cache) || cache.Fingerprint != profile.GenerationFingerprint || cache.Seed != seed)
                {
                    cache?.Dispose();
                    cache = new SurfaceCache(planetId, profile, seed, GameRes.RequireWorldLiquidTypes());
                    caches[planetId] = cache;
                }
                if (!cache.World.TryGetChunkTerrain(address, out terrain))
                {
                    using var generationSample = coldChunkGenerationMarker.Auto();
                    ChunkGenerationRequest request = cache.World.BeginChunkGeneration(address, seed, profile, topology);
                    using ChunkGenerationResult generated = cache.Generator.Generate(request, CancellationToken.None);
                    if (!cache.World.TryCommit(generated, out string reason)) throw new InvalidDataException($"降落地表生成失败：{reason}");
                    if (!cache.World.TryGetChunkTerrain(address, out terrain)) throw new InvalidOperationException("降落地表未生成");
                    cache.Order.Enqueue(address);
                    while (cache.Order.Count > MaximumCachedChunksPerPlanet) cache.World.EvictChunk(cache.Order.Dequeue());
                }
            }
            if (!loaded && terrain.WeatherProfile != null)
                caches[planetId].WindStrength.Store(address.ChunkOrigin, terrain.WeatherProfile.Climate.WindStrength);
            TerrainCell cell = terrain.GetCell(localX, localY);
            string liquidId = terrain.GetLiquidId(localX, localY);
            int liquidLevel = terrain.GetLiquidLevel(localX, localY);
            int support = TerrainSupportLayer.GetTileId(terrain, localX, localY);
            // 冷缓存保存生成基线，每次读取最新差量，建筑损毁后不会返回旧状态。
            if (!loaded && saves.TryGetChunkDelta(planetId, new Vector2Int(originX, originY), out ChunkSaveRecord delta))
            {
                var local = new Vector2Int(localX, localY);
                if (delta.RuntimeTileDeltas != null)
                foreach (RuntimeTileCellSaveDelta tile in delta.RuntimeTileDeltas)
                    if (tile.LocalPosition == local) cell = new TerrainCell(tile.GroundTileId, tile.BackTileId, tile.BlockingTileId,
                        tile.BiomeId, tile.NavigationCost, tile.Flags);
                if (delta.LiquidCells != null)
                foreach (LiquidCellSaveData liquid in delta.LiquidCells)
                    if (liquid.LocalPosition == local) { liquidId = liquid.LiquidId; liquidLevel = liquid.LiquidLevel; }
                if (delta.SupportCells != null)
                foreach (SupportCellSaveData floor in delta.SupportCells)
                    if (floor.LocalPosition == local) support = floor.TileId;
            }
            bool water = support == 0 && liquidLevel > 0 && GameRes.ExistingInstance != null &&
                GameRes.ExistingInstance.TryGetLiquidDefinition(liquidId, out LiquidDefinition liquidDefinition) &&
                liquidDefinition.WorldWater?.WaterContact == true;
            terrain.TryGetEnvironmentValue("temperature.celsius", localX, localY, out float geographicTemperature);
            sampledTerrain = terrain; sampledLocalCell = new Vector2Int(localX, localY);
            return new LandingSurfaceSample
            {
                IsWater = water, GroundTileId = cell.GroundTileId, BlockingTileId = cell.BlockingTileId, SupportTileId = support,
                LiquidId = liquidId, LiquidLevel = liquidLevel, BiomeId = cell.BiomeId, Cell = new Vector2Int(cellX, cellY),
                TemperatureCelsius = geographicTemperature + planet.GlobalTemperature - PlanetData.DefaultGlobalTemperature,
                PressureKPa = AtmosphereService.PressureKPa(planet.Atmosphere)
            };
        }

        public static bool TryGetLandingSurface(string planetId, Vector2 position, out LandingSurfaceSample sample)
        {
            sample = default;
            GameSaveData save = SaveDataMgr.Instance?.SaveData;
            if (save?.PlanetData_Dict == null || !save.PlanetData_Dict.ContainsKey(planetId ?? string.Empty)) return false;
            sample = GetLandingSurface(planetId, position);
            return true;
        }

        #region 无需生成区块的地理温度查询
        public static bool TryGetAmbientTemperature(string worldKey, Vector2 position, out float temperature)
        {
            temperature = 0f;
            if (string.IsNullOrWhiteSpace(worldKey) || !float.IsFinite(position.x) || !float.IsFinite(position.y)) return false;
            global::WorldAddress world = global::WorldAddress.FromWorldKey(worldKey);
            GameSaveData save = SaveDataMgr.Instance?.SaveData;
            if (!world.IsSurface || save?.PlanetData_Dict == null || !save.PlanetData_Dict.TryGetValue(world.PlanetId, out PlanetData planet)) return false;
            if (!ReferenceEquals(owner, save)) { Reset(); owner = save; }
            BindResourceReload();
            ChunkGenerationProfileSnapshot profile = GetFrozenProfile(planet);
            ChunkGenerationTopologySnapshot topology = WorldTopologyBounds.TryCreate(planet, out WorldTopologyBounds bounds)
                ? new ChunkGenerationTopologySnapshot(new Int2(bounds.Min.x, bounds.Min.y), new Int2(bounds.Span.x, bounds.Span.y)) : default;
            int cellX = topology.NormalizeX(Mathf.FloorToInt(position.x)), cellY = topology.NormalizeY(Mathf.FloorToInt(position.y));
            int originX = (int)Math.Floor(cellX / (double)profile.Width) * profile.Width;
            int originY = (int)Math.Floor(cellY / (double)profile.Height) * profile.Height;
            var address = new RuntimeAddress(WorldAddress.SurfaceDimensionId, new Int2(originX, originY));
            ChunkTerrainData terrain = GetLoadedTerrain(world.PlanetId, address);
            int seed = GetGenerationSeed(save.Seed, world.PlanetId);
            if (terrain == null && caches.TryGetValue(world.PlanetId, out SurfaceCache cache) &&
                cache.Fingerprint == profile.GenerationFingerprint && cache.Seed == seed)
                cache.World.TryGetChunkTerrain(address, out terrain);
            float geographic;
            if (terrain == null || !terrain.TryGetEnvironmentValue("temperature.celsius", cellX - originX, cellY - originY, out geographic))
            {
                var key = (profile, seed, cellX, cellY, topology.IsWrapped, topology.Min, topology.Span);
                if (!temperatureSamples.TryGetValue(key, out geographic))
                {
                    using var temperatureSample = coldTemperatureMarker.Auto();
                    var request = new ChunkGenerationRequest(1, address, seed, 1, profile, topology);
                    geographic = SampleGeneratedTemperature(request, cellX, cellY);
                    if (temperatureSamples.Count >= 512) temperatureSamples.Clear();
                    temperatureSamples.Add(key, geographic);
                }
            }
            temperature = (float)((double)geographic + planet.GlobalTemperature - PlanetData.DefaultGlobalTemperature);
            return true;
        }

        // 只读环境温度不需要生成冷区块，异常世界仍沿用完整地表查询的错误行为。
        public static double GetAmbientTemperature(string worldKey, Vector2 position)
            => TryGetAmbientTemperature(worldKey, position, out float temperature)
                ? temperature : GetLandingSurface(worldKey, position).TemperatureCelsius;

        // 冷查询只复用正式温度公式，地表覆盖层和全局调温仍按各自真实来源生效。
        internal static float SampleGeneratedTemperature(ChunkGenerationRequest request, int x, int y)
            => SpaceSurfaceTerrain.TrySampleTemperature(request, x, y, out float celsius, out _)
                ? celsius : DeterministicChunkGenerator.SampleGeographicTemperature(request, x, y);
        #endregion

        // 无人地表机器从自己的冷地形和最新差量取环境，不借用本地玩家所在世界。
        public static bool TrySampleEnvironmentValue(string worldKey, Vector2 position, string key, out float value)
        {
            value = 0f;
            if (key == "windStrength" && TryGetCachedWindStrength(worldKey, position, out value)) return true;
            if (!TryGetColdSample(worldKey, position, out _, out ChunkTerrainData terrain, out Vector2Int local)) return false;
            if (key == "windStrength")
            {
                if (terrain.WeatherProfile == null) return false;
                value = terrain.WeatherProfile.Climate.WindStrength; return true;
            }
            return terrain.TryGetEnvironmentValue(key, local.x, local.y, out value);
        }

        /// <summary>冷风力机复用固定区块气候值；实时已加载地形始终优先。</summary>
        private static bool TryGetCachedWindStrength(string worldKey, Vector2 position, out float value)
        {
            value = 0f;
            if (string.IsNullOrWhiteSpace(worldKey) || !float.IsFinite(position.x) || !float.IsFinite(position.y))
                return false;
            global::WorldAddress world = global::WorldAddress.FromWorldKey(worldKey);
            GameSaveData save = SaveDataMgr.Instance?.SaveData;
            if (!world.IsSurface || !ReferenceEquals(owner, save) || save?.PlanetData_Dict == null ||
                !save.PlanetData_Dict.TryGetValue(world.PlanetId, out PlanetData planet)) return false;

            ChunkGenerationProfileSnapshot profile = GetFrozenProfile(planet);
            ChunkGenerationTopologySnapshot topology = WorldTopologyBounds.TryCreate(planet, out WorldTopologyBounds bounds)
                ? new ChunkGenerationTopologySnapshot(new Int2(bounds.Min.x, bounds.Min.y), new Int2(bounds.Span.x, bounds.Span.y)) : default;
            int cellX = topology.NormalizeX(Mathf.FloorToInt(position.x));
            int cellY = topology.NormalizeY(Mathf.FloorToInt(position.y));
            int originX = (int)Math.Floor(cellX / (double)profile.Width) * profile.Width;
            int originY = (int)Math.Floor(cellY / (double)profile.Height) * profile.Height;
            var address = new RuntimeAddress(WorldAddress.SurfaceDimensionId, new Int2(originX, originY));
            ChunkTerrainData loaded = GetLoadedTerrain(world.PlanetId, address);
            if (loaded != null)
            {
                if (loaded.WeatherProfile == null) return false;
                value = loaded.WeatherProfile.Climate.WindStrength;
                return true;
            }
            return caches.TryGetValue(world.PlanetId, out SurfaceCache cache) &&
                   cache.Fingerprint == profile.GenerationFingerprint &&
                   cache.Seed == GetGenerationSeed(save.Seed, world.PlanetId) &&
                   cache.WindStrength.TryGet(address.ChunkOrigin, out value);
        }

        public static bool TryGetWaterCurrent(string worldKey, Vector2 position, out RuntimeWaterCurrentSample current)
        {
            current = default;
            if (!TryGetColdSample(worldKey, position, out LandingSurfaceSample sample, out ChunkTerrainData terrain, out Vector2Int local) ||
                sample.LiquidDepth <= 0f || sample.SupportTileId != 0) return false;
            terrain.TryGetEnvironmentValue("riverKind", local.x, local.y, out float riverKind);
            RuntimeWaterCurrentKind kind = WaterEnvironmentRules.ResolveCurrentKind(Mathf.RoundToInt(riverKind),
                (SurfaceBiomeKind)sample.BiomeId == SurfaceBiomeKind.Ocean);
            if (kind == RuntimeWaterCurrentKind.River)
            {
                terrain.TryGetEnvironmentValue("riverFlowX", local.x, local.y, out float x);
                terrain.TryGetEnvironmentValue("riverFlowY", local.x, local.y, out float y);
                terrain.TryGetEnvironmentValue("riverFlow", local.x, local.y, out float flow);
                Vector2 direction = new(x, y);
                if (direction.sqrMagnitude <= .000001f) return false;
                current = new RuntimeWaterCurrentSample(kind, direction, flow); return true;
            }
            if (kind != RuntimeWaterCurrentKind.Ocean) return false;
            Vector2 ocean = WaterEnvironmentRules.ResolveOceanCurrentDirection(terrain, local.x, local.y);
            if (ocean == Vector2.zero) return false;
            current = new RuntimeWaterCurrentSample(kind, ocean, 1f); return true;
        }

        private static bool TryGetColdSample(string worldKey, Vector2 position, out LandingSurfaceSample sample,
            out ChunkTerrainData terrain, out Vector2Int local)
        {
            sample = default; terrain = null; local = default;
            if (string.IsNullOrWhiteSpace(worldKey) || !float.IsFinite(position.x) || !float.IsFinite(position.y)) return false;
            global::WorldAddress address = global::WorldAddress.FromWorldKey(worldKey);
            if (!address.IsSurface || SaveDataMgr.Instance?.SaveData?.PlanetData_Dict?.ContainsKey(address.PlanetId) != true) return false;
            sample = GetLandingSurface(address.PlanetId, position, out terrain, out local); return true;
        }

        private static ChunkTerrainData GetLoadedTerrain(string planetId, RuntimeAddress address)
        {
            DimensionManager dimension = DimensionManager.ExistingInstance;
            ChunkMgr manager = ChunkMgr.ExistingInstance;
            if (dimension == null || !dimension.ActiveAddress.IsSurface || dimension.ActiveAddress.PlanetId != planetId ||
                SpaceSession.Current?.IsSpaceView == true || manager?.WorldRuntime == null) return null;
            return manager.WorldRuntime.TryGetChunkTerrain(address, out ChunkTerrainData terrain) ? terrain : null;
        }

        private static ChunkGenerationProfileSnapshot GetFrozenProfile(PlanetData planet)
        {
            // 冻结生成配置只随世界和资源会话变化，不在每次地表采样时重新构造。
            if (!frozenProfiles.TryGetValue(planet, out ChunkGenerationProfileSnapshot profile))
            {
                profile = ChunkMgr.CreateFrozenSurfaceProfile(planet);
                frozenProfiles.Add(planet, profile);
            }
            return profile;
        }

        // 只采样冻结地理规则，不为放置预览生成区块或加载场景。
        public static bool IsInsideEquatorBand(string worldKey, Vector2 position, out double normalizedX)
        {
            normalizedX = 0d;
            GameSaveData save = SaveDataMgr.Instance?.SaveData;
            if (string.IsNullOrWhiteSpace(worldKey) || !global::WorldAddress.FromWorldKey(worldKey).IsSurface ||
                save?.PlanetData_Dict == null || !save.PlanetData_Dict.TryGetValue(worldKey, out PlanetData planet) ||
                !float.IsFinite(position.x) || !float.IsFinite(position.y) ||
                !WorldTopologyBounds.TryCreate(planet, out WorldTopologyBounds bounds)) return false;
            if (!ReferenceEquals(owner, save)) { Reset(); owner = save; }
            BindResourceReload();
            ChunkGenerationProfileSnapshot profile = GetFrozenProfile(planet);
            var topology = new ChunkGenerationTopologySnapshot(new Int2(bounds.Min.x, bounds.Min.y),
                new Int2(bounds.Span.x, bounds.Span.y));
            int x = topology.NormalizeX(Mathf.FloorToInt(position.x));
            int y = topology.NormalizeY(Mathf.FloorToInt(position.y));
            normalizedX = (bounds.NormalizePosition(position).x - (double)bounds.Min.x) / bounds.Span.x;
            var address = new RuntimeAddress(global::WorldAddress.SurfaceDimensionId, default);
            var request = new ChunkGenerationRequest(1, address, GetGenerationSeed(save.Seed, worldKey), 1, profile, topology);
            return DeterministicChunkGenerator.IsInsideEquatorBand(request, x, y);
        }

        private static void BindResourceReload()
        {
            GameRes resources = GameRes.ExistingInstance;
            if (ReferenceEquals(resourceOwner, resources)) return;
            if (resourceOwner != null) resourceOwner.ResourcesReloaded -= Reset;
            resourceOwner = resources;
            if (resourceOwner != null) resourceOwner.ResourcesReloaded += Reset;
        }

        private static int GetGenerationSeed(int baseSeed, string planetId)
        {
            // 同一资源会话复用目录引用，仍即时读取目录里的种子盐。
            if (generationCatalog == null) generationCatalog = Resources.Load<DimensionCatalogSO>("Config/DimensionCatalog_Default");
            int salt = generationCatalog?.Find(WorldAddress.SurfaceDimensionId)?.SeedSalt ?? 0;
            unchecked
            {
                uint hash = (uint)(baseSeed == 0 ? 1 : baseSeed);
                foreach (char c in planetId) hash = (hash ^ c) * 16777619u;
                hash = (hash ^ (uint)salt) * 16777619u;
                int result = (int)hash; return result == 0 ? 1 : result;
            }
        }

        public static void Reset()
        {
            foreach (SurfaceCache cache in caches.Values) cache.Dispose();
            caches.Clear(); frozenProfiles.Clear(); temperatureSamples.Clear(); owner = null; generationCatalog = null;
            if (resourceOwner != null) resourceOwner.ResourcesReloaded -= Reset;
            resourceOwner = null;
        }
        #endregion
    }
}
