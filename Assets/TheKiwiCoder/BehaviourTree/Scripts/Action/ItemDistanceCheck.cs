using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;
using System.Linq;
using UnityEditor;

[NodeMenu("ActionNode/检测/物体与本体的距离")]
public class ItemDistanceCheck : ActionNode
{
    public Vector2 Range;

    private Item targetItem;

    public List<string> itemTypeTags = new List<string>(); // Inspector 中配置

    protected override void OnStart()
    {
        targetItem = null;

        foreach (var item in context.itemDetector.CurrentItemsInArea)
        {
            var itemTags = item.Item_Data.ItemTags.Item_TypeTag;

            // 检查是否有任意一个标签匹配到 itemTypeTags
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
            Debug.LogWarning("未找到指定名称的任何目标物体");
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


        // 最大距离圈
        Handles.DrawWireDisc(position, Vector3.forward, Range.x);

#endif
    }

}
