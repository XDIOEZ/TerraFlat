using System;

namespace FlatWorld.WorldModel
{
    /// <summary>独立支撑面：保留原始水体、地形与通行代价，移除覆盖后直接重新使用底层数据。</summary>
    public static class TerrainSupportLayer
    {
        public const string TileIdLayer = "terrain.support.tileId"; // 覆盖地块身份。
        public const string CostLayer = "terrain.support.navigationCost"; // 支撑面通行代价。

        /// <summary>查询独立支撑地块，零表示没有支撑面。</summary>
        public static int GetTileId(ChunkTerrainData terrain, int x, int y) =>
            terrain.TryGetEnvironmentValue(TileIdLayer, x, y, out float id) ? (int)id : 0;

        /// <summary>生成供通行和交互使用的有效表面；不修改基础水体数据。</summary>
        public static TerrainCell GetSurfaceCell(ChunkTerrainData terrain, int x, int y)
        {
            TerrainCell cell = terrain.GetCell(x, y);
            int id = GetTileId(terrain, x, y);
            if (id == 0)
                return cell;
            terrain.TryGetEnvironmentValue(CostLayer, x, y, out float cost);
            return new TerrainCell(id, cell.BackTileId, cell.BlockingTileId, cell.BiomeId,
                (short)Math.Clamp(cost, 1f, short.MaxValue), (cell.Flags & ~TerrainCellFlags.Water) | TerrainCellFlags.Walkable);
        }

        /// <summary>更新覆盖层；成本先提交，身份最后提交，使变化通知读取完整结果。</summary>
        public static void Set(ChunkTerrainData terrain, int x, int y, int tileId, short cost)
        {
            terrain.SetEnvironmentValue(CostLayer, x, y, tileId == 0 ? 0 : cost);
            terrain.SetEnvironmentValue(TileIdLayer, x, y, tileId);
        }
    }
}
