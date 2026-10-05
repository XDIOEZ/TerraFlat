using MemoryPack;
using UnityEngine;

/// <summary>衣物、裤装和袜子的温度耐受效果；只扩展安全范围，不改变体表温度或传热速度。</summary>
[System.Serializable]
[MemoryPackable]
public partial class EquipmentInstance_ThermalInsulation : EquipmentInstance
{
    [Min(0f)]
    public float ColdToleranceIncrease;
    [Min(0f)]
    public float HeatToleranceIncrease;

    [MemoryPackIgnore]
    private Mod_Temperature appliedTemperature;

    public override void Equip(Item item)
    {
        if (appliedTemperature != null)
            return;

        Mod_Temperature temperature = item?.itemMods?.GetMod_ByID<Mod_Temperature>(ModText.Temperature);
        if (temperature?.Data == null)
            return;

        temperature.SetSafeTemperatureRangeModifier(
            this,
            -Mathf.Max(0f, ColdToleranceIncrease),
            Mathf.Max(0f, HeatToleranceIncrease));
        appliedTemperature = temperature;
    }

    public override void Update()
    {
    }

    public override void UnEquip(Item item)
    {
        appliedTemperature?.RemoveSafeTemperatureRangeModifier(this);

        appliedTemperature = null;
    }
}
