using MemoryPack;
using UnityEngine;

/// <summary>
/// 水体隔热装备效果。
/// WaterCoolingProtection 使用 0~1 的保护比例：0 不影响，0.8 将入水降温速度降至 20%，1 完全阻止该次入水降温。
/// 多件装备按保护值相加后统一限制到 100%，由体温模块负责最终结算。
/// </summary>
[System.Serializable]
[MemoryPackable]
public partial class EquipmentInstance_WaterInsulation : EquipmentInstance
{
    [Range(0f, 1f)]
    [Tooltip("入水降温保护比例。0=无保护，0.8=降温速度剩余20%，1=完全免疫入水降温。")]
    public float WaterCoolingProtection;

    [MemoryPackIgnore]
    private bool _isApplied;

    /// <summary>装备时向角色体温模块登记水体隔热保护。</summary>
    public override void Equip(Item item)
    {
        if (_isApplied)
            return;

        Mod_Temperature temperature = item?.itemMods?.GetMod_ByID<Mod_Temperature>(ModText.Temperature);
        if (temperature == null)
            throw new MissingComponentException($"[{nameof(EquipmentInstance_WaterInsulation)}] Cannot find {nameof(Mod_Temperature)} on item {item?.name}");

        temperature.AddWaterCoolingProtection(Mathf.Clamp01(WaterCoolingProtection));
        _isApplied = true;
    }

    /// <summary>装备效果当前无需逐帧更新。</summary>
    public override void Update()
    {
    }

    /// <summary>卸下时移除此前登记的水体隔热保护。</summary>
    public override void UnEquip(Item item)
    {
        if (!_isApplied)
            return;

        Mod_Temperature temperature = item?.itemMods?.GetMod_ByID<Mod_Temperature>(ModText.Temperature);
        if (temperature == null)
            throw new MissingComponentException($"[{nameof(EquipmentInstance_WaterInsulation)}] Cannot find {nameof(Mod_Temperature)} on item {item?.name}");

        temperature.RemoveWaterCoolingProtection(Mathf.Clamp01(WaterCoolingProtection));
        _isApplied = false;
    }
}
