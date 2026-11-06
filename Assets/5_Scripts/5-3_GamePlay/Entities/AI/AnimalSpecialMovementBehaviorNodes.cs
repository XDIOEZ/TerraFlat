using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>为特殊移动能力注册树条件与可中断动作。</summary>
public static partial class AnimalSpecialMovementBehaviorNodes
{
    #region 注册
    public static void Register()
    {
        RegisterAquaticNodes();
        RegisterLightNavigationNodes();
        RegisterFlightNodes();
        RegisterColonyNodes();
    }

    private static Mod_AquaticMovement Aquatic(AnimalBehaviorTreeContext context) =>
        context.GetOrCreateCapability("aquaticEnvironment", _ => new AquaticEnvironmentCapability(context)).Movement;

    private sealed class AquaticEnvironmentCapability : AnimalBehaviorTreeCapability
    {
        public Mod_AquaticMovement Movement { get; }
        public AquaticEnvironmentCapability(AnimalBehaviorTreeContext context)
        {
            context.DisableSharedPerceptionUpdates();
            Movement = context.Actor.itemMods.RequireSingleModById<Mod_AquaticMovement>(Mod_AquaticMovement.ModuleId);
        }
        public override void Tick(float deltaTime)
        {
            Movement.BeginAquaticAnimationMotionFrame();
            Movement.TickAquaticEnvironmentAndThreats(deltaTime);
        }
        public override void TickWhileManuallyControlled(float deltaTime)
        {
            Movement.BeginAquaticAnimationMotionFrame();
            Movement.TickAquaticEnvironmentAndThreats(deltaTime, false);
        }
        public override void Dispose() => Movement.SetWaterCombatTargetRestriction(false);
    }

    private static Mod_LightSensitiveNavigation LightNavigation(AnimalBehaviorTreeContext context)
    {
        context.DisableSharedPerceptionUpdates();
        return context.Actor.itemMods.RequireSingleModById<Mod_LightSensitiveNavigation>(Mod_LightSensitiveNavigation.ModuleId);
    }

    private static void RegisterAquaticNodes()
    {
        AnimalBehaviorTreeRegistry.RegisterCondition("aquatic.externallyControlled", (context, _) =>
        {
            var movement = Aquatic(context);
            return () => movement.IsBehaviorExternallyControlled;
        });
        AnimalBehaviorTreeRegistry.RegisterCondition("aquatic.stranded", (context, _) =>
        {
            var movement = Aquatic(context);
            return () => movement.IsStranded;
        });
        AnimalBehaviorTreeRegistry.RegisterCondition("aquatic.hooked", (context, _) =>
        {
            var movement = Aquatic(context);
            return () => movement.IsHooked;
        });
        AnimalBehaviorTreeRegistry.RegisterCondition("aquatic.threat", (context, _) =>
        {
            var movement = Aquatic(context);
            return () => movement.HasAquaticThreat;
        });
        AnimalBehaviorTreeRegistry.RegisterAction("aquatic.stop", (context, _) =>
        {
            var movement = Aquatic(context);
            return new SpecialMovementActionNode(_ => { movement.StopAquaticAction(); return AnimalBehaviorTreeStatus.Running; });
        });
        AnimalBehaviorTreeRegistry.RegisterAction("aquatic.returnWater", (context, _) =>
        {
            var movement = Aquatic(context);
            return new SpecialMovementActionNode(dt => { movement.TickStrandedAction(dt); return AnimalBehaviorTreeStatus.Running; },
                movement.BeginStrandedAction, movement.EndStrandedAction);
        });
        AnimalBehaviorTreeRegistry.RegisterAction("aquatic.flee", (context, _) =>
        {
            var movement = Aquatic(context);
            return new SpecialMovementActionNode(dt => { movement.TickFleeAction(dt); return AnimalBehaviorTreeStatus.Running; },
                () => movement.SetAquaticFleeing(true), movement.StopAquaticAction);
        });
        AnimalBehaviorTreeRegistry.RegisterAction("aquatic.forage", (context, _) =>
        {
            var movement = Aquatic(context);
            return new SpecialMovementActionNode(dt => movement.TickForageAction(dt)
                ? AnimalBehaviorTreeStatus.Running : AnimalBehaviorTreeStatus.Failure,
                null, movement.StopAquaticAction, () => movement.CanForage);
        });
        AnimalBehaviorTreeRegistry.RegisterAction("aquatic.wander", (context, _) =>
        {
            var movement = Aquatic(context);
            return new SpecialMovementActionNode(dt => { movement.TickSwimAction(dt); return AnimalBehaviorTreeStatus.Running; },
                null, movement.StopAquaticAction);
        });
        AnimalBehaviorTreeRegistry.RegisterAction("aquatic.combat", (context, parameters) =>
            new AquaticCombatActionNode(context, parameters, Aquatic(context)));
    }

