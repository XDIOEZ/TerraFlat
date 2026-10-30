using System;
using Newtonsoft.Json.Linq;

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
        public override void Tick(float deltaTime) => Movement.TickAquaticEnvironmentAndThreats(deltaTime);
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
