using System.Collections.Generic;
using FlatWorld.NaturalEntities;
using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.Tilemaps;
using Stopwatch = System.Diagnostics.Stopwatch;

/// <summary>
/// 地形使用静态 Composite 合并，资源与独立建筑使用按身份复用的 BoxCollider2D。
/// 矩形沿 Chunk 边界裁剪，共用 Blocking 的静态刚体；实体事件集中到一个物理 Tick，物理表现不持有玩法权威。
/// </summary>
public sealed class ChunkCollisionRenderer : MonoBehaviour, IChunkViewRenderer
{
    #region 碰撞映射

    private enum DirtyReason : byte { Machine, NaturalRegistered, NaturalRemoved, NaturalVisualRevision, NaturalSnapshot, NaturalReset, Count }
    private enum GeometryReason : byte { Bind, Unbind, Terrain }

    [SerializeField] private TilemapCollider2D tilemapCollider;
    [SerializeField] private CompositeCollider2D compositeCollider;
    private Tilemap collisionTilemap;
    private ChunkObstacleColliderSet obstacleColliders;
    private static Tile collisionTile;
    private readonly List<MachineWorld.MachineRenderCell> machineCells = new();
    private readonly HashSet<int> projectedMachineIds = new();
    private readonly List<NaturalEntityEcsService.BlockingBodySnapshot> naturalBodies = new();
    private readonly Dictionary<int, NaturalBodyUpdate> pendingNaturalBodies = new();
    private bool machineObstaclesDirty;
    private bool naturalResetPending;
    private readonly List<Collider2D> colliderScratch = new();
    private static readonly HashSet<ChunkCollisionRenderer> dirtyRenderers = new();
    private static readonly List<ChunkCollisionRenderer> dirtyRendererScratch = new();
    private bool dataObstaclesDirty;
    private int obstacleColliderCount;

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
    // 障碍变化独立通知，树木变化不能触发地形 Tilemap 镜像全量重建。
    internal static event System.Action<ChunkCollisionRenderer> ObstaclePresentationChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetPresentationEvents()
    {
        PresentationChanged = null;
        ObstaclePresentationChanged = null;
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
        compositeCollider.generationType = CompositeCollider2D.GenerationType.Manual;
        obstacleColliders = new ChunkObstacleColliderSet(collisionTilemap.transform);
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
        NaturalEntityEcsService.BlockingBodyChanged += HandleNaturalBodyChanged;
        NaturalEntityEcsService.PhysicsBodiesReset += HandlePhysicsBodiesReset;
        tilemapCollider.enabled = true;
        SyncAll(chunk.Terrain);
        RebuildDataObstacles(initialBind: true);
        PresentationChanged?.Invoke(this);
    }

    public void Unbind()
    {
        if (BoundChunk?.Terrain != null)
            BoundChunk.Terrain.Changed -= HandleTerrainChanged;
        MachineWorld.CellChanged -= HandleMachineCellChanged;
        NaturalEntityEcsService.BlockingBodyChanged -= HandleNaturalBodyChanged;
        NaturalEntityEcsService.PhysicsBodiesReset -= HandlePhysicsBodiesReset;
        BoundChunk = null;
        dataObstaclesDirty = false;
        machineObstaclesDirty = naturalResetPending = false;
        pendingNaturalBodies.Clear();
        obstacleColliders?.Clear();
        obstacleColliderCount = 0;
        dirtyRenderers.Remove(this);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        System.Array.Clear(dirtyReasonCounts, 0, dirtyReasonCounts.Length);
        visualRevisionDefinitionSample = null;
        visualRevisionGuidSample = 0;
#endif
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
        if (tilemapCollider == null || !tilemapCollider.hasTilemapChanges) return;
        tilemapCollider.ProcessTilemapChanges();
        if (compositeCollider != null &&
            compositeCollider.generationType == CompositeCollider2D.GenerationType.Manual)
            compositeCollider.GenerateGeometry();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        double milliseconds = ElapsedMilliseconds(started, Stopwatch.GetTimestamp());
        if (milliseconds >= SlowPhysicsRebuildMilliseconds && CanLogPhysicsRebuild(false))
            Debug.Log($"[ChunkPhysicsRebuild] frame={Time.frameCount} trigger={reason} chunk={name} geometry={milliseconds:F1}ms", this);
#endif
    }

    #endregion

    #region 纯数据实体投影

