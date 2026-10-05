using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Mod_FocusPoint_AI : Mod_FocusPoint
{
    public Mod_Mover mover;
    public override void Load()
    {
        ModData.ReadData(ref Data);

        mover = item.itemMods.GetMod_ByID(ModText.Mod_Mover) as Mod_Mover;
    }

    public override void ModUpdate(float deltaTime)
    {
        Data.Move_Point = mover.TargetPosition;
        Data.See_Point = mover.TargetPosition;
        Data.DefaultSkill_Point = mover.TargetPosition;
    }
}
