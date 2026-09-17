using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace FlatWorld.AIECS
{
    /// <summary>共享规则驱动的 Brain；仅计算事实和选择意图，扩展提议可覆盖默认规则，不执行导航或伤害。</summary>
    [BurstCompile]
    public partial struct AiecsDecisionSystem : IJobEntity
    {
        [ReadOnly] public NativeArray<AiecsDefinition> Definitions;
        [ReadOnly] public AiecsSpatialView Spatial;
        [ReadOnly] public NativeArray<AiecsEngagementSlot> EngagementSlots;
        public double Time; // 当前权威时间。

        /// <summary>错峰计算事实，并原子提交这一单位的行为意图。</summary>
        private void Execute([EntityIndexInQuery] int index, Entity entity, in AiecsIdentity identity, in AiecsVital vital,
            in AiecsFlowAgent actor, in AiecsAttackState attack, ref AiecsBrain brain,
            ref AiecsBehaviorIntent intent, ref AiecsBehaviorProposal proposal)
        {
            if (identity.External != 0 || vital.Dead != 0) { intent = default; proposal = default; return; }
            bool targetValid = Spatial.TryTarget(brain.Target, brain.TargetKey, out var target);
            bool locked = attack.Phase == AiecsAttackPhase.Windup || attack.Phase == AiecsAttackPhase.Active || attack.Phase == AiecsAttackPhase.Recovery;
            AiecsDefinition definition = Definitions[identity.Definition];
            bool hasSlot = targetValid && (uint)index < (uint)EngagementSlots.Length &&
                AiecsEngagementSlots.Matches(EngagementSlots[index], entity, brain.Target, brain.TargetKey);
            bool inAttackRange = hasSlot && Time >= attack.NextAttack &&
                math.lengthsq(Spatial.Domain.ShortestDelta(actor.Position, target.Position)) <=
                definition.AttackStartRange * definition.AttackStartRange;
            if (Time < brain.NextDecision && proposal.Priority <= 0 && targetValid == (intent.Target != Entity.Null) &&
                (intent.Behavior == (int)AiecsBehavior.Attack) == (locked || inAttackRange)) return;
            brain.NextDecision = Time + definition.DecisionPeriod;
            AiecsDecisionFacts facts = AiecsDecisionFacts.None;
            if (targetValid)
            {
                facts |= AiecsDecisionFacts.HasTarget;
                if (inAttackRange) facts |= AiecsDecisionFacts.InAttackRange;
            }
            if (vital.Hp <= vital.MaxHp * definition.FleeHealthRatio) facts |= AiecsDecisionFacts.LowHealth;
            if (brain.Threat > 0f && Time < brain.MemoryUntil) facts |= AiecsDecisionFacts.HasThreat;
            else brain.Threat = 0f;
            bool wander = brain.Behavior == (int)AiecsBehavior.Wander;
            if (wander ? Time < brain.BehaviorUntil : Time >= brain.BehaviorUntil) facts |= AiecsDecisionFacts.RestFinished;
            if (locked) facts |= AiecsDecisionFacts.AttackLocked;
            int behavior = (int)AiecsBehavior.Idle, priority = int.MinValue;
            for (int i = 0; i < definition.Rules.Length; i++)
            {
                AiecsDecisionRule rule = definition.Rules[i];
                if ((facts & rule.Require) != rule.Require || (facts & rule.Exclude) != 0 || rule.Priority <= priority) continue;
                behavior = rule.Behavior; priority = rule.Priority;
            }
            bool useProposal = proposal.Priority > 0 && proposal.Priority > priority;
            if (useProposal) behavior = proposal.Behavior;
            if (brain.Behavior != behavior)
            {
                brain.Behavior = behavior; brain.EnteredAt = Time;
                brain.BehaviorUntil = Time + (behavior == (int)AiecsBehavior.Wander ? definition.WanderSeconds :
                    behavior == (int)AiecsBehavior.Flee ? definition.FleeSeconds : definition.IdleSeconds);
            }
            intent = new AiecsBehaviorIntent { Behavior = behavior, Target = targetValid ? brain.Target : Entity.Null,
                TargetKey = targetValid ? brain.TargetKey : default, HasDestination = useProposal ? proposal.HasDestination : (byte)0,
                Destination = proposal.Destination };
            proposal = default;
        }
    }
}
