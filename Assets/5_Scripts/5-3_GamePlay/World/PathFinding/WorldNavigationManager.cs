using System;
using System.Collections.Generic;
using System.Diagnostics;
using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.SceneManagement;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

public readonly struct WorldNavigationPathResult
{
    public readonly int RequestId;
    public readonly bool Success;
    public readonly Vector2 RequestedDestination;
    public readonly Vector2 ResolvedDestination;
    public readonly Vector2[] Waypoints;
    public readonly bool ReachesDestination;
    /// <summary>带权导航用于本次路径结果判定的总代价。</summary>
    public readonly int TotalCost;
    public readonly int GridRevision;
    public readonly int PathCostRevision;

    public WorldNavigationPathResult(
        int requestId,
        bool success,
        Vector2 requestedDestination,
        Vector2 resolvedDestination,
        Vector2[] waypoints,
        bool reachesDestination,
        int totalCost,
        int gridRevision,
        int pathCostRevision)
    {
        RequestId = requestId;
        Success = success;
        RequestedDestination = requestedDestination;
        ResolvedDestination = resolvedDestination;
        Waypoints = waypoints ?? Array.Empty<Vector2>();
        ReachesDestination = reachesDestination;
        TotalCost = totalCost;
        GridRevision = gridRevision;
        PathCostRevision = pathCostRevision;
    }
}

/// <summary>
/// Navigation service for the infinite chunk world. Loaded maps contribute sparse cells using
/// absolute coordinates. Path searches are time-sliced and agents sharing a goal share one
/// reverse flow-field search.
/// </summary>
public sealed class WorldNavigationManager : SingletonAutoMono<WorldNavigationManager>
{
    private const int MaxPooledMapCellSets = 32;

    [Header("分帧寻路预算")]
    [SerializeField, Min(128)] private int nodeExpansionBudgetPerFrame = 2500;
    [SerializeField, Min(0.1f)] private float maxPathMillisecondsPerFrame = 2f;
    [SerializeField, Min(1024)] private int maxExpandedCellsPerGoal = 65536;
    [SerializeField, Min(16384)] private int maxTotalCachedSearchCells = 262144;
    [SerializeField, Min(1)] private int maxCompletionsPerFrame = 256;
    [SerializeField, Min(0.05f)] private float maxCompletionMillisecondsPerFrame = 0.5f;
    [SerializeField, Min(1)] private int maxPathBuildsPerExpansion = 16;
    [SerializeField, Min(2)] private int maxPathCellsPerResult = 64;
    [SerializeField, Min(16)] private int maxBufferedCompletions = 1024;
    [SerializeField, Min(1)] private int maxDirectLineOfSightCells = 64;

    [Header("共享目标缓存")]
    [SerializeField, Min(1)] private int maxCachedGoalFields = 64;
    [SerializeField, Min(1)] private int maxConcurrentGoalSearches = 8;
    [SerializeField, Min(0.1f)] private float goalFieldLifetime = 3f;
    [SerializeField, Min(0)] private int nearestWalkableSearchRadius = 4;

    [Header("调试")]
    public bool EnableDebugLogs;

    private readonly WorldNavigationGrid grid = new();
    private readonly Dictionary<int, HashSet<Vector2Int>> cellsByMap = new(128);
    private readonly Dictionary<RuntimeWorldAddress, HashSet<Vector2Int>> cellsByRuntimeChunk = new();
    private readonly Dictionary<Vector2Int, RuntimeWorldAddress> runtimeTerrainOwnerByCell = new(8192);
    private readonly Stack<HashSet<Vector2Int>> mapCellSetPool = new(32);
    private readonly Dictionary<Vector2Int, int> terrainOwnerByCell = new(8192);
    private readonly Dictionary<Vector2Int, GoalField> fieldsByGoal = new(32);
    private readonly List<GoalField> fieldSchedule = new(32);
    private readonly Dictionary<int, PathRequest> requests = new(256);
    private readonly LinkedList<CompletedRequest> completions = new();
    private readonly Dictionary<int, LinkedListNode<CompletedRequest>> completionNodes = new(256);
    private readonly LinkedList<int> admissionQueue = new();
    private readonly List<PathRequest> revisionRequeueBuffer = new(128);
    private readonly List<int> requestIdBuffer = new(128);
    private readonly List<Vector2Int> gridChangeBuffer = new(256);
    private readonly Stopwatch pathStopwatch = new();

    private int nextRequestId = 1;
    private int observedGridRevision;
    private int scheduleIndex;
    private string activeWorldKey;

    public bool Init { get; private set; }
    public static WorldNavigationManager ExistingInstance { get; private set; }
    public bool IsNavigationReady => Init && grid.CellCount > 0;
    public int GridRevision => grid.Revision;
    public int PathInvalidationRevision => grid.PathInvalidationRevision;
    public int PathCostRevision => grid.PathCostRevision;
    public int RegisteredCellCount => grid.CellCount;
    public int PendingPathCount => requests.Count;
    public int QueuedAdmissionCount => admissionQueue.Count;
    public int BufferedCompletionCount => completions.Count;
    public int CachedGoalCount => fieldsByGoal.Count;
    public string ActiveWorldKey => activeWorldKey;
    public WorldNavigationGrid Grid => grid;

    protected override void Awake()
    {
        base.Awake();
        if (Instance != this)
            return;

        ExistingInstance = this;
        observedGridRevision = grid.Revision;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
    }

    private void Start()
    {
        GameManager gameManager = GameManager.Instance;
        if (gameManager != null)
        {
            gameManager.Event_GameWorldEnter += OnGameWorldEnter;
            gameManager.Event_GameWorldExit += OnGameWorldExit;
            Init = gameManager.IsInGameWorld;
        }

        enabled = Init;
        if (Init)
        {
            CaptureActiveWorldKey();
            RegisterActiveMaps();
        }
    }

    protected override void OnDestroy()
    {
        if (ExistingInstance == this)
            ExistingInstance = null;

        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        GameManager gameManager = FindObjectOfType<GameManager>();
        if (gameManager != null)
        {
            gameManager.Event_GameWorldEnter -= OnGameWorldEnter;
            gameManager.Event_GameWorldExit -= OnGameWorldExit;
        }

        FailAllRequests();
        RecycleAllMapCellSets();
        base.OnDestroy();
    }

