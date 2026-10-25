using System;
using System.Buffers;
using System.Threading;

namespace FlatWorld.WorldModel
{
    /// <summary>按真实离岸距离生成浅海陆架和短距离陡降，不改变大陆高度与海岸轮廓。</summary>
    public sealed class OceanShoreDistanceBathymetryKernel : IDisposable
    {
        #region 离岸距离窗口

        private readonly int width;
        private readonly double shelfWidth;
        private readonly double slopeWidth;
        private float[] shoreDistances;

        public OceanShoreDistanceBathymetryKernel(int originX, int originY, int width, int height,
            double seaLevel, double shelfWidth, double slopeWidth,
            Func<int, int, double> sampleHeight, CancellationToken cancellationToken = default)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (!double.IsFinite(shelfWidth) || shelfWidth < 1d ||
                !double.IsFinite(slopeWidth) || slopeWidth < 1d)
                throw new ArgumentOutOfRangeException(nameof(shelfWidth));
            this.width = width;
            this.shelfWidth = shelfWidth;
            this.slopeWidth = slopeWidth;
            int radius = checked((int)Math.Ceiling(shelfWidth + slopeWidth));
            int stride = checked(width + radius * 2);
            int rows = checked(height + radius * 2);
            int[] verticalDistances = ArrayPool<int>.Shared.Rent(checked(stride * rows));
            shoreDistances = ArrayPool<float>.Shared.Rent(checked(width * height));
            try
            {
                bool hasLand = false;
                // 纵向先求最近陆地，横向只算输出格，精确欧氏距离跨区块保持一致。
                for (int x = 0; x < stride; x++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int distance = radius;
                    for (int y = 0; y < rows; y++)
                    {
                        bool land = sampleHeight(originX + x - radius, originY + y - radius) >= seaLevel;
                        hasLand |= land;
                        distance = land ? 0 : Math.Min(radius, distance + 1);
                        verticalDistances[y * stride + x] = distance;
                    }
                    distance = radius;
                    for (int y = rows - 1; y >= 0; y--)
                    {
                        int index = y * stride + x;
                        distance = Math.Min(verticalDistances[index], Math.Min(radius, distance + 1));
                        verticalDistances[index] = distance;
                    }
                }
                for (int y = 0; y < height; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int row = (y + radius) * stride;
                    for (int x = 0; x < width; x++)
                    {
                        int nearestSquared = radius * radius;
                        if (hasLand)
                        {
                            for (int dx = -radius; dx <= radius; dx++)
                            {
                                int dy = verticalDistances[row + x + radius + dx];
                                nearestSquared = Math.Min(nearestSquared, dx * dx + dy * dy);
                            }
                        }
                        shoreDistances[y * width + x] = (float)Math.Sqrt(nearestSquared);
                    }
                }
            }
            catch
            {
                Dispose();
                throw;
            }
            finally { ArrayPool<int>.Shared.Return(verticalDistances); }
        }

        public double GetShoreDistance(int x, int y) => shoreDistances[y * width + x];

        public float GetInitialLiquidDepth(int x, int y) =>
            ResolveInitialLiquidDepth(GetShoreDistance(x, y), shelfWidth, slopeWidth);

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
