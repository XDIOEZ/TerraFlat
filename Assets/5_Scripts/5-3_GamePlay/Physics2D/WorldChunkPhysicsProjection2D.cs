using System.Collections.Generic;
using FlatWorld.NaturalEntities;
using FlatWorld.WorldModel;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.Tilemaps;
using Stopwatch = System.Diagnostics.Stopwatch;

/// <summary>
/// 地形使用静态 Composite，组合储罐按精确矩形合并，资源与独立建筑使用按身份复用的 BoxCollider2D。
/// 矩形沿 Chunk 边界裁剪，共用世界物理节点的静态刚体；实体事件集中到一个物理 Tick，物理表现不持有玩法权威。
/// </summary>
public sealed class WorldChunkPhysicsProjection2D : MonoBehaviour
{
    #region 碰撞映射

    private enum DirtyReason : byte { Machine, NaturalRegistered, NaturalRemoved, NaturalVisualRevision, NaturalSnapshot, NaturalReset, Count }
    private enum GeometryReason : byte { Bind, Unbind, Terrain }

    private TilemapCollider2D tilemapCollider;
    private CompositeCollider2D compositeCollider;
    private Tilemap collisionTilemap;
    private ChunkObstacleColliderSet obstacleColliders;
    private static Tile collisionTile;
    private readonly List<MachineWorld.MachineRenderCell> machineCells = new();
    private readonly HashSet<int> projectedMachineIds = new();
    private readonly Dictionary<int, Vector2Int> projectedMachineAnchors = new();
    private readonly ChunkMergedObstacleRectangles mergedStorageRectangles = new();
    private readonly HashSet<int> projectedMergedStorageRectangleIds = new();
    private readonly HashSet<Vector2Int> dirtyMachineCells = new();
    private readonly List<Vector2Int> dirtyMachineScratch = new();
    private readonly HashSet<int> currentMachineIds = new();
    private readonly List<int> staleMachineIds = new();
    private readonly List<NaturalEntityEcsService.BlockingBodySnapshot> naturalBodies = new();
    private readonly Dictionary<int, NaturalBodyUpdate> pendingNaturalBodies = new();
    private bool machineObstaclesDirty;
    private bool naturalResetPending;
    private readonly List<Collider2D> colliderScratch = new();
    private static readonly ObstacleDirtyQueue dirtyProjections = new();
    private static readonly ProfilerMarker FlushObstacleMarker = new("FlatWorld.ChunkPhysics.FlushObstacles");
    private static readonly ProfilerMarker RebuildObstacleMarker = new("FlatWorld.ChunkPhysics.RebuildObstacles");
    private static readonly ProfilerMarker PublishObstacleMarker = new("FlatWorld.ChunkPhysics.PublishObstacles");
    private string boundWorldKey;
    private ChunkTerrainData boundTerrain;
    private WrappedTilemapPhysicsAdapter wrappedPhysics;
    private WorldPhysicsService2D physics;
    private bool dataObstaclesDirty;
    private int obstacleColliderCount;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // 只汇总慢重建的来源和耗时，避免逐格日志干扰测量。
    public static bool PhysicsRebuildDiagnosticsEnabled { get; set; } = true;
    private readonly int[] dirtyReasonCounts = new int[(int)DirtyReason.Count];
    private Vector2Int machineCellSample;
    private int visualRevisionGuidSample;
    private string visualRevisionDefinitionSample;
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
    public string WorldKey => boundWorldKey;
    internal ChunkTerrainData BoundTerrain => boundTerrain;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetPhysicsQueue()
    {
        dirtyProjections.Clear();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        directPhysicsRebuildLogCount = 0;
        dataPhysicsRebuildLogCount = 0;
        physicsRebuildLogWindowStart = 0f;
#endif
    }

