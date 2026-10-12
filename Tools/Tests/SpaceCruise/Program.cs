using System.Diagnostics;
using FlatWorld.Spaceflight;

internal static partial class Program
{
    #region 离线回归入口
    private static int passed;
    private static readonly SpaceGameplaySettings Settings = new();
    private static void Main()
    {
        Run("会话身份索引增删、替换与保存恢复", QueryIndexMutations);
        Run("星区负向、多区、高位整数与空域惯性", SectorCoordinates);
        Run("边界预加载、自由穿越、折返及多人资源租约", SectorStreaming);
        Run("跨区星体扫掠与巨大编号局部坐标", SectorSurfaceContacts);
        Run("跨边界对接整体的重心、惯量与刚体携带", SectorDocking);
        Run("跨界自由乘员、支撑乘员及极大编号接触", SectorPassengerContacts);
        Run("稳定船体身份查询无临时分配", QueryIndexAllocations);
        Run("散落物缩放仅保存分量并完整往返", LooseItemScaleSnapshot);
        Run("完整太空会话散落物保存、读回和重复保存", LooseItemSessionSnapshot);
        Run("限速、反向制动、部分燃料", Velocity);
        Run("松键滑行、横向推力、无动力及超速碰撞惯性", ManualInertia);
        Run("四向与斜向喷嘴、船体旋转和对接成员朝向", ThrusterDirectionRules);
        Run("喷嘴按推力需求选择、反向制动与松键滑行", ThrusterDemandRules);
        Run("单轴缺油、部分供油与同向多引擎容量", ThrusterFuelRules);
        Run("转向喷嘴力矩与平移转向独立授权", ThrusterTurnRules);
        Run("喷嘴分配工作区复用无临时分配", ThrusterAllocations);
        Run("乘员继承船速、松键及转向保留漂移、碰撞冲量与快照恢复", PassengerInertia);
        Run("降落范围、星体上方自由飞行及实际降落姿态", LandingBoundary);
        Run("椭圆近远点、公转周期、父星运动和快照恢复", OrbitMotion);
        Run("移动星体扫掠、起飞边缘停留和星系更新无分配", MovingBodyEntry);
        Run("延长起降、实际推力不足和重新点火", SurfaceFlightTiming);
        Run("不同步长及横向初速下到点停稳", Arrival);
        Run("船头转向取最短方向", Heading);
        Run("导航到星体画面中心及穿过星体到达航点", OverflightNavigation);
        Run("缓存保留实时载荷及运动量", AssemblyRefresh);
        Run("对接、断开、损坏及碎片归属失效", AssemblyChanges);
        Run("光纤断开、修复及集合复用", Optical);
        Run("稳定拓扑与光纤查询无临时分配", Allocations);
        Run("船体、载荷与舱气质量及惯量", MassProperties);
        Run("载荷变动及重心移动保持船体坐标和点速度", MassChanges);
        Run("增量质量账本、动态载荷移交及重心修正", IncrementalMassLedger);
        Run("质量来源去重与非法输入拒绝", MassIdentity);
        Run("质量工作区跨船复用及热路径分配", MassWorkspace);
        Run("载荷池来源更新与全字段清理", ContributionPoolFields);
        Run("载荷池收缩、清空与不同船隔离", ContributionPoolIsolation);
        Run("载荷池稳定来源预热后无临时分配", ContributionPoolAllocations);
        Run("引擎外缘朝向、缺口、凹形与旋转占地", EnginePlacementRules);
        Run("后续安装保留引擎喷口通道", EnginePlacementAdditions);
        Run("GM模板六台引擎有效且结构层无重叠", EnginePlacementTemplate);
        Benchmark();
        MassBenchmark();
        Console.WriteLine($"PASS: {passed} regression groups");
    }
    private static void Run(string name, Action test)
    {
        test(); passed++; Console.WriteLine($"PASS {name}");
    }
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
    private static void Near(double expected, double actual, string message, double tolerance = 1e-7d)
        => Check(Math.Abs(expected - actual) <= tolerance, $"{message}: expected {expected}, actual {actual}");
    #endregion

