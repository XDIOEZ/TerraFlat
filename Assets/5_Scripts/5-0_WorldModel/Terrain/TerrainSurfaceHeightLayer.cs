using System;

namespace FlatWorld.WorldModel
{
    /// <summary>从真实地层顶层投影固体、液面与独立支撑面的逻辑高程。</summary>
    public static class TerrainSurfaceHeightLayer
    {
        #region 高度层与查询

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

        /// <summary>采挖凹陷只从真实顶层与生成底床的差派生，不再保存计数。</summary>
        public static int GetExcavatedLevels(ChunkTerrainData terrain, int x, int y) => Math.Max(0,
            GetBaseLevel(terrain, x, y, terrain.GenerationMode == ChunkGenerationMode.Surface) -
            GetNaturalDepthLevels(terrain, x, y) - terrain.GetTopGroundZ(x, y));

        public static int GetNaturalDepthLevels(ChunkTerrainData terrain, int x, int y) =>
            ReadLevels(terrain, NaturalDepthLevelsLayerId, x, y);

        public static bool TryGetSurface(ChunkTerrainData terrain, int x, int y, bool useGeneratedHeight,
            out TerrainSurfaceHeight surface)
        {
            surface = default;
            if (terrain == null || terrain.IsDisposed || (uint)x >= (uint)terrain.Width ||
                (uint)y >= (uint)terrain.Height)
                return false;
            TerrainCell cell = terrain.GetCell(x, y);
            bool support = TerrainSupportLayer.GetExposedTileId(terrain, x, y) != 0;
            bool liquid = !support && terrain.GetLiquidLevel(x, y) > 0;
            bool blocking = cell.BlockingTileId != 0 && (cell.Flags & TerrainCellFlags.Blocking) != 0;
            if (cell.GroundTileId == 0 && !support && !liquid && !blocking)
                return false;

            int baseline = GetBaseLevel(terrain, x, y, useGeneratedHeight);
            int naturalDepth = GetNaturalDepthLevels(terrain, x, y);
            // 天然水面只低于同地势河岸一层，抽干后才显示底床的真实深浅。
            int solidZ = terrain.GetTopGroundZ(x, y) + baseline -
                GetBaseLevel(terrain, x, y, terrain.GenerationMode == ChunkGenerationMode.Surface);
            int level = support ? baseline : liquid ? solidZ + naturalDepth - (naturalDepth > 0 ? 1 : -1) : solidZ;
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
