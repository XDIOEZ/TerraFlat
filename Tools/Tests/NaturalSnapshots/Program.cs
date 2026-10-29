using System.Diagnostics;
using MemoryPack;
using UnityEngine;

internal static class Program
{
    #region 自然物快照隔离与复制成本

    private static int assertions;
    private static ItemData retained;
    private static void Check(bool condition, string label)
    {
        assertions++;
        if (!condition) throw new Exception(label);
    }

    private static void Main(string[] args)
    {
        var template = new Data_GeneralItem
        {
            IDName = "probe:natural", Guid = 143, code = "probe", MaxDurability = 100, Durability = 25,
            Stack = new ItemStack { Amount = 3, Weight = 2, Volume = 4 },
            ModuleDataDic = new ModuleDataCollection
            {
                ["stock"] = new CollectableModuleData
                { StableName = "stock", ModuleId = "collectable", CurrentStock = 7, IsInitialized = true },
                ["state"] = new Ex_ModData_MemoryPackable
                { StableName = "state", ModuleId = "probe.state", BitData = new byte[] { 2, 4, 8 } }
            }
        };
        ItemData source = ItemInstanceDataFactory.Create(template);
        source.Tags.Add("runtime.natural");
        source.transform.position = new Vector3(12, -4, 0);
        source.MatterState.TemperatureCelsius = 37;
        source.PhysicsState = new ItemPhysicsRuntimeState
        { Velocity = new Vector2(3, -2), AngularVelocity = 7, PersistentWorldResident = true };
        ItemData copy = ItemInstanceDataFactory.CloneRuntime(source);
        Check(copy.Guid == source.Guid && copy.IDName == source.IDName, "持久身份改变");
        Check(ReferenceEquals(copy.SharedConfiguration, source.SharedConfiguration), "重复复制只读配置");
        Check(!ReferenceEquals(copy.Stack, source.Stack) && copy.Stack.Amount == 3, "堆叠状态共享");
        Check(!ReferenceEquals(copy.ModuleDataDic, source.ModuleDataDic), "模块集合共享");
        Check(!ReferenceEquals(copy.ModuleDataDic["stock"], source.ModuleDataDic["stock"]), "模块状态共享");
        Check(!ReferenceEquals(copy.PhysicsState, source.PhysicsState), "物理状态共享");
        Check(copy.PhysicsState.Velocity == new Vector2(3, -2) && copy.PhysicsState.AngularVelocity == 7 &&
            copy.PhysicsState.PersistentWorldResident, "运动状态丢失");
        source.Tags.Clear();
        source.Stack.Amount = 99;
        source.transform.position = Vector3.zero;
        source.PhysicsState.Velocity = Vector2.zero;
        source.MatterState.TemperatureCelsius = -10;
        ((CollectableModuleData)source.ModuleDataDic["stock"]).CurrentStock = 0;
        ((Ex_ModData_MemoryPackable)source.ModuleDataDic["state"]).BitData[0] = 99;
        Check(copy.Stack.Amount == 3 && copy.Tags.Contains("runtime.natural"), "回池污染数量或标签");
        Check(copy.transform.position.x == 12 && copy.MatterState.TemperatureCelsius == 37, "回池污染姿态或物质");
        Check(copy.PhysicsState.Velocity.x == 3, "回池污染保存速度");
        Check(((CollectableModuleData)copy.ModuleDataDic["stock"]).CurrentStock == 7, "回池污染保存库存");
        Check(((Ex_ModData_MemoryPackable)copy.ModuleDataDic["state"]).BitData[0] == 2, "回池污染二进制载荷");
        ItemData restored = MemoryPackSerializer.Deserialize<ItemInstanceSnapshot>(
            ItemSnapshotSerialization.SerializePayload(ItemInstanceSnapshot.Capture(copy))).CreateColdData();
        Check(restored.Guid == copy.Guid && restored.Tags.Contains("runtime.natural"), "快照往返改变身份或标签");
        Check(restored.PhysicsState.Velocity == copy.PhysicsState.Velocity &&
            restored.PhysicsState.AngularVelocity == 7 && restored.PhysicsState.PersistentWorldResident,
            "快照往返丢失运动状态");
        TestNestedInventoryAndExtensionState();
        template.Stack.Weight = 9;
        template.MaxDurability = 200;
        ItemData changedDefinition = ItemInstanceDataFactory.Create(template);
        ItemInstanceDataFactory.ApplyCurrentDefinition(copy, changedDefinition);
        Check(copy.Stack.Weight == 9 && copy.MaxDurability == 200 && copy.Durability == 50 &&
            copy.Guid == 143 && copy.Tags.Contains("runtime.natural") && copy.PhysicsState.Velocity.x == 3,
            "未落盘快照重新挂接定义时丢失实例状态或沿用旧配置");

        // 预热后比较同一份数据的原深拷贝与正式运行态工厂，结果仅代表独立 CLR。
        Func<ItemData> previous = () => FastCloner.FastCloner.DeepClone(copy);
        Func<ItemData> current = () => ItemInstanceDataFactory.CloneRuntime(copy);
        for (int i = 0; i < 32; i++) { retained = previous(); retained = current(); }
        var oldCost = Measure(previous, 512);
        var newCost = Measure(current, 512);
        Check(newCost.Bytes < oldCost.Bytes, "运行态快照没有减少分配");
        Console.WriteLine($"PASS {assertions} assertions; 512 copies: previous={oldCost.Bytes} B/{oldCost.Ms:F2} ms, current={newCost.Bytes} B/{newCost.Ms:F2} ms");
    }

