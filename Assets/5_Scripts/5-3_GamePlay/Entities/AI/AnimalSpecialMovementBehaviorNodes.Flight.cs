using System;

public static partial class AnimalSpecialMovementBehaviorNodes
{
    #region 飞行与水生捕食节点
    private static Mod_AnimalFlightMovement Flight(AnimalBehaviorTreeContext context) =>
        context.GetOrCreateCapability("flightMotion", _ => new FlightMotionCapability(context)).Movement;

    private sealed class FlightMotionCapability : AnimalBehaviorTreeCapability
    {
        public Mod_AnimalFlightMovement Movement { get; }
        public FlightMotionCapability(AnimalBehaviorTreeContext context)
        {
            context.DisableSharedPerceptionUpdates();
            Movement = context.Actor.itemMods.RequireSingleModById<Mod_AnimalFlightMovement>(Mod_AnimalFlightMovement.ModuleId);
        }
        public override void Tick(float deltaTime)
        {
            Movement.BeginAnimationMotionFrame(deltaTime);
            Movement.TickFlightEnvironmentAndTimers(deltaTime);
        }
    }

    private static void RegisterFlightNodes()
    {
        AnimalBehaviorTreeRegistry.RegisterCondition("flight.hooked", (context, _) =>
        {
            var flight = Flight(context); return () => flight.IsHookedToFishingLine;
        });
        AnimalBehaviorTreeRegistry.RegisterCondition("flight.forcedLanding", (context, _) =>
        {
            var flight = Flight(context); return () => flight.RequiresForcedLanding;
        });
        AnimalBehaviorTreeRegistry.RegisterCondition("flight.fatigued", (context, _) =>
        {
            var flight = Flight(context); return () => flight.NeedsFatigueLanding;
        });
        AnimalBehaviorTreeRegistry.RegisterCondition("flight.threat", (context, _) =>
        {
            var flight = Flight(context); return () => flight.IsEscaping;
        });
        AnimalBehaviorTreeRegistry.RegisterCondition("flight.canHunt", (context, _) =>
        {
            var flight = Flight(context); return () => flight.CanStartAquaticHunt;
        });
        AnimalBehaviorTreeRegistry.RegisterCondition("flight.transitioning", (context, _) =>
        {
            var flight = Flight(context); return () => flight.IsInFlightTransition;
        });
        AnimalBehaviorTreeRegistry.RegisterCondition("flight.grounded", (context, _) =>
        {
            var flight = Flight(context); return () => flight.IsGrounded;
        });
        AnimalBehaviorTreeRegistry.RegisterCondition("flight.flying", (context, _) =>
        {
            var flight = Flight(context); return () => flight.IsFlying;
        });
        AnimalBehaviorTreeRegistry.RegisterCondition("flight.shouldTakeOff", (context, _) =>
        {
            var flight = Flight(context); return () => flight.ShouldTakeOff;
        });
        AnimalBehaviorTreeRegistry.RegisterCondition("flight.shouldLand", (context, _) =>
        {
            var flight = Flight(context); return () => flight.ShouldLandAfterFlight;
        });
        AnimalBehaviorTreeRegistry.RegisterAction("flight.containerFoodService", (context, _) =>
        {
            var flight = Flight(context);
            return new SpecialMovementActionNode(dt => { flight.TryTakeNearbyContainerFood(dt); return AnimalBehaviorTreeStatus.Success; });
        });
        AnimalBehaviorTreeRegistry.RegisterAction("flight.stop", (context, _) =>
        {
            var flight = Flight(context);
            return new SpecialMovementActionNode(_ => { flight.StopFlightAction(); return AnimalBehaviorTreeStatus.Running; });
        });
        AnimalBehaviorTreeRegistry.RegisterAction("flight.fatigueLand", (context, _) =>
        {
            var flight = Flight(context);
            return new SpecialMovementActionNode(dt => { flight.TickFatigueLandingAction(dt); return AnimalBehaviorTreeStatus.Running; },
                null, flight.StopFlightAction);
        });
        AnimalBehaviorTreeRegistry.RegisterAction("flight.flee", (context, _) =>
        {
            var flight = Flight(context);
            return new SpecialMovementActionNode(dt => flight.TickEscapeAction(dt)
                ? AnimalBehaviorTreeStatus.Running : AnimalBehaviorTreeStatus.Success,
                flight.BeginEscapeAction, flight.StopFlightAction);
        });
        AnimalBehaviorTreeRegistry.RegisterAction("flight.forage", (context, _) =>
        {
            var flight = Flight(context);
            return new SpecialMovementActionNode(dt => flight.TickGroundForageAction(dt)
                ? AnimalBehaviorTreeStatus.Running : AnimalBehaviorTreeStatus.Failure,
                null, flight.StopGroundForageAction, () => flight.CanTryGroundFood);
        });
        AnimalBehaviorTreeRegistry.RegisterAction("flight.takeOff", (context, _) =>
        {
            var flight = Flight(context);
            return new SpecialMovementActionNode(_ => { flight.BeginTakeoff(); return AnimalBehaviorTreeStatus.Success; });
        });
        AnimalBehaviorTreeRegistry.RegisterAction("flight.land", (context, _) =>
        {
            var flight = Flight(context);
            return new SpecialMovementActionNode(_ => { flight.BeginLanding(); return AnimalBehaviorTreeStatus.Success; });
        });
        AnimalBehaviorTreeRegistry.RegisterAction("flight.transition", (context, _) =>
        {
            var flight = Flight(context);
            return new SpecialMovementActionNode(dt =>
            {
                flight.TickFlightTransition(dt);
                return flight.IsInFlightTransition ? AnimalBehaviorTreeStatus.Running : AnimalBehaviorTreeStatus.Success;
            }, null, flight.StopFlightAction);
        });
        AnimalBehaviorTreeRegistry.RegisterAction("flight.wanderGround", (context, _) =>
        {
            var flight = Flight(context);
            return new SpecialMovementActionNode(dt => { flight.TickGroundWanderAction(dt); return AnimalBehaviorTreeStatus.Running; },
                null, flight.StopFlightAction);
        });
        AnimalBehaviorTreeRegistry.RegisterAction("flight.wanderAir", (context, _) =>
        {
            var flight = Flight(context);
            return new SpecialMovementActionNode(dt => { flight.TickFlightWanderAction(dt); return AnimalBehaviorTreeStatus.Running; },
                null, flight.StopFlightAction);
        });
        AnimalBehaviorTreeRegistry.RegisterAction("flight.acquireAquaticPrey", (context, _) =>
        {
            var flight = Flight(context);
            return new SpecialMovementActionNode(_ => flight.TryAcquireAquaticHuntTarget()
                ? AnimalBehaviorTreeStatus.Success : AnimalBehaviorTreeStatus.Failure);
        });
        AnimalBehaviorTreeRegistry.RegisterAction("flight.approachAquaticPrey", (context, _) =>
        {
            var flight = Flight(context); return new AquaticHuntActionNode(flight, flight.TickAquaticHuntApproach);
        });
        AnimalBehaviorTreeRegistry.RegisterAction("flight.diveForPrey", (context, _) =>
        {
            var flight = Flight(context); return new AquaticHuntActionNode(flight, flight.TickAquaticHuntDive, flight.BeginAquaticHuntDive);
        });
        AnimalBehaviorTreeRegistry.RegisterAction("flight.recoverFromDive", (context, _) =>
        {
            var flight = Flight(context); return new AquaticHuntActionNode(flight, flight.TickAquaticHuntRecovery, flight.BeginAquaticHuntRecovery);
        });
        AnimalBehaviorTreeRegistry.RegisterAction("flight.carryPreyToLand", (context, _) =>
        {
            var flight = Flight(context); return new AquaticHuntActionNode(flight, flight.TickAquaticHuntCarry);
        });
        AnimalBehaviorTreeRegistry.RegisterAction("flight.landWithPrey", (context, _) =>
        {
            var flight = Flight(context); return new AquaticHuntActionNode(flight, flight.TickAquaticHuntLanding);
        });
        AnimalBehaviorTreeRegistry.RegisterAction("flight.finishPrey", (context, _) =>
        {
            var flight = Flight(context); return new AquaticHuntActionNode(flight, flight.TickAquaticHuntFinish);
        });
        AnimalBehaviorTreeRegistry.RegisterAction("flight.eatPrey", (context, _) =>
        {
            var flight = Flight(context); return new AquaticHuntActionNode(flight, flight.TickAquaticHuntEat);
        });
    }

    private sealed class AquaticHuntActionNode : AnimalBehaviorTreeNode
    {
        private readonly Mod_AnimalFlightMovement flight;
        private readonly Func<float, AnimalBehaviorTreeStatus> tick;
        private readonly Action enter;
        private AnimalBehaviorTreeStatus result;
        public AquaticHuntActionNode(Mod_AnimalFlightMovement flight,
            Func<float, AnimalBehaviorTreeStatus> tick, Action enter = null)
        {
            this.flight = flight;
            this.tick = tick;
            this.enter = enter;
        }
        protected override void OnEnter() { result = AnimalBehaviorTreeStatus.Running; enter?.Invoke(); }
        protected override AnimalBehaviorTreeStatus OnTick(float deltaTime) => result = tick(deltaTime);
        protected override void OnExit()
        {
            if (result != AnimalBehaviorTreeStatus.Success) flight.CancelAquaticHunt();
        }
    }
    #endregion
}
