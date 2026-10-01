using MemoryPack;

/// <summary>衣物、裤装和袜子的保温效果；没有体温模块的装备宿主会自然忽略该效果。</summary>
[System.Serializable]
[MemoryPackable]
public partial class EquipmentInstance_ThermalInsulation : EquipmentInstance
{
    public float InsulationIncrease;

    [MemoryPackIgnore]
    private Mod_Temperature appliedTemperature;

    public override void Equip(Item item)
    {
        if (appliedTemperature != null)
            return;

        Mod_Temperature temperature = item?.itemMods?.GetMod_ByID<Mod_Temperature>(ModText.Temperature);
        if (temperature?.Data == null)
            return;

        temperature.Data.Insulation += InsulationIncrease;
        appliedTemperature = temperature;
    }

    public override void Update()
    {
    }

    public override void UnEquip(Item item)
    {
        if (appliedTemperature?.Data != null)
            appliedTemperature.Data.Insulation -= InsulationIncrease;

        appliedTemperature = null;
    }
}
