using System;

namespace FlatWorld.WorldModel
{
    /// <summary>格子建筑复制只携带核心地形、支撑面、损伤和草层，不覆盖液体及天气状态。</summary>
    public struct TerrainBuildingCellState
    {
        #region 复制数据
        public const string DamageLayerId = "flatworld.tileBuilding.damage";
        public int Index;
        public int GroundTileId, BackTileId, BlockingTileId, BiomeId;
        public short NavigationCost;
        public TerrainCellFlags Flags;
        public int SupportTileId;
        public short SupportCost;
        public float Damage;
        public byte Grass;
        #endregion

        #region 捕获与应用
        public static TerrainBuildingCellState Capture(ChunkTerrainData terrain, int index)
        {
            int x = index % terrain.Width, y = index / terrain.Width;
            TerrainCell cell = terrain.GetCell(x, y);
            terrain.TryGetEnvironmentValue(TerrainSupportLayer.CostLayer, x, y, out float cost);
            terrain.TryGetEnvironmentValue(DamageLayerId, x, y, out float damage);
            return new TerrainBuildingCellState
            {
                Index = index, GroundTileId = cell.GroundTileId, BackTileId = cell.BackTileId,
                BlockingTileId = cell.BlockingTileId, BiomeId = cell.BiomeId,
                NavigationCost = cell.NavigationCost, Flags = cell.Flags,
                SupportTileId = TerrainSupportLayer.GetTileId(terrain, x, y), SupportCost = (short)cost,
                Damage = damage, Grass = terrain.GetGrass(x, y)
            };
        }

        public bool IsValid(int cellCount) => (uint)Index < (uint)cellCount &&
            GroundTileId >= 0 && BackTileId >= 0 && BlockingTileId >= 0 && SupportTileId >= 0 &&
            (SupportTileId == 0 ? SupportCost == 0 : SupportCost > 0) &&
            !float.IsNaN(Damage) && !float.IsInfinity(Damage) && Damage >= 0f && Grass <= ChunkTerrainData.GrassPresent;

        public void Apply(ChunkTerrainData terrain)
        {
            if (!IsValid(checked(terrain.Width * terrain.Height)))
                throw new ArgumentException("格子建筑网络状态无效。");
            int x = Index % terrain.Width, y = Index / terrain.Width;
            terrain.SetCell(x, y, new TerrainCell(GroundTileId, BackTileId, BlockingTileId,
                BiomeId, NavigationCost, Flags));
            terrain.SetEnvironmentValue(DamageLayerId, x, y, Damage);
            TerrainSupportLayer.Set(terrain, x, y, SupportTileId, SupportCost);
            terrain.SetGrass(x, y, Grass);
        }
        #endregion
    }
}
