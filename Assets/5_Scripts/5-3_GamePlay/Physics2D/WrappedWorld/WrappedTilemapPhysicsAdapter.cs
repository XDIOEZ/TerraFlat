using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Map / ChunkView 的边界 Tilemap 物理适配；触边即镜像，不使用 Item 的 32 单位接缝带。
/// 镜像不渲染、不保存；旧 Map 受击保留镜像坐标绑定，新 WorldModel 继续走权威格子查询。
/// </summary>
[DisallowMultipleComponent]
public sealed class WrappedTilemapPhysicsAdapter : MonoBehaviour
{
    private readonly List<ProxyRecord> records = new();
    private Map map;
    private ChunkCollisionRenderer chunkRenderer;
    private bool dirty = true;
    private int refreshGeneration;
    private WorldTopologyDomain lastDomain;

    public int ActiveProxyCount { get; private set; }
    public int EligibleSourceColliderCount { get; private set; }

    private bool IsSourceReady => map != null
        ? map.isActiveAndEnabled && map.Data != null && map.IsTilemapVisualReady
        : chunkRenderer != null && chunkRenderer.isActiveAndEnabled && chunkRenderer.BoundChunk?.Terrain != null;

    public static void Ensure(Map target)
    {
        if (target == null)
            return;

        WrappedTilemapPhysicsAdapter proxy = target.GetComponent<WrappedTilemapPhysicsAdapter>();
        if (proxy == null)
            proxy = target.gameObject.AddComponent<WrappedTilemapPhysicsAdapter>();
        proxy.map = target;
        proxy.chunkRenderer = null;
        proxy.dirty = true;
        proxy.enabled = true;
    }

    public static void Ensure(ChunkCollisionRenderer target)
    {
        if (target == null || target.SourceCollider == null)
            return;
        WrappedTilemapPhysicsAdapter proxy = target.GetComponent<WrappedTilemapPhysicsAdapter>();
        if (proxy == null)
            proxy = target.gameObject.AddComponent<WrappedTilemapPhysicsAdapter>();
        proxy.map = null;
        proxy.chunkRenderer = target;
        proxy.dirty = true;
        proxy.enabled = true;
    }

    internal void Suspend()
    {
        SetAllActive(false);
        dirty = true;
        enabled = false;
    }

    private void Awake()
    {
        map = GetComponent<Map>();
    }

    private void LateUpdate()
    {
        WorldTopologyDomain domain = WorldTopologyRuntime.GetActiveDomain();
        if (domain.IsWrapped != lastDomain.IsWrapped ||
            math.any(domain.Min != lastDomain.Min) || math.any(domain.Span != lastDomain.Span))
        {
            ClearProxies();
            lastDomain = domain;
            dirty = true;
        }

        if (!IsSourceReady)
        {
            SetAllActive(false);
            dirty = true;
            return;
        }

        if (dirty)
            RefreshNow();
        else
            SyncSourceState();
    }

