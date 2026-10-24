using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Pool;

public static partial class GameplayTestSystemProbe
{
    #region 机器独立快照与空炉体预备采样

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterMachinePreparationRules()
    {
        Register("mechanical", "runtime_snapshot_clone", ProbeMachineRuntimeSnapshotClone);
        Register("mechanical", "machine_pulse_order", ProbeMachinePulseOrder);
        Register("mechanical", "process_lookup_index", ProbeMachineProcessLookupIndex);
    }

    private static JObject ProbeMachineProcessLookupIndex(JObject args)
    {
        // 只查询正式配方目录，不增删运行中设备或当前游戏的配方。
        var rules = new List<MachineProcessDefinition>(MachineCatalog.Processes);
        bool exact = rules.Count > 0;
        foreach (MachineProcessDefinition rule in rules)
            exact &= MachineCatalog.TryGetProcess(rule.Station, rule.Input, out var found) &&
                ReferenceEquals(found, rule);
        bool missing = !MachineCatalog.TryGetProcess("probe:unknown-station", "probe:unknown-input", out _) &&
            !MachineCatalog.TryGetProcess(null, null, out _);
        MachineProcessDefinition first = rules.Count > 0 ? rules[0] : null;
        bool caseSensitive = first != null &&
            !MachineCatalog.TryGetProcess(first.Station.ToUpperInvariant(), first.Input.ToLowerInvariant(), out _);
        // 原始键允许嵌入分隔符；这类异常键必须仍走旧字符串查询。
        bool delimiterRejected = !MachineCatalog.TryGetProcess(first?.Station + "\u001fextra", first?.Input, out _);
        if (first != null)
            for (int i = 0; i < 64; i++)
                MachineCatalog.TryGetProcess(first.Station, first.Input, out _);
        int matched = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        if (rules.Count > 0)
            for (int i = 0; i < 8192; i++)
            {
                MachineProcessDefinition rule = rules[i % rules.Count];
                if (MachineCatalog.TryGetProcess(rule.Station, rule.Input, out MachineProcessDefinition found) &&
                    ReferenceEquals(found, rule))
                    matched++;
            }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        return new JObject
        {
            ["rules"] = rules.Count, ["exact"] = exact, ["missing"] = missing,
            ["caseSensitive"] = caseSensitive, ["delimiterRejected"] = delimiterRejected,
            ["matched"] = matched, ["allocatedBytes"] = allocated
        };
    }

    private static JObject ProbeMachinePulseOrder(JObject args)
    {
        // 不创建 Collider 或机器，不改变世界；直接验证实际战斗候选排序和租约边界。
        var hits = new List<CombatColliderHit2D>
        {
            new(0, .5f, new Vector2(5f, 0f)),
            new(1, 0f, new Vector2(10f, 0f)),
            new(2, .5f, new Vector2(1f, 0f)),
            new(3, 0f, new Vector2(1f, 0f))
        };
        using var firstLease = ListPool<MachineCombatBridge.OrderedMachineHit>.Get(out var first);
        MachineCombatBridge.SortMachineHits(hits, Vector2.zero, first);
        var firstOrder = new JArray();
        for (int i = 0; i < first.Count; i++) firstOrder.Add(first[i].Hit.CandidateId);

        using var secondLease = ListPool<MachineCombatBridge.OrderedMachineHit>.Get(out var second);
        bool nestedIndependent = !ReferenceEquals(first, second) && second.Count == 0;
        MachineCombatBridge.SortMachineHits(hits, new Vector2(10f, 0f), second);
        var secondOrder = new JArray();
        for (int i = 0; i < second.Count; i++) secondOrder.Add(second[i].Hit.CandidateId);
        bool firstUnchanged = first.Count == 4 && first[0].Hit.CandidateId == 3 && first[3].Hit.CandidateId == 0;

        MachineCombatBridge.SortMachineHits(new List<CombatColliderHit2D>(), Vector2.zero, first);
        bool emptyCleared = first.Count == 0;
        List<MachineCombatBridge.OrderedMachineHit> failingLeaseList = null;
        bool exceptionObserved = false;
        try
        {
            using var failingLease = ListPool<MachineCombatBridge.OrderedMachineHit>.Get(out failingLeaseList);
            failingLeaseList.Add(new MachineCombatBridge.OrderedMachineHit(hits[0], 25f));
            throw new InvalidOperationException("模拟伤害回调异常");
        }
        catch (InvalidOperationException) { exceptionObserved = true; }
        using var recoveredLease = ListPool<MachineCombatBridge.OrderedMachineHit>.Get(out var recovered);
        bool exceptionLeaseCleared = exceptionObserved && ReferenceEquals(failingLeaseList, recovered) &&
            recovered.Count == 0;
        return new JObject
        {
            ["first"] = firstOrder, ["second"] = secondOrder,
            ["nestedIndependent"] = nestedIndependent,
            ["firstUnchanged"] = firstUnchanged,
            ["emptyCleared"] = emptyCleared,
            ["exceptionLeaseCleared"] = exceptionLeaseCleared,
            ["sourceUntouched"] = hits[0].CandidateId == 0 && hits[3].CandidateId == 3
        };
    }

