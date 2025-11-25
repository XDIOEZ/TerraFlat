using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

/// <summary>
/// 随机生成目标位置，并考虑同类吸引力
/// </summary>
public class RandomPosition : ActionNode
{
    #region 字段

    [Header("游荡范围半径")]
    public float wanderRadius = 10f;
    
    public bool TrueRandom;

    [Header("同类吸引参数")]
    public float sameTypeAttraction = 3f;

    private Vector3 lastValidPosition = Vector3.zero;
    private const int maxTries = 5;

    // 重用列表减少GC分配
    private readonly List<Vector2> sameTypePositions = new List<Vector2>(8);
    
    // Gizmos显示相关
    private Vector3 gizmoPosition = Vector3.zero;

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

        if (!TrueRandom && context.tileEffectReceiver != null && context.tileEffectReceiver.Cache_map != null)
        {
            return HandleMapConstrainedMovement(sameTypeDirection);
        }
        else
        {
            // 无地图约束，直接随机
            return HandleFreeMovement(sameTypeDirection);
        }
    }

    #endregion

    #region 私有方法

private State HandleMapConstrainedMovement(Vector2 sameTypeDirection)
{
    Vector3 chosenPosition = Vector3.zero;
    bool found = false;
    int attempts = 0;
    const int maxAttempts = 5; // 增加尝试次数

    Vector3 originalPosition = context.transform.position;

    while (attempts < maxAttempts)
    {
        // 在圆形范围内生成随机偏移
        Vector2 randomOffset = Random.insideUnitCircle * wanderRadius;

        // 添加反向偏移
        if (lastValidPosition != originalPosition)
        {
            Vector2 direction = ((Vector2)originalPosition - (Vector2)lastValidPosition).normalized;
            randomOffset += direction * 0.5f;
        }

        // 添加同类吸引
        if (sameTypeDirection.sqrMagnitude > 0.001f)
        {
            randomOffset += sameTypeDirection * sameTypeAttraction;
        }

        // 生成测试位置A
        Vector3 testPositionA = originalPosition + (Vector3)randomOffset;
        Vector2Int testPosIntA = Vector2Int.FloorToInt(testPositionA);

        // 检查位置A是否危险
        if (IsDangerousTile(testPosIntA))
        {
            // 位置A危险，计算对称位置B
            Vector3 positionB = originalPosition - (Vector3)randomOffset;
            Vector2Int posIntB = Vector2Int.FloorToInt(positionB);

            // 检查位置B是否安全
            if (!IsDangerousTile(posIntB))
            {
                chosenPosition = positionB;
                found = true;
                gizmoPosition = chosenPosition;
                break;
            }
            else
            {
                // 位置B也危险，计算位置D (B-C向量*2)
                Vector3 vectorBC = positionB - originalPosition;
                Vector3 positionD = positionB + vectorBC;
                Vector2Int posIntD = Vector2Int.FloorToInt(positionD);

                // 检查位置D是否安全
                if (!IsDangerousTile(posIntD))
                {
                    chosenPosition = positionD;
                    found = true;
                    gizmoPosition = chosenPosition;
                    break;
                }
                else
                {
                    // 位置D也危险，在圆内随机落点三次
                    float radius = vectorBC.magnitude;
                    bool circlePointFound = false;
                    
                    // 尝试三次圆内随机点
                    for (int i = 0; i < 3; i++)
                    {
                        // 在以B为圆心，半径为radius的圆内随机生成点
                        Vector2 randomInCircle = Random.insideUnitCircle * radius;
                        Vector3 positionEFG = positionB + (Vector3)randomInCircle;
                        Vector2Int posIntEFG = Vector2Int.FloorToInt(positionEFG);

                        if (!IsDangerousTile(posIntEFG))
                        {
                            chosenPosition = positionEFG;
                            found = true;
                            circlePointFound = true;
                            gizmoPosition = chosenPosition;
                            break;
                        }
                    }

                    if (circlePointFound)
                    {
                        break;
                    }
                }
            }
        }
        else
        {
            // 位置A安全
            chosenPosition = testPositionA;
            found = true;
            gizmoPosition = chosenPosition;
            break;
        }

        attempts++;
    }

    // 如果前面所有尝试都失败了，使用一个强制的随机点（不管是否危险）
    if (!found)
    {
        // 生成更大的随机偏移
        Vector2 finalOffset = Random.insideUnitCircle * wanderRadius * 2;
        
        if (sameTypeDirection.sqrMagnitude > 0.001f)
            finalOffset += sameTypeDirection * sameTypeAttraction;

        chosenPosition = originalPosition + (Vector3)finalOffset;
        gizmoPosition = chosenPosition;
        Debug.Log("使用强制的随机位置，可能危险");
    }

    SetTargetPosition(chosenPosition);
    return State.Success;
}

    // 伪代码方法：判断是否为危险tile
    private bool IsDangerousTile(Vector2Int tilePosition)
    {
        // 获取指定位置的TileData
        TileData tileData = context.tileEffectReceiver.Cache_map?.GetTile(tilePosition);
        if (tileData == null)
            return true; // 空位置视为危险
            
        // 检查惩罚值是否大于等于5000
        return tileData.Penalty >= 5000;
    }

    private State HandleFreeMovement(Vector2 sameTypeDirection)
    {
        // 在圆形范围内生成随机偏移
        Vector2 randomOffset = Random.insideUnitCircle * wanderRadius;

        if (sameTypeDirection.sqrMagnitude > 0.001f)
            randomOffset += sameTypeDirection * sameTypeAttraction;

        Vector3 targetPosition = context.transform.position + (Vector3)randomOffset;
        // 设置Gizmos显示位置
        gizmoPosition = targetPosition;
        
        SetTargetPosition(targetPosition, randomOffset);
        return State.Success;
    }

    private void SetTargetPosition(Vector3 worldPosition, Vector2? relativeOffset = null)
    {
        // 添加null检查以避免空引用异常
        if (context != null && context.mover != null)
        {
            context.mover.TargetPosition = worldPosition;
        }
        else
        {
            // 添加Debug日志
            if (context == null)
            {
                Debug.LogWarning("RandomPosition: context is null, cannot set target position!");
            }
            else if (context.mover == null)
            {
                Debug.LogWarning($"RandomPosition: context.mover is null for {context.gameObject.name}, cannot set target position!");
            }
        }
        
        if (blackboard != null)
        {
            blackboard.TargetPosition = relativeOffset ?? (worldPosition - context.transform.position);
        }
        else
        {
            Debug.LogWarning($"RandomPosition: blackboard is null for {context?.gameObject.name ?? "unknown object"}, cannot set target position offset!");
        }
        
        if (context != null)
        {
            lastValidPosition = context.transform.position;
        }
        else
        {
            Debug.LogWarning("RandomPosition: context is null, cannot update lastValidPosition!");
        }
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
    
    public override void OnDrawGizmos()
    {
        // 保存原始Gizmos颜色
        Color originalColor = Gizmos.color;
        
        // 绘制检测范围（仅在运行时）
        if (Application.isPlaying && context != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(context.transform.position, 5f);
        }
        
        // 绘制随机游荡范围
        DrawWanderRange();
        
        // 绘制目标位置标记
        DrawTargetPositionMarker();
        
        // 恢复原始颜色
        Gizmos.color = originalColor;
    }
    
    /// <summary>
    /// 绘制随机游荡范围
    /// </summary>
    private void DrawWanderRange()
    {
        if (context == null) return;
        
        // 保存原始Gizmos颜色
        Color originalColor = Gizmos.color;
        
        // 设置Gizmos颜色为淡绿色半透明
        Gizmos.color = new Color(0.0f, 1.0f, 0.0f, 0.3f);
        
        Vector3 centerPosition = context != null ? context.transform.position : Vector3.zero;
        
        // 绘制圆形范围（实心）
        DrawSolidDisc(centerPosition, wanderRadius);
        
        // 绘制边框
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(centerPosition, wanderRadius);
        
        // 恢复原始颜色
        Gizmos.color = originalColor;
    }
    
    /// <summary>
    /// 绘制目标位置标记
    /// </summary>
    private void DrawTargetPositionMarker()
    {
        // 保存原始Gizmos颜色
        Color originalColor = Gizmos.color;
        
        // 设置Gizmos颜色
        Gizmos.color = Color.yellow;
        
        // 绘制一个圆形标记
        Gizmos.DrawWireSphere(gizmoPosition, 0.5f);
        
        // 绘制一个X标记
        float size = 0.3f;
        Gizmos.DrawLine(gizmoPosition + new Vector3(size, size, 0), gizmoPosition + new Vector3(-size, -size, 0));
        Gizmos.DrawLine(gizmoPosition + new Vector3(-size, size, 0), gizmoPosition + new Vector3(size, -size, 0));
        
        // 绘制一个点（通过绘制小球实现）
        Gizmos.color = Color.red;
        Gizmos.DrawSphere(gizmoPosition, 0.1f);
        
        // 恢复原始颜色
        Gizmos.color = originalColor;
    }
    
    /// <summary>
    /// 绘制实心圆盘
    /// </summary>
    private void DrawSolidDisc(Vector3 center, float radius)
    {
        // 通过绘制多个线段来模拟实心圆盘效果
        int segments = 32;
        float angleStep = (2f * Mathf.PI) / segments;
        
        for (int i = 0; i < segments; i++)
        {
            float angle1 = i * angleStep;
            float angle2 = (i + 1) * angleStep;
            
            Vector3 point1 = center + new Vector3(Mathf.Cos(angle1) * radius, Mathf.Sin(angle1) * radius, 0);
            Vector3 point2 = center + new Vector3(Mathf.Cos(angle2) * radius, Mathf.Sin(angle2) * radius, 0);
            
            // 绘制从圆心到边缘的线段
            Gizmos.DrawLine(center, point1);
            Gizmos.DrawLine(center, point2);
            
            // 绘制边缘线段
            Gizmos.DrawLine(point1, point2);
        }
    }

    #endregion
}