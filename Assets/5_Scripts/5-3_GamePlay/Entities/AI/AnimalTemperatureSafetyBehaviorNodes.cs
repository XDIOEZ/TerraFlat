using UnityEngine;

public static class AnimalTemperatureSafetyBehaviorNodes
{
    #region 温度避险条件与动作
    public static void Register()
    {
        AnimalBehaviorTreeRegistry.RegisterCondition("temperature.pending", (context, _) =>
        {
            Mod_AnimalBehaviorTree host = context.Actor.GetComponentInChildren<Mod_AnimalBehaviorTree>(true);
            return () => host.HasTemperatureSafetyRequest;
        });
        AnimalBehaviorTreeRegistry.RegisterAction("temperature.retreat", (context, _) => new TemperatureRetreatAction(context));
    }

    private sealed class TemperatureRetreatAction : AnimalBehaviorTreeNode
    {
        private readonly AnimalBehaviorTreeContext context;
        private readonly Mod_AnimalBehaviorTree host;
        public TemperatureRetreatAction(AnimalBehaviorTreeContext context)
        {
            this.context = context;
            host = context.Actor.GetComponentInChildren<Mod_AnimalBehaviorTree>(true);
        }
        protected override void OnEnter() => context.BeginAction("temperature", role: AIStateAnimationRole.Moving);
        protected override AnimalBehaviorTreeStatus OnTick(float deltaTime)
        {
            if (!host.HasTemperatureSafetyRequest) return AnimalBehaviorTreeStatus.Success;
            host.TickTemperatureSafetyMovement(deltaTime);
            return AnimalBehaviorTreeStatus.Running;
        }
        protected override void OnExit() => host.StopTemperatureSafetyMovement();
    }
    #endregion
}
