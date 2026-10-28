using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace FlatWorld.WorldModel
{
    public sealed partial class DeterministicChunkGenerator
    {
        #region 天然宿主邻区标签缓存

        private const int MaximumCachedEcologyHostTags = 2048;
        private readonly ConcurrentDictionary<EcologyHostTagKey, bool> ecologyHostTagCache = new();
        private readonly Queue<EcologyHostTagKey> ecologyHostTagCacheOrder = new();
        private readonly object ecologyHostTagCacheGate = new();

        /// <summary>只为需要邻域标签的规则复用本次正式地形，避免邻块重新计算河湖。</summary>
        private void CacheEcologyHostTags(ChunkGenerationRequest request,
            ChunkTerrainBuffer terrain, CancellationToken cancellationToken)
        {
            if (request.Profile.EcologyGlobalMultiplier <= 0d)
                return;
            IReadOnlyList<EcologySpawnRuleSnapshot> rules = request.Profile.EcologyRules;
            HashSet<string> visitedTags = null;
            for (int i = 0; i < rules.Count; i++)
            {
                EcologySpawnRuleSnapshot rule = rules[i];
                if (rule == null || !rule.CompanionOnly || rule.RequiredTagChunkRadius == 0 ||
                    string.IsNullOrEmpty(rule.RequiredChunkTag) ||
                    rule.CompanionSpawnChance <= 0d || rule.SpawnChanceMultiplier <= 0d)
                    continue;
                visitedTags ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!visitedTags.Add(rule.RequiredChunkTag))
                    continue;
                var key = new EcologyHostTagKey(request, rule.RequiredChunkTag);
                if (ecologyHostTagCache.ContainsKey(key))
                    continue;
                bool found = ChunkEcologyGenerator.HasHostTagInChunk(request, terrain,
                    request.Profile.EcologyGlobalMultiplier, rules,
                    rule.RequiredChunkTag, cancellationToken);
                StoreEcologyHostTag(key, found, cancellationToken);
            }
        }

        /// <summary>取消和退出世界的旧任务都不能重新写回缓存。</summary>
        private void StoreEcologyHostTag(EcologyHostTagKey key, bool found,
            CancellationToken cancellationToken)
        {
            lock (ecologyHostTagCacheGate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (key.Epoch <= hydrologyRegions.RetiredEpoch)
                    throw new OperationCanceledException("旧世界的生态请求已经失效。");
                if (!ecologyHostTagCache.TryAdd(key, found))
                    return;
                ecologyHostTagCacheOrder.Enqueue(key);
                while (ecologyHostTagCacheOrder.Count > MaximumCachedEcologyHostTags)
                    ecologyHostTagCache.TryRemove(ecologyHostTagCacheOrder.Dequeue(), out _);
            }
        }

        private void ClearEcologyHostTagCache()
        {
            lock (ecologyHostTagCacheGate)
            {
                ecologyHostTagCache.Clear();
                ecologyHostTagCacheOrder.Clear();
            }
        }

        private void ThrowIfEcologyEpochRetired(ChunkGenerationRequest request)
        {
            if (request.WorldEpoch <= hydrologyRegions.RetiredEpoch)
                throw new OperationCanceledException("旧世界的生态请求已经失效。");
        }

        private readonly struct EcologyHostTagKey : IEquatable<EcologyHostTagKey>
        {
            public EcologyHostTagKey(ChunkGenerationRequest request, string tag)
            {
                Epoch = request.WorldEpoch;
                address = request.Address;
                worldSeed = request.WorldSeed;
                profileFingerprint = request.Profile.GenerationFingerprint;
                wrapped = request.Topology.IsWrapped;
                topologyMin = request.Topology.Min;
                topologySpan = request.Topology.Span;
                this.tag = tag;
            }

            public long Epoch { get; }
            private readonly WorldAddress address;
            private readonly int worldSeed;
            private readonly ulong profileFingerprint;
            private readonly bool wrapped;
            private readonly Int2 topologyMin;
            private readonly Int2 topologySpan;
            private readonly string tag;

            public bool Equals(EcologyHostTagKey other) =>
                Epoch == other.Epoch && address.Equals(other.address) &&
                worldSeed == other.worldSeed && profileFingerprint == other.profileFingerprint &&
                wrapped == other.wrapped && topologyMin.Equals(other.topologyMin) &&
                topologySpan.Equals(other.topologySpan) &&
                string.Equals(tag, other.tag, StringComparison.OrdinalIgnoreCase);

            public override bool Equals(object obj) => obj is EcologyHostTagKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = Epoch.GetHashCode();
                    hash = hash * 397 ^ address.GetHashCode();
                    hash = hash * 397 ^ worldSeed;
                    hash = hash * 397 ^ profileFingerprint.GetHashCode();
                    hash = hash * 397 ^ wrapped.GetHashCode();
                    hash = hash * 397 ^ topologyMin.GetHashCode();
                    hash = hash * 397 ^ topologySpan.GetHashCode();
                    return hash * 397 ^ (tag == null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(tag));
                }
            }
        }

        #endregion
    }
}
