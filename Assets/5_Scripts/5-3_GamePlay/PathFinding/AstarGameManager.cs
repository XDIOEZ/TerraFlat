using Pathfinding;
using Pathfinding.Graphs.Grid;
using Pathfinding.Jobs;
using Sirenix.OdinInspector;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Jobs;
using UnityEngine;
using Progress = Pathfinding.Progress;

public class AstarGameManager : SingletonAutoMono<AstarGameManager>
{
    #region 配置

    public AstarPath Pathfinder;
    public bool Init;
    public bool EnableDebugLogs;

    [Header("按键调权重配置")]
    public bool enableKeyControl = true;
    public int penaltyStep = 100;
    public int minPenalty;
    public int maxPenalty = 10000;
    public Camera mainCamera;

    [Header("增量导航更新")]
    [SerializeField, Min(0.01f)] private float dirtyUpdateInterval = 0.04f;
    [SerializeField, Min(64)] private int maxGridNodesPerAxis = 512;

    #endregion

    #region 运行时状态

    private readonly List<RectInt> pendingNavigationRects = new List<RectInt>(16);
    private readonly List<RectInt> navigationRectSnapshot = new List<RectInt>(16);
    private readonly Dictionary<Vector2Int, NavigationOverride> pendingOverrides = new Dictionary<Vector2Int, NavigationOverride>();
    private readonly List<DebugBounds> penaltyModifiedBounds = new List<DebugBounds>();
    private readonly List<DebugBounds> updatedBounds = new List<DebugBounds>();

    private bool navigationWorkItemQueued;
    private float nextDirtyUpdateTime;
    private bool isRefreshingNavigation;
    private bool processQueuedRefreshNextFrame;
    private bool hasQueuedRefreshRequest;
    private Vector2 queuedRefreshCenter;
    private int queuedRefreshRadius;
    private Action queuedRefreshOnComplete;
    private bool astarInitialized;
    private bool hasLoggedGridGraphNotReady;

    private GridGraphPenaltyAccess cachedGridGraphPenaltyAccess;
    private GridGraph cachedGridGraphPenaltyGraph;
    private GridNodeBase[] cachedGridGraphPenaltyNodes;
    private int cachedGridGraphPenaltyWidth;
    private int cachedGridGraphPenaltyDepth;
    private Vector3 cachedGridGraphPenaltyCenter;
    private float cachedGridGraphPenaltyNodeSize;
    private bool hasCachedGridGraphPenaltyAccess;

    private readonly struct NavigationOverride
    {
        public readonly uint Penalty;
        public readonly bool Walkable;

        public NavigationOverride(uint penalty, bool walkable)
        {
            Penalty = penalty;
            Walkable = walkable;
        }
    }

    public readonly struct GridGraphPenaltyAccess
    {
        public readonly GridNodeBase[] Nodes;
        public readonly int Width;
        public readonly int Depth;
        public readonly float Left;
        public readonly float Bottom;
        public readonly float InvNodeSize;
        public readonly int CellOriginX;
        public readonly int CellOriginY;
        public readonly bool UseDirectCellMapping;

        public GridGraphPenaltyAccess(
            GridNodeBase[] nodes,
            int width,
            int depth,
            float left,
            float bottom,
            float invNodeSize,
            int cellOriginX,
            int cellOriginY,
            bool useDirectCellMapping)
        {
            Nodes = nodes;
            Width = width;
            Depth = depth;
            Left = left;
            Bottom = bottom;
            InvNodeSize = invNodeSize;
            CellOriginX = cellOriginX;
            CellOriginY = cellOriginY;
            UseDirectCellMapping = useDirectCellMapping;
        }
    }

    private sealed class DebugBounds
    {
        public Bounds Bounds;
        public float Time;
        public bool IsKeyAdjust;
    }

    #endregion

    #region 生命周期

    public bool IsGridGraphReady
    {
        get
        {
            if (!TryGetGridGraph(out GridGraph gridGraph))
                return false;

            return gridGraph.isScanned &&
                   gridGraph.nodes != null &&
                   gridGraph.nodes.Length == gridGraph.width * gridGraph.depth;
        }
    }

    private void Start()
    {
        enabled = false;
        if (GameManager.Instance == null)
            return;

        GameManager.Instance.Event_GameWorldEnter += OnGameWorldEnter;
        GameManager.Instance.Event_GameWorldExit += OnGameWorldExit;
    }