    private void Update()
    {
        pathStopwatch.Restart();
        SynchronizeGridRevision();
        PruneGoalFields();
        EnforceCacheCellBudget();

        int remaining = Mathf.Max(1, nodeExpansionBudgetPerFrame);
        while (remaining > 0 &&
               pathStopwatch.Elapsed.TotalMilliseconds < Mathf.Max(0.1f, maxPathMillisecondsPerFrame))
        {
            int processed = ProcessPathRequests(1);
            if (processed <= 0)
                break;
            remaining -= processed;
        }
        pathStopwatch.Stop();

        pathStopwatch.Restart();
        DispatchCompletions(
            Mathf.Max(1, maxCompletionsPerFrame),
            Mathf.Max(0.05f, maxCompletionMillisecondsPerFrame));
        pathStopwatch.Stop();
    }

    private void OnGameWorldEnter()
    {
        Init = true;
        enabled = true;
        CaptureActiveWorldKey();
        RegisterActiveMaps();
    }

    private void OnGameWorldExit()
    {
        Init = false;
        FailAllRequests();
        fieldsByGoal.Clear();
        fieldSchedule.Clear();
        admissionQueue.Clear();
        grid.Clear();
        RecycleAllMapCellSets();
        terrainOwnerByCell.Clear();
        cellsByRuntimeChunk.Clear();
        runtimeTerrainOwnerByCell.Clear();
        observedGridRevision = grid.Revision;
        activeWorldKey = string.Empty;
        enabled = false;
    }

    #region Grid registration

    public void RegisterMap(Map map)
    {
        if (map?.chunk == null || ChunkMgr.Instance == null)
            return;
        RuntimeWorldAddress address = ChunkMgr.Instance.ResolveWorldAddress(map.chunk.transform.position);
        if (ChunkMgr.Instance.TryGetChunkRuntime(address, out ChunkRuntime runtimeChunk))
            RegisterChunkRuntime(runtimeChunk);
    }

    public void UnregisterMap(Map map)
    {
        if (map?.chunk == null || ChunkMgr.Instance == null)
            return;
        UnregisterChunkRuntime(ChunkMgr.Instance.ResolveWorldAddress(map.chunk.transform.position));
    }

    public void RegisterChunkRuntime(ChunkRuntime chunk)
    {
        if (chunk?.Terrain == null || chunk.DataStatus != ChunkDataStatus.Ready)
            return;

        UnregisterChunkRuntime(chunk.Address);
        var ownedCells = new HashSet<Vector2Int>();
        ChunkTerrainData terrain = chunk.Terrain;
        grid.BeginBatchUpdate();
        try
        {
            for (int y = 0; y < terrain.Height; y++)
            {
                for (int x = 0; x < terrain.Width; x++)
                {
                    Vector2Int worldCell = WorldNavigationGrid.NormalizeCell(new Vector2Int(
                        chunk.Address.ChunkOrigin.X + x,
                        chunk.Address.ChunkOrigin.Y + y));
                    TerrainCell terrainCell = terrain.GetCell(x, y);
                    bool walkable = (terrainCell.Flags & TerrainCellFlags.Walkable) != 0 &&
                                    (terrainCell.Flags & TerrainCellFlags.Blocking) == 0;
                    walkable = BuildingOccupancyRegistry.GetEffectiveWalkable(worldCell, walkable);
                    uint penalty = walkable ? (uint)Mathf.Max(1, terrainCell.NavigationCost) : 0u;
                    grid.SetCell(worldCell, penalty, walkable);
                    ownedCells.Add(worldCell);
                    runtimeTerrainOwnerByCell[worldCell] = chunk.Address;
                }
            }
            cellsByRuntimeChunk[chunk.Address] = ownedCells;
        }
        finally
        {
            grid.EndBatchUpdate();
        }
    }

    public void UnregisterChunkRuntime(ChunkRuntime chunk)
    {
        if (chunk != null)
            UnregisterChunkRuntime(chunk.Address);
    }

    public void UnregisterChunkRuntime(RuntimeWorldAddress address)
    {
        if (!cellsByRuntimeChunk.TryGetValue(address, out HashSet<Vector2Int> ownedCells))
            return;

        grid.BeginBatchUpdate();
        try
        {
            cellsByRuntimeChunk.Remove(address);
            foreach (Vector2Int worldCell in ownedCells)
            {
                if (!runtimeTerrainOwnerByCell.TryGetValue(worldCell, out RuntimeWorldAddress owner) ||
                    owner != address)
                    continue;
                runtimeTerrainOwnerByCell.Remove(worldCell);
                grid.RemoveCell(worldCell);
            }
        }
        finally
        {
            grid.EndBatchUpdate();
        }
    }

    public void SetNavigationCell(Vector2Int worldCell, uint penalty, bool walkable)
        => grid.SetCell(worldCell, penalty, walkable);

    public void SetNavigationCell(Map owner, Vector2Int worldCell, uint penalty, bool walkable)
    {
        grid.SetCell(worldCell, penalty, walkable);
        AssignCellOwner(owner, worldCell);
    }

    public void QueueNavigationCell(Vector2Int worldCell)
        => RefreshCellFromLoadedMap(worldCell);

    public void QueueNavigationCells(IEnumerable<Vector2Int> worldCells)
    {
        if (worldCells == null)
            return;

        grid.BeginBatchUpdate();
        try
        {
            foreach (Vector2Int worldCell in worldCells)
                RefreshCellFromLoadedMap(worldCell);
        }
        finally
        {
            grid.EndBatchUpdate();
        }
    }

    public void QueueNavigationRegion(RectInt worldRect)
    {
        grid.BeginBatchUpdate();
        try
        {
            for (int y = worldRect.yMin; y < worldRect.yMax; y++)
            {
                for (int x = worldRect.xMin; x < worldRect.xMax; x++)
                    RefreshCellFromLoadedMap(new Vector2Int(x, y));
            }
        }
        finally
        {
            grid.EndBatchUpdate();
        }
    }

    public void SetNavigationRegion(Map owner, RectInt worldRect, uint penalty, bool walkable)
    {
        grid.BeginBatchUpdate();
        try
        {
            for (int y = worldRect.yMin; y < worldRect.yMax; y++)
            {
                for (int x = worldRect.xMin; x < worldRect.xMax; x++)
                    SetNavigationCell(owner, new Vector2Int(x, y), penalty, walkable);
            }
        }
        finally
        {
            grid.EndBatchUpdate();
        }
    }