    #region 航行规则
    private static void Velocity()
    {
        var velocity = new SpaceVector2();
        for (int i = 0; i < 1000; i++) velocity = ShipCruiseGuidance.StepVelocity(velocity, new SpaceVector2(999d, 0d), Settings, .02d);
        Near(120d, velocity.X, "speed cap");
        velocity = ShipCruiseGuidance.StepVelocity(new SpaceVector2(120d, 0d), new SpaceVector2(-120d, 0d), Settings, .02d);
        Near(118d, velocity.X, "reverse uses brakes");
        velocity = ShipCruiseGuidance.StepVelocity(default, new SpaceVector2(120d, 0d), Settings, .02d, .1d);
        Near(.12d, velocity.X, "ten percent fuel gives ten percent acceleration");
        velocity = new SpaceVector2(100d, 50d);
        for (int i = 0; i < 100; i++) velocity = ShipCruiseGuidance.StepVelocity(velocity, default, Settings, .02d);
        Near(0d, velocity.Magnitude, "powered navigation brakes exactly");
        var coast = new SpaceVector2(10d, 0d);
        Check(ShipCruiseGuidance.StepVelocity(coast, new SpaceVector2(120d, 0d), Settings, .02d, 0d) == coast, "empty tank cannot accelerate");
    }

    private static void ManualInertia()
    {
        var coast = new SpaceVector2(35d, 12d);
        var current = coast;
        for (int i = 0; i < 500; i++)
            current = ShipCruiseGuidance.StepVelocity(current,
                ShipCruiseGuidance.ManualVelocity(current, default, Settings, .02d), Settings, .02d);
        Check(current == coast, "released input must preserve full velocity over ten seconds");
        var sideways = ShipCruiseGuidance.ManualVelocity(coast, new SpaceVector2(0d, 1d), Settings, .02d);
        Near(coast.X, sideways.X, "sideways thrust cannot cancel existing drift");
        Near(coast.Y + 1.2d, sideways.Y, "sideways thrust adds bounded acceleration");
        var reverse = ShipCruiseGuidance.ManualVelocity(new SpaceVector2(120d, 0d), new SpaceVector2(-1d, 0d), Settings, .02d);
        Near(118d, reverse.X, "reverse input must brake gradually");
        var overspeed = new SpaceVector2(180d, 0d);
        Check(ShipCruiseGuidance.StepVelocity(overspeed, default, Settings, .02d, 0d) == overspeed,
            "lost power or fuel cannot silently cap collision speed");
        Near(178d, ShipCruiseGuidance.StepVelocity(overspeed, default, Settings, .02d, 1d).X,
            "powered braking of collision speed is still gradual");
        var diagonal = ShipCruiseGuidance.ManualVelocity(default, new SpaceVector2(1d, 1d), Settings, .02d);
        Near(1.2d, diagonal.Magnitude, "multiple inputs cannot multiply acceleration");
        var partial = ShipCruiseGuidance.StepVelocity(coast, sideways, Settings, .02d, .25d);
        Near(coast.X, partial.X, "partial fuel preserves lateral drift");
        Near(coast.Y + .3d, partial.Y, "partial fuel proportionally limits thrust");
        var smallRequest = ShipCruiseGuidance.ManualVelocity(default, new SpaceVector2(.1d, 0d), Settings, .02d);
        var smallPartial = ShipCruiseGuidance.StepVelocity(default, smallRequest, Settings, .02d, .05d);
        Near(.06d, smallPartial.X, "half fuel on ten percent throttle gives half the requested change");
    }

