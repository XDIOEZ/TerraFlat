using MemoryPack;
using UnityEngine;

[System.Serializable]
[MemoryPackable]
public partial class EquipmentInstance_Defense : EquipmentInstance
{
    public float DefenseBonusIncrease = 0f;

    [MemoryPackIgnore]
    private bool _isApplied = false;

    public override void Equip(Item item)
    {
        if (_isApplied)
            return;

        var damageReceiver = item.itemMods.GetMod_ByID<DamageReceiver>(ModText.Hp);
        if (damageReceiver == null)
            throw new MissingComponentException($"[{nameof(EquipmentInstance_Defense)}] Cannot find {nameof(DamageReceiver)} on item {item?.name}");

        damageReceiver.AddDefense(DefenseBonusIncrease);
        _isApplied = true;
    }

    public override void UnEquip(Item item)
    {
        if (!_isApplied)
            return;

        var damageReceiver = item.itemMods.GetMod_ByID<DamageReceiver>(ModText.Hp);
        if (damageReceiver == null)
            throw new MissingComponentException($"[{nameof(EquipmentInstance_Defense)}] Cannot find {nameof(DamageReceiver)} on item {item?.name}");

        damageReceiver.RemoveDefense(DefenseBonusIncrease);
        _isApplied = false;
    }

    public override void Update()
    {
    }
}
