using System.Collections;
using UnityEngine;
using TheKiwiCoder;
[NodeMenu("ActionNode/ͨ�ýڵ�/�ȴ�֡��")]
public class WaitFrame : ActionNode
{
    public int frameCount = 1; // Ҫ�ȴ���֡��

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
