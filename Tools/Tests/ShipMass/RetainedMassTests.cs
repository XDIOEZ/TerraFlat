using FlatWorld.Spaceflight;
using Reader = FlatWorld.Spaceflight.SpaceSession.MassReaderHarness;

internal static partial class Program
{
    #region 常驻来源与质量账本
    private static void RetainedSourceQueue()
    {
        var reader = new Reader(); var dirty = new HashSet<object>();
        reader.SourceInvalidated = key => dirty.Add(key);
        ItemData[] pieces = Enumerable.Range(1, 1000).Select(i => Cargo(i, 5d)).ToArray();
        ItemData cargo = Cargo(2001, 3d);
        pieces[83].ModuleDataDic["storage"] = new Inventory_ModuleData { Data = new() { ["cargo"] = Inventory(cargo) } };
        for (int pass = 0; pass < 2; pass++)
        {
            reader.BeginIncremental(true);
            foreach (ItemData piece in pieces) reader.CachedContents(piece, piece);
            reader.EndIncremental();
        }
        dirty.Clear();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            reader.BeginIncremental(true);
            Check(!reader.EndIncremental(), "stationary sources survive without being revisited");
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(dirty.Count == 0 && allocated == 0, "stable retained sources neither enqueue work nor allocate");
        cargo.Stack.Amount = 2f; cargo.NotifyMassChanged();
        Check(dirty.Count == 1 && dirty.Contains(pieces[83]), "one cargo change enqueues only its owning source");
        reader.BeginIncremental(true);
        Near(6d, reader.CachedContents(pieces[83], pieces[83]), "single dirty cargo uses current amount");
        Check(!reader.EndIncremental(), "unchanged ownership does not force full re-registration");
        dirty.Clear();
        reader.BeginIncremental(true); reader.EndIncremental();
        Check(dirty.Count == 0, "completed dirty source stays clean on following steps");
        MachinePersistence.Write(pieces[84], "storage", new MachineStorageState { Inventories = new() { Inventory(Cargo(2002, 4d)) } });
        Check(dirty.Count == 1 && dirty.Contains(pieces[84]), "empty cold payload becoming cargo enqueues its owner");
        for (int pass = 0; pass < 2; pass++)
        {
            reader.BeginIncremental(true);
            foreach (ItemData piece in pieces)
                if (dirty.Contains(piece)) reader.CachedContents(piece, piece);
            dirty.Clear();
            if (!reader.EndIncremental()) break;
        }
        reader.BeginIncremental(true);
        Near(4d, reader.CachedContents(pieces[84], pieces[84]), "new cold cargo is counted without revisiting static sources");
        reader.EndIncremental(); dirty.Clear();
        reader.ClearCache(); cargo.NotifyMassChanged();
        Check(dirty.Count == 0, "released reader unsubscribes retained inventories");
        Console.WriteLine($"Retained 1000-piece source x1000 idle steps: {allocated} B; one change enqueues one source");
    }

    private static void RetainedRuntimeInventoryRebinding()
    {
        var reader = new Reader(); var shell = Cargo(1, 0d); var live = new Item();
        var view = new Inventory { Data = Inventory(Cargo(2, 3d)) };
        live.SetModule("storage", new Mod_Inventory { InventoryInstances = new() { view } });
        void Read(double expected)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                reader.BeginIncremental(true);
                Near(expected, reader.CachedContents("root", shell, live), "retained runtime inventory uses actual current data");
                if (!reader.EndIncremental()) break;
            }
        }
        Read(3d);
        view.Data = Inventory(Cargo(3, 8d)); Read(8d);
        view.Data.itemSlots[0].itemData.Stack.Amount = 2f;
        view.Data.itemSlots[0].itemData.NotifyMassChanged(); Read(16d);
        view.Data = null; Read(0d);
    }

    private static void RetainedLedgerAndContributions()
    {
        var ship = new ShipState(); var ledger = new ShipMassLedger(); var pool = new ShipMassContributionPool();
        ledger.Set("floor", 12d, 1d, .5d, 5d); ledger.Commit(ship);
        ledger.Commit(ship); Near(12d, ship.MassKg, "unchanged commit retains fixed mass");
        ledger.Set("cargo", 6d, 3d, .5d, 0d); ledger.Commit(ship);
        Near(18d, ship.MassKg, "cargo adds one mass entry");
        Near(5d / 3d, ship.CenterOfMassLocalX, "cargo changes first moment");
        ledger.Set("cargo", 3d, 3d, .5d, 0d); ledger.Commit(ship);
        Near(15d, ship.MassKg, "cargo amount replaces its prior contribution");
        ledger.Remove("cargo"); ledger.Commit(ship);
        Near(12d, ship.MassKg, "explicit removal retains structural mass");
        Near(5d, ship.InertiaKgM2, "explicit removal restores inertia");
        pool.Clear(ship); pool.Begin(ship);
        ShipMassContribution first = pool.Add(ship, "cargo:", "one", persistent: true);
        ShipMassContribution second = pool.Add(ship, "cargo:", "two", persistent: true);
        ShipMassContribution passenger = pool.Add(ship, "person:", "moving");
        ledger.Set(first.SourceId, 2d, 0d, 0d, 0d);
        ledger.Set(second.SourceId, 4d, 0d, 0d, 0d);
        ledger.Set(passenger.SourceId, 6d, 0d, 0d, 0d);
        pool.Begin(ship); pool.CompleteDynamicSources(ledger); ledger.Commit(ship);
        Check(ship.MassContributions.Count == 2, "departure removes only dynamic entry");
        Near(18d, ship.MassKg, "departure removes passenger mass");
        Check(ReferenceEquals(first, pool.Add(ship, "cargo:", "one", persistent: true)), "fixed contribution identity is stable");
        pool.Remove("cargo:", "one", 0, ledger);
        pool.Remove("cargo:", "two", 0, ledger);
        Check(ship.MassContributions.Count == 0, "swap removal updates remaining source indices");
        ledger.Commit(ship); Near(12d, ship.MassKg, "last cargo removal restores fixed mass");
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) { pool.Begin(ship); pool.CompleteDynamicSources(ledger); ledger.Commit(ship); }
        Check(GC.GetAllocatedBytesForCurrentThread() == before, "stable contribution commits do not allocate");
    }
    #endregion
}

namespace FlatWorld.Spaceflight
{
    #region 非测试目标的物理提交替身
    public sealed class ShipState
    {
        public double MassKg, CenterOfMassLocalX, CenterOfMassLocalY, InertiaKgM2;
        public List<ShipMassContribution> MassContributions = new();
    }
    public enum ShipMassKind { Cargo, Occupant }
    public sealed class ShipMassContribution
    {
        public string SourceId, SupportPieceId;
        public ShipMassKind Kind;
        public double MassKg, LocalX, LocalY, IntrinsicInertiaKgM2;
    }
    public static class ShipStructureService
    {
        public static void ApplyMassProperties(ShipState ship, double mass, double x, double y, double inertia)
        { ship.MassKg = mass; ship.CenterOfMassLocalX = x; ship.CenterOfMassLocalY = y; ship.InertiaKgM2 = inertia; }
    }
    #endregion
}

public static class FluidUnits
{
    public static bool IsFinite(double value) => double.IsFinite(value);
}
