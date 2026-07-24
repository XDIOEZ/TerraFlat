using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;
[NodeMenu("ActionNode/搜查/更新角色搜查器")]
public class node_ItemDetector : ActionNode
{
    private long _requestVersion;

    protected override void OnStart()
    {
        _requestVersion = context?.itemDetector != null
            ? context.itemDetector.RequestDetectorUpdate()
            : 0;
    }

    protected override void OnStop()
    {
        // 可选：清理操作
    }

    protected override State OnUpdate()
    {
        if (context?.itemDetector == null || _requestVersion == 0)
            return State.Failure;

        return context.itemDetector.IsRequestApplied(_requestVersion)
            ? State.Success
            : State.Running;
    }
}