using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AoutTurnBody : TurnBody
{
    public ISpeed mover;
    public new void Start()
    {
        base.Start();
        mover = GetComponentInParent<ISpeed>();
    }
    public void Update()
    {
        UpdateWork();
    }
    public override void UpdateWork()
    {
        Vector2 dir = (Vector2)mover.MoveTargetPosition - (Vector2)transform.position;
        TurnBodyToDirection(dir);
    }
}
