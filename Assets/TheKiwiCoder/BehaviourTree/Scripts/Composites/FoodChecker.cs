using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

public class FoodChecker : ActionNode
{
    [Tooltip("��ʳ������Ϊ�ƶ�Ŀ��")]
    public bool setMoveTarget = true;
    protected override void OnStart() {
    }

    protected override void OnStop() {
    }

    protected override State OnUpdate() {

        foreach (Item item in context.itemDetector.CurrentItemsInArea)
        {
            foreach (string tag in item.Item_Data.ItemTags.Item_TypeTag)
            {
                if (tag == "Food")
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
