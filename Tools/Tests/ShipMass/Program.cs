using System.Diagnostics;
using FlatWorld.Spaceflight;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reader = FlatWorld.Spaceflight.SpaceSession.MassReaderHarness;

internal static class Program
{
    #region 离线回归入口
    private static int passed;
    private static void Main()
    {
        FluidCatalog.Default.Definitions["test-gas"] = new FluidDefinition
        { Id = "test-gas", LiquidId = "test-liquid", MolarMassKgPerMol = .5d, LiquidDensityKgPerLiter = 2d };
        Run("165 个部件重复读取复用建筑 JSON", StructuralJson);
        Run("建筑 JSON 和打包快照变化失效", BuildingChanges);
        Run("二进制变化失效且冷库存只还原一次", BinarySnapshots);
        Run("冷机器只解析一次根文档和已有载荷", ColdMachine);
        Run("运行库存、堆叠和流体每步读取真实状态", LiveChanges);
        Run("运行机器库存和容器优先于冷载荷", LiveMachine);
        Run("嵌套库存、循环和重复 GUID 去重", NestedIdentity);
        Run("共享气罐和液体引用去重", SharedFluid);
        Run("运行装备包优先于冷载荷", EquipmentPriority);
        Run("资源版本和资源实例变更清理缓存", ResourceChanges);
        Run("递归运行模块集合保持隔离", NestedLiveModules);
        Benchmark();
        Console.WriteLine($"PASS: {passed} regression groups");
    }
    private static void Run(string name, Action action)
    { GameRes.ExistingInstance = new GameRes(); DecodeCounters.Reset(); action(); passed++; Console.WriteLine("PASS " + name); }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Near(double expected, double actual, string message)
        => Check(Math.Abs(expected - actual) < 1e-8d, $"{message}: expected {expected}, actual {actual}");
    private static ItemData Cargo(int guid, double weight)
        => new() { Guid = guid, Stack = new StackData { UnitWeight = weight } };
    private static Inventory_Data Inventory(params ItemData[] items)
        => new() { itemSlots = items.Select(x => new ItemSlot { itemData = x }).ToList() };
    private static Ex_ModData Building(ItemData item, string snapshot = null)
    {
        var module = new Ex_ModData { ID = ModText.Building, ModuleId = ModText.Building };
        module.WriteData(new Mod_Building.Building_Data { SnapshotBase64 = snapshot });
        item.ModuleDataDic[ModText.Building] = module; return module;
    }
    private static Ex_ModData_MemoryPackable Binary<T>(ItemData item, string id, T state)
    {
        var module = new Ex_ModData_MemoryPackable
        { ID = id, ModuleId = id, BitData = MemoryPack.MemoryPackSerializer.Serialize(state) };
        item.ModuleDataDic[id] = module; return module;
    }
    private static InventoryInstanceSnapshot Snapshot(int guid, double weight)
        => new() { Factory = () => Inventory(Cargo(guid, weight)) };
    #endregion

