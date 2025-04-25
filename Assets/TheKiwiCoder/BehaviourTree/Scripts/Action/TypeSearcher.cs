using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

public class TypeSearcher : ActionNode
{

    [Tooltip("����������Ϊ�ƶ�Ŀ��")]
    public bool setMoveTarget = true;
    [Tooltip("���Ҷ�������")]
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
