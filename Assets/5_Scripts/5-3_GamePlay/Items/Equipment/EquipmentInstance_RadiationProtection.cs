using MemoryPack;

/// <summary>辐射防护装备只扩展穿戴者的安全辐射范围。</summary>
#region 辐射耐受装备能力
[System.Serializable, MemoryPackable(GenerateType.VersionTolerant)]
public partial class EquipmentInstance_RadiationProtection : EquipmentInstance
{
    [MemoryPackOrder(1)] public float LowRadiationToleranceIncrease;
    [MemoryPackOrder(2)] public float HighRadiationToleranceIncrease;

    [MemoryPackIgnore] private Mod_Radiation applied;

    public override void Equip(Item owner)
    {
        if (applied != null || owner == null) return;
        applied = owner.itemMods?.GetMod_ByID<Mod_Radiation>(Mod_Radiation.ModuleId);
        applied?.SetSafeRadiationRangeModifier(this, LowRadiationToleranceIncrease, HighRadiationToleranceIncrease);
    }

    public override void Update() { }

    public override void UnEquip(Item owner)
    {
        applied?.RemoveSafeRadiationRangeModifier(this);
        applied = null;
    }
}
#endregion
