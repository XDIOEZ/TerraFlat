using System.Collections.Generic;
using FlatWorld.Combat;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace FlatWorld.AIECS
{
    #region 命中路由
    /// <summary>确定性目标/时间/攻击排序；同一目标由一个结算 Job 实例顺序处理。</summary>
    public struct AiecsHitComparer : IComparer<AiecsHitEvent>
    {
        /// <summary>先按目标分组，再按模拟时间和攻击身份排出稳定顺序。</summary>
        public int Compare(AiecsHitEvent left, AiecsHitEvent right)
        {
            int result = left.Target.Index.CompareTo(right.Target.Index);
            if (result == 0) result = left.Target.Version.CompareTo(right.Target.Version);
            if (result == 0) result = left.Context.Clock.Time.CompareTo(right.Context.Clock.Time);
            if (result == 0) result = left.Context.Attack.Source.CompareTo(right.Context.Attack.Source);
            if (result == 0) result = left.Context.Attack.Sequence.CompareTo(right.Context.Attack.Sequence);
            if (result == 0) result = left.Context.Attack.Window.CompareTo(right.Context.Attack.Window);
            if (result == 0) result = left.Context.Attack.Pulse.CompareTo(right.Context.Attack.Pulse);
            return result;
        }
    }

    /// <summary>收集攻击、周期和 Bridge Pulse；只对实际命中排序，空槽仅做线性过滤。</summary>
    [BurstCompile]
    internal struct AiecsRouteHitsJob : IJob
    {
        [ReadOnly] public NativeArray<AiecsHitEvent> Attacks, External;
        [ReadOnly] public NativeArray<int> PeriodicCounts;
        public NativeArray<int> TotalCount;
        public NativeQueue<AiecsHitEvent> Abilities;
        public NativeList<AiecsHitEvent> Hits;
        public NativeParallelHashMap<Entity, int2> Ranges;

        /// <summary>合并纯值命中并建立每个目标的连续结算范围。</summary>
        public void Execute()
        {
            Hits.Clear(); Ranges.Clear();
            Append(Attacks); Append(External);
            while (Abilities.TryDequeue(out var abilityHit)) if (abilityHit.Target != Entity.Null) Hits.Add(abilityHit);
            if (Ranges.Capacity < Hits.Length) Ranges.Capacity = Hits.Length;
            int total = Hits.Length;
            for (int i = 0; i < PeriodicCounts.Length; i++) total += PeriodicCounts[i];
            TotalCount[0] = total;
            Hits.AsArray().Sort(new AiecsHitComparer());
            int start = 0;
            while (start < Hits.Length)
            {
                int end = start + 1;
                while (end < Hits.Length && Hits[end].Target == Hits[start].Target) end++;
                Ranges.Add(Hits[start].Target, new int2(start, end - start)); start = end;
            }
        }

        /// <summary>只添加实际生效的目标事件，预留容量由批次所有者保证。</summary>
        private void Append(NativeArray<AiecsHitEvent> source)
        {
            for (int i = 0; i < source.Length; i++) if (source[i].Target != Entity.Null) Hits.AddNoResize(source[i]);
        }
    }
    #endregion

    #region 生命结算
    /// <summary>正式 ECS 生命权威：四类伤害、防御、难度、部位、间隔、Buff 和慢速在同一目标写入域提交。</summary>
    [BurstCompile]
    public partial struct AiecsDamageSettlementSystem : IJobEntity
    {
        [ReadOnly] public NativeArray<AiecsHitEvent> Hits;
        [ReadOnly] public NativeParallelHashMap<Entity, int2> Ranges;
        [ReadOnly] public NativeArray<FixedString128Bytes> Factions;
        [ReadOnly] public NativeArray<AiecsDefinition> Definitions;
        [ReadOnly] public NativeArray<AiecsBuffDefinition> BuffDefinitions;
        [ReadOnly] public AiecsSpatialView Spatial;
        public NativeList<AiecsDamageResult>.ParallelWriter Results;
        public NativeList<AiecsHitEvent>.ParallelWriter ExternalHits;
        public CombatDifficulty Difficulty;

        /// <summary>每个目标串行处理自己的命中；其它目标可安全并行，外部生命只输出给 Bridge。</summary>
        private void Execute(Entity entity, in AiecsIdentity identity, in AiecsDefense defense,
            ref AiecsVital vital, ref AiecsAnatomy anatomy, ref AiecsStatus status,
            ref AiecsBrain brain, ref AiecsWorkCounters counters, ref DynamicBuffer<AiecsBuff> buffs,
            in DynamicBuffer<AiecsPeriodicHit> periodic)
        {
            Ranges.TryGetValue(entity, out int2 range);
            if (range.y == 0 && periodic.Length == 0) return;
            var random = new Random(brain.RandomState == 0 ? 1u : brain.RandomState);
            CombatAttackKey previous = default; bool hasPrevious = false;
            for (int i = 0; i < range.y + periodic.Length; i++)
            {
                AiecsHitEvent hit = i < range.y ? Hits[range.x + i] : periodic[i - range.y].Hit;
                CombatDamageContext context = hit.Context;
                if (hasPrevious && previous.Equals(context.Attack)) continue;
                previous = context.Attack; hasPrevious = true;
                if (identity.Key != hit.TargetKey || !Allowed(identity, context)) continue;
                if (identity.External != 0) { ExternalHits.AddNoResize(hit); continue; }
                float loss = -1f; float4 typed = float4.zero; byte fatal = 0;
                if (vital.Dead == 0 && vital.Hp > 0f && (context.IsTrueDamage != 0 ||
                    context.Clock.Time - vital.LastDamageTime >= vital.DamageInterval))
                {
                    if (context.IsTrueDamage == 0) vital.LastDamageTime = context.Clock.Time;
                    float4 damage = context.IsTrueDamage != 0 ? math.max(float4.zero, context.Damage) :
                        CombatRules.Resolve(context.Damage, defense.Values,
                            Difficulty.Resolve(context.SourceIsPlayer != 0, identity.Player != 0), vital.ReceivedMultiplier);
                    float amount = math.csum(damage);
                    float before = vital.Hp;
                    if (amount > 0f)
                    {
                        if (anatomy.Parts.Length == 0) vital.Hp = math.max(0f, vital.Hp - amount);
                        else ApplyBodyDamage(ref anatomy, ref vital, amount, context.IsTrueDamage != 0, ref random);
                    }
                    loss = before - vital.Hp; typed = CombatRules.ActualLoss(damage, loss);
                    if (context.IsTrueDamage == 0)
                    {
                        if (loss > 0f || random.NextFloat() < 0.1f)
                        { vital.LastAttacker = context.Attack.Source; vital.LastCredit = context.Credit; }
                        if (loss > 0f)
                        {
                            brain.ThreatPosition = context.Origin; brain.Threat = 1f;
                            brain.MemoryUntil = math.max(brain.MemoryUntil, context.Clock.Time + Definitions[identity.Definition].MemorySeconds);
                        }
                        if (loss > 0f && vital.Hp > 0f && context.SlowEnabled != 0)
                        { status.MoveMultiplier = math.clamp(context.SlowMultiplier, 0.01f, 1f); status.SlowUntil = context.Clock.Time + context.SlowDuration; }
                        int tier = CombatRules.BleedingTier(typed, identity.HasBlood != 0, vital.Hp > 0f, random.NextFloat());
                        if (tier > 0) AiecsBuffOperations.ApplyBleeding(ref buffs, BuffDefinitions, tier, context.Clock.Time, context.Credit);
                        if (vital.Hp > 0f)
                            foreach (CombatOnHitBuff effect in context.OnHitBuffs)
                            {
                                if (effect.Chance < 1f && random.NextFloat() >= effect.Chance) continue;
                                for (int buff = 0; buff < BuffDefinitions.Length; buff++)
                                    if (BuffDefinitions[buff].Id.Equals(effect.Id))
                                    { AiecsBuffOperations.Add(ref buffs, BuffDefinitions, buff, context.Clock.Time, context.Credit, math.max(1, effect.Stacks)); break; }
                            }
                    }
                    if (vital.Hp <= 0f)
                    {
                        vital.Dead = 1; vital.DeathTime = context.Clock.Time;
                        if (context.Credit.IsValid) vital.LastCredit = context.Credit;
                        fatal = 1;
                    }
                }
                Results.AddNoResize(new AiecsDamageResult { Target = identity.Key, Context = context,
                    Loss = loss, Hp = vital.Hp, TypedLoss = typed, Fatal = fatal }); counters.DamageResults++;
            }
            brain.RandomState = random.state;
        }

        /// <summary>来源身份、世界和阵营均有效；环境周期仅允许显式真实伤害分支。</summary>
        private bool Allowed(AiecsIdentity target, CombatDamageContext context)
        {
            if (!context.Attack.Source.IsValid || context.Attack.Source.World != target.Key.World ||
                context.Attack.Source.Dimension != target.Key.Dimension || context.Attack.Source == target.Key ||
                !math.all(math.isfinite(context.Damage)) || !math.isfinite(context.Clock.Time)) return false;
            if (context.Attack.Source.Backend == CombatBackend.Environment) return context.IsTrueDamage != 0;
            for (int i = 0; i < Factions.Length; i++) if (Factions[i].Equals(context.Faction)) return Spatial.IsHostile(i, target.Faction);
            return false;
        }

        /// <summary>普通攻击按面积权重随机命中一至两个部位；周期真实伤害按各部位剩余生命分摊。</summary>
        private static void ApplyBodyDamage(ref AiecsAnatomy anatomy, ref AiecsVital vital, float damage, bool fullBody, ref Random random)
        {
            if (fullBody)
            {
                float ratio = math.max(0f, vital.Hp - damage) / math.max(0.0001f, vital.Hp);
                for (int i = 0; i < anatomy.Parts.Length; i++)
                { AiecsBodyPart part = anatomy.Parts[i]; part.Hp *= ratio; anatomy.Parts[i] = part; }
            }
            else
            {
                int first = Pick(anatomy, -1, ref random);
                int second = random.NextFloat() < anatomy.TwoPartChance ? Pick(anatomy, first, ref random) : -1;
                float portion = second >= 0 ? damage * 0.5f : damage;
                HurtPart(ref anatomy, first, portion); HurtPart(ref anatomy, second, portion);
            }
            vital.Hp = 0f;
            for (int i = 0; i < anatomy.Parts.Length; i++) vital.Hp += anatomy.Parts[i].Hp;
        }

        /// <summary>无可用面积权重时复用旧后端的最高剩余生命回退。</summary>
        private static int Pick(AiecsAnatomy anatomy, int excluded, ref Random random)
        {
            float total = 0f, hp = -1f; int fallback = -1;
            for (int i = 0; i < anatomy.Parts.Length; i++)
            {
                AiecsBodyPart part = anatomy.Parts[i]; if (i == excluded || part.Hp <= 0f) continue;
                total += math.max(0f, part.Weight); if (part.Hp > hp) { hp = part.Hp; fallback = i; }
            }
            if (total <= 0f) return fallback;
            float value = random.NextFloat(total);
            for (int i = 0; i < anatomy.Parts.Length; i++)
            {
                var part = anatomy.Parts[i]; if (i == excluded || part.Hp <= 0f) continue;
                value -= math.max(0f, part.Weight); if (value <= 0f) return i;
            }
            return fallback;
        }

        /// <summary>部位损失截到零，多余伤害不重新扩散到其它部位。</summary>
        private static void HurtPart(ref AiecsAnatomy anatomy, int index, float damage)
        {
            if (index < 0) return;
            var part = anatomy.Parts[index]; part.Hp = math.max(0f, part.Hp - damage); anatomy.Parts[index] = part;
        }
    }
    #endregion

    #region 死亡发布
    /// <summary>只在生命提交后发布一次死亡和掉落请求；先锁存状态，再让 Bridge 消费。</summary>
    [BurstCompile]
    public partial struct AiecsDeathSystem : IJobEntity
    {
        public NativeList<AiecsDeathEvent>.ParallelWriter Events;
        public CombatClock Clock;

        /// <summary>死亡实体立即停止移动并清理目标，尸体短时保留供表现读取。</summary>
        private void Execute(Entity entity, in AiecsIdentity identity, ref AiecsVital vital, ref AiecsFlowAgent actor, ref AiecsBrain brain)
        {
            if (identity.External != 0 || vital.Dead == 0) return;
            actor.Mode = AiecsMoveMode.Hold; actor.Velocity = float2.zero;
            brain.Target = Entity.Null; brain.TargetKey = default;
            if (vital.DeathPublished != 0) return;
            vital.DeathPublished = 1; vital.LootPublished = 1;
            Events.AddNoResize(new AiecsDeathEvent { Entity = entity, Actor = identity.Key, Killer = vital.LastCredit,
                Position = actor.Position, Definition = identity.Definition, Clock = Clock });
        }
    }
    #endregion
}
