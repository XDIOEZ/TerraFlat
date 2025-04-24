using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

public class TypeSearcher : ActionNode
{

    [Tooltip("将对象设置为移动目标")]
    public bool setMoveTarget = true;
    [Tooltip("查找对象类型")]
    public string searchType;
    protected override void OnStart() {
    }

    protected override void OnStop() {
    }

    protected override State OnUpdate() 
    {
        foreach (Item item in context.itemDetector.CurrentItemsInArea)
        {
            foreach (string tag in item.Item_Data.ItemTags.Item_TypeTag)
            {
                if (tag == searchType)
                {
                    if (setMoveTarget)
                    {
                        context.mover.TargetPosition = item.transform.position;
                    }
                    return State.Success;
                }
            }

        }
        return State.Failure;
    }
}
