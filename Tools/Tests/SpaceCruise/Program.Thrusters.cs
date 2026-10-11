using FlatWorld.Spaceflight;

internal static partial class Program
{
    #region 喷嘴方向与请求回归
    private static void ThrusterDirectionRules()
    {
        var allocation = new ShipThrustAllocation();
        SpaceVector2[] requests = { new(0d, 2d), new(-2d, 0d), new(0d, -2d), new(2d, 0d) };
        for (int input = 0; input < requests.Length; input++)
        {
            allocation.Reset(requests[input], 2d, 0d);
            AddCardinalThrusters(allocation);
            Check(allocation.Count == 4, "every installed engine retains its output index");
            for (int engine = 0; engine < 4; engine++)
                Near(engine == input ? 1d : 0d, allocation.GetThrottle(engine), "WASD selects the opposing nozzle");
            SupplyRequestedThrusters(allocation);
            NearVector(requests[input], allocation.ResolveVelocityChange(), "cardinal fuel recovers requested acceleration");
        }

        double diagonal = Math.Sqrt(2d);
        allocation.Reset(new SpaceVector2(diagonal, diagonal), 2d, 0d);
        AddCardinalThrusters(allocation);
        Near(1d / Math.Sqrt(2d), allocation.GetThrottle(0), "upper movement uses bottom nozzle at diagonal throttle");
        Near(1d / Math.Sqrt(2d), allocation.GetThrottle(3), "right movement uses left nozzle at diagonal throttle");
        Near(0d, allocation.GetThrottle(1), "right nozzle stays off during right movement");
        Near(0d, allocation.GetThrottle(2), "top nozzle stays off during upper movement");
        SupplyRequestedThrusters(allocation);
        NearVector(new SpaceVector2(diagonal, diagonal), allocation.ResolveVelocityChange(), "diagonal uses two directions without extra speed");

        allocation.Reset(new SpaceVector2(-2d, 0d), 2d, 0d);
        AddCardinalThrusters(allocation, Math.PI * .5d);
        Near(1d, allocation.GetThrottle(0), "rotated ship uses its world left thrust direction");
        SupplyRequestedThrusters(allocation);
        NearVector(new SpaceVector2(-2d, 0d), allocation.ResolveVelocityChange(), "ship rotation carries engine direction into world space");

        allocation.Reset(new SpaceVector2(-2d, 0d), 2d, 0d);
        allocation.Add(ShipThrustAllocation.GetThrustDirection(0d, 1), default, 100d);
        allocation.Add(ShipThrustAllocation.GetThrustDirection(Math.PI * .5d, 0), default, 100d);
        allocation.Add(ShipThrustAllocation.GetThrustDirection(Math.PI, 2), default, 100d);
        SupplyRequestedThrusters(allocation);
        Near(1d, allocation.GetThrottle(0), "leader right nozzle fires left");
        Near(1d, allocation.GetThrottle(1), "docked rotated member bottom nozzle fires world left");
        Near(0d, allocation.GetThrottle(2), "unrelated docked direction stays off");
        NearVector(new SpaceVector2(-2d, 0d), allocation.ResolveVelocityChange(), "docked same direction engines share one speed budget");

        allocation.Reset(new SpaceVector2(2d, 0d), 2d, 0d);
        allocation.Add(ShipThrustAllocation.GetThrustDirection(-Math.PI * .25d, 0), default, 100d);
        allocation.Add(ShipThrustAllocation.GetThrustDirection(-Math.PI * .125d, 0), default, 100d);
        SupplyRequestedThrusters(allocation);
        Check(allocation.ResolveVelocityChange().Magnitude <= 2d + 1e-9d, "nonorthogonal docked members cannot exceed requested speed change");
    }

