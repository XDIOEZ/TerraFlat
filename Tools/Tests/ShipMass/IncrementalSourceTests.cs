using Reader = FlatWorld.Spaceflight.SpaceSession.MassReaderHarness;

internal static partial class Program
{
    #region 增量读取与身份移交
    private static void IncrementalSources()
    {
        var reader = new Reader();
        ItemData body = Cargo(1, 10d), cargo = Cargo(2, 3d);
        Inventory_Data inventory = Inventory(cargo);
        body.ModuleDataDic["storage"] = new Inventory_ModuleData { Data = new() { ["cargo"] = inventory } };
        Ex_ModData building = Building(body);
        void Read(double expected)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                reader.BeginIncremental(); Near(expected, reader.CachedItem("root", body), "incremental root real state");
                if (!reader.EndIncremental()) break;
            }
        }
        Read(13d);
        int json = DecodeCounters.Json;
        for (int i = 0; i < 100; i++) Read(13d);
        Check(DecodeCounters.Json == json, "unchanged cached source must not traverse or decode payload");
        cargo.Stack.Amount = 4f; cargo.NotifyMassChanged(); Read(22d);
        cargo.Stack.UnitWeight = 5d; cargo.NotifyMassChanged(); Read(30d);
        building.WriteData(new Mod_Building.Building_Data { BuildingPrefabId = "changed" }); Read(30d);
        Check(DecodeCounters.Json == json + 1, "changed mass-bearing payload invalidates that source once");
        var fluid = new FluidInventoryState { Components = new() { new FluidComponentState { FluidId = "test-gas", GasMoles = 4d } } };
        body.PortableTank = new FluidInventory { State = fluid }; body.NotifyModuleStructureChanged();
        for (int pass = 0; pass < 2; pass++)
        {
            reader.BeginIncremental(); Near(2d, reader.CachedFluid("shared-tank", fluid), "one real shared tank");
            Near(30d, reader.CachedItem("root", body), "shared tank not counted in root twice");
            if (!reader.EndIncremental()) break;
        }
        fluid.Components[0].GasMoles = 10d; fluid.NotifyMassChanged();
        reader.BeginIncremental(); Near(5d, reader.CachedFluid("shared-tank", fluid), "shared tank actual moles changed");
        Near(30d, reader.CachedItem("root", body), "shared tank dedup survives incremental update"); reader.EndIncremental();
        reader.ClearCache(); Read(35d);
    }
    private static void IncrementalOwnership()
    {
        var reader = new Reader(); ItemData a = Cargo(1, 0d), b = Cargo(2, 0d);
        Inventory_Data shared = Inventory(Cargo(3, 7d));
        var storageA = new Inventory_ModuleData { Data = new() { ["cargo"] = shared } };
        a.ModuleDataDic["storage"] = storageA;
        b.ModuleDataDic["storage"] = new Inventory_ModuleData { Data = new() { ["cargo"] = shared } };
        void Read(double expectedA, double expectedB)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                reader.BeginIncremental(); Near(expectedA, reader.CachedContents("A", a), "first source identity ownership");
                Near(expectedB, reader.CachedContents("B", b), "second source identity ownership");
                if (!reader.EndIncremental()) break;
            }
        }
        Read(7d, 0d); Read(7d, 0d);
        storageA.Data["cargo"] = Inventory(); a.NotifyModuleStructureChanged(); Read(0d, 7d);
        shared.itemSlots[0].itemData.Stack.Amount = 2f; shared.NotifyMassChanged(); Read(0d, 14d);
        reader.BeginIncremental(); Near(14d, reader.CachedContents("B", b), "departed first source cannot steal cargo");
        reader.EndIncremental();
    }
    private static void IncrementalMachineReplacement()
    {
        var reader = new Reader(); var shell = Cargo(1, 0d);
        var machine = new MachineEntity { Definition = new() { Fluid = new() }, Logic = new() { Inventories = new() { new Inventory { Data = Inventory(Cargo(2, 3d)) } } } };
        machine.FluidState.Chambers["main"] = new FluidInventoryState
        { Components = new() { new FluidComponentState { FluidId = "test-gas", GasMoles = 4d } } };
        void Read(double expected)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                reader.BeginIncremental(); Near(expected, reader.CachedContents("machine", shell, machine: machine), "same node current storage references");
                if (!reader.EndIncremental()) break;
            }
        }
        Read(5d); Read(5d);
        machine.Logic.Inventories[0].Data = Inventory(Cargo(3, 8d)); Read(10d);
        machine.FluidState.Chambers["main"] = new FluidInventoryState
        { Components = new() { new FluidComponentState { FluidId = "test-gas", GasMoles = 10d } } }; Read(13d);
        machine.FluidState.PortableTank = Inventory(Cargo(4, 2d)); Read(15d);
        machine.FluidState = new FluidMachineState { Chambers = new() }; Read(8d);
        var live = new Item();
        var storage = new Mod_Inventory { InventoryInstances = new() { new Inventory { Data = Inventory(Cargo(5, 4d)) } } };
        live.Mods["runtime-storage"] = storage;
        void ReadLive(double expected)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                reader.BeginIncremental(); Near(expected, reader.CachedContents("live", shell, live), "same item module lifecycle invalidates cache");
                if (!reader.EndIncremental()) break;
            }
        }
        ReadLive(4d); storage.IsRuntimeLoaded = false; ReadLive(0d);
        storage.IsRuntimeLoaded = true; ReadLive(4d);
        storage.Enabled = false; ReadLive(0d); storage.Enabled = true; ReadLive(4d);
        live.Mods["runtime-storage"] = new Mod_Inventory { InventoryInstances = new() { new Inventory { Data = Inventory(Cargo(6, 9d)) } } }; ReadLive(9d);
    }
    private static void IncrementalAllocations()
    {
        var reader = new Reader(); ItemData[] pieces = Enumerable.Range(1, 165).Select(i => Cargo(i, 5d)).ToArray();
        foreach (ItemData piece in pieces) Building(piece);
        void Read()
        {
            reader.BeginIncremental();
            foreach (ItemData piece in pieces) reader.CachedContents(piece, piece);
            reader.EndIncremental();
        }
        for (int i = 0; i < 20; i++) Read();
        DecodeCounters.Reset(); long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) Read();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0, "cached source warm steps must not allocate");
        Check(DecodeCounters.Json == 0 && DecodeCounters.Binary == 0, "cached source warm steps must not decode unchanged contents");
        Console.WriteLine($"Incremental cached 165-piece source x1000: {allocated} B, JSON={DecodeCounters.Json}, binary={DecodeCounters.Binary}");
    }
    #endregion
}
