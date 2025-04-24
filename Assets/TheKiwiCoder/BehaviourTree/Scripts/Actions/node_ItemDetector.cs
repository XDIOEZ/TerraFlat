using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

public class node_ItemDetector : ActionNode
{
    protected override void OnStart()
    {
        if (context.itemDetector == null)
            context.itemDetector = context.gameObject.GetComponent<IDetector>();
    }

    protected override void OnStop()
    {
        // 可选：清理操作
    }

    protected override State OnUpdate()
    {
        // 直接执行检测，无需间隔控制
        context.itemDetector.Update_Detector();
        return State.Success; // 或根据需求返回 Running/Success
    }
}