    private static void PassengerInertia()
    {
        var ship = new ShipState { PositionX = 1000d, PositionY = 1000d, VelocityX = 120d, VelocityY = 30d };
        ShipGeometry.PointVelocity(ship, ship.PositionX + 2d, ship.PositionY + 3d, out double vx, out double vy);
        var passenger = new SpacePassengerState { IsInSpace = true,
            FreeMotion = new SpaceMotionState { PositionMeters = new SpaceVector2(1002d, 1003d),
                VelocityMetersPerSecond = new SpaceVector2(vx, vy) } };
        var universe = new UniverseSimulation(new UniverseState { Bodies = new List<BodyState>
            { new BodyState { BodyId = "earth", PlanetId = "earth", RadiusMeters = 1d } } });
        for (int i = 0; i < 500; i++)
        {
            passenger.UpdateFreeWalkVelocity(default);
            universe.StepFreeMotion(passenger.FreeMotion, default, .02d);
        }
        Near(120d, passenger.FreeMotion.VelocityMetersPerSecond.X, "release input preserves inherited ship speed above walking cap");
        Near(30d, passenger.FreeMotion.VelocityMetersPerSecond.Y, "release input preserves lateral ship speed");
        Near(1200d, passenger.FreeMotion.PositionMeters.X - 1002d, "ten seconds outside coasting ship maintains relative distance");
        passenger.UpdateFreeWalkVelocity(new SpaceVector2(0d, 10d));
        Near(120d, passenger.FreeMotion.VelocityMetersPerSecond.X, "sideways walking keeps carrier drift");
        Near(40d, passenger.FreeMotion.VelocityMetersPerSecond.Y, "walking adds relative velocity");
        passenger.FreeMotion.VelocityMetersPerSecond += new SpaceVector2(-80d, 5d);
        passenger.UpdateFreeWalkVelocity(new SpaceVector2(-10d, 0d));
        passenger.UpdateFreeWalkVelocity(default);
        Near(40d, passenger.FreeMotion.VelocityMetersPerSecond.X, "turning and stopping preserve collision impulse");
        Near(35d, passenger.FreeMotion.VelocityMetersPerSecond.Y, "stopping removes only walking velocity");
        passenger.UpdateFreeWalkVelocity(new SpaceVector2(3d, 4d));
        var restored = Newtonsoft.Json.JsonConvert.DeserializeObject<SpacePassengerState>(
            Newtonsoft.Json.JsonConvert.SerializeObject(passenger));
        restored.UpdateFreeWalkVelocity(default);
        Near(40d, restored.FreeMotion.VelocityMetersPerSecond.X, "snapshot restores driven component separately");
        Near(35d, restored.FreeMotion.VelocityMetersPerSecond.Y, "loaded input release preserves drift");
        ship.AngularVelocityRadiansPerSecond = .5d;
        ShipGeometry.PointVelocity(ship, ship.PositionX + 2d, ship.PositionY + 3d, out vx, out vy);
        Near(118.5d, vx, "departure inherits rotational point velocity x");
        Near(31d, vy, "departure inherits rotational point velocity y");
        Near(.35d, Settings.FreeFloatAccelerationMultiplier * 3d, "space acceleration uses one third of ice", 1e-7d);
    }

