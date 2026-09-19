using MemoryPack;
using UnityEngine;
using System.Linq;

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

    // 追加字段保持旧数据布局；空数组保留旧装备的全身覆盖语义。
    public BodyPartType[] CoveredParts = new BodyPartType[0];

    [MemoryPackIgnore] private DamageReceiver appliedReceiver;
    [MemoryPackIgnore] private CombatDefense appliedDefense;
    [MemoryPackIgnore] private bool appliedToBodyParts;

    public override void Equip(Item item)
    {
        if (_isApplied)
            return;

        var damageReceiver = item.itemMods.GetMod_ByID<DamageReceiver>(ModText.Hp);
        if (damageReceiver == null)
            throw new MissingComponentException($"[{nameof(EquipmentInstance_Defense)}] Cannot find {nameof(DamageReceiver)} on item {item?.name}");

        CombatDefense bonus = ResolveDefenseBonus();
        appliedDefense = new CombatDefense(bonus.Cutting, bonus.Piercing, bonus.Chopping, bonus.Blunt);
        appliedReceiver = damageReceiver;
        appliedToBodyParts = damageReceiver.UsesBodyPartHealth;
        if (appliedToBodyParts)
        {
            BodyPartType[] parts = CoveredParts != null && CoveredParts.Length > 0
                ? CoveredParts : damageReceiver.BodyParts.Select(part => part.Part).ToArray();
            // 部位装备不再同时写入全身防御，否则头盔会保护腿，且可能重复抵扣。
            damageReceiver.SetBodyPartArmor(this, parts, appliedDefense);
        }
        else
            damageReceiver.AddDefense(appliedDefense);
        _isApplied = true;
    }

    public override void UnEquip(Item item)
    {
        if (!_isApplied)
            return;

        if (appliedReceiver != null)
        {
            if (appliedToBodyParts)
                appliedReceiver.RemoveBodyPartArmor(this);
            else
                appliedReceiver.RemoveDefense(appliedDefense);
        }
        appliedReceiver = null;
        appliedDefense = null;
        appliedToBodyParts = false;
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
