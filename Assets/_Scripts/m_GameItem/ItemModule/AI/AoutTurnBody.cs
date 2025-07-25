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

    public override void Update()
    {
        TurnBodyToDirection(mover.TargetPosition-item.transform.position);
    }
}
