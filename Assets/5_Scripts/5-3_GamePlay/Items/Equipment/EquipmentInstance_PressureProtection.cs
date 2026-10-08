using MemoryPack;
using UnityEngine;

/// <summary>普通防护服以独立装备来源提供被动耐压，不消耗氧气或耐久。</summary>
[System.Serializable, MemoryPackable]
public partial class EquipmentInstance_PressureProtection : EquipmentInstance
{
    #region 耐压装备
    public float LowPressureToleranceIncreaseKPa;
    public float HighPressureToleranceIncreaseKPa;
    [MemoryPackIgnore] private Mod_Pressure applied;
    public override void Equip(Item owner)
    {
        if (applied != null || owner == null) return;
        applied = owner.itemMods.GetMod_ByID<Mod_Pressure>(Mod_Pressure.ModuleId);
        if (applied != null) applied.SetSafePressureRangeModifier(this, LowPressureToleranceIncreaseKPa, HighPressureToleranceIncreaseKPa);
    }
    public override void Update() { }
    public override void UnEquip(Item owner)
    {
        if (applied != null) applied.RemoveSafePressureRangeModifier(this);
        applied = null;
    }
    #endregion
}