    public void RegisterObstacle(int obstacleId, IEnumerable<Vector2Int> occupiedCells)
        => grid.RegisterBlocker(obstacleId, occupiedCells);

    public void UnregisterObstacle(int obstacleId)
        => grid.UnregisterBlocker(obstacleId);

    public bool TryGetCell(Vector2 worldPosition, out uint penalty, out bool walkable)
    {
        Vector2Int cellPosition = WorldNavigationGrid.WorldToCell(worldPosition);
        if (grid.TryGetCell(cellPosition, out WorldNavigationCell cell))
        {
            penalty = cell.Penalty;
            walkable = cell.Walkable;
            return true;
        }

        if (TryReadCellFromRuntime(cellPosition, out cell, out ChunkRuntime sourceChunk))
        {
            grid.SetCell(cellPosition, cell.Penalty, cell.Walkable);
            AssignRuntimeCellOwner(sourceChunk, cellPosition);
            penalty = cell.Penalty;
            walkable = cell.Walkable;
            return true;
        }

        penalty = 0u;
        walkable = false;
        return false;
    }

    public bool IsWalkable(Vector2 worldPosition)
        => TryGetCell(worldPosition, out _, out bool walkable) && walkable;

    public bool HasGridLineOfSight(Vector2 from, Vector2 to)
        => grid.HasLineOfSight(
            WorldNavigationGrid.WorldToCell(from),
            WorldNavigationGrid.WorldToCell(to));

    /// <summary>检查直线中间没有比起终点更高代价的地形，供短路优化使用。</summary>
    private bool HasNoHigherPenaltyGridLineOfSight(Vector2 from, Vector2 to)
    {
        Vector2Int fromCell = WorldNavigationGrid.WorldToCell(from);
        Vector2Int toCell = WorldNavigationGrid.WorldToCell(to);
        if (!grid.TryGetCell(fromCell, out WorldNavigationCell fromData) ||
            !grid.TryGetCell(toCell, out WorldNavigationCell toData))
        {
            return false;
        }

        uint maxEndpointPenalty = Math.Max(fromData.Penalty, toData.Penalty);
        return grid.HasLineOfSight(fromCell, toCell, maxEndpointPenalty);
    }

    public bool IsPathStillValid(Vector2 currentPosition, IReadOnlyList<Vector2> waypoints, int waypointIndex)
    {
        if (waypoints == null || waypointIndex < 0 || waypointIndex >= waypoints.Count)
            return false;

        Vector2 segmentStart = currentPosition;
        for (int i = waypointIndex; i < waypoints.Count; i++)
        {
            if (!HasGridLineOfSight(segmentStart, waypoints[i]))
                return false;
            segmentStart = waypoints[i];
        }

        return true;
    }

    public void RefreshLoadedRegion(Vector2 center = default, int chunkRadius = 1, Action onComplete = null)
    {
        CaptureActiveWorldKey();
        // ChunkMgr already owns the current 1x1/3x3/etc. streaming window. Navigation mirrors
        // that active set and never expands or generates terrain outside the loaded chunks.
        RegisterActiveMaps();
        Init = true;
        onComplete?.Invoke();
    }

    private void RegisterActiveMaps()
    {
        ChunkMgr chunkManager = ChunkMgr.Instance;
        if (chunkManager?.WorldRuntime == null)
            return;

        grid.BeginBatchUpdate();
        try
        {
            foreach (ChunkRuntime chunk in chunkManager.Chunks.Values)
            {
                if (chunk == null || !chunk.HasNavigationLease ||
                    chunk.DataStatus != ChunkDataStatus.Ready)
                    continue;
                if (!cellsByRuntimeChunk.ContainsKey(chunk.Address))
                    RegisterChunkRuntime(chunk);
            }
        }
        finally
        {
            grid.EndBatchUpdate();
        }
    }

