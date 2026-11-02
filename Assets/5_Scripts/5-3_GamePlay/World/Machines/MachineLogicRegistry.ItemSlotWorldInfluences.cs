using System.Collections.Generic;

public static partial class MachineLogicRegistry
{
    #region 冷态机器库存槽快照

    private static void CollectCoreProcessorItemSlotBindings(MachineEntity machine, List<ItemSlotWorldBinding> result)
    {
        MachineState state = machine.State ?? MachineWorld.ReadMachineState(machine.Snapshot);
        CollectColdInventoryItemSlotBindings(state?.Processing?.Input, "core-input", result);
        CollectColdInventoryItemSlotBindings(state?.Processing?.Output, "core-output", result);
    }

    private static void CollectWorkbenchItemSlotBindings(MachineEntity machine, List<ItemSlotWorldBinding> result)
    {
        RecipeProcessingState state = MachinePersistence.Read<RecipeProcessingState>(machine.Snapshot, "workbench");
        Mod_MakeTable authoring = machine.Definition.Content.Find<Mod_MakeTable>()?.Authoring as Mod_MakeTable;
        CollectColdInventoryItemSlotBindings(state?.Input ?? authoring?.inputInventory?.Data, "workbench-input", result);
        CollectColdInventoryItemSlotBindings(state?.Output ?? authoring?.outputInventory?.Data, "workbench-output", result);
    }

    private static void CollectFurnaceItemSlotBindings(MachineEntity machine, List<ItemSlotWorldBinding> result)
    {
        FurnaceRuntimeState state = MachinePersistence.Read<FurnaceRuntimeState>(machine.Snapshot, "furnace");
        ModSmeltingData smelting = state?.Smelting;
        if (smelting == null && machine.Definition.Content.Find<Mod_Furnace>() is MachineModuleConfiguration config &&
            config.Authoring is Mod_Furnace authoring)
            smelting = config.Data("Data", authoring.Data ?? new ModSmeltingData());

        Mod_Furnace furnace = machine.Definition.Content.Find<Mod_Furnace>()?.Authoring as Mod_Furnace;
        Dictionary<string, Inventory_Data> saved = smelting?.InvData;
        CollectColdInventoryItemSlotBindings(FindColdInventory(saved, "furnace.input") ?? furnace?.InputInventory?.Data,
            "furnace-input", result);
        CollectColdInventoryItemSlotBindings(FindColdInventory(saved, "furnace.output") ?? furnace?.OutputInventory?.Data,
            "furnace-output", result);
        CollectColdInventoryItemSlotBindings(FindColdInventory(saved, "furnace.fuel") ?? furnace?.FuelInventory?.Data,
            "furnace-fuel", result);
    }

    private static void CollectCompostItemSlotBindings(MachineEntity machine, List<ItemSlotWorldBinding> result)
    {
        SlotProcessingState state = MachinePersistence.Read<SlotProcessingState>(machine.Snapshot, "compost");
        Mod_CompostBin authoring = machine.Definition.Content.Find<Mod_CompostBin>()?.Authoring as Mod_CompostBin;
        CollectColdInventoryItemSlotBindings(state?.Inventory ?? authoring?.CompostInventory?.Data, "compost", result);
    }

    private static void CollectDryingRackItemSlotBindings(MachineEntity machine, List<ItemSlotWorldBinding> result)
    {
        SlotProcessingState state = MachinePersistence.Read<SlotProcessingState>(machine.Snapshot, "drying");
        Mod_Meatrack authoring = machine.Definition.Content.Find<Mod_Meatrack>()?.Authoring as Mod_Meatrack;
        CollectColdInventoryItemSlotBindings(state?.Inventory ?? authoring?.RackInventory?.Data, "drying", result);
    }

    private static void CollectStorageItemSlotBindings(MachineEntity machine, List<ItemSlotWorldBinding> result)
    {
        MachineModuleConfiguration config = machine.Definition.Content.Find<Mod_Inventory>();
        if (config?.Authoring is not Mod_Inventory authoring) return;
        MachineStorageState state = MachinePersistence.Read<MachineStorageState>(machine.Snapshot, "storage");
        Inventory_ModuleData initial = null;
        if (machine.Snapshot.ModuleDataDic?.TryGetValue(config.Declaration.StableName, out ModuleData module) == true)
            initial = module as Inventory_ModuleData;
        initial ??= authoring.Data;

        if (authoring.InventoryInstances == null) return;
        for (int index = 0; index < authoring.InventoryInstances.Count; index++)
        {
            Inventory template = authoring.InventoryInstances[index];
            Inventory_Data inventory = index < (state?.Inventories?.Count ?? 0) ? state.Inventories[index] : null;
            if (inventory == null && initial?.Data?.TryGetValue(Mod_Inventory.GetInventoryKey(template, index), out Inventory_Data seeded) == true)
                inventory = seeded;
            CollectColdInventoryItemSlotBindings(inventory ?? template?.Data, "storage-" + index, result);
        }
    }

    private static void CollectManualProcessingItemSlotBindings(MachineEntity machine, List<ItemSlotWorldBinding> result)
    {
        ManualProcessingState state = TryReadColdModuleState<ManualProcessingState>(machine.Snapshot, Mod_ManualProcessor.ModuleId);
        CollectColdInventoryItemSlotBindings(state?.Processing?.Input, "manual-input", result);
        CollectColdInventoryItemSlotBindings(state?.Processing?.Output, "manual-output", result);
        CollectColdInventoryItemSlotBindings(state?.Tool, "manual-tool", result);
    }

    private static void CollectHandDrillItemSlotBindings(MachineEntity machine, List<ItemSlotWorldBinding> result)
    {
        HandDrillRuntimeState state = TryReadColdModuleState<HandDrillRuntimeState>(machine.Snapshot, Mod_HandDrill.ModuleId);
        CollectColdInventoryItemSlotBindings(state?.Processing?.Input, "hand-drill-input", result);
        CollectColdInventoryItemSlotBindings(state?.Processing?.Output, "hand-drill-output", result);
    }

    private static void CollectFireDrillItemSlotBindings(MachineEntity machine, List<ItemSlotWorldBinding> result)
    {
        FireDrillRuntimeState state = TryReadColdModuleState<FireDrillRuntimeState>(machine.Snapshot, FireDrillLogic.ModuleId);
        Mod_FireDrill authoring = machine.Definition.Content.Find<Mod_FireDrill>()?.Authoring as Mod_FireDrill;
        CollectColdInventoryItemSlotBindings(state?.Input ?? authoring?.InputInventory?.Data, "fire-drill-input", result);
        CollectColdInventoryItemSlotBindings(state?.Output ?? authoring?.OutputInventory?.Data, "fire-drill-output", result);
    }

    private static void CollectMortarItemSlotBindings(MachineEntity machine, List<ItemSlotWorldBinding> result)
    {
        MortarState state = TryReadColdModuleState<MortarState>(machine.Snapshot, Mod_Mortar.ModuleId);
        CollectColdInventoryItemSlotBindings(state?.Bowl, "mortar-bowl", result);
    }

    private static void CollectVesselItemSlotBindings(MachineEntity machine, List<ItemSlotWorldBinding> result)
    {
        VesselContentsState state = TryReadColdModuleState<VesselContentsState>(machine.Snapshot, Mod_VesselContents.ModuleId);
        CollectColdInventorySnapshotItemSlotBindings(state?.Items, "vessel-contents", result);
    }

    private static void CollectFluidMachineItemSlotBindings(MachineEntity machine, List<ItemSlotWorldBinding> result)
    {
        FluidMachineState state = MachinePersistence.Read<FluidMachineState>(machine.Snapshot, "fluid");
        CollectColdInventoryItemSlotBindings(state?.PortableTank, "fluid-portable-tank", result);
        CollectColdInventoryItemSlotBindings(state?.FuelInventory, "fluid-solid-fuel", result);
    }

    private static void CollectInductionStoveItemSlotBindings(MachineEntity machine, List<ItemSlotWorldBinding> result)
    {
        InductionStoveState state = MachinePersistence.Read<InductionStoveState>(machine.Snapshot, "induction-stove");
        CollectColdInventoryItemSlotBindings(state?.Input, "induction-input", result);
        CollectColdInventoryItemSlotBindings(state?.Output, "induction-output", result);
    }

    private static void CollectCultivatorItemSlotBindings(MachineEntity machine, List<ItemSlotWorldBinding> result)
    {
        CultivatorState state = TryReadColdModuleState<CultivatorState>(machine.Snapshot, Mod_Cultivator.ModuleId);
        CollectColdInventorySnapshotItemSlotBindings(state?.Seeds, "cultivator-seeds", result);
        CollectColdInventorySnapshotItemSlotBindings(state?.Harvest, "cultivator-harvest", result);
        CollectColdInventorySnapshotItemSlotBindings(state?.Fertilizer, "cultivator-fertilizer", result);
    }

    private static void CollectColdInventoryItemSlotBindings(
        Inventory_Data inventory, string inventoryId, List<ItemSlotWorldBinding> result)
    {
        if (inventory?.itemSlots == null) return;
        for (int index = 0; index < inventory.itemSlots.Count; index++)
        {
            ItemData itemData = inventory.itemSlots[index]?.itemData;
            if (itemData != null)
                result.Add(new ItemSlotWorldBinding(inventoryId + "/" + index, itemData));
        }
    }

    private static void CollectColdInventorySnapshotItemSlotBindings(
        InventoryInstanceSnapshot inventory, string inventoryId, List<ItemSlotWorldBinding> result)
    {
        if (inventory == null) return;
        for (int index = 0; index < inventory.SlotCount; index++)
        {
            ItemInstanceSnapshot item = inventory.GetItemSlotSnapshot(index);
            if (item != null)
                result.Add(new ItemSlotWorldBinding(inventoryId + "/" + index, item));
        }
    }

    private static Inventory_Data FindColdInventory(Dictionary<string, Inventory_Data> inventories, string key)
        => inventories != null && inventories.TryGetValue(key, out Inventory_Data inventory) ? inventory : null;

    private static T TryReadColdModuleState<T>(ItemData snapshot, string moduleId) where T : class
    {
        if (snapshot?.ModuleDataDic == null) return null;
        Ex_ModData_MemoryPackable state = null;
        foreach (ModuleData module in snapshot.ModuleDataDic.Values)
        {
            if (module?.ID != moduleId) continue;
            if (state != null || module is not Ex_ModData_MemoryPackable binary) return null;
            state = binary;
        }
        return state?.BitData?.Length > 0 ? state.GetData<T>() : null;
    }

    #endregion
}
