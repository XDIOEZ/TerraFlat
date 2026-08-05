using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

[NodeMenu("ActionNode/行动/奔跑")]
public class RunAway : ActionNode
{
    public bool isRunning = false;
    protected override void OnInit()
    {
        context.OnTreeStop += () => context.mover.SetRunState(false);
    }
    protected override void OnStart()
    {

    }

    protected override void OnStop()
    {
    }

    protected override State OnUpdate()
    {
        context.mover.SetRunState(isRunning);
        return State.Success;
    }
}
