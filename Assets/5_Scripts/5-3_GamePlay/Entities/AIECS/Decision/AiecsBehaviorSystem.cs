using FlatWorld.Navigation;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace FlatWorld.AIECS
{
    /// <summary>行为到移动输入的适配：远距离追击共享组目标，近距离差异和游荡/逃跑使用有界合法短段。</summary>
    [BurstCompile]
    public partial struct AiecsBehaviorSystem : IJobEntity
    {
        [ReadOnly] public NativeArray<AiecsDefinition> Definitions;
        [ReadOnly] public NativeArray<FlowGoalHandle> GroupGoals;
        [ReadOnly] public AiecsSpatialView Spatial;
        [ReadOnly] public FlowNavigationSnapshot Navigation;
        public double Time;

        /// <summary>消费行为意图，设置共享 Goal 或局部目标，不建立单位级路线。</summary>
        private void Execute(in AiecsIdentity identity, in AiecsVital vital, in AiecsBehaviorIntent intent,
            in AiecsAttackState attack, in AiecsStatus status, ref AiecsBrain brain, ref AiecsLocalMotion local,
            ref AiecsBody body, ref AiecsFlowAgent actor)
        {
            actor.Mode = AiecsMoveMode.Hold;
            if (identity.External != 0 || vital.Dead != 0) return;
            AiecsDefinition definition = Definitions[identity.Definition];
            actor.Speed = definition.MoveSpeed * (Time < status.SlowUntil ? status.MoveMultiplier : 1f);
            if (math.lengthsq(actor.Velocity) > 0.0001f) body.Facing = math.normalizesafe(actor.Velocity);
            bool locked = attack.Phase == AiecsAttackPhase.Windup || attack.Phase == AiecsAttackPhase.Active || attack.Phase == AiecsAttackPhase.Recovery;
            if (locked || intent.Behavior == (int)AiecsBehavior.Idle || intent.Behavior == (int)AiecsBehavior.Attack) return;
            if (intent.Behavior == (int)AiecsBehavior.Chase && Spatial.TryTarget(intent.Target, intent.TargetKey, out var target))
            {
                actor.StopDistance = definition.AttackStartRange * 0.8f;
                body.Facing = math.normalizesafe(Spatial.Domain.ShortestDelta(actor.Position, target.Position), body.Facing);
                if (Navigation.CanSteer(actor.Position, target.Position, actor.Radius))
                {
                    actor.Mode = AiecsMoveMode.Local; actor.LocalDestination = target.Position;
                }
                else if ((uint)target.Identity.Group < (uint)GroupGoals.Length)
                {
                    actor.Mode = AiecsMoveMode.SharedGoal; actor.Goal = GroupGoals[target.Identity.Group];
                }
                return;
            }
            if (intent.HasDestination != 0)
            {
                if (Navigation.CanSteer(actor.Position, intent.Destination, actor.Radius))
                { actor.Mode = AiecsMoveMode.Local; actor.LocalDestination = intent.Destination; actor.StopDistance = 0.15f; }
                return;
            }
            if (intent.Behavior != (int)AiecsBehavior.Wander && intent.Behavior != (int)AiecsBehavior.Flee) return;
            bool reached = math.lengthsq(Spatial.Domain.ShortestDelta(actor.Position, local.Destination)) < 0.04f;
            if (local.Behavior != intent.Behavior || Time >= local.RefreshAt || reached)
            {
                local.Valid = 0; local.Behavior = intent.Behavior;
                local.RefreshAt = Time + (intent.Behavior == (int)AiecsBehavior.Flee ? 0.5f : definition.WanderSeconds);
                var random = new Random(brain.RandomState == 0 ? 1u : brain.RandomState);
                float2 away = math.normalizesafe(Spatial.Domain.ShortestDelta(brain.ThreatPosition, actor.Position), random.NextFloat2Direction());
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    float2 direction = intent.Behavior == (int)AiecsBehavior.Flee
                        ? math.normalizesafe(away + random.NextFloat2Direction() * 0.65f, away) : random.NextFloat2Direction();
                    float distance = math.min(7f, definition.WanderRadius) * random.NextFloat(0.35f, 1f);
                    float2 point = Navigation.Domain.Normalize(actor.Position + direction * distance);
                    if (!Navigation.CanOccupy(point, actor.Radius) || !Navigation.CanSteer(actor.Position, point, actor.Radius)) continue;
                    local.Destination = point; local.Valid = 1; break;
                }
                brain.RandomState = random.state;
            }
            if (local.Valid != 0)
            { actor.Mode = AiecsMoveMode.Local; actor.LocalDestination = local.Destination; actor.StopDistance = 0.15f; }
        }
    }
}
