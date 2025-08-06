using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

public class CheckItemIsAround : ActionNode, IDebug
{
    private IDetector itemDetector;

    public string itemName;

    public bool DebugMode { get; set; }

    protected override void OnStart()
    {
        if(itemDetector == null)
        {
            itemDetector = context.gameObject.GetComponent<IDetector>();
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

        foreach (var item in itemDetector.CurrentItemsInArea)
        {
            if ( item.itemData.ItemTags.Item_TypeTag.Contains(itemName))
            {
                if (DebugMode)
                {
                    Debug.Log($"<color=lime>检测到符合条件的物品：{item.name}（ID：{item.GetInstanceID()}）</color>");
                }
                return State.Success;
            }
        }

        if (DebugMode)
        {
            Debug.Log($"<color=gray>未检测到符合条件的物品，继续检测...</color>");
        }

        return State.Failure;
    }
}