using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

public class RandomPosition : ActionNode
{
    public WorldType worldType;
    public Vector2 min = Vector2.one * -10;
    public Vector2 max = Vector2.one * 10;

    public enum WorldType 
    {
       DDD,
       DD
    }

    protected override void OnStart() 
    {
    }

    protected override void OnStop() {
    }

    protected override State OnUpdate() 
    {
        if (worldType == WorldType.DDD)
        {
            blackboard.moveToPosition.x = Random.Range(min.x, max.x);
            blackboard.moveToPosition.z = Random.Range(min.y, max.y);
        }
        else if (worldType == WorldType.DD)
        {
           
            blackboard.moveToPosition.x = Random.Range(min.x, max.x);
            blackboard.moveToPosition.y = Random.Range(min.y, max.y);
            context.gameObject.GetComponent<IMover>().TargetPosition = context.gameObject.GetComponent<IMover>().Position + blackboard.moveToPosition;
        }
    
        return State.Success;

    }
}
