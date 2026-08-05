using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;
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

        for (int itemIndex = 0; itemIndex < context.itemDetector.CurrentItemsInArea.Count; itemIndex++)
        {
            Item item = context.itemDetector.CurrentItemsInArea[itemIndex];
            if (item == null || item.itemData?.Tags == null)
                continue;

            bool matches = false;
            List<string> itemTags = item.itemData.Tags;
            for (int targetTagIndex = 0; targetTagIndex < itemTypeTags.Count && !matches; targetTagIndex++)
            {
                string targetTag = itemTypeTags[targetTagIndex];
                for (int itemTagIndex = 0; itemTagIndex < itemTags.Count; itemTagIndex++)
                {
                    if (itemTags[itemTagIndex] == targetTag)
                    {
                        matches = true;
                        break;
                    }
                }
            }

            if (!matches)
                continue;

            Vector2 offset = (Vector2)item.transform.position - (Vector2)context.transform.position;
            float distanceSqr = offset.sqrMagnitude;
            float minDistanceSqr = Range.x * Range.x;
            float maxDistanceSqr = Range.y * Range.y;

            if (distanceSqr >= minDistanceSqr && distanceSqr <= maxDistanceSqr)
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