    private static void ThrusterDemandRules()
    {
        var allocation = new ShipThrustAllocation();
        SpaceVector2 current = new(120d, 0d);
        SpaceVector2 manual = ShipCruiseGuidance.ManualVelocity(current, new SpaceVector2(-1d, 0d), Settings, .02d);
        SpaceVector2 change = ShipCruiseGuidance.StepVelocity(current, manual, Settings, .02d) - current;
        allocation.Reset(change, change.Magnitude, 0d);
        AddCardinalThrusters(allocation);
        Near(1d, allocation.GetThrottle(1), "braking rightward drift fires right nozzle from leftward demand");
        Near(0d, allocation.GetThrottle(3), "existing rightward velocity cannot select the left nozzle during braking");
        SupplyRequestedThrusters(allocation);
        NearVector(change, allocation.ResolveVelocityChange(), "braking preserves bounded guidance delta");

        allocation.Reset(default, 2d, 0d);
        AddCardinalThrusters(allocation);
        for (int i = 0; i < allocation.Count; i++) Near(0d, allocation.GetThrottle(i), "released keys request no fuel");
        SupplyRequestedThrusters(allocation);
        NearVector(default, allocation.ResolveVelocityChange(), "released keys preserve inertial velocity");
        Near(0d, allocation.ResolveTurnAuthority(), "released keys request no turn authority");
        allocation.Reset(new SpaceVector2(-2d, 0d), 2d, 0d);
        allocation.Add(ShipThrustAllocation.GetThrustDirection(0d, 3), default, 100d);
        SupplyRequestedThrusters(allocation);
        NearVector(default, allocation.ResolveVelocityChange(), "missing opposing thruster cannot produce the requested direction");
    }
    #endregion

    #region 燃料与转向回归
    private static void ThrusterFuelRules()
    {
        var allocation = new ShipThrustAllocation();
        allocation.Reset(new SpaceVector2(1.2d, 1.6d), 2d, 0d);
        AddCardinalThrusters(allocation);
        allocation.SetActualOutput(0, allocation.GetThrottle(0) * .5d);
        allocation.SetActualOutput(3, 0d);
        NearVector(new SpaceVector2(0d, .8d), allocation.ResolveVelocityChange(), "empty horizontal fuel loses only the horizontal axis");
        allocation.SetActualOutput(3, allocation.GetThrottle(3));
        NearVector(new SpaceVector2(1.2d, .8d), allocation.ResolveVelocityChange(), "restored fuel restores only its own axis");

        allocation.Reset(new SpaceVector2(2d, 0d), 2d, 0d);
        SpaceVector2 right = ShipThrustAllocation.GetThrustDirection(0d, 3);
        allocation.Add(right, default, 100d);
        allocation.Add(right, default, 300d);
        allocation.SetActualOutput(0, .25d);
        allocation.SetActualOutput(1, .75d);
        NearVector(new SpaceVector2(1.25d, 0d), allocation.ResolveVelocityChange(), "real fuel fractions are weighted by thrust capacity");
        allocation.SetActualOutput(0, 1d);
        allocation.SetActualOutput(1, 0d);
        NearVector(new SpaceVector2(.5d, 0d), allocation.ResolveVelocityChange(), "empty large engine remains in planned capacity");
        allocation.SetActualOutput(1, 1d);
        NearVector(new SpaceVector2(2d, 0d), allocation.ResolveVelocityChange(), "multiple engines cannot multiply the cruise acceleration");

        allocation.Reset(new SpaceVector2(.2d, 0d), 2d, 0d);
        allocation.Add(right, default, 100d);
        Near(.1d, allocation.GetThrottle(0), "small velocity demand requests partial throttle");
        allocation.SetActualOutput(0, .05d);
        NearVector(new SpaceVector2(.1d, 0d), allocation.ResolveVelocityChange(), "partial fuel is divided by requested throttle");
        allocation.SetActualOutput(0, .2d);
        NearVector(new SpaceVector2(.2d, 0d), allocation.ResolveVelocityChange(), "actual output above request cannot overdrive guidance");
        allocation.Reset(new SpaceVector2(.2d, 0d), 2d, 0d);
        allocation.Add(right, default, 100d);
        NearVector(default, allocation.ResolveVelocityChange(), "reset clears fuel output before the next fixed step");
    }

