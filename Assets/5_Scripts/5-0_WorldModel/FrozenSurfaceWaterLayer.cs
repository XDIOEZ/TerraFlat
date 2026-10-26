using System;

namespace FlatWorld.WorldModel
{
    /// <summary>保存冻结前的真实水量与底床，解冻不重新猜测水文身份。</summary>
    public readonly struct FrozenSurfaceWaterSource
    {
        #region 冻结来源快照
        public readonly TerrainCell Bed;
        public readonly int IceTileId;
        public readonly int LiquidTypeIndex;
        public readonly int LiquidLevel;

        public FrozenSurfaceWaterSource(TerrainCell bed, int iceTileId, int liquidTypeIndex, int liquidLevel)
        {
            Bed = bed;
            IceTileId = iceTileId;
            LiquidTypeIndex = liquidTypeIndex;
            LiquidLevel = liquidLevel;
        }
        public bool IsValid => IceTileId > 0 && Bed.GroundTileId > 0 && LiquidTypeIndex > 0 &&
            LiquidLevel > 0 && LiquidLevel <= LiquidCellValue.MaximumLevel;
        #endregion
    }

    public static class FrozenSurfaceWaterLayer
    {
        #region 冻结元数据
        public const string IceTileLayer = "ice.source.tile";
        public const string BedGroundLayer = "ice.source.ground";
        public const string BedBackLayer = "ice.source.back";
        public const string BedBlockingLayer = "ice.source.blocking";
        public const string BedNavigationLayer = "ice.source.navigation";
        public const string BedFlagsLayer = "ice.source.flags";
        public const string LiquidTypeLayer = "ice.source.liquidType";
        public const string LiquidLevelLayer = "ice.source.liquidLevel";
        public const string MeltProgressLayer = "ice.melt.progress";

        // 只有实际冻水格才分配来源层，普通陆地不增加整块数组。
        public static void WriteGenerationSource(ChunkTerrainBuffer terrain, int index, FrozenSurfaceWaterSource source)
        {
            if (!source.IsValid) return;
            terrain.GetOrCreateEnvironmentLayer(IceTileLayer)[index] = source.IceTileId;
            terrain.GetOrCreateEnvironmentLayer(BedGroundLayer)[index] = source.Bed.GroundTileId;
            terrain.GetOrCreateEnvironmentLayer(BedBackLayer)[index] = source.Bed.BackTileId;
            terrain.GetOrCreateEnvironmentLayer(BedBlockingLayer)[index] = source.Bed.BlockingTileId;
            terrain.GetOrCreateEnvironmentLayer(BedNavigationLayer)[index] = source.Bed.NavigationCost;
            terrain.GetOrCreateEnvironmentLayer(BedFlagsLayer)[index] = (byte)source.Bed.Flags;
            terrain.GetOrCreateEnvironmentLayer(LiquidTypeLayer)[index] = source.LiquidTypeIndex;
            terrain.GetOrCreateEnvironmentLayer(LiquidLevelLayer)[index] = source.LiquidLevel;
        }

        public static bool TryReadSource(ChunkTerrainData terrain, int x, int y, out FrozenSurfaceWaterSource source)
        {
            source = default;
            if (terrain == null || terrain.IsDisposed ||
                !terrain.TryGetEnvironmentValue(IceTileLayer, x, y, out float ice) || ice <= 0f) return false;
            terrain.TryGetEnvironmentValue(BedGroundLayer, x, y, out float ground);
            terrain.TryGetEnvironmentValue(BedBackLayer, x, y, out float back);
            terrain.TryGetEnvironmentValue(BedBlockingLayer, x, y, out float blocking);
            terrain.TryGetEnvironmentValue(BedNavigationLayer, x, y, out float navigation);
            terrain.TryGetEnvironmentValue(BedFlagsLayer, x, y, out float flags);
            terrain.TryGetEnvironmentValue(LiquidTypeLayer, x, y, out float liquid);
            terrain.TryGetEnvironmentValue(LiquidLevelLayer, x, y, out float level);
            source = new FrozenSurfaceWaterSource(new TerrainCell((int)ground, (int)back, (int)blocking,
                terrain.GetCell(x, y).BiomeId, (short)navigation, (TerrainCellFlags)(byte)flags),
                (int)ice, (int)liquid, (int)level);
            return source.IsValid;
        }
        #endregion
    }
}
