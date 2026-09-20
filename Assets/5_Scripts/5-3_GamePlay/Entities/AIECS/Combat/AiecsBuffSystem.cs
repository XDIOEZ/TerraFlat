using FlatWorld.Combat;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace FlatWorld.AIECS
{
    /// <summary>共享 Buff 定义的周期执行器；本轮编译真实伤害和失水，伤害仍进入统一生命结算。</summary>
    [BurstCompile]
    public partial struct AiecsBuffSystem : IJobEntity
    {
        [ReadOnly] public NativeArray<AiecsBuffDefinition> Definitions;
        [NativeDisableParallelForRestriction] public NativeArray<int> HitCounts;
        public CombatClock Clock;

        /// <summary>按各实例时钟推进周期与到期，避免使用渲染帧时间合并多个模拟 Tick。</summary>
        private void Execute([EntityIndexInQuery] int index, Entity entity, in AiecsIdentity identity, in AiecsVital vital,
            in AiecsFlowAgent actor, ref AiecsStatus status, ref DynamicBuffer<AiecsBuff> buffs, ref DynamicBuffer<AiecsPeriodicHit> output)
        {
            output.Clear(); HitCounts[index] = 0;
            if (identity.External != 0 || vital.Dead != 0) return;
            if (Clock.Time >= status.SlowUntil) status.MoveMultiplier = 1f;
            status.BleedingTier = 0;
            for (int i = buffs.Length - 1; i >= 0; i--)
            {
                AiecsBuff buff = buffs[i]; AiecsBuffDefinition definition = Definitions[buff.Definition];
                double limit = math.min(Clock.Time, buff.Expires);
                if (definition.Interval > 0f && buff.NextTick <= limit)
                {
                    int ticks = 1 + (int)math.floor((limit - buff.NextTick) / definition.Interval);
                    status.Water = math.max(0f, status.Water + definition.WaterDelta * ticks);
                    float damage = definition.TrueDamage + definition.TrueDamagePerStack * math.max(1, buff.Stacks);
                    if (damage > 0f)
                    {
                        var source = identity.Key; source.Backend = CombatBackend.Environment;
                        output.Add(new AiecsPeriodicHit { Hit = new AiecsHitEvent { Target = entity, TargetKey = identity.Key,
                            Context = new CombatDamageContext { Attack = new CombatAttackKey { Source = source,
                                Sequence = (uint)Clock.Tick, Window = (uint)(Clock.Tick >> 32), Pulse = (uint)buff.Definition + 1 },
                                Credit = buff.Credit, Clock = Clock, Damage = new float4(0, 0, 0, damage * ticks), IsTrueDamage = 1,
                                Origin = actor.Position, HitPoint = actor.Position, BuildingMultiplier = 1f } } });
                    }
                    buff.NextTick += ticks * definition.Interval;
                }
                if (Clock.Time >= buff.Expires) { buffs.RemoveAtSwapBack(i); continue; }
                buffs[i] = buff; status.BleedingTier = math.max(status.BleedingTier, definition.BleedingTier);
            }
            HitCounts[index] = output.Length;
        }
    }

    /// <summary>可复用的状态操作；出血互斥等级与刷新规则来自当前内容定义。</summary>
    public static class AiecsBuffOperations
    {
        /// <summary>同级按配置处理，高级替换低级，较低出血刷新已有高级出血。</summary>
        public static void ApplyBleeding(ref DynamicBuffer<AiecsBuff> buffs, NativeArray<AiecsBuffDefinition> definitions,
            int tier, double time, CombatIdentity credit)
        {
            int selected = -1;
            for (int i = 0; i < definitions.Length; i++) if (definitions[i].BleedingTier == tier) { selected = i; break; }
            if (selected < 0) return;
            for (int i = buffs.Length - 1; i >= 0; i--)
            {
                AiecsBuff current = buffs[i]; var definition = definitions[current.Definition];
                if (definition.BleedingTier == 0) continue;
                if (definition.BleedingTier >= tier)
                {
                    Add(ref buffs, definitions, current.Definition, time, credit); return;
                }
                buffs.RemoveAtSwapBack(i);
            }
            Add(ref buffs, definitions, selected, time, credit);
        }

        /// <summary>按共享定义的 Ignore/Extend/Refresh 规则增加任意已编译 Buff。</summary>
        public static void Add(ref DynamicBuffer<AiecsBuff> buffs, NativeArray<AiecsBuffDefinition> definitions,
            int index, double time, CombatIdentity credit)
        {
            Add(ref buffs, definitions, index, time, credit, 1);
        }

        /// <summary>一次应用完整层数，刷新持续时间但不重置伤害时钟。</summary>
        public static void Add(ref DynamicBuffer<AiecsBuff> buffs, NativeArray<AiecsBuffDefinition> definitions,
            int index, double time, CombatIdentity credit, int stacks)
        {
            if (stacks <= 0) return;
            AiecsBuffDefinition definition = definitions[index];
            int maxStacks = math.max(1, definition.MaxStacks);
            for (int i = 0; i < buffs.Length; i++)
            {
                if (buffs[i].Definition != index) continue;
                AiecsBuff current = buffs[i];
                if (current.Expires <= time)
                {
                    buffs.RemoveAtSwapBack(i);
                    break;
                }
                if (definition.StackMode == 0) return;
                if (definition.StackMode == 3)
                {
                    int added = math.min(maxStacks, stacks);
                    current.Stacks = math.min(maxStacks - added, math.max(1, current.Stacks)) + added;
                }
                current.Expires = definition.StackMode == 1 ? current.Expires + definition.Duration : time + definition.Duration;
                current.Credit = credit; buffs[i] = current; return;
            }
            buffs.Add(new AiecsBuff { Definition = index, Stacks = math.min(maxStacks, stacks), Expires = time + definition.Duration,
                NextTick = time + definition.Interval, Credit = credit });
        }
    }
}
