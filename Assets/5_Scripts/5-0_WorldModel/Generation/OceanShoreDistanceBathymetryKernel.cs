using System;
using System.Buffers;
using System.Threading;

namespace FlatWorld.WorldModel
{
    /// <summary>共享真实离岸距离生成海床与分带洋流，不改变大陆高度与海岸轮廓。</summary>
    public sealed class OceanShoreDistanceBathymetryKernel : IDisposable
    {
        #region 离岸距离窗口

        private readonly int stride;
        private readonly double shelfWidth;
        private readonly double slopeWidth;
        private readonly double currentReach;
        private readonly double currentTransitionWidth;
        private float[] shoreDistances;

        public OceanShoreDistanceBathymetryKernel(int originX, int originY, int width, int height,
            double seaLevel, double shelfWidth, double slopeWidth,
            Func<int, int, double> sampleHeight, CancellationToken cancellationToken = default,
            double currentSearchDistance = 0d, double currentTransitionWidth = 0d)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (!double.IsFinite(shelfWidth) || shelfWidth < 1d ||
                !double.IsFinite(slopeWidth) || slopeWidth < 1d ||
                !double.IsFinite(currentSearchDistance) || currentSearchDistance < 0d ||
                !double.IsFinite(currentTransitionWidth) || currentTransitionWidth < 0d)
                throw new ArgumentOutOfRangeException(nameof(shelfWidth));
            if (sampleHeight == null) throw new ArgumentNullException(nameof(sampleHeight));
            cancellationToken.ThrowIfCancellationRequested();
            stride = checked(width + 2);
            this.shelfWidth = shelfWidth;
            this.slopeWidth = slopeWidth;
            currentReach = Math.Max(shelfWidth + slopeWidth, currentSearchDistance);
            this.currentTransitionWidth = currentTransitionWidth > 0d
                ? currentTransitionWidth : Math.Max(1d, shelfWidth * (3d / 16d));
            int radius = checked((int)Math.Ceiling(currentReach));
            int sourceStride = checked(stride + radius * 2);
            int sourceRows = checked(height + 2 + radius * 2);
            int[] verticalDistances = null;
            int[] envelopeSites = null;
            double[] envelopeBoundaries = null;
            try
            {
                verticalDistances = ArrayPool<int>.Shared.Rent(checked(sourceStride * sourceRows));
                envelopeSites = ArrayPool<int>.Shared.Rent(sourceStride);
                envelopeBoundaries = ArrayPool<double>.Shared.Rent(sourceStride + 1);
                shoreDistances = ArrayPool<float>.Shared.Rent(checked(stride * (height + 2)));
                for (int x = 0; x < sourceStride; x++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int distance = radius;
                    for (int y = 0; y < sourceRows; y++)
                    {
                        bool land = sampleHeight(originX + x - radius - 1, originY + y - radius - 1) >= seaLevel;
                        distance = land ? 0 : Math.Min(radius, distance + 1);
                        verticalDistances[y * sourceStride + x] = distance;
                    }
                    distance = radius;
                    for (int y = sourceRows - 1; y >= 0; y--)
                    {
                        int index = y * sourceStride + x;
                        distance = Math.Min(verticalDistances[index], Math.Min(radius, distance + 1));
                        verticalDistances[index] = distance;
                    }
                }
                // 抛物线下包络在线性时间恢复精确欧氏距离，海床和流向不再逐格横扫搜索半径。
                for (int y = 0; y < height + 2; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int row = (y + radius) * sourceStride;
                    int lastSite = 0;
                    envelopeSites[0] = 0;
                    envelopeBoundaries[0] = double.NegativeInfinity;
                    envelopeBoundaries[1] = double.PositiveInfinity;
                    for (int x = 1; x < sourceStride; x++)
                    {
                        double boundary;
                        while (true)
                        {
                            int previous = envelopeSites[lastSite];
                            double value = verticalDistances[row + x];
                            double previousValue = verticalDistances[row + previous];
                            boundary = ((double)x * x + value * value -
                                ((double)previous * previous + previousValue * previousValue)) /
                                (2d * (x - previous));
                            if (boundary > envelopeBoundaries[lastSite]) break;
                            lastSite--;
                        }
                        envelopeSites[++lastSite] = x;
                        envelopeBoundaries[lastSite] = boundary;
                        envelopeBoundaries[lastSite + 1] = double.PositiveInfinity;
                    }
                    int siteIndex = 0;
                    for (int x = 0; x < stride; x++)
                    {
                        int targetX = x + radius;
                        while (envelopeBoundaries[siteIndex + 1] < targetX) siteIndex++;
                        int sourceX = envelopeSites[siteIndex];
                        double dx = targetX - sourceX;
                        double dy = verticalDistances[row + sourceX];
                        shoreDistances[y * stride + x] = (float)Math.Min(currentReach,
                            Math.Sqrt(dx * dx + dy * dy));
                    }
                }
            }
            catch
            {
                Dispose();
                throw;
            }
            finally
            {
                if (verticalDistances != null) ArrayPool<int>.Shared.Return(verticalDistances);
                if (envelopeSites != null) ArrayPool<int>.Shared.Return(envelopeSites);
                if (envelopeBoundaries != null) ArrayPool<double>.Shared.Return(envelopeBoundaries);
            }
        }