    private static void LandingBoundary()
    {
        var body = new BodyState { BodyId = "target", PlanetId = "planet", RadiusMeters = 100d };
        var bodies = new[] { body };
        Check(ShipLandingGuidance.FindNearbyBody(new SpaceVector2(211d, 0d), 10d, bodies) == null,
            "far ships cannot request landing");
        Check(ShipLandingGuidance.FindNearbyBody(new SpaceVector2(210d, 0d), 10d, bodies) == body,
            "landing range includes ship radius");
        var universe = new UniverseSimulation(new UniverseState { Bodies = new List<BodyState> { body } });
        var motion = new SpaceMotionState { PositionMeters = new SpaceVector2(140d, 0d),
            VelocityMetersPerSecond = new SpaceVector2(-60d, 9d), RadiusMeters = 10d };
        universe.StepFreeMotion(motion, default, 1d);
        Near(80d, motion.PositionMeters.X, "ship flies over planet disk instead of stopping at its boundary");
        Near(9d, motion.PositionMeters.Y, "lateral drift remains intact over planet");
        Near(-60d, motion.VelocityMetersPerSecond.X, "planet overlap does not brake inward velocity");
        Check(ShipLandingGuidance.FindNearbyBody(motion.PositionMeters, motion.RadiusMeters, bodies) == body,
            "landing button is available while ship overlaps planet disk");
        universe.StepFreeMotion(motion, default, 4d);
        Near(-160d, motion.PositionMeters.X, "ship crosses the whole planet without being intercepted");
        var accelerated = new SpaceMotionState { PositionMeters = default, RadiusMeters = 10d };
        universe.StepFreeMotion(accelerated, new SpaceVector2(2d, 0d), 2d);
        Near(4d, accelerated.PositionMeters.X, "movement can start inside planet disk");
        Near(4d, accelerated.VelocityMetersPerSecond.X, "free flight retains acceleration integration");
        var explicitDescent = ShipLandingGuidance.CreateDescentContact(body, motion.PositionMeters, motion.VelocityMetersPerSecond, 2d);
        Check(explicitDescent.IsValid && explicitDescent.BodyId == body.BodyId,
            "explicit request uses the actual nearby body");
        Near(motion.PositionMeters.X, explicitDescent.PositionMeters.X, "descent uses actual position");
        Near(motion.VelocityMetersPerSecond.Y, explicitDescent.RelativeVelocityMetersPerSecond.Y, "descent preserves actual entry speed");
        var freePassenger = new SpaceMotionState { PositionMeters = new SpaceVector2(140d, 0d),
            VelocityMetersPerSecond = new SpaceVector2(-60d, 0d), RadiusMeters = 1d };
        Check(universe.StepMotionAtTime(freePassenger, default, 0d, 1d, out _),
            "independent passenger contact rules remain available");
    }
    private static void Arrival()
    {
        foreach (double dt in new[] { .01d, .02d, .05d, .1d })
        foreach (SpaceVector2 initial in new[] { new SpaceVector2(), new SpaceVector2(-80d, 0d), new SpaceVector2(0d, 120d), new SpaceVector2(120d, 0d) })
        foreach (SpaceVector2 target in new[] { new SpaceVector2(500d, 0d), new SpaceVector2(2d, 2d) })
        {
            SpaceVector2 position = default, velocity = initial;
            bool reached = false;
            for (int i = 0; i < 20000; i++)
            {
                SpaceVector2 error = target - position;
                if (error.Magnitude <= Settings.NavigationArrivalDistanceMeters && velocity.Magnitude <= Settings.NavigationArrivalSpeedMetersPerSecond)
                { reached = true; break; }
                velocity = ShipCruiseGuidance.StepVelocity(velocity, ShipCruiseGuidance.ArrivalVelocity(error, Settings, dt), Settings, dt);
                position += velocity * dt;
                Check(position.IsFinite && velocity.IsFinite && velocity.Magnitude <= 120.00001d, "arrival remains finite and bounded");
            }
            Check(reached, $"arrival did not settle: dt={dt}, initial={initial.X},{initial.Y}, remaining={(target - position).Magnitude}");
        }
    }
    private static void Heading()
    {
        double turn = ShipCruiseGuidance.TurnToward(Math.PI - .01d, SpaceVector2.FromAngle(-Math.PI + .01d + Math.PI * .5d), Settings, .02d);
        Check(turn > 0d && turn <= 1d, "heading wraps via short arc");
        Near(0d, ShipCruiseGuidance.TurnToward(0d, new SpaceVector2(0d, 1d), Settings, .02d), "local up is forward");
    }
    private static void OverflightNavigation()
    {
        var body = new BodyState { BodyId = "planet", PlanetId = "planet", RadiusMeters = 100d };
        var universe = new UniverseSimulation(new UniverseState { Bodies = new List<BodyState> { body } });
        foreach (SpaceVector2 target in new[] { new SpaceVector2(), new SpaceVector2(250d, 0d) })
        {
            var motion = new SpaceMotionState { PositionMeters = new SpaceVector2(-250d, 0d), RadiusMeters = 5d };
            bool crossedDisk = false, reached = false;
            for (int i = 0; i < 10000; i++)
            {
                SpaceVector2 error = target - motion.PositionMeters;
                if (error.Magnitude <= Settings.NavigationArrivalDistanceMeters && motion.VelocityMetersPerSecond.Magnitude < .001d)
                { reached = true; break; }
                motion.VelocityMetersPerSecond = ShipCruiseGuidance.StepVelocity(motion.VelocityMetersPerSecond,
                    ShipCruiseGuidance.ArrivalVelocity(error, Settings, .02d), Settings, .02d);
                universe.StepFreeMotion(motion, default, .02d);
                crossedDisk |= motion.PositionMeters.Magnitude < body.RadiusMeters;
            }
            Check(reached && crossedDisk, "navigation reaches requested point over or beyond planet");
        }
    }
    #endregion