    private void HandleMachineCellChanged(Vector2Int cell) => MarkDataObstaclesDirty(cell);

    /// <summary>记录最终实体状态，旧/新位置通知在同一物理 Tick 合并，不扫描其它树。</summary>
    private void HandleNaturalBodyChanged(NaturalEntityEcsService.BlockingBodySnapshot body, bool exists,
        NaturalEntityEcsService.PhysicsBodyChangeReason reason)
    {
        if (BoundChunk?.Terrain == null) return;
        long key = ChunkObstacleColliderSet.Key(ChunkObstacleColliderSet.NaturalKind, body.RuntimeId);
        if (!obstacleColliders.Contains(key) && !pendingNaturalBodies.ContainsKey(body.RuntimeId) &&
            !TryResolveLocalBox(body.Bounds.center, body.Bounds.extents, out _, out _)) return;
        pendingNaturalBodies[body.RuntimeId] = new NaturalBodyUpdate(body, exists);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (reason == NaturalEntityEcsService.PhysicsBodyChangeReason.VisualRevision && visualRevisionDefinitionSample == null)
        {
            visualRevisionGuidSample = body.Guid;
            visualRevisionDefinitionSample = body.DefinitionId;
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

    private void HandlePhysicsBodiesReset()
    {
        pendingNaturalBodies.Clear();
        naturalResetPending = true;
        QueueDataObstacleRebuild(DirtyReason.NaturalReset);
    }

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
            machineObstaclesDirty = true;
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
        int slowestX = 0, slowestY = 0, slowestBoxes = 0;
        int rebuilt = 0, totalBoxes = 0, updatedBoxes = 0;
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
            int changedBoxes = renderer.RebuildDataObstacles();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            long rebuildFinished = Stopwatch.GetTimestamp();
            long chunkTicks = rebuildFinished - rebuildStarted;
            rebuildTicks += chunkTicks;
            rebuilt++;
            totalBoxes += renderer.obstacleColliderCount;
            updatedBoxes += changedBoxes;
            if (chunkTicks > slowestTicks)
            {
                slowestTicks = chunkTicks;
                slowestX = renderer.BoundChunk.Address.ChunkOrigin.X;
                slowestY = renderer.BoundChunk.Address.ChunkOrigin.Y;
                slowestBoxes = renderer.obstacleColliderCount;
            }
#endif
            if (changedBoxes > 0) ObstaclePresentationChanged?.Invoke(renderer);
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
                      $"boxes={totalBoxes} changed={updatedBoxes} total={totalMs:F1}ms " +
                      $"rebuild={rebuildTicks * 1000d / Stopwatch.Frequency:F1}ms " +
                      $"presentation={presentationTicks * 1000d / Stopwatch.Frequency:F1}ms " +
                      $"slowest=({slowestX},{slowestY})/{slowestBoxes}boxes/" +
                      $"{slowestTicks * 1000d / Stopwatch.Frequency:F1}ms");
        }
#endif
    }

    /// <summary>初始化查询当前状态；后续只更新脏机器集合和事件指明的自然物。</summary>
    private int RebuildDataObstacles(bool initialBind = false)
    {
        dataObstaclesDirty = false;
        if (BoundChunk?.Terrain == null || obstacleColliders == null) return 0;
        int changed = 0;
        if (initialBind || machineObstaclesDirty) changed += SyncMachineObstacles();
        machineObstaclesDirty = false;
        if (naturalResetPending) changed += obstacleColliders.ClearKind(ChunkObstacleColliderSet.NaturalKind);
        naturalResetPending = false;
        if (initialBind)
        {
            BoundsInt area = new(BoundChunk.Address.ChunkOrigin.X, BoundChunk.Address.ChunkOrigin.Y, 0,
                BoundChunk.Terrain.Width, BoundChunk.Terrain.Height, 1);
            NaturalEntityEcsService.CollectBlockingBodies(area, naturalBodies);
            for (int i = 0; i < naturalBodies.Count; i++) changed += SyncNaturalBody(naturalBodies[i], true);
            naturalBodies.Clear();
        }
        foreach (NaturalBodyUpdate update in pendingNaturalBodies.Values)
            changed += SyncNaturalBody(update.Body, update.Exists);
        pendingNaturalBodies.Clear();
        obstacleColliderCount = obstacleColliders.Count;
        return changed;
    }

    private int SyncNaturalBody(NaturalEntityEcsService.BlockingBodySnapshot body, bool exists)
    {
        long key = ChunkObstacleColliderSet.Key(ChunkObstacleColliderSet.NaturalKind, body.RuntimeId);
        return exists ? SyncObstacle(key, body.Bounds.center, body.Bounds.extents)
            : obstacleColliders.Remove(key) ? 1 : 0;
    }

    /// <summary>机器格事件可能只改变转速或库存，相同矩形不会写回 Unity 物理组件。</summary>
    private int SyncMachineObstacles()
    {
        int changed = 0;
        int originX = BoundChunk.Address.ChunkOrigin.X, originY = BoundChunk.Address.ChunkOrigin.Y;
        projectedMachineIds.Clear();
        MachineWorld.CollectInBounds(new BoundsInt(originX - 2, originY - 2, 0,
            BoundChunk.Terrain.Width + 4, BoundChunk.Terrain.Height + 4, 1), machineCells);
        for (int i = 0; i < machineCells.Count; i++)
        {
            MachineEntity node = machineCells[i].Node;
            if (node?.Definition.BlocksMovement != true || HasPhysicalItemCollider(node) ||
                !projectedMachineIds.Add(node.Id)) continue;
            MachineCollisionBounds.ResolveWorldBox(node, out Vector2 center, out Vector2 halfExtents);
            changed += SyncObstacle(ChunkObstacleColliderSet.Key(ChunkObstacleColliderSet.MachineKind, node.Id), center, halfExtents);
        }
        changed += obstacleColliders.RemoveExcept(ChunkObstacleColliderSet.MachineKind, projectedMachineIds);
        return changed;
    }

    private int SyncObstacle(long key, Vector2 worldCenter, Vector2 halfExtents)
    {
        if (!TryResolveLocalBox(worldCenter, halfExtents, out Vector2 center, out Vector2 size))
            return obstacleColliders.Remove(key) ? 1 : 0;
        return obstacleColliders.Set(key, center, size, SourceCollider) ? 1 : 0;
    }

    /// <summary>保留既有矩形与跨 Chunk 裁剪语义，不改变数据占地、导航或受击形状。</summary>
    private bool TryResolveLocalBox(Vector2 worldCenter, Vector2 halfExtents, out Vector2 center, out Vector2 size)
    {
        center = size = default;
        if (!(halfExtents.x > 0f && halfExtents.y > 0f)) return false;
        Vector2 origin = new(BoundChunk.Address.ChunkOrigin.X, BoundChunk.Address.ChunkOrigin.Y);
        Vector2 local = WorldTopologyRuntime.ShortestDelta(origin, worldCenter);
        Vector2 min = Vector2.Max(Vector2.zero, local - halfExtents);
        Vector2 max = Vector2.Min(new Vector2(BoundChunk.Terrain.Width, BoundChunk.Terrain.Height), local + halfExtents);
        size = max - min;
        if (!(size.x > 0f && size.y > 0f)) return false;
        center = (min + max) * 0.5f;
        return true;
    }

    /// <summary>障碍镜像只增删或更新发生变化的 Box，不参与地形 Composite。</summary>
    internal void CopyObstacleCollidersTo(ChunkObstacleColliderSet target, Vector2 imageOffset)
        => obstacleColliders?.CopyTo(target, imageOffset);

    private readonly struct NaturalBodyUpdate
    {
        public readonly NaturalEntityEcsService.BlockingBodySnapshot Body;
        public readonly bool Exists;
        public NaturalBodyUpdate(NaturalEntityEcsService.BlockingBodySnapshot body, bool exists)
        { Body = body; Exists = exists; }
    }

    private bool HasPhysicalItemCollider(MachineEntity node)
    {
        Item view = node.View != null ? node.View.item : null;
        if (view == null) return false;
        colliderScratch.Clear();
        view.GetComponentsInChildren(false, colliderScratch);
        for (int i = 0; i < colliderScratch.Count; i++)
            if (colliderScratch[i].enabled && colliderScratch[i].gameObject.activeInHierarchy && !colliderScratch[i].isTrigger) return true;
        return false;
    }

    private void OnEnable() => PresentationChanged?.Invoke(this);
    private void OnDisable() => PresentationChanged?.Invoke(this);
    private void OnDestroy()
    {
        Unbind();
        obstacleColliders?.Dispose();
    }

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
