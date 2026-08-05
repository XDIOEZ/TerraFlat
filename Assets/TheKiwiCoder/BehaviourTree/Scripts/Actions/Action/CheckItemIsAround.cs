using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

public class CheckItemIsAround : ActionNode
{
    private Mod_ItemDetector itemDetector;

    public string itemName;

    public bool DebugMode { get; set; }

    protected override void OnStart()
    {
        if(itemDetector == null)
        {
            itemDetector = context.gameObject.GetComponent<Mod_ItemDetector>();
           // Debug.LogWarning("未指定物品检测器，将使用默认的物品检测器");
        }
    }

    protected override void OnStop()
    {
        if (DebugMode)
        {
            Debug.Log($"<color=orange>CheckItemIsAround 检测已停止</color>");
        }
    }

    protected override State OnUpdate()
    {
        if (DebugMode)
        {
            Debug.Log($"<color=green>正在检测周围物品...</color>");
        }

        if (DebugMode)
        {
            Debug.Log($"<color=gray>未检测到符合条件的物品，继续检测...</color>");
        }

        return State.Failure;
    }
}