    private static (long Bytes, double Ms) Measure(Func<ItemData> clone, int count)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < count; i++) retained = clone();
        return (GC.GetAllocatedBytesForCurrentThread() - before,
            (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency);
    }

    private static void TestNestedInventoryAndExtensionState()
    {
        var nested = new Data_GeneralItem
        { IDName = "probe:nested", Guid = 144, code = "nested", Stack = new ItemStack { Amount = 4 } };
        var container = new Data_GeneralItem
        {
            IDName = "probe:container", Guid = 145, code = "container",
            ModuleDataDic = new ModuleDataCollection
            {
                ["inventory"] = new Inventory_ModuleData
                {
                    StableName = "inventory", ModuleId = "inventory",
                    Data = new Dictionary<string, Inventory_Data>
                    { ["bag"] = new(new List<ItemSlot> { new(0) { itemData = nested } }, "bag") }
                }
            }
        };
        ItemData frozen = ItemInstanceDataFactory.CloneRuntime(container);
        var bag = ((Inventory_ModuleData)frozen.ModuleDataDic["inventory"]).Data["bag"];
        nested.Stack.Amount = 0;
        Check(bag.itemSlots[0].itemData.Guid == 144 && bag.itemSlots[0].itemData.Stack.Amount == 4,
            "自然物嵌套库存没有隔离");

        using var codec = ModuleInstanceStateCodecs.Register(new ExtensionCodec());
        var extension = new ExtensionData
        { StableName = "extension", ModuleId = "extension", Payload = new byte[] { 7, 8 }, Configuration = "kept" };
        container.ModuleDataDic["extension"] = extension;
        ItemData codecCopy = ItemInstanceDataFactory.CloneRuntime(container);
        using var factory = ModuleRuntimeDataFactories.Register(new ExtensionFactory());
        ItemData factoryCopy = ItemInstanceDataFactory.CloneRuntime(container);
        extension.Payload[0] = 0;
        foreach (ItemData value in new[] { codecCopy, factoryCopy })
        {
            var state = (ExtensionData)value.ModuleDataDic["extension"];
            Check(state.Payload[0] == 7 && state.Configuration == "kept" &&
                !ReferenceEquals(state.Payload, extension.Payload), "MOD codec或内存工厂丢失不透明状态");
        }
    }

    private sealed class ExtensionData : ModuleData
    {
        [FastCloner.Code.FastClonerIgnore] public byte[] Payload;
        public string Configuration;
    }

    private sealed class ExtensionCodec : IModuleInstanceStateCodec
    {
        public string Id => "probe:natural-extension";
        public Type DataType => typeof(ExtensionData);
        public ModuleData CreateData() => new ExtensionData();
        public byte[] Capture(ModuleData data, bool publicState) => ((ExtensionData)data).Payload;
        public void Restore(ModuleData data, byte[] payload) => ((ExtensionData)data).Payload = payload;
    }

    private sealed class ExtensionFactory : IModuleRuntimeDataFactory
    {
        public Type DataType => typeof(ExtensionData);
        public Func<ModuleData> Compile(ModuleData defaults)
        {
            var frozen = (ExtensionData)defaults;
            return () => new ExtensionData
            { Configuration = frozen.Configuration, Payload = (byte[])frozen.Payload.Clone() };
        }
    }

    #endregion
}
