using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// 玩家及必须保留 GameObject 几何的旧目标适配器；Physics2D 访问只发生在主线程。
/// 正式 Actor 使用纯数据几何，不能创建此 Bridge 或把其碰撞体传入 Job。
/// </summary>
internal sealed class PerceptionColliderBridge
{
    // 只在注册或结构变化时收集根级碰撞体。
    private readonly Collider2D[] colliders;

    /// <summary>冻结旧对象的碰撞体成员集合。</summary>
    internal PerceptionColliderBridge(Item item)
    {
        colliders = item.GetComponents<Collider2D>();
    }

    /// <summary>为旧对象构建本批次粗筛范围。</summary>
    internal bool TryGetBounds(out Bounds bounds)
    {
        bounds = default;
        bool found = false;
        foreach (Collider2D collider in colliders)
        {
            if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;
            if (found) bounds.Encapsulate(collider.bounds);
            else { bounds = collider.bounds; found = true; }
        }
        return found;
    }

    /// <summary>完成旧对象的主线程精筛；镜像中心由调用方按冻结拓扑传入。</summary>
    internal bool IntersectsCircle(float2 center, float radius)
    {
        var point = new Vector2(center.x, center.y);
        float squared = radius * radius;
        foreach (Collider2D collider in colliders)
        {
            if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;
            if ((collider.ClosestPoint(point) - point).sqrMagnitude <= squared) return true;
        }
        return false;
    }
}
