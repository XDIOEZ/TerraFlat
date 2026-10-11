using FlatWorld.Spaceflight;

internal static class Program
{
    #region 接触入口回归
    private static int assertions;
    private static void Main()
    {
        DistantExplosionDebrisSkipsAllWork();
        SpaceContactsPublishAndRefreshImpulse();
        SurfaceRealmAndHeightFilter();
        CandidateChangesAndDockingTopology();
        Console.WriteLine($"PASS: {assertions} ship contact assertions.");
    }

    private static void DistantExplosionDebrisSkipsAllWork()
    {
        SpaceSession session = new();
        AddShip(session, "second-ship", ShipFlightPhase.Landed, "Earth_Surface", 175);
        SpaceLooseItemState[] debris = Enumerable.Range(0, 157)
            .Select(index => Loose("debris-" + index, true, null, invalidSnapshot: true)).ToArray();
        SpacePassengerState passenger = new() { ProfileId = "player", IsInSpace = true, FreeMotion = Motion() };
        SpaceSession.ResetCounters();
        bool hit = false;
        // 预热 JIT 后只测真实入口，计数和断言都留在分配窗口外。
        for (int warm = 0; warm < 8; warm++)
            foreach (SpaceLooseItemState item in debris) hit |= session.ResolveLooseItemContact(item, new(-.6d, 0d), .02d);
        hit |= session.ResolveFreePassengerContact(passenger, new(-.6d, 0d), .02d);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int round = 0; round < 100; round++)
            foreach (SpaceLooseItemState item in debris) hit |= session.ResolveLooseItemContact(item, new(-.6d, 0d), .02d);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert(!hit, "太空碎片和乘员不能撞到地表船。");
        Assert(SpaceSession.DecodeCalls == 0, "没有候选船时不得解码散落物。");
        Assert(ShipMassReader.ResetCalls == 0 && ShipMassReader.ReadCalls == 0, "没有候选船时不得读取重量。");
        Assert(session.TestContactRebuildCount == 0, "没有候选船时不得建立对接整体。");
        Assert(allocated == 0, $"157件碎片重复100轮预热后分配应为0，实际 {allocated} 字节。");
        Console.WriteLine($"Explosion fixture: 157 debris, 175 landed pieces, 15700 calls, {allocated} bytes allocated.");
    }

    private static void SpaceContactsPublishAndRefreshImpulse()
    {
        SpaceSession session = new();
        ShipState ship = AddShip(session, "space-ship", ShipFlightPhase.Cruising, "Earth_Surface");
        SpaceLooseItemState first = Loose("first", true, null);
        Assert(session.ResolveLooseItemContact(first, new(-.6d, 0d), .02d), "第一件散落物应撞到太空船。");
        double firstVelocity = ship.VelocityX;
        Near(firstVelocity, 10d / 6d, "第一件散落物的冲量应传给船。");
        Near(session.GetFlight(ship.ShipId).Motion.VelocityMetersPerSecond.X, firstVelocity, "冲量应写回飞行记录。");
        int rebuilds = session.TestContactRebuildCount;
        SpaceLooseItemState second = Loose("second", true, null);
        SpaceVector2 previous = new(ship.PositionX - .6d, 0d);
        second.Motion.PositionMeters = previous + second.Motion.VelocityMetersPerSecond * .02d;
        Assert(session.ResolveLooseItemContact(second, previous, .02d), "第二件散落物应继续碰撞。");
        Near(ship.VelocityX, (2d * 10d + 10d * firstVelocity) / 12d, "第二件应读取前一件写回后的船速。");
        Assert(ship.VelocityX > firstVelocity, "连续碰撞应累积真实动量。");
        Assert(session.TestContactRebuildCount == rebuilds, "拓扑不变时复用同一个接触整体。");
        Near(second.Motion.VelocityMetersPerSecond.X, ship.VelocityX, "正中接触后双方速度应相同。");
    }

    private static void SurfaceRealmAndHeightFilter()
    {
        SpaceSession session = new();
        AddShip(session, "landed", ShipFlightPhase.Landed, "Earth_Surface");
        SpaceSession.ResetCounters();
        SpaceLooseItemState otherWorld = Loose("other-world", false, "Moon_Surface", invalidSnapshot: true);
        Assert(!session.ResolveLooseItemContact(otherWorld, new(-.6d, 0d), .02d), "不同地表不能接触。");
        Assert(SpaceSession.DecodeCalls == 0 && ShipMassReader.ReadCalls == 0, "不同地表不得解码或计重。");
        SpaceLooseItemState overhead = Loose("overhead", false, "Earth_Surface");
        overhead.SurfaceHeight = 10d;
        Assert(!session.ResolveLooseItemContact(overhead, new(-.6d, 0d), .02d), "高度不相交不能接触。");
        SpaceLooseItemState sameWorld = Loose("same-world", false, "Earth_Surface");
        Assert(session.ResolveLooseItemContact(sameWorld, new(-.6d, 0d), .02d), "同地表同高度仍须正常接触。");
        Near(sameWorld.Motion.VelocityMetersPerSecond.X, 0d, "地面静止船应挡住物品。");
    }

    private static void CandidateChangesAndDockingTopology()
    {
        SpaceSession session = new();
        ShipState first = AddShip(session, "a", ShipFlightPhase.Landed, "Earth_Surface");
        Assert(!session.ResolveLooseItemContact(Loose("before", true, null, true), new(-.6d, 0d), .02d), "无太空船时应过滤。");
        session.GetFlight(first.ShipId).Phase = ShipFlightPhase.Cruising;
        Assert(session.ResolveLooseItemContact(Loose("after-phase", true, null), new(-.6d, 0d), .02d), "Phase改变后应立即可碰撞。");
        session.GetFlight(first.ShipId).Phase = ShipFlightPhase.Landed;
        SpaceSession.ResetCounters();
        Assert(!session.ResolveLooseItemContact(Loose("after-land", true, null, true), new(-.6d, 0d), .02d), "落地后应立即重新过滤。");
        Assert(SpaceSession.DecodeCalls == 0, "Phase过滤不依赖旧缓存结果。");
        ShipState added = AddShip(session, "b", ShipFlightPhase.Cruising, "Earth_Surface");
        Assert(session.ResolveLooseItemContact(Loose("new-ship", true, null), new(-.6d, 0d), .02d), "新增太空船后应立即可碰撞。");
        Assert(session.TestContactAssembly(added.ShipId) != null, "接触拓扑应包含新增船。");

        // 改变对接关系后组合缓存必须重新分组，拆开后双方恢复独立身份。
        SpaceSession docking = new();
        ShipState a = AddShip(docking, "dock-a", ShipFlightPhase.Cruising, "Earth_Surface");
        ShipState b = AddShip(docking, "dock-b", ShipFlightPhase.Cruising, "Earth_Surface");
        b.PositionX = 3d;
        AddPort(a, "port-a"); AddPort(b, "port-b");
        ShipDockingPairState pair = new() { ShipAId = a.ShipId, ShipBId = b.ShipId, PortAId = "port-a", PortBId = "port-b" };
        docking.State.Docking.Add(pair);
        docking.ResolveLooseItemContact(Loose("dock-before", true, null), new(-.6d, 0d), .02d);
        Assert(!ReferenceEquals(docking.TestContactAssembly(a.ShipId), docking.TestContactAssembly(b.ShipId)), "未确认接口为两艘独立船。");
        int rebuilds = docking.TestContactRebuildCount;
        pair.Connected = pair.ConfirmedA = pair.ConfirmedB = true;
        docking.ResolveLooseItemContact(Loose("dock-joined", true, null), new(-.6d, 0d), .02d);
        Assert(ReferenceEquals(docking.TestContactAssembly(a.ShipId), docking.TestContactAssembly(b.ShipId)), "对接改变后应形成同一整体。");
        Assert(docking.TestContactRebuildCount == rebuilds + 1, "对接变化应重建一次拓扑。");
        pair.Connected = pair.ConfirmedA = pair.ConfirmedB = false;
        docking.ResolveLooseItemContact(Loose("dock-split", true, null), new(-.6d, 0d), .02d);
        Assert(!ReferenceEquals(docking.TestContactAssembly(a.ShipId), docking.TestContactAssembly(b.ShipId)), "解除对接后恢复独立整体。");
    }
    #endregion

    #region 几何夹具与断言
    private static ShipState AddShip(SpaceSession session, string id, ShipFlightPhase phase, string world, int count = 1)
    {
        ShipState ship = new()
        {
            ShipId = id, WorldAddress = world, MassKg = 10d, InertiaKgM2 = 10d,
            CenterOfMassLocalX = .5d, CenterOfMassLocalY = .5d
        };
        for (int i = 0; i < count; i++)
            ship.Pieces.Add(new ShipPieceState { PieceId = id + ":" + i, Kind = ShipPieceKind.Wall, CellX = i, BlocksMovement = true });
        session.State.Ships.Add(ship);
        session.State.Flights.Add(new ShipFlightState { ShipId = id, Phase = phase, SurfaceWorldKey = world });
        return ship;
    }
    private static void AddPort(ShipState ship, string id)
        => ship.Pieces.Add(new ShipPieceState { PieceId = id, Kind = ShipPieceKind.DockingPort, CellX = 1 });
    private static SpaceMotionState Motion()
        => new() { PositionMeters = new(-.4d, 0d), VelocityMetersPerSecond = new(10d, 0d), RadiusMeters = .15d };
    private static SpaceLooseItemState Loose(string id, bool inSpace, string world, bool invalidSnapshot = false)
        => new() { Id = id, InSpace = inSpace, WorldKey = world, Snapshot = invalidSnapshot ? "must-not-decode" : "contact-fixture", Motion = Motion() };
    private static void Assert(bool value, string message)
    {
        assertions++;
        if (!value) throw new InvalidOperationException(message);
    }
    private static void Near(double actual, double expected, string message)
        => Assert(Math.Abs(actual - expected) < 1e-8d, $"{message} actual={actual:R}, expected={expected:R}");
    #endregion
}
