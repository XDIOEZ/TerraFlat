using FlatWorld.Localization;
using UnityEngine;

// 地表承重与通行规则独立于建筑模块，预览和各放置后端读取同一结果。
internal static class BuildingGroundPlacementRules
{
    #region 建造表面校验
    private const uint BlockedTilePenalty = 1000;
    /// <summary>放置门槛来自当前物品定义；旧存档中的建筑模块状态不能覆盖它。</summary>
    public static int GetRequiredGroundSupport(string itemId)
    {
        return GameRes.ExistingInstance != null &&
               GameRes.ExistingInstance.TryGetItemDefinition(itemId, out RuntimeItemDefinition definition)
            ? definition.RequiredGroundSupport
            : 0;
    }

    /// <summary>地板和水上平台只检查来源表面的承重，保留各自独立的液体与占用规则。</summary>
    public static bool CheckGroundLoadCapacity(Vector2Int worldCell, int requiredGroundSupport,
        out string reason)
    {
        worldCell = WorldTopologyRuntime.NormalizeCell(worldCell);
        Vector2 center = new(worldCell.x + 0.5f, worldCell.y + 0.5f);
        ChunkMgr manager = ChunkMgr.ExistingInstance;
        if (manager == null)
        {
            reason = "区块管理器尚未就绪";
            return false;
        }
        if (manager.TryGetRuntimeTerrainTile(center, out RuntimeTerrainTileSample runtimeTile))
            return CheckRuntimeTileLoadCapacity(runtimeTile, requiredGroundSupport, out reason);
        manager.GetChunkBy_ItemPosition(center, out Chunk chunk);
        TileData topTile = chunk?.Map?.Data?.GetTopTile(worldCell);
        if (topTile != null)
            return CheckLegacyTileLoadCapacity(chunk.Map, worldCell, topTile, requiredGroundSupport, out reason);
        reason = FlatWorldLocalizationService.GetUiFormat("地块 ({0},{1}) 尚未加载", worldCell.x, worldCell.y);
        return false;
    }

    /// <summary>每个占地格独立校验有效表面；液面、平台和干地均读取自己的承重定义。</summary>
    public static bool CheckTilePenalties(Vector2Int worldCell, int requiredGroundSupport, out string reason)
    {
        reason = null;
        ChunkMgr chunkManager = ChunkMgr.Instance;
        if (chunkManager == null)
        {
            reason = "区块管理器尚未就绪";
            return false;
        }

        worldCell = WorldTopologyRuntime.NormalizeCell(worldCell);
        Vector2 tileCenter = new(worldCell.x + 0.5f, worldCell.y + 0.5f);
        if (chunkManager.TryGetRuntimeTerrainTile(
                tileCenter, out RuntimeTerrainTileSample runtimeTile))
        {
            if (runtimeTile.TopTileId == 0)
            {
                reason = FlatWorldLocalizationService.GetUiFormat("地块 ({0},{1}) 不可建造", worldCell.x, worldCell.y);
                return false;
            }

            if (!CheckRuntimeTileLoadCapacity(runtimeTile, requiredGroundSupport, out reason))
                return false;

            if (!runtimeTile.Terrain.IsWalkable(
                    runtimeTile.LocalCell.x, runtimeTile.LocalCell.y) ||
                runtimeTile.Cell.NavigationCost > BlockedTilePenalty)
            {
                reason = FlatWorldLocalizationService.GetUiFormat("地块 ({0},{1}) 不可通行", worldCell.x, worldCell.y);
                return false;
            }
        }
        else
        {
            // 旧 Map 世界仍保留兼容回退；新区块权威不依赖表现对象。
            chunkManager.GetChunkBy_ItemPosition(tileCenter, out Chunk chunk);
            if (chunk?.Map?.Data == null)
            {
                reason = FlatWorldLocalizationService.GetUiFormat("地块 ({0},{1}) 尚未加载", worldCell.x, worldCell.y);
                return false;
            }

            TileData topTile = chunk.Map.Data.GetTopTile(worldCell);
            if (topTile == null)
            {
                reason = FlatWorldLocalizationService.GetUiFormat("地块 ({0},{1}) 不可建造", worldCell.x, worldCell.y);
                return false;
            }

            if (!CheckLegacyTileLoadCapacity(chunk.Map, worldCell, topTile, requiredGroundSupport, out reason))
                return false;

            if (!topTile.IsWalkable || topTile.Penalty > BlockedTilePenalty)
            {
                reason = FlatWorldLocalizationService.GetUiFormat("地块 ({0},{1}) 不可通行", worldCell.x, worldCell.y);
                return false;
            }
        }

        return true;
    }

