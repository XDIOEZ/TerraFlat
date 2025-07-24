using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AoutTurnBody : TurnBody
{
    public ISpeed mover;
    public new void Load()
    {
        base.Load();
        mover = GetComponentInParent<ISpeed>();
    }
}
