using System;
using System.Collections.Generic;

namespace FlatWorld.WorldModel
{
    #region 纯水文输出
    /// <summary>纯水文结果中的淡水类型；数值会写入 riverKind 环境层。</summary>
    internal enum GeneratedHydrologyKind : byte
    {
        None = 0,
        River = 1,
        Lake = 2
    }

    /// <summary>一个淡水格子的类型、汇流量、水深、横截面位置和湖面高度。</summary>
    internal readonly struct GeneratedHydrologyCell
    {
        internal GeneratedHydrologyCell(
            GeneratedHydrologyKind kind,
            double flow,
            double depth,
            double surfaceLevel = 0d,
            double flowDirectionX = 0d,
            double flowDirectionY = 0d,
            double bedCenterStrength = 1d,
            bool bedEdgeDeposit = false)
        {
            Kind = kind;
            Flow = Math.Max(0d, flow);
            Depth = Clamp01(depth);
            SurfaceLevel = Clamp01(surfaceLevel);
            BedCenterStrength = kind == GeneratedHydrologyKind.River
                ? Clamp01(bedCenterStrength)
                : 0d;
            BedEdgeDeposit = kind == GeneratedHydrologyKind.River && bedEdgeDeposit;
            double directionLength = Math.Sqrt(
                flowDirectionX * flowDirectionX + flowDirectionY * flowDirectionY);
            if (kind == GeneratedHydrologyKind.River && directionLength > 0.000001d)
            {
                FlowDirectionX = flowDirectionX / directionLength;
                FlowDirectionY = flowDirectionY / directionLength;
            }
            else
            {
                FlowDirectionX = 0d;
                FlowDirectionY = 0d;
            }
        }

        internal GeneratedHydrologyKind Kind { get; }
        internal double Flow { get; }
        internal double Depth { get; }
        internal double SurfaceLevel { get; }
        internal double BedCenterStrength { get; }
        internal bool BedEdgeDeposit { get; }
        internal double FlowDirectionX { get; }
        internal double FlowDirectionY { get; }

        private static double Clamp01(double value) =>
            value < 0d ? 0d : value > 1d ? 1d : value;
    }

    /// <summary>当前区块可查询的淡水格与冲积平原，只保存纯数据。</summary>
    internal sealed class GeneratedHydrologyMap
    {
        private readonly IReadOnlyDictionary<Int2, GeneratedHydrologyCell> cells;
        private readonly IReadOnlyDictionary<Int2, double> floodplainCells;

        internal GeneratedHydrologyMap(
            IReadOnlyDictionary<Int2, GeneratedHydrologyCell> cells,
            IReadOnlyDictionary<Int2, double> floodplainCells)
        {
            this.cells = cells ?? throw new ArgumentNullException(nameof(cells));
            this.floodplainCells = floodplainCells ??
                                   throw new ArgumentNullException(nameof(floodplainCells));
        }

        /// <summary>查询世界坐标上的淡水类型和水文数据。</summary>
        internal bool TryGet(int worldX, int worldY, out GeneratedHydrologyCell cell) =>
            cells.TryGetValue(new Int2(worldX, worldY), out cell);

        /// <summary>查询世界坐标上的最大冲积平原强度。</summary>
        internal double GetFloodplainStrength(int worldX, int worldY) =>
            floodplainCells.TryGetValue(new Int2(worldX, worldY), out double strength)
                ? strength
                : 0d;
    }

    #endregion
}
