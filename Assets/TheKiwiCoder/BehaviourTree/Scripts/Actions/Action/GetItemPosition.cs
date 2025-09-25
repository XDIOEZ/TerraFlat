using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

[NodeMenu("ActionNode/搜查/根据ItemType设定为目标")]
public class GetItemPosition : ActionNode
{
    #region 枚举定义
    public enum MovementBehaviorType
    {
        追击,  // 追击
        逃离    // 逃离
    }
    #endregion

    #region 序列化字段
    [Header("物品搜索设置")]
    [Tooltip("要搜索的物品类型列表（部分匹配）")]
    public List<string> ItemType = new List<string>();

    [Header("行为设置")]
    [Tooltip("选择行为类型：追击或逃离")]
    public MovementBehaviorType BehaviorType = MovementBehaviorType.追击;

    [Header("逃离行为参数")]
    [Tooltip("逃跑的最小距离")]
    [Range(1f, 10f)]
    public float minRunDistance = 3.0f;

    [Tooltip("逃跑的最大距离")]
    [Range(1f, 20f)]
    public float maxRunDistance = 7.0f;

    [Tooltip("逃跑方向的角度波动范围（度）")]
    [Range(0f, 180f)]
    public float angleVariance = 30f;

    [Header("黑板设置")]
    [Tooltip("是否设置黑板目标对象")]
    public bool setBlackboardTarget = true;

    [Tooltip("找到目标后不执行任何移动操作")]
    public bool doNothing = false;

    public Mover mover =>context.mover;
    #endregion

    #region 重写方法
    protected override void OnStart()
    {
    }

    protected override void OnStop()
    {
        // 节点停止时的清理逻辑
    }

    public override void OnDrawGizmos()
    {
        base.OnDrawGizmos();

        // 在Scene视图中绘制调试信息
        if (Application.isPlaying && context != null)
        {
            DrawDebugInfo();
        }
    }

    protected override State OnUpdate()
    {
        // 查找目标物品
        Item targetItem = FindTargetItem();
        if (targetItem == null)
        {
            return State.Failure;
        }

        // 如果设置为不执行任何操作，直接返回成功
        if (doNothing)
        {
            return State.Success;
        }

        // 根据行为类型处理移动
        ProcessMovementBehavior(targetItem);

        return State.Success;
    }
    #endregion

    #region 私有方法


    /// <summary>
    /// 查找符合条件的目标物品
    /// </summary>
    private Item FindTargetItem()
    {
        foreach (Item item in context.itemDetector.CurrentItemsInArea)
        {
            if (item?.itemData?.ItemTags?.Item_TypeTag == null)
                continue;

            // 检查物品类型是否匹配
            if (ItemType.Exists(type => item.itemData.ItemTags.Item_TypeTag.Contains(type)))
            {
                return item;
            }
        }

        return null;
    }

    /// <summary>
    /// 根据行为类型处理移动逻辑
    /// </summary>
    private void ProcessMovementBehavior(Item targetItem)
    {
        Vector2 targetPosition = targetItem.transform.position;

        switch (BehaviorType)
        {
            case MovementBehaviorType.追击:
                ProcessChaseMovement(targetPosition);
                break;

            case MovementBehaviorType.逃离:
                ProcessFleeMovement(targetPosition);
                break;

            default:
                Debug.LogError($"[{GetType().Name}] 未知的行为类型: {BehaviorType}");
                break;
        }
    }

    /// <summary>
    /// 处理追击移动
    /// </summary>
    private void ProcessChaseMovement(Vector2 targetPosition)
    {
        
        blackboard.TargetPosition = targetPosition;
        mover.TargetPosition = targetPosition;
        context.mover.TargetPosition = targetPosition;
        //  Debug.Log($"[{GetType().Name}] 设置追击目标位置: {targetPosition}");
    }

