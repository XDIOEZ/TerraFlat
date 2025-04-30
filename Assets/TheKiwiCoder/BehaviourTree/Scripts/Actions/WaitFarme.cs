using System.Collections;
using UnityEngine;
using TheKiwiCoder;
[NodeMenu("ActionNode/通用节点/等待帧数")]
public class WaitFrame : ActionNode
{
    public int frameCount = 1; // 要等待的帧数

    public int counter;

    protected override void OnStart()
    {
        counter = 0;
    }

    protected override void OnStop()
    {
    }

    protected override State OnUpdate()
    {
        counter++;
        if (counter >= frameCount)
        {
            return State.Success;
        }
        return State.Running;
    }
}
