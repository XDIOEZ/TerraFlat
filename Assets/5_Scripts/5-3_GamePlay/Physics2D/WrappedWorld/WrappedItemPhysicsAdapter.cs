using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 当前 Item 的碰撞镜像适配器；32 单位严格接缝带保持既有查询覆盖范围。
/// 只在注册、重绑或组件结构变化时筛选碰撞源，FixedUpdate 不扫描子层级。
/// </summary>
[DisallowMultipleComponent]
public sealed class WrappedItemPhysicsAdapter : MonoBehaviour
{
    private const float SeamBand = 32f;

    private readonly List<ProxyRecord> proxies = new();
    private Item item;
    private bool sourcesDirty;
    private int refreshGeneration;
    private WorldTopologyDomain lastDomain;
    // 只缓存真正需要镜像的碰撞体；过滤过程包含多次 GetComponent，绝不能放在 FixedUpdate 热路径里。
    private Collider2D[] sources = Array.Empty<Collider2D>();

    public int ActiveProxyCount { get; private set; }

    public static void Ensure(Item target)
    {
        if (target == null)
            return;

        Collider2D[] colliders = CollectProxySources(target);
        WrappedItemPhysicsAdapter source = target.GetComponent<WrappedItemPhysicsAdapter>();
        if (colliders.Length == 0)
        {
            // 对象池复用时，旧实例可能已经带有该组件；此时必须关闭旧代理。
            source?.Bind(target, colliders);
            return;
        }

        if (source == null)
            source = target.gameObject.AddComponent<WrappedItemPhysicsAdapter>();
        source.Bind(target, colliders);
    }

    private void Awake()
    {
        item = GetComponent<Item>();
    }

    private void OnEnable() => sourcesDirty = true;

    internal void InvalidateSources()
    {
        sourcesDirty = true;
        enabled = true;
    }

    internal void Suspend()
    {
        SetAllActive(false);
        enabled = false;
    }

    private void FixedUpdate()
    {
        RefreshProxies();
    }

    private void OnDisable()
    {
        SetAllActive(false);
    }

    private void OnDestroy()
    {
        ClearProxies();
    }

    private void ClearProxies()
    {
        SetAllActive(false);
        for (int i = 0; i < proxies.Count; i++)
        {
            if (proxies[i].Proxy != null)
                Destroy(proxies[i].Proxy.gameObject);
        }
        proxies.Clear();
    }

    public void RefreshProxies()
    {
        refreshGeneration++;
        if (sourcesDirty && item != null)
        {
            sourcesDirty = false;
            sources = CollectProxySources(item);
            RemoveStaleProxies();
        }

        if (!isActiveAndEnabled || !IsRegisteredRuntimeItem() ||
            !WorldTopologyRuntime.TryGetActiveBounds(out WorldTopologyBounds bounds))
        {
            ClearProxies();
            lastDomain = default;
            return;
        }

        WorldTopologyDomain domain = bounds.ToDomain();
        if (!lastDomain.IsWrapped || math.any(domain.Min != lastDomain.Min) || math.any(domain.Span != lastDomain.Span))
            ClearProxies();
        lastDomain = domain;

        if (sources == null || sources.Length == 0)
        {
            SetAllActive(false);
            enabled = false;
            return;
        }

        int activeCount = 0;
        for (int sourceIndex = 0; sourceIndex < sources.Length; sourceIndex++)
        {
            Collider2D source = sources[sourceIndex];
            if (source == null || !source.enabled || !source.gameObject.activeInHierarchy ||
                (source.attachedRigidbody != null && !source.attachedRigidbody.simulated))
                continue;

            Bounds area = source.bounds;
            WorldTopologyImageOffsets requiredOffsets = domain.GetRequiredImageOffsets(
                new float2(area.min.x, area.min.y), new float2(area.max.x, area.max.y),
                SeamBand, includeBoundary: false);
            uint shapeHash = requiredOffsets.Count > 0 ? source.GetShapeHash() : 0u;
            for (int offsetIndex = 0; offsetIndex < requiredOffsets.Count; offsetIndex++)
            {
                float2 imageOffset = requiredOffsets[offsetIndex];
                Vector2 offset = new Vector2(imageOffset.x, imageOffset.y);
                ProxyRecord record = GetOrCreate(source, offset, shapeHash);
                if (record == null)
                    continue;
                UpdateProxy(record, source, offset, shapeHash);
                record.Proxy.enabled = source.enabled && source.gameObject.activeInHierarchy;
                activeCount++;
            }
        }

        for (int i = proxies.Count - 1; i >= 0; i--)
        {
            ProxyRecord record = proxies[i];
            if (record.Source == null || record.Proxy == null)
            {
                if (record.Proxy != null)
                {
                    record.Proxy.enabled = false;
                    Destroy(record.Proxy.gameObject);
                }
                proxies.RemoveAt(i);
                continue;
            }
            bool required = record.Generation == refreshGeneration;
            if (record.Proxy != null)
                record.Proxy.enabled = required && record.Proxy.enabled;
        }
        ActiveProxyCount = activeCount;
    }

