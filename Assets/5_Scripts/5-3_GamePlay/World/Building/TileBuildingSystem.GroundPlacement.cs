using System;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using UnityEngine;

/// <summary>
/// 可移除地表覆盖的铺设事务；基础地形保持原值，平台/地板单独保存，后续系统读取有效表面。
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

    /// <summary>预览与提交共用规则；只接受满足来源标记且没有叠层或建筑占用的来源格。</summary>
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

        TerrainCell cell = sample.Terrain.GetCell(sample.LocalCell.x, sample.LocalCell.y);
        TerrainCellFlags required = definition.groundPlacement.RequiredSourceFlags;
        if ((cell.Flags & required) != required)
        {
            reason = required == TerrainCellFlags.Water ? "水上平台只能铺在水里" : "目标地形不满足铺设条件";
            return false;
        }
        TerrainCellFlags forbidden = definition.groundPlacement.ForbiddenSourceFlags;
        if ((cell.Flags & forbidden) != 0)
        {
            reason = (forbidden & TerrainCellFlags.Water) != 0 ? "地板只能铺在非水地面" : "目标地形不满足铺设条件";
            return false;
        }
        if (TerrainSupportLayer.GetTileId(sample.Terrain, sample.LocalCell.x, sample.LocalCell.y) != 0 ||
            cell.GroundTileId == 0 || cell.BackTileId != 0 || cell.BlockingTileId != 0 ||
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

    /// <summary>增加独立地表覆盖，并同步持久化和通行查询。</summary>
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
        TerrainSupportLayer.Set(sample.Terrain, sample.LocalCell.x, sample.LocalCell.y, tileId, cost);
        SaveDataMgr.Instance?.RecordSupportCell(sample);
        RefreshSupportNavigation(sample.WorldCell);
        CellPlaced?.Invoke(placedCell);
        return true;
    }

    /// <summary>仅回滚尚未覆盖其他内容的本次铺设；移除覆盖后直接恢复原地形。</summary>
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
        if (TerrainSupportLayer.GetTileId(terrain, local.x, local.y) != placedCell.RuntimeTileId || current.BackTileId != 0 ||
            current.BlockingTileId != 0 || BuildingOccupancyRegistry.IsOccupied(placedCell.Position))
        {
            reason = "铺设格已经被修改，无法回滚";
            return false;
        }

        TerrainSupportLayer.Set(terrain, local.x, local.y, 0, 0);
        SaveDataMgr.Instance?.RecordSupportCell(new RuntimeTerrainTileSample(placedCell.RuntimeChunk.Address,
            terrain, placedCell.Position, local, current, terrain.GetTopTileId(local.x, local.y)));
        RefreshSupportNavigation(placedCell.Position);
        return true;
    }

    /// <summary>更新覆盖格的导航，不改变底层地形及周围格的基础代价。</summary>
    private static void RefreshSupportNavigation(Vector2Int cell) =>
        WorldNavigationManager.Instance?.QueueNavigationRegion(new RectInt(cell.x, cell.y, 1, 1));

    /// <summary>主动拆除只允许空覆盖格；返还物先创建成功，再撤销覆盖状态。</summary>
    public static bool TryDismantleSupport(Vector2Int worldCell, out string reason)
    {
        reason = null;
        if (!GameNetwork.HasStateAuthority)
            return false;
        ChunkMgr manager = ChunkMgr.ExistingInstance;
        if (manager == null || !manager.TryGetRuntimeTerrainTile((Vector2)worldCell + Vector2.one * 0.5f, out RuntimeTerrainTileSample sample))
            return false;
        int tileId = TerrainSupportLayer.GetTileId(sample.Terrain, sample.LocalCell.x, sample.LocalCell.y);
        if (tileId == 0 || !TryResolveRuntimeTileBlockId(manager, tileId, out string blockId))
        {
            reason = "这里没有可拆的地板或平台。";
            return false;
        }
        TerrainCell baseCell = sample.Terrain.GetCell(sample.LocalCell.x, sample.LocalCell.y);
        if (baseCell.BackTileId != 0 || baseCell.BlockingTileId != 0 || BuildingOccupancyRegistry.IsOccupied(sample.WorldCell))
        {
            reason = "请先移走覆盖面上的建筑。";
            return false;
        }
        var occupants = new System.Collections.Generic.List<Item>();
        var uniqueItems = new System.Collections.Generic.HashSet<Item>();
        ItemMgr.Instance.QueryItemsInCircleNonAlloc((Vector2)sample.WorldCell + Vector2.one * 0.5f,
            2f, ~0, null, occupants, uniqueItems);
        foreach (Item occupant in occupants)
        {
            if (occupant == null || occupant.DestructionHandled || occupant.InHand || occupant.Owner != null)
                continue;
            Vector2 position = WorldTopologyRuntime.NormalizePosition(occupant.transform.position);
            if (Mathf.FloorToInt(position.x) == sample.WorldCell.x && Mathf.FloorToInt(position.y) == sample.WorldCell.y)
            {
                reason = "请先移走覆盖面上的角色和物品。";
                return false;
            }
        }
        string refundId = GameRes.Instance.GetTileBlock(blockId).groundPlacement.RefundItemId;
        if (string.IsNullOrEmpty(refundId))
            throw new InvalidOperationException($"地表覆盖缺少拆除返还物品：{blockId}");
        DroppedItemService.Spawn(GameRes.ExistingInstance.CreateItemData(refundId),
            new Vector2(sample.WorldCell.x + 0.5f, sample.WorldCell.y + 0.5f));
        TerrainSupportLayer.Set(sample.Terrain, sample.LocalCell.x, sample.LocalCell.y, 0, 0);
        SaveDataMgr.Instance.RecordSupportCell(sample);
        RefreshSupportNavigation(sample.WorldCell);
        return true;
    }

    #endregion
}
