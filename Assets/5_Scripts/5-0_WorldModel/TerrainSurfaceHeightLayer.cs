using System;

namespace FlatWorld.WorldModel
{
    /// <summary>统一查询地表的逻辑高低；不改生成高度、二维坐标或物理碰撞。</summary>
    public static class TerrainSurfaceHeightLayer
    {
        #region 高度层与查询

        public const string ExcavatedLevelsLayerId = "terrain.surface.excavatedLevels";
        public const string CoverLevelsLayerId = "terrain.surface.coverLevels";
        public const string NaturalDepthLevelsLayerId = "terrain.surface.naturalDepthLevels";
        public const int GeneratedHeightLevels = 20;

        /// <summary>生成高度只提供基础档位，缺失或非法高度使用平地基准。</summary>
        public static int GetBaseLevel(ChunkTerrainData terrain, int x, int y, bool useGeneratedHeight)
        {
            if (!useGeneratedHeight || !terrain.TryGetEnvironmentValue("height", x, y, out float height) ||
                float.IsNaN(height) || float.IsInfinity(height))
                return 0;
            return Math.Min(GeneratedHeightLevels - 1,
                (int)Math.Floor(Math.Clamp(height, 0f, 1f) * GeneratedHeightLevels));
        }

        public static int GetExcavatedLevels(ChunkTerrainData terrain, int x, int y) =>
            ReadLevels(terrain, ExcavatedLevelsLayerId, x, y);

        /// <summary>天然底床凹陷属于生成地形，取水或倒水不会改写。</summary>
        public static int GetNaturalDepthLevels(ChunkTerrainData terrain, int x, int y) =>
            ReadLevels(terrain, NaturalDepthLevelsLayerId, x, y);

        /// <summary>每次真正采掉天然地层才降低表面，独立于石头的剩余产量。</summary>
        public static void SetExcavatedLevels(ChunkTerrainData terrain, int x, int y, int levels)
        {
            if (levels < 0) throw new ArgumentOutOfRangeException(nameof(levels));
            if (GetExcavatedLevels(terrain, x, y) != levels)
                terrain.SetEnvironmentValue(ExcavatedLevelsLayerId, x, y, levels);
        }

        /// <summary>普通填土覆盖按堆栈抬升；地板与平台另读支撑层，不冒充填土。</summary>
        public static void SetCoverLevels(ChunkTerrainData terrain, int x, int y, int levels)
        {
            if (levels < 0) throw new ArgumentOutOfRangeException(nameof(levels));
            if (ReadLevels(terrain, CoverLevelsLayerId, x, y) != levels)
                terrain.SetEnvironmentValue(CoverLevelsLayerId, x, y, levels);
        }

        public static bool TryGetSurface(ChunkTerrainData terrain, int x, int y, bool useGeneratedHeight,
            out TerrainSurfaceHeight surface, int minimumDepth = 0)
        {
            surface = default;
            if (terrain == null || terrain.IsDisposed || (uint)x >= (uint)terrain.Width ||
                (uint)y >= (uint)terrain.Height)
                return false;
            TerrainCell cell = terrain.GetCell(x, y);
            bool support = TerrainSupportLayer.GetTileId(terrain, x, y) != 0;
            bool liquid = !support && terrain.GetLiquidLevel(x, y) > 0;
            bool blocking = cell.BlockingTileId != 0 && (cell.Flags & TerrainCellFlags.Blocking) != 0;
            if (cell.GroundTileId == 0 && !support && !liquid && !blocking)
                return false;

            int baseline = GetBaseLevel(terrain, x, y, useGeneratedHeight);
            int level = support ? baseline : baseline - GetNaturalDepthLevels(terrain, x, y) -
                Math.Max(GetExcavatedLevels(terrain, x, y), minimumDepth) +
                ReadLevels(terrain, CoverLevelsLayerId, x, y);
            // 液面统一高于实际底床一层，支撑面仍回到地面基准并遮住坑水。
            if (liquid) level++;
            surface = new TerrainSurfaceHeight(level, blocking ? level + 1 : level, !blocking);
            return true;
        }

        /// <summary>正差代表邻格更高，阴影只落在当前较低的接收面。</summary>
        public static int GetShadowDelta(TerrainSurfaceHeight receiver, TerrainSurfaceHeight neighbour) =>
            receiver.ReceivesShadow ? Math.Max(0, neighbour.TopLevel - receiver.Level) : 0;

        private static int ReadLevels(ChunkTerrainData terrain, string layer, int x, int y) =>
            terrain.TryGetEnvironmentValue(layer, x, y, out float value) &&
            !float.IsNaN(value) && !float.IsInfinity(value) ? Math.Max(0, (int)value) : 0;

        #endregion
    }

    /// <summary>表面高度供查询，顶部高度供邻格投影，墙体自身不接收贴地阴影。</summary>
    public readonly struct TerrainSurfaceHeight
    {
        #region 高度快照

        public TerrainSurfaceHeight(int level, int topLevel, bool receivesShadow)
        {
            Level = level;
            TopLevel = topLevel;
            ReceivesShadow = receivesShadow;
        }

        public int Level { get; }
        public int TopLevel { get; }
        public bool ReceivesShadow { get; }

        #endregion
    }
}
