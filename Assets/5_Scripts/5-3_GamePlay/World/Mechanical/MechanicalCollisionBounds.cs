using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>把数据机械的物品视觉矩形转成角色移动阻挡范围；默认一格，资源热更后清除缓存。</summary>
public static class MechanicalCollisionBounds
{
    #region 视觉碰撞配置缓存

    private readonly struct LocalBox
    {
        public readonly Vector2 Offset; // 相对机械根锚点的中心。
        public readonly Vector2 HalfExtents; // 未旋转的半宽高。

        public LocalBox(Vector2 offset, Vector2 halfExtents)
        {
            Offset = offset;
            HalfExtents = halfExtents;
        }
    }

    private static readonly Dictionary<string, LocalBox> localBoxes = new(StringComparer.Ordinal);
    private static GameRes resources; // 当前正式物品目录。
    private static readonly LocalBox DefaultBox = new(Vector2.zero, Vector2.one * .5f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        if (resources != null) resources.ResourcesReloaded -= ClearCache;
        resources = null;
        localBoxes.Clear();
    }

    /// <summary>目录实例切换或 F5 资源热更时重新读取物品视觉碰撞配置。</summary>
    private static void EnsureResources()
    {
        GameRes current = GameRes.ExistingInstance;
        if (resources == current) return;
        if (resources != null) resources.ResourcesReloaded -= ClearCache;
        resources = current;
        if (resources != null) resources.ResourcesReloaded += ClearCache;
        ClearCache();
    }

    /// <summary>缓存同类型机械的局部矩形；缺省沿用原有一格阻挡。</summary>
    private static LocalBox ResolveLocalBox(string itemId)
    {
        if (localBoxes.TryGetValue(itemId, out LocalBox box)) return box;
        if (resources == null || !resources.TryGetItemDefinition(itemId, out RuntimeItemDefinition item))
            return DefaultBox;

        ItemColliderDefinitionDto collider = item.Visual?.Collider;
        Vector2 size = collider?.Size ?? Vector2.one;
        Vector2 offset = collider?.Offset ?? Vector2.zero;
        bool boxCollider = collider == null || string.IsNullOrEmpty(collider.Type) ||
            string.Equals(collider.Type, "BoxCollider2D", StringComparison.Ordinal);
        if (!boxCollider || !IsFinitePositive(size.x) || !IsFinitePositive(size.y) ||
            !IsFinite(offset.x) || !IsFinite(offset.y))
            box = DefaultBox;
        else box = new LocalBox(offset, size * .5f);
        localBoxes.Add(itemId, box);
        return box;
    }

    private static bool IsFinitePositive(float value) => value > 0f && IsFinite(value);
    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static void ClearCache() => localBoxes.Clear();

    #endregion

    #region 世界矩形

    /// <summary>按节点建造朝向旋转底座，不读取碰撞体或实例化机械本体。</summary>
    public static void ResolveWorldBox(MechanicalNode node, out Vector2 center,
        out Vector2 halfExtents)
    {
        if (node?.Snapshot == null || node.Definition == null)
        {
            center = Vector2.zero;
            halfExtents = DefaultBox.HalfExtents;
            return;
        }

        EnsureResources();
        LocalBox box = ResolveLocalBox(node.Definition.Id);
        Vector2 offset = box.Offset;
        bool sideways = (node.RotationQuarterTurns & 1) != 0;
        Vector2 rotatedOffset = (node.RotationQuarterTurns & 3) switch
        {
            1 => new Vector2(-offset.y, offset.x),
            2 => -offset,
            3 => new Vector2(offset.y, -offset.x),
            _ => offset
        };
        center = (Vector2)node.Snapshot.transform.position + rotatedOffset;
        halfExtents = sideways
            ? new Vector2(box.HalfExtents.y, box.HalfExtents.x)
            : box.HalfExtents;
    }

    #endregion
}