    #region 对接与光纤
    private static ShipState Ship(string id, double x)
    {
        var ship = new ShipState { ShipId = id, PositionX = x, MassKg = 10d, InertiaKgM2 = 3d };
        ship.Pieces.Add(new ShipPieceState { PieceId = id + "-port", Kind = ShipPieceKind.DockingPort });
        ship.Pieces.Add(new ShipPieceState { PieceId = id + "-console", Kind = ShipPieceKind.Console });
        return ship;
    }
    private static ShipDockingPairState Pair(ShipState a, ShipState b) => new()
    {
        ShipAId = a.ShipId, PortAId = a.Pieces[0].PieceId, ShipBId = b.ShipId, PortBId = b.Pieces[0].PieceId,
        Connected = true, ConfirmedA = true, ConfirmedB = true
    };
    private static void AssemblyRefresh()
    {
        var a = Ship("a", 0d); var b = Ship("b", 10d);
        var ships = new List<ShipState> { a, b }; var pairs = new List<ShipDockingPairState> { Pair(a, b) };
        var cache = new ShipAssemblyCache();
        var initial = cache.Refresh(ships, pairs)[0];
        a.MassKg = 30d; a.VelocityX = 2d; b.PositionY = 4d; b.AngularVelocityRadiansPerSecond = .4d;
        var actual = cache.Refresh(ships, pairs)[0];
        var expected = ShipDockingService.RebuildAssemblies(ships, pairs)[0];
        Check(ReferenceEquals(initial, actual) && cache.RebuildCount == 1, "motion does not rebuild topology");
        Near(expected.MassKg, actual.MassKg, "mass"); Near(expected.PositionX, actual.PositionX, "center x");
        Near(expected.PositionY, actual.PositionY, "center y"); Near(expected.VelocityX, actual.VelocityX, "velocity");
        Near(expected.InertiaKgM2, actual.InertiaKgM2, "inertia"); Near(expected.AngularVelocityRadiansPerSecond, actual.AngularVelocityRadiansPerSecond, "angular momentum");
        Check(cache.AreConnected(ships, pairs, "a", "b"), "connected lookup");
        Check(!cache.AreConnected(ships, pairs, null, null), "missing ids are not an assembly");
        a.MassKg = b.MassKg = 0d;
        actual = cache.Refresh(ships, pairs)[0];
        Near(a.PositionX, actual.PositionX, "zero mass uses primary pose");
    }
    private static void AssemblyChanges()
    {
        var a = Ship("a", 0d); var b = Ship("b", 10d);
        var ships = new List<ShipState> { a, b }; var link = Pair(a, b);
        var pairs = new List<ShipDockingPairState> { link }; var cache = new ShipAssemblyCache();
        Check(cache.Refresh(ships, pairs).Count == 1, "initial docking");
        Check(ShipDockingService.Disconnect(ships, pairs, link.PairId), "disconnect accepted");
        Check(cache.Refresh(ships, pairs).Count == 2 && !link.ConfirmedA && !link.ConfirmedB, "disconnect invalidates cache and confirmations");
        link.Connected = link.ConfirmedA = link.ConfirmedB = true;
        Check(cache.Refresh(ships, pairs).Count == 1, "reconnection invalidates");
        a.Pieces[0].Destroyed = true;
        Check(cache.Refresh(ships, pairs).Count == 2 && !link.Connected, "port destruction disconnects");
        a.Pieces[0].Destroyed = false;
        link.Connected = link.ConfirmedA = link.ConfirmedB = true;
        cache.Refresh(ships, pairs);
        var fragment = Ship("fragment", 20d);
        fragment.Pieces.Clear(); fragment.Pieces.Add(a.Pieces[0]); a.Pieces.RemoveAt(0); ships.Add(fragment);
        Check(cache.Refresh(ships, pairs).Count == 2 && link.ShipAId == "fragment", "split follows stable port identity");
        Check(cache.AreConnected(ships, pairs, "fragment", "b") && !cache.AreConnected(ships, pairs, "a", "b"), "new ownership lookup");
        ships.Remove(b);
        Check(cache.Refresh(ships, pairs).Count == 2 && !link.Connected, "removed ship invalidates link");
        cache.Clear();
        Check(cache.Find("fragment") == null && cache.RebuildCount == 0, "session cleanup");
    }
    private static (ShipState Ship, ShipPieceState From, ShipPieceState To) OpticalShip()
    {
        var ship = Ship("optical", 0d);
        var from = new ShipPieceState { CellX = 0, Kind = ShipPieceKind.Console };
        var to = new ShipPieceState { CellX = 8, Kind = ShipPieceKind.Navigation };
        ship.Pieces.Clear(); ship.Pieces.Add(from); ship.Pieces.Add(to);
        for (int x = 1; x < 8; x++) ship.Pieces.Add(new ShipPieceState { CellX = x, Kind = ShipPieceKind.Fiber });
        return (ship, from, to);
    }
    private static void Optical()
    {
        var (ship, from, to) = OpticalShip(); var workspace = new ShipOpticalGraph.Workspace();
        Check(ShipOpticalGraph.Connected(ship, from, to, workspace), "fiber connects");
        ship.Pieces[4].Health = 0d;
        Check(!ShipOpticalGraph.Connected(ship, from, to, workspace), "broken fiber disconnects without stale cache");
        ship.Pieces[4].Health = 100d;
        Check(ShipOpticalGraph.Connected(ship, from, to, workspace), "repair reconnects");
        to.CellY = 1;
        Check(!ShipOpticalGraph.Connected(ship, from, to, workspace), "diagonal is not a connection");
        to.CellY = 0; to.Destroyed = true;
        Check(!ShipOpticalGraph.Connected(ship, from, to, workspace), "destroyed endpoint rejected");
    }
    private static void Allocations()
    {
        var a = Ship("a", 0d); var b = Ship("b", 10d);
        var ships = new List<ShipState> { a, b }; var pairs = new List<ShipDockingPairState> { Pair(a, b) };
        var cache = new ShipAssemblyCache();
        var (ship, from, to) = OpticalShip(); var workspace = new ShipOpticalGraph.Workspace();
        for (int i = 0; i < 100; i++) { cache.Refresh(ships, pairs); ShipOpticalGraph.Connected(ship, from, to, workspace); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++)
        {
            cache.Refresh(ships, pairs); cache.AreConnected(ships, pairs, "a", "b");
            ShipOpticalGraph.Connected(ship, from, to, workspace);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0, $"steady-state kernels allocated {allocated} bytes");
    }
    #endregion

