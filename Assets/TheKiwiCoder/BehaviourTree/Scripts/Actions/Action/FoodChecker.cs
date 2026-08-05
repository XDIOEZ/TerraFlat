using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

public class FoodChecker : ActionNode
{
    [Tooltip("将食物设置为移动目标")]
    public bool setMoveTarget = true;
    protected override void OnStart() {
    }

    protected override void OnStop() {
    }

    protected override State OnUpdate() {

        context.itemDetector.Type_Tag_Item_Dict.TryGetValue("Food", out List<Item> items);
       
            foreach (var item in items)
            {
                    if (setMoveTarget)
                    {
                        context.mover.TargetPosition = item.transform.position;
                    }
                    return State.Success;
            }
        return State.Failure;
    }
}
