using FlatWorld.Navigation;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace FlatWorld.AIECS
{
    /// <summary>低频错峰感知；锁定有效时不搜桶，单次请求候选数由共享定义限制，密集战场不会平方扫描。</summary>
    [BurstCompile]
    public partial struct AiecsPerceptionSystem : IJobEntity
    {
        [ReadOnly] public NativeArray<AiecsDefinition> Definitions;
        [ReadOnly] public AiecsSpatialView Spatial;
        [ReadOnly] public AiecsLosView Los;
        public double Time; // 当前权威模拟时间。
        public uint Tick; // 密集候选遍历起点的轮转种子。

        /// <summary>维护目标生命周期，只有到期且失去有效锁定时才找敌。</summary>
        private void Execute(Entity entity, in AiecsIdentity identity, in AiecsVital vital, in AiecsFlowAgent actor,
            ref AiecsBrain brain, ref AiecsWorkCounters counters)
        {
            counters = default;
            if (identity.External != 0 || vital.Dead != 0) return;
            AiecsDefinition definition = Definitions[identity.Definition];
            bool valid = Spatial.TryTarget(brain.Target, brain.TargetKey, out AiecsTargetSample target);
            if (!valid) Clear(ref brain);
            if (Time < brain.NextPerception) return;
            brain.NextPerception = Time + definition.PerceptionPeriod;
            bool unreachable = valid && brain.Behavior == (int)AiecsBehavior.Chase && actor.Mode == AiecsMoveMode.SharedGoal &&
                actor.Status == FlowSampleStatus.Unreachable;
            if (unreachable)
            { brain.RejectedTarget = brain.TargetKey; brain.RetryTargetAfter = Time + definition.ChaseRetryDelay; }
            if (valid && Eligible(identity, actor.Position, target, definition.ChaseRange, definition.RequireLos, ref counters) &&
                !unreachable)
            {
                Remember(ref brain, target, definition.MemorySeconds);
                return;
            }
            Clear(ref brain);
            counters.PerceptionRequests++;
            Spatial.BucketRange(actor.Position, new float2(definition.SenseRange + Spatial.MaximumBodyExtent), out int2 first, out int2 size);
            int cells = size.x * size.y;
            uint seed = math.hash(new uint3((uint)entity.Index, (uint)entity.Version, Tick));
            int start = cells > 0 ? (int)(seed % (uint)cells) : 0;
            float best = float.MinValue;
            int budget = definition.CandidateBudget;
            for (int offset = 0; offset < cells && budget > 0; offset++)
            {
                int cell = (start + offset) % cells;
                int2 bucket = Spatial.NormalizeBucket(first + new int2(cell % size.x, cell / size.x));
                for (int faction = 0; faction < Spatial.FactionCount && budget > 0; faction++)
                {
                    if (!Spatial.IsHostile(identity.Faction, faction)) continue;
                    if (!Spatial.Buckets.TryGetFirstValue(new int3(bucket, faction), out int index, out var iterator)) continue;
                    do
                    {
                        --budget; counters.Candidates++;
                        target = Spatial.Samples[index];
                        if (target.Identity.Key == brain.RejectedTarget && Time < brain.RetryTargetAfter) continue;
                        if (target.Entity == entity || !Eligible(identity, actor.Position, target, definition.SenseRange, definition.RequireLos, ref counters)) continue;
                        float distance = math.lengthsq(Spatial.Domain.ShortestDelta(actor.Position, target.Position));
                        float score = 1f / (1f + distance) + (target.Identity.Key == vital.LastAttacker ? 2f : 0f);
                        if (score <= best) continue;
                        best = score; brain.Target = target.Entity; brain.TargetKey = target.Identity.Key;
                        Remember(ref brain, target, definition.MemorySeconds);
                    } while (budget > 0 && Spatial.Buckets.TryGetNextValue(out index, ref iterator));
                }
            }
        }

        /// <summary>统一锁定与新候选的世界、阵营、几何和遮挡规则。</summary>
        private bool Eligible(AiecsIdentity observer, float2 position, AiecsTargetSample target, float range, byte requireLos, ref AiecsWorkCounters counters)
        {
            if (target.Dead != 0 || target.Hp <= 0f || target.Identity.Key.World != observer.Key.World ||
                target.Identity.Key.Dimension != observer.Key.Dimension || !Spatial.IsHostile(observer.Faction, target.Identity.Faction)) return false;
            if (!target.ShapeNear(position, Spatial.Domain, false).IntersectsCircle(position, range)) return false;
            if (requireLos == 0) return true;
            counters.Los++;
            return Los.Visible(position, target.Position);
        }

        /// <summary>威胁位置保留到记忆到期，目标丢失后仍可执行短时逃跑。</summary>
        private void Remember(ref AiecsBrain brain, AiecsTargetSample target, float duration)
        {
            brain.ThreatPosition = target.Position; brain.Threat = 1f; brain.MemoryUntil = Time + duration;
        }

        /// <summary>清除目标引用但保留有限时间的威胁记忆。</summary>
        private static void Clear(ref AiecsBrain brain) { brain.Target = Entity.Null; brain.TargetKey = default; }
    }
}