    private void CaptureActiveWorldKey()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        string nextWorldKey = activeScene.IsValid() ? activeScene.name : string.Empty;
        ApplyActiveWorldKey(nextWorldKey);
    }

    private void OnActiveSceneChanged(Scene previousScene, Scene nextScene)
    {
        if (nextScene.IsValid())
            ApplyActiveWorldKey(nextScene.name);
    }

    private void ApplyActiveWorldKey(string nextWorldKey)
    {
        if (!string.IsNullOrEmpty(activeWorldKey) &&
            !string.IsNullOrEmpty(nextWorldKey) &&
            !string.Equals(activeWorldKey, nextWorldKey, StringComparison.Ordinal))
        {
            FailAllRequests();
            fieldsByGoal.Clear();
            fieldSchedule.Clear();
            admissionQueue.Clear();
            grid.Clear();
            RecycleAllMapCellSets();
            terrainOwnerByCell.Clear();
            cellsByRuntimeChunk.Clear();
            runtimeTerrainOwnerByCell.Clear();
            observedGridRevision = grid.Revision;
            scheduleIndex = 0;
        }

        activeWorldKey = nextWorldKey;
    }

    private void RefreshCellFromLoadedMap(Vector2Int worldCell)
    {
        worldCell = WorldNavigationGrid.NormalizeCell(worldCell);
        if (TryReadCellFromRuntime(worldCell, out WorldNavigationCell cell, out ChunkRuntime sourceChunk))
        {
            grid.SetCell(worldCell, cell.Penalty, cell.Walkable);
            AssignRuntimeCellOwner(sourceChunk, worldCell);
        }
        else
        {
            RemoveCellOwner(worldCell);
            grid.RemoveCell(worldCell);
        }
    }

    private void AssignCellOwner(Map map, Vector2Int worldCell)
    {
        if (map == null)
            return;

        worldCell = WorldNavigationGrid.NormalizeCell(worldCell);

        int mapId = map.GetInstanceID();
        if (!cellsByMap.TryGetValue(mapId, out HashSet<Vector2Int> ownedCells))
        {
            ownedCells = RentMapCellSet(1);
            cellsByMap[mapId] = ownedCells;
        }

        ownedCells.Add(worldCell);
        terrainOwnerByCell[worldCell] = mapId;
    }

    private void RemoveCellOwner(Vector2Int worldCell)
    {
        worldCell = WorldNavigationGrid.NormalizeCell(worldCell);
        if (!terrainOwnerByCell.TryGetValue(worldCell, out int mapId))
            return;

        terrainOwnerByCell.Remove(worldCell);
        if (!cellsByMap.TryGetValue(mapId, out HashSet<Vector2Int> ownedCells))
            return;

        ownedCells.Remove(worldCell);
        if (ownedCells.Count == 0)
        {
            cellsByMap.Remove(mapId);
            ReturnMapCellSet(ownedCells);
        }
    }

    private HashSet<Vector2Int> RentMapCellSet(int estimatedCapacity)
    {
        if (mapCellSetPool.Count > 0)
            return mapCellSetPool.Pop();

        return new HashSet<Vector2Int>(Mathf.Max(0, estimatedCapacity));
    }

    private void ReturnMapCellSet(HashSet<Vector2Int> cells)
    {
        if (cells == null)
            return;

        cells.Clear();
        if (mapCellSetPool.Count < MaxPooledMapCellSets)
            mapCellSetPool.Push(cells);
    }

    private void RecycleAllMapCellSets()
    {
        foreach (HashSet<Vector2Int> cells in cellsByMap.Values)
            ReturnMapCellSet(cells);

        cellsByMap.Clear();
    }

    private static bool TryReadCellFromRuntime(
        Vector2Int worldCell,
        out WorldNavigationCell cell,
        out ChunkRuntime sourceChunk)
    {
        worldCell = WorldNavigationGrid.NormalizeCell(worldCell);
        cell = default;
        sourceChunk = null;
        ChunkMgr chunkManager = ChunkMgr.Instance;
        if (chunkManager == null)
            return false;

        Vector2 center = WorldNavigationGrid.CellCenter(worldCell);
        RuntimeWorldAddress address = chunkManager.ResolveWorldAddress(center);
        if (!chunkManager.TryGetChunkRuntime(address, out sourceChunk) || sourceChunk.Terrain == null)
            return false;
        int localX = worldCell.x - sourceChunk.Address.ChunkOrigin.X;
        int localY = worldCell.y - sourceChunk.Address.ChunkOrigin.Y;
        if ((uint)localX >= (uint)sourceChunk.Terrain.Width ||
            (uint)localY >= (uint)sourceChunk.Terrain.Height)
            return false;
        TerrainCell terrainCell = sourceChunk.Terrain.GetCell(localX, localY);
        bool walkable = (terrainCell.Flags & TerrainCellFlags.Walkable) != 0 &&
                        (terrainCell.Flags & TerrainCellFlags.Blocking) == 0;
        cell = new WorldNavigationCell(
            walkable ? (uint)Mathf.Max(1, terrainCell.NavigationCost) : 0u,
            BuildingOccupancyRegistry.GetEffectiveWalkable(worldCell, walkable));
        return true;
    }

    private void AssignRuntimeCellOwner(ChunkRuntime chunk, Vector2Int worldCell)
    {
        if (chunk == null)
            return;
        worldCell = WorldNavigationGrid.NormalizeCell(worldCell);
        if (!cellsByRuntimeChunk.TryGetValue(chunk.Address, out HashSet<Vector2Int> cells))
        {
            cells = new HashSet<Vector2Int>();
            cellsByRuntimeChunk[chunk.Address] = cells;
        }
        cells.Add(worldCell);
        runtimeTerrainOwnerByCell[worldCell] = chunk.Address;
    }

    #endregion

    #region Path requests

    public int RequestPath(
        Vector2 start,
        Vector2 destination,
        Action<WorldNavigationPathResult> callback)
    {
        int requestId = nextRequestId++;
        if (nextRequestId <= 0)
            nextRequestId = 1;

        PathRequest request = new(requestId, start, destination, callback);
        requests[requestId] = request;
        QueueForAdmission(request);
        return requestId;
    }

    public void CancelPath(int requestId)
    {
        if (requestId <= 0)
            return;

        if (requests.TryGetValue(requestId, out PathRequest request))
        {
            DetachRequestFromField(request);
            RemoveFromAdmissionQueue(request);
            requests.Remove(requestId);
            return;
        }

        if (completionNodes.TryGetValue(requestId, out LinkedListNode<CompletedRequest> completionNode))
        {
            completions.Remove(completionNode);
            completionNodes.Remove(requestId);
        }
    }

    /// <summary>Processes a deterministic expansion count; Update additionally applies a time budget.</summary>
    public int ProcessPathRequests(int expansionBudget)
    {
        SynchronizeGridRevision();
        int completionReserve = Mathf.Max(1, maxPathBuildsPerExpansion);
        if (completions.Count > Mathf.Max(16, maxBufferedCompletions) - completionReserve)
            return 0;

        int remaining = Mathf.Max(0, expansionBudget);
        int processed = 0;
        if (remaining > 0)
        {
            int admitted = AdmitQueuedRequests(1);
            remaining -= admitted;
            processed += admitted;
        }

        int idleChecks = 0;

        while (remaining > 0 && fieldSchedule.Count > 0 && idleChecks < fieldSchedule.Count)
        {
            if (scheduleIndex >= fieldSchedule.Count)
                scheduleIndex = 0;

            GoalField field = fieldSchedule[scheduleIndex];
            scheduleIndex = (scheduleIndex + 1) % fieldSchedule.Count;

            if (!field.HasWaitingRequests)
            {
                idleChecks++;
                continue;
            }

            idleChecks = 0;
            if (field.Failed || field.ExpandedCells >= GetGoalSearchCellLimit())
            {
                field.MarkFailed();
                DrainFailedRequests(field, Mathf.Max(1, maxPathBuildsPerExpansion));
                if (!field.HasWaitingRequests)
                    RemoveField(field);
                remaining--;
                processed++;
                continue;
            }

            if (!ExpandGoalField(field))
            {
                field.MarkFailed();
                DrainFailedRequests(field, Mathf.Max(1, maxPathBuildsPerExpansion));
                if (!field.HasWaitingRequests)
                    RemoveField(field);
            }

            remaining--;
            processed++;
        }

        return processed;
    }

    private int GetConcurrentGoalSearchLimit()
    {
        int reservedCellsPerGoal = GetGoalSearchCellLimit();
        int memoryLimitedSearches = Mathf.Max(
            1,
            Mathf.Max(16384, maxTotalCachedSearchCells) / reservedCellsPerGoal);
        return Mathf.Min(Mathf.Max(1, maxConcurrentGoalSearches), memoryLimitedSearches);
    }

    private int GetGoalSearchCellLimit()
    {
        // A reverse field must be allowed to exhaust the currently loaded sparse grid. This
        // removes the old 65k hard failure while still reducing concurrent fields as worlds grow.
        return Mathf.Max(Mathf.Max(1024, maxExpandedCellsPerGoal), grid.CellCount);
    }

    private int GetActiveGoalSearchCount()
    {
        int activeFieldCount = 0;
        for (int i = 0; i < fieldSchedule.Count; i++)
        {
            if (fieldSchedule[i].HasWaitingRequests)
                activeFieldCount++;
        }
        return activeFieldCount;
    }

    private void PrepareRequest(PathRequest request)
    {
        DetachRequestFromField(request);
        RemoveFromAdmissionQueue(request);

        if (!Init || grid.CellCount == 0)
        {
            ScheduleFailure(request);
            return;
        }

        Vector2Int requestedStart = WorldNavigationGrid.WorldToCell(request.Start);
        Vector2Int requestedGoal = WorldNavigationGrid.WorldToCell(request.Destination);
        if (!grid.TryGetCell(requestedStart, out _) || !grid.TryGetCell(requestedGoal, out _))
        {
            // Unknown means the owning chunk is not loaded yet. Do not snap an agent to the
            // edge of the currently loaded world; the client can retry after streaming.
            ScheduleFailure(request);
            return;
        }

        if (!grid.TryFindNearestWalkable(requestedStart, nearestWalkableSearchRadius, out Vector2Int start) ||
            !grid.TryFindNearestWalkable(requestedGoal, nearestWalkableSearchRadius, out Vector2Int goal))
        {
            ScheduleFailure(request);
            return;
        }

        request.StartCell = start;
        request.GoalCell = goal;
        request.PreparedRevision = grid.Revision;

        Vector2Int directDelta = WorldTopologyRuntime.ShortestDelta(start, goal);
        int directDistance = Mathf.Max(Mathf.Abs(directDelta.x), Mathf.Abs(directDelta.y));
        int directPathCost = 0;
        bool directLineIsEndpointCostSafe =
            directDistance <= Mathf.Max(1, maxDirectLineOfSightCells) &&
            HasNoHigherPenaltyGridLineOfSight(
                WorldNavigationGrid.CellCenter(start),
                WorldNavigationGrid.CellCenter(goal)) &&
            grid.TryCalculateLineTraversalCost(start, goal, out directPathCost);
        if (start == goal ||
            directLineIsEndpointCostSafe)
        {
            Vector2 resolved = goal == requestedGoal
                ? WorldTopologyRuntime.NormalizePosition(request.Destination)
                : WorldNavigationGrid.CellCenter(goal);
            ScheduleSuccess(
                request,
                resolved,
                new[] { resolved },
                true,
                start == goal ? 0 : directPathCost);
            return;
        }

        if (!TryGetOrCreateField(goal, out GoalField field))
        {
            QueueForAdmission(request);
            return;
        }

        field.LastUsedTime = Time.unscaledTime;
        if (field.TryBuildPath(
                start,
                request,
                grid,
                Mathf.Max(2, maxPathCellsPerResult),
                out Vector2[] cachedPath,
                out Vector2 resolvedDestination,
                out bool cachedPathReachesDestination,
                out int cachedPathCost))
        {
            ScheduleSuccess(
                request,
                resolvedDestination,
                cachedPath,
                cachedPathReachesDestination,
                cachedPathCost);
            return;
        }

        if (!field.HasWaitingRequests &&
            GetActiveGoalSearchCount() >= GetConcurrentGoalSearchLimit())
        {
            QueueForAdmission(request);
            return;
        }

        request.FieldNode = field.AddWaitingRequest(start, request.Id);
        request.Field = field;
    }

    private bool TryGetOrCreateField(Vector2Int goal, out GoalField field)
    {
        if (fieldsByGoal.TryGetValue(goal, out field))
        {
            if (!field.Failed || field.HasWaitingRequests)
                return true;

            RemoveField(field);
            field = null;
        }

        if (GetActiveGoalSearchCount() >= GetConcurrentGoalSearchLimit())
        {
            field = null;
            return false;
        }

        if (fieldSchedule.Count >= Mathf.Max(1, maxCachedGoalFields) && !EvictOldestIdleField())
        {
            field = null;
            return false;
        }

        field = new GoalField(goal, grid.Revision, Time.unscaledTime);
        fieldsByGoal[goal] = field;
        fieldSchedule.Add(field);
        return true;
    }

    private void QueueForAdmission(PathRequest request)
    {
        if (request.AdmissionQueued)
            return;

        request.AdmissionNode = admissionQueue.AddLast(request.Id);
    }

    private int AdmitQueuedRequests(int maxAdmissions)
    {
        int remaining = Mathf.Max(0, maxAdmissions);
        int admitted = 0;
        while (remaining-- > 0 && admissionQueue.Count > 0)
        {
            if (completions.Count >= Mathf.Max(16, maxBufferedCompletions))
                break;

            LinkedListNode<int> node = admissionQueue.First;
            admissionQueue.RemoveFirst();
            int requestId = node.Value;
            if (!requests.TryGetValue(requestId, out PathRequest request) ||
                !ReferenceEquals(request.AdmissionNode, node))
            {
                continue;
            }

            request.AdmissionNode = null;
            PrepareRequest(request);
            if (request.AdmissionQueued)
                break;
            admitted++;
        }

        return admitted;
    }

    private bool ExpandGoalField(GoalField field)
    {
        if (!field.TryPopNext(out WorldNavigationGrid.HeapEntry current))
            return false;

        field.ExpandedCells++;
        field.LastUsedTime = Time.unscaledTime;

        bool hasMoreAtCurrentCell = false;
        int remainingBuilds = Mathf.Max(1, maxPathBuildsPerExpansion);
        while (remainingBuilds-- > 0 &&
               field.TakeWaitingRequests(
                   current.Cell,
                   requestIdBuffer,
                   1,
                   out hasMoreAtCurrentCell))
        {
            int requestId = requestIdBuffer[0];
            if (requests.TryGetValue(requestId, out PathRequest request))
            {
                request.FieldNode = null;
                if (field.TryBuildPath(
                        request.StartCell,
                        request,
                        grid,
                        Mathf.Max(2, maxPathCellsPerResult),
                        out Vector2[] waypoints,
                        out Vector2 resolvedDestination,
                        out bool reachesDestination,
                        out int totalCost))
                {
                    ScheduleSuccess(
                        request,
                        resolvedDestination,
                        waypoints,
                        reachesDestination,
                        totalCost);
                }
                else
                {
                    ScheduleFailure(request);
                }
            }

            if (!hasMoreAtCurrentCell)
                break;

            // Each result has a bounded path horizon, but still respect the hard frame-time
            // budget between fan-out builds when many agents share exactly one start cell.
            if (pathStopwatch.IsRunning &&
                pathStopwatch.Elapsed.TotalMilliseconds >= Mathf.Max(0.1f, maxPathMillisecondsPerFrame))
            {
                break;
            }
        }

        if (hasMoreAtCurrentCell)
        {
            // Revisit this resolved start on the next scheduler slice. This prevents a same-cell
            // crowd from monopolizing one frame while preserving the shared reverse search.
            field.Requeue(current);
            field.ExpandedCells--;
            return true;
        }

        IReadOnlyList<Vector2Int> offsets = WorldNavigationGrid.GetNeighbourOffsets();
        for (int i = 0; i < offsets.Count; i++)
        {
            Vector2Int neighbour = WorldNavigationGrid.NormalizeCell(current.Cell + offsets[i]);
            // Reverse search: an agent standing at neighbour must be able to move to current.
            if (!grid.CanTraverse(neighbour, current.Cell, out int traversalCost))
                continue;

            int nextCost = current.Cost + traversalCost;
            field.TryRelax(neighbour, current.Cell, nextCost);
        }

        return true;
    }

    private void SynchronizeGridRevision()
    {
        if (observedGridRevision == grid.Revision)
            return;

        observedGridRevision = grid.Revision;
        grid.ConsumeChanges(gridChangeBuffer, out bool fullReset);
        revisionRequeueBuffer.Clear();

        if (fullReset)
        {
            admissionQueue.Clear();
            foreach (PathRequest request in requests.Values)
            {
                request.Field = null;
                request.FieldNode = null;
                request.AdmissionNode = null;
                revisionRequeueBuffer.Add(request);
            }

            fieldsByGoal.Clear();
            fieldSchedule.Clear();
            scheduleIndex = 0;
        }
        else
        {
            for (int fieldIndex = fieldSchedule.Count - 1; fieldIndex >= 0; fieldIndex--)
            {
                GoalField field = fieldSchedule[fieldIndex];
                if (!field.IsAffected(gridChangeBuffer))
                {
                    field.AcceptRevision(grid.Revision);
                    continue;
                }

                field.CopyWaitingRequestIds(requestIdBuffer);
                for (int requestIndex = 0; requestIndex < requestIdBuffer.Count; requestIndex++)
                {
                    if (!requests.TryGetValue(requestIdBuffer[requestIndex], out PathRequest request))
                        continue;

                    request.Field = null;
                    request.FieldNode = null;
                    revisionRequeueBuffer.Add(request);
                }

                RemoveFieldAt(fieldIndex);
            }
        }

        for (int i = 0; i < revisionRequeueBuffer.Count; i++)
        {
            PathRequest request = revisionRequeueBuffer[i];
            if (requests.ContainsKey(request.Id))
                QueueForAdmission(request);
        }
    }

    private void RemoveFieldAt(int index)
    {
        if (index < 0 || index >= fieldSchedule.Count)
            return;

        GoalField field = fieldSchedule[index];
        if (fieldsByGoal.TryGetValue(field.Goal, out GoalField current) && ReferenceEquals(current, field))
            fieldsByGoal.Remove(field.Goal);

        fieldSchedule.RemoveAt(index);
        if (scheduleIndex > index)
            scheduleIndex--;
        if (scheduleIndex >= fieldSchedule.Count)
            scheduleIndex = 0;
    }

    private void RemoveField(GoalField field)
    {
        int index = fieldSchedule.IndexOf(field);
        if (index >= 0)
            RemoveFieldAt(index);
    }

    private void ScheduleSuccess(
        PathRequest request,
        Vector2 resolvedDestination,
        Vector2[] waypoints,
        bool reachesDestination,
        int totalCost)
    {
        DetachRequestFromField(request);
        RemoveFromAdmissionQueue(request);
        requests.Remove(request.Id);
        EnqueueCompletion(new CompletedRequest(
            request.Callback,
            request.Start,
            new WorldNavigationPathResult(
                request.Id,
                true,
                request.Destination,
                resolvedDestination,
                waypoints,
                reachesDestination,
                totalCost,
                grid.Revision,
                grid.PathCostRevision)));
    }

    private void ScheduleFailure(PathRequest request)
    {
        DetachRequestFromField(request);
        RemoveFromAdmissionQueue(request);
        requests.Remove(request.Id);
        EnqueueCompletion(new CompletedRequest(
            request.Callback,
            request.Start,
            new WorldNavigationPathResult(
                request.Id,
                false,
                request.Destination,
                request.Destination,
                Array.Empty<Vector2>(),
                false,
                int.MaxValue,
                grid.Revision,
                grid.PathCostRevision)));
    }

    private void EnqueueCompletion(CompletedRequest completed)
    {
        LinkedListNode<CompletedRequest> node = completions.AddLast(completed);
        completionNodes[completed.Result.RequestId] = node;
    }

    private static void DetachRequestFromField(PathRequest request)
    {
        if (request?.Field == null)
            return;

        request.Field.RemoveWaitingRequest(request.StartCell, request.FieldNode);
        request.FieldNode = null;
        request.Field = null;
    }

    private void RemoveFromAdmissionQueue(PathRequest request)
    {
        if (request?.AdmissionNode == null)
            return;

        if (request.AdmissionNode.List != null)
            admissionQueue.Remove(request.AdmissionNode);
        request.AdmissionNode = null;
    }

    private void DrainFailedRequests(GoalField field, int maxCount)
    {
        field.TakeAnyWaitingRequests(requestIdBuffer, maxCount);
        for (int i = 0; i < requestIdBuffer.Count; i++)
        {
            if (!requests.TryGetValue(requestIdBuffer[i], out PathRequest request))
                continue;

            request.FieldNode = null;
            ScheduleFailure(request);
        }
    }

    private void FailAllRequests()
    {
        revisionRequeueBuffer.Clear();
        foreach (PathRequest request in requests.Values)
            revisionRequeueBuffer.Add(request);

        requests.Clear();
        admissionQueue.Clear();
        for (int i = 0; i < revisionRequeueBuffer.Count; i++)
            ScheduleFailure(revisionRequeueBuffer[i]);

        DispatchCompletions(int.MaxValue, double.PositiveInfinity);
    }

    private void DispatchCompletions(int maxCount, double maxMilliseconds)
    {
        int remaining = Mathf.Max(0, maxCount);
        int dispatched = 0;
        while (remaining-- > 0 && completions.Count > 0)
        {
            if (dispatched > 0 && pathStopwatch.Elapsed.TotalMilliseconds >= maxMilliseconds)
                break;

            LinkedListNode<CompletedRequest> node = completions.First;
            completions.RemoveFirst();
            CompletedRequest completed = node.Value;
            completionNodes.Remove(completed.Result.RequestId);

            WorldNavigationPathResult result = completed.Result;
            if (result.Success &&
                (result.GridRevision != grid.Revision ||
                 result.PathCostRevision != grid.PathCostRevision))
            {
                bool pathCostChanged = result.PathCostRevision != grid.PathCostRevision;
                bool pathRemainsWalkable = !pathCostChanged &&
                                           result.Waypoints.Length > 0 &&
                                           IsPathStillValid(completed.Start, result.Waypoints, 0);
                result = pathRemainsWalkable
                    ? new WorldNavigationPathResult(
                        result.RequestId,
                        true,
                        result.RequestedDestination,
                        result.ResolvedDestination,
                        result.Waypoints,
                        result.ReachesDestination,
                        result.TotalCost,
                        grid.Revision,
                        grid.PathCostRevision)
                    : new WorldNavigationPathResult(
                        result.RequestId,
                        false,
                        result.RequestedDestination,
                        result.RequestedDestination,
                        Array.Empty<Vector2>(),
                        false,
                        int.MaxValue,
                        grid.Revision,
                        grid.PathCostRevision);
            }

            try
            {
                completed.Callback?.Invoke(result);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogException(exception, this);
            }

            dispatched++;
        }
    }

    private void PruneGoalFields()
    {
        float now = Time.unscaledTime;
        for (int i = fieldSchedule.Count - 1; i >= 0; i--)
        {
            GoalField field = fieldSchedule[i];
            if (field.HasWaitingRequests || now - field.LastUsedTime <= goalFieldLifetime)
                continue;

            RemoveFieldAt(i);
        }
    }

    private void EnforceCacheCellBudget()
    {
        int cellBudget = Mathf.Max(16384, maxTotalCachedSearchCells);
        while (GetTotalCachedSearchCells() > cellBudget && EvictOldestIdleField())
        {
        }
    }

    private int GetTotalCachedSearchCells()
    {
        int total = 0;
        for (int i = 0; i < fieldSchedule.Count; i++)
            total += fieldSchedule[i].StoredCellCount;
        return total;
    }

    private bool EvictOldestIdleField()
    {
        int oldestIndex = -1;
        float oldestTime = float.PositiveInfinity;
        for (int i = 0; i < fieldSchedule.Count; i++)
        {
            GoalField candidate = fieldSchedule[i];
            if (candidate.HasWaitingRequests || candidate.LastUsedTime >= oldestTime)
                continue;
            oldestIndex = i;
            oldestTime = candidate.LastUsedTime;
        }

        if (oldestIndex < 0)
            return false;

        RemoveFieldAt(oldestIndex);
        return true;
    }

    #endregion

    private sealed class PathRequest
    {
        public readonly int Id;
        public readonly Vector2 Start;
        public readonly Vector2 Destination;
        public readonly Action<WorldNavigationPathResult> Callback;
        public Vector2Int StartCell;
        public Vector2Int GoalCell;
        public int PreparedRevision;
        public GoalField Field;
        public LinkedListNode<int> FieldNode;
        public LinkedListNode<int> AdmissionNode;
        public bool AdmissionQueued => AdmissionNode != null;

        public PathRequest(int id, Vector2 start, Vector2 destination, Action<WorldNavigationPathResult> callback)
        {
            Id = id;
            Start = start;
            Destination = destination;
            Callback = callback;
        }
    }

    private readonly struct CompletedRequest
    {
        public readonly Action<WorldNavigationPathResult> Callback;
        public readonly Vector2 Start;
        public readonly WorldNavigationPathResult Result;

        public CompletedRequest(
            Action<WorldNavigationPathResult> callback,
            Vector2 start,
            WorldNavigationPathResult result)
        {
            Callback = callback;
            Start = start;
            Result = result;
        }
    }

    private sealed class GoalField
    {
        private readonly WorldNavigationGrid.MinHeap frontier = new();
        private readonly Dictionary<Vector2Int, int> costs = new(1024);
        private readonly Dictionary<Vector2Int, Vector2Int> nextTowardGoal = new(1024);
        private readonly Dictionary<Vector2Int, LinkedList<int>> waitingByStart = new(32);
        private int waitingCount;
        private int minX;
        private int maxX;
        private int minY;
        private int maxY;

        public readonly Vector2Int Goal;
        public int Revision { get; private set; }
        public int ExpandedCells;
        public float LastUsedTime;
        public bool HasWaitingRequests => waitingCount > 0;
        public int StoredCellCount => costs.Count;
        public bool Failed { get; private set; }

        public GoalField(Vector2Int goal, int revision, float now)
        {
            Goal = goal;
            Revision = revision;
            LastUsedTime = now;
            minX = maxX = goal.x;
            minY = maxY = goal.y;
            costs[goal] = 0;
            frontier.Push(goal, 0, 0);
        }

        public LinkedListNode<int> AddWaitingRequest(Vector2Int start, int requestId)
        {
            if (!waitingByStart.TryGetValue(start, out LinkedList<int> ids))
            {
                ids = new LinkedList<int>();
                waitingByStart[start] = ids;
            }

            LinkedListNode<int> node = ids.AddLast(requestId);
            waitingCount++;
            return node;
        }

        public void RemoveWaitingRequest(Vector2Int start, LinkedListNode<int> node)
        {
            if (node == null ||
                !waitingByStart.TryGetValue(start, out LinkedList<int> ids) ||
                !ReferenceEquals(node.List, ids))
            {
                return;
            }

            ids.Remove(node);
            waitingCount--;
            if (ids.Count == 0)
                waitingByStart.Remove(start);
        }

        public bool IsAffected(IReadOnlyList<Vector2Int> changedCells)
        {
            IReadOnlyList<Vector2Int> offsets = WorldNavigationGrid.GetNeighbourOffsets();
            bool wrapped = WorldTopologyRuntime.TryGetActiveBounds(out _);
            for (int i = 0; i < changedCells.Count; i++)
            {
                Vector2Int changed = changedCells[i];
                if (!wrapped &&
                    (changed.x < minX - 1 || changed.x > maxX + 1 ||
                    changed.y < minY - 1 || changed.y > maxY + 1)
                   )
                {
                    continue;
                }

                if (costs.ContainsKey(changed))
                    return true;

                for (int offsetIndex = 0; offsetIndex < offsets.Count; offsetIndex++)
                {
                    if (costs.ContainsKey(WorldNavigationGrid.NormalizeCell(changed + offsets[offsetIndex])))
                        return true;
                }
            }

            return false;
        }

        public void AcceptRevision(int revision)
        {
            Revision = revision;
        }

        public bool TryPopNext(out WorldNavigationGrid.HeapEntry entry)
        {
            while (frontier.TryPop(out entry))
            {
                if (costs.TryGetValue(entry.Cell, out int knownCost) && knownCost == entry.Cost)
                    return true;
            }
            return false;
        }

        public void Requeue(WorldNavigationGrid.HeapEntry entry)
            => frontier.Push(entry.Cell, entry.Priority, entry.Cost);

        public void MarkFailed()
        {
            Failed = true;
        }

        public void TryRelax(Vector2Int cell, Vector2Int next, int cost)
        {
            if (costs.TryGetValue(cell, out int oldCost) && oldCost <= cost)
                return;

            costs[cell] = cost;
            nextTowardGoal[cell] = next;
            if (cell.x < minX) minX = cell.x;
            if (cell.x > maxX) maxX = cell.x;
            if (cell.y < minY) minY = cell.y;
            if (cell.y > maxY) maxY = cell.y;
            frontier.Push(cell, cost, cost);
        }

        public bool TakeWaitingRequests(
            Vector2Int start,
            List<int> result,
            int maxCount,
            out bool hasMore)
        {
            result.Clear();
            hasMore = false;
            if (!waitingByStart.TryGetValue(start, out LinkedList<int> ids))
                return false;

            int remaining = Mathf.Max(1, maxCount);
            while (remaining-- > 0 && ids.First != null)
            {
                LinkedListNode<int> node = ids.First;
                ids.RemoveFirst();
                result.Add(node.Value);
                waitingCount--;
            }

            hasMore = ids.Count > 0;
            if (!hasMore)
                waitingByStart.Remove(start);
            return true;
        }

        public bool TakeAnyWaitingRequests(List<int> result, int maxCount)
        {
            result.Clear();
            int remaining = Mathf.Max(1, maxCount);
            while (remaining > 0 && waitingByStart.Count > 0)
            {
                Vector2Int selectedStart = default;
                LinkedList<int> selectedIds = null;
                foreach (KeyValuePair<Vector2Int, LinkedList<int>> pair in waitingByStart)
                {
                    selectedStart = pair.Key;
                    selectedIds = pair.Value;
                    break;
                }

                if (selectedIds == null)
                    break;

                while (selectedIds.First != null && remaining-- > 0)
                {
                    LinkedListNode<int> node = selectedIds.First;
                    selectedIds.RemoveFirst();
                    result.Add(node.Value);
                    waitingCount--;
                }

                if (selectedIds.Count == 0)
                    waitingByStart.Remove(selectedStart);
            }

            return result.Count > 0;
        }

        public bool TryBuildPath(
            Vector2Int start,
            PathRequest request,
            WorldNavigationGrid grid,
            int maxPathCells,
            out Vector2[] waypoints,
            out Vector2 resolvedDestination,
            out bool reachesDestination,
            out int totalCost)
        {
            reachesDestination = false;
            totalCost = int.MaxValue;
            resolvedDestination = request.GoalCell == WorldNavigationGrid.WorldToCell(request.Destination)
                ? WorldTopologyRuntime.NormalizePosition(request.Destination)
                : WorldNavigationGrid.CellCenter(request.GoalCell);

            if (!costs.TryGetValue(start, out totalCost) ||
                (start != Goal && !nextTowardGoal.ContainsKey(start)))
            {
                waypoints = null;
                return false;
            }

            List<Vector2Int> raw = new(64) { start };
            Vector2Int current = start;
            int guard = nextTowardGoal.Count + 1;
            int pathCellLimit = Mathf.Max(2, maxPathCells);
            while (current != Goal && raw.Count < pathCellLimit && guard-- > 0)
            {
                if (!nextTowardGoal.TryGetValue(current, out current))
                {
                    waypoints = null;
                    return false;
                }
                raw.Add(current);
            }

            reachesDestination = current == Goal;
            if (!reachesDestination && raw.Count < pathCellLimit)
            {
                waypoints = null;
                return false;
            }

            List<Vector2> smoothed = new(16);
            bool includeResolvedStart = start != WorldNavigationGrid.WorldToCell(request.Start);
            if (includeResolvedStart)
                smoothed.Add(WorldNavigationGrid.CellCenter(start));

            int anchor = 0;
            while (anchor < raw.Count - 1)
            {
                int furthest = Mathf.Min(raw.Count - 1, anchor + 16);
                while (furthest > anchor + 1 && !grid.CanSmoothPathSegment(raw, anchor, furthest))
                    furthest--;

                smoothed.Add(WorldNavigationGrid.CellCenter(raw[furthest]));
                anchor = furthest;
            }

            Vector2 pathEnd = reachesDestination
                ? resolvedDestination
                : WorldNavigationGrid.CellCenter(raw[^1]);
            if (smoothed.Count == 0 || WorldTopologyRuntime.SqrDistance(smoothed[^1], pathEnd) > 0.0001f)
                smoothed.Add(pathEnd);
            else
                smoothed[^1] = pathEnd;

            waypoints = smoothed.ToArray();
            return true;
        }

        public void CopyWaitingRequestIds(List<int> result)
        {
            result.Clear();
            foreach (LinkedList<int> ids in waitingByStart.Values)
            {
                for (LinkedListNode<int> node = ids.First; node != null; node = node.Next)
                    result.Add(node.Value);
            }
        }

        public void ClearWaitingRequests()
        {
            waitingByStart.Clear();
            waitingCount = 0;
        }
    }
}
