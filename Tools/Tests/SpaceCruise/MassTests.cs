using System.Diagnostics;
using System.IO;
using FlatWorld.Spaceflight;

internal static partial class Program
{
    #region 质量、重心与惯量回归
    private static ShipState MassShip()
    {
        var ship = new ShipState { CellSizeMeters = 2d };
        ship.Pieces.Add(new ShipPieceState { PieceId = "hull", CellX = -1, Width = 2, Height = 1, MassKg = 12d });
        ship.MassContributions.Add(new ShipMassContribution { SourceId = "cargo", MassKg = 6d, LocalX = 4d, LocalY = 1d, IntrinsicInertiaKgM2 = 3d });
        ship.MassContributions.Add(new ShipMassContribution { SourceId = "person", Kind = ShipMassKind.Occupant, MassKg = 2d, LocalX = 0d, LocalY = 5d });
        ship.Compartments.Add(new ShipCompartmentState
        {
            CompartmentId = "room", Gas = new FluidInventoryState { TestMassKg = 4d },
            Cells = new List<ShipCell> { new(0, 0), new(1, 0) }
        });
        return ship;
    }

    private static void MassProperties()
    {
        FloorSupportQueries();
        var ship = MassShip();
        var actual = ShipStructureService.RecalculateMass(ship, new ShipStructureService.MassWorkspace());
        Near(24d, actual.MassKg, "structure, cargo, occupant and cabin gas all count");
        Near(4d / 3d, actual.CenterX, "weighted center x");
        Near(4d / 3d, actual.CenterY, "weighted center y");
        // 独立手算矩形自身惯量、离心距离与每格均匀舱气惯量。
        Near(385d / 3d, actual.InertiaKgM2, "rectangle, cargo intrinsic and two-cell gas inertia");
        Near(actual.MassKg, ship.MassKg, "mass is committed to ship");
        Near(actual.InertiaKgM2, ship.InertiaKgM2, "inertia is committed to ship");

        ship.MassContributions.Clear(); ship.Compartments.Clear();
        ship.Pieces[0].QuarterTurns = 1;
        ship.Pieces.Add(new ShipPieceState { PieceId = "broken", MassKg = 1000000d, Destroyed = true });
        ship.Pieces.Add(new ShipPieceState { PieceId = "dead", MassKg = 1000000d, Health = 0d });
        ship.Pieces.Add(null);
        actual = ShipStructureService.RecalculateMass(ship);
        Near(12d, actual.MassKg, "dead and null pieces do not contribute");
        Near(-1d, actual.CenterX, "quarter turn swaps footprint width");
        Near(2d, actual.CenterY, "quarter turn swaps footprint height");
        Near(20d, actual.InertiaKgM2, "quarter turn preserves intrinsic rectangle inertia");
    }

    private static void FloorSupportQueries()
    {
        ShipState ship = new();
        ShipPieceState floor = new() { Kind = ShipPieceKind.Floor, CellX = -4, CellY = -3, Width = 2, Height = 3 };
        ship.Pieces.Add(null); ship.Pieces.Add(floor);
        for (int turn = 0; turn < 4; turn++)
        {
            floor.QuarterTurns = turn;
            HashSet<ShipCell> expected = new(ShipGeometry.Footprint(floor));
            for (int y = -5; y <= 2; y++)
            for (int x = -6; x <= 2; x++)
                Check(ShipGeometry.HasFloor(ship, x, y) == expected.Contains(new(x, y)), "floor support matches rotated footprint");
        }
        floor.Destroyed = true;
        Check(!ShipGeometry.HasFloor(ship, -4, -3), "destroyed floor provides no support");
        floor.Destroyed = false; floor.Width = floor.Height = 0;
        Check(ShipGeometry.HasFloor(ship, -4, -3), "minimum footprint is one cell");
        int supported = 0;
        for (int i = 0; i < 100; i++) if (ShipGeometry.HasFloor(ship, -4, -3)) supported++;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100000; i++)
        {
            if (ShipGeometry.HasFloor(ship, -4, -3)) supported++;
            if (ShipGeometry.HasFloor(ship, 1000, 1000)) supported++;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(supported == 100100 && allocated == 0, $"floor queries retain support without allocations: {allocated} bytes");
        Console.WriteLine($"Floor support fixture: 200000 queries, {allocated} bytes allocated.");
    }

