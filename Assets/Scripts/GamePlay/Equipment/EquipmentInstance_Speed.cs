using System.Collections;
using System.Collections.Generic;
using MemoryPack;
using UnityEngine;

[System.Serializable]
[MemoryPackable]
public partial class EquipmentInstance_Speed : EquipmentInstance
{
    public float SpeedIncrease = 0f;
    public override void Equip(Item item)
    {
        item.itemMods.GetMod_ByID<Mover>(ModText.Mover).Data.Speed.AdditiveModifier += SpeedIncrease;
    }

    public override void UnEquip(Item item)
    {
        item.itemMods.GetMod_ByID<Mover>(ModText.Mover).Data.Speed.AdditiveModifier -= SpeedIncrease;
    }

    public override void Update()
    {
    }
}
