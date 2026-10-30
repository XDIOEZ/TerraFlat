using FlatWorld.Spaceflight;

internal static partial class Program
{
    #region 增量质量账本回归
    private static void IncrementalMassLedger()
    {
        ShipState Make() => new()
        {
            ShipId = "mass-ledger", CellSizeMeters = 1d, PositionX = 23d, PositionY = -9d,
            AngleRadians = .7d, VelocityX = 3d, VelocityY = -2d, AngularVelocityRadiansPerSecond = .8d,
            Pieces = new() { new ShipPieceState { PieceId = "base", MassKg = 100d, CellX = 0, CellY = 0, Width = 2, Height = 1 } },
            MassContributions = new() { new ShipMassContribution { SourceId = "cargo", MassKg = 7d, LocalX = 2d, LocalY = 1d, IntrinsicInertiaKgM2 = 3d } }
        };
        ShipState actual = Make(), expected = Make();
        var ledger = new ShipMassLedger();
        ledger.Set("base", 100d, 1d, .5d, 100d * 5d / 12d);
        for (int i = 0; i < 100; i++)
        {
            ShipMassContribution cargo = expected.MassContributions[0];
            cargo.MassKg = 7d + i * .1d; cargo.LocalX = 2d + i * .03d; cargo.LocalY = 1d - i * .02d;
            ledger.Set("cargo", cargo.MassKg, cargo.LocalX, cargo.LocalY, cargo.IntrinsicInertiaKgM2); ledger.Commit(actual);
            ShipStructureService.RecalculateMass(expected);
            Near(expected.MassKg, actual.MassKg, "incremental real mass");
            Near(expected.CenterOfMassLocalX, actual.CenterOfMassLocalX, "incremental center x");
            Near(expected.CenterOfMassLocalY, actual.CenterOfMassLocalY, "incremental center y");
            Near(expected.InertiaKgM2, actual.InertiaKgM2, "incremental parallel-axis inertia");
            Near(expected.PositionX, actual.PositionX, "changing COM preserves hull x");
            Near(expected.PositionY, actual.PositionY, "changing COM preserves hull y");
            Near(expected.VelocityX, actual.VelocityX, "changing COM preserves point velocity x");
            Near(expected.VelocityY, actual.VelocityY, "changing COM preserves point velocity y");
        }
        ulong revision = actual.MassRevision;
        ShipMassContribution unchanged = expected.MassContributions[0];
        ledger.Set("cargo", unchanged.MassKg, unchanged.LocalX, unchanged.LocalY, unchanged.IntrinsicInertiaKgM2); ledger.Commit(actual);
        Check(actual.MassRevision == revision, "unchanged contribution must not reapply mass properties");
        ledger.Commit(actual);
        Near(expected.MassKg, actual.MassKg, "unvisited stationary load remains registered");
        ledger.Remove("cargo"); ledger.Commit(actual);
        expected.MassContributions.Clear(); ShipStructureService.RecalculateMass(expected);
        Near(expected.MassKg, actual.MassKg, "departed load removed in same step");
        Near(expected.InertiaKgM2, actual.InertiaKgM2, "departed load inertia removed");
        ledger.Clear(); ledger.Commit(actual); Near(0d, actual.MassKg, "empty ledger resets mass");
    }
    #endregion
}