    /// <summary>世界液面遮盖底土，独立覆盖面则使用平台自身的地块承重。</summary>
    private static bool CheckRuntimeTileLoadCapacity(RuntimeTerrainTileSample tile,
        int requiredGroundSupport, out string reason)
    {
        if (tile.LiquidDepth > 0f)
        {
            if (!WorldLiquidSystem.TryGetDefinition(tile, out LiquidDefinition liquid))
            {
                reason = FlatWorldLocalizationService.GetUiFormat("地块 ({0},{1}) 的液体定义缺失", tile.WorldCell.x, tile.WorldCell.y);
                return false;
            }
            return CheckLoadCapacity(tile.WorldCell, liquid.WorldWater.LoadCapacity, requiredGroundSupport, out reason);
        }

        if (GameRes.ExistingInstance == null ||
            !GameRes.ExistingInstance.TryGetTileDefinition(tile.TopTileId, out RuntimeTileDefinition definition))
        {
            reason = FlatWorldLocalizationService.GetUiFormat("地块 ({0},{1}) 的承重定义缺失", tile.WorldCell.x, tile.WorldCell.y);
            return false;
        }
        return CheckLoadCapacity(tile.WorldCell, definition.LoadCapacity, requiredGroundSupport, out reason);
    }

    /// <summary>旧 Map 的生成期液体也按液面承重，干地从当前地块目录读取配置。</summary>
    private static bool CheckLegacyTileLoadCapacity(Map map, Vector2Int worldCell, TileData tile,
        int requiredGroundSupport, out string reason)
    {
        if (map.GetGeneratedLiquidDepth(worldCell) > 0f)
        {
            string liquidId = map.GetGeneratedLiquidId(worldCell);
            if (GameRes.ExistingInstance == null ||
                !GameRes.ExistingInstance.TryGetLiquidDefinition(liquidId, out LiquidDefinition liquid) ||
                liquid.WorldWater == null)
            {
                reason = FlatWorldLocalizationService.GetUiFormat("地块 ({0},{1}) 的液体定义缺失", worldCell.x, worldCell.y);
                return false;
            }
            return CheckLoadCapacity(worldCell, liquid.WorldWater.LoadCapacity, requiredGroundSupport, out reason);
        }

        if (GameRes.ExistingInstance == null ||
            !GameRes.ExistingInstance.TryGetTileDefinition(tile.ID, out RuntimeTileDefinition definition))
        {
            reason = FlatWorldLocalizationService.GetUiFormat("地块 ({0},{1}) 的承重定义缺失", worldCell.x, worldCell.y);
            return false;
        }
        return CheckLoadCapacity(worldCell, definition.LoadCapacity, requiredGroundSupport, out reason);
    }

    /// <summary>建筑需求超过地表承重时拒绝放置，等值允许。</summary>
    private static bool CheckLoadCapacity(Vector2Int worldCell, int available, int required, out string reason)
    {
        reason = available >= required
            ? null
            : FlatWorldLocalizationService.GetUiFormat("地块 ({0},{1}) 承重 {2}，建筑需要 {3}", worldCell.x, worldCell.y, available, required);
        return reason == null;
    }
    #endregion
}
