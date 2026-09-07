using System;
using FlatWorld.WorldModel;
using UnityEngine;

/// <summary>
/// 普通地面的铺设事务：验证来源地形，替换权威地表并记录差量。
/// 不占用 Blocking 层或动态建筑格，后续建筑直接沿用普通地面规则。
/// </summary>
public static partial class TileBuildingSystem
{
    #region 地表铺设规则

    /// <summary>该 Tile 是否独立负责来源地形校验，允许覆盖原有水域高通行代价。</summary>
    public static bool IsGroundPlacement(string tileBlockId)
    {
        return !string.IsNullOrWhiteSpace(tileBlockId) &&
               GameRes.Instance?.GetTileBlock(tileBlockId)?.groundPlacement != null;
    }

    /// <summary>预览与提交共用规则；只接受已加载且没有叠层或建筑占用的来源格。</summary>
    private static bool TryResolveGroundPlacement(Vector2Int worldCell, Tile_Block definition,
        out RuntimeTerrainTileSample sample, out int tileId, out string reason)
    {
        sample = default;
        tileId = 0;
        reason = null;
        ChunkMgr manager = ChunkMgr.ExistingInstance;
        if (manager == null || !manager.TryGetRuntimeTerrainTile(
                new Vector2(worldCell.x + 0.5f, worldCell.y + 0.5f), out sample))
        {
            reason = "目标地形尚未加载";
            return false;
        }

        TerrainCell cell = sample.Cell;
        TerrainCellFlags required = definition.groundPlacement.RequiredSourceFlags;
        if ((cell.Flags & required) != required)
        {
            reason = required == TerrainCellFlags.Water ? "水上平台只能铺在水里" : "目标地形不满足铺设条件";
            return false;
        }
        if (cell.GroundTileId == 0 || cell.BackTileId != 0 || cell.BlockingTileId != 0 ||
            (cell.Flags & (TerrainCellFlags.Blocking | TerrainCellFlags.Occupied)) != 0 ||
            sample.Terrain.GetTileLayerCount(sample.LocalCell.x, sample.LocalCell.y) != 1 ||
            BuildingOccupancyRegistry.IsOccupied(sample.WorldCell))
        {
            reason = "目标格已有建筑或其他地形覆盖";
            return false;
        }
        if (!definition.tileDataTemplate.IsWalkable ||
            BlockingTilemapLayer.IsBlockingTile(definition.tileDataTemplate))
        {
            reason = "铺设目标必须是可行走的普通地面";
            return false;
        }
        if (!TryResolveRuntimeTileId(manager, definition.tileItemName, out tileId))
        {
            reason = $"当前世界未登记铺设地块：{definition.tileItemName}";
            return false;
        }
        return true;
    }

    /// <summary>替换单格地表；Changed 事件驱动水面、邻岸、地块效果和导航更新。</summary>
    private static bool TryPlaceGround(Vector2Int worldCell, Tile_Block definition,
        out TileBuildingCell placedCell, out string reason)
    {
        placedCell = default;
        if (!TryResolveGroundPlacement(worldCell, definition, out RuntimeTerrainTileSample sample,
                out int tileId, out reason))
            return false;

        ChunkMgr manager = ChunkMgr.ExistingInstance;
        if (!manager.TryGetChunkRuntime(sample.Address, out ChunkRuntime chunk))
        {
            reason = "目标区块已经卸载";
            return false;
        }

        // 普通陆地默认代价来自生成配置，不能使用旧 TileData 的 1000 代替 WorldModel 的 1。
        double configuredCost = manager.ActiveGenerationProfile.NumericParameters.TryGetValue(
            "navigation.defaultCost", out double value) ? value : 1d;
        short cost = (short)Math.Clamp(configuredCost, 1d, short.MaxValue);
        placedCell = new TileBuildingCell(chunk, sample.WorldCell, sample.LocalCell,
            tileId, definition.tileItemName, sample.Cell);
        TerrainCell ground = new TerrainCell(tileId, 0, 0, sample.Cell.BiomeId,
            cost, TerrainCellFlags.Walkable);
        sample.Terrain.SetCell(sample.LocalCell.x, sample.LocalCell.y, ground);
        SaveDataMgr.Instance?.RecordRuntimeTerrainChange(placedCell);
        CellPlaced?.Invoke(placedCell);
        return true;
    }

    /// <summary>仅回滚尚未覆盖其他内容的本次铺设，恢复原水格全部核心属性。</summary>
    private static bool TryRollbackGround(TileBuildingCell placedCell, out string reason)
    {
        reason = null;
        ChunkTerrainData terrain = placedCell.RuntimeChunk?.Terrain;
        if (terrain == null || terrain.IsDisposed)
        {
            reason = "铺设区块已经卸载，无法回滚";
            return false;
        }
        Vector2Int local = placedCell.LocalPosition;
        TerrainCell current = terrain.GetCell(local.x, local.y);
        if (current.GroundTileId != placedCell.RuntimeTileId || current.BackTileId != 0 ||
            current.BlockingTileId != 0 || BuildingOccupancyRegistry.IsOccupied(placedCell.Position))
        {
            reason = "铺设格已经被修改，无法回滚";
            return false;
        }

        terrain.SetCell(local.x, local.y, placedCell.ReplacedGroundCell.Value);
        SaveDataMgr.Instance?.RecordRuntimeTerrainChange(placedCell);
        return true;
    }

    #endregion
}
