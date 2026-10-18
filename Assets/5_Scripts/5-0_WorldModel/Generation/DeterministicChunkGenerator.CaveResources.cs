using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace FlatWorld.WorldModel
{
    /// <summary>只解析洞穴基础生态，邻区矿山查询不触发散矿阶段。</summary>
    public interface ICaveBaseEcologyResolver
    {
        ChunkEcologyData GetCaveBaseEcology(ChunkGenerationRequest request,
            ChunkTerrainBuffer terrain, CancellationToken cancellationToken);
    }

    public sealed partial class DeterministicChunkGenerator : ICaveBaseEcologyResolver
    {
        #region 洞穴基础生态缓存

        private const int MaximumCachedCaveBaseChunks = 256;
        private readonly ConcurrentDictionary<EcologyHostTagKey, Lazy<ChunkEcologyData>>
            caveBaseEcologyCache = new();
        private readonly Queue<EcologyHostTagKey> caveBaseEcologyOrder = new();
        private readonly object caveBaseEcologyGate = new();

        // 缓存纯基础结果，临近区块共享矿山来源，避免每块重复生成整圈地形。
        public ChunkEcologyData GetCaveBaseEcology(ChunkGenerationRequest request,
            ChunkTerrainBuffer terrain, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfEcologyEpochRetired(request);
            var key = new EcologyHostTagKey(request, "cave.base");
            Lazy<ChunkEcologyData> lazy;
            lock (caveBaseEcologyGate)
            {
                ThrowIfEcologyEpochRetired(request);
                if (!caveBaseEcologyCache.TryGetValue(key, out lazy))
                {
                    lazy = new Lazy<ChunkEcologyData>(() => BuildCaveBaseEcology(
                        request, terrain, cancellationToken),
                        LazyThreadSafetyMode.ExecutionAndPublication);
                    caveBaseEcologyCache.TryAdd(key, lazy);
                    caveBaseEcologyOrder.Enqueue(key);
                    while (caveBaseEcologyOrder.Count > MaximumCachedCaveBaseChunks)
                        caveBaseEcologyCache.TryRemove(caveBaseEcologyOrder.Dequeue(), out _);
                }
            }
            try
            {
                ChunkEcologyData result = lazy.Value;
                cancellationToken.ThrowIfCancellationRequested();
                ThrowIfEcologyEpochRetired(request);
                return result;
            }
            catch
            {
                // 取消或失败不留下异常 Lazy，后续正式请求可以重新计算。
                lock (caveBaseEcologyGate)
                {
                    if (caveBaseEcologyCache.TryGetValue(key, out var current) &&
                        ReferenceEquals(current, lazy))
                        caveBaseEcologyCache.TryRemove(key, out _);
                }
                throw;
            }
        }

        private ChunkEcologyData BuildCaveBaseEcology(ChunkGenerationRequest request,
            ChunkTerrainBuffer terrain, CancellationToken cancellationToken)
        {
            if (terrain != null)
                return CaveGenerationFeatureGenerator.GenerateCaveBase(
                    request, terrain, cancellationToken, this);
            using var neighborTerrain = new ChunkTerrainBuffer(
                request.Profile.Width, request.Profile.Height, liquidTypes);
            PopulateTerrain(request, neighborTerrain, true, cancellationToken, null);
            neighborTerrain.InitializeWeatherProfile(weatherSettings);
            return CaveGenerationFeatureGenerator.GenerateCaveBase(
                request, neighborTerrain, cancellationToken, this);
        }

        private void ClearCaveBaseEcologyCache()
        {
            lock (caveBaseEcologyGate)
            {
                caveBaseEcologyCache.Clear();
                caveBaseEcologyOrder.Clear();
            }
        }

        #endregion
    }
}
