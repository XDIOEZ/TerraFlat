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
    
    // Gizmos显示相关
    private Vector3 gizmoPosition = Vector3.zero;
    private bool showGizmo = false;

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
        int attempts = 0;
        const int maxAttempts = 5;

        while (attempts < maxAttempts)
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

            // 生成测试位置
            Vector3 testPosition = context.transform.position + (Vector3)randomOffset;
            Vector2Int testPosInt = Vector2Int.FloorToInt(testPosition);

            // 检查是否落在危险区域（权重大于等于5000的tiledata）
            // 伪代码：假设有一个方法可以获取tile的权重
            // int tileWeight = context.map.GetTileWeight(testPosInt);
            // if (tileWeight >= 5000) // 危险区域判断
            if (IsDangerousTile(testPosInt)) // 使用伪代码方法
            {
                // A点落入危险地点，生成对称点B
                Vector3 symmetricPosition = context.transform.position - (Vector3)randomOffset;
                Vector2Int symmetricPosInt = Vector2Int.FloorToInt(symmetricPosition);
                
                // 检查对称点是否安全
                // if (context.map.GetTileWeight(symmetricPosInt) < 5000) // 安全区域判断
                if (!IsDangerousTile(symmetricPosInt)) // 使用伪代码方法
                {
                    chosenPosition = symmetricPosition;
                    found = true;
                    // 设置Gizmos显示
                    gizmoPosition = chosenPosition;
                    showGizmo = true;
                    break;
                }
                else
                {
                    // 对称点B也危险，继续下一次随机尝试
                    attempts++;
                    continue;
                }
            }
            else
            {
                // 检查地图可用性（原有逻辑）
                if (context.map.GetTileArea(testPosInt) <= 2)
                {
                    chosenPosition = testPosition;
                    found = true;
                    // 设置Gizmos显示
                    gizmoPosition = chosenPosition;
                    showGizmo = true;
                    break;
                }
            }
            
            attempts++;
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
            // 设置Gizmos显示
            gizmoPosition = chosenPosition;
            showGizmo = true;
        }

        SetTargetPosition(chosenPosition);
        return State.Success;
    }

    // 伪代码方法：判断是否为危险tile
    private bool IsDangerousTile(Vector2Int tilePosition)
    {
        // 获取指定位置的TileData
        TileData tileData = context.map?.GetTile(tilePosition);
        if (tileData == null)
            return true; // 空位置视为危险
            
        // 检查惩罚值是否大于等于5000
        return tileData.Penalty >= 5000;
    }

    private State HandleFreeMovement(Vector2 sameTypeDirection)
    {
        Vector2 randomOffset = new Vector2(
            Random.Range(min.x, max.x),
            Random.Range(min.y, max.y)
        );

        if (sameTypeDirection.sqrMagnitude > 0.001f)
            randomOffset += sameTypeDirection * sameTypeAttraction;

        Vector3 targetPosition = context.transform.position + (Vector3)randomOffset;
        // 设置Gizmos显示
        gizmoPosition = targetPosition;
        showGizmo = true;
        
        SetTargetPosition(targetPosition, randomOffset);
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
    
    public override void OnDrawGizmos()
    {
        if (showGizmo)
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
    }

    #endregion
}