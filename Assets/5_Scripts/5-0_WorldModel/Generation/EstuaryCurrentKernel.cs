using System;

namespace FlatWorld.WorldModel
{
    /// <summary>独立于水量与液体身份的入海口表层流场。</summary>
    public readonly struct EstuaryCurrentSample
    {
        public EstuaryCurrentSample(double x, double y, double influence)
        { X = x; Y = y; Influence = influence; }

        public double X { get; }
        public double Y { get; }
        public double Influence { get; }
    }

    /// <summary>河口扇面与反向洋流转向共用纯规则，不依赖区块加载顺序。</summary>
    public static class EstuaryCurrentKernel
    {
        #region 扇面采样

        public const string DirectionXLayer = "estuary.currentX";
        public const string DirectionYLayer = "estuary.currentY";
        public const string InfluenceLayer = "estuary.influence";

        /// <summary>沿下游展开有限扇面；近口不横推岸边，外缘平滑交还洋流。</summary>
        public static bool TrySample(double offsetX, double offsetY, double downstreamX,
            double downstreamY, double halfWidth, double length, double spread,
            out EstuaryCurrentSample sample)
        {
            sample = default;
            if (!Finite(offsetX) || !Finite(offsetY) || !Finite(downstreamX) ||
                !Finite(downstreamY) || !Finite(halfWidth) || !Finite(length) ||
                !Finite(spread) || halfWidth <= 0d || length <= 0d || spread <= 0d)
                return false;
            double magnitude = Math.Sqrt(downstreamX * downstreamX + downstreamY * downstreamY);
            if (!Finite(magnitude) || magnitude <= 0.000001d) return false;
            double x = downstreamX / magnitude, y = downstreamY / magnitude;
            double forward = offsetX * x + offsetY * y;
            double side = offsetY * x - offsetX * y;
            double distance = Math.Sqrt(offsetX * offsetX + offsetY * offsetY);
            double width = halfWidth + Math.Max(0d, forward) * spread;
            if (forward < -halfWidth || distance >= length || Math.Abs(side) >= width)
                return false;

            double influence = (1d - Smooth((distance / length - 0.6d) / 0.4d)) *
                               (1d - Smooth((Math.Abs(side) / width - 0.75d) / 0.25d));
            if (influence <= 0d) return false;
            double lateral = side * Smooth(Math.Max(0d, forward) / Math.Max(1d, halfWidth));
            double axial = halfWidth / spread + Math.Max(0d, forward);
            double vx = x * axial - y * lateral, vy = y * axial + x * lateral;
            double norm = Math.Sqrt(vx * vx + vy * vy);
            if (!Finite(norm) || norm <= 0.000001d) return false;
            sample = new EstuaryCurrentSample(vx / norm, vy / norm, influence);
            return true;
        }

        #endregion

        #region 洋流衔接

        /// <summary>插值转角而非抵消速度；正反流交接时侧向绕开，不形成静止堵点。</summary>
        public static void ResolveDirection(double windX, double windY, EstuaryCurrentSample plume,
            out double x, out double y)
        {
            double windLength = Math.Sqrt(windX * windX + windY * windY);
            bool hasWind = Finite(windLength) && windLength > 0.000001d;
            double plumeLength = Math.Sqrt(plume.X * plume.X + plume.Y * plume.Y);
            if (!Finite(plume.Influence) || plume.Influence <= 0d ||
                !Finite(plumeLength) || plumeLength <= 0.000001d)
            {
                x = hasWind ? windX / windLength : 0d;
                y = hasWind ? windY / windLength : 0d;
                return;
            }
            x = plume.X / plumeLength; y = plume.Y / plumeLength;
            if (!hasWind || plume.Influence >= 1d) return;
            windX /= windLength; windY /= windLength;
            double cross = x * windY - y * windX;
            double dot = x * windX + y * windY;
            // 正对顶流只有中心线上需要固定转向；两侧按扇面自然向外分开。
            double turn = Math.Abs(cross) <= 0.000000001d && dot < 0d
                ? Math.PI : Math.Atan2(cross, dot);
            double angle = turn * (1d - plume.Influence);
            double cos = Math.Cos(angle), sin = Math.Sin(angle);
            double originalX = x;
            x = originalX * cos - y * sin;
            y = originalX * sin + y * cos;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static double Smooth(double value)
        {
            double t = Math.Max(0d, Math.Min(1d, value));
            return t * t * (3d - 2d * t);
        }

        #endregion
    }
}
