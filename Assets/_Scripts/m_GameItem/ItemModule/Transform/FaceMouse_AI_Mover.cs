using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FaceMouse_AI_Mover : FaceMouse
{
    public Mover mover;
    public override void Load()
    {
        ModData.ReadData(ref Data);

        mover = item.itemMods.GetMod_ByID(ModText.Mover) as Mover;
    }

    public override void ModUpdate(float deltaTime)
    {
        Data.FocusPoint = mover.TargetPosition;
    }
}
