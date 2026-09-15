using FlatWorld.Combat;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace FlatWorld.AIECS
{
    /// <summary>稳定统计槽；计数来自实际执行和当前状态，不逐帧写日志。</summary>
    public enum AiecsStatistic { Alive, External, Dead, Buckets, HotBucket, PerceptionRequests, Candidates, Los,
        Targets, Idle, Wander, Chase, Flee, Attack, AttackStarts, Hits, DamageResults, Count }

    /// <summary>生命提交后的表现与工作快照，不反写任何玩法状态。</summary>
    [BurstCompile]
    internal partial struct AiecsCaptureStateJob : IJobEntity
    {
        [NativeDisableParallelForRestriction] public NativeArray<AiecsDisplayRecord> Display;
        [NativeDisableParallelForRestriction] public NativeArray<AiecsWorkCounters> Work;
        public CombatClock Clock;

        /// <summary>按查询索引写出一致状态供渲染、界面和组中心归约消费。</summary>
        private void Execute([EntityIndexInQuery] int index, in AiecsIdentity identity, in AiecsFlowAgent actor,
            in AiecsBody body, in AiecsVital vital, in AiecsBrain brain, in AiecsAttackState attack, in AiecsWorkCounters counters)
        {
            Display[index] = new AiecsDisplayRecord { Key = identity.Key, Position = actor.Position, Facing = body.Facing,
                Hp = vital.Hp, MaxHp = vital.MaxHp, Definition = identity.Definition, Group = identity.Group, Behavior = brain.Behavior,
                AttackPhase = attack.Phase, Dead = vital.Dead, External = identity.External,
                ActionElapsed = (float)(Clock.Time - (vital.Dead != 0 ? vital.DeathTime : brain.EnteredAt)),
                HasTarget = (byte)(brain.Target != Entity.Null ? 1 : 0) };
            Work[index] = counters;
        }
    }

    /// <summary>线性统计和少量组中心归约；循环坐标以首个成员的最近镜像求中心。</summary>
    [BurstCompile]
    internal struct AiecsStatisticsJob : IJob
    {
        [ReadOnly] public NativeArray<AiecsDisplayRecord> Display;
        [ReadOnly] public NativeArray<AiecsWorkCounters> Work;
        public NativeArray<int> Statistics;
        public NativeArray<float3> Groups;
        public NativeArray<float2> Anchors;
        public NativeArray<float> Nearest;
        [ReadOnly] public FlatWorld.Navigation.FlowNavigationSnapshot Navigation;
        public NativeParallelHashMap<int2, int> Density;
        public WorldTopologyDomain Domain;

        /// <summary>汇总工作计数、热点桶、行为和战略中心。</summary>
        public void Execute()
        {
            for (int i = 0; i < Statistics.Length; i++) Statistics[i] = 0;
            for (int i = 0; i < Groups.Length; i++) Groups[i] = float3.zero;
            Density.Clear();
            for (int i = 0; i < Display.Length; i++)
            {
                var actor = Display[i]; var work = Work[i];
                Add(AiecsStatistic.PerceptionRequests, work.PerceptionRequests); Add(AiecsStatistic.Candidates, work.Candidates);
                Add(AiecsStatistic.Los, work.Los); Add(AiecsStatistic.AttackStarts, work.Attacks);
                Add(AiecsStatistic.Hits, work.Hits); Add(AiecsStatistic.DamageResults, work.DamageResults);
                if (actor.Dead != 0) { Add(AiecsStatistic.Dead, 1); continue; }
                int2 key = (int2)math.floor(actor.Position / AiecsSpatialView.BucketSize);
                Density.TryGetValue(key, out int count); Density[key] = ++count;
                Statistics[(int)AiecsStatistic.HotBucket] = math.max(Statistics[(int)AiecsStatistic.HotBucket], count);
                if ((uint)actor.Group < (uint)Groups.Length)
                {
                    float3 group = Groups[actor.Group];
                    if (group.z == 0f) Anchors[actor.Group] = actor.Position;
                    group.xy += Domain.ShortestDelta(Anchors[actor.Group], actor.Position); group.z++;
                    Groups[actor.Group] = group;
                }
                if (actor.External != 0) { Add(AiecsStatistic.External, 1); continue; }
                Add(AiecsStatistic.Alive, 1); Add(AiecsStatistic.Targets, actor.HasTarget);
                if ((uint)actor.Behavior <= (uint)AiecsBehavior.Attack) Add(AiecsStatistic.Idle + actor.Behavior, 1);
            }
            Statistics[(int)AiecsStatistic.Buckets] = Density.Count();
            for (int i = 0; i < Groups.Length; i++)
            {
                float3 group = Groups[i]; if (group.z == 0f) continue;
                group.xy = Domain.Normalize(Anchors[i] + group.xy / group.z); Groups[i] = group;
            }
            // 平均点可能落在墙或未加载格，战略目标选中心附近实际可走成员，避免全组停在无效 Goal。
            for (int i = 0; i < Groups.Length; i++) { Anchors[i] = Groups[i].xy; Nearest[i] = float.PositiveInfinity; }
            for (int i = 0; i < Display.Length; i++)
            {
                var actor = Display[i];
                if (actor.Dead != 0 || (uint)actor.Group >= (uint)Groups.Length || Navigation.CostAt(actor.Position) < 0) continue;
                float distance = math.lengthsq(Domain.ShortestDelta(Anchors[actor.Group], actor.Position));
                if (distance >= Nearest[actor.Group]) continue;
                Nearest[actor.Group] = distance; float3 group = Groups[actor.Group]; group.xy = actor.Position; Groups[actor.Group] = group;
            }
            for (int i = 0; i < Groups.Length; i++) if (float.IsPositiveInfinity(Nearest[i])) Groups[i] = float3.zero;
        }

        /// <summary>向固定槽累加一个真实执行计数。</summary>
        private void Add(AiecsStatistic key, int value) => Statistics[(int)key] += value;
    }
}
