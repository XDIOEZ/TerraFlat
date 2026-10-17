namespace FlatWorld.WorldModel
{
    public sealed partial class ChunkTerrainData
    {
        #region 液体生成基线查询
        /// <summary>读取首次修改前的液体，用于区分被抽干的水域和原本没有水的陆地。</summary>
        public LiquidCellValue GetLiquidBaseline(int x, int y)
        {
            ThrowIfDisposed();
            int index = GetIndex(x, y);
            return liquidBaselines.TryGetValue(index, out LiquidCellValue baseline) ? baseline : liquid.Read(index);
        }
        #endregion
    }
}
