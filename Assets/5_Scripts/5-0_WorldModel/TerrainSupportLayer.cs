using System;

namespace FlatWorld.WorldModel
{
    /// <summary>独立地表覆盖：保留原始地形与通行代价，移除平台/地板后直接重新使用底层数据。</summary>
    public static class TerrainSupportLayer
    {
        public const string TileIdLayer = "terrain.support.tileId"; // 覆盖地块身份。
        public const string CostLayer = "terrain.support.navigationCost"; // 覆盖面通行代价。
        public const string ExternalVisualLayer = "terrain.support.externalVisual"; // 已由普通实体显示的支撑面不重复绘制。
        public const string ExternalOwnershipLayer = "terrain.support.externalOwnership"; // 结构事务持有的支撑面不再由地格事务重复移交。

        /// <summary>查询独立覆盖地块，零表示没有覆盖面。</summary>
        public static int GetTileId(ChunkTerrainData terrain, int x, int y) =>
            terrain.TryGetEnvironmentValue(TileIdLayer, x, y, out float id) ? (int)id : 0;

        // 铺在支撑面上的真实地层优先参与普通采样，挖掉覆盖后重新露出支撑面。
        public static int GetExposedTileId(ChunkTerrainData terrain, int x, int y) =>
            terrain.GetCell(x, y).GroundTileId != 0 &&
            terrain.TryGetEnvironmentValue(TerrainSurfaceHeightLayer.CoverLevelsLayerId, x, y, out float covers) && covers > 0
                ? 0 : GetTileId(terrain, x, y);

        /// <summary>生成供通行和交互使用的有效表面；不修改基础地形数据。</summary>
        public static TerrainCell GetSurfaceCell(ChunkTerrainData terrain, int x, int y)
        {
            TerrainCell cell = terrain.GetCell(x, y);
            int id = GetExposedTileId(terrain, x, y);
            if (id == 0)
                return cell;
            terrain.TryGetEnvironmentValue(CostLayer, x, y, out float cost);
            return new TerrainCell(id, cell.BackTileId, cell.BlockingTileId, cell.BiomeId,
                (short)Math.Clamp(cost, 1f, short.MaxValue), cell.Flags | TerrainCellFlags.Walkable);
        }

        /// <summary>更新覆盖层；成本先提交，身份最后提交，使变化通知读取完整结果。</summary>
        public static void Set(ChunkTerrainData terrain, int x, int y, int tileId, short cost,
            bool renderedByEntity = false, bool externallyOwned = false)
        {
            terrain.SetEnvironmentValue(CostLayer, x, y, tileId == 0 ? 0 : cost);
            if (renderedByEntity || terrain.TryGetEnvironmentValue(ExternalVisualLayer, x, y, out _))
                terrain.SetEnvironmentValue(ExternalVisualLayer, x, y, tileId != 0 && renderedByEntity ? 1 : 0);
            if (externallyOwned || terrain.TryGetEnvironmentValue(ExternalOwnershipLayer, x, y, out _))
                terrain.SetEnvironmentValue(ExternalOwnershipLayer, x, y, tileId != 0 && externallyOwned ? 1 : 0);
            terrain.SetEnvironmentValue(TileIdLayer, x, y, tileId);
        }
    }
}
