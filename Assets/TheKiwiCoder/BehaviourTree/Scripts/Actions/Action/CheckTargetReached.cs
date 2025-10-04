using UnityEngine;
using TheKiwiCoder;

[NodeMenu("ActionNode/条件/检查是否到达目标点")]
public class CheckTargetReached : ActionNode
{
    [Header("距离阈值")]
    [Tooltip("判断到达目标点的距离阈值")]
    public float distanceThreshold = 0.1f;

    protected override void OnStart()
    {
        // 不需要初始化操作
        context.mover.stopDistance = distanceThreshold;
    }

    protected override void OnStop()
    {
        // 不需要清理操作
    }

    protected override State OnUpdate()
    {
        // 检查必要的组件是否存在
        if (context.mover == null)
        {
            return State.Failure;
        }

        // 获取当前位置和目标位置
        Vector2 currentPosition = context.mover.transform.position;
        Vector2 targetPosition = context.mover.TargetPosition;

        // 计算距离
        float distanceToTarget = Vector2.Distance(currentPosition, targetPosition);

        // 检查是否在阈值范围内
        if (distanceToTarget <= distanceThreshold)
        {
            // 已经到达目标点
            return State.Success;
        }
        else
        {
            // 还未到达目标点
            return State.Failure;
        }
    }
}