    /// <summary>
    /// 处理逃离移动
    /// </summary>
    private void ProcessFleeMovement(Vector2 targetPosition)
    {
        Vector2 currentPosition = context.transform.position;
        Vector2 directionAway = (currentPosition - targetPosition).normalized;

        // 添加随机角度偏移
        float angleOffset = Random.Range(-angleVariance, angleVariance);
        Vector2 rotatedDirection = RotateVector2(directionAway, angleOffset);

        // 计算逃离点
        float runDistance = Random.Range(minRunDistance, maxRunDistance);
        Vector2 escapePoint = currentPosition + rotatedDirection * runDistance;

        escapePoint = GetUnlockedTargetPosition(escapePoint);

        //TODO MemoryPath_Forbidden是一个存储了禁止移动点位 的列表
        //TODO 在创建escapePoint处理位置时 根据禁止移动点位添加偏移 避免走向禁止点位
        //TODO 通过向escapePoint添加向量 实现方向的偏移
        blackboard.TargetPosition = escapePoint;
        mover.TargetPosition = escapePoint;
       // Debug.Log($"[{GetType().Name}] 设置逃离目标位置: {escapePoint}");
    }

    public Vector2 GetUnlockedTargetPosition(Vector2 targetPosition)
    {
        Vector2 currentPosition = context.transform.position;

        // 情况 1：当前方向被锁定，强制偏转
        if (context.mover.IsLock)
        {
            Vector2 originalDir = (targetPosition - currentPosition).normalized;

            // ±90~180度偏转
            float angleOffset = Random.Range(90f, 180f);
            angleOffset = Random.value < 0.5f ? angleOffset : -angleOffset;

            Vector2 newDir = RotateVector2(originalDir, angleOffset);
            float runDistance = (targetPosition - currentPosition).magnitude;

            return currentPosition + newDir * runDistance;
        }

        // 情况 2：正常逃离，但避开危险区域（朝安全方向逃）
        Vector2 escapeDir = (targetPosition - currentPosition).normalized;

        Vector2 dangerDir = Vector2.zero;
        foreach (var danger in context.mover.MemoryPath_Forbidden)
        {
            dangerDir += (danger - currentPosition).normalized;
        }

        if (context.mover.MemoryPath_Forbidden.Count > 0)
        {
            dangerDir /= context.mover.MemoryPath_Forbidden.Count;
            context.mover.MemoryPath_Forbidden.RemoveAt(0); // 淘汰最早的
            // 调整逃离方向：避开危险方向
            escapeDir = (escapeDir - dangerDir).normalized;
        }

        float finalDistance = (targetPosition - currentPosition).magnitude;

        // 可选：添加一定角度扰动
        float angleNoise = Random.Range(-angleVariance, angleVariance);
        escapeDir = RotateVector2(escapeDir, angleNoise);

        return currentPosition + escapeDir * finalDistance;
    }




    /// <summary>
    /// 旋转2D向量
    /// </summary>
    private Vector2 RotateVector2(Vector2 vector, float angleDegrees)
    {
        float angleRadians = angleDegrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(angleRadians);
        float sin = Mathf.Sin(angleRadians);

        return new Vector2(
            vector.x * cos - vector.y * sin,
            vector.x * sin + vector.y * cos
        );
    }

    /// <summary>
    /// 绘制调试信息
    /// </summary>
    private void DrawDebugInfo()
    {
        if (context?.itemDetector?.CurrentItemsInArea == null)
            return;

        // 绘制检测到的物品位置
        Gizmos.color = Color.yellow;
        foreach (Item item in context.itemDetector.CurrentItemsInArea)
        {
            if (item != null)
            {
                Gizmos.DrawWireSphere(item.transform.position, 0.5f);
            }
        }
    }
    #endregion

    #region 验证方法
    private void OnValidate()
    {
        // 确保距离参数的合理性
        if (minRunDistance > maxRunDistance)
        {
            maxRunDistance = minRunDistance;
        }

        // 确保角度范围合理
        angleVariance = Mathf.Clamp(angleVariance, 0f, 180f);
    }
    #endregion
}