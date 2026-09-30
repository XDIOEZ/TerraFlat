using System.Collections.Generic;
using FlatWorld.NaturalEntities;
using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.Tilemaps;
using Stopwatch = System.Diagnostics.Stopwatch;

/// <summary>把权威阻挡格映射成 Chunk 的唯一静态物理碰撞体。</summary>
public sealed class ChunkCollisionRenderer : MonoBehaviour, IChunkViewRenderer
{
    #region 碰撞映射

    private enum DirtyReason : byte { Machine, NaturalRegistered, NaturalRemoved, NaturalVisualRevision, NaturalSnapshot, NaturalReset, Count }
    private enum GeometryReason : byte { Bind, Unbind, Terrain, DataObstacle }

    [SerializeField] private TilemapCollider2D tilemapCollider;
    [SerializeField] private CompositeCollider2D compositeCollider;
    private Tilemap collisionTilemap;
    private PolygonCollider2D dataCollider;
    private static Tile collisionTile;
    private readonly List<MachineWorld.MachineRenderCell> machineCells = new();
    private readonly HashSet<int> projectedMachineIds = new();
    private readonly List<Bounds> naturalBounds = new();
    private readonly List<Vector2[]> obstaclePaths = new();
    private readonly List<Collider2D> colliderScratch = new();
    private static readonly HashSet<ChunkCollisionRenderer> dirtyRenderers = new();
    private static readonly List<ChunkCollisionRenderer> dirtyRendererScratch = new();
    private bool dataObstaclesDirty;
    private int obstaclePathCount;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // 只汇总慢重建的来源和耗时，避免逐格日志干扰测量。
    public static bool PhysicsRebuildDiagnosticsEnabled { get; set; } = true;
    private readonly int[] dirtyReasonCounts = new int[(int)DirtyReason.Count];
    private Vector2Int machineCellSample;
    private int visualRevisionGuidSample;
    private string visualRevisionDefinitionSample;
    private static readonly int[] flushReasonCounts = new int[(int)DirtyReason.Count];
    private const double SlowPhysicsRebuildMilliseconds = 8d;
    private const int MaxPhysicsRebuildLogsPerWindow = 12;
    private const float PhysicsRebuildLogWindowSeconds = 30f;
    private static int directPhysicsRebuildLogCount;
    private static int dataPhysicsRebuildLogCount;
    private static float physicsRebuildLogWindowStart;
#endif

    public ChunkRuntime BoundChunk { get; private set; }
    public TilemapCollider2D SourceTilemapCollider => tilemapCollider;
    public Collider2D SourceCollider => compositeCollider != null ? compositeCollider : tilemapCollider;
    internal static event System.Action<ChunkCollisionRenderer> PresentationChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetPresentationEvents()
    {
        PresentationChanged = null;
        dirtyRenderers.Clear();
        dirtyRendererScratch.Clear();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        System.Array.Clear(flushReasonCounts, 0, flushReasonCounts.Length);
        directPhysicsRebuildLogCount = 0;
        dataPhysicsRebuildLogCount = 0;
        physicsRebuildLogWindowStart = 0f;
#endif
    }

    private void Awake()
    {
        collisionTilemap = tilemapCollider != null ? tilemapCollider.GetComponent<Tilemap>() : null;
        if (collisionTilemap == null || compositeCollider == null ||
            tilemapCollider.attachedRigidbody == null ||
            tilemapCollider.attachedRigidbody.bodyType != RigidbodyType2D.Static ||
            !tilemapCollider.usedByComposite)
        {
            throw new System.InvalidOperationException("Chunk Collision Tilemap 必须装配静态 Rigidbody2D、TilemapCollider2D 和 CompositeCollider2D。");
        }
        dataCollider = collisionTilemap.GetComponent<PolygonCollider2D>();
        if (dataCollider == null)
            dataCollider = collisionTilemap.gameObject.AddComponent<PolygonCollider2D>();
        dataCollider.pathCount = 0;
        dataCollider.usedByComposite = true;
    }