        public double GetShoreDistance(int x, int y) =>
            Math.Min(shelfWidth + slopeWidth, GetCurrentShoreDistance(x, y));

        public double GetCurrentShoreDistance(int x, int y) => shoreDistances[(y + 1) * stride + x + 1];

        public float GetInitialLiquidDepth(int x, int y) =>
            ResolveInitialLiquidDepth(GetShoreDistance(x, y), shelfWidth, slopeWidth);

        public void GetOutwardShoreDirection(int x, int y, out double outwardX, out double outwardY)
        {
            int index = (y + 1) * stride + x + 1;
            outwardX = shoreDistances[index - stride + 1] + 2d * shoreDistances[index + 1] +
                shoreDistances[index + stride + 1] - shoreDistances[index - stride - 1] -
                2d * shoreDistances[index - 1] - shoreDistances[index + stride - 1];
            outwardY = shoreDistances[index + stride - 1] + 2d * shoreDistances[index + stride] +
                shoreDistances[index + stride + 1] - shoreDistances[index - stride - 1] -
                2d * shoreDistances[index - stride] - shoreDistances[index - stride + 1];
            double length = Math.Sqrt(outwardX * outwardX + outwardY * outwardY);
            double normalization = Math.Max(8d, length);
            outwardX /= normalization;
            outwardY /= normalization;
        }

        public OceanShoreCurrentSample GetOceanCurrent(int x, int y, double windX, double windY)
        {
            GetOutwardShoreDirection(x, y, out double outwardX, out double outwardY);
            OceanShoreCurrentKernel.ResolveCurrentVector(outwardX, outwardY, GetCurrentShoreDistance(x, y),
                shelfWidth, currentTransitionWidth, currentReach, windX, windY, out double currentX, out double currentY);
            return new OceanShoreCurrentSample(currentX, currentY);
        }

        public void Dispose()
        {
            if (shoreDistances == null) return;
            ArrayPool<float>.Shared.Return(shoreDistances);
            shoreDistances = null;
        }

        #endregion

        #region 浅海陆架与海底陡降

        /// <summary>陆架水量一至三级，离岸达到陆架宽度后迅速降到十级深海。</summary>
        public static float ResolveInitialLiquidDepth(double shoreDistance, double shelfWidth, double slopeWidth)
        {
            if (shoreDistance <= 0d) return 0f;
            if (shoreDistance <= shelfWidth)
                return (float)(0.1d + 0.2d * Math.Clamp((shoreDistance - 1d) / Math.Max(1d, shelfWidth - 1d), 0d, 1d));
            double descent = Math.Clamp((shoreDistance - shelfWidth) / slopeWidth, 0d, 1d);
            return (float)(0.3d + 0.7d * descent);
        }

        /// <summary>天然海底随初始水量加深，取水后保留真实海底台阶。</summary>
        public static int ResolveNaturalOceanBedDepthLevels(int liquidLevel) =>
            liquidLevel <= 0 ? 0 : 1 + Math.Clamp(liquidLevel, 1, 10);

        #endregion
    }
}
