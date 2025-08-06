using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;
using System.Linq;
using UnityEditor;

[NodeMenu("ActionNode/检测/物体与本体的距离")]
public class ItemDistanceCheck : ActionNode
{
    public Vector2 Range;
    public List<string> itemTypeTags = new List<string>();
    [Tooltip("本地检测点位置")]
    public Transform  localPoint;

    protected override void OnStart()
    {
        // 不再在这里缓存 targetItem
        if (localPoint == null)
        localPoint = context.gameObject.GetComponentInChildren<ITriggerAttack>().Weapon_GameObject.transform;
    }

    protected override void OnStop()
    {
    }

    protected override State OnUpdate()
    {
        if (context?.itemDetector?.CurrentItemsInArea == null || itemTypeTags == null || itemTypeTags.Count == 0)
        {
            Debug.LogWarning("检测条件异常，可能是未设置标签或检测器为空");
            return State.Failure;
        }

        // 优化匹配性能
        HashSet<string> tagSet = new HashSet<string>(itemTypeTags);

        foreach (var item in context.itemDetector.CurrentItemsInArea)
        {
            var itemTags = item.itemData.ItemTags.Item_TypeTag;

            bool matches = itemTags.Any(tag => tagSet.Contains(tag.ToString()));
            if (!matches)
                continue;

            float distance = Vector2.Distance(
                new Vector2(context.transform.position.x, context.transform.position.y),
                new Vector2(item.transform.position.x, item.transform.position.y)
            );

            if (distance >= Range.x && distance <= Range.y)
            {
                return State.Success;
            }
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

        // 最小距离圈
        Handles.DrawWireDisc(position, Vector3.forward, Range.x);
        // 最大距离圈
        Handles.DrawWireDisc(position, Vector3.forward, Range.y);
#endif
    }
}
