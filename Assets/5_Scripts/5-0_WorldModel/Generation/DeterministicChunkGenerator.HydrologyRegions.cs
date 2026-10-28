using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace FlatWorld.WorldModel
{
    public sealed partial class DeterministicChunkGenerator
    {
        #region 水文区域缓存所有权

        private readonly HeightDrivenHydrologyRegionCache hydrologyRegions = new();
        internal int CachedHeightDrivenRegionCount => hydrologyRegions.Count;

        // 区域缓存独占纪元取消、共享 Lazy 和有界淘汰，查询只借已完成的结果。
        private sealed class HeightDrivenHydrologyRegionCache
        {
            private readonly ConcurrentDictionary<HeightDrivenRegionKey, Lazy<MacroHydrologyRegion>>
                heightDrivenRegionCache = new();
            private readonly ConcurrentQueue<HeightDrivenRegionKey> heightDrivenCacheOrder = new();
            private CancellationTokenSource sharedHydrologyCancellation = new();
            private long retiredWorldEpoch = long.MinValue;

            internal int Count => heightDrivenRegionCache.Count;
            internal long RetiredEpoch => Volatile.Read(ref retiredWorldEpoch);
            internal CancellationToken LifetimeToken => sharedHydrologyCancellation.Token;

            internal void RetireWorld(long epoch)
            {
                Volatile.Write(ref retiredWorldEpoch, epoch);
                CancellationTokenSource previous = Interlocked.Exchange(ref sharedHydrologyCancellation,
                    new CancellationTokenSource());
                previous.Cancel();
                heightDrivenRegionCache.Clear();
                while (heightDrivenCacheOrder.TryDequeue(out _)) { }
            }

            internal void UseSearchLifetimeAndCompletedRegions(CancellationTokenSource lifetime,
                HeightDrivenHydrologyRegionCache source)
            {
                sharedHydrologyCancellation.Dispose();
                sharedHydrologyCancellation = lifetime;
                foreach (var pair in source.heightDrivenRegionCache)
                {
                    if (!pair.Value.IsValueCreated) continue;
                    heightDrivenRegionCache.TryAdd(pair.Key, pair.Value);
                    heightDrivenCacheOrder.Enqueue(pair.Key);
                }
            }

            internal object GetPendingGroupKey(ChunkGenerationRequest request)
            {
                ChunkGenerationSettingsSnapshot settings = request.Profile.Settings;
                if (!settings.RiverEnabled || settings.Mode == ChunkGenerationMode.Cave)
                    return null;

                HeightDrivenRegionDescriptor region = ResolveHeightDrivenRegion(request, settings);
                HeightDrivenRegionKey key = CreateHeightDrivenRegionKey(request, region);
                return heightDrivenRegionCache.TryGetValue(key, out Lazy<MacroHydrologyRegion> cached) &&
                       cached.IsValueCreated ? null : key;
            }

            /// <summary>河流定位和正式区块生成共享宏观河网入口。</summary>
            internal MacroHydrologyRegion GetOrGenerateRegion(ChunkGenerationRequest request,
                ChunkGenerationSettingsSnapshot settings, CancellationToken cancellationToken,
                ChunkGenerationTiming timing = null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (request.WorldEpoch <= Volatile.Read(ref retiredWorldEpoch))
                    throw new OperationCanceledException("旧世界的水文请求已经失效。");
                HeightDrivenRegionDescriptor region = ResolveHeightDrivenRegion(request, settings);
                HeightDrivenRegionKey key = CreateHeightDrivenRegionKey(request, region);
                if (!heightDrivenRegionCache.TryGetValue(key, out Lazy<MacroHydrologyRegion> shared))
                {
                    Lazy<MacroHydrologyRegion> candidate = CreateRegionCandidate(request, settings, region,
                        sharedHydrologyCancellation.Token);
                    shared = heightDrivenRegionCache.GetOrAdd(key, candidate);
                    if (ReferenceEquals(shared, candidate))
                    {
                        heightDrivenCacheOrder.Enqueue(key);
                        TrimHeightDrivenRegionCache(settings.RiverMaxCachedRegions);
                    }
                }

                try
                {
                    // region_get 包含共享等待；有 region_compute 的请求才真正执行了区域计算。
                    MacroHydrologyRegion macro;
                    using (timing?.MeasureStage("river.macro_get") ?? default)
                        macro = shared.Value;
                    cancellationToken.ThrowIfCancellationRequested();
                    if (request.WorldEpoch <= Volatile.Read(ref retiredWorldEpoch))
                        throw new OperationCanceledException("旧世界的水文请求已经失效。");
                    return macro;
                }
                catch
                {
                    if (!shared.IsValueCreated &&
                        heightDrivenRegionCache.TryGetValue(key, out Lazy<MacroHydrologyRegion> failed) &&
                        ReferenceEquals(failed, shared))
                        heightDrivenRegionCache.TryRemove(key, out _);
                    throw;
                }
            }

            // 闭包只在冷路径工厂创建，热命中不为捕获方法参数生成显示类。
            private static Lazy<MacroHydrologyRegion> CreateRegionCandidate(ChunkGenerationRequest request,
                ChunkGenerationSettingsSnapshot settings, HeightDrivenRegionDescriptor region, CancellationToken token) =>
                new(() =>
                {
                    using (ChunkGenerationTiming.CurrentOnThread?.MeasureStage("river.macro_compute") ?? default)
                        return BuildMacroHydrologyRegion(CreateHeightDrivenRegionRequest(request, region), settings, token);
                }, LazyThreadSafetyMode.ExecutionAndPublication);

            /// <summary>只在靠近区域边界时预热相邻低分辨率图，后台任务共享世界纪元取消。</summary>
            internal void QueueNeighbourPrewarm(ChunkGenerationRequest request,
                ChunkGenerationSettingsSnapshot settings, HeightDrivenRegionDescriptor region)
            {
                int x = request.Address.ChunkOrigin.X;
                int y = request.Address.ChunkOrigin.Y;
                int distance = Math.Max(request.Profile.Width, request.Profile.Height);
                if (x - region.Origin.X <= distance)
                    TryQueueHydrologyPrewarm(request, settings, region, -1, 0);
                if (region.Origin.X + region.Width - x - request.Profile.Width <= distance)
                    TryQueueHydrologyPrewarm(request, settings, region, 1, 0);
                if (y - region.Origin.Y <= distance)
                    TryQueueHydrologyPrewarm(request, settings, region, 0, -1);
                if (region.Origin.Y + region.Height - y - request.Profile.Height <= distance)
                    TryQueueHydrologyPrewarm(request, settings, region, 0, 1);
            }

            private void TryQueueHydrologyPrewarm(ChunkGenerationRequest request,
                ChunkGenerationSettingsSnapshot settings, HeightDrivenRegionDescriptor region,
                int offsetX, int offsetY)
            {
                if (request.WorldEpoch <= Volatile.Read(ref retiredWorldEpoch)) return;
                if (heightDrivenRegionCache.Count >= settings.RiverMaxCachedRegions)
                    return;
                int nextX = request.Topology.NormalizeX(region.Origin.X + offsetX *
                    settings.RiverHydrologyRegionSize);
                int nextY = request.Topology.NormalizeY(region.Origin.Y + offsetY *
                    settings.RiverHydrologyRegionSize);
                var adjacentRequest = new ChunkGenerationRequest(request.WorldEpoch,
                    new WorldAddress(request.Address.DimensionId, new Int2(nextX, nextY)),
                    request.WorldSeed, request.RequestVersion, request.Profile, request.Topology);
                HeightDrivenRegionDescriptor adjacent = ResolveHeightDrivenRegion(adjacentRequest, settings);
                HeightDrivenRegionKey key = CreateHeightDrivenRegionKey(adjacentRequest, adjacent);
                if (heightDrivenRegionCache.ContainsKey(key)) return;
                CancellationToken token = sharedHydrologyCancellation.Token;
                var candidate = new Lazy<MacroHydrologyRegion>(() =>
                {
                    if (request.WorldEpoch <= Volatile.Read(ref retiredWorldEpoch))
                        throw new OperationCanceledException("旧世界的水文预热已经失效。");
                    return BuildMacroHydrologyRegion(
                        CreateHeightDrivenRegionRequest(adjacentRequest, adjacent), settings, token);
                },
                    LazyThreadSafetyMode.ExecutionAndPublication);
                if (!heightDrivenRegionCache.TryAdd(key, candidate)) return;
                heightDrivenCacheOrder.Enqueue(key);
                _ = Task.Run(() =>
                {
                    try { _ = candidate.Value; }
                    catch
                    {
                        if (heightDrivenRegionCache.TryGetValue(key, out Lazy<MacroHydrologyRegion> current) &&
                            ReferenceEquals(current, candidate))
                            heightDrivenRegionCache.TryRemove(key, out _);
                    }
                });
            }
            /// <summary>把缓存限制在配置上限内，优先移除最早加入的区域。</summary>
            private void TrimHeightDrivenRegionCache(int maximumRegions)
            {
                maximumRegions = Math.Max(1, maximumRegions);
                while (heightDrivenRegionCache.Count > maximumRegions &&
                       heightDrivenCacheOrder.TryDequeue(out HeightDrivenRegionKey oldest))
                {
                    if (!heightDrivenRegionCache.TryGetValue(oldest, out Lazy<MacroHydrologyRegion> entry))
                        continue;
                    if (!entry.IsValueCreated)
                    {
                        heightDrivenCacheOrder.Enqueue(oldest);
                        break;
                    }
                    heightDrivenRegionCache.TryRemove(oldest, out _);
                }
            }
        }

        /// <summary>按有限世界左下角或无限世界原点，把区块归入稳定的水文区域。</summary>
        private static HeightDrivenRegionDescriptor ResolveHeightDrivenRegion(
            ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings)
        {
            int size = settings.RiverHydrologyRegionSize;
            int anchorX = request.Topology.IsWrapped ? request.Topology.Min.X : 0;
            int anchorY = request.Topology.IsWrapped ? request.Topology.Min.Y : 0;
            int normalizedX = request.Topology.NormalizeX(request.Address.ChunkOrigin.X);
            int normalizedY = request.Topology.NormalizeY(request.Address.ChunkOrigin.Y);
            int originX = anchorX + FloorDiv(normalizedX - anchorX, size) * size;
            int originY = anchorY + FloorDiv(normalizedY - anchorY, size) * size;
            int width = size;
            int height = size;
            if (request.Topology.IsWrapped)
            {
                width = Math.Min(size,
                    request.Topology.Min.X + request.Topology.Span.X - originX);
                height = Math.Min(size,
                    request.Topology.Min.Y + request.Topology.Span.Y - originY);
            }
            return new HeightDrivenRegionDescriptor(
                new Int2(originX, originY), Math.Max(1, width), Math.Max(1, height));
        }

        /// <summary>调度分组与实际河网缓存共用完全相同的区域身份。</summary>
        private static HeightDrivenRegionKey CreateHeightDrivenRegionKey(
            ChunkGenerationRequest request, HeightDrivenRegionDescriptor region)
        {
            return new HeightDrivenRegionKey(
                request.WorldEpoch, request.Address.DimensionId, request.WorldSeed,
                request.Profile.GenerationFingerprint, request.Topology,
                region.Origin, region.Width, region.Height);
        }

        /// <summary>复制原配置，仅把生成范围扩大为水文区域，不读取任何 Unity 对象。</summary>
        private static ChunkGenerationRequest CreateHeightDrivenRegionRequest(
            ChunkGenerationRequest source,
            HeightDrivenRegionDescriptor region)
        {
            var numbers = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, double> pair in source.Profile.NumericParameters)
                numbers.Add(pair.Key, pair.Value);
            var texts = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> pair in source.Profile.TextParameters)
                texts.Add(pair.Key, pair.Value);
            var profile = new ChunkGenerationProfileSnapshot(
                source.Profile.ProfileId,
                source.Profile.Signature,
                region.Width,
                region.Height,
                numbers,
                texts);
            return new ChunkGenerationRequest(
                source.WorldEpoch,
                new WorldAddress(source.Address.DimensionId, region.Origin),
                source.WorldSeed,
                source.RequestVersion,
                profile,
                source.Topology);
        }
        /// <summary>水文区域的绝对原点和实际尺寸。</summary>
        private readonly struct HeightDrivenRegionDescriptor
        {
            public HeightDrivenRegionDescriptor(Int2 origin, int width, int height)
            {
                Origin = origin;
                Width = width;
                Height = height;
            }

            public Int2 Origin { get; }
            public int Width { get; }
            public int Height { get; }
        }

        /// <summary>隔离世界、种子、配置、拓扑和区域的新版水文缓存键。</summary>
        private readonly struct HeightDrivenRegionKey : IEquatable<HeightDrivenRegionKey>
        {
            public HeightDrivenRegionKey(long epoch, string dimensionId, int worldSeed,
                ulong profileFingerprint, ChunkGenerationTopologySnapshot topology,
                Int2 origin, int width, int height)
            {
                Epoch = epoch;
                DimensionId = dimensionId;
                WorldSeed = worldSeed;
                ProfileFingerprint = profileFingerprint;
                IsWrapped = topology.IsWrapped;
                TopologyMin = topology.Min;
                TopologySpan = topology.Span;
                Origin = origin;
                Width = width;
                Height = height;
            }

            private long Epoch { get; }
            private string DimensionId { get; }
            private int WorldSeed { get; }
            private ulong ProfileFingerprint { get; }
            private bool IsWrapped { get; }
            private Int2 TopologyMin { get; }
            private Int2 TopologySpan { get; }
            private Int2 Origin { get; }
            private int Width { get; }
            private int Height { get; }

            public bool Equals(HeightDrivenRegionKey other) =>
                Epoch == other.Epoch &&
                string.Equals(DimensionId, other.DimensionId, StringComparison.Ordinal) &&
                WorldSeed == other.WorldSeed &&
                ProfileFingerprint == other.ProfileFingerprint &&
                IsWrapped == other.IsWrapped &&
                TopologyMin.Equals(other.TopologyMin) &&
                TopologySpan.Equals(other.TopologySpan) &&
                Origin.Equals(other.Origin) &&
                Width == other.Width &&
                Height == other.Height;

            public override bool Equals(object obj) =>
                obj is HeightDrivenRegionKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = Epoch.GetHashCode();
                    hash = hash * 397 ^ StringComparer.Ordinal.GetHashCode(DimensionId);
                    hash = hash * 397 ^ WorldSeed;
                    hash = hash * 397 ^ ProfileFingerprint.GetHashCode();
                    hash = hash * 397 ^ IsWrapped.GetHashCode();
                    hash = hash * 397 ^ TopologyMin.GetHashCode();
                    hash = hash * 397 ^ TopologySpan.GetHashCode();
                    hash = hash * 397 ^ Origin.GetHashCode();
                    hash = hash * 397 ^ Width;
                    return hash * 397 ^ Height;
                }
            }
        }

        #endregion
    }
}
