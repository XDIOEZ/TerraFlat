using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Mod_AoutTurnBody : Mod_TurnBack
{
    public Mod_Mover mover;
    protected override void OnLoad()
    {
        base.OnLoad();
        mover = item.Mods[ModText.Mod_Mover] as Mod_Mover;
    }
    public override void ModUpdate(float delta)
    {
        UpdateTurn(delta);

        if (mover != null)
        {
            Vector2 direction = mover.TargetPosition - (Vector2)item.transform.position;
            TurnBodyToDirection(direction);
        }
    }
}