    private static Tile CollisionTile
    {
        get
        {
            if (collisionTile != null) return collisionTile;
            // 碰撞形状只由权威阻挡标记决定，不依赖美术 Tile 的 Sprite 物理轮廓。
            collisionTile = ScriptableObject.CreateInstance<Tile>();
            collisionTile.hideFlags = HideFlags.HideAndDontSave;
            collisionTile.colliderType = Tile.ColliderType.Grid;
            return collisionTile;
        }
    }

    public void Bind(ChunkRuntime chunk)
    {
        if (chunk == null)
            throw new System.ArgumentNullException(nameof(chunk));
        Unbind();
        BoundChunk = chunk;
        if (chunk.Terrain != null)
            chunk.Terrain.Changed += HandleTerrainChanged;
        MachineWorld.CellChanged += HandleMachineCellChanged;
        NaturalEntityEcsService.PhysicsBodyChangedWithReason += HandleNaturalBodyChanged;
        NaturalEntityEcsService.PhysicsBodiesReset += HandlePhysicsBodiesReset;
        tilemapCollider.enabled = true;
        SyncAll(chunk.Terrain);
        RebuildDataObstacles();
        PresentationChanged?.Invoke(this);
    }

    public void Unbind()
    {
        if (BoundChunk?.Terrain != null)
            BoundChunk.Terrain.Changed -= HandleTerrainChanged;
        MachineWorld.CellChanged -= HandleMachineCellChanged;
        NaturalEntityEcsService.PhysicsBodyChangedWithReason -= HandleNaturalBodyChanged;
        NaturalEntityEcsService.PhysicsBodiesReset -= HandlePhysicsBodiesReset;
        BoundChunk = null;
        dataObstaclesDirty = false;
        dirtyRenderers.Remove(this);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        System.Array.Clear(dirtyReasonCounts, 0, dirtyReasonCounts.Length);
        visualRevisionDefinitionSample = null;
        visualRevisionGuidSample = 0;
#endif
        if (dataCollider != null)
            dataCollider.pathCount = 0;
        if (collisionTilemap != null)
        {
            collisionTilemap.ClearAllTiles();
            FlushGeometry(GeometryReason.Unbind);
        }
        if (tilemapCollider != null)
            tilemapCollider.enabled = false;
        PresentationChanged?.Invoke(this);
    }

    private void HandleTerrainChanged(ChunkTerrainChanged changed)
    {
        if (BoundChunk?.Terrain == null ||
            (changed.Kind != TerrainChangeKind.Cell && changed.Kind != TerrainChangeKind.TileStack))
            return;
        int x = changed.LocalCell.X;
        int y = changed.LocalCell.Y;
        if (x < 0 || x >= BoundChunk.Terrain.Width || y < 0 || y >= BoundChunk.Terrain.Height)
            return;
        Vector3Int position = new(x, y, 0);
        TileBase tile = GetCollisionTile(BoundChunk.Terrain.GetCell(x, y));
        if (collisionTilemap.GetTile(position) == tile) return;
        collisionTilemap.SetTile(position, tile);
        FlushGeometry(GeometryReason.Terrain);
        PresentationChanged?.Invoke(this);
    }

    private void SyncAll(ChunkTerrainData terrain)
    {
        if (terrain == null) return;
        var tiles = new TileBase[terrain.CellCount];
        for (int y = 0; y < terrain.Height; y++)
        for (int x = 0; x < terrain.Width; x++)
            tiles[y * terrain.Width + x] = GetCollisionTile(terrain.GetCell(x, y));
        collisionTilemap.SetTilesBlock(new BoundsInt(0, 0, 0, terrain.Width, terrain.Height, 1), tiles);
        FlushGeometry(GeometryReason.Bind);
    }

    private static TileBase GetCollisionTile(TerrainCell cell) =>
        cell.BlockingTileId != 0 && (cell.Flags & TerrainCellFlags.Blocking) != 0
            ? CollisionTile
            : null;