    private void OnGameWorldEnter()
    {
        if (!astarInitialized)
        {
            InitializeAstar();
            astarInitialized = true;
        }

        enabled = true;
    }

    private void OnGameWorldExit()
    {
        enabled = false;
        pendingNavigationRects.Clear();
        navigationRectSnapshot.Clear();
        pendingOverrides.Clear();
        queuedRefreshOnComplete = null;
        navigationWorkItemQueued = false;
        isRefreshingNavigation = false;
        hasQueuedRefreshRequest = false;
        processQueuedRefreshNextFrame = false;
        InvalidateGridGraphPenaltyAccessCache();
    }

    protected override void OnDestroy()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.Event_GameWorldEnter -= OnGameWorldEnter;
            GameManager.Instance.Event_GameWorldExit -= OnGameWorldExit;
        }
    }

    private void Update()
    {
        UpdateKeyControl();

        if (processQueuedRefreshNextFrame ||
            (hasQueuedRefreshRequest && !isRefreshingNavigation && !navigationWorkItemQueued))
        {
            processQueuedRefreshNextFrame = false;
            ProcessQueuedRefreshRequest();
        }

        TrySchedulePendingNavigationUpdates();
    }

    private void InitializeAstar()
    {
        Pathfinder = GetComponent<AstarPath>();
        if (Pathfinder == null && AstarPath.active != null)
            Pathfinder = AstarPath.active;

        if (Pathfinder == null && GameRes.Instance != null)
        {
            GameObject astarPrefab = GameRes.Instance.InstantiatePrefab("AStar");
            Pathfinder = astarPrefab != null ? astarPrefab.GetComponent<AstarPath>() : null;
        }

        if (Pathfinder == null)
            Debug.LogError("[AstarGameManager] AstarPath 初始化失败", this);

        mainCamera ??= Camera.main;
    }

    #endregion

    #region 局部网格移动

    [Button("Update NavMesh")]
    public void RefreshNavMeshAsync(Vector2 center = default, int radius = 1, Action onComplete = null)
    {
        if (!TryGetGridGraph(out GridGraph gridGraph))
        {
            Debug.LogError("[AstarGameManager] 无法刷新导航：GridGraph 尚未初始化", this);
            onComplete?.Invoke();
            return;
        }

        if (isRefreshingNavigation || navigationWorkItemQueued || AstarPath.active.isScanning)
        {
            QueueRefreshRequest(center, radius, onComplete);
            return;
        }

        radius = Mathf.Max(1, radius);
        Vector2 chunkSize = ChunkMgr.GetChunkSize();
        Vector3 targetCenter = new Vector3(
            center.x + chunkSize.x * 0.5f,
            center.y + chunkSize.y * 0.5f,
            0f);

        int requestedWidth = Mathf.RoundToInt(chunkSize.x * (2 * radius - 1));
        int requestedDepth = Mathf.RoundToInt(chunkSize.y * (2 * radius - 1));
        int targetWidth = Mathf.Clamp(requestedWidth, 1, maxGridNodesPerAxis);
        int targetDepth = Mathf.Clamp(requestedDepth, 1, maxGridNodesPerAxis);

        if (EnableDebugLogs && (requestedWidth != targetWidth || requestedDepth != targetDepth))
        {
            Debug.LogWarning(
                $"[AstarGameManager] 导航窗口已限制为 {targetWidth}x{targetDepth}，请求值={requestedWidth}x{requestedDepth}",
                this);
        }

        gridGraph.collision.collisionCheck = false;
        gridGraph.collision.heightCheck = false;

        bool requiresInitialScan = !gridGraph.isScanned ||
                                   gridGraph.nodes == null ||
                                   gridGraph.nodes.Length != gridGraph.width * gridGraph.depth ||
                                   gridGraph.width != targetWidth ||
                                   gridGraph.depth != targetDepth ||
                                   !Mathf.Approximately(gridGraph.nodeSize, 1f);

        if (requiresInitialScan)
        {
            BeginInitialScan(gridGraph, targetCenter, targetWidth, targetDepth, onComplete);
            return;
        }

        BeginIncrementalMove(gridGraph, targetCenter, onComplete);
    }

    private void BeginInitialScan(
        GridGraph gridGraph,
        Vector3 targetCenter,
        int targetWidth,
        int targetDepth,
        Action onComplete)
    {
        isRefreshingNavigation = true;
        gridGraph.center = targetCenter;
        gridGraph.SetDimensions(targetWidth, targetDepth, 1f);
        InvalidateGridGraphPenaltyAccessCache();
        StartCoroutine(HandleInitialScan(AstarPath.active.ScanAsync(gridGraph), onComplete));
    }

    private IEnumerator HandleInitialScan(IEnumerable<Progress> progressEnumerable, Action onComplete)
    {
        foreach (Progress _ in progressEnumerable)
            yield return null;

        InvalidateGridGraphPenaltyAccessCache();
        QueueEntireGrid();
        FlushPendingNavigationUpdates(() => FinishRefresh(onComplete));
    }

    private void BeginIncrementalMove(GridGraph gridGraph, Vector3 targetCenter, Action onComplete)
    {
        Vector3 graphDelta = gridGraph.transform.InverseTransformVector(targetCenter - gridGraph.center);
        int dx = Mathf.RoundToInt(graphDelta.x);
        int dz = Mathf.RoundToInt(graphDelta.z);

        isRefreshingNavigation = true;
        if (dx == 0 && dz == 0)
        {
            FlushPendingNavigationUpdates(() => FinishRefresh(onComplete));
            return;
        }

        List<(IGraphUpdatePromise, IEnumerator<JobHandle>)> promises =
            new List<(IGraphUpdatePromise, IEnumerator<JobHandle>)>(1);

        navigationWorkItemQueued = true;
        AstarPath.active.AddWorkItem(new AstarWorkItem(
            context =>
            {
                IGraphUpdatePromise promise = gridGraph.TranslateInDirection(dx, dz);
                promises.Add((promise, promise.Prepare()));
            },
            (context, force) =>
            {
                TimeSlice timeSlice = force ? TimeSlice.Infinite : TimeSlice.MillisFromNow(2f);
                if (GraphUpdateProcessor.ProcessGraphUpdatePromises(promises, context, timeSlice) != -1)
                    return false;

                InvalidateGridGraphPenaltyAccessCache();
                QueueMovedGridInsets(gridGraph, dx, dz);
                ApplyPendingNavigationUpdatesInsideWorkItem(gridGraph);
                navigationWorkItemQueued = false;
                FinishRefresh(onComplete);
                return true;
            }));
    }

    private void FinishRefresh(Action onComplete)
    {
        Init = true;
        onComplete?.Invoke();
        isRefreshingNavigation = false;
        if (hasQueuedRefreshRequest)
            processQueuedRefreshNextFrame = true;
    }

    private void QueueRefreshRequest(Vector2 center, int radius, Action onComplete)
    {
        hasQueuedRefreshRequest = true;
        queuedRefreshCenter = center;
        queuedRefreshRadius = Mathf.Max(1, radius);
        queuedRefreshOnComplete += onComplete;
    }

    private void ProcessQueuedRefreshRequest()
    {
        if (!hasQueuedRefreshRequest || isRefreshingNavigation || navigationWorkItemQueued)
            return;

        hasQueuedRefreshRequest = false;
        Vector2 center = queuedRefreshCenter;
        int radius = queuedRefreshRadius;
        Action onComplete = queuedRefreshOnComplete;
        queuedRefreshOnComplete = null;
        RefreshNavMeshAsync(center, radius, onComplete);
    }

    [Button("Update NavMesh Sync")]
    public void UpdateMeshSync(Vector2 center = default, int radius = 1)
    {
        if (!TryGetGridGraph(out GridGraph gridGraph))
            return;

        radius = Mathf.Max(1, radius);
        Vector2 chunkSize = ChunkMgr.GetChunkSize();
        gridGraph.center = new Vector3(center.x + chunkSize.x * 0.5f, center.y + chunkSize.y * 0.5f, 0f);
        gridGraph.SetDimensions(
            Mathf.Clamp(Mathf.RoundToInt(chunkSize.x * (2 * radius - 1)), 1, maxGridNodesPerAxis),
            Mathf.Clamp(Mathf.RoundToInt(chunkSize.y * (2 * radius - 1)), 1, maxGridNodesPerAxis),
            1f);
        gridGraph.collision.collisionCheck = false;
        gridGraph.collision.heightCheck = false;

        AstarPath.active.Scan(gridGraph);
        InvalidateGridGraphPenaltyAccessCache();
        QueueEntireGrid();
        navigationWorkItemQueued = true;
        AstarPath.active.AddWorkItem(() =>
        {
            ApplyPendingNavigationUpdatesInsideWorkItem(gridGraph);
            navigationWorkItemQueued = false;
            Init = true;
        });
        AstarPath.active.FlushWorkItems();
    }

    #endregion

    #region 脏区队列

    public void QueueNavigationCell(Vector2Int worldCell)
    {
        QueueNavigationRegion(new RectInt(worldCell.x, worldCell.y, 1, 1));
    }

    public void QueueNavigationRegion(RectInt worldRect)
    {
        if (worldRect.width <= 0 || worldRect.height <= 0)
            return;

        RectInt merged = worldRect;
        for (int i = pendingNavigationRects.Count - 1; i >= 0; i--)
        {
            RectInt current = pendingNavigationRects[i];
            if (!CanMergeWithoutAreaInflation(current, merged))
                continue;

            merged = Union(current, merged);
            pendingNavigationRects.RemoveAt(i);
        }

        pendingNavigationRects.Add(merged);
    }

    private void QueueEntireGrid()
    {
        if (TryGetGridGraph(out GridGraph gridGraph))
            QueueNavigationRegion(GetGridWorldRect(gridGraph));
    }

    private void QueueMovedGridInsets(GridGraph gridGraph, int dx, int dz)
    {
        if (Mathf.Abs(dx) > gridGraph.width / 2 || Mathf.Abs(dz) > gridGraph.depth / 2)
        {
            QueueNavigationRegion(GetGridWorldRect(gridGraph));
            return;
        }

        RectInt gridRect = GetGridWorldRect(gridGraph);
        int insetLeft = Mathf.Min(gridRect.width, Mathf.Max(1, -dx));
        int insetRight = Mathf.Min(gridRect.width, Mathf.Max(1, dx));
        int insetBottom = Mathf.Min(gridRect.height, Mathf.Max(1, -dz));
        int insetTop = Mathf.Min(gridRect.height, Mathf.Max(1, dz));

        QueueNavigationRegion(new RectInt(gridRect.xMin, gridRect.yMin, insetLeft, gridRect.height));
        QueueNavigationRegion(new RectInt(gridRect.xMax - insetRight, gridRect.yMin, insetRight, gridRect.height));

        int middleWidth = Mathf.Max(0, gridRect.width - insetLeft - insetRight);
        if (middleWidth <= 0)
            return;

        QueueNavigationRegion(new RectInt(gridRect.xMin + insetLeft, gridRect.yMin, middleWidth, insetBottom));
        QueueNavigationRegion(new RectInt(gridRect.xMin + insetLeft, gridRect.yMax - insetTop, middleWidth, insetTop));
    }

    private void TrySchedulePendingNavigationUpdates()
    {
        if (navigationWorkItemQueued || isRefreshingNavigation || pendingNavigationRects.Count == 0)
            return;
        if (!IsGridGraphReady || Time.unscaledTime < nextDirtyUpdateTime)
            return;

        FlushPendingNavigationUpdates();
    }

    private void FlushPendingNavigationUpdates(Action onComplete = null)
    {
        if (pendingNavigationRects.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        if (navigationWorkItemQueued || !TryGetGridGraph(out GridGraph gridGraph) || !gridGraph.isScanned)
        {
            onComplete?.Invoke();
            return;
        }

        navigationWorkItemQueued = true;
        nextDirtyUpdateTime = Time.unscaledTime + dirtyUpdateInterval;
        AstarPath.active.AddWorkItem(() =>
        {
            ApplyPendingNavigationUpdatesInsideWorkItem(gridGraph);
            navigationWorkItemQueued = false;
            onComplete?.Invoke();
        });
    }

    private void ApplyPendingNavigationUpdatesInsideWorkItem(GridGraph gridGraph)
    {
        if (pendingNavigationRects.Count == 0)
            return;

        navigationRectSnapshot.Clear();
        navigationRectSnapshot.AddRange(pendingNavigationRects);
        pendingNavigationRects.Clear();

        RectInt gridWorldRect = GetGridWorldRect(gridGraph);
        for (int i = 0; i < navigationRectSnapshot.Count; i++)
        {
            RectInt worldRect = Intersect(navigationRectSnapshot[i], gridWorldRect);
            if (worldRect.width > 0 && worldRect.height > 0)
                ApplyNavigationWorldRect(gridGraph, gridWorldRect, worldRect);
        }

        InvalidateGridGraphPenaltyAccessCache();
    }

    private void ApplyNavigationWorldRect(GridGraph gridGraph, RectInt gridWorldRect, RectInt worldRect)
    {
        int graphXMin = worldRect.xMin - gridWorldRect.xMin;
        int graphYMin = worldRect.yMin - gridWorldRect.yMin;
        int graphXMaxExclusive = graphXMin + worldRect.width;
        int graphYMaxExclusive = graphYMin + worldRect.height;

        Vector2 chunkSize = ChunkMgr.GetChunkSize();
        int chunkStepX = Mathf.Max(1, Mathf.RoundToInt(chunkSize.x));
        int chunkStepY = Mathf.Max(1, Mathf.RoundToInt(chunkSize.y));
        Chunk cachedChunk = null;
        Vector2Int cachedChunkPosition = new Vector2Int(int.MinValue, int.MinValue);

        for (int graphY = graphYMin; graphY < graphYMaxExclusive; graphY++)
        {
            int worldY = gridWorldRect.yMin + graphY;
            int rowOffset = graphY * gridGraph.width;

            for (int graphX = graphXMin; graphX < graphXMaxExclusive; graphX++)
            {
                int worldX = gridWorldRect.xMin + graphX;
                Vector2Int worldCell = new Vector2Int(worldX, worldY);
                Vector2Int chunkPosition = new Vector2Int(
                    Mathf.FloorToInt((worldX + 0.5f) / chunkSize.x) * chunkStepX,
                    Mathf.FloorToInt((worldY + 0.5f) / chunkSize.y) * chunkStepY);

                if (chunkPosition != cachedChunkPosition)
                {
                    cachedChunkPosition = chunkPosition;
                    cachedChunk = null;
                    ChunkMgr.Instance?.TryGetActiveChunkByPos(chunkPosition, out cachedChunk);
                }

                uint penalty = 0u;
                bool walkable = false;
                if (pendingOverrides.TryGetValue(worldCell, out NavigationOverride navigationOverride))
                {
                    penalty = navigationOverride.Penalty;
                    walkable = navigationOverride.Walkable;
                    pendingOverrides.Remove(worldCell);
                }
                else
                {
                    List<TileData> tiles = cachedChunk?.Map?.Data?.GetTileListAt(worldCell);
                    if (tiles != null && tiles.Count > 0)
                    {
                        TileData topTile = tiles[^1];
                        penalty = topTile.Penalty;
                        walkable = BuildingOccupancyRegistry.GetEffectiveWalkable(worldCell, topTile.IsWalkable);
                    }
                }

                ApplyNodePenaltyFast(gridGraph.nodes[rowOffset + graphX], penalty, walkable);
            }
        }

        gridGraph.RecalculateConnectionsInRegion(new IntRect(
            graphXMin - 1,
            graphYMin - 1,
            graphXMaxExclusive,
            graphYMaxExclusive));
    }

    private static RectInt GetGridWorldRect(GridGraph gridGraph)
    {
        int minX = Mathf.RoundToInt(gridGraph.center.x - gridGraph.width * gridGraph.nodeSize * 0.5f);
        int minY = Mathf.RoundToInt(gridGraph.center.y - gridGraph.depth * gridGraph.nodeSize * 0.5f);
        return new RectInt(minX, minY, gridGraph.width, gridGraph.depth);
    }

    private static bool CanMergeWithoutAreaInflation(RectInt a, RectInt b)
    {
        RectInt union = Union(a, b);
        RectInt intersection = Intersect(a, b);
        int exactArea = a.width * a.height + b.width * b.height - intersection.width * intersection.height;
        if (union.width * union.height == exactArea)
            return true;

        bool horizontalNeighbours = a.yMin == b.yMin && a.yMax == b.yMax &&
                                    (a.xMax == b.xMin || b.xMax == a.xMin);
        bool verticalNeighbours = a.xMin == b.xMin && a.xMax == b.xMax &&
                                  (a.yMax == b.yMin || b.yMax == a.yMin);
        return horizontalNeighbours || verticalNeighbours;
    }

    private static RectInt Union(RectInt a, RectInt b)
    {
        int xMin = Mathf.Min(a.xMin, b.xMin);
        int yMin = Mathf.Min(a.yMin, b.yMin);
        int xMax = Mathf.Max(a.xMax, b.xMax);
        int yMax = Mathf.Max(a.yMax, b.yMax);
        return new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
    }

    private static RectInt Intersect(RectInt a, RectInt b)
    {
        int xMin = Mathf.Max(a.xMin, b.xMin);
        int yMin = Mathf.Max(a.yMin, b.yMin);
        int xMax = Mathf.Min(a.xMax, b.xMax);
        int yMax = Mathf.Min(a.yMax, b.yMax);
        return xMax <= xMin || yMax <= yMin
            ? new RectInt()
            : new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
    }

    #endregion

    #region 节点映射和查询

    private bool TryGetGridGraph(out GridGraph gridGraph)
    {
        AstarPath active = AstarPath.active;
        gridGraph = active != null && active.data != null ? active.data.gridGraph : null;
        return gridGraph != null;
    }

    private void InvalidateGridGraphPenaltyAccessCache()
    {
        hasCachedGridGraphPenaltyAccess = false;
        cachedGridGraphPenaltyGraph = null;
        cachedGridGraphPenaltyNodes = null;
    }

    public bool TryGetGridGraphPenaltyAccess(out GridGraphPenaltyAccess access)
    {
        access = default;
        if (!TryGetGridGraph(out GridGraph gridGraph))
            return false;

        GridNodeBase[] nodes = gridGraph.nodes;
        int width = gridGraph.width;
        int depth = gridGraph.depth;
        if (nodes == null || nodes.Length != width * depth)
        {
            if (!hasLoggedGridGraphNotReady && EnableDebugLogs)
            {
                hasLoggedGridGraphNotReady = true;
                Debug.LogWarning("[AstarGameManager] GridGraph 尚未就绪", this);
            }
            return false;
        }

        float nodeSize = gridGraph.nodeSize;
        if (nodeSize <= 0f)
            return false;

        hasLoggedGridGraphNotReady = false;
        Vector3 center = gridGraph.center;
        if (!hasCachedGridGraphPenaltyAccess ||
            cachedGridGraphPenaltyGraph != gridGraph ||
            cachedGridGraphPenaltyNodes != nodes ||
            cachedGridGraphPenaltyWidth != width ||
            cachedGridGraphPenaltyDepth != depth ||
            cachedGridGraphPenaltyCenter != center ||
            !Mathf.Approximately(cachedGridGraphPenaltyNodeSize, nodeSize))
        {
            float left = center.x - width * nodeSize * 0.5f;
            float bottom = center.y - depth * nodeSize * 0.5f;
            int cellOriginX = Mathf.RoundToInt(left);
            int cellOriginY = Mathf.RoundToInt(bottom);
            bool directMapping = Mathf.Approximately(nodeSize, 1f) &&
                                 Mathf.Abs(left - cellOriginX) < 0.0001f &&
                                 Mathf.Abs(bottom - cellOriginY) < 0.0001f;

            cachedGridGraphPenaltyAccess = new GridGraphPenaltyAccess(
                nodes,
                width,
                depth,
                left,
                bottom,
                1f / nodeSize,
                cellOriginX,
                cellOriginY,
                directMapping);
            cachedGridGraphPenaltyGraph = gridGraph;
            cachedGridGraphPenaltyNodes = nodes;
            cachedGridGraphPenaltyWidth = width;
            cachedGridGraphPenaltyDepth = depth;
            cachedGridGraphPenaltyCenter = center;
            cachedGridGraphPenaltyNodeSize = nodeSize;
            hasCachedGridGraphPenaltyAccess = true;
        }

        access = cachedGridGraphPenaltyAccess;
        return true;
    }

    private static bool TryGetGridGraphNode(GridGraphPenaltyAccess access, float worldX, float worldY, out GridNodeBase node)
    {
        int x = Mathf.FloorToInt((worldX - access.Left) * access.InvNodeSize);
        int y = Mathf.FloorToInt((worldY - access.Bottom) * access.InvNodeSize);
        if ((uint)x >= (uint)access.Width || (uint)y >= (uint)access.Depth)
        {
            node = null;
            return false;
        }

        node = access.Nodes[x + y * access.Width];
        return node != null;
    }

    public bool TryGetNodePenalty_GridGraphFast(Vector2 worldPosition, out uint penalty, out bool walkable)
    {
        penalty = 0u;
        walkable = false;
        if (!TryGetGridGraphPenaltyAccess(out GridGraphPenaltyAccess access) ||
            !TryGetGridGraphNode(access, worldPosition.x, worldPosition.y, out GridNodeBase node))
        {
            return false;
        }

        penalty = node.Penalty;
        walkable = node.Walkable;
        return true;
    }

    private static void ApplyNodePenaltyFast(GridNodeBase node, uint penalty, bool walkable)
    {
        if (node == null)
            return;

        bool targetWalkable = walkable && penalty > 0u;
        uint targetPenalty = targetWalkable ? penalty : 0u;
        if (node.Walkable == targetWalkable && node.Penalty == targetPenalty)
            return;

        node.Walkable = targetWalkable;
        node.Penalty = targetPenalty;
    }

    #endregion

    #region 兼容节点修改 API

    internal void ModifyNodePenalty_Internal(Vector2Int nodePosition, uint penalty)
    {
        if (!TryGetGridGraphPenaltyAccess(out GridGraphPenaltyAccess access))
            return;
        if ((uint)nodePosition.x >= (uint)access.Width || (uint)nodePosition.y >= (uint)access.Depth)
            return;

        Vector2Int worldCell = new Vector2Int(access.CellOriginX + nodePosition.x, access.CellOriginY + nodePosition.y);
        QueueNavigationOverride(worldCell, penalty, penalty > 0u);
    }

    [Button("修改单个节点权重")]
    public void ModifyNodePenalty(Vector3 worldPosition, uint newPenalty = 1000)
    {
        QueueNavigationOverride(
            new Vector2Int(Mathf.FloorToInt(worldPosition.x), Mathf.FloorToInt(worldPosition.y)),
            newPenalty,
            newPenalty > 0u);
    }

    public void ModifyNodePenalty_Optimized(Vector2 worldPosition, uint newPenalty = 1000, bool isWalkable = true)
    {
        QueueNavigationOverride(
            new Vector2Int(Mathf.FloorToInt(worldPosition.x), Mathf.FloorToInt(worldPosition.y)),
            newPenalty,
            isWalkable);
    }

    public void ModifyNodePenalty_GridGraphFast(Vector2 worldPosition, uint newPenalty = 1000, bool isWalkable = true)
    {
        ModifyNodePenalty_Optimized(worldPosition, newPenalty, isWalkable);
    }

    public void ModifyNodePenalty_GridGraphFast(
        GridGraphPenaltyAccess access,
        Vector2 worldPosition,
        uint newPenalty = 1000,
        bool isWalkable = true)
    {
        ModifyNodePenalty_Optimized(worldPosition, newPenalty, isWalkable);
    }

    public void ModifyNodePenalty_GridGraphFast(
        GridGraphPenaltyAccess access,
        Vector2Int cellPosition,
        uint newPenalty = 1000,
        bool isWalkable = true)
    {
        QueueNavigationOverride(cellPosition, newPenalty, isWalkable);
    }

    private void QueueNavigationOverride(Vector2Int worldCell, uint penalty, bool walkable)
    {
        pendingOverrides[worldCell] = new NavigationOverride(penalty, walkable);
        QueueNavigationCell(worldCell);
    }

    [Button("修改区域权重")]
    public void ModifyRegionPenalty(Vector2 center, int sizeX, int sizeY, int penaltyDelta = 500)
    {
        if (AstarPath.active == null)
            return;

        Bounds bounds = new Bounds(new Vector3(center.x, center.y, 0f), new Vector3(sizeX, sizeY, 1f));
        GraphUpdateObject update = new GraphUpdateObject(bounds)
        {
            modifyWalkability = false,
            addPenalty = penaltyDelta
        };
        AstarPath.active.UpdateGraphs(update);
        penaltyModifiedBounds.Add(new DebugBounds { Bounds = bounds, Time = Time.time });
    }

    [Button("更新特定区块权重")]
    public void UpdateAreaPenalty_Rectangle(
        Vector2 center,
        int length,
        int width,
        int penaltyValue = 500,
        bool setAbsolute = false)
    {
        RectInt rect = CreateWorldRect(center, length, width);
        if (!setAbsolute)
        {
            ModifyRegionPenalty(center, length, width, penaltyValue);
            return;
        }

        uint penalty = (uint)Mathf.Clamp(penaltyValue, minPenalty, maxPenalty);
        for (int x = rect.xMin; x < rect.xMax; x++)
        {
            for (int y = rect.yMin; y < rect.yMax; y++)
                pendingOverrides[new Vector2Int(x, y)] = new NavigationOverride(penalty, penalty > 0u);
        }
        QueueNavigationRegion(rect);
    }

    public void UpdateArea_Rectangle(Vector2 center, int length, int width)
    {
        RectInt rect = CreateWorldRect(center, length, width);
        QueueNavigationRegion(rect);
        updatedBounds.Add(new DebugBounds
        {
            Bounds = new Bounds(new Vector3(center.x, center.y, 0f), new Vector3(length, width, 1f)),
            Time = Time.time
        });
    }

    public void UpdateArea_Rectangle_Sync(Vector2 center, int length, int width)
    {
        UpdateArea_Rectangle(center, length, width);
        if (AstarPath.active == null || !TryGetGridGraph(out GridGraph gridGraph))
            return;

        navigationWorkItemQueued = true;
        AstarPath.active.AddWorkItem(() =>
        {
            ApplyPendingNavigationUpdatesInsideWorkItem(gridGraph);
            navigationWorkItemQueued = false;
        });
        AstarPath.active.FlushWorkItems();
    }

    private static RectInt CreateWorldRect(Vector2 center, int width, int height)
    {
        int xMin = Mathf.FloorToInt(center.x - width * 0.5f);
        int yMin = Mathf.FloorToInt(center.y - height * 0.5f);
        return new RectInt(xMin, yMin, Mathf.Max(1, width), Mathf.Max(1, height));
    }

    #endregion

    #region 调试

    private void UpdateKeyControl()
    {
        if (!enableKeyControl || mainCamera == null || AstarPath.active == null)
            return;

        int delta = 0;
        if (Input.GetKeyDown(KeyCode.Plus) || Input.GetKeyDown(KeyCode.KeypadPlus))
            delta = penaltyStep;
        else if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
            delta = -penaltyStep;

        if (delta == 0)
            return;

        Vector3 mousePosition = Input.mousePosition;
        mousePosition.z = Mathf.Abs(mainCamera.transform.position.z);
        Vector3 worldPosition = mainCamera.ScreenToWorldPoint(mousePosition);
        if (!TryGetNodePenalty_GridGraphFast(worldPosition, out uint currentPenalty, out bool walkable) || !walkable)
            return;

        uint nextPenalty = (uint)Mathf.Clamp((int)currentPenalty + delta, minPenalty, maxPenalty);
        Vector2Int cell = new Vector2Int(Mathf.FloorToInt(worldPosition.x), Mathf.FloorToInt(worldPosition.y));
        QueueNavigationOverride(cell, nextPenalty, true);
        penaltyModifiedBounds.Add(new DebugBounds
        {
            Bounds = new Bounds(new Vector3(cell.x + 0.5f, cell.y + 0.5f, 0f), Vector3.one * 0.8f),
            Time = Time.time,
            IsKeyAdjust = true
        });
    }

    private void OnDrawGizmos()
    {
        DrawDebugBounds(updatedBounds, Color.red, 10f);
        DrawDebugBounds(penaltyModifiedBounds, Color.green, 10f);
    }

    private static void DrawDebugBounds(List<DebugBounds> boundsList, Color defaultColor, float lifetime)
    {
        if (boundsList == null)
            return;

        for (int i = boundsList.Count - 1; i >= 0; i--)
        {
            DebugBounds debugBounds = boundsList[i];
            if (Time.time - debugBounds.Time > lifetime)
            {
                boundsList.RemoveAt(i);
                continue;
            }

            Gizmos.color = debugBounds.IsKeyAdjust ? Color.yellow : defaultColor;
            Gizmos.DrawWireCube(debugBounds.Bounds.center, debugBounds.Bounds.size);
        }
    }

    #endregion
}
