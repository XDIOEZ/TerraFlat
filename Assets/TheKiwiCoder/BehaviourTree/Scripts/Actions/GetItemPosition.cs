using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

public class GetItemPosition : ActionNode
{
    public enum MovementBehaviorType
    {
        Chase,
        Flee
    }

    [Tooltip("要搜索的物品类型列表（部分匹配）")]
    public List<string> ItemType;

    [Tooltip("选择行为类型：追击或逃离")]
    public MovementBehaviorType BehaviorType;

    [Tooltip("逃跑的最小距离")]
    public float minRunDistance = 3.0f;

    [Tooltip("逃跑的最大距离")]
    public float maxRunDistance = 7.0f;

    [Tooltip("逃跑方向的角度波动范围")]
    public float angleVariance = 30f;

    [Tooltip("设置为黑板目标对象")]
    public bool setBlackboardTarget = true;



    [Tooltip("什么也不做")]
    public bool doNothing = false;


    protected override void OnStart()
    {
    }

    protected override void OnStop()
    {
    }

    public override void OnDrawGizmos()
    {
        base.OnDrawGizmos();
        Debug.Log("GetItemPosition OnDrawGizmos");
    }
    protected override State OnUpdate()
    {
        
        if (context.itemDetector == null || context.itemDetector.CurrentItemsInArea == null)
            return State.Failure;

        Item targetItem = null;
        foreach (Item item in context.itemDetector.CurrentItemsInArea)
        {
            if (item != null && ItemType.Exists(type => item.Item_Data.ItemTags.Item_TypeTag.Contains(type)))
            {
                targetItem = item;
                if (setBlackboardTarget)
                {
                    //设置黑板中的目标物品
                    blackboard.target = targetItem.transform;
                }
                break;
            }
        }

        if (targetItem == null)
        {
            if (setBlackboardTarget)
            {
                blackboard.target = null;
            }
            return State.Failure;
        }
            
        if (doNothing)
        {
            return State.Success;
        }

        // 将物品位置转换为Vector2（仅使用X和Y）
        Vector2 targetPosition = targetItem.transform.position;

        switch (BehaviorType)
        {
            case MovementBehaviorType.Chase:
                // 直接将物品位置设为目标点
                blackboard.TargetPosition = targetPosition;
                break;

            case MovementBehaviorType.Flee:
                // 获取当前角色的Vector2位置
                Vector2 currentPosition = context.transform.position;
                // 计算逃离方向（从物品指向角色的反方向）
                Vector2 directionAway = (currentPosition - targetPosition).normalized;

                // 添加随机角度偏移（绕Z轴旋转）
                float angleOffset = Random.Range(-angleVariance, angleVariance);
                Quaternion rotation = Quaternion.Euler(0, 0, angleOffset);
                Vector2 rotatedDirection = (Vector2)(rotation * (Vector3)directionAway);

                // 计算逃离点
                float runDistance = Random.Range(minRunDistance, maxRunDistance);
                Vector2 escapePoint = currentPosition + rotatedDirection * runDistance;

                // 设置移动目标为逃离点
                blackboard.TargetPosition = escapePoint;
                break;

        }

        return State.Success;
    }
}