    private static void MassChanges()
    {
        var ship = MassShip();
        var workspace = new ShipStructureService.MassWorkspace();
        ship.PositionX = 10d; ship.PositionY = 20d;
        ship.VelocityX = 3d; ship.VelocityY = 4d;
        ship.AngleRadians = Math.PI * .5d; ship.AngularVelocityRadiansPerSecond = 2d;
        ShipGeometry.LocalToWorld(ship, 7d, 8d, out double beforeX, out double beforeY);
        ShipGeometry.PointVelocity(ship, beforeX, beforeY, out double beforeVelocityX, out double beforeVelocityY);
        ShipStructureService.RecalculateMass(ship, workspace);
        AssertPointUnchanged(ship, beforeX, beforeY, beforeVelocityX, beforeVelocityY);
        ship.MassContributions[0].MassKg = 0d;
        ship.MassContributions[1].LocalX = 4d;
        ship.Compartments[0].Gas.TestMassKg = 8d;
        var actual = ShipStructureService.RecalculateMass(ship, workspace);
        Near(22d, actual.MassKg, "live cargo and cabin changes are not cached away");
        Near(12d / 11d, actual.CenterX, "moving occupant updates center x");
        Near(15d / 11d, actual.CenterY, "moving occupant updates center y");
        double cx = 12d / 11d, cy = 15d / 11d;
        double expected = 23d + 12d * (cx * cx + (1d - cy) * (1d - cy)) +
            2d * ((4d - cx) * (4d - cx) + (5d - cy) * (5d - cy)) +
            8d * ((2d - cx) * (2d - cx) + (1d - cy) * (1d - cy) + 5d / 3d);
        Near(expected, actual.InertiaKgM2, "moving occupant and gas update inertia");
        AssertPointUnchanged(ship, beforeX, beforeY, beforeVelocityX, beforeVelocityY);
        double currentX = ship.PositionX, currentY = ship.PositionY;
        ShipStructureService.RecalculateMass(ship, workspace);
        Near(currentX, ship.PositionX, "unchanged mass does not move origin again");
        Near(currentY, ship.PositionY, "unchanged mass does not move origin again");
    }

    private static void AssertPointUnchanged(ShipState ship, double expectedX, double expectedY, double expectedVelocityX, double expectedVelocityY)
    {
        ShipGeometry.LocalToWorld(ship, 7d, 8d, out double x, out double y);
        ShipGeometry.PointVelocity(ship, x, y, out double vx, out double vy);
        Near(expectedX, x, "center change preserves physical point x");
        Near(expectedY, y, "center change preserves physical point y");
        Near(expectedVelocityX, vx, "center change preserves physical point velocity x");
        Near(expectedVelocityY, vy, "center change preserves physical point velocity y");
    }

    private static void MassIdentity()
    {
        var ship = MassShip();
        var workspace = new ShipStructureService.MassWorkspace();
        ship.MassContributions.Add(new ShipMassContribution { SourceId = "cargo", MassKg = 999d });
        ship.MassContributions.Add(new ShipMassContribution { SourceId = "hull", MassKg = 999d });
        ship.Compartments.Add(new ShipCompartmentState
        {
            CompartmentId = "room", Gas = new FluidInventoryState { TestMassKg = 999d }, Cells = new List<ShipCell> { new(20, 20) }
        });
        Near(24d, ShipStructureService.RecalculateMass(ship, workspace).MassKg, "same piece, cargo and cabin identities count once");
        ship.MassContributions.Add(new ShipMassContribution { SourceId = "cabin-gas:room", MassKg = 1d });
        Near(21d, ShipStructureService.RecalculateMass(ship, workspace).MassKg, "explicit cabin source prevents another gas entry");

        InvalidMass(s => s.Pieces.Add(new ShipPieceState { PieceId = "hull" }), "duplicate live piece id rejected");
        InvalidMass(s => s.Pieces[0].PieceId = " ", "empty piece id rejected");
        InvalidMass(s => s.Pieces[0].MassKg = -1d, "negative structural mass rejected");
        InvalidMass(s => s.Pieces[0].MassKg = double.NaN, "nonfinite structural mass rejected");
        InvalidMass(s => s.MassContributions[0].SourceId = null, "missing cargo id rejected");
        InvalidMass(s => s.MassContributions[0].MassKg = -1d, "negative cargo mass rejected");
        InvalidMass(s => s.MassContributions[0].LocalX = double.NaN, "nonfinite cargo coordinate rejected");
        InvalidMass(s => s.MassContributions[0].IntrinsicInertiaKgM2 = -1d, "negative cargo inertia rejected");
        InvalidMass(s => s.CellSizeMeters = 0d, "zero grid size rejected");
    }

    private static void InvalidMass(Action<ShipState> edit, string message)
    {
        var ship = MassShip(); edit(ship);
        try { ShipStructureService.RecalculateMass(ship); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException(message);
    }

    private static void MassWorkspace()
    {
        var workspace = new ShipStructureService.MassWorkspace();
        var first = MassShip(); var second = MassShip();
        second.MassContributions.Clear(); second.Compartments.Clear(); second.Pieces[0].MassKg = 1d;
        ShipStructureService.RecalculateMass(first, workspace);
        Near(1d, ShipStructureService.RecalculateMass(second, workspace).MassKg, "workspace clears identities between ships");
        Near(24d, ShipStructureService.RecalculateMass(first, workspace).MassKg, "workspace can return to previous ship");
        second.Pieces.Clear(); second.CenterOfMassLocalX = 2d; second.CenterOfMassLocalY = 3d;
        second.PositionX = 11d; second.VelocityY = 12d;
        var empty = ShipStructureService.RecalculateMass(second, workspace);
        Near(0d, empty.MassKg, "empty ship has zero mass");
        Near(0d, empty.InertiaKgM2, "empty ship has zero inertia");
        Near(2d, empty.CenterX, "empty ship retains prior center x");
        Near(3d, empty.CenterY, "empty ship retains prior center y");
        Near(11d, second.PositionX, "zero mass preserves position");
        Near(12d, second.VelocityY, "zero mass preserves velocity");

        var ship = LargeMassShip();
        for (int i = 0; i < 100; i++) ShipStructureService.RecalculateMass(ship, workspace);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) ShipStructureService.RecalculateMass(ship, workspace);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(bytes == 0, $"pooled structure and contribution aggregation allocated {bytes} bytes");
    }
    #endregion

    #region 载荷池复用与隔离回归
    private static void ContributionPoolFields()
    {
        var ship = new ShipState(); var pool = new ShipMassContributionPool();
        pool.Begin(ship);
        var original = pool.Add(ship, "cargo:", "crate");
        Check(original.SourceId == "cargo:crate", "cargo source identity");
        original.SupportPieceId = "floor"; original.Kind = ShipMassKind.Other;
        original.LocalX = 4d; original.LocalY = 5d; original.MassKg = 99d; original.IntrinsicInertiaKgM2 = 100d;
        pool.Begin(ship);
        var reused = pool.Add(ship, "person:", "player");
        Check(ReferenceEquals(original, reused), "contribution object reused across source change");
        Check(reused.SourceId == "person:player", "prefix and identity change rebuild source");
        Check(reused.SupportPieceId == null && reused.Kind == ShipMassKind.Cargo, "support and kind are reset");
        Near(0d, reused.LocalX, "local x cleared"); Near(0d, reused.LocalY, "local y cleared");
        Near(0d, reused.MassKg, "mass cleared"); Near(0d, reused.IntrinsicInertiaKgM2, "intrinsic inertia cleared");

        pool.Begin(ship); var fluid = pool.Add(ship, "fluid-group:", "tank", 10);
        Check(fluid.SourceId == "fluid-group:10:tank", "fluid source includes owner id");
        pool.Begin(ship); fluid = pool.Add(ship, "fluid-group:", "tank", 11);
        Check(fluid.SourceId == "fluid-group:11:tank", "owner change updates fluid source");
        pool.Begin(ship); fluid = pool.Add(ship, "fluid-group:", "other-tank", 11);
        Check(fluid.SourceId == "fluid-group:11:other-tank", "identity-only change updates fluid source");
        string source = fluid.SourceId;
        pool.Begin(ship); fluid = pool.Add(ship, new string("fluid-group:".ToCharArray()), new string("other-tank".ToCharArray()), 11);
        Check(ReferenceEquals(source, fluid.SourceId), "equal source strings preserve pooled source reference");
    }