    private static void RegisterLightNavigationNodes()
    {
        AnimalBehaviorTreeRegistry.RegisterCondition("light.playerVisible", (context, _) =>
        {
            var movement = LightNavigation(context);
            return movement.HasVisiblePlayerTarget;
        });
        AnimalBehaviorTreeRegistry.RegisterCondition("light.known", (context, parameters) =>
        {
            var movement = LightNavigation(context);
            return () => movement.TryGetCurrentLightLevel(out _);
        });
        AnimalBehaviorTreeRegistry.RegisterCondition("light.illuminated", (context, _) =>
        {
            var movement = LightNavigation(context);
            return movement.IsCurrentPositionIlluminated;
        });
        AnimalBehaviorTreeRegistry.RegisterAction("light.chaseDirect", (context, _) =>
        {
            var movement = LightNavigation(context);
            return new SpecialMovementActionNode(dt => movement.TickDirectPursuit(dt)
                ? AnimalBehaviorTreeStatus.Running : AnimalBehaviorTreeStatus.Failure,
                movement.BeginDirectPursuit, () => movement.StopNavigation());
        });
        AnimalBehaviorTreeRegistry.RegisterAction("light.seekDark", (context, _) =>
        {
            var movement = LightNavigation(context);
            return new SpecialMovementActionNode(dt => movement.TickDarkNavigation(dt, false)
                ? AnimalBehaviorTreeStatus.Running : AnimalBehaviorTreeStatus.Failure,
                () => movement.BeginDarkNavigation(false), () => movement.StopNavigation(),
                () => movement.CanSearchForDarkness);
        });
        AnimalBehaviorTreeRegistry.RegisterAction("light.waitInLight", (context, _) =>
        {
            var movement = LightNavigation(context);
            return new SpecialMovementActionNode(_ => { movement.WaitInLight(); return AnimalBehaviorTreeStatus.Running; });
        });
        AnimalBehaviorTreeRegistry.RegisterAction("light.wanderDark", (context, _) =>
        {
            var movement = LightNavigation(context);
            return new SpecialMovementActionNode(dt => { movement.TickDarkNavigation(dt, true); return AnimalBehaviorTreeStatus.Running; },
                () => movement.BeginDarkNavigation(true), () => movement.StopNavigation());
        });
        AnimalBehaviorTreeRegistry.RegisterAction("light.stop", (context, _) =>
        {
            var movement = LightNavigation(context);
            return new SpecialMovementActionNode(_ => { movement.StopNavigation(); return AnimalBehaviorTreeStatus.Running; });
        });
    }
    #endregion

    #region 水域追击与攻击
    private sealed class AquaticCombatActionNode : AnimalBehaviorTreeNode
    {
        private readonly AnimalBehaviorTreeContext context;
        private readonly Mod_AquaticMovement movement;
        private readonly AnimalHuntingCapability hunting;
        private readonly AnimalCombatCapability combat;

        public AquaticCombatActionNode(AnimalBehaviorTreeContext context, JObject parameters, Mod_AquaticMovement movement)
        {
            this.context = context;
            this.movement = movement;
            hunting = AnimalLandActionSupport.Capability(context, AnimalLandTargetRules.Text(parameters, "sourceKey", "hunting"),
                (actor, config, saved) => new AnimalHuntingCapability(actor, config, saved));
            combat = AnimalLandActionSupport.Combat(context, parameters);
            hunting.AdditionalEligibility = movement.CanPursueTargetInWater;
            combat.AttackWindowGuard = CanContinueAttackWindow;
        }

        private bool HasUsableTarget() => hunting.Target != null && hunting.TargetVisible &&
            AnimalLandTargetRules.IsLivingOpponent(context, hunting.Target) && movement.CanPursueTargetInWater(hunting.Target);

        private bool CanContinueAttackWindow() => HasUsableTarget() &&
            WorldTopologyRuntime.SqrDistance(context.Position, hunting.Target.transform.position) <= combat.AttackRange * combat.AttackRange;

        public override bool CanRun => hunting.Target != null && hunting.TargetVisible &&
            AnimalLandTargetRules.IsLivingOpponent(context, hunting.Target);
        protected override void OnEnter()
        {
            movement.SetWaterCombatTargetRestriction(true);
            combat.EnterAttack();
        }
        protected override AnimalBehaviorTreeStatus OnTick(float deltaTime)
        {
            if (!HasUsableTarget()) return AnimalBehaviorTreeStatus.Failure;
            Vector2 position = hunting.Target.transform.position;
            context.FaceTarget(position, true);
            if (combat.IsAttackLocked)
            {
                context.BeginAction("attack", "Attack", AIStateAnimationRole.Action);
                return AnimalBehaviorTreeStatus.Running;
            }
            if (combat.IsCooldownDone && WorldTopologyRuntime.SqrDistance(context.Position, position) <= combat.AttackRange * combat.AttackRange)
            {
                context.BeginAction("attack", "Attack", AIStateAnimationRole.Action);
                combat.StartAttack(position);
                return AnimalBehaviorTreeStatus.Running;
            }
            context.BeginAction("chase", "Move", AIStateAnimationRole.Moving);
            return movement.TrySwimTowardCombatTarget(position, deltaTime, combat.AttackRange)
                ? AnimalBehaviorTreeStatus.Running : AnimalBehaviorTreeStatus.Failure;
        }
        protected override void OnExit()
        {
            combat.StopAttack(true);
            movement.StopAquaticAction();
            movement.SetWaterCombatTargetRestriction(false);
        }
    }
    #endregion

    #region 节点生命周期
    private sealed class SpecialMovementActionNode : AnimalBehaviorTreeNode
    {
        private readonly Func<float, AnimalBehaviorTreeStatus> tick;
        private readonly Action enter;
        private readonly Action exit;
        private readonly Func<bool> canRun;

        public SpecialMovementActionNode(Func<float, AnimalBehaviorTreeStatus> tick,
            Action enter = null, Action exit = null, Func<bool> canRun = null)
        {
            this.tick = tick;
            this.enter = enter;
            this.exit = exit;
            this.canRun = canRun;
        }

        public override bool CanRun => canRun == null || canRun();
        protected override void OnEnter() => enter?.Invoke();
        protected override AnimalBehaviorTreeStatus OnTick(float deltaTime) => tick(deltaTime);
        protected override void OnExit() => exit?.Invoke();
    }
    #endregion
}