    /// <summary>注册/对象池复用时刷新一次碰撞源，运行时热路径只读取缓存。</summary>
    private void Bind(Item target, Collider2D[] proxySources)
    {
        item = target;
        sources = proxySources ?? Array.Empty<Collider2D>();
        sourcesDirty = false;
        SetAllActive(false);
        RemoveStaleProxies();

        bool hasSources = sources.Length > 0;
        if (!hasSources)
            SetAllActive(false);
        enabled = hasSources;
    }

    /// <summary>筛掉交互、伤害等不应复制的查询碰撞体；仅在实体注册时执行。</summary>
    private static Collider2D[] CollectProxySources(Item target)
    {
        Collider2D[] colliders = target.GetComponentsInChildren<Collider2D>(true);
        int validCount = 0;
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider2D collider = colliders[i];
            // 不镜像嵌套 Item（尤其手持物/远程表现）的碰撞体，归属与其自己的注册链一致。
            if (!ShouldProxy(collider) || collider.GetComponentInParent<Item>(true) != target)
                continue;

            colliders[validCount++] = collider;
        }

        if (validCount == colliders.Length)
            return colliders;
        if (validCount == 0)
            return Array.Empty<Collider2D>();

        Array.Resize(ref colliders, validCount);
        return colliders;
    }

    /// <summary>对象池重绑后释放已经不属于当前碰撞源集合的旧镜像。</summary>
    private void RemoveStaleProxies()
    {
        for (int i = proxies.Count - 1; i >= 0; i--)
        {
            ProxyRecord record = proxies[i];
            if (record.Source != null && ContainsSource(record.Source))
                continue;

            if (record.Proxy != null)
            {
                record.Proxy.enabled = false;
                Destroy(record.Proxy.gameObject);
            }
            proxies.RemoveAt(i);
        }
    }

    private bool ContainsSource(Collider2D source)
    {
        for (int i = 0; i < sources.Length; i++)
        {
            if (sources[i] == source)
                return true;
        }

        return false;
    }

    private bool IsRegisteredRuntimeItem()
    {
        return item != null && item.itemData != null && ItemMgr.Instance != null &&
               !item.itemData.inHand &&
               ItemMgr.Instance.GetItemByGuid(item.itemData.Guid) == item;
    }

    private static bool ShouldProxy(Collider2D source)
    {
        if (source == null || source is TilemapCollider2D || source is CompositeCollider2D)
            return false;
        if (source is not BoxCollider2D && source is not CircleCollider2D &&
            source is not CapsuleCollider2D && source is not PolygonCollider2D && source is not EdgeCollider2D)
            return false;
        if (source.GetComponent<ItemPicker>() != null ||
            source.GetComponent<Mod_InteractSender>() != null ||
            source.GetComponent<Mod_Damage>() != null ||
            source.GetComponent<Mod_ChargeAttack>() != null ||
            source.GetComponent<BuildingShadow>() != null)
        {
            return false;
        }
        return true;
    }

    private ProxyRecord GetOrCreate(Collider2D source, Vector2 offset, uint shapeHash)
    {
        for (int i = 0; i < proxies.Count; i++)
        {
            ProxyRecord existing = proxies[i];
            if (existing.Source == source && existing.Offset == offset)
            {
                existing.Generation = refreshGeneration;
                return existing;
            }
        }

        Collider2D clone = CreateColliderClone(source, offset);
        if (clone == null)
            return null;
        var record = new ProxyRecord
        {
            Source = source,
            Proxy = clone,
            Body = clone.GetComponent<Rigidbody2D>(),
            Offset = offset,
            Generation = refreshGeneration,
            ShapeHash = shapeHash
        };
        proxies.Add(record);
        return record;
    }

    private static Collider2D CreateColliderClone(Collider2D source, Vector2 offset)
    {
        var proxyObject = new GameObject($"Topology Proxy - {source.name}");
        proxyObject.hideFlags = HideFlags.DontSave;
        proxyObject.layer = source.gameObject.layer;
        Rigidbody2D body = proxyObject.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        body.gravityScale = 0f;
        body.useFullKinematicContacts = true;

        Collider2D clone = source switch
        {
            BoxCollider2D _ => proxyObject.AddComponent<BoxCollider2D>(),
            CircleCollider2D _ => proxyObject.AddComponent<CircleCollider2D>(),
            CapsuleCollider2D _ => proxyObject.AddComponent<CapsuleCollider2D>(),
            PolygonCollider2D _ => proxyObject.AddComponent<PolygonCollider2D>(),
            EdgeCollider2D _ => proxyObject.AddComponent<EdgeCollider2D>(),
            _ => null
        };
        if (clone == null)
        {
            Destroy(proxyObject);
            return null;
        }

        CopyGeometry(source, clone);
        CopyCommon(source, clone);
        proxyObject.AddComponent<ColliderSource2D>().Bind(source, offset);
        return clone;
    }

    private void UpdateProxy(ProxyRecord record, Collider2D source, Vector2 offset, uint shapeHash)
    {
        if (record.ShapeHash != shapeHash)
        {
            CopyGeometry(source, record.Proxy);
            record.ShapeHash = shapeHash;
        }
        Transform sourceTransform = source.transform;
        Vector3 position = sourceTransform.position + (Vector3)offset;
        record.Body.position = position;
        record.Body.rotation = sourceTransform.eulerAngles.z;
        record.Proxy.transform.localScale = sourceTransform.lossyScale;
        record.Proxy.gameObject.layer = source.gameObject.layer;
        record.Proxy.isTrigger = source.isTrigger;
        record.Proxy.sharedMaterial = source.sharedMaterial;
        record.Proxy.offset = source.offset;
        record.Proxy.usedByEffector = source.usedByEffector;
        record.Generation = refreshGeneration;
    }

    /// <summary>只在形状变化时复制几何，Polygon/Edge 不在每个物理帧分配路径数组。</summary>
    private static void CopyGeometry(Collider2D source, Collider2D target)
    {
        switch (source)
        {
            case BoxCollider2D box:
                var boxTarget = (BoxCollider2D)target;
                boxTarget.size = box.size;
                boxTarget.edgeRadius = box.edgeRadius;
                boxTarget.autoTiling = box.autoTiling;
                break;
            case CircleCollider2D circle:
                ((CircleCollider2D)target).radius = circle.radius;
                break;
            case CapsuleCollider2D capsule:
                var capsuleTarget = (CapsuleCollider2D)target;
                capsuleTarget.size = capsule.size;
                capsuleTarget.direction = capsule.direction;
                break;
            case PolygonCollider2D polygon:
                var polygonTarget = (PolygonCollider2D)target;
                polygonTarget.pathCount = polygon.pathCount;
                for (int i = 0; i < polygon.pathCount; i++)
                    polygonTarget.SetPath(i, polygon.GetPath(i));
                break;
            case EdgeCollider2D edge:
                var edgeTarget = (EdgeCollider2D)target;
                edgeTarget.points = edge.points;
                edgeTarget.edgeRadius = edge.edgeRadius;
                break;
        }
    }

    private void SetAllActive(bool active)
    {
        ActiveProxyCount = 0;
        for (int i = 0; i < proxies.Count; i++)
        {
            if (proxies[i].Proxy != null)
                proxies[i].Proxy.enabled = active;
        }
    }

    private static void CopyCommon(Collider2D source, Collider2D target)
    {
        target.enabled = source.enabled;
        target.isTrigger = source.isTrigger;
        target.sharedMaterial = source.sharedMaterial;
        target.offset = source.offset;
        target.usedByEffector = source.usedByEffector;
    }

    private sealed class ProxyRecord
    {
        public Collider2D Source;
        public Collider2D Proxy;
        public Rigidbody2D Body;
        public Vector2 Offset;
        public int Generation;
        public uint ShapeHash;
    }
}
