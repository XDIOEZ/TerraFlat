using FlatWorld.WorldModel;

namespace FlatWorld.Networking.Gameplay
{
    #region 地形独立层复制数据
    public struct NetworkTerrainFireCellState
    {
        public int Index;
        public float Intensity;

        public readonly bool IsValid(int totalCells) => (uint)Index < (uint)totalCells &&
            float.IsFinite(Intensity) && Intensity >= 0f && Intensity <= 1f;
    }

    public struct NetworkTerrainLiquidCellState
    {
        public int Index;
        public string LiquidId;
        public int Level;

        public static NetworkTerrainLiquidCellState Capture(ChunkTerrainData terrain, int index)
        {
            int x = index % terrain.Width, y = index / terrain.Width;
            return new NetworkTerrainLiquidCellState
            {
                Index = index, LiquidId = terrain.GetLiquidId(x, y), Level = terrain.GetLiquidLevel(x, y)
            };
        }

        public readonly bool IsValid(ChunkTerrainData terrain)
        {
            if ((uint)Index >= (uint)terrain.CellCount || Level < 0 || Level > LiquidCellValue.MaximumLevel ||
                (Level == 0) != string.IsNullOrEmpty(LiquidId)) return false;
            try { return terrain.LiquidTypes.GetIndex(LiquidId) >= 0; }
            catch (System.InvalidOperationException) { return false; }
        }
    }
    #endregion
}