    private static void ContributionPoolIsolation()
    {
        var first = new ShipState(); var firstPool = new ShipMassContributionPool();
        firstPool.Begin(first);
        var firstValue = firstPool.Add(first, "cargo:", "a"); firstValue.MassKg = 5d;
        var secondValue = firstPool.Add(first, "cargo:", "b"); secondValue.MassKg = 7d;
        firstPool.Add(first, "cargo:", "c").MassKg = 9d;
        Near(21d, ShipStructureService.RecalculateMass(first).MassKg, "all initial loads counted");
        firstPool.Begin(first);
        var remaining = firstPool.Add(first, "cargo:", "a"); remaining.MassKg = 3d;
        Check(first.MassContributions.Count == 1 && ReferenceEquals(firstValue, remaining), "shorter load list keeps only current contributions");
        Near(3d, ShipStructureService.RecalculateMass(first).MassKg, "removed loads do not persist in mass calculation");
        firstPool.Begin(first);
        Check(first.MassContributions.Count == 0, "begin without add clears all current loads");
        Near(0d, ShipStructureService.RecalculateMass(first).MassKg, "empty load list has no stale mass");
        firstPool.Add(first, "cargo:", "a");
        Check(ReferenceEquals(secondValue, firstPool.Add(first, "cargo:", "b")), "pool can regrow using prior unused entries");

        var second = new ShipState(); var secondPool = new ShipMassContributionPool();
        secondPool.Begin(second); var isolated = secondPool.Add(second, "cargo:", "a"); isolated.MassKg = 42d;
        Check(!ReferenceEquals(firstValue, isolated), "different ship pools do not share contribution objects");
        firstValue.MassKg = 8d;
        Near(42d, isolated.MassKg, "mutating first ship does not affect second ship load");
        secondPool.Begin(second); secondPool.Add(second, "person:", "b").MassKg = 70d;
        Check(firstValue.SourceId == "cargo:a" && firstValue.MassKg == 8d, "reusing second pool does not overwrite first pool");
    }

    private static void ContributionPoolAllocations()
    {
        var ship = new ShipState(); var pool = new ShipMassContributionPool();
        var identities = new string[16];
        for (int i = 0; i < identities.Length; i++) identities[i] = "load-" + i;
        for (int i = 0; i < 100; i++) FillContributionPool(ship, pool, identities);
        var sample = ship.MassContributions[0]; string source = sample.SourceId;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) FillContributionPool(ship, pool, identities);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(bytes == 0, $"stable contribution pool allocated {bytes} bytes");
        Check(ReferenceEquals(sample, ship.MassContributions[0]) && ReferenceEquals(source, sample.SourceId), "stable pool retains sample and source references");
    }

    private static void FillContributionPool(ShipState ship, ShipMassContributionPool pool, string[] identities)
    {
        pool.Begin(ship);
        for (int i = 0; i < identities.Length; i++)
        {
            string prefix = i < 2 ? "fluid-group:" : i == 2 ? "person:" : "cargo:";
            var value = pool.Add(ship, prefix, identities[i], i < 2 ? 123 : 0);
            value.SupportPieceId = identities[i]; value.Kind = i == 2 ? ShipMassKind.Occupant : ShipMassKind.Cargo;
            value.LocalX = i % 4; value.LocalY = i / 4; value.MassKg = 1d + i; value.IntrinsicInertiaKgM2 = i;
        }
    }
    #endregion

    #region 165部件质量汇总基准
    private static ShipState LargeMassShip()
    {
        var ship = new ShipState();
        for (int i = 0; i < 165; i++) ship.Pieces.Add(new ShipPieceState { PieceId = "piece:" + i, CellX = i % 15, CellY = i / 15, MassKg = 20d });
        for (int i = 0; i < 16; i++) ship.MassContributions.Add(new ShipMassContribution { SourceId = "cargo:" + i, LocalX = i % 4, LocalY = i / 4, MassKg = 70d });
        return ship;
    }

    private static void MassBenchmark()
    {
        var ship = LargeMassShip();
        var workspace = new ShipStructureService.MassWorkspace();
        for (int i = 0; i < 100; i++) ShipStructureService.RecalculateMass(ship, workspace);
        const int iterations = 10000;
        var timer = new Stopwatch();
        long before = GC.GetAllocatedBytesForCurrentThread(); timer.Start();
        for (int i = 0; i < iterations; i++) ShipStructureService.RecalculateMass(ship, workspace);
        timer.Stop(); long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Console.WriteLine($"BENCH mass aggregation / 165 pieces + 16 contributions / {iterations} refreshes: {timer.Elapsed.TotalMilliseconds:F2} ms, {bytes} B; per refresh {timer.Elapsed.TotalMilliseconds / iterations:F5} ms");
    }
    #endregion
}
