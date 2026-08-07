using System.Collections;
using System.Collections.Generic;
using MemoryPack;
using UnityEngine;

[System.Serializable]
[MemoryPackable]
public partial class EquipmentInstance_Speed : EquipmentInstance
{
    public float SpeedIncrease = 0f;

    [MemoryPackIgnore]
    private bool _isApplied = false;

    public override void Equip(Item item)
    {
        if (_isApplied)
            return;

        item.itemMods.GetMod_ByID<Mover>(ModText.Mover).Data.Speed.AdditiveModifier += SpeedIncrease;
        _isApplied = true;
    }

    public override void UnEquip(Item item)
    {
        if (!_isApplied)
            return;

        item.itemMods.GetMod_ByID<Mover>(ModText.Mover).Data.Speed.AdditiveModifier -= SpeedIncrease;
        _isApplied = false;
    }

    public override void Update()
    {
    }
}
