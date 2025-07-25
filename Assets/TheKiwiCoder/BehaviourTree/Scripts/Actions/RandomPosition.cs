using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

public class RandomPosition : ActionNode
{

    public Vector2 min = Vector2.one * -10;
    public Vector2 max = Vector2.one * 10;
    Mover speed;

    protected override void OnStart() 
    {
        speed = context.item.Mods[ModText.Mover] as Mover;
    }

    protected override void OnStop() {
    }

    protected override State OnUpdate() 
    {
            blackboard.TargetPosition.x = Random.Range(min.x, max.x);
            blackboard.TargetPosition.y = Random.Range(min.y, max.y);


        speed.TargetPosition = context.transform.position + blackboard.TargetPosition;

    
        return State.Success;

    }
}