    #region 解码缓存和失效
    private static void StructuralJson()
    {
        var reader = new Reader();
        ItemData[] pieces = Enumerable.Range(1, 165).Select(i => Cargo(i, 5d)).ToArray();
        foreach (ItemData piece in pieces) Building(piece);
        void Read()
        {
            reader.Reset();
            foreach (ItemData piece in pieces) reader.RegisterStructuralItem(piece);
            foreach (ItemData piece in pieces) Near(0d, reader.ReadContents(piece), "empty building has no cargo");
        }
        Read();
        Check(DecodeCounters.Json == 165, "first pass must decode each building module once");
        for (int i = 0; i < 100; i++) Read();
        Check(DecodeCounters.Json == 165, "Reset must retain unchanged JSON cache");
        reader.ClearCache(); Read();
        Check(DecodeCounters.Json == 330, "ClearCache must discard building sources");
    }
    private static void BuildingChanges()
    {
        var reader = new Reader(); var piece = Cargo(1, 10d); Ex_ModData building = Building(piece);
        reader.Reset(); Near(0d, reader.ReadContents(piece), "initial empty building");
        var packed = Cargo(900, 123d);
        packed.ModuleDataDic["storage"] = new Inventory_ModuleData
        { Data = new() { ["cargo"] = Inventory(Cargo(901, 3d)) } };
        building.WriteData(new Mod_Building.Building_Data
        { SnapshotBase64 = Convert.ToBase64String(MemoryPack.MemoryPackSerializer.Serialize(packed)) });
        reader.Reset(); Near(3d, reader.ReadContents(piece), "packed contents count without packed shell weight");
        Check(DecodeCounters.Json == 2 && DecodeCounters.Binary == 1, "changed building source must decode once");
        reader.Reset(); Near(3d, reader.ReadContents(piece), "unchanged packed contents");
        Check(DecodeCounters.Json == 2 && DecodeCounters.Binary == 1, "packed item restore must be cached");
        building.WriteData(new Mod_Building.Building_Data());
        reader.Reset(); Near(0d, reader.ReadContents(piece), "removing packed state updates mass");
        Check(DecodeCounters.Json == 3, "new JSON invalidates previous decode");
    }
    private static void BinarySnapshots()
    {
        var reader = new Reader(); ItemData suit = Cargo(1, 1d);
        Ex_ModData_MemoryPackable module = Binary(suit, Mod_Spacesuit.ModuleId,
            new SpacesuitState { OxygenTank = Snapshot(2, 2d) });
        reader.Reset(); Near(3d, reader.ReadItem(suit), "first suit mass");
        reader.Reset(); Near(3d, reader.ReadItem(suit), "cached suit mass");
        Check(DecodeCounters.Binary == 1 && DecodeCounters.ColdInventory == 1, "cold snapshot is materialized once");
        module.BitData = MemoryPack.MemoryPackSerializer.Serialize(new SpacesuitState { OxygenTank = Snapshot(3, 5d) });
        reader.Reset(); Near(6d, reader.ReadItem(suit), "replacement binary updates contents");
        Check(DecodeCounters.Binary == 2 && DecodeCounters.ColdInventory == 2, "replacement binary rehydrates once");
        reader.ClearCache(); reader.Reset(); Near(6d, reader.ReadItem(suit), "clear then re-read");
        Check(DecodeCounters.Binary == 3 && DecodeCounters.ColdInventory == 3, "ClearCache clears snapshot restoration too");
    }
    private static void ColdMachine()
    {
        var reader = new Reader(); ItemData machine = Cargo(1, 0d);
        MachinePersistence.Write(machine, "storage", new MachineStorageState { Inventories = new() { Inventory(Cargo(2, 2d)) } });
        MachinePersistence.Write(machine, "workbench", new RecipeProcessingState { Input = Inventory(Cargo(3, 3d)), Output = Inventory(Cargo(4, 4d)) });
        DecodeCounters.Reset();
        reader.Reset(); Near(9d, reader.ReadContents(machine), "cold storage and processor mass");
        Check(DecodeCounters.RootJson == 1 && DecodeCounters.Binary == 2, "cold machine parses one document and only present payloads");
        for (int i = 0; i < 100; i++) { reader.Reset(); Near(9d, reader.ReadContents(machine), "cached cold machine"); }
        Check(DecodeCounters.RootJson == 1 && DecodeCounters.Binary == 2, "unchanged machine state must reuse decoded payloads");
        MachinePersistence.Write(machine, "storage", new MachineStorageState { Inventories = new() { Inventory(Cargo(2, 5d)) } });
        DecodeCounters.Reset();
        reader.Reset(); Near(12d, reader.ReadContents(machine), "special data change updates inventory mass");
        Check(DecodeCounters.RootJson == 1 && DecodeCounters.Binary == 2, "changed special data is decoded once");
    }
    private static void ResourceChanges()
    {
        var reader = new Reader(); var piece = Cargo(1, 0d); Building(piece);
        reader.Reset(); reader.ReadContents(piece); Check(DecodeCounters.Json == 1, "initial cache");
        GameRes.ExistingInstance.ResourceReloadVersion++;
        reader.Reset(); reader.ReadContents(piece); Check(DecodeCounters.Json == 2, "resource revision clears old definitions");
        GameRes.ExistingInstance = new GameRes();
        reader.Reset(); reader.ReadContents(piece); Check(DecodeCounters.Json == 3, "new resource session clears source cache");
    }
    #endregion

