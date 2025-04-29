using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;
using System.Linq;
using UnityEditor;

[NodeMenu("ActionNode/���/�����뱾��ľ���")]
public class ItemDistanceCheck : ActionNode
{
    public Vector2 Range;

    private Item targetItem;

    public List<string> itemTypeTags = new List<string>(); // Inspector ������

    protected override void OnStart()
    {
        targetItem = null;

        foreach (var item in context.itemDetector.CurrentItemsInArea)
        {
            var itemTags = item.Item_Data.ItemTags.Item_TypeTag;

            // ����Ƿ�������һ����ǩƥ�䵽 itemTypeTags
            if (itemTags.Any(tag => itemTypeTags.Contains(tag.ToString())))
            {
                targetItem = item;
                break;
            }
        }
    }


    protected override void OnStop()
    {
    }

    protected override State OnUpdate()
    {
        if (targetItem == null)
        {
            Debug.LogWarning("δ�ҵ�ָ�����Ƶ��κ�Ŀ������");
            return State.Failure;
        }

        float distance = Vector2.Distance(context.transform.position, targetItem.transform.position);

        if (distance >= Range.x && distance <= Range.y)
        {
            return State.Success;
        }

        return State.Failure;
    }
    public override void OnDrawGizmos()
    {
        base.OnDrawGizmos();

#if UNITY_EDITOR
        if (context == null || context.transform == null)
            return;

        Vector3 position = context.transform.position;
        Handles.color = Color.yellow;

        Handles.DrawWireDisc(position, Vector3.forward, Range.x);


        // ������Ȧ
        Handles.DrawWireDisc(position, Vector3.forward, Range.x);

#endif
    }

}
