using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Mod_FocusPoint_AI : Mod_FocusPoint
{
    public override ModuleTickMode TickMode => ModuleTickMode.EveryFrame;
    public override bool WantsRenderFrameUpdate => false;
    public override bool WantsInputFrameUpdate => ActorProfileRegistry.IsLocallyControlled(item);
    public Mod_Mover mover;
    protected override void OnLoad()
    {
        base.OnLoad();
        mover = item.itemMods.GetMod_ByID(ModText.Mod_Mover) as Mod_Mover;
    }

    public override void ModUpdate(float deltaTime)
    {
        if (ActorProfileRegistry.IsManuallyControlled(item))
        {
            if (ActorProfileRegistry.IsLocallyControlled(item)) base.ModUpdate(deltaTime);
            return;
        }
        if (mover == null) return;
        Data.Move_Point = mover.TargetPosition;
        Data.See_Point = mover.TargetPosition;
        Data.DefaultSkill_Point = mover.TargetPosition;
    }
}
