using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace FlatWorld.WorldModel
{
    public sealed partial class DeterministicChunkGenerator
    {
        #region 高度图水文

        private const double DownhillEpsilon = 0.00001d;

        private static readonly Int2[] RiverNeighbors =
        {
            new Int2(-1, -1), new Int2(0, -1), new Int2(1, -1),
            new Int2(-1, 0),                       new Int2(1, 0),
            new Int2(-1, 1),  new Int2(0, 1),  new Int2(1, 1)
        };

        // D∞ 的八条栅格边，按数学正方向逆时针排列；相邻两条边共同组成一个三角坡面。
        private static readonly Int2[] RiverFlowDirections =
        {
            new Int2(1, 0), new Int2(1, 1), new Int2(0, 1), new Int2(-1, 1),
            new Int2(-1, 0), new Int2(-1, -1), new Int2(0, -1), new Int2(1, -1)
        };

        /// <summary>同一栅格被多条曲线经过时保留水量更大的主槽样本。</summary>
        private static void SetChannelCenterSample(
            Dictionary<Int2, ChannelCenterSample> samples,
            Int2 position,
            ChannelCenterSample candidate)
        {
            if (samples.TryGetValue(position, out ChannelCenterSample existing))
            {
                if (existing.Flow >= candidate.Flow)
                {
                    if (!candidate.BedEdgeDeposit || existing.BedEdgeDeposit)
                        return;
                    samples[position] = new ChannelCenterSample(
                        existing.Flow,
                        existing.DirectionX,
                        existing.DirectionY,
                        true);
                    return;
                }
                if (existing.BedEdgeDeposit && !candidate.BedEdgeDeposit)
                {
                    candidate = new ChannelCenterSample(
                        candidate.Flow,
                        candidate.DirectionX,
                        candidate.DirectionY,
                        true);
                }
            }
            samples[position] = candidate;
        }
        /// <summary>只在低坡且已有明显汇流的主河两侧生成宽缓冲积带。</summary>
        private static void AddFloodplain(
            ChunkGenerationSettingsSnapshot settings,
            HydrologySamplingContext sampling,
            Int2 center,
            double flow,
            int channelRadius,
            FloodplainRaster floodplain,
            bool measureCandidates,
            ref long candidates,
            ref long mergeAttempts)
        {
            if (settings.RiverFloodplainMaxRadius <= channelRadius ||
                flow < settings.RiverFloodplainStartFlow)
            {
                return;
            }

            double slope = EstimateLocalSlope(sampling, center);
            double flatness = 1d - Clamp01(slope / settings.RiverFloodplainMaxSlope);
            if (flatness <= 0.05d)
                return;

            double flowT = InverseLerp(
                settings.RiverFloodplainStartFlow,
                Math.Max(settings.RiverFloodplainStartFlow + 0.001d,
                    settings.RiverFullWidthFlow),
                flow);
            int minimumRadius = Math.Min(
                settings.RiverFloodplainMaxRadius,
                channelRadius + 2);
            int radius = minimumRadius + (int)Math.Round(
                (settings.RiverFloodplainMaxRadius - minimumRadius) *
                Math.Sqrt(flowT * flatness),
                MidpointRounding.AwayFromZero);
            double centerStrength = Math.Sqrt(flatness) * (0.55d + Math.Sqrt(flowT) * 0.45d);

            FloodplainKernel kernel = FloodplainKernel.ForRadius(radius);
            // 常见圆核将 X/Y 投影从逐像素降为逐行列，超大半径保留原路径避免栈空间过大。
            if (radius <= 96)
            {
                int diameter = radius * 2 + 1;
                Span<int> mappedX = stackalloc int[diameter];
                Span<int> mappedY = stackalloc int[diameter];
                for (int i = 0; i < diameter; i++)
                {
                    mappedX[i] = floodplain.MapX(center.X + i - radius);
                    mappedY[i] = floodplain.MapY(center.Y + i - radius);
                }
                foreach (FloodplainKernel.Sample sample in kernel.Samples)
                {
                    if (measureCandidates) candidates++;
                    Int2 position = new(center.X + sample.X, center.Y + sample.Y);
                    if (floodplain.MergeMapped(position, mappedX[sample.X + radius],
                        mappedY[sample.Y + radius], centerStrength * sample.Strength) && measureCandidates)
                        mergeAttempts++;
                }
                return;
            }
            foreach (FloodplainKernel.Sample sample in kernel.Samples)
            {
                if (measureCandidates) candidates++;
                Int2 position = new(center.X + sample.X, center.Y + sample.Y);
                if (floodplain.Merge(position, centerStrength * sample.Strength) && measureCandidates)
                    mergeAttempts++;
            }
        }

        /// <summary>以八邻域最大高差估计格子附近坡度。</summary>
        private static double EstimateLocalSlope(HydrologySamplingContext sampling, Int2 position)
        {
            double center = sampling.Height(position);
            double maximum = 0d;
            for (int i = 0; i < RiverNeighbors.Length; i++)
            {
                double delta = Math.Abs(
                    sampling.Height(position + RiverNeighbors[i]) - center);
                maximum = Math.Max(maximum, delta);
            }
            return maximum;
        }

        /// <summary>合并淡水格；湖泊优先于河道，同类则保留更深和更大流量。</summary>
        private static void SetRiverCell(
            Dictionary<Int2, GeneratedHydrologyCell> cells,
            Int2 position,
            GeneratedHydrologyCell candidate)
        {
            if (!cells.TryGetValue(position, out GeneratedHydrologyCell current))
            {
                cells[position] = candidate;
                return;
            }
            if (current.Kind == GeneratedHydrologyKind.Lake &&
                candidate.Kind != GeneratedHydrologyKind.Lake)
            {
                return;
            }
            if (current.Kind == candidate.Kind &&
                current.Depth >= candidate.Depth &&
                current.Flow >= candidate.Flow &&
                current.BedCenterStrength >= candidate.BedCenterStrength &&
                (!candidate.BedEdgeDeposit || current.BedEdgeDeposit))
            {
                return;
            }

            GeneratedHydrologyKind kind = current.Kind == GeneratedHydrologyKind.Lake ||
                                           candidate.Kind == GeneratedHydrologyKind.Lake
                ? GeneratedHydrologyKind.Lake
                : GeneratedHydrologyKind.River;
            double flowDirectionX = current.FlowDirectionX;
            double flowDirectionY = current.FlowDirectionY;
            bool currentHasDirection = Math.Abs(flowDirectionX) > 0.000001d ||
                                       Math.Abs(flowDirectionY) > 0.000001d;
            if (kind == GeneratedHydrologyKind.Lake)
            {
                flowDirectionX = 0d;
                flowDirectionY = 0d;
            }
            else if (!currentHasDirection || candidate.Flow > current.Flow)
            {
                flowDirectionX = candidate.FlowDirectionX;
                flowDirectionY = candidate.FlowDirectionY;
            }
            cells[position] = new GeneratedHydrologyCell(
                kind,
                Math.Max(current.Flow, candidate.Flow),
                Math.Max(current.Depth, candidate.Depth),
                Math.Max(current.SurfaceLevel, candidate.SurfaceLevel),
                flowDirectionX,
                flowDirectionY,
                Math.Max(current.BedCenterStrength, candidate.BedCenterStrength),
                current.BedEdgeDeposit || candidate.BedEdgeDeposit);
        }

        /// <summary>记录格子的最大冲积带强度，重复计算时只保留更明显的一次。</summary>
        private static void SetFloodplainStrength(
            Dictionary<Int2, double> cells,
            Int2 position,
            double strength)
        {
            strength = Clamp01(strength);
            if (!cells.TryGetValue(position, out double current) || strength > current)
                cells[position] = strength;
        }

        /// <summary>判断一个世界坐标是否落在当前区块范围内。</summary>
        private static bool ContainsChunk(ChunkGenerationRequest request, Int2 position)
        {
            int localX = position.X - request.Address.ChunkOrigin.X;
            int localY = position.Y - request.Address.ChunkOrigin.Y;
            return (uint)localX < (uint)request.Profile.Width &&
                   (uint)localY < (uint)request.Profile.Height;
        }

        /// <summary>把数值映射到两个边界之间的 0 到 1 比例。</summary>
        private static double InverseLerp(double from, double to, double value)
        {
            return Clamp01((value - from) / Math.Max(0.0001d, to - from));
        }
        /// <summary>平滑中心线重新栅格化后的河槽中心样本。</summary>
        private readonly struct ChannelCenterSample
        {
            public ChannelCenterSample(
                double flow,
                double directionX,
                double directionY,
                bool bedEdgeDeposit = false)
            {
                Flow = flow;
                DirectionX = directionX;
                DirectionY = directionY;
                BedEdgeDeposit = bedEdgeDeposit;
            }

            public double Flow { get; }
            public double DirectionX { get; }
            public double DirectionY { get; }
            public bool BedEdgeDeposit { get; }
        }

        /// <summary>复用单次区块水文计算中的高度和降水采样，避免重复计算同一格噪声。</summary>
        private sealed class HydrologySamplingContext
        {
            private readonly Dictionary<Int2, double> heightCache = new();
            private readonly Dictionary<Int2, double> precipitationCache = new();
            private readonly Dictionary<Int2, double> channelCenterBiasCache = new();
            private readonly SharedHydrologyHeightSamplingCache.Partition sharedHeightPartition;
            private readonly List<KeyValuePair<Int2, double>> newSharedHeightSamples;
            private readonly bool measureSampling;

            /// <summary>仅统计缓存未命中时的真实高度计算。</summary>
            public long HeightComputeTicks { get; private set; }
            public long HeightMissCount { get; private set; }
            public long HeightHitCount { get; private set; }
            public long HeightSharedHitCount { get; private set; }
            public long ChannelCenterBiasHitCount { get; private set; }
            public long ChannelCenterBiasMissCount { get; private set; }

            /// <summary>创建一次水文采样上下文，并准备各类采样缓存。</summary>
            public HydrologySamplingContext(
                ChunkGenerationRequest request,
                ChunkGenerationSettingsSnapshot settings)
                : this(request, settings, null)
            {
            }

            public HydrologySamplingContext(
                ChunkGenerationRequest request,
                ChunkGenerationSettingsSnapshot settings,
                SharedHydrologyHeightSamplingCache sharedHeightSamples)
            {
                Request = request;
                Settings = settings;
                measureSampling = ChunkGenerationTiming.CurrentOnThread != null;
                sharedHeightPartition = sharedHeightSamples?.GetPartition(request, settings);
                if (sharedHeightPartition != null) newSharedHeightSamples = new List<KeyValuePair<Int2, double>>();
            }

            public ChunkGenerationRequest Request { get; }
            public ChunkGenerationSettingsSnapshot Settings { get; }

            /// <summary>把坐标归一化到当前世界范围，处理环绕世界边界。</summary>
            public Int2 Normalize(Int2 position)
            {
                return new Int2(
                    Request.Topology.NormalizeX(position.X),
                    Request.Topology.NormalizeY(position.Y));
            }

            public int NormalizeX(int x) => Request.Topology.NormalizeX(x);
            public int NormalizeY(int y) => Request.Topology.NormalizeY(y);

            /// <summary>读取坐标高度；第一次读取后缓存结果，避免重复计算噪声。</summary>
            public double Height(Int2 position)
            {
                return HeightNormalized(Normalize(position));
            }

            /// <summary>河道已归一化坐标直接查高度缓存，避免同一个格再次执行取模。</summary>
            public double HeightNormalized(Int2 position)
            {
                if (!heightCache.TryGetValue(position, out double value))
                {
                    if (sharedHeightPartition != null && sharedHeightPartition.TryGetValue(position, out value))
                    {
                        if (measureSampling) HeightSharedHitCount++;
                    }
                    else
                    {
                        long started = measureSampling ? Stopwatch.GetTimestamp() : 0;
                        value = SampleHeight(Request, Settings, position.X, position.Y);
                        if (measureSampling)
                        {
                            HeightComputeTicks += Stopwatch.GetTimestamp() - started;
                            HeightMissCount++;
                        }
                        newSharedHeightSamples?.Add(new KeyValuePair<Int2, double>(position, value));
                    }
                    heightCache.Add(position, value);
                }
                else if (measureSampling) HeightHitCount++;
                return value;
            }

            // 取消或异常前的局部结果不进入跨区域缓存。
            public void PublishCompletedHeightSamples(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sharedHeightPartition?.PublishCompletedSamples(newSharedHeightSamples, cancellationToken);
            }

            /// <summary>河源只读取第一层海洋距离降水，不反馈最终河湖影响。</summary>
            public double Precipitation(Int2 position)
            {
                position = Normalize(position);
                if (!precipitationCache.TryGetValue(position, out double value))
                {
                    value = SampleOceanPrecipitation(Request, Settings, position.X, position.Y);
                    precipitationCache.Add(position, value);
                }
                return value;
            }

            /// <summary>
            /// 河槽中心线使用的较短尺度横向偏移场。
            /// 它只改变连续中心线在格子里的亚格位置，不参与汇流和水量计算；
            /// 尺度比主蜿蜒场更短，用来打散长距离同方向栅格锁定，但仍保持连续平滑。
            /// </summary>
            public double ChannelCenterBias(Int2 position)
            {
                position = Normalize(position);
                if (channelCenterBiasCache.TryGetValue(position, out double value))
                {
                    if (measureSampling) ChannelCenterBiasHitCount++;
                    return value;
                }
                if (measureSampling) ChannelCenterBiasMissCount++;

                double scale = Math.Max(7d, Settings.RiverMeanderScale * 0.28d);
                double broad = Fractal(
                    CreateSeed(Request, 0xa24baed5u),
                    position.X,
                    position.Y,
                    1d / scale,
                    2,
                    2.07d,
                    0.52d,
                    Request.Topology);
                double detail = Fractal(
                    CreateSeed(Request, 0x9fb21c65u),
                    position.X,
                    position.Y,
                    1d / Math.Max(5d, scale * 0.52d),
                    2,
                    2.13d,
                    0.46d,
                    Request.Topology);
                value = Math.Max(-1d, Math.Min(1d,
                    (broad * 0.68d + detail * 0.32d - 0.5d) * 2.75d));
                channelCenterBiasCache.Add(position, value);
                return value;
            }

        }

        #endregion
    }
}
