using MemoryPack;
using UnityEngine;

[System.Serializable]
[MemoryPackable]
public partial class EquipmentInstance_Defense : EquipmentInstance
{
    [HideInInspector]
    public float DefenseBonusIncrease = 0f;

    [MemoryPackIgnore]
    private bool _isApplied = false;

    // MemoryPack 兼容：新字段只追加在旧 DefenseBonusIncrease 后面。
    public CombatDefense DefenseBonus = new CombatDefense();

    public override void Equip(Item item)
    {
        if (_isApplied)
            return;

        var damageReceiver = item.itemMods.GetMod_ByID<DamageReceiver>(ModText.Hp);
        if (damageReceiver == null)
            throw new MissingComponentException($"[{nameof(EquipmentInstance_Defense)}] Cannot find {nameof(DamageReceiver)} on item {item?.name}");

        damageReceiver.AddDefense(ResolveDefenseBonus());
        _isApplied = true;
    }

    public override void UnEquip(Item item)
    {
        if (!_isApplied)
            return;

        var damageReceiver = item.itemMods.GetMod_ByID<DamageReceiver>(ModText.Hp);
        if (damageReceiver == null)
            throw new MissingComponentException($"[{nameof(EquipmentInstance_Defense)}] Cannot find {nameof(DamageReceiver)} on item {item?.name}");

        damageReceiver.RemoveDefense(ResolveDefenseBonus());
        _isApplied = false;
    }

    public override void Update()
    {
    }

    /// <summary>兼容旧装备单值防御；新装备直接填写四类防御。</summary>
    private CombatDefense ResolveDefenseBonus()
    {
        DefenseBonus ??= new CombatDefense();
        DefenseBonus.ClampNonNegative();
        if (DefenseBonus.TotalDefense > 0f || DefenseBonusIncrease <= 0f)
            return DefenseBonus;

        float value = Mathf.Max(0f, DefenseBonusIncrease);
        DefenseBonus = new CombatDefense(value, value, value, value);
        return DefenseBonus;
    }
}
