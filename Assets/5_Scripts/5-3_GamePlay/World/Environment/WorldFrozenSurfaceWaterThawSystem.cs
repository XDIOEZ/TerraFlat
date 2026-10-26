using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using UnityEngine;

/// <summary>冻结水格复用天气地表队列，融完才以正式液体和地形接口恢复水域。</summary>
public static class WorldFrozenSurfaceWaterThawSystem
{
    #region 冻水候选索引
    private static readonly ConditionalWeakTable<ChunkTerrainData, FrozenCellIndex> indices = new();
    private sealed class FrozenCellIndex
    {
        private readonly ChunkTerrainData terrain;
        internal readonly List<int> Cells = new();
        internal FrozenCellIndex(ChunkTerrainData terrain)
        {
            this.terrain = terrain;
            if (terrain.TryGetEnvironmentLayer(FrozenSurfaceWaterLayer.IceTileLayer, out _))
                for (int index = 0; index < terrain.CellCount; index++) Refresh(index);
            terrain.Changed += change =>
            {
                if (change.Kind != TerrainChangeKind.Cell && change.Kind != TerrainChangeKind.TileStack &&
                    !(change.Kind == TerrainChangeKind.Environment &&
                      (change.AffectsEnvironmentLayer(FrozenSurfaceWaterLayer.IceTileLayer) ||
                       change.AffectsEnvironmentLayer(TerrainSupportLayer.TileIdLayer) ||
                       change.AffectsEnvironmentLayer(GroundTileLayerSystem.DepthLayerId)))) return;
                foreach (int index in change.GetCellIndices(terrain.Width)) Refresh(index);
            };
        }
        private void Refresh(int index)
        {
            bool candidate = TryGetThawableSource(terrain, index % terrain.Width, index / terrain.Width, out _);
            int slot = Cells.BinarySearch(index);
            if (candidate && slot < 0) Cells.Insert(~slot, index);
            else if (!candidate && slot >= 0) Cells.RemoveAt(slot);
        }
    }

    internal static IReadOnlyList<int> GetFrozenCellIndices(ChunkTerrainData terrain)
        => indices.GetValue(terrain, value => new FrozenCellIndex(value)).Cells;

    internal static bool TryGetThawableSource(ChunkTerrainData terrain, int x, int y,
        out FrozenSurfaceWaterSource source)
    {
        if (!FrozenSurfaceWaterLayer.TryReadSource(terrain, x, y, out source)) return false;
        TerrainCell cell = terrain.GetCell(x, y);
        return cell.GroundTileId == source.IceTileId && cell.BackTileId == source.Bed.BackTileId &&
            cell.BlockingTileId == source.Bed.BlockingTileId && cell.Flags == source.Bed.Flags &&
            cell.NavigationCost == Math.Min(short.MaxValue, source.Bed.NavigationCost + 1) &&
            TerrainSupportLayer.GetTileId(terrain, x, y) == 0 &&
            GroundTileLayerSystem.ReadDepth(terrain, new Vector2Int(x, y)) == 0 &&
            !GroundTileLayerSystem.TryGetUnderlying(terrain, new Vector2Int(x, y), out _);
    }
    #endregion

    #region 权威解冻事务
    internal static bool TryCompleteThaw(ChunkRuntime chunk, int index)
    {
        if (!GameNetwork.HasStateAuthority || SaveDataMgr.Instance?.SaveData == null || chunk?.Terrain == null)
            return false;
        ChunkTerrainData terrain = chunk.Terrain;
        int x = index % terrain.Width, y = index / terrain.Width;
        if (!TryGetThawableSource(terrain, x, y, out FrozenSurfaceWaterSource source) ||
            terrain.GetLiquidLevel(x, y) > 0 || GameRes.ExistingInstance == null ||
            !GameRes.ExistingInstance.TryGetTileDefinition(source.Bed.GroundTileId, out RuntimeTileDefinition bed))
            return false;
        string liquidId = terrain.LiquidTypes.GetId(source.LiquidTypeIndex);
        if (!GameRes.ExistingInstance.TryGetLiquidDefinition(liquidId, out LiquidDefinition liquid) ||
            liquid.WorldWater == null) return false;
        var local = new Vector2Int(x, y);
        var worldCell = new Vector2Int(chunk.Address.ChunkOrigin.X + x, chunk.Address.ChunkOrigin.Y + y);
        if (BuildingOccupancyRegistry.IsOccupied(worldCell)) return false;
        TerrainCell previous = terrain.GetCell(x, y);
        terrain.TryGetEnvironmentValue(FrozenSurfaceWaterLayer.MeltProgressLayer, x, y, out float progress);
        terrain.TryGetEnvironmentValue(TileBuildingSystem.RuntimeDamageLayerId, x, y, out float damage);
        try
        {
            // 先公布已融身份，液体变化监听器当场就能识别恢复后的天然水域。
            terrain.SetEnvironmentValue(FrozenSurfaceWaterLayer.MeltProgressLayer, x, y, 1f);
            terrain.SetCell(x, y, source.Bed);
            var sample = new RuntimeTerrainTileSample(chunk.Address, terrain, worldCell, local,
                source.Bed, terrain.GetTopTileId(x, y));
            if (!WorldLiquidSystem.TrySet(sample, liquidId, source.LiquidLevel))
                throw new InvalidOperationException("解冻水量未能提交。");
            terrain.SetEnvironmentValue(TileBuildingSystem.RuntimeDamageLayerId, x, y, 0f);
            SaveDataMgr.Instance.RecordRuntimeTerrainChange(new TileBuildingCell(chunk, worldCell, local,
                source.Bed.GroundTileId, bed.Id));
            return true;
        }
        catch
        {
            terrain.SetEnvironmentValue(FrozenSurfaceWaterLayer.MeltProgressLayer, x, y, progress);
            terrain.SetCell(x, y, previous);
            terrain.SetEnvironmentValue(TileBuildingSystem.RuntimeDamageLayerId, x, y, damage);
            var sample = new RuntimeTerrainTileSample(chunk.Address, terrain, worldCell, local,
                previous, terrain.GetTopTileId(x, y));
            if (terrain.GetLiquidLevel(x, y) > 0) WorldLiquidSystem.TrySet(sample, null, 0);
            SaveDataMgr.Instance.RecordRuntimeTerrainChange(new TileBuildingCell(chunk, worldCell, local,
                previous.GroundTileId, FarmlandSystem.GetBlockId(previous.GroundTileId)));
            throw;
        }
    }
    #endregion
}
