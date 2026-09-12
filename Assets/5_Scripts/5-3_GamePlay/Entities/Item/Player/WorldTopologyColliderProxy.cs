using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public sealed class WorldTopologyColliderProxy : MonoBehaviour
{
    public Collider2D SourceCollider { get; private set; }
    public Vector2 ImageOffset { get; private set; }

    internal void Bind(Collider2D source, Vector2 imageOffset)
    {
        SourceCollider = source;
        ImageOffset = imageOffset;
    }

    public static Collider2D Resolve(Collider2D collider)
    {
        WorldTopologyColliderProxy proxy = collider != null
            ? collider.GetComponent<WorldTopologyColliderProxy>()
            : null;
        return proxy?.SourceCollider != null ? proxy.SourceCollider : collider;
    }

    public static T ResolveComponent<T>(Collider2D collider) where T : class
    {
        Collider2D source = Resolve(collider);
        if (source == null)
            return null;

        T component = source.GetComponent<T>();
        component ??= source.GetComponentInParent<T>();
        if (component != null)
            return component;

        // 玩家碰撞体常在根节点，而 DamageReceiver 等模块在子节点，需要继续向下解析。
        component = source.GetComponentInChildren<T>(true);
        if (component != null)
            return component;

        // Item 的碰撞体与功能模块不保证处于同一分支：例如碰撞体可能属于表现/成长模块，
        // DamageReceiver 位于兄弟模块。最后回到最近的 Item 根，再搜索完整 Item 子树。
        Item ownerItem = source.GetComponentInParent<Item>();
        return ownerItem != null ? ownerItem.GetComponentInChildren<T>(true) : null;
    }
}

/// <summary>Maintains collision-only images of one authoritative Item near torus seams.</summary>
[DisallowMultipleComponent]
public sealed class WorldTopologyProxySource : MonoBehaviour
{
    private const float SeamBand = 32f;

    private readonly List<ProxyRecord> proxies = new();
    private readonly List<Vector2> requiredOffsets = new(3);
    private Item item;
    // 只缓存真正需要镜像的碰撞体；过滤过程包含多次 GetComponent，绝不能放在 FixedUpdate 热路径里。
    private Collider2D[] sources = Array.Empty<Collider2D>();

    public int ActiveProxyCount { get; private set; }

    public static void Ensure(Item target)
    {
        if (target == null)
            return;

        Collider2D[] colliders = CollectProxySources(target);
        WorldTopologyProxySource source = target.GetComponent<WorldTopologyProxySource>();
        if (colliders.Length == 0)
        {
            // 对象池复用时，旧实例可能已经带有该组件；此时必须关闭旧代理。
            source?.Bind(target, colliders);
            return;
        }

        if (source == null)
            source = target.gameObject.AddComponent<WorldTopologyProxySource>();
        source.Bind(target, colliders);
    }

    private void Awake()
    {
        item = GetComponent<Item>();
        sources = item != null ? CollectProxySources(item) : Array.Empty<Collider2D>();
        enabled = sources.Length > 0;
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
        for (int i = 0; i < proxies.Count; i++)
        {
            if (proxies[i].Proxy != null)
                Destroy(proxies[i].Proxy.gameObject);
        }
        proxies.Clear();
    }

