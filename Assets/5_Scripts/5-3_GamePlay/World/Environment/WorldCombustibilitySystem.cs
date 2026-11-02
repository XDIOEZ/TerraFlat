using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using UnityEngine;

internal readonly struct WorldCombustibilityCellSample
{
    #region 已加载可燃格地址
    internal readonly ChunkTerrainData Terrain;
    internal readonly Vector2Int WorldCell, LocalCell;
    internal WorldCombustibilityCellSample(ChunkTerrainData terrain, Vector2Int worldCell, Vector2Int localCell)
    { Terrain = terrain; WorldCell = worldCell; LocalCell = localCell; }
    #endregion
}

/// <summary>可燃物主动发布占地，读取和火场计算不搜索实体。</summary>
public static class WorldCombustibilitySystem
{
    #region 世界可燃性读写
    private static readonly ConditionalWeakTable<WorldRuntime, WorldField> fields = new();

    private static WorldField GetField(string worldKey)
    {
        if (!GameplayWorldRegistry.TryGetOwnedWorldModel(worldKey, out var world)) return null;
        if (fields.TryGetValue(world, out var field)) return field;
        using var scope = GameplayWorldRegistry.TryGet(worldKey, out _) ? GameplayWorldRegistry.UseWorld(worldKey) : default;
        field = new WorldField(world); fields.Add(world, field); return field;
    }

    public static bool TryGetCombustibility(string worldKey, Vector2 logicalPosition, out WorldCombustibilityValue value)
    {
        value = default;
        if (GetField(worldKey)?.TryGetSample(Vector2Int.FloorToInt(logicalPosition), out var sample) != true) return false;
        value = sample.Terrain.GetCombustibility(sample.LocalCell.x, sample.LocalCell.y);
        return true;
    }

    public static bool SetCombustibility(string worldKey, Vector2 logicalPosition, float ignitionProbability, float fuelSeconds)
    {
        if (!GameNetwork.HasStateAuthority || !float.IsFinite(ignitionProbability) || !float.IsFinite(fuelSeconds) ||
            GetField(worldKey)?.TryGetSample(Vector2Int.FloorToInt(logicalPosition), out var sample) != true) return false;
        sample.Terrain.SetCombustibility(sample.LocalCell.x, sample.LocalCell.y, new(ignitionProbability, fuelSeconds));
        return true;
    }

    public static void ReplaceCombustibilitySource(string worldKey, object owner,
        IReadOnlyCollection<Vector2Int> occupiedCells, float ignitionProbability, float fuelSeconds)
    {
        if (!GameNetwork.HasStateAuthority) return;
        WorldField field = GetField(worldKey);
        if (field == null) return;
        var cells = new HashSet<Int2>();
        foreach (Vector2Int cell in occupiedCells)
            cells.Add(new Int2(field.Topology.NormalizeX(cell.x), field.Topology.NormalizeY(cell.y)));
        field.Sources.ReplaceSource(owner, cells, new(Mathf.Clamp01(ignitionProbability), Mathf.Max(0f, fuelSeconds)));
    }

    public static void RemoveCombustibilitySource(string worldKey, object owner, bool preserveFuelConsumption = false)
    {
        if (GameplayWorldRegistry.TryGetOwnedWorldModel(worldKey, out var world) && fields.TryGetValue(world, out var field))
            field.RemoveSource(owner, preserveFuelConsumption);
    }

    internal static bool TryGetPreparedSample(WorldRuntime world, Vector2Int cell, out WorldCombustibilityCellSample sample)
    {
        if (!fields.TryGetValue(world, out var field)) field = GetField(world.WorldId);
        sample = default;
        return field != null && field.TryGetSample(cell, out sample);
    }

    internal static void RefreshPreparedCacheRevision(WorldRuntime world)
    {
        if (!fields.TryGetValue(world, out var field)) field = GetField(world.WorldId);
        field?.RefreshDefinitionCacheRevision();
    }
    #endregion

    #region 已加载地形与基础可燃性缓存
    private sealed class WorldField
    {
        internal readonly WorldTopologyDomain Topology;
        internal readonly WorldCombustibilitySourceField Sources = new();
        private readonly Dictionary<Vector2Int, ChunkRuntime> chunks = new();
        private readonly Dictionary<Vector2Int, (ChunkTerrainData Terrain, Action<ChunkTerrainChanged> Handler)> watches = new();
        private int width, height;
        private GameRes definitionOwner;
        private ulong definitionRevision;
        private bool preservingFuelConsumption;

        internal WorldField(WorldRuntime world)
        {
            Topology = WorldTopologyRuntime.GetActiveDomain();
            foreach (ChunkRuntime chunk in world.Chunks.Values) Observe(chunk);
            world.Events.Subscribe<ChunkCommitted>(change => { if (world.TryGetChunk(change.Address, out var chunk)) Observe(chunk); });
            world.Events.Subscribe<ChunkDataStatusChanged>(change =>
            {
                var origin = new Vector2Int(change.Address.ChunkOrigin.X, change.Address.ChunkOrigin.Y);
                if (change.Current == ChunkDataStatus.Evicting || change.Current == ChunkDataStatus.Absent) Remove(origin);
                else if (world.TryGetChunk(change.Address, out var chunk)) Observe(chunk);
            });
            Sources.Changed += (cell, value) =>
            {
                if (TryGetSample(new Vector2Int(cell.X, cell.Y), out var sample))
                    sample.Terrain.SetCombustibilitySourceContribution(sample.LocalCell.y * width + sample.LocalCell.x, value, preservingFuelConsumption);
            };
        }

