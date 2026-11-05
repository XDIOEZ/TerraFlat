/// <summary>机仆节点只连接通用树生命周期和能力模块，不另建第二套决策循环。</summary>
public static class ServitorBehaviorNodes
{
    #region 正式行为树注册
    private sealed class ServitorCapability : AnimalBehaviorTreeCapability
    {
        public readonly Mod_ServitorController Controller;
        public ServitorCapability(AnimalBehaviorTreeContext context)
            => Controller = context.Actor.itemMods.RequireSingleModById<Mod_ServitorController>(Mod_ServitorController.ModuleId);
        public override void Tick(float seconds) => Controller.TickServitorState(seconds);
        public override void Dispose() { Controller.StopWork(); Controller.StopCharging(); }
    }
    private static Mod_ServitorController Resolve(AnimalBehaviorTreeContext context)
        => context.GetOrCreateCapability("servitor", _ => new ServitorCapability(context)).Controller;

    public static void Register()
    {
        AnimalBehaviorTreeRegistry.RegisterCondition("servitor.needsRecharge", (context, _) =>
        { var robot = Resolve(context); return () => robot.NeedsRecharge; });
        AnimalBehaviorTreeRegistry.RegisterCondition("servitor.canWork", (context, _) =>
        { var robot = Resolve(context); return () => robot.CanExecuteWork; });
        AnimalBehaviorTreeRegistry.RegisterAction("servitor.recharge", (context, _) => new ServitorAction(Resolve(context), 0));
        AnimalBehaviorTreeRegistry.RegisterAction("servitor.executeJob", (context, _) => new ServitorAction(Resolve(context), 1));
        AnimalBehaviorTreeRegistry.RegisterAction("servitor.standby", (context, _) => new ServitorAction(Resolve(context), 2));
    }

    private sealed class ServitorAction : AnimalBehaviorTreeNode
    {
        private readonly Mod_ServitorController robot;
        private readonly int action;
        public ServitorAction(Mod_ServitorController robot, int action) { this.robot = robot; this.action = action; }
        protected override AnimalBehaviorTreeStatus OnTick(float seconds)
        {
            if (!robot.Ready) return AnimalBehaviorTreeStatus.Failure;
            if (action == 0) robot.TickRecharge(seconds);
            else if (action == 1) robot.TickWork(seconds);
            else robot.TickStandby();
            return AnimalBehaviorTreeStatus.Running;
        }
        protected override void OnExit()
        { if (action == 0) robot.StopCharging(); else if (action == 1) robot.StopWork(); }
    }
    #endregion
}
