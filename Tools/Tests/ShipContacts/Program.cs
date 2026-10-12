using FlatWorld.Spaceflight;

internal static class Program
{
    #region 接触入口回归
    private static int assertions;
    private static void Main()
    {
        DistantExplosionDebrisSkipsAllWork();
        DistantDebrisInSameRealmSkipsAllWork();
        NearbyContactWorkspaceHasNoAllocations();
        MovingShipBroadphaseIncludesSweep();
        SpaceContactsPublishAndRefreshImpulse();
        SurfaceRealmAndHeightFilter();
        CandidateChangesAndDockingTopology();
        CrossSectorContactsAndFarIsolation();
        CrossSectorShipOverlapKeepsLocalPrecision();
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

    private static void DistantDebrisInSameRealmSkipsAllWork()
    {
        SpaceSession session = new();
        ShipState ship = AddShip(session, "third-ship", ShipFlightPhase.Cruising, "Earth_Surface", 175);
        ship.PositionX = -32560d; ship.PositionY = -2284d;
        session.CaptureShipContactPoses();
        SpaceLooseItemState[] debris = Enumerable.Range(0, 166)
            .Select(index => Loose("old-debris-" + index, true, null, invalidSnapshot: true)).ToArray();
        for (int warm = 0; warm < 8; warm++)
            foreach (SpaceLooseItemState item in debris) session.ResolveLooseItemContact(item, new(-.6d, 0d), .02d);
        SpaceSession.ResetCounters();
        long before = GC.GetAllocatedBytesForCurrentThread();
        bool hit = false;
        for (int round = 0; round < 100; round++)
            foreach (SpaceLooseItemState item in debris) hit |= session.ResolveLooseItemContact(item, new(-.6d, 0d), .02d);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert(!hit, "同在太空但相隔数千米的残骸不能碰撞。");
        Assert(SpaceSession.DecodeCalls == 0 && ShipMassReader.ReadCalls == 0, "远处碎片不解码、不计重。");
        Assert(session.TestContactRebuildCount == 0, "远处碎片不重建船体整体。");
        Assert(allocated == 0, $"16600次同空间远处接触入口应无分配，实际{allocated}字节。");
        Near(ship.VelocityX, 0d, "远处碎片不能改变船速。");
        Console.WriteLine($"Same-space fixture: 166 debris, 175 ship pieces, 16600 calls, {allocated} bytes allocated.");
        ship.PositionX = ship.PositionY = 0d;
        session.CaptureShipContactPoses();
        Assert(session.ResolveLooseItemContact(Loose("now-nearby", true, null), new(-.6d, 0d), .02d), "移动到身边后立即恢复真实碰撞。");
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

    private static void NearbyContactWorkspaceHasNoAllocations()
    {
        SpaceSession session = new();
        AddShip(session, "nearby", ShipFlightPhase.Cruising, "Earth_Surface");
        session.CaptureShipContactPoses();
        SpaceLooseItemState item = Loose("near-corner", true, null);
        item.Motion.PositionMeters = new(-.65d, .65d); item.Motion.VelocityMetersPerSecond = default;
        for (int warm = 0; warm < 8; warm++) session.ResolveLooseItemContact(item, item.Motion.PositionMeters, .02d);
        SpaceSession.ResetCounters();
        bool hit = false;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) hit |= session.ResolveLooseItemContact(item, item.Motion.PositionMeters, .02d);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert(!hit && ShipMassReader.ReadCalls == 10000, "近角落候选必须进入真实接触算法，但不能产生虚假碰撞。");
        Assert(allocated == 0, $"近处接触容器复用后应无分配，实际{allocated}字节。");
        Console.WriteLine($"Nearby fixture: 10000 contact queries, {allocated} bytes allocated.");
    }

    private static void MovingShipBroadphaseIncludesSweep()
    {
        SpaceSession session = new();
        ShipState ship = AddShip(session, "fast-ship", ShipFlightPhase.Cruising, "Earth_Surface");
        ship.PositionX = -10d; ship.VelocityX = 1000d;
        session.CaptureShipContactPoses();
        ship.PositionX = 10d;
        SpaceLooseItemState item = Loose("crossed", true, null);
        item.Motion.PositionMeters = default; item.Motion.VelocityMetersPerSecond = default;
        Assert(session.ResolveLooseItemContact(item, default, .02d), "快速船穿过碎片时，远距筛选必须保留本步完整扫掠。");
        Assert(item.Motion.VelocityMetersPerSecond.X > 0d, "快速船实际接触仍传递动量。");
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
    private static void CrossSectorShipOverlapKeepsLocalPrecision()
    {
        SpaceSession session = new();
        ShipState a = AddShip(session, "a-boundary", ShipFlightPhase.Cruising, "a-space");
        ShipState b = AddShip(session, "b-boundary", ShipFlightPhase.Cruising, "b-space");
        ShipState remote = AddShip(session, "c-remote", ShipFlightPhase.Cruising, "c-space");
        a.Sector = new(long.MaxValue - 1, 0); b.Sector = new(long.MaxValue, 0);
        a.PositionX = 127999.75d; b.PositionX = -127999.75d;
        remote.PositionX = .125d; remote.PositionY = .25d;
        session.CaptureShipContactPoses();
        var assemblies = new ShipAssemblyCache().Refresh(session.State.Ships, session.State.Docking);
        session.StepShipContacts(.02d, assemblies);
        double distance = SpaceSectorCoordinates.Difference(new(b.Sector, new(b.PositionX, b.PositionY)),
            new(a.Sector, new(a.PositionX, a.PositionY))).Magnitude;
        Near(distance, 1.001d, "边界两侧的实际船体重叠按真实矩形分开。");
        Near(remote.PositionX, .125d, "多个遥远星区成对查询不会吃掉船体亚米坐标。");
        Near(remote.PositionY, .25d, "遥远查询不会把纯局部起点反复转换成巨大绝对坐标。");
    }

    private static void CrossSectorContactsAndFarIsolation()
    {
        SpaceSession session = new();
        ShipState ship = AddShip(session, "boundary", ShipFlightPhase.Cruising, "unused-space-key");
        ship.Sector = new SpaceSectorId(long.MaxValue - 1, 0);
        ship.PositionX = 127999.75d;
        SpaceLooseItemState item = Loose("neighbor", true, null);
        item.Motion.Sector = new SpaceSectorId(long.MaxValue, 0);
        item.Motion.PositionMeters = new(-127999.85d, 0d);
        item.Motion.VelocityMetersPerSecond = new(-10d, 0d);
        Assert(session.ResolveLooseItemContact(item, new(-127999.65d, 0d), .02d), "边界另一侧的散落物能撞到实际相邻船体。");
        Near(ship.VelocityX, -10d / 6d, "巨大星区编号附近仍传递正确动量。");
        Assert(item.Motion.Sector.X == long.MaxValue, "接触解算不擅自改变散落物归属。");
        Assert(ship.PositionX > 127998d && ship.PositionX < 128001d, "碰撞不把船坐标转换成巨大绝对值。");
        Assert(session.GetFlight(ship.ShipId).Motion.Sector == ship.Sector, "飞行状态发布保留船体星区。");

        SpaceLooseItemState distant = Loose("far-copy", true, null, true);
        distant.Motion.Sector = new(0, 0);
        distant.Motion.PositionMeters = new(ship.PositionX - .4d, 0d);
        SpaceSession.ResetCounters();
        Assert(!session.ResolveLooseItemContact(distant, new(ship.PositionX - .6d, 0d), .02d), "遥远星区同样的局部坐标不发生碰撞。");
        Assert(SpaceSession.DecodeCalls == 0 && ShipMassReader.ReadCalls == 0, "遥远同坐标物体被权威距离筛掉。");
    }

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
