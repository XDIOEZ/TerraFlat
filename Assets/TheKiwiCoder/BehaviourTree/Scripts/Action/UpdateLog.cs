using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

public class UpDateDebug : ActionNode
{
    public float updateTime = 2f;
    float startTime;
    public string debugMessage;

    public bool completeReturn_Success = true;
    protected override void OnStart() {
        startTime = Time.time;
    }

    protected override void OnStop() {
    }

    protected override State OnUpdate() 
    {
        if (Time.time - startTime > updateTime)
        {
            if (completeReturn_Success)
            {
                return State.Success;
            }
            else
            {
                return State.Failure;
            }
        }

        Debug.Log(debugMessage);

        return State.Running;
    }
}