    private void FlushGeometry(GeometryReason reason)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        long started = Stopwatch.GetTimestamp();
#endif
        if (tilemapCollider != null && tilemapCollider.hasTilemapChanges)
            tilemapCollider.ProcessTilemapChanges();
        if (compositeCollider != null &&
            compositeCollider.generationType == CompositeCollider2D.GenerationType.Manual)
            compositeCollider.GenerateGeometry();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (reason != GeometryReason.DataObstacle)
        {
            double milliseconds = ElapsedMilliseconds(started, Stopwatch.GetTimestamp());
            if (milliseconds >= SlowPhysicsRebuildMilliseconds && CanLogPhysicsRebuild(false))
                Debug.Log($"[ChunkPhysicsRebuild] frame={Time.frameCount} trigger={reason} chunk={name} geometry={milliseconds:F1}ms", this);
        }
#endif
    }

    #endregion

    #region 纯数据实体投影

    private void HandleMachineCellChanged(Vector2Int cell) => MarkDataObstaclesDirty(cell);
    private void HandleNaturalBodyChanged(Bounds bounds, NaturalEntityEcsService.PhysicsBodyChangeReason reason,
        int guid, string definitionId)
    {
        if (BoundChunk?.Terrain == null) return;
        Vector2 origin = new(BoundChunk.Address.ChunkOrigin.X, BoundChunk.Address.ChunkOrigin.Y);
        Vector2 center = WorldTopologyRuntime.ShortestDelta(origin, bounds.center);
        Vector2 extents = bounds.extents;
        if (center.x + extents.x >= 0f && center.x - extents.x <= BoundChunk.Terrain.Width &&
            center.y + extents.y >= 0f && center.y - extents.y <= BoundChunk.Terrain.Height)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (reason == NaturalEntityEcsService.PhysicsBodyChangeReason.VisualRevision &&
                visualRevisionDefinitionSample == null)
            {
                visualRevisionGuidSample = guid;
                visualRevisionDefinitionSample = definitionId;
            }
#endif
            QueueDataObstacleRebuild(reason switch
            {
                NaturalEntityEcsService.PhysicsBodyChangeReason.Registered => DirtyReason.NaturalRegistered,
                NaturalEntityEcsService.PhysicsBodyChangeReason.Removed => DirtyReason.NaturalRemoved,
                NaturalEntityEcsService.PhysicsBodyChangeReason.VisualRevision => DirtyReason.NaturalVisualRevision,
                _ => DirtyReason.NaturalSnapshot
            });
        }
    }
    private void HandlePhysicsBodiesReset() => QueueDataObstacleRebuild(DirtyReason.NaturalReset);

    private void MarkDataObstaclesDirty(Vector2Int cell)
    {
        if (BoundChunk?.Terrain == null) return;
        Vector2 origin = new(BoundChunk.Address.ChunkOrigin.X, BoundChunk.Address.ChunkOrigin.Y);
        Vector2 delta = WorldTopologyRuntime.ShortestDelta(origin, cell);
        if (delta.x >= -2f && delta.x <= BoundChunk.Terrain.Width + 2f &&
            delta.y >= -2f && delta.y <= BoundChunk.Terrain.Height + 2f)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (dirtyReasonCounts[(int)DirtyReason.Machine] == 0)
                machineCellSample = cell;
#endif
            QueueDataObstacleRebuild(DirtyReason.Machine);
        }
    }

    /// <summary>变化事件只把真正受影响的 Chunk 入队；不再让每个 Chunk 常驻 FixedUpdate。</summary>
    private void QueueDataObstacleRebuild(DirtyReason reason)
    {
        if (BoundChunk?.Terrain == null) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        dirtyReasonCounts[(int)reason]++;
#endif
        dataObstaclesDirty = true;
        dirtyRenderers.Add(this);
    }

    /// <summary>由 ChunkMgr 的单一物理 Tick 批量处理脏区块，同一 Chunk 在一次刷新前只入队一次。</summary>
    internal static void FlushDirtyDataObstacles()
    {
        if (dirtyRenderers.Count == 0) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        long flushStarted = Stopwatch.GetTimestamp();
        long rebuildTicks = 0;
        long presentationTicks = 0;
        long slowestTicks = 0;
        int slowestX = 0, slowestY = 0, slowestPaths = 0;
        int rebuilt = 0, totalPaths = 0;
        Vector2Int machineSample = default;
        bool hasMachineSample = false;
        int visualGuidSample = 0;
        string visualDefinitionSample = null;
        System.Array.Clear(flushReasonCounts, 0, flushReasonCounts.Length);
#endif
        dirtyRendererScratch.Clear();
        dirtyRendererScratch.AddRange(dirtyRenderers);
        dirtyRenderers.Clear();
        for (int i = 0; i < dirtyRendererScratch.Count; i++)
        {
            ChunkCollisionRenderer renderer = dirtyRendererScratch[i];
            if (renderer == null || !renderer.dataObstaclesDirty || renderer.BoundChunk?.Terrain == null)
                continue;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!hasMachineSample && renderer.dirtyReasonCounts[(int)DirtyReason.Machine] > 0)
            {
                machineSample = renderer.machineCellSample;
                hasMachineSample = true;
            }
            for (int reason = 0; reason < flushReasonCounts.Length; reason++)
            {
                flushReasonCounts[reason] += renderer.dirtyReasonCounts[reason];
                renderer.dirtyReasonCounts[reason] = 0;
            }
            if (visualDefinitionSample == null && renderer.visualRevisionDefinitionSample != null)
            {
                visualGuidSample = renderer.visualRevisionGuidSample;
                visualDefinitionSample = renderer.visualRevisionDefinitionSample;
            }
            renderer.visualRevisionDefinitionSample = null;
            renderer.visualRevisionGuidSample = 0;
            long rebuildStarted = Stopwatch.GetTimestamp();
#endif
            renderer.RebuildDataObstacles();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            long rebuildFinished = Stopwatch.GetTimestamp();
            long chunkTicks = rebuildFinished - rebuildStarted;
            rebuildTicks += chunkTicks;
            rebuilt++;
            totalPaths += renderer.obstaclePathCount;
            if (chunkTicks > slowestTicks)
            {
                slowestTicks = chunkTicks;
                slowestX = renderer.BoundChunk.Address.ChunkOrigin.X;
                slowestY = renderer.BoundChunk.Address.ChunkOrigin.Y;
                slowestPaths = renderer.obstaclePathCount;
            }
#endif
            PresentationChanged?.Invoke(renderer);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            presentationTicks += Stopwatch.GetTimestamp() - rebuildFinished;
#endif
        }
        dirtyRendererScratch.Clear();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        double totalMs = ElapsedMilliseconds(flushStarted, Stopwatch.GetTimestamp());
        if (rebuilt > 0 && (totalMs >= SlowPhysicsRebuildMilliseconds || rebuilt >= 16) && CanLogPhysicsRebuild(true))
        {
            string machineSource = hasMachineSample ? $"({machineSample.x},{machineSample.y})" : "none";
            string visualSource = visualDefinitionSample != null
                ? $"{visualDefinitionSample}#{visualGuidSample}" : "none";
            Debug.Log($"[ChunkPhysicsRebuild] frame={Time.frameCount} trigger=DataObstacle " +
                      $"chunks={rebuilt} calls(machine={flushReasonCounts[(int)DirtyReason.Machine]}, " +
                      $"naturalAdd={flushReasonCounts[(int)DirtyReason.NaturalRegistered]}, " +
                      $"naturalRemove={flushReasonCounts[(int)DirtyReason.NaturalRemoved]}, " +
                      $"visualRevision={flushReasonCounts[(int)DirtyReason.NaturalVisualRevision]}, " +
                      $"snapshot={flushReasonCounts[(int)DirtyReason.NaturalSnapshot]}, " +
                      $"reset={flushReasonCounts[(int)DirtyReason.NaturalReset]}) " +
                      $"sample(machineCell={machineSource}, visualEntity={visualSource}) " +
                      $"paths={totalPaths} total={totalMs:F1}ms " +
                      $"rebuild={rebuildTicks * 1000d / Stopwatch.Frequency:F1}ms " +
                      $"presentation={presentationTicks * 1000d / Stopwatch.Frequency:F1}ms " +
                      $"slowest=({slowestX},{slowestY})/{slowestPaths}paths/" +
                      $"{slowestTicks * 1000d / Stopwatch.Frequency:F1}ms");
        }