    #region 实时质量与身份
    private static void LiveChanges()
    {
        var reader = new Reader(); ItemData owner = Cargo(1, 1d), cargo = Cargo(2, 2d);
        var liveBag = Inventory(cargo); var live = new Item();
        owner.ModuleDataDic["bag"] = new Inventory_ModuleData { Data = new() { ["old"] = Inventory(Cargo(3, 8d)) } };
        live.Mods["bag"] = new Mod_Inventory { InventoryInstances = new() { new Inventory { Data = liveBag } } };
        reader.Reset(); Near(3d, reader.ReadItem(owner, live), "live store replaces old module store");
        cargo.Stack.Amount = 3f;
        reader.Reset(); Near(7d, reader.ReadItem(owner, live), "stack changes are never cached as mass");
        liveBag.itemSlots[0].itemData = Cargo(4, 9d);
        reader.Reset(); Near(10d, reader.ReadItem(owner, live), "slot replacement is read on next step");
        var gas = new FluidInventoryState { Components = new() { new FluidComponentState { FluidId = "test-gas", GasMoles = 4d } } };
        owner.PortableTank = new FluidInventory { State = gas };
        reader.Reset(); Near(12d, reader.ReadItem(owner, live), "live gas mass");
        gas.Components[0].GasMoles = 2d;
        reader.Reset(); Near(11d, reader.ReadItem(owner, live), "gas consumption updates next step");
    }
    private static void NestedIdentity()
    {
        var reader = new Reader(); var root = Cargo(1, 1d); var child = Cargo(2, 2d); var leaf = Cargo(3, 3d);
        root.ModuleDataDic["bag"] = new Inventory_ModuleData { Data = new() { ["bag"] = Inventory(child, Cargo(3, 3d), leaf) } };
        child.ModuleDataDic["bag"] = new Inventory_ModuleData { Data = new() { ["bag"] = Inventory(leaf, root) } };
        reader.Reset(); Near(6d, reader.ReadItem(root), "nested items and copied GUID count once without cycle");
        reader.Reset(); reader.RegisterStructuralItem(child);
        Near(4d, reader.ReadItem(root), "registered shell is excluded while remaining leaf counts");
        Near(0d, reader.ReadItem(root), "same root reference is already counted");
        reader.Reset();
        Inventory_Data shared = Inventory(Cargo(10, 2d));
        Near(2d, reader.ReadInventory(shared), "first shared inventory read");
        Near(0d, reader.ReadInventory(shared), "same inventory reference is already counted");
    }
    private static void LiveMachine()
    {
        var reader = new Reader(); ItemData data = Cargo(1, 0d), liveCargo = Cargo(2, 5d);
        data.ItemSpecialData = "flatworld.machine invalid cold payload must not parse when machine is live";
        data.ModuleDataDic["storage"] = new Inventory_ModuleData
        { Data = new() { ["stale"] = Inventory(Cargo(99, 80d)) } };
        Binary(data, Mod_VesselContents.ModuleId, new VesselContentsState { Items = Snapshot(98, 90d) });
        Binary(data, Mod_WaterVessel.ModuleId, new LiquidContainerState { Composition = new() { ["test-liquid"] = 100d } });
        var machine = new MachineEntity
        {
            Logic = new VesselLogic
            {
                Inventories = new() { new Inventory { Data = Inventory(liveCargo) } },
                Data = new LiquidContainerState { Composition = new() { ["test-liquid"] = 4d } }
            },
            Processor = new MachineProcessor
            { Input = new Inventory { Data = Inventory(Cargo(3, 2d)) }, Output = new Inventory { Data = Inventory(Cargo(4, 3d)) } }
        };
        reader.Reset(); Near(18d, reader.ReadContents(data, machine: machine), "live machine stores and vessel replace cold data");
        Check(DecodeCounters.Binary == 0 && DecodeCounters.RootJson == 0, "live machine bypasses cold machine and vessel parsing");
        liveCargo.Stack.Amount = 2f;
        reader.Reset(); Near(23d, reader.ReadContents(data, machine: machine), "live machine stack changes are immediate");
    }
    private static void SharedFluid()
    {
        var reader = new Reader(); var gas = new FluidInventoryState
        { Components = new() { new FluidComponentState { FluidId = "test-gas", GasMoles = 3d, LiquidMoles = 1d } } };
        var a = Cargo(1, 1d); var b = Cargo(2, 1d);
        a.PortableTank = new FluidInventory { State = gas }; b.PortableTank = a.PortableTank;
        reader.Reset(); Near(4d, reader.ReadItem(a) + reader.ReadItem(b), "shared tank state is counted once across items");
        var liquid = new LiquidContainerState { Composition = new() { ["test-liquid"] = 4d } };
        var live = new Item(); live.Mods["water-a"] = new Mod_WaterVessel { Data = liquid }; live.Mods["water-b"] = new Mod_WaterVessel { Data = liquid };
        reader.Reset(); Near(9d, reader.ReadItem(Cargo(3, 1d), live), "shared liquid state is counted once");
        liquid.Composition["test-liquid"] = 2d;
        reader.Reset(); Near(5d, reader.ReadItem(Cargo(3, 1d), live), "live liquid changes update mass");
    }
    private static void EquipmentPriority()
    {
        var reader = new Reader(); var owner = Cargo(1, 1d); var bagItem = Cargo(2, 2d);
        Binary(bagItem, ModText.Equipment_Store, new List<EquipmentInstance>
        { new EquipmentInstance_Bag { BagData = Inventory(Cargo(99, 99d)) } });
        var live = new Item();
        live.Mods[ModText.Equipment_Module] = new Mod_Equipment
        {
            EquipmentInventory = new Inventory { Data = Inventory(bagItem) },
            Instances = new() { new EquipmentInstance_Bag { EquippedItem = bagItem, BagData = Inventory(Cargo(3, 3d)) } }
        };
        reader.Reset(); Near(6d, reader.ReadItem(owner, live), "equipped live bag skips stale binary store");
        Check(DecodeCounters.Binary == 0, "stale equipped payload should not even decode");
    }
    private static void NestedLiveModules()
    {
        var reader = new Reader(); var owner = Cargo(1, 1d); var child = Cargo(2, 2d);
        child.ModuleDataDic["bag"] = new Inventory_ModuleData { Data = new() { ["child"] = Inventory(Cargo(3, 3d)) } };
        owner.ModuleDataDic["bag"] = new Inventory_ModuleData { Data = new() { ["stale"] = Inventory(Cargo(4, 40d)) } };
        var live = new Item(); live.Mods["bag"] = new Mod_Inventory { InventoryInstances = new() { new Inventory { Data = Inventory(child) } } };
        for (int i = 0; i < 100; i++) { reader.Reset(); Near(6d, reader.ReadItem(owner, live), "recursive child must not overwrite outer live module exclusion"); }
    }
    #endregion

    #region 专项测量
    private static void Benchmark()
    {
        var reader = new Reader(); ItemData[] pieces = Enumerable.Range(1, 165).Select(i => Cargo(i, 5d)).ToArray();
        foreach (ItemData piece in pieces) Building(piece);
        void Read() { reader.Reset(); foreach (ItemData piece in pieces) { reader.RegisterStructuralItem(piece); reader.ReadContents(piece); } }
        for (int i = 0; i < 100; i++) Read();
        DecodeCounters.Reset(); long before = GC.GetAllocatedBytesForCurrentThread(); long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < 1000; i++) Read();
        long elapsed = Stopwatch.GetTimestamp() - start; long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(DecodeCounters.Json == 0 && DecodeCounters.Binary == 0 && DecodeCounters.ColdInventory == 0, "steady 165-piece benchmark must not decode or restore");
        Check(bytes == 0, "steady structural mass reads must not allocate after warm-up");
        Console.WriteLine($"165-piece warm reads x1000: {elapsed * 1000d / Stopwatch.Frequency:F2} ms, {bytes:N0} bytes allocated, JSON={DecodeCounters.Json}, binary={DecodeCounters.Binary}, cold inventory={DecodeCounters.ColdInventory}");
    }
    #endregion
}
