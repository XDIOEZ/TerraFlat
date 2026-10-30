public static partial class AnimalSpecialMovementBehaviorNodes
{
    #region 群落节点
    private static Mod_ColonyResident Colony(AnimalBehaviorTreeContext context)
    {
        Flight(context);
        return context.GetOrCreateCapability("colonyEnvironment", _ => new ColonyEnvironmentCapability(context)).Resident;
    }

    private sealed class ColonyEnvironmentCapability : AnimalBehaviorTreeCapability
    {
        public Mod_ColonyResident Resident { get; }
        public ColonyEnvironmentCapability(AnimalBehaviorTreeContext context)
        {
            Resident = context.Actor.itemMods.RequireSingleModById<Mod_ColonyResident>(Mod_ColonyResident.ModuleId);
        }
        public override void Tick(float deltaTime) => Resident.TickColonyEnvironmentAndThreats(deltaTime);
    }

    private static void RegisterColonyNodes()
    {
        AnimalBehaviorTreeRegistry.RegisterCondition("colony.defense", (context, _) =>
        {
            var colony = Colony(context); return () => colony.HasDefenseTarget;
        });
        AnimalBehaviorTreeRegistry.RegisterCondition("colony.sleepReturn", (context, _) =>
        {
            var colony = Colony(context); return () => colony.ShouldReturnForSleep;
        });
        AnimalBehaviorTreeRegistry.RegisterCondition("colony.angry", (context, _) =>
        {
            var colony = Colony(context); return () => colony.IsAngry;
        });
        AnimalBehaviorTreeRegistry.RegisterCondition("colony.hasLockedTarget", (context, _) =>
        {
            var colony = Colony(context); return () => colony.HasLockedTarget;
        });
        AnimalBehaviorTreeRegistry.RegisterCondition("colony.searching", (context, _) =>
        {
            var colony = Colony(context); return () => colony.IsSearchingTarget;
        });
        AnimalBehaviorTreeRegistry.RegisterCondition("colony.orphaned", (context, _) =>
        {
            var colony = Colony(context); return () => colony.IsOrphaned;
        });
        AnimalBehaviorTreeRegistry.RegisterCondition("colony.returningHoney", (context, _) =>
        {
            var colony = Colony(context); return () => colony.IsReturningHoney;
        });
        AnimalBehaviorTreeRegistry.RegisterCondition("colony.canForage", (context, _) =>
        {
            var colony = Colony(context); return () => colony.CanForageNow;
        });
        AnimalBehaviorTreeRegistry.RegisterCondition("colony.needsMeal", (context, _) =>
        {
            var colony = Colony(context); return () => colony.ShouldTakeColonyMeal;
        });
        AnimalBehaviorTreeRegistry.RegisterCondition("colony.hasHome", (context, _) =>
        {
            var colony = Colony(context); return () => colony.HasColonyHome;
        });
        AnimalBehaviorTreeRegistry.RegisterAction("colony.returnForSleep", (context, _) =>
        {
            var colony = Colony(context);
            return new SpecialMovementActionNode(dt => { colony.TickSleepReturn(dt); return AnimalBehaviorTreeStatus.Running; },
                null, colony.StopColonyAction);
        });
        AnimalBehaviorTreeRegistry.RegisterAction("colony.pursue", (context, _) =>
        {
            var colony = Colony(context);
            return new SpecialMovementActionNode(dt => { colony.TickColonyPursuit(dt); return AnimalBehaviorTreeStatus.Running; },
                null, colony.StopColonyAction);
        });
        AnimalBehaviorTreeRegistry.RegisterAction("colony.search", (context, _) =>
        {
            var colony = Colony(context);
            return new SpecialMovementActionNode(dt => { colony.TickColonySearch(dt); return AnimalBehaviorTreeStatus.Running; },
                null, colony.StopColonyAction);
        });
        AnimalBehaviorTreeRegistry.RegisterAction("colony.angryPatrol", (context, _) =>
        {
            var colony = Colony(context);
            return new SpecialMovementActionNode(dt => { colony.TickAngryPatrol(dt); return AnimalBehaviorTreeStatus.Running; },
                null, colony.StopColonyAction);
        });
        AnimalBehaviorTreeRegistry.RegisterAction("colony.independentWander", (context, _) =>
        {
            var colony = Colony(context);
            return new SpecialMovementActionNode(dt => { colony.TickIndependentWander(dt); return AnimalBehaviorTreeStatus.Running; },
                null, colony.StopColonyAction);
        });
        AnimalBehaviorTreeRegistry.RegisterAction("colony.returnHoney", (context, _) =>
        {
            var colony = Colony(context);
            return new SpecialMovementActionNode(colony.TickColonyReturnHoney, null, colony.StopColonyAction);
        });
        AnimalBehaviorTreeRegistry.RegisterAction("colony.forage", (context, _) =>
        {
            var colony = Colony(context);
            return new SpecialMovementActionNode(colony.TickColonyForageAction, null,
                () => { colony.CancelColonyForage(); colony.StopColonyAction(); });
        });
        AnimalBehaviorTreeRegistry.RegisterAction("colony.meal", (context, _) =>
        {
            var colony = Colony(context);
            return new SpecialMovementActionNode(colony.TickColonyMeal, null, colony.StopColonyAction);
        });
        AnimalBehaviorTreeRegistry.RegisterAction("colony.patrol", (context, _) =>
        {
            var colony = Colony(context);
            return new SpecialMovementActionNode(dt => { colony.TickColonyPatrol(dt); return AnimalBehaviorTreeStatus.Running; },
                null, colony.StopColonyAction);
        });
        AnimalBehaviorTreeRegistry.RegisterAction("colony.stop", (context, _) =>
        {
            var colony = Colony(context);
            return new SpecialMovementActionNode(_ => { colony.StopColonyAction(); return AnimalBehaviorTreeStatus.Running; });
        });
    }
    #endregion
}
