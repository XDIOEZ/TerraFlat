using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Mirrors collision-only tilemaps for boundary chunks into the neighbouring torus image.
/// Rendering is handled by WrappedWorldCameraRenderer; these objects never render or save.
/// </summary>
[DisallowMultipleComponent]
public sealed class WrappedTilemapCollisionProxy : MonoBehaviour
{
    private readonly List<ProxyRecord> records = new();
    private readonly List<Vector2> requiredOffsets = new(3);
    private Map map;
    private bool dirty = true;
    private int refreshGeneration;

    public int ActiveProxyCount { get; private set; }
    public int EligibleSourceColliderCount { get; private set; }

    public static void Ensure(Map target)
    {
        if (target == null)
            return;

        WrappedTilemapCollisionProxy proxy = target.GetComponent<WrappedTilemapCollisionProxy>();
        if (proxy == null)
            proxy = target.gameObject.AddComponent<WrappedTilemapCollisionProxy>();
        proxy.map = target;
        proxy.dirty = true;
    }

    public static void MarkDirty(Map target)
    {
        if (target == null)
            return;
        Ensure(target);
        target.GetComponent<WrappedTilemapCollisionProxy>().dirty = true;
    }

    private void Awake()
    {
        map = GetComponent<Map>();
    }

    private void LateUpdate()
    {
        if (dirty)
            RefreshNow();
    }

    private void OnDisable()
    {
        SetAllActive(false);
    }

    private void OnDestroy()
    {
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

        if (map == null || map.Data == null || !map.IsTilemapVisualReady ||
            !WorldTopologyRuntime.TryGetActiveBounds(out WorldTopologyBounds bounds))
        {
            SetAllActive(false);
            return;
        }

        BuildRequiredOffsets(bounds);
        if (requiredOffsets.Count == 0)
        {
            SetAllActive(false);
            return;
        }

        RefreshSource(map.tileMap, requiredOffsets);
        BlockingTilemapLayer blockingLayer = map.GetComponent<BlockingTilemapLayer>();
        RefreshSource(blockingLayer != null ? blockingLayer.BlockingTilemap : null, requiredOffsets);

        for (int i = 0; i < records.Count; i++)
        {
            ProxyRecord record = records[i];
            bool active = record.Generation == refreshGeneration &&
                          record.Source != null && record.Source.gameObject.activeInHierarchy;
            if (record.Root != null)
                record.Root.SetActive(active);
        }
    }

    private void RefreshSource(Tilemap source, List<Vector2> offsets)
    {
        if (source == null)
            return;
        TilemapCollider2D sourceCollider = source.GetComponent<TilemapCollider2D>();
        if (sourceCollider == null || !sourceCollider.enabled)
            return;
        EligibleSourceColliderCount++;

        for (int i = 0; i < offsets.Count; i++)
        {
            ProxyRecord record = GetOrCreate(source, sourceCollider, offsets[i]);
            if (record == null)
                continue;
            CopyTiles(source, record.Tilemap);
            UpdateTransform(record, source, offsets[i]);
            record.Generation = refreshGeneration;
            record.Root.SetActive(source.gameObject.activeInHierarchy);
            record.Collider.enabled = sourceCollider.enabled;
            record.Collider.ProcessTilemapChanges();
            ActiveProxyCount++;
        }
    }

    private void BuildRequiredOffsets(WorldTopologyBounds bounds)
    {
        requiredOffsets.Clear();
        int xDirection = map.Data.position.x <= bounds.Min.x
            ? 1
            : map.Data.position.x + map.Data.Width >= bounds.MaxExclusive.x ? -1 : 0;
        int yDirection = map.Data.position.y <= bounds.Min.y
            ? 1
            : map.Data.position.y + map.Data.Height >= bounds.MaxExclusive.y ? -1 : 0;

        if (xDirection != 0)
            requiredOffsets.Add(new Vector2(xDirection * bounds.Span.x, 0f));
        if (yDirection != 0)
            requiredOffsets.Add(new Vector2(0f, yDirection * bounds.Span.y));
        if (xDirection != 0 && yDirection != 0)
            requiredOffsets.Add(new Vector2(xDirection * bounds.Span.x, yDirection * bounds.Span.y));
    }

    private ProxyRecord GetOrCreate(Tilemap source, TilemapCollider2D sourceCollider, Vector2 offset)
    {
        for (int i = 0; i < records.Count; i++)
        {
            ProxyRecord existing = records[i];
            if (existing.Source == source && existing.Offset == offset)
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
        proxyCollider.gameObject.AddComponent<WorldTopologyColliderProxy>().Bind(sourceCollider, offset);
        TilemapDamageReceiver damageReceiver = tileObject.AddComponent<TilemapDamageReceiver>();
        damageReceiver.Bind(map, proxyTilemap, proxyCollider);

        var record = new ProxyRecord
        {
            Source = source,
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
        public GameObject Root;
        public Tilemap Tilemap;
        public TilemapCollider2D Collider;
        public Vector2 Offset;
        public int Generation;
    }
}