        internal void RemoveSource(object owner, bool preserveFuelConsumption)
        {
            bool previous = preservingFuelConsumption;
            preservingFuelConsumption = preserveFuelConsumption;
            try { Sources.RemoveSource(owner); }
            finally { preservingFuelConsumption = previous; }
        }

        private void Observe(ChunkRuntime chunk)
        {
            if (chunk.DataStatus != ChunkDataStatus.Ready || chunk.Terrain == null || chunk.Terrain.IsDisposed) return;
            Vector2Int origin = new(chunk.Address.ChunkOrigin.X, chunk.Address.ChunkOrigin.Y);
            if (watches.TryGetValue(origin, out var old) && ReferenceEquals(old.Terrain, chunk.Terrain)) return;
            Remove(origin);
            chunks[origin] = chunk; width = chunk.Terrain.Width; height = chunk.Terrain.Height;
            ChunkTerrainData terrain = chunk.Terrain;
            Action<ChunkTerrainChanged> changed = change =>
            {
                if (change.Kind == TerrainChangeKind.Fire || change.Kind == TerrainChangeKind.Combustibility ||
                    change.Kind == TerrainChangeKind.Occupancy) return;
                if (change.Kind == TerrainChangeKind.Environment && !change.AffectsEnvironmentLayer(TerrainSupportLayer.TileIdLayer) &&
                    !change.AffectsEnvironmentLayer(TerrainSurfaceHeightLayer.CoverLevelsLayerId) &&
                    !change.AffectsEnvironmentLayer(TerrainSupportLayer.ExternalOwnershipLayer)) return;
                foreach (int index in change.GetCellIndices(terrain.Width)) terrain.InvalidateCombustibilityBase(index);
            };
            watches[origin] = (terrain, changed); terrain.Changed += changed;
        }

        private void Remove(Vector2Int origin)
        {
            chunks.Remove(origin);
            if (watches.Remove(origin, out var watch)) watch.Terrain.Changed -= watch.Handler;
        }

        internal bool TryGetSample(Vector2Int cell, out WorldCombustibilityCellSample sample)
        {
            sample = default;
            RefreshDefinitionCacheRevision();
            if (width == 0) return false;
            cell = new(Topology.NormalizeX(cell.x), Topology.NormalizeY(cell.y));
            Vector2Int origin = new(Mathf.FloorToInt((float)cell.x / width) * width, Mathf.FloorToInt((float)cell.y / height) * height);
            if (!chunks.TryGetValue(origin, out var chunk) || chunk.DataStatus != ChunkDataStatus.Ready ||
                chunk.Terrain == null || chunk.Terrain.IsDisposed) return false;
            ChunkTerrainData terrain = chunk.Terrain;
            Vector2Int local = cell - origin;
            sample = new WorldCombustibilityCellSample(terrain, cell, local);
            int index = local.y * width + local.x;
            if (terrain.HasCachedCombustibilityBase(index)) return true;
            TerrainCell surface = TerrainSupportLayer.GetSurfaceCell(terrain, local.x, local.y);
            int support = TerrainSupportLayer.GetExposedTileId(terrain, local.x, local.y);
            var terrainSample = new RuntimeTerrainTileSample(chunk.Address, terrain, cell, local, surface,
                support != 0 && surface.BlockingTileId == 0 && surface.BackTileId == 0 ? support : terrain.GetTopTileId(local.x, local.y));
            float probability = 0f, fuel = 0f;
            bool grass = WorldFireSystem.HasExposedGrass(terrainSample);
            if (grass) { probability = .2f; fuel = 10f; }
            int tileId = surface.BlockingTileId != 0 ? surface.BlockingTileId : terrainSample.TopTileId;
            bool sourceOwnsSupport = support != 0 && surface.BlockingTileId == 0 &&
                terrain.TryGetEnvironmentValue(TerrainSupportLayer.ExternalOwnershipLayer, local.x, local.y, out float externallyOwned) && externallyOwned > 0f;
            if (!sourceOwnsSupport && WorldFireSystem.TryGetTileFuel(tileId, out var definition))
            { probability = Mathf.Max(probability, definition.FireFuel.Flammability); fuel += definition.FireFuel.BurnDurationSeconds; }
            if (terrainSample.LiquidDepth > 0f && GameRes.ExistingInstance != null &&
                GameRes.ExistingInstance.TryGetLiquidDefinition(terrainSample.LiquidId, out var liquid))
            {
                probability = Mathf.Max(probability, liquid.FireFuel.Flammability);
                fuel += liquid.FireFuel.BurnDurationSeconds * terrainSample.LiquidDepth;
            }
            int identity = unchecked((tileId * 397 ^ (grass ? 1 : 0)) * 397 ^ terrain.GetLiquidTypeIndex(local.x, local.y));
            terrain.CacheCombustibilityBase(index, new(probability, fuel), identity);
            terrain.SetCombustibilitySourceContribution(index, Sources.Get(new Int2(cell.x, cell.y)), preservingFuelConsumption);
            return true;
        }

        internal void RefreshDefinitionCacheRevision()
        {
            GameRes resources = GameRes.ExistingInstance;
            ulong revision = resources?.ItemDefinitionRevision ?? 0;
            if (!ReferenceEquals(definitionOwner, resources) || definitionRevision != revision)
            {
                definitionOwner = resources; definitionRevision = revision;
                foreach (var loaded in chunks.Values) loaded.Terrain.InvalidateAllCombustibilityBaseCaches();
            }
        }
    }
    #endregion
}