    public void RefreshProxies()
    {
        if (!IsRegisteredRuntimeItem() ||
            !WorldTopologyRuntime.TryGetActiveBounds(out WorldTopologyBounds bounds))
        {
            SetAllActive(false);
            return;
        }

        if (sources == null || sources.Length == 0)
        {
            SetAllActive(false);
            return;
        }

        int activeCount = 0;
        for (int sourceIndex = 0; sourceIndex < sources.Length; sourceIndex++)
        {
            Collider2D source = sources[sourceIndex];
            if (source == null)
                continue;

            BuildRequiredOffsets(source.bounds, bounds);
            for (int offsetIndex = 0; offsetIndex < requiredOffsets.Count; offsetIndex++)
            {
                Vector2 offset = requiredOffsets[offsetIndex];
                ProxyRecord record = GetOrCreate(source, offset);
                if (record == null)
                    continue;
                UpdateProxy(record, source, offset);
                record.Proxy.enabled = source.enabled && source.gameObject.activeInHierarchy;
                activeCount++;
            }
        }

        for (int i = 0; i < proxies.Count; i++)
        {
            ProxyRecord record = proxies[i];
            bool required = record.LastRefreshFrame == Time.frameCount;
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
            if (!ShouldProxy(collider))
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
                Destroy(record.Proxy.gameObject);
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
        if (source.GetComponent<ItemPicker>() != null ||
            source.GetComponent<Mod_InteractSender>() != null ||
            source.GetComponent<Mod_Damage>() != null ||
            source.GetComponent<BuildingShadow>() != null)
        {
            return false;
        }
        return true;
    }

    private void BuildRequiredOffsets(Bounds colliderBounds, WorldTopologyBounds bounds)
    {
        requiredOffsets.Clear();
        int xDirection = colliderBounds.min.x < bounds.Min.x + SeamBand
            ? 1
            : colliderBounds.max.x > bounds.MaxExclusive.x - SeamBand ? -1 : 0;
        int yDirection = colliderBounds.min.y < bounds.Min.y + SeamBand
            ? 1
            : colliderBounds.max.y > bounds.MaxExclusive.y - SeamBand ? -1 : 0;

        if (xDirection != 0)
            requiredOffsets.Add(new Vector2(xDirection * bounds.Span.x, 0f));
        if (yDirection != 0)
            requiredOffsets.Add(new Vector2(0f, yDirection * bounds.Span.y));
        if (xDirection != 0 && yDirection != 0)
            requiredOffsets.Add(new Vector2(xDirection * bounds.Span.x, yDirection * bounds.Span.y));
    }

    private ProxyRecord GetOrCreate(Collider2D source, Vector2 offset)
    {
        for (int i = 0; i < proxies.Count; i++)
        {
            ProxyRecord existing = proxies[i];
            if (existing.Source == source && existing.Offset == offset)
            {
                existing.LastRefreshFrame = Time.frameCount;
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
            LastRefreshFrame = Time.frameCount
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
            BoxCollider2D box => CopyBox(proxyObject, box),
            CircleCollider2D circle => CopyCircle(proxyObject, circle),
            CapsuleCollider2D capsule => CopyCapsule(proxyObject, capsule),
            PolygonCollider2D polygon => CopyPolygon(proxyObject, polygon),
            EdgeCollider2D edge => CopyEdge(proxyObject, edge),
            _ => null
        };
        if (clone == null)
        {
            Destroy(proxyObject);
            return null;
        }

        CopyCommon(source, clone);
        proxyObject.AddComponent<WorldTopologyColliderProxy>().Bind(source, offset);
        return clone;
    }

    private static void UpdateProxy(ProxyRecord record, Collider2D source, Vector2 offset)
    {
        Transform sourceTransform = source.transform;
        Vector3 position = sourceTransform.position + (Vector3)offset;
        record.Body.position = position;
        record.Body.rotation = sourceTransform.eulerAngles.z;
        record.Proxy.transform.localScale = sourceTransform.lossyScale;
        record.Proxy.gameObject.layer = source.gameObject.layer;
        record.Proxy.isTrigger = source.isTrigger;
        record.Proxy.sharedMaterial = source.sharedMaterial;
        record.LastRefreshFrame = Time.frameCount;
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

    private static Collider2D CopyBox(GameObject target, BoxCollider2D source)
    {
        BoxCollider2D clone = target.AddComponent<BoxCollider2D>();
        clone.size = source.size;
        clone.edgeRadius = source.edgeRadius;
        clone.autoTiling = source.autoTiling;
        return clone;
    }

    private static Collider2D CopyCircle(GameObject target, CircleCollider2D source)
    {
        CircleCollider2D clone = target.AddComponent<CircleCollider2D>();
        clone.radius = source.radius;
        return clone;
    }

    private static Collider2D CopyCapsule(GameObject target, CapsuleCollider2D source)
    {
        CapsuleCollider2D clone = target.AddComponent<CapsuleCollider2D>();
        clone.size = source.size;
        clone.direction = source.direction;
        return clone;
    }

    private static Collider2D CopyPolygon(GameObject target, PolygonCollider2D source)
    {
        PolygonCollider2D clone = target.AddComponent<PolygonCollider2D>();
        clone.pathCount = source.pathCount;
        for (int i = 0; i < source.pathCount; i++)
            clone.SetPath(i, source.GetPath(i));
        return clone;
    }

    private static Collider2D CopyEdge(GameObject target, EdgeCollider2D source)
    {
        EdgeCollider2D clone = target.AddComponent<EdgeCollider2D>();
        clone.points = source.points;
        clone.edgeRadius = source.edgeRadius;
        return clone;
    }

    private sealed class ProxyRecord
    {
        public Collider2D Source;
        public Collider2D Proxy;
        public Rigidbody2D Body;
        public Vector2 Offset;
        public int LastRefreshFrame;
    }
}