    // 世界物理服务独占装配，任何物理对象都不挂在 ChunkView 下。
    internal static WorldChunkPhysicsProjection2D Create(WorldPhysicsService2D owner, Transform parent)
    {
        var root = new GameObject("World Chunk Physics") { hideFlags = HideFlags.DontSave };
        root.SetActive(false);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, owner.PrimarySpace.Scene);
        root.transform.SetParent(parent, false);
        try
        {
            root.AddComponent<Grid>();
            var terrain = new GameObject("Terrain Collision") { hideFlags = HideFlags.DontSave };
            terrain.transform.SetParent(root.transform, false);
            Tilemap tilemap = terrain.AddComponent<Tilemap>();
            Rigidbody2D body = terrain.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Static;
            body.gravityScale = 0f;
            TilemapCollider2D tiles = terrain.AddComponent<TilemapCollider2D>();
            CompositeCollider2D composite = terrain.AddComponent<CompositeCollider2D>();
            composite.generationType = CompositeCollider2D.GenerationType.Manual;
            composite.geometryType = CompositeCollider2D.GeometryType.Outlines;
            composite.vertexDistance = .0005f;
            composite.offsetDistance = .00005f;
            tiles.usedByComposite = true;
            var projection = root.AddComponent<WorldChunkPhysicsProjection2D>();
            projection.collisionTilemap = tilemap;
            projection.tilemapCollider = tiles;
            projection.compositeCollider = composite;
            projection.obstacleColliders = new ChunkObstacleColliderSet(terrain.transform);
            return projection;
        }
        catch { WorldPhysicsService2D.DestroyPhysicsObject(root); throw; }
    }

    internal void RefreshLocalPhysicsPosition()
    {
        if (BoundChunk == null) return;
        Vector2 origin = new(BoundChunk.Address.ChunkOrigin.X, BoundChunk.Address.ChunkOrigin.Y);
        transform.position = GameplayWorldRegistry.TryGet(boundWorldKey, out _)
            ? WorldLocalPresentation.ProjectPosition(origin) : origin;
        wrappedPhysics?.RefreshNow();
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

    internal void Bind(WorldPhysicsService2D owner, ChunkRuntime chunk)
    {
        if (chunk == null)
            throw new System.ArgumentNullException(nameof(chunk));
        Unbind();
        BoundChunk = chunk;
        physics = owner ?? throw new System.ArgumentNullException(nameof(owner));
        boundWorldKey = owner.WorldKey;
        boundTerrain = chunk.Terrain;
        name = $"Chunk Physics ({chunk.Address.ChunkOrigin.X}, {chunk.Address.ChunkOrigin.Y})";
        RefreshLocalPhysicsPosition();
        obstacleColliders.BindWorldPhysics(physics);
        physics.RegisterCollider(SourceCollider, regionId: null, entity: chunk);
        if (boundTerrain != null)
            boundTerrain.Changed += HandleTerrainChanged;
        MachineWorld.WorldPresentationCellChanged += HandleWorldMachineCellChanged;
        MachineWorld.WorldPresentationCellsChanged += HandleWorldMachineCellsChanged;
        NaturalEntityEcsService.BlockingBodyChanged += HandleNaturalBodyChanged;
        NaturalEntityEcsService.PhysicsBodiesReset += HandlePhysicsBodiesReset;
        tilemapCollider.enabled = true;
        SyncAll(chunk.Terrain);
        // 没有注册实体世界的纯地形服务不能借用当前游戏世界的机器或自然物。
        if (GameplayWorldRegistry.TryGet(boundWorldKey, out _)) RebuildDataObstacles(initialBind: true);
        WrappedTilemapPhysicsAdapter.Ensure(this);
        wrappedPhysics = GetComponent<WrappedTilemapPhysicsAdapter>();
        wrappedPhysics.RefreshNow();
    }

    internal void Unbind()
    {
        if (BoundChunk == null && physics == null) return;
        wrappedPhysics?.ReleaseChunkPhysicsProxies();
        physics?.UnregisterCollider(SourceCollider);
        if (boundTerrain != null)
            boundTerrain.Changed -= HandleTerrainChanged;
        boundTerrain = null;
        MachineWorld.WorldPresentationCellChanged -= HandleWorldMachineCellChanged;
        MachineWorld.WorldPresentationCellsChanged -= HandleWorldMachineCellsChanged;
        NaturalEntityEcsService.BlockingBodyChanged -= HandleNaturalBodyChanged;
        NaturalEntityEcsService.PhysicsBodiesReset -= HandlePhysicsBodiesReset;
        BoundChunk = null;
        dataObstaclesDirty = false;
        machineObstaclesDirty = naturalResetPending = false;
        projectedMachineAnchors.Clear();
        mergedStorageRectangles.Clear();
        projectedMergedStorageRectangleIds.Clear();
        dirtyMachineCells.Clear();
        dirtyMachineScratch.Clear();
        currentMachineIds.Clear();
        staleMachineIds.Clear();
        pendingNaturalBodies.Clear();
        obstacleColliders?.Clear();
        physics = null;
        obstacleColliderCount = 0;
        dirtyProjections.Remove(boundWorldKey, this);
        boundWorldKey = null;
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
        wrappedPhysics?.RefreshNow();
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
        wrappedPhysics?.RefreshNow();
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

    private void HandleWorldMachineCellChanged(string worldKey, Vector2Int cell)
    {
        if (worldKey != boundWorldKey || !GameplayWorldRegistry.TryGet(worldKey, out _)) return;
        using (GameplayWorldRegistry.UseWorld(worldKey)) MarkDataObstaclesDirty(cell);
    }

    private void HandleWorldMachineCellsChanged(string worldKey, IReadOnlyList<Vector2Int> cells)
    {
        if (worldKey != boundWorldKey || cells == null || !GameplayWorldRegistry.TryGet(worldKey, out _)) return;
        using (GameplayWorldRegistry.UseWorld(worldKey))
            foreach (Vector2Int cell in cells) MarkDataObstaclesDirty(cell);
    }

    /// <summary>记录最终实体状态，旧/新位置通知在同一物理 Tick 合并，不扫描其它树。</summary>
    private void HandleNaturalBodyChanged(NaturalEntityEcsService.BlockingBodySnapshot body, bool exists,
        NaturalEntityEcsService.PhysicsBodyChangeReason reason)
    {
        if (BoundChunk?.Terrain == null || body.WorldKey != boundWorldKey) return;
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
        if (GameplayWorldRegistry.ExecutionWorldKey != boundWorldKey) return;
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
            dirtyMachineCells.Add(WorldTopologyRuntime.NormalizeCell(cell));
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
        dirtyProjections.Add(boundWorldKey, this);
    }

    /// <summary>由世界物理服务在原生模拟前处理脏区块，同一 Chunk 在一次刷新前只入队一次。</summary>
    internal static void FlushDirtyDataObstacles(string worldKey)
    {
        if (!dirtyProjections.HasPending(worldKey)) return;
        using var marker = FlushObstacleMarker.Auto();
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
        System.Span<int> flushReasonCounts = stackalloc int[(int)DirtyReason.Count];
        flushReasonCounts.Clear();
#endif
        // 嵌套迁移可重入刷新，每次独占遍历缓冲。
        using var scratchScope = ListPool<WorldChunkPhysicsProjection2D>.Get(out var dirtyProjectionScratch);
        dirtyProjections.Take(worldKey, dirtyProjectionScratch);
        for (int i = 0; i < dirtyProjectionScratch.Count; i++)
        {
            WorldChunkPhysicsProjection2D projection = dirtyProjectionScratch[i];
            if (projection == null || projection.boundWorldKey != worldKey ||
                !projection.dataObstaclesDirty || projection.BoundChunk?.Terrain == null)
                continue;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!hasMachineSample && projection.dirtyReasonCounts[(int)DirtyReason.Machine] > 0)
            {
                machineSample = projection.machineCellSample;
                hasMachineSample = true;
            }
            for (int reason = 0; reason < flushReasonCounts.Length; reason++)
            {
                flushReasonCounts[reason] += projection.dirtyReasonCounts[reason];
                projection.dirtyReasonCounts[reason] = 0;
            }
            if (visualDefinitionSample == null && projection.visualRevisionDefinitionSample != null)
            {
                visualGuidSample = projection.visualRevisionGuidSample;
                visualDefinitionSample = projection.visualRevisionDefinitionSample;
            }
            projection.visualRevisionDefinitionSample = null;
            projection.visualRevisionGuidSample = 0;
            long rebuildStarted = Stopwatch.GetTimestamp();
#endif
            int changedBoxes;
            using (RebuildObstacleMarker.Auto()) changedBoxes = projection.RebuildDataObstacles();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            long rebuildFinished = Stopwatch.GetTimestamp();
            long chunkTicks = rebuildFinished - rebuildStarted;
            rebuildTicks += chunkTicks;
            rebuilt++;
            totalBoxes += projection.obstacleColliderCount;
            updatedBoxes += changedBoxes;
            if (chunkTicks > slowestTicks)
            {
                slowestTicks = chunkTicks;
                slowestX = projection.BoundChunk.Address.ChunkOrigin.X;
                slowestY = projection.BoundChunk.Address.ChunkOrigin.Y;
                slowestBoxes = projection.obstacleColliderCount;
            }
#endif
            if (changedBoxes > 0)
                using (PublishObstacleMarker.Auto()) projection.wrappedPhysics?.RefreshObstaclesNow();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            presentationTicks += Stopwatch.GetTimestamp() - rebuildFinished;
#endif
        }
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
        bool refreshMachines = machineObstaclesDirty;
        machineObstaclesDirty = false;
        if (initialBind)
        {
            dirtyMachineCells.Clear();
            changed += SyncMachineObstacles();
        }
        else if (refreshMachines) changed += SyncDirtyMachineObstacles();
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
        return exists ? SyncObstacle(key, body.Bounds.center, body.Bounds.extents,
                entity: body.Entity)
            : obstacleColliders.Remove(key) ? 1 : 0;
    }

    /// <summary>机器格事件可能只改变转速或库存，相同矩形不会写回 Unity 物理组件。</summary>
    private int SyncMachineObstacles()
    {
        int changed = 0;
        int originX = BoundChunk.Address.ChunkOrigin.X, originY = BoundChunk.Address.ChunkOrigin.Y;
        projectedMachineIds.Clear();
        projectedMachineAnchors.Clear();
        MachineWorld.CollectInBounds(new BoundsInt(originX - 2, originY - 2, 0,
            BoundChunk.Terrain.Width + 4, BoundChunk.Terrain.Height + 4, 1), machineCells);
        for (int i = 0; i < machineCells.Count; i++)
        {
            MachineEntity node = machineCells[i].Node;
            if (node?.Definition.BlocksTraversal != true || HasPhysicalItemCollider(node) ||
                !projectedMachineIds.Add(node.Id)) continue;
            changed += SyncMachineObstacleRectangle(node, out bool projected);
            if (projected) projectedMachineAnchors[node.Id] = node.Cell;
        }
        mergedStorageRectangles.RemoveSourcesExcept(projectedMachineIds);
        changed += obstacleColliders.RemoveExcept(ChunkObstacleColliderSet.MachineKind, projectedMachineIds);
        changed += SyncMergedStorageObstacleRectangles();
        return changed;
    }

    /// <summary>普通机器变化只更新受影响的锚点，复杂占地回退完整扫描。</summary>
    private int SyncDirtyMachineObstacles()
    {
        if (dirtyMachineCells.Count == 0) return SyncMachineObstacles();
        int changed = 0;
        // 拷贝本轮脏格，处理中途新事件留给下一次物理 Tick。
        dirtyMachineScratch.Clear();
        dirtyMachineScratch.AddRange(dirtyMachineCells);
        dirtyMachineCells.Clear();
        int highestLayer = MachineWorld.HighestOccupiedLayer;
        foreach (Vector2Int cell in dirtyMachineScratch)
        {
            currentMachineIds.Clear();
            for (int layer = 0; layer <= highestLayer; layer++)
            {
                MachineEntity node = MachineWorld.GetAtCurrentWorld(cell, layer);
                if (node == null) continue;
                // 占地格可能属于邻近机器，不能假定事件格就是实体锚点。
                if (node.Cell != cell)
                {
                    dirtyMachineScratch.Clear();
                    return changed + SyncMachineObstacles();
                }
                if (node.Definition?.BlocksTraversal != true || !currentMachineIds.Add(node.Id))
                    continue;
                if (HasPhysicalItemCollider(node))
                {
                    currentMachineIds.Remove(node.Id);
                    continue;
                }
                changed += SyncMachineObstacleRectangle(node, out bool projected);
                if (projected) projectedMachineAnchors[node.Id] = cell;
                else currentMachineIds.Remove(node.Id);
            }
            CollectStaleMachineIds(projectedMachineAnchors, cell, currentMachineIds, staleMachineIds);
            for (int i = 0; i < staleMachineIds.Count; i++)
            {
                int id = staleMachineIds[i];
                if (obstacleColliders.Remove(ChunkObstacleColliderSet.Key(ChunkObstacleColliderSet.MachineKind, id)))
                    changed++;
                mergedStorageRectangles.RemoveSourceRectangle(id);
                projectedMachineAnchors.Remove(id);
            }
        }
        currentMachineIds.Clear();
        staleMachineIds.Clear();
        dirtyMachineScratch.Clear();
        changed += SyncMergedStorageObstacleRectangles();
        return changed;
    }

    /// <summary>组合资格只读取既有存储能力，不绑定水、气、材质或库存组身份。</summary>
    internal static bool CanMergeAdjacentStorageCollision(MachineEntity node)
        => node?.Definition?.BlocksTraversal == true && node.Definition.Fluid?.CombineAdjacent == true &&
           MachineWorld.IsFluidTankBlock(node) && MachineWorld.CanBatchWorldFixedMachineBlockingRegions(node);

    /// <summary>每个节点保留源几何，组合储罐只投影精确合并后的实体阻挡。</summary>
    private int SyncMachineObstacleRectangle(MachineEntity node, out bool projected)
    {
        MachineWorld.ResolveRequiredWorldFixedMachineProxyMode(node);
        MachineCollisionBounds.ResolveWorldBox(node, out Vector2 worldCenter, out Vector2 halfExtents);
        long key = ChunkObstacleColliderSet.Key(ChunkObstacleColliderSet.MachineKind, node.Id);
        projected = TryResolveLocalBox(worldCenter, halfExtents, out Vector2 center, out Vector2 size);
        if (!projected)
        {
            mergedStorageRectangles.RemoveSourceRectangle(node.Id);
            return obstacleColliders.Remove(key) ? 1 : 0;
        }
        if (CanMergeAdjacentStorageCollision(node))
        {
            mergedStorageRectangles.SetSourceRectangle(node.Id, center, size);
            return obstacleColliders.Remove(key) ? 1 : 0;
        }
        mergedStorageRectangles.RemoveSourceRectangle(node.Id);
        return obstacleColliders.Set(key, center, size, SourceCollider, entity: node) ? 1 : 0;
    }

    /// <summary>本轮脏节点处理完才统一提交，库存和表现通知未改几何时直接返回。</summary>
    private int SyncMergedStorageObstacleRectangles()
    {
        if (!mergedStorageRectangles.RebuildMergedRectangles()) return 0;
        int changed = 0;
        projectedMergedStorageRectangleIds.Clear();
        IReadOnlyList<ChunkMergedObstacleRectangles.MergedObstacleRectangle> rectangles = mergedStorageRectangles.Rectangles;
        for (int i = 0; i < rectangles.Count; i++)
        {
            ChunkMergedObstacleRectangles.MergedObstacleRectangle rectangle = rectangles[i];
            projectedMergedStorageRectangleIds.Add(rectangle.Id);
            long key = ChunkObstacleColliderSet.Key(ChunkObstacleColliderSet.MergedStorageKind, rectangle.Id);
            if (obstacleColliders.Set(key, rectangle.Center, rectangle.Size, SourceCollider)) changed++;
        }
        changed += obstacleColliders.RemoveExcept(ChunkObstacleColliderSet.MergedStorageKind, projectedMergedStorageRectangleIds);
        return changed;
    }

    /// <summary>只撤销当前锚点已失效的机器，其他设备的物理形状保持原样。</summary>
    internal static void CollectStaleMachineIds(Dictionary<int, Vector2Int> anchors, Vector2Int changedCell,
        HashSet<int> liveIds, List<int> result)
    {
        result.Clear();
        foreach (KeyValuePair<int, Vector2Int> pair in anchors)
            if (pair.Value == changedCell && !liveIds.Contains(pair.Key))
                result.Add(pair.Key);
    }

    private int SyncObstacle(long key, Vector2 worldCenter, Vector2 halfExtents,
        IGameplayPhysicsDamageTarget damageTarget = null, object entity = null)
    {
        if (!TryResolveLocalBox(worldCenter, halfExtents, out Vector2 center, out Vector2 size))
            return obstacleColliders.Remove(key) ? 1 : 0;
        return obstacleColliders.Set(key, center, size, SourceCollider, damageTarget: damageTarget, entity: entity) ? 1 : 0;
    }

    /// <summary>保留既有矩形与跨 Chunk 裁剪语义，不改变数据占地、导航或受击形状。</summary>
    private bool TryResolveLocalBox(Vector2 worldCenter, Vector2 halfExtents, out Vector2 center, out Vector2 size)
    {
        Vector2 origin = new(BoundChunk.Address.ChunkOrigin.X, BoundChunk.Address.ChunkOrigin.Y);
        Vector2 local = WorldTopologyRuntime.ShortestDelta(origin, worldCenter);
        return TryClipLocalObstacleRectangle(local, halfExtents,
            new Vector2(BoundChunk.Terrain.Width, BoundChunk.Terrain.Height), out center, out size);
    }

    /// <summary>先沿区块裁剪每个真实矩形，再合并，跨区块和周期镜像仍各自持有完整局部并集。</summary>
    internal static bool TryClipLocalObstacleRectangle(Vector2 localCenter, Vector2 halfExtents,
        Vector2 chunkSize, out Vector2 center, out Vector2 size)
    {
        center = size = default;
        if (!(halfExtents.x > 0f && halfExtents.y > 0f)) return false;
        Vector2 min = Vector2.Max(Vector2.zero, localCenter - halfExtents);
        Vector2 max = Vector2.Min(chunkSize, localCenter + halfExtents);
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

    private void OnDestroy()
    {
        Unbind();
        obstacleColliders?.Dispose();
    }

    #endregion

    #region 世界碰撞队列所有权

    /// <summary>按绑定世界隔离脏批次，空世界只查字典，取批次后再次入队不会丢通知。</summary>
    internal sealed class ObstacleDirtyQueue
    {
        private readonly Dictionary<string, HashSet<WorldChunkPhysicsProjection2D>> worlds = new(System.StringComparer.Ordinal);
        private readonly Stack<HashSet<WorldChunkPhysicsProjection2D>> pool = new();

        internal bool HasPending(string worldKey) => worldKey != null &&
            worlds.TryGetValue(worldKey, out HashSet<WorldChunkPhysicsProjection2D> entries) && entries.Count > 0;

        internal void Add(string worldKey, WorldChunkPhysicsProjection2D projection)
        {
            if (worldKey == null || projection == null) return;
            if (!worlds.TryGetValue(worldKey, out HashSet<WorldChunkPhysicsProjection2D> entries))
            {
                entries = pool.Count > 0 ? pool.Pop() : new HashSet<WorldChunkPhysicsProjection2D>();
                worlds.Add(worldKey, entries);
            }
            entries.Add(projection);
        }

        internal void Remove(string worldKey, WorldChunkPhysicsProjection2D projection)
        {
            if (worldKey == null || !worlds.TryGetValue(worldKey, out HashSet<WorldChunkPhysicsProjection2D> entries)) return;
            entries.Remove(projection);
            if (entries.Count == 0) Release(worldKey, entries);
        }

        internal void Take(string worldKey, List<WorldChunkPhysicsProjection2D> output)
        {
            output.Clear();
            if (worldKey == null || !worlds.TryGetValue(worldKey, out HashSet<WorldChunkPhysicsProjection2D> entries)) return;
            foreach (WorldChunkPhysicsProjection2D projection in entries)
                if (projection != null) output.Add(projection);
            Release(worldKey, entries);
        }

        private void Release(string worldKey, HashSet<WorldChunkPhysicsProjection2D> entries)
        {
            worlds.Remove(worldKey);
            entries.Clear();
            pool.Push(entries);
        }

        internal void Clear()
        {
            worlds.Clear();
            pool.Clear();
        }
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
