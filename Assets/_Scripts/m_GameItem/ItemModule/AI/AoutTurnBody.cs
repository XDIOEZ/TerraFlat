using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AoutTurnBody : TurnBody
{
    public Mover mover;
    public override void Load()
    {
        base.Load();
        mover = item.Mods[ModText.Mover] as Mover;
    }
    public override void Action(float delta)
    {
        UpdateTurn(delta);

        if (mover != null)
        {
            Vector2 direction = mover.TargetPosition - (Vector2)item.transform.position;
            TurnBodyToDirection(direction);
        }
    }
}

