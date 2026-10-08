using MemoryPack;
using UnityEngine;

/// <summary>宇航服装备效果主动读取实际气罐，供氧和整套耐压均由服装侧处理。</summary>
[System.Serializable, MemoryPackable]
public partial class EquipmentInstance_SpacesuitSupport : EquipmentInstance, IAmbientOxygenBlocker
{
    #region 装备生命周期与供给
    [MemoryPackIgnore] private Item owner;
    [MemoryPackIgnore] private Mod_Oxygen oxygen;
    [MemoryPackIgnore] private float supplyClock;
    public override bool RequiresUpdate => true;
    [MemoryPackIgnore] public bool BlocksAmbientOxygen => TryGetCurrentSuit(out SpacesuitBinding suit) && suit.IsHelmetSealed;

    public override void Equip(Item item)
    {
        owner = item;
        oxygen = item != null ? item.itemMods.GetMod_ByID<Mod_Oxygen>(Mod_Oxygen.ModuleId) : null;
        SpacesuitSystem.RefreshProtection(owner);
    }
    public override void Update() => Update(Time.deltaTime);
    public override void Update(float deltaTime)
    {
        if (owner == null) return;
        SpacesuitSystem.RefreshProtection(owner);
        if (!float.IsFinite(deltaTime) || deltaTime <= 0f) return;
        if (oxygen == null || !oxygen.CanReceiveOxygen || !TryGetCurrentSuit(out SpacesuitBinding suit) || !suit.IsHelmetSealed)
        { supplyClock = 0f; return; }
        supplyClock += deltaTime;
        if (supplyClock < .2f) return;
        float seconds = supplyClock;
        supplyClock = 0f;
        suit.SupplyOxygen(oxygen, seconds);
    }
    public override void UnEquip(Item item)
    {
        SpacesuitSystem.ClearProtection(owner);
        SpacesuitPanelSession.CloseFor(EquippedItem);
        owner = null;
        oxygen = null;
        supplyClock = 0f;
        BindEquippedItem(null);
    }
    private bool TryGetCurrentSuit(out SpacesuitBinding suit)
    {
        suit = null;
        if (owner == null || EquippedItem == null || !SpacesuitSystem.IsActuallyEquipped(owner, EquippedItem)) return false;
        suit = SpacesuitSystem.GetBinding(EquippedItem, owner);
        return suit != null;
    }
    #endregion
}
