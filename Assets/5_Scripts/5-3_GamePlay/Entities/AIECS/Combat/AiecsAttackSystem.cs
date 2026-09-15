using FlatWorld.Combat;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace FlatWorld.AIECS
{
    /// <summary>攻击时钟权威；前摇锁定目标/朝向，Active Tick 重新验目标和几何，每个 Sequence 只发一个普通攻击 Pulse。</summary>
    [BurstCompile]
    public partial struct AiecsAttackSystem : IJobEntity
    {
        [ReadOnly] public NativeArray<AiecsDefinition> Definitions;
        [ReadOnly] public NativeArray<FixedString128Bytes> Factions;
        [ReadOnly] public AiecsSpatialView Spatial;
        [ReadOnly] public AiecsLosView Los;
        [NativeDisableParallelForRestriction] public NativeArray<AiecsHitEvent> Hits;
        public CombatClock Clock;

        /// <summary>推进有限阶段并在真正生效时提交纯值事件；动画和 Bridge 不决定命中。</summary>
        private void Execute([EntityIndexInQuery] int index, in AiecsIdentity identity, in AiecsVital vital,
            in AiecsFlowAgent actor, in AiecsBehaviorIntent intent, ref AiecsBody body, ref AiecsAttackState attack,
            ref AiecsWorkCounters counters)
        {
            Hits[index] = default;
            if (identity.External != 0 || vital.Dead != 0) return;
            AiecsDefinition definition = Definitions[identity.Definition];
            if (attack.Phase == AiecsAttackPhase.Cooldown && Clock.Time >= attack.NextAttack) attack.Phase = AiecsAttackPhase.Ready;
            if (attack.Phase == AiecsAttackPhase.Ready && intent.Behavior == (int)AiecsBehavior.Attack && Clock.Time >= attack.NextAttack &&
                Spatial.TryTarget(intent.Target, intent.TargetKey, out var startingTarget))
            {
                attack.Target = intent.Target; attack.TargetKey = intent.TargetKey;
                attack.Facing = math.normalizesafe(Spatial.Domain.ShortestDelta(actor.Position, startingTarget.Position), body.Facing);
                body.Facing = attack.Facing;
                attack.Sequence++; if (attack.Sequence == 0) attack.Sequence = 1;
                attack.Pulse = 0; attack.Phase = AiecsAttackPhase.Windup;
                attack.PhaseEnds = Clock.Time + definition.Windup;
                attack.NextAttack = attack.PhaseEnds + definition.Active + definition.Recovery + definition.Cooldown;
                counters.Attacks++;
            }
            if (attack.Phase == AiecsAttackPhase.Windup && Clock.Time >= attack.PhaseEnds)
            {
                attack.Phase = AiecsAttackPhase.Active; attack.PhaseEnds += definition.Active;
                attack.Pulse++;
                if (CanHit(identity, actor.Position, attack, definition, ref counters, out var target))
                {
                    Hits[index] = new AiecsHitEvent { Target = target.Entity, TargetKey = target.Identity.Key,
                        Context = new CombatDamageContext { Attack = new CombatAttackKey { Source = identity.Key,
                            Sequence = attack.Sequence, Window = 1, Pulse = attack.Pulse }, Credit = identity.Key,
                            Faction = Factions[identity.Faction], Damage = definition.Damage, Origin = actor.Position,
                            HitPoint = target.Position, Clock = Clock, SourceIsPlayer = identity.Player,
                            SlowEnabled = (byte)(definition.SlowDuration > 0f ? 1 : 0), SlowMultiplier = definition.SlowMultiplier,
                            SlowDuration = definition.SlowDuration, BuildingMultiplier = 1f, OnHitBuffs = definition.OnHitBuffs } };
                    counters.Hits++;
                }
            }
            if (attack.Phase == AiecsAttackPhase.Active && Clock.Time >= attack.PhaseEnds)
            { attack.Phase = AiecsAttackPhase.Recovery; attack.PhaseEnds += definition.Recovery; }
            if (attack.Phase == AiecsAttackPhase.Recovery && Clock.Time >= attack.PhaseEnds) attack.Phase = AiecsAttackPhase.Cooldown;
        }

        /// <summary>起手后逃离、换世界、死亡、墙体遮挡和转到背后均可以导致挥空。</summary>
        private bool CanHit(AiecsIdentity source, float2 position, AiecsAttackState attack, AiecsDefinition definition,
            ref AiecsWorkCounters counters, out AiecsTargetSample target)
        {
            if (!Spatial.TryTarget(attack.Target, attack.TargetKey, out target) || source.Key.World != target.Identity.Key.World ||
                source.Key.Dimension != target.Identity.Key.Dimension || !Spatial.IsHostile(source.Faction, target.Identity.Faction)) return false;
            if (!target.ShapeNear(position, Spatial.Domain, true).IntersectsCircle(position, definition.HitRange)) return false;
            float2 direction = math.normalizesafe(Spatial.Domain.ShortestDelta(position, target.Position), attack.Facing);
            if (math.dot(direction, attack.Facing) < definition.AttackArcCos) return false;
            counters.Los++;
            return Los.Visible(position, target.Position);
        }
    }
}