    private static JObject ProbeMachineRuntimeSnapshotClone(JObject args)
    {
        UnityEngine.Random.State random = UnityEngine.Random.state;
        int worlds = GameplayWorldRegistry.Worlds.Count;
        string worldKey = MachineWorld.WorldKey;
        ItemData nested = ItemInstanceDataFactory.Create(new Data_GeneralItem
        {
            IDName = "probe:material", Guid = 901,
            Stack = new ItemStack { Amount = 5, Weight = 2, Volume = 3, Stackable = true },
            Tags = new List<string> { "Material" }
        });
        var binary = new Ex_ModData_MemoryPackable
        { StableName = "furnace", ModuleId = "Furnace", BitData = new byte[] { 1, 2, 3 } };
        var inventory = new Inventory_ModuleData
        {
            StableName = "inventory", ModuleId = "Inventory",
            Data = new Dictionary<string, Inventory_Data>
            {
                ["input"] = new Inventory_Data(new List<ItemSlot>
                {
                    new(0) { itemData = nested, SlotMaxVolume = 100, CanAcceptTags = new List<string> { "Material" } }
                }, "input")
            }
        };
        ItemData source = ItemInstanceDataFactory.Create(new Data_GeneralItem
        {
            IDName = "probe:machine", Guid = 801, code = "machine-code",
            Stack = new ItemStack { Amount = 2, Weight = 11, Volume = 13, Stackable = true },
            MaxDurability = 200, Durability = 120, CraftedDurabilityMultiplier = 1.5f,
            ItemSpecialData = "{\"mod-state\":{\"value\":7}}", FactionId = "probe:faction",
            Tags = new List<string> { "Machine" },
            transform = new ItemTransform { position = new Vector3(4, 8, 0), scale = Vector3.one * 2 },
            MatterState = new ItemMatterState { Initialized = true, TemperatureCelsius = 81, Moisture = .2f },
            ModuleDataDic = new Dictionary<string, ModuleData> { ["furnace"] = binary, ["inventory"] = inventory }
        });
        source.Tags.Add(CraftedWorkEfficiencyQuality.TagPrefix + "1.25");
        MachinePersistence.Write(source, "core", new MachineState { Hp = 77, RatioIndex = 4, Generated = true });
        MachinePersistence.Write(source, "furnace", new FurnaceRuntimeState
        { Smelting = new ModSmeltingData { Temperature = 321, IsSmelting = true }, Fuel = new FuelData { Fuel = new Vector2(9, 20) } });
        ItemData clone = ItemInstanceDataFactory.CloneRuntime(source);
        var sourceBinary = (Ex_ModData_MemoryPackable)source.ModuleDataDic["furnace"];
        var clonedBinary = (Ex_ModData_MemoryPackable)clone.ModuleDataDic["furnace"];
        var sourceInventoryModule = (Inventory_ModuleData)source.ModuleDataDic["inventory"];
        var clonedInventoryModule = (Inventory_ModuleData)clone.ModuleDataDic["inventory"];
        Inventory_Data sourceInventory = sourceInventoryModule.Data["input"];
        Inventory_Data clonedInventory = clonedInventoryModule.Data["input"];
        ItemData sourceNested = sourceInventory.itemSlots[0].itemData;
        ItemData clonedNested = clonedInventory.itemSlots[0].itemData;
        var core = MachinePersistence.Read<MachineState>(clone, "core");
        var furnace = MachinePersistence.Read<FurnaceRuntimeState>(clone, "furnace");
        var result = new JObject
        {
            ["identity"] = clone.IDName == source.IDName && clone.Guid == source.Guid &&
                ((Data_GeneralItem)clone).code == "machine-code" && clone.FactionId == source.FactionId,
            ["configurationShared"] = ReferenceEquals(clone.SharedConfiguration, source.SharedConfiguration) &&
                ReferenceEquals(clonedNested.SharedConfiguration, sourceNested.SharedConfiguration),
            ["valuesPreserved"] = clone.Durability == source.Durability && clone.MaxDurability == source.MaxDurability &&
                clone.CraftedDurabilityMultiplier == 1.5f && CraftedWorkEfficiencyQuality.Read(clone) == 1.25f &&
                clone.ItemSpecialData == source.ItemSpecialData && clone.MatterState.TemperatureCelsius == 81 &&
                clone.Stack.Amount == 2 && clone.transform.position == source.transform.position &&
                clone.transform.rotation == source.transform.rotation && clone.transform.scale == source.transform.scale &&
                (int?)JObject.Parse(clone.ItemSpecialData)["mod-state"]?["value"] == 7 &&
                clonedNested.IDName == sourceNested.IDName && clonedNested.Guid == 901 && clonedNested.Stack.Amount == 5,
            ["machineStatePreserved"] = core.Hp == 77 && core.RatioIndex == 4 && core.Generated &&
                furnace.Smelting.Temperature == 321 && furnace.Smelting.IsSmelting && furnace.Fuel.Fuel.x == 9,
            ["rootIndependent"] = !ReferenceEquals(clone, source) && !ReferenceEquals(clone.Stack, source.Stack) &&
                !ReferenceEquals(clone.transform, source.transform) && !ReferenceEquals(clone.Tags, source.Tags) &&
                !ReferenceEquals(clone.MatterState, source.MatterState) && !ReferenceEquals(clone.ModuleDataDic, source.ModuleDataDic),
            ["modulesIndependent"] = !ReferenceEquals(sourceBinary, clonedBinary) &&
                !ReferenceEquals(sourceBinary.BitData, clonedBinary.BitData) && clonedBinary.BitData.Length == 3 &&
                clonedBinary.BitData[0] == 1 && clonedBinary.StableName == sourceBinary.StableName &&
                clonedBinary.ModuleId == sourceBinary.ModuleId,
            ["inventoryIndependent"] = !ReferenceEquals(sourceInventoryModule, clonedInventoryModule) &&
                !ReferenceEquals(sourceInventoryModule.Data, clonedInventoryModule.Data) &&
                !ReferenceEquals(sourceInventory, clonedInventory) && clonedInventory.Name == "input" &&
                !ReferenceEquals(sourceInventory.itemSlots, clonedInventory.itemSlots) &&
                !ReferenceEquals(sourceInventory.itemSlots[0], clonedInventory.itemSlots[0]) &&
                !ReferenceEquals(sourceNested, clonedNested) && !ReferenceEquals(sourceNested.Stack, clonedNested.Stack) &&
                !ReferenceEquals(sourceInventory.itemSlots[0].CanAcceptTags, clonedInventory.itemSlots[0].CanAcceptTags)
        };
        // 直接改副本的可变层，验证机器、库存与定义都不会被反向污染。
        clone.Stack.Amount = 8;
        clone.transform.position = Vector3.zero;
        clone.Tags.Clear();
        clone.MatterState.TemperatureCelsius = 0;
        clonedBinary.BitData[0] = 99;
        clonedBinary.Enabled = false;
        clonedNested.Stack.Amount = 1;
        clonedInventory.itemSlots[0].CanAcceptTags.Clear();
        result["sourceUnchanged"] = source.Stack.Amount == 2 && source.transform.position == new Vector3(4, 8, 0) &&
            source.MatterState.TemperatureCelsius == 81 && sourceBinary.BitData[0] == 1 && sourceBinary.Enabled &&
            sourceNested.Stack.Amount == 5 && sourceInventory.itemSlots[0].CanAcceptTags.Count == 1 &&
            CraftedWorkEfficiencyQuality.Read(source) == 1.25f;
        result["definitionUnchanged"] = source.SharedConfiguration.Tags.Count == 1 &&
            source.SharedConfiguration.Tags[0] == "Machine" && source.SharedConfiguration.UnitWeight == 11 &&
            source.SharedConfiguration.UnitVolume == 13 && sourceNested.SharedConfiguration.UnitWeight == 2;

        FurnaceRuntimeState empty = GameRes.CreateEmptyFurnacePreparationState();
        FurnaceRuntimeState other = GameRes.CreateEmptyFurnacePreparationState();
        MachinePersistence.Write(clone, "furnace", empty);
        FurnaceRuntimeState decoded = MachinePersistence.Read<FurnaceRuntimeState>(clone, "furnace");
        bool emptyInventories = true;
        foreach (Inventory_Data value in decoded.Smelting.InvData.Values)
            foreach (ItemSlot slot in value.itemSlots) emptyInventories &= slot?.itemData == null;
        result["preparationEmpty"] = !decoded.Smelting.IsSmelting && !decoded.AwaitingIgnition &&
            decoded.Fuel.Fuel == Vector2.zero && decoded.Smelting.InvData.Count == 3 && emptyInventories;
        result["preparationIndependent"] = !ReferenceEquals(empty, other) && !ReferenceEquals(empty.Smelting, other.Smelting) &&
            !ReferenceEquals(empty.Smelting.InvData["furnace.input"], other.Smelting.InvData["furnace.input"]);
        result["noWorldOrRandomChanges"] = GameplayWorldRegistry.Worlds.Count == worlds &&
            MachineWorld.WorldKey == worldKey && UnityEngine.Random.state.Equals(random);
        return result;
    }

    #endregion
}