    #region 同机分配与耗时对照
    private static void Benchmark()
    {
        var ships = new List<ShipState>(); var pairs = new List<ShipDockingPairState>();
        for (int i = 0; i < 32; i++)
        {
            var ship = Ship(i.ToString("D3"), i * 100d);
            for (int j = 0; j < 64; j++) ship.Pieces.Add(new ShipPieceState { Kind = ShipPieceKind.Floor });
            ships.Add(ship);
        }
        var cache = new ShipAssemblyCache(); cache.Refresh(ships, pairs);
        const int iterations = 1000;
        var timer = new Stopwatch();
        long before = GC.GetAllocatedBytesForCurrentThread(); timer.Start();
        for (int i = 0; i < iterations; i++) ShipDockingService.RebuildAssemblies(ships, pairs);
        timer.Stop(); long originalBytes = GC.GetAllocatedBytesForCurrentThread() - before; double originalMs = timer.Elapsed.TotalMilliseconds;
        before = GC.GetAllocatedBytesForCurrentThread(); timer.Restart();
        for (int i = 0; i < iterations; i++) cache.Refresh(ships, pairs);
        timer.Stop(); long cachedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Console.WriteLine($"BENCH 32 ships / {iterations} topology refreshes: original {originalMs:F2} ms, {originalBytes} B; cached {timer.Elapsed.TotalMilliseconds:F2} ms, {cachedBytes} B");
    }
    #endregion
}
