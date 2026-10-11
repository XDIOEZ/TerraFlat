using System.Diagnostics;
using FlatWorld.Spaceflight;

internal static partial class Program
{
    #region 离线回归入口
    private static int passed;
    private static readonly SpaceGameplaySettings Settings = new();
    private static void Main()
    {
        Run("限速、反向制动、部分燃料", Velocity);
        Run("不同步长及横向初速下到点停稳", Arrival);
        Run("船头转向取最短方向", Heading);
        Run("星体拦截、近侧停靠及离开安全圈", SafeTargets);
        Run("缓存保留实时载荷及运动量", AssemblyRefresh);
        Run("对接、断开、损坏及碎片归属失效", AssemblyChanges);
        Run("光纤断开、修复及集合复用", Optical);
        Run("稳定拓扑与光纤查询无临时分配", Allocations);
        Run("船体、载荷与舱气质量及惯量", MassProperties);
        Run("载荷变动及重心移动保持船体坐标和点速度", MassChanges);
        Run("质量来源去重与非法输入拒绝", MassIdentity);
        Run("质量工作区跨船复用及热路径分配", MassWorkspace);
        Run("载荷池来源更新与全字段清理", ContributionPoolFields);
        Run("载荷池收缩、清空与不同船隔离", ContributionPoolIsolation);
        Run("载荷池稳定来源预热后无临时分配", ContributionPoolAllocations);
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
        Near(0d, velocity.Magnitude, "release stops exactly");
        var coast = new SpaceVector2(10d, 0d);
        Check(ShipCruiseGuidance.StepVelocity(coast, new SpaceVector2(120d, 0d), Settings, .02d, 0d) == coast, "empty tank cannot accelerate");
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
    private static void SafeTargets()
    {
        var first = new BodyState { BodyId = "near", PositionMeters = new SpaceVector2(100d, 0d), RadiusMeters = 10d };
        var far = new BodyState { BodyId = "far", PositionMeters = new SpaceVector2(200d, 0d), RadiusMeters = 10d };
        var bodies = new List<BodyState> { far, first };
        var target = ShipCruiseGuidance.SafeTarget(default, new SpaceVector2(100d, 0d), bodies, 5d, 12d, out var blocker);
        Near(72.99d, target.X, "planet center becomes near-side standoff");
        Check(blocker == first, "nearest blocker selected regardless of order");
        target = ShipCruiseGuidance.SafeTarget(default, new SpaceVector2(300d, 0d), bodies, 5d, 12d, out blocker);
        Near(72.99d, target.X, "route crossing a planet stops before it");
        target = ShipCruiseGuidance.SafeTarget(default, new SpaceVector2(0d, 300d), bodies, 5d, 12d, out blocker);
        Check(blocker == null && target.Y == 300d, "clear route unchanged");
        target = ShipCruiseGuidance.SafeTarget(new SpaceVector2(80d, 0d), default, bodies, 5d, 12d, out blocker);
        Check(blocker == null && target == default, "ship can leave safety shell");
        target = ShipCruiseGuidance.SafeTarget(new SpaceVector2(80d, 0d), new SpaceVector2(100d, 0d), bodies, 5d, 12d, out blocker);
        Near(80d, target.X, "inside shell cannot navigate further inward");
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