#endif
    }

    private void RebuildDataObstacles()
    {
        dataObstaclesDirty = false;
        if (BoundChunk?.Terrain == null || dataCollider == null) return;
        int originX = BoundChunk.Address.ChunkOrigin.X;
        int originY = BoundChunk.Address.ChunkOrigin.Y;
        BoundsInt area = new(originX, originY, 0, BoundChunk.Terrain.Width, BoundChunk.Terrain.Height, 1);
        Vector2 origin = new(originX, originY);
        obstaclePathCount = 0;
        projectedMachineIds.Clear();
        MachineWorld.CollectInBounds(new BoundsInt(originX - 2, originY - 2, 0,
            BoundChunk.Terrain.Width + 4, BoundChunk.Terrain.Height + 4, 1), machineCells);
        for (int i = 0; i < machineCells.Count; i++)
        {
            MachineEntity node = machineCells[i].Node;
            if (node?.Definition.BlocksMovement != true ||
                !projectedMachineIds.Add(node.Id) || HasPhysicalItemCollider(node)) continue;
            MachineCollisionBounds.ResolveWorldBox(node, out Vector2 center, out Vector2 halfExtents);
            AddObstaclePath(WorldTopologyRuntime.ShortestDelta(origin, center), halfExtents);
        }
        NaturalEntityEcsService.CollectBlockingBounds(area, naturalBounds);
        for (int i = 0; i < naturalBounds.Count; i++)
        {
            Bounds box = naturalBounds[i];
            AddObstaclePath(WorldTopologyRuntime.ShortestDelta(origin, box.center), box.extents);
        }
        dataCollider.pathCount = obstaclePathCount;
        for (int i = 0; i < obstaclePathCount; i++)
            dataCollider.SetPath(i, obstaclePaths[i]);
        FlushGeometry(GeometryReason.DataObstacle);
    }

    private bool HasPhysicalItemCollider(MachineEntity node)
    {
        Item view = node.View != null ? node.View.item : null;
        if (view == null) return false;
        colliderScratch.Clear();
        view.GetComponentsInChildren(false, colliderScratch);
        for (int i = 0; i < colliderScratch.Count; i++)
            if (colliderScratch[i].enabled && !colliderScratch[i].isTrigger) return true;
        return false;
    }

    private void AddObstaclePath(Vector2 center, Vector2 halfExtents)
    {
        if (!(halfExtents.x > 0f && halfExtents.y > 0f)) return;
        float left = center.x - halfExtents.x;
        float right = center.x + halfExtents.x;
        float bottom = center.y - halfExtents.y;
        float top = center.y + halfExtents.y;
        left = Mathf.Max(0f, left);
        right = Mathf.Min(BoundChunk.Terrain.Width, right);
        bottom = Mathf.Max(0f, bottom);
        top = Mathf.Min(BoundChunk.Terrain.Height, top);
        if (right <= left || top <= bottom) return;
        Vector2[] path;
        if (obstaclePathCount < obstaclePaths.Count)
            path = obstaclePaths[obstaclePathCount];
        else
        {
            path = new Vector2[4];
            obstaclePaths.Add(path);
        }
        path[0] = new Vector2(left, bottom);
        path[1] = new Vector2(right, bottom);
        path[2] = new Vector2(right, top);
        path[3] = new Vector2(left, top);
        obstaclePathCount++;
    }

    private void OnEnable() => PresentationChanged?.Invoke(this);
    private void OnDisable() => PresentationChanged?.Invoke(this);
    private void OnDestroy() => Unbind();

    #endregion

    #region 物理重建诊断

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private static double ElapsedMilliseconds(long started, long finished) =>
        (finished - started) * 1000d / Stopwatch.Frequency;

    private static bool CanLogPhysicsRebuild(bool dataObstacle)
    {
        if (!PhysicsRebuildDiagnosticsEnabled) return false;
        float now = Time.realtimeSinceStartup;
        if (now < physicsRebuildLogWindowStart || now - physicsRebuildLogWindowStart >= PhysicsRebuildLogWindowSeconds)
        {
            physicsRebuildLogWindowStart = now;
            directPhysicsRebuildLogCount = 0;
            dataPhysicsRebuildLogCount = 0;
        }
        if (dataObstacle)
        {
            if (dataPhysicsRebuildLogCount >= MaxPhysicsRebuildLogsPerWindow) return false;
            dataPhysicsRebuildLogCount++;
        }
        else
        {
            if (directPhysicsRebuildLogCount >= MaxPhysicsRebuildLogsPerWindow) return false;
            directPhysicsRebuildLogCount++;
        }
        return true;
    }
#endif

    #endregion
}