    private void OnEnable() => dirty = true;

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
        for (int i = 0; i < records.Count; i++)
        {
            if (records[i].Root != null)
                Destroy(records[i].Root);
        }
        records.Clear();
    }

    public void RefreshNow()
    {
        dirty = false;
        refreshGeneration++;
        ActiveProxyCount = 0;
        EligibleSourceColliderCount = 0;

        if (!isActiveAndEnabled || !IsSourceReady ||
            !WorldTopologyRuntime.TryGetActiveBounds(out WorldTopologyBounds bounds))
        {
            ClearProxies();
            return;
        }

        lastDomain = bounds.ToDomain();
        float2 areaMin;
        float2 areaMax;
        if (map != null)
        {
            areaMin = new float2(map.Data.position.x, map.Data.position.y);
            areaMax = areaMin + new float2(map.Data.Width, map.Data.Height);
        }
        else
        {
            var chunk = chunkRenderer.BoundChunk;
            areaMin = new float2(chunk.Address.ChunkOrigin.X, chunk.Address.ChunkOrigin.Y);
            areaMax = areaMin + new float2(chunk.Terrain.Width, chunk.Terrain.Height);
        }
        WorldTopologyImageOffsets requiredOffsets = lastDomain.GetRequiredImageOffsets(
            areaMin, areaMax, includeBoundary: true);
        if (requiredOffsets.Count == 0)
        {
            ClearProxies();
            return;
        }

        if (map != null)
        {
            RefreshSource(map.tileMap, requiredOffsets);
            BlockingTilemapLayer blockingLayer = map.GetComponent<BlockingTilemapLayer>();
            RefreshSource(blockingLayer != null ? blockingLayer.BlockingTilemap : null, requiredOffsets);
        }
        else if (chunkRenderer.SourceCollider != null)
        {
            RefreshSource(chunkRenderer.SourceCollider.GetComponent<Tilemap>(), requiredOffsets);
        }
        RemoveStaleProxies();
        SyncSourceState();
    }

    private void RefreshSource(Tilemap source, WorldTopologyImageOffsets offsets)
    {
        if (source == null)
            return;
        TilemapCollider2D sourceCollider = source.GetComponent<TilemapCollider2D>();
        if (sourceCollider == null)
            return;
        EligibleSourceColliderCount++;

        for (int i = 0; i < offsets.Count; i++)
        {
            float2 imageOffset = offsets[i];
            Vector2 offset = new Vector2(imageOffset.x, imageOffset.y);
            ProxyRecord record = GetOrCreate(source, sourceCollider, offset);
            if (record == null)
                continue;
            CopyTiles(source, record.Tilemap);
            UpdateTransform(record, source, offset);
            record.Generation = refreshGeneration;
            record.Root.SetActive(source.gameObject.activeInHierarchy && sourceCollider.enabled);
            record.Collider.enabled = sourceCollider.enabled;
            record.Collider.ProcessTilemapChanges();
            if (record.Root.activeSelf)
                ActiveProxyCount++;
        }
    }

    /// <summary>只同步缓存源的启停与物理属性；复制地块仍由表现变更通知驱动。</summary>
    private void SyncSourceState()
    {
        ActiveProxyCount = 0;
        for (int i = records.Count - 1; i >= 0; i--)
        {
            ProxyRecord record = records[i];
            if (record.Source == null || record.SourceCollider == null)
            {
                record.Root.SetActive(false);
                Destroy(record.Root);
                records.RemoveAt(i);
                dirty = true;
                continue;
            }
            bool active = record.Source != null && record.SourceCollider != null &&
                          record.SourceCollider.enabled && record.Source.gameObject.activeInHierarchy;
            record.Root.SetActive(active);
            if (!active)
                continue;
            UpdateTransform(record, record.Source, record.Offset);
            record.Collider.enabled = true;
            record.Collider.isTrigger = record.SourceCollider.isTrigger;
            record.Collider.sharedMaterial = record.SourceCollider.sharedMaterial;
            record.Collider.usedByEffector = record.SourceCollider.usedByEffector;
            ActiveProxyCount++;
        }
    }

    /// <summary>重绑或碰撞源替换后销毁旧镜像，避免跨区块复用遗留几何与伤害归属。</summary>
    private void RemoveStaleProxies()
    {
        for (int i = records.Count - 1; i >= 0; i--)
        {
            ProxyRecord record = records[i];
            if (record.Generation == refreshGeneration && record.Source != null && record.SourceCollider != null)
                continue;
            record.Root.SetActive(false);
            Destroy(record.Root);
            records.RemoveAt(i);
        }
    }

    private ProxyRecord GetOrCreate(Tilemap source, TilemapCollider2D sourceCollider, Vector2 offset)
    {
        for (int i = 0; i < records.Count; i++)
        {
            ProxyRecord existing = records[i];
            if (existing.Source == source && existing.SourceCollider == sourceCollider && existing.Offset == offset)
                return existing;
        }

        var root = new GameObject($"Topology Tilemap Proxy - {source.name}");
        root.hideFlags = HideFlags.DontSave;
        root.layer = source.gameObject.layer;

        Grid sourceGrid = source.layoutGrid as Grid;
        Grid proxyGrid = root.AddComponent<Grid>();
        if (sourceGrid != null)
        {
            proxyGrid.cellSize = sourceGrid.cellSize;
            proxyGrid.cellGap = sourceGrid.cellGap;
            proxyGrid.cellLayout = sourceGrid.cellLayout;
            proxyGrid.cellSwizzle = sourceGrid.cellSwizzle;
        }

        var tileObject = new GameObject("Collision Tilemap");
        tileObject.hideFlags = HideFlags.DontSave;
        tileObject.layer = source.gameObject.layer;
        tileObject.transform.SetParent(root.transform, false);
        Tilemap proxyTilemap = tileObject.AddComponent<Tilemap>();
        proxyTilemap.orientation = source.orientation;
        proxyTilemap.orientationMatrix = source.orientationMatrix;
        proxyTilemap.tileAnchor = source.tileAnchor;
        TilemapCollider2D proxyCollider = tileObject.AddComponent<TilemapCollider2D>();
        proxyCollider.isTrigger = sourceCollider.isTrigger;
        proxyCollider.sharedMaterial = sourceCollider.sharedMaterial;
        proxyCollider.usedByEffector = sourceCollider.usedByEffector;
        proxyCollider.extrusionFactor = sourceCollider.extrusionFactor;
        proxyCollider.maximumTileChangeCount = sourceCollider.maximumTileChangeCount;
        proxyCollider.gameObject.AddComponent<ColliderSource2D>().Bind(sourceCollider, offset);
        if (map != null)
        {
            TilemapDamageReceiver damageReceiver = tileObject.AddComponent<TilemapDamageReceiver>();
            damageReceiver.Bind(map, proxyTilemap, proxyCollider);
        }

        var record = new ProxyRecord
        {
            Source = source,
            SourceCollider = sourceCollider,
            Root = root,
            Tilemap = proxyTilemap,
            Collider = proxyCollider,
            Offset = offset
        };
        records.Add(record);
        return record;
    }

    private static void CopyTiles(Tilemap source, Tilemap target)
    {
        target.ClearAllTiles();
        BoundsInt cellBounds = source.cellBounds;
        if (cellBounds.size.x <= 0 || cellBounds.size.y <= 0)
            return;
        target.SetTilesBlock(cellBounds, source.GetTilesBlock(cellBounds));
        target.RefreshAllTiles();
    }

    private static void UpdateTransform(ProxyRecord record, Tilemap source, Vector2 offset)
    {
        Transform sourceGrid = source.layoutGrid != null ? source.layoutGrid.transform : source.transform.parent;
        Transform rootTransform = record.Root.transform;
        if (sourceGrid != null)
        {
            rootTransform.SetPositionAndRotation(sourceGrid.position + (Vector3)offset, sourceGrid.rotation);
            rootTransform.localScale = sourceGrid.lossyScale;
            record.Tilemap.transform.localPosition = sourceGrid.InverseTransformPoint(source.transform.position);
            record.Tilemap.transform.localRotation = Quaternion.Inverse(sourceGrid.rotation) * source.transform.rotation;
            record.Tilemap.transform.localScale = source.transform.localScale;
        }
        else
        {
            rootTransform.SetPositionAndRotation(source.transform.position + (Vector3)offset, source.transform.rotation);
            rootTransform.localScale = source.transform.lossyScale;
        }
        record.Root.layer = source.gameObject.layer;
        record.Tilemap.gameObject.layer = source.gameObject.layer;
    }

    private void SetAllActive(bool active)
    {
        ActiveProxyCount = 0;
        for (int i = 0; i < records.Count; i++)
        {
            if (records[i].Root != null)
                records[i].Root.SetActive(active);
        }
    }

    private sealed class ProxyRecord
    {
        public Tilemap Source;
        public TilemapCollider2D SourceCollider;
        public GameObject Root;
        public Tilemap Tilemap;
        public TilemapCollider2D Collider;
        public Vector2 Offset;
        public int Generation;
    }
}
