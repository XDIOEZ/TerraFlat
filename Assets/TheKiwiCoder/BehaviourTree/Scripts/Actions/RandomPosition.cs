using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

/// <summary>
/// 随机生成目标位置，并考虑同类吸引力
/// </summary>
public class RandomPosition : ActionNode
{
    #region 字段

    public Vector2 min = Vector2.one * -10;
    public Vector2 max = Vector2.one * 10;
    public bool TrueRandom;

    [Header("同类吸引参数")]
    public float sameTypeAttraction = 3f;

    private Vector3 lastValidPosition = Vector3.zero;
    private const int maxTries = 5;

    // 重用列表减少GC分配
    private readonly List<Vector2> sameTypePositions = new List<Vector2>(8);

    #endregion

    #region 生命周期

    protected override void OnStart()
    {
        lastValidPosition = context.transform.position;
    }

    protected override void OnStop()
    {
        // 无需操作
    }

    #endregion

    #region 行为逻辑

    protected override State OnUpdate()
    {
        Vector2 sameTypeDirection = GetSameTypeAverageDirection();

        if (!TrueRandom && context.map != null)
        {
            return HandleMapConstrainedMovement(sameTypeDirection);
        }
        else
        {
            return HandleFreeMovement(sameTypeDirection);
        }
    }

    #endregion

    #region 私有方法

    private State HandleMapConstrainedMovement(Vector2 sameTypeDirection)
    {
        Vector3 chosenPosition = Vector3.zero;
        bool found = false;

        for (int i = 0; i < maxTries; i++)
        {
            Vector2 randomOffset = new Vector2(
                Random.Range(min.x, max.x),
                Random.Range(min.y, max.y)
            );

            // 添加反向偏移
            if (lastValidPosition != context.transform.position)
            {
                Vector2 direction = ((Vector2)context.transform.position - (Vector2)lastValidPosition).normalized;
                randomOffset += direction * 0.5f;
            }

            // 添加同类吸引
            if (sameTypeDirection.sqrMagnitude > 0.001f)
            {
                randomOffset += sameTypeDirection * sameTypeAttraction;
            }

            // 检查地图可用性
            Vector3 testPosition = context.transform.position + (Vector3)randomOffset;
            Vector2Int testPosInt = Vector2Int.FloorToInt(testPosition);
            if (context.map.GetTileArea(testPosInt) <= 2)
            {
                chosenPosition = testPosition;
                found = true;
                break;
            }
        }

        // 如果没有找到合适位置，使用最后一次随机偏移
        if (!found)
        {
            Vector2 randomOffset = new Vector2(
                Random.Range(min.x, max.x),
                Random.Range(min.y, max.y)
            );
            if (sameTypeDirection.sqrMagnitude > 0.001f)
                randomOffset += sameTypeDirection * sameTypeAttraction;

            chosenPosition = context.transform.position + (Vector3)randomOffset;
        }

        SetTargetPosition(chosenPosition);
        return State.Success;
    }

    private State HandleFreeMovement(Vector2 sameTypeDirection)
    {
        Vector2 randomOffset = new Vector2(
            Random.Range(min.x, max.x),
            Random.Range(min.y, max.y)
        );

        if (sameTypeDirection.sqrMagnitude > 0.001f)
            randomOffset += sameTypeDirection * sameTypeAttraction;

        SetTargetPosition(context.transform.position + (Vector3)randomOffset, randomOffset);
        return State.Success;
    }

    private void SetTargetPosition(Vector3 worldPosition, Vector2? relativeOffset = null)
    {
        context.mover.TargetPosition = worldPosition;
        blackboard.TargetPosition = relativeOffset ?? (worldPosition - context.transform.position);
        lastValidPosition = context.transform.position;
    }

    /// <summary>
    /// 计算同类平均方向
    /// </summary>
    private Vector2 GetSameTypeAverageDirection()
    {
        sameTypePositions.Clear();

        if (context.itemDetector == null || context.item == null ||
            context.item.itemData == null || context.itemDetector.CurrentItemsInArea == null)
        {
            return Vector2.zero;
        }

        string selfId = context.item.itemData.IDName;

        foreach (var collider in context.itemDetector.CurrentItemsInArea)
        {
            if (collider == null || collider.transform == context.transform || collider.itemData == null)
                continue;

            if (collider.itemData.IDName == selfId)
                sameTypePositions.Add(collider.transform.position);
        }

        if (sameTypePositions.Count == 0) return Vector2.zero;

        Vector2 averagePosition = Vector2.zero;
        for (int i = 0; i < sameTypePositions.Count; i++)
        {
            averagePosition += sameTypePositions[i];
        }
        averagePosition /= sameTypePositions.Count;

        Vector2 direction = averagePosition - (Vector2)context.transform.position;
        return direction.sqrMagnitude > 0.001f ? direction.normalized : Vector2.zero;
    }

    #endregion

    #region Gizmos

    private void OnDrawGizmosSelected()
    {
        if (Application.isPlaying && context != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(context.transform.position, 5f); // 可替换为实际检测范围
        }
    }

    #endregion
}
