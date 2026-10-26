using System;

namespace FlatWorld.WorldModel
{
    public readonly struct OceanShoreCurrentSample
    {
        #region 洋流向量

        public OceanShoreCurrentSample(double x, double y) { X = x; Y = y; }
        public double X { get; }
        public double Y { get; }

        #endregion
    }

    /// <summary>海岸内侧回推、外侧离岸，分界弱流与远洋背景共用确定性纯规则。</summary>
    public static class OceanShoreCurrentKernel
    {
        #region 海岸分带洋流

        public const string DirectionXLayer = "ocean.currentX";
        public const string DirectionYLayer = "ocean.currentY";
        public const double NearShoreStrength = 0.45d;
        public const double OffshoreStrength = 1d;

        public static double ResolveSignedCurrentStrength(double shoreDistance, double shelfWidth,
            double transitionWidth)
        {
            if (shoreDistance <= 0d) return 0d;
            double offset = shoreDistance - shelfWidth;
            double progress = Math.Clamp(Math.Abs(offset) / Math.Max(0.000001d, transitionWidth * 0.5d), 0d, 1d);
            double smooth = progress * progress * (3d - 2d * progress);
            return offset < 0d ? -NearShoreStrength * smooth : OffshoreStrength * smooth;
        }

        public static void ResolveCurrentVector(double outwardX, double outwardY, double shoreDistance,
            double shelfWidth, double transitionWidth, double coastalReach,
            double windX, double windY, out double currentX, out double currentY)
        {
            currentX = currentY = 0d;
            if (shoreDistance <= 0d) return;
            double windLength = Math.Sqrt(windX * windX + windY * windY);
            bool hasWind = windLength > 0.000001d;
            if (hasWind) { windX /= windLength; windY /= windLength; }
            if (shoreDistance >= coastalReach)
            {
                if (hasWind) { currentX = windX; currentY = windY; }
                return;
            }
            double signedStrength = ResolveSignedCurrentStrength(shoreDistance, shelfWidth, transitionWidth);
            double normalLength = Math.Sqrt(outwardX * outwardX + outwardY * outwardY);
            if (normalLength > 1d) { outwardX /= normalLength; outwardY /= normalLength; }
            double maximumFadeStart = shelfWidth + Math.Max(0.000001d, coastalReach - shelfWidth) * 0.75d;
            double fadeStart = Math.Min(maximumFadeStart,
                Math.Max(shelfWidth + transitionWidth * 0.5d, coastalReach * 0.75d));
            double fade = Math.Clamp((shoreDistance - fadeStart) / Math.Max(0.000001d, coastalReach - fadeStart), 0d, 1d);
            fade = fade * fade * (3d - 2d * fade);
            // 海岸等距脊线保留弱法线，远洋按真实向量混合以免反向轴出现跳变。
            currentX = outwardX * signedStrength * (1d - fade) + (hasWind ? windX * fade : 0d);
            currentY = outwardY * signedStrength * (1d - fade) + (hasWind ? windY * fade : 0d);
        }

        #endregion
    }
}
