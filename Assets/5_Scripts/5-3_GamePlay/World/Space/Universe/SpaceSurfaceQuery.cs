using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
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
        public float LiquidDepth;
        public double TemperatureCelsius;
        public double PressureKPa;
        public Vector2Int Cell;
        #endregion
    }

    public static class SpaceSurfaceQuery
    {
        #region 地表结构冲击入口
        public static SpaceSurfaceImpactResult ApplyStructureDamageInRadius(string worldKey, Vector2 center, float radius, float peak,
            bool pressureExplosion = false, ulong eventId = 0)
            => SaveDataMgr.Instance.ApplySpaceSurfaceImpact(global::WorldAddress.FromWorldKey(worldKey), center, radius, peak,
                pressureExplosion, eventId);

        // 冷地表也按真实格容量接纳液体，调用者只扣除返回的实际泄出量。
        public static float ReleaseLiquidInWorld(string worldKey, Vector2 center, string liquidId, float depth)
        {
            if (!GameNetwork.HasStateAuthority || !float.IsFinite(depth) || depth <= 0f || string.IsNullOrWhiteSpace(worldKey) ||
                worldKey.StartsWith("ship:", StringComparison.Ordinal) ||
                !global::WorldAddress.FromWorldKey(worldKey).IsSurface || GameRes.ExistingInstance == null ||
                !GameRes.ExistingInstance.TryGetLiquidDefinition(liquidId, out LiquidDefinition definition) || definition.WorldWater == null)
                return 0f;
            LandingSurfaceSample sample = GetLandingSurface(worldKey, center);
            if (sample.BlockingTileId != 0 || sample.SupportTileId != 0 ||
                (sample.LiquidDepth > 0f && !string.Equals(sample.LiquidId, liquidId, StringComparison.OrdinalIgnoreCase))) return 0f;
            float next = Mathf.Min(1f, sample.LiquidDepth + depth);
            float accepted = next - sample.LiquidDepth;
            if (accepted <= 0f) return 0f;
            PlanetData planet = SaveDataMgr.Instance.SaveData.PlanetData_Dict[worldKey];
            Vector2Int size = PlanetData.NormalizeChunkSize(planet.ChunkSize);
            int originX = (int)Math.Floor(sample.Cell.x / (double)size.x) * size.x;
            int originY = (int)Math.Floor(sample.Cell.y / (double)size.y) * size.y;
            var address = new RuntimeAddress(global::WorldAddress.SurfaceDimensionId, new Int2(originX, originY));
            ChunkTerrainData loaded = GetLoadedTerrain(worldKey, address);
            if (loaded != null)
                return WorldLiquidSystem.TrySet(new Vector2(sample.Cell.x + .5f, sample.Cell.y + .5f), liquidId, next) ? accepted : 0f;
            if (!caches.TryGetValue(worldKey, out SurfaceCache cache) || !cache.World.TryGetChunkTerrain(address, out ChunkTerrainData baseline))
                throw new InvalidOperationException("冷液体写回前缺少地表生成基线");
            int x = sample.Cell.x - originX, y = sample.Cell.y - originY;
            SaveDataMgr.Instance.RecordSpaceLiquidChange(worldKey, sample.Cell, liquidId, next,
                baseline.GetLiquidId(x, y), baseline.GetLiquidDepth(x, y));
            return accepted;
        }

        public static bool TrySampleLiquid(string worldKey, Vector2 point, out LiquidDefinition liquid, out float depth)
        {
            liquid = null; depth = 0f;
            if (string.IsNullOrWhiteSpace(worldKey) || worldKey.StartsWith("ship:", StringComparison.Ordinal) ||
                !global::WorldAddress.FromWorldKey(worldKey).IsSurface || !float.IsFinite(point.x) || !float.IsFinite(point.y) ||
                GameRes.ExistingInstance == null || !TryGetLandingSurface(worldKey, point, out LandingSurfaceSample sample) ||
                sample.LiquidDepth <= 0f || !GameRes.ExistingInstance.TryGetLiquidDefinition(sample.LiquidId, out liquid) || liquid.WorldWater == null)
            { liquid = null; return false; }
            depth = sample.LiquidDepth;
            return true;
        }

        // 抽取冷世界真实有限库存，平台只遮断浸水而不复制或删除底部液体。
        public static bool TryPumpLiquidInWorld(string worldKey, Vector2 point, float requestedDepth, out string liquidId, out float removedDepth)
        {
            liquidId = null; removedDepth = 0f;
            if (!GameNetwork.HasStateAuthority || !float.IsFinite(requestedDepth) || requestedDepth <= 0f ||
                !TrySampleLiquid(worldKey, point, out LiquidDefinition liquid, out _)) return false;
            LandingSurfaceSample sample = GetLandingSurface(worldKey, point);
            PlanetData planet = SaveDataMgr.Instance.SaveData.PlanetData_Dict[worldKey];
            Vector2Int size = PlanetData.NormalizeChunkSize(planet.ChunkSize);
            int originX = (int)Math.Floor(sample.Cell.x / (double)size.x) * size.x;
            int originY = (int)Math.Floor(sample.Cell.y / (double)size.y) * size.y;
            var address = new RuntimeAddress(global::WorldAddress.SurfaceDimensionId, new Int2(originX, originY));
            if (GetLoadedTerrain(worldKey, address) != null)
                return WorldLiquidSystem.TryPump(new Vector2(sample.Cell.x + .5f, sample.Cell.y + .5f), requestedDepth, out liquidId, out removedDepth);
            if (!caches.TryGetValue(worldKey, out SurfaceCache cache) || !cache.World.TryGetChunkTerrain(address, out ChunkTerrainData baseline))
                throw new InvalidOperationException("冷液体抽取前缺少地表生成基线");
            float next = LiquidCellValue.NormalizeDepth(sample.LiquidDepth - requestedDepth);
            removedDepth = sample.LiquidDepth - next;
            if (removedDepth <= 0f) return false;
            liquidId = sample.LiquidId;
            SaveDataMgr.Instance.RecordSpaceLiquidChange(worldKey, sample.Cell, next > 0f ? liquidId : null, next,
                baseline.GetLiquidId(sample.Cell.x - originX, sample.Cell.y - originY),
                baseline.GetLiquidDepth(sample.Cell.x - originX, sample.Cell.y - originY));
            return true;
        }
        #endregion

        #region 不依赖活动场景的地表查询
        private const int MaximumCachedChunksPerPlanet = 32;
        private static GameSaveData owner;
        private static readonly Dictionary<string, SurfaceCache> caches = new(StringComparer.Ordinal);

        private sealed class SurfaceCache : IDisposable
        {
            public readonly WorldRuntime World;
            public readonly DeterministicChunkGenerator Generator;
            public readonly Queue<RuntimeAddress> Order = new();
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
        {
            SaveDataMgr saves = SaveDataMgr.Instance;
            GameSaveData save = saves?.SaveData;
            if (save?.PlanetData_Dict == null || !save.PlanetData_Dict.TryGetValue(planetId, out PlanetData planet))
                throw new KeyNotFoundException($"降落星球尚未创建：{planetId}");
            if (!float.IsFinite(position.x) || !float.IsFinite(position.y)) throw new ArgumentOutOfRangeException(nameof(position));
            if (!ReferenceEquals(owner, save)) { Reset(); owner = save; }
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
                    cache = new SurfaceCache(planetId, profile, seed, GameRes.ExistingInstance?.LiquidTypes);
                    caches[planetId] = cache;
                }
                if (!cache.World.TryGetChunkTerrain(address, out terrain))
                {
                    ChunkGenerationRequest request = cache.World.BeginChunkGeneration(address, seed, profile, topology);
                    using ChunkGenerationResult generated = cache.Generator.Generate(request, CancellationToken.None);
                    if (!cache.World.TryCommit(generated, out string reason)) throw new InvalidDataException($"降落地表生成失败：{reason}");
                    if (!cache.World.TryGetChunkTerrain(address, out terrain)) throw new InvalidOperationException("降落地表未生成");
                    cache.Order.Enqueue(address);
                    while (cache.Order.Count > MaximumCachedChunksPerPlanet) cache.World.EvictChunk(cache.Order.Dequeue());
                }
            }
            TerrainCell cell = terrain.GetCell(localX, localY);
            string liquidId = terrain.GetLiquidId(localX, localY);
            float liquidDepth = terrain.GetLiquidDepth(localX, localY);
            int support = TerrainSupportLayer.GetTileId(terrain, localX, localY);
            // 冷缓存保存生成基线，每次读取最新差量，建筑损毁后不会返回旧状态。
            if (!loaded && saves.TryGetChunkDelta(planetId, new Vector2Int(originX, originY).ToString(), out ChunkSaveRecord delta))
            {
                var local = new Vector2Int(localX, localY);
                foreach (RuntimeTileCellSaveDelta tile in delta.RuntimeTileDeltas ?? new List<RuntimeTileCellSaveDelta>())
                    if (tile.LocalPosition == local) cell = new TerrainCell(tile.GroundTileId, tile.BackTileId, tile.BlockingTileId,
                        tile.BiomeId, tile.NavigationCost, tile.Flags);
                foreach (LiquidCellSaveData liquid in delta.LiquidCells ?? new List<LiquidCellSaveData>())
                    if (liquid.LocalPosition == local) { liquidId = liquid.LiquidId; liquidDepth = liquid.LiquidDepth; }
                foreach (SupportCellSaveData floor in delta.SupportCells ?? new List<SupportCellSaveData>())
                    if (floor.LocalPosition == local) support = floor.TileId;
            }
            bool water = support == 0 && liquidDepth > 0f && GameRes.ExistingInstance != null &&
                GameRes.ExistingInstance.TryGetLiquidDefinition(liquidId, out LiquidDefinition liquidDefinition) &&
                liquidDefinition.WorldWater?.WaterContact == true;
            terrain.TryGetEnvironmentValue("temperature.celsius", localX, localY, out float geographicTemperature);
            return new LandingSurfaceSample
            {
                IsWater = water, GroundTileId = cell.GroundTileId, BlockingTileId = cell.BlockingTileId, SupportTileId = support,
                LiquidId = liquidId, LiquidDepth = liquidDepth, Cell = new Vector2Int(cellX, cellY),
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

        private static ChunkTerrainData GetLoadedTerrain(string planetId, RuntimeAddress address)
        {
            DimensionManager dimension = DimensionManager.ExistingInstance;
            ChunkMgr manager = ChunkMgr.ExistingInstance;
            if (dimension == null || !dimension.ActiveAddress.IsSurface || dimension.ActiveAddress.PlanetId != planetId ||
                SpaceSession.Current?.IsSpaceView == true || manager?.WorldRuntime == null) return null;
            return manager.WorldRuntime.TryGetChunkTerrain(address, out ChunkTerrainData terrain) ? terrain : null;
        }

        private static ChunkGenerationProfileSnapshot GetFrozenProfile(PlanetData planet)
            => ChunkMgr.CreateFrozenSurfaceProfile(planet);

        private static int GetGenerationSeed(int baseSeed, string planetId)
        {
            DimensionCatalogSO catalog = Resources.Load<DimensionCatalogSO>("Config/DimensionCatalog_Default");
            int salt = catalog?.Find(WorldAddress.SurfaceDimensionId)?.SeedSalt ?? 0;
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
            caches.Clear(); owner = null;
        }
        #endregion
    }
}
