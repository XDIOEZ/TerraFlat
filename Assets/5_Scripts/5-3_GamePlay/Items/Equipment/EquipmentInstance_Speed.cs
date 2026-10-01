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

    [MemoryPackIgnore]
    private Mod_Mover appliedMover;

    public override void Equip(Item item)
    {
        if (_isApplied)
            return;

        Mod_Mover mover = item?.itemMods?.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover);
        if (mover?.Data?.Speed == null)
            return;

        mover.Data.Speed.AdditiveModifier += SpeedIncrease;
        appliedMover = mover;
        _isApplied = true;
    }

    public override void UnEquip(Item item)
    {
        if (!_isApplied)
            return;

        if (appliedMover?.Data?.Speed != null)
            appliedMover.Data.Speed.AdditiveModifier -= SpeedIncrease;

        appliedMover = null;
        _isApplied = false;
    }

    public override void Update()
    {
    }
}
