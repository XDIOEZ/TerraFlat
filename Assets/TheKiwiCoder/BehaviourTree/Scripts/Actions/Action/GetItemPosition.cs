using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

[NodeMenu("ActionNode/搜查/根据ItemType设定为目标")]
public class GetItemPosition : ActionNode
{
    #region 枚举定义
    public enum MovementBehaviorType
    {
        追击,
        逃离
    }
    #endregion

    #region 序列化字段
    [Header("物品搜索设置")]
    [Tooltip("要搜索的物品类型列表（部分匹配）")]
    public List<string> ItemType = new List<string>();

    [Header("行为设置")]
    public MovementBehaviorType BehaviorType = MovementBehaviorType.追击;

    [Header("逃离行为参数")]
    [Tooltip("逃离距离范围：x=最小，y=最大")]
    public Vector2 fleeDistanceRange = new Vector2(7f, 7f);

    [Tooltip("逃离角度范围（度）")]
    [Range(0f, 180f)]
    public float fleeAngleRange = 45f;

    [Header("黑板设置")]
    public bool setBlackboardTarget = true;
    public bool doNothing = false;

    public Mover mover => context.mover;
    #endregion

    #region 重写方法
    protected override void OnStart() { }

    protected override void OnStop() { }

    protected override State OnUpdate()
    {
        // 查找目标物品
        Item targetItem = context.itemDetector.GetFirstItemByIdNamesFast(itemIds: ItemType);
        if (targetItem == null)
        {
            return State.Failure;
        }

        // 如果设置为不执行任何操作，直接返回成功
        if (doNothing)
            return State.Success;

        // 根据行为类型处理
        ProcessMovementBehavior(targetItem);
        return State.Success;
    }

    public override void OnDrawGizmos()
    {
        base.OnDrawGizmos();
        if (!Application.isPlaying || context == null) return;

        Vector2 currentPosition = context.transform.position;

        if (BehaviorType == MovementBehaviorType.逃离)
        {
            DrawFleeConeGizmo(currentPosition);
        }
    }
    #endregion

    #region 移动逻辑
    private void ProcessMovementBehavior(Item targetItem)
    {
        Vector2 targetPos = targetItem.transform.position;
        switch (BehaviorType)
        {
            case MovementBehaviorType.追击:
                ProcessChaseMovement(targetPos);
                break;

            case MovementBehaviorType.逃离:
                ProcessFleeMovement(targetPos);
                break;
        }
    }

    private void ProcessChaseMovement(Vector2 targetPosition)
    {
        SetTarget(targetPosition);
    }

    private void ProcessFleeMovement(Vector2 targetPosition)
    {
        Vector2 currentPosition = context.transform.position;
        Vector2 awayDir = (currentPosition - targetPosition).normalized;

        // 随机角度
        float angleOffset = Random.Range(-fleeAngleRange * 0.5f, fleeAngleRange * 0.5f);
        Vector2 finalDir = RotateVector2(awayDir, angleOffset);

        // 随机距离
        float fleeDistance = Random.Range(fleeDistanceRange.x, fleeDistanceRange.y);
        Vector2 escapePoint = currentPosition + finalDir * fleeDistance;

        // 经过解锁点处理（避开危险点）
        escapePoint = GetUnlockedTargetPosition(escapePoint);

        SetTarget(escapePoint);
    }

    private void SetTarget(Vector2 pos)
    {
        if (setBlackboardTarget)
            blackboard.TargetPosition = pos;
        mover.TargetPosition = pos;
    }
    #endregion

    #region 辅助逻辑
    private Vector2 RotateVector2(Vector2 vector, float angleDegrees)
    {
        float rad = angleDegrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);
        return new Vector2(
            vector.x * cos - vector.y * sin,
            vector.x * sin + vector.y * cos
        );
    }

    /// <summary>
    /// 避开危险点逻辑（可简单改为寻路检测）
    /// </summary>
    private Vector2 GetUnlockedTargetPosition(Vector2 targetPosition)
    {
        Vector2 currentPos = context.transform.position;

        if (context.mover.IsLock)
        {
            Vector2 dir = (targetPosition - currentPos).normalized;
            float angleOffset = Random.Range(90f, 180f) * (Random.value < 0.5f ? 1 : -1);
            Vector2 rotated = RotateVector2(dir, angleOffset);
            float dist = (targetPosition - currentPos).magnitude;
            return currentPos + rotated * dist;
        }

        // 避开危险点
        if (context.mover.MemoryPath_Forbidden.Count > 0)
        {
            Vector2 avgDangerDir = Vector2.zero;
            foreach (var danger in context.mover.MemoryPath_Forbidden)
                avgDangerDir += (danger - currentPos).normalized;
            avgDangerDir /= context.mover.MemoryPath_Forbidden.Count;

            Vector2 fleeDir = (targetPosition - currentPos).normalized;
            fleeDir = (fleeDir - avgDangerDir).normalized;

            float dist = Random.Range(fleeDistanceRange.x, fleeDistanceRange.y);
            return currentPos + fleeDir * dist;
        }

        return targetPosition;
    }
    #endregion

    #region Gizmos 可视化逻辑
    private void DrawFleeConeGizmo(Vector2 currentPosition)
    {
        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.3f);

        // 从当前位置绘制一个扇形
        float minDist = fleeDistanceRange.x;
        float maxDist = fleeDistanceRange.y;
        float halfAngle = fleeAngleRange * 0.5f;

        Vector2 forward = -context.transform.right; // 假设“逃离”是朝背面方向

        // 绘制边缘线
        Vector2 leftEdge = RotateVector2(forward, -halfAngle);
        Vector2 rightEdge = RotateVector2(forward, halfAngle);

        Gizmos.DrawLine(currentPosition, currentPosition + leftEdge * maxDist);
        Gizmos.DrawLine(currentPosition, currentPosition + rightEdge * maxDist);

        // 绘制扇形弧线
        int segments = 16;
        Vector2 prev = currentPosition + RotateVector2(forward, -halfAngle) * maxDist;
        for (int i = 1; i <= segments; i++)
        {
            float angle = -halfAngle + (fleeAngleRange / segments) * i;
            Vector2 next = currentPosition + RotateVector2(forward, angle) * maxDist;
            Gizmos.DrawLine(prev, next);
            prev = next;
        }

        // 绘制内圈（最小距离）
        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.15f);
        prev = currentPosition + RotateVector2(forward, -halfAngle) * minDist;
        for (int i = 1; i <= segments; i++)
        {
            float angle = -halfAngle + (fleeAngleRange / segments) * i;
            Vector2 next = currentPosition + RotateVector2(forward, angle) * minDist;
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }
    #endregion

    #region 验证
    private void OnValidate()
    {
        fleeDistanceRange.x = Mathf.Max(0f, fleeDistanceRange.x);
        fleeDistanceRange.y = Mathf.Max(fleeDistanceRange.x, fleeDistanceRange.y);
        fleeAngleRange = Mathf.Clamp(fleeAngleRange, 0f, 180f);
    }
    #endregion
}
