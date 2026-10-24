using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>矿车的引擎和货物使用正式库存，面板只绑定这些真实槽位。</summary>
public sealed class Mod_MinecartInventory : Module, IInventory
{
    #region 库存配置和生命周期
    public const string ModuleId = "Mod_MinecartInventory";
    public Inventory_ModuleData Data = new();
    public override ModuleData _Data { get => Data; set => Data = (Inventory_ModuleData)value; }
    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.EveryFrame;
    public int CargoSlots = 18;
    public float MaxCargoWeightKg = 600f;
    public float MaxCargoVolumeCubicMeters = .6f;
    public Inventory EngineInventory { get; private set; }
    public Inventory CargoInventory { get; private set; }

    protected override void OnLoad()
    {
        if (CargoSlots <= 0 || !(MaxCargoWeightKg > 0f) || !(MaxCargoVolumeCubicMeters > 0f) ||
            !float.IsFinite(MaxCargoWeightKg + MaxCargoVolumeCubicMeters))
            throw new InvalidOperationException("矿车货仓格数和容量必须有效。");
        Data.Data ??= new Dictionary<string, Inventory_Data>();
        EngineInventory = Create("Engines", 2);
        CargoInventory = Create("Cargo", CargoSlots);
        CargoInventory.StorageMaxWeightKg = MaxCargoWeightKg;
        CargoInventory.StorageMaxVolumeCubicMeters = MaxCargoVolumeCubicMeters;
        EngineInventory.InitData(); CargoInventory.InitData();
        foreach (ItemSlot slot in EngineInventory.Data.itemSlots) slot.SlotMaxVolume = 1f;
        EngineInventory.Data.SetItemAcceptancePolicy((incoming, _) => CanStore(incoming) && EnginePowerService.IsEngine(incoming));
        CargoInventory.Data.SetItemAcceptancePolicy((incoming, _) => CanStore(incoming));
    }

    private Inventory Create(string name, int count)
    {
        if (!Data.Data.TryGetValue(name, out Inventory_Data saved) || saved == null)
        {
            var slots = new List<ItemSlot>(count);
            for (int i = 0; i < count; i++) slots.Add(new ItemSlot(i));
            saved = new Inventory_Data(slots, name) { PanelIsOpen = false };
            Data.Data[name] = saved;
        }
        if (saved.itemSlots == null || saved.itemSlots.Count != count)
            throw new InvalidOperationException("矿车库存布局与当前定义不一致。");
        foreach (ItemSlot slot in saved.itemSlots)
            if (slot?.itemData != null && GameRes.ExistingInstance != null)
                slot.itemData = ItemDefinitionRuntime.RebasePersistedData(GameRes.ExistingInstance, slot.itemData);
        return new Inventory { item = item, Data = saved, RuntimeAccessGuard = () => false };
    }

    /// <summary>拒绝把自身或持有自身的容器放入货仓，阻断嵌套库存环。</summary>
    private bool CanStore(ItemData incoming)
        => !ContainsHost(incoming, new HashSet<ItemData>());

    private bool ContainsHost(ItemData candidate, HashSet<ItemData> visited)
    {
        if (candidate == null) return false;
        if (ReferenceEquals(candidate, Item_Data) || candidate.Guid != 0 && candidate.Guid == Item_Data.Guid) return true;
        if (!visited.Add(candidate)) return true;
        if (candidate.ModuleDataDic != null)
            foreach (ModuleData module in candidate.ModuleDataDic.Values)
                if (module is Inventory_ModuleData inventory && inventory.Data != null)
                    foreach (Inventory_Data nested in inventory.Data.Values)
                        if (nested?.itemSlots != null)
                            foreach (ItemSlot slot in nested.itemSlots)
                                if (ContainsHost(slot?.itemData, visited)) return true;
        visited.Remove(candidate);
        return false;
    }

    public Inventory GetDefaultTargetInventory() => CargoInventory;
    public override void ModUpdate(float deltaTime)
    { EngineInventory?.ModUpdate(deltaTime); CargoInventory?.ModUpdate(deltaTime); }
    protected override void OnSave() { EngineInventory?.Save(); CargoInventory?.Save(); }
    protected override void OnUnload()
    { Release(EngineInventory); Release(CargoInventory); EngineInventory = CargoInventory = null; }
    private static void Release(Inventory inventory)
    {
        if (inventory == null) return;
        inventory.Data.SetItemAcceptancePolicy(null);
        inventory.RuntimeAccessGuard = () => false;
        inventory.UnbindPlayerCarryWeightEvents(); inventory.UnbindController();
        inventory.UnbindRuntimeDataEvents(); inventory.ReleaseRuntimePanel(); inventory.item = null;
    }
    #endregion
}