    private static void ThrusterTurnRules()
    {
        var allocation = new ShipThrustAllocation();
        allocation.Reset(default, 2d, .5d);
        AddTurnThrusters(allocation);
        Near(.5d, allocation.GetThrottle(0), "positive turn selects positive torque at lower edge");
        Near(.5d, allocation.GetThrottle(1), "positive turn selects positive torque at upper edge");
        Near(0d, allocation.GetThrottle(2), "negative torque stays off for positive turn");
        Near(0d, allocation.GetThrottle(3), "opposing torque stays off for positive turn");
        allocation.SetActualOutput(0, .125d);
        allocation.SetActualOutput(1, .375d);
        Near(.625d, allocation.ResolveTurnAuthority(), "turn authority weights real fuel by selected engine capacity");
        NearVector(default, allocation.ResolveVelocityChange(), "pure turn does not add translation");

        allocation.Reset(default, 2d, -1d);
        AddTurnThrusters(allocation);
        Near(0d, allocation.GetThrottle(0), "negative turn disables positive torque");
        Near(0d, allocation.GetThrottle(1), "negative turn disables all positive torque engines");
        Near(1d, allocation.GetThrottle(2), "negative turn selects negative torque at lower edge");
        Near(1d, allocation.GetThrottle(3), "negative turn selects negative torque at upper edge");
        Near(0d, allocation.ResolveTurnAuthority(), "reversed turn cannot retain previous fuel output");
        SupplyRequestedThrusters(allocation);
        Near(1d, allocation.ResolveTurnAuthority(), "full fuel retains direct angular-speed authority");
        NearVector(default, allocation.ResolveVelocityChange(), "negative pure turn still has no translation");

        allocation.Reset(default, 2d, 1d);
        allocation.Add(ShipThrustAllocation.GetThrustDirection(0d, 3), default, 100d);
        SupplyRequestedThrusters(allocation);
        Near(0d, allocation.GetThrottle(0), "thruster at center of mass has no turn lever");
        Near(0d, allocation.ResolveTurnAuthority(), "unsupported turn has no authority");

        allocation.Reset(new SpaceVector2(2d, 0d), 2d, 1d);
        allocation.Add(ShipThrustAllocation.GetThrustDirection(0d, 0), new SpaceVector2(2d, 0d), 100d);
        allocation.Add(ShipThrustAllocation.GetThrustDirection(0d, 3), default, 100d);
        SupplyRequestedThrusters(allocation);
        NearVector(new SpaceVector2(2d, 0d), allocation.ResolveVelocityChange(), "turn-only upward nozzle does not alter simultaneous horizontal input");
        Near(1d, allocation.ResolveTurnAuthority(), "simultaneous translation keeps independent turn authority");
    }
    #endregion

    #region 复用工作区回归与工具
    private static void ThrusterAllocations()
    {
        var allocation = new ShipThrustAllocation();
        for (int i = 0; i < 100; i++) RunThrusterStep(allocation);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) RunThrusterStep(allocation);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(bytes == 0, $"reused directional allocation created {bytes} temporary bytes");
        NearVector(new SpaceVector2(.5d, .5d), allocation.ResolveVelocityChange(), "reused workspace retains only current-step fuel");
    }

    private static void RunThrusterStep(ShipThrustAllocation allocation)
    {
        allocation.Reset(new SpaceVector2(1d, 1d), 2d, .5d);
        AddCardinalThrusters(allocation);
        for (int i = 0; i < allocation.Count; i++) allocation.SetActualOutput(i, allocation.GetThrottle(i) * .5d);
        allocation.ResolveVelocityChange();
        allocation.ResolveTurnAuthority();
    }

    private static void AddCardinalThrusters(ShipThrustAllocation allocation, double angle = 0d)
    {
        for (int i = 0; i < 4; i++)
        {
            SpaceVector2 direction = ShipThrustAllocation.GetThrustDirection(angle, i);
            allocation.Add(direction, direction * -3d, 100d);
        }
    }

    private static void AddTurnThrusters(ShipThrustAllocation allocation)
    {
        SpaceVector2 right = ShipThrustAllocation.GetThrustDirection(0d, 3);
        SpaceVector2 left = ShipThrustAllocation.GetThrustDirection(0d, 1);
        allocation.Add(right, new SpaceVector2(0d, -2d), 100d);
        allocation.Add(left, new SpaceVector2(0d, 2d), 300d);
        allocation.Add(left, new SpaceVector2(0d, -2d), 100d);
        allocation.Add(right, new SpaceVector2(0d, 2d), 300d);
    }

    private static void SupplyRequestedThrusters(ShipThrustAllocation allocation)
    {
        for (int i = 0; i < allocation.Count; i++) allocation.SetActualOutput(i, allocation.GetThrottle(i));
    }

    private static void NearVector(SpaceVector2 expected, SpaceVector2 actual, string message)
    {
        Near(expected.X, actual.X, message + " X");
        Near(expected.Y, actual.Y, message + " Y");
    }
    #endregion
}
