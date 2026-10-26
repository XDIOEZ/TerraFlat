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
            LiquidCellValue baseline = liquidBaselines.TryGetValue(index, out LiquidCellValue initial)
                ? initial : liquid.Read(index);
            if (baseline.LiquidLevel > 0) return baseline;
            // 已解冻水域恢复天然身份，存盘差量仍对原始冻结液体数组比较。
            if (TryGetEnvironmentValue(FrozenSurfaceWaterLayer.MeltProgressLayer, x, y, out float progress) &&
                progress >= 1f && FrozenSurfaceWaterLayer.TryReadSource(this, x, y, out FrozenSurfaceWaterSource source))
                return new LiquidCellValue(source.LiquidTypeIndex, source.LiquidLevel);
            return baseline;
        }
        #endregion
    }
}
