using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

public class RandomPosition : ActionNode
{
    public Vector2 min = Vector2.one * -10;
    public Vector2 max = Vector2.one * 10;
    public bool TrueRandom;

    // 同类检测与吸引相关参数
    public float sameTypeAttraction = 3f;   // 同类吸引力强度

    private Vector3 lastValidPosition = Vector3.zero;
    private const int maxTries = 5;
    // 重用列表减少GC分配
    private List<Vector2> sameTypePositions = new List<Vector2>(8); // 预设容量

    protected override void OnStart()
    {
        lastValidPosition = context.transform.position;
    }

    protected override void OnStop()
    {
    }

    protected override State OnUpdate()
    {
        // 获取指向同类的方向向量（已内部处理性能优化）
        Vector2 sameTypeDirection = GetSameTypeAverageDirection();

        if (!TrueRandom && context.map != null)
        {
            Vector3 chosenPosition = Vector3.zero;
            bool found = false;

            for (int i = 0; i < maxTries; i++)
            {
                // 合并随机偏移计算，减少局部变量
                Vector2 randomOffset = new Vector2(
                    Random.Range(min.x, max.x),
                    Random.Range(min.y, max.y)
                );

                // 添加反向偏移（现有逻辑）
                if (lastValidPosition != context.transform.position) // 避免零向量检查
                {
                    Vector2 direction = (Vector2)(context.transform.position - lastValidPosition).normalized;
                    randomOffset += direction * 0.5f;
                }

                // 添加同类吸引偏移（仅在有同类时计算）
                if (sameTypeDirection.sqrMagnitude > 0.001f) // 比直接比较Vector2.zero更高效
                {
                    randomOffset += sameTypeDirection * sameTypeAttraction;
                }

                // 计算测试位置并验证
                Vector3 testPosition = context.transform.position + (Vector3)randomOffset;
                Vector2Int testPositionInt = Vector2Int.FloorToInt(testPosition);

                if (context.map.GetTileArea(testPositionInt) <= 2)
                {
                    chosenPosition = testPosition;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                Vector2 randomOffset = new Vector2(
                    Random.Range(min.x, max.x),
                    Random.Range(min.y, max.y)
                );

                if (sameTypeDirection.sqrMagnitude > 0.001f)
                {
                    randomOffset += sameTypeDirection * sameTypeAttraction;
                }
                chosenPosition = context.transform.position + (Vector3)randomOffset;
            }

            context.mover.TargetPosition = chosenPosition;
            blackboard.TargetPosition = chosenPosition - context.transform.position;
            lastValidPosition = context.transform.position;

            return State.Success;
        }
        else
        {
            Vector2 randomOffset = new Vector2(
                Random.Range(min.x, max.x),
                Random.Range(min.y, max.y)
            );

            if (sameTypeDirection.sqrMagnitude > 0.001f)
            {
                randomOffset += sameTypeDirection * sameTypeAttraction;
            }

            context.mover.TargetPosition = context.transform.position + (Vector3)randomOffset;
            blackboard.TargetPosition = randomOffset;
            lastValidPosition = context.transform.position;

            return State.Success;
        }
    }

    // 优化性能的同类方向计算
    private Vector2 GetSameTypeAverageDirection()
    {
        // 清空重用列表（避免每次创建新列表）
        sameTypePositions.Clear();

        // 快速空引用检查
        if (context.itemDetector == null || context.item == null ||
            context.item.itemData == null || context.itemDetector.CurrentItemsInArea == null)
        {
            return Vector2.zero;
        }

        // 缓存自身ID避免重复访问
        string selfId = context.item.itemData.IDName;

        // 遍历已过滤的物品列表
        foreach (var collider in context.itemDetector.CurrentItemsInArea)
        {
            // 快速排除空引用和自身
            if (collider == null || collider.transform == context.transform ||
                collider.itemData == null)
            {
                continue;
            }

            // 匹配同类ID
            if (collider.itemData.IDName == selfId)
            {
                sameTypePositions.Add(collider.transform.position);
            }
        }

        // 无同类直接返回
        int count = sameTypePositions.Count;
        if (count == 0)
        {
            return Vector2.zero;
        }

        // 计算平均位置（使用单循环减少迭代次数）
        Vector2 averagePosition = Vector2.zero;
        for (int i = 0; i < count; i++)
        {
            averagePosition += sameTypePositions[i];
        }
        averagePosition /= count;

        // 计算方向向量（使用sqrMagnitude判断是否需要归一化）
        Vector2 direction = averagePosition - (Vector2)context.transform.position;
        return direction.sqrMagnitude > 0.001f ? direction.normalized : Vector2.zero;
    }

    // 编辑器Gizmo
    private void OnDrawGizmosSelected()
    {
        if (Application.isPlaying && context != null)
        {
            Gizmos.color = Color.cyan;
            // 这里可以绘制实际检测范围（如果需要）
            Gizmos.DrawWireSphere(context.transform.position, 5f); // 可替换为实际范围值
        }
    }
}
