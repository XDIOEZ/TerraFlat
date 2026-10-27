using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>独轮车货仓沿用正式库存、容量规则和建筑拆装快照。</summary>
public sealed class Mod_WheelbarrowInventory : Module, IInventory
{
    #region 货仓配置和生命周期
    public const string ModuleId = "Mod_WheelbarrowInventory";
    public Inventory_ModuleData Data = new();
    public override ModuleData _Data { get => Data; set => Data = (Inventory_ModuleData)value; }
    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.EveryFrame;
    public int CargoSlots = 12;
    public float MaxCargoWeightKg = 120f;
    public float MaxCargoVolumeCubicMeters = .3f;
    public Inventory CargoInventory { get; private set; }

    protected override void OnLoad()
    {
        if (CargoSlots <= 0 || !(MaxCargoWeightKg > 0f) || !(MaxCargoVolumeCubicMeters > 0f) ||
            !float.IsFinite(MaxCargoWeightKg + MaxCargoVolumeCubicMeters))
            throw new InvalidOperationException("独轮车货仓格数和容量必须有效。");
        Data.Data ??= new Dictionary<string, Inventory_Data>();
        if (!Data.Data.TryGetValue("Cargo", out Inventory_Data cargo) || cargo == null)
        {
            var slots = new List<ItemSlot>(CargoSlots);
            for (int i = 0; i < CargoSlots; i++) slots.Add(new ItemSlot(i));
            Data.Data["Cargo"] = cargo = new Inventory_Data(slots, "Cargo");
        }
        if (cargo.itemSlots == null || cargo.itemSlots.Count != CargoSlots)
            throw new InvalidOperationException("独轮车货仓布局与当前定义不一致。");
        foreach (ItemSlot slot in cargo.itemSlots)
            if (slot?.itemData != null && GameRes.ExistingInstance != null)
                slot.itemData = ItemDefinitionRuntime.RebasePersistedData(GameRes.ExistingInstance, slot.itemData);
        cargo.PanelIsOpen = false;
        CargoInventory = new Inventory
        {
            item = item,
            Data = cargo,
            StorageMaxWeightKg = MaxCargoWeightKg,
            StorageMaxVolumeCubicMeters = MaxCargoVolumeCubicMeters,
            RuntimeAccessGuard = () => false
        };
        CargoInventory.InitData();
        cargo.SetItemAcceptancePolicy((incoming, _) => !ContainsHost(incoming, new HashSet<ItemData>()));
    }

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
    public override void ModUpdate(float deltaTime) => CargoInventory?.ModUpdate(deltaTime);
    protected override void OnSave() => CargoInventory?.Save();
    protected override void OnUnload()
    {
        if (CargoInventory == null) return;
        CargoInventory.Data.SetItemAcceptancePolicy(null);
        CargoInventory.RuntimeAccessGuard = () => false;
        CargoInventory.UnbindPlayerCarryWeightEvents();
        CargoInventory.UnbindController();
        CargoInventory.UnbindRuntimeDataEvents();
        CargoInventory.ReleaseRuntimePanel();
        CargoInventory.item = null;
        CargoInventory = null;
    }
    #endregion
}
