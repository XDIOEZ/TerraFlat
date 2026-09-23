using System;
using UnityEngine;

/// <summary>
/// 玩家可复用的外部智能体操作接口。
/// 当前提供基于 WorldNavigationManager 的 MoveTo 命令；提交方必须先通过 GameController 外部控制租约，
/// 移动始终走玩家输入链和 Mover。运行中的请求、路径与控制者仅保留在内存，不写入玩家存档。
/// </summary>
public sealed class Mod_GameMCP_LLM : Module
{
    #region 身份与状态

    public const string ModuleId = "GameMCP_LLM";

    private const float MaxLoadedPathHorizon = 24f; // 未加载目标前，每段只前往已加载区域内的有限路点。
    private const float PathHorizonSearchStep = 1f;
    private const float MinimumPathHorizon = 1.25f;
    private const float WaypointArrivalDistance = 0.2f;
    private const float ProgressDistanceEpsilon = 0.05f;
    private const float StallRepathSeconds = 1.5f;
    private const int MaximumStallRepaths = 2;

    public enum OperationState
    {
        Idle,
        WaitingForPath,
        Moving,
        Reached,
        Failed,
        Cancelled
    }

    /// <summary>外部智能体操作的只读结果快照，供 GameMCP 和打包后的 LLM 适配器复用。</summary>
    public readonly struct MoveToStatus
    {
        public readonly OperationState State;
        public readonly string StopReason;
        public readonly Vector2 Target;
        public readonly Vector2 ResolvedDestination;
        public readonly bool HasResolvedDestination;
        public readonly bool ArrivedAtResolvedDestination;
        public readonly bool ReachesDestination;
        public readonly int PathRequestCount;
        public readonly int ReplanCount;
        public readonly int WaypointCount;
        public readonly int PathCost;

        public bool IsTerminal => State == OperationState.Reached ||
                                  State == OperationState.Failed ||
                                  State == OperationState.Cancelled;

        internal MoveToStatus(
            OperationState state,
            string stopReason,
            Vector2 target,
            Vector2 resolvedDestination,
            bool hasResolvedDestination,
            bool arrivedAtResolvedDestination,
            bool reachesDestination,
            int pathRequestCount,
            int replanCount,
            int waypointCount,
            int pathCost)
        {
            State = state;
            StopReason = stopReason ?? string.Empty;
            Target = target;
            ResolvedDestination = resolvedDestination;
            HasResolvedDestination = hasResolvedDestination;
            ArrivedAtResolvedDestination = arrivedAtResolvedDestination;
            ReachesDestination = reachesDestination;
            PathRequestCount = pathRequestCount;
            ReplanCount = replanCount;
            WaypointCount = waypointCount;
            PathCost = pathCost;
        }
    }

    public Ex_ModData ModData = new();
    public override string CanonicalModuleId => ModuleId;
    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = value as Ex_ModData;
    }

    private Player player;
    private GameController gameController;
    private Mover mover;
    private WorldNavigationManager navigation;
    private object operationOwner;
    private OperationState operationState;
    private string stopReason = string.Empty;
    private Vector2 requestedTarget;
    private Vector2 routeGoal;
    private Vector2 resolvedDestination;
    private bool hasResolvedDestination;
    private bool routeGoalIsFinal;
    private bool routeReachesDestination;
    private bool arrivedAtResolvedDestination;
    private Vector2[] waypoints = Array.Empty<Vector2>();
    private int waypointIndex;
    private int pathRequestId;
    private int requestGeneration;
    private int pathRequestCount;
    private int replanCount;
    private int pathCost;
    private int completedWaypointCount;
    private int pathInvalidationRevision;
    private int pathCostRevision;
    private int frontierPathFailures;
    private float maximumNextPathHorizon = MaxLoadedPathHorizon;
    private float tolerance;
    private float noProgressSeconds;
    private int stalledRepathCount;
    private Vector2 lastProgressPosition;
    private bool requestPending;
    private bool routeReady;

    public override void Awake()
    {
        base.Awake();
        if (ModData == null)
            ModData = new Ex_ModData();
        ModData.ID = ModuleId;
    }

    #endregion

    #region 模块生命周期

    public override void Load()
    {
        player = item as Player ?? GetComponentInParent<Player>();
        gameController = player != null ? player.GetComponentInChildren<GameController>(true) : null;
        mover = player != null ? player.GetComponentInChildren<Mover>(true) : null;
        navigation = WorldNavigationManager.ExistingInstance ?? WorldNavigationManager.Instance;
        ResetOperation();
    }

    /// <summary>操作状态是短期运行态，不写入玩家存档。</summary>
    public override void Save()
    {
    }

    /// <summary>玩家卸载或世界切换时取消未完成寻路并清空本模块持有的输入。</summary>
    public override void Unload()
    {
        CancelActiveOperation(OperationState.Cancelled, "module_unloaded");
        player = null;
        gameController = null;
        mover = null;
        navigation = null;
    }

    /// <summary>由 Player 的正式模块 Tick 推进外部导航，不直接驱动刚体或 Transform。</summary>
    public override void ModUpdate(float deltaTime)
    {
        if (operationState != OperationState.WaitingForPath && operationState != OperationState.Moving)
            return;

        if (player == null || gameController == null || mover == null || operationOwner == null ||
            !gameController.IsExternalGameplayControlOwner(operationOwner))
        {
            CancelActiveOperation(OperationState.Failed, "control_lost");
            return;
        }

        navigation = WorldNavigationManager.ExistingInstance ?? WorldNavigationManager.Instance;
        if (navigation == null || !navigation.IsNavigationReady)
        {
            CancelActiveOperation(OperationState.Failed, "navigation_unavailable");
            return;
        }

        if (WorldTopologyRuntime.ShortestDelta(player.transform.position, requestedTarget).magnitude <= tolerance)
        {
            arrivedAtResolvedDestination = true;
            CompleteOperation(OperationState.Reached, "reached");
            return;
        }

        TrackPlayerProgress(deltaTime);
        if (operationState == OperationState.Failed)
            return;

        if (requestPending)
        {
            gameController.TrySetExternalMoveInput(operationOwner, Vector2.zero);
            return;
        }

        if (!routeReady)
        {
            RequestNavigationPath();
            return;
        }

        if (HasNavigationRevisionChanged())
        {
            bool pathIsValid = navigation.IsPathStillValid(player.transform.position, waypoints, waypointIndex);
            bool pathCostChanged = navigation.PathCostRevision != pathCostRevision;
            UpdateObservedRevisions();
            if (!pathIsValid || pathCostChanged)
            {
                RequestNavigationPath();
                return;
            }
        }

        FollowCurrentRoute();
    }

    #endregion

    #region 外部操作接口

    /// <summary>在调用者持有玩家外部控制租约时提交一次基于项目导航网格的目标移动。</summary>
    public bool TryBeginMoveTo(object requester, Vector2 target, float arrivalTolerance, out string error)
    {
        error = string.Empty;
        if (requester == null || gameController == null || player == null || mover == null)
        {
            error = "player_operation_unavailable";
            return false;
        }

        if (!gameController.IsExternalGameplayControlOwner(requester))
        {
            error = "control_lease_required";
            return false;
        }

        if (operationState == OperationState.WaitingForPath || operationState == OperationState.Moving)
        {
            error = "operation_busy";
            return false;
        }

        navigation = WorldNavigationManager.ExistingInstance ?? WorldNavigationManager.Instance;
        if (navigation == null || !navigation.IsNavigationReady)
        {
            error = "navigation_unavailable";
            return false;
        }

        if (!IsFinite(target.x) || !IsFinite(target.y))
        {
            error = "invalid_target";
            return false;
        }

        ResetOperation();
        operationOwner = requester;
        requestedTarget = WorldTopologyRuntime.NormalizePosition(target);
        tolerance = Mathf.Clamp(arrivalTolerance, 0.05f, 2f);
        lastProgressPosition = player.transform.position;
        operationState = OperationState.WaitingForPath;

        if (WorldTopologyRuntime.ShortestDelta(player.transform.position, requestedTarget).magnitude <= tolerance)
        {
            arrivedAtResolvedDestination = true;
            CompleteOperation(OperationState.Reached, "reached");
        }

        return true;
    }

    /// <summary>读取本操作的只读状态；只有提交操作的同一调用者可以读取。</summary>
    public bool TryGetMoveToStatus(object requester, out MoveToStatus status)
    {
        if (requester == null || !ReferenceEquals(operationOwner, requester))
        {
            status = default;
            return false;
        }

        status = new MoveToStatus(
            operationState,
            stopReason,
            requestedTarget,
            resolvedDestination,
            hasResolvedDestination,
            arrivedAtResolvedDestination,
            routeReachesDestination,
            pathRequestCount,
            replanCount,
            operationState == OperationState.WaitingForPath || operationState == OperationState.Moving
                ? waypoints?.Length ?? 0
                : completedWaypointCount,
            pathCost);
        return true;
    }

    /// <summary>取消由同一调用者提交的操作，并确保外部移动输入归零。</summary>
    public bool TryCancelMoveTo(object requester)
    {
        if (requester == null || !ReferenceEquals(operationOwner, requester))
            return false;

        CancelActiveOperation(OperationState.Cancelled, "cancelled");
        return true;
    }

    #endregion

    #region 导航请求与路点跟随

    private void RequestNavigationPath()
    {
        if (player == null || navigation == null || !navigation.IsNavigationReady)
        {
            CompleteOperation(OperationState.Failed, "navigation_unavailable");
            return;
        }

        if (!TrySelectLoadedRouteGoal(out routeGoal, out routeGoalIsFinal))
        {
            CompleteOperation(OperationState.Failed, "no_loaded_route_goal");
            return;
        }

        if (routeReady || pathRequestCount > 0)
            replanCount++;

        routeReady = false;
        requestPending = true;
        operationState = OperationState.WaitingForPath;
        waypoints = Array.Empty<Vector2>();
        waypointIndex = 0;
        gameController.TrySetExternalMoveInput(operationOwner, Vector2.zero);

        int generation = ++requestGeneration;
        pathRequestCount++;
        pathRequestId = navigation.RequestPath(
            player.transform.position,
            routeGoal,
            result => OnPathCompleted(generation, result));
    }

    /// <summary>远目标尚未加载时，选取朝目标方向最近的已加载可走分段终点。</summary>
    private bool TrySelectLoadedRouteGoal(out Vector2 selectedGoal, out bool reachesRequestedTarget)
    {
        selectedGoal = requestedTarget;
        reachesRequestedTarget = false;
        if (navigation.TryGetCell(requestedTarget, out _, out _))
        {
            reachesRequestedTarget = true;
            return true;
        }

        Vector2 currentPosition = player.transform.position;
        Vector2 delta = WorldTopologyRuntime.ShortestDelta(currentPosition, requestedTarget);
        float targetDistance = delta.magnitude;
        if (targetDistance <= MinimumPathHorizon)
            return false;

        Vector2 direction = delta / targetDistance;
        float maximumDistance = Mathf.Min(maximumNextPathHorizon, targetDistance - MinimumPathHorizon);
        for (float distance = maximumDistance; distance >= MinimumPathHorizon; distance -= PathHorizonSearchStep)
        {
            Vector2 candidate = WorldTopologyRuntime.NormalizePosition(currentPosition + direction * distance);
            if (!navigation.TryGetCell(candidate, out _, out bool walkable) || !walkable)
                continue;

            if (WorldTopologyRuntime.ShortestDelta(currentPosition, candidate).magnitude < MinimumPathHorizon)
                continue;

            selectedGoal = candidate;
            return true;
        }

        return false;
    }

    private void OnPathCompleted(int generation, WorldNavigationPathResult result)
    {
        if (generation != requestGeneration || operationOwner == null ||
            operationState == OperationState.Cancelled || operationState == OperationState.Failed)
        {
            return;
        }

        pathRequestId = 0;
        requestPending = false;
        noProgressSeconds = 0f;
        lastProgressPosition = player != null ? player.transform.position : Vector2.zero;

        if (!result.Success)
        {
            if (!routeGoalIsFinal && frontierPathFailures < 2)
            {
                frontierPathFailures++;
                float failedHorizon = player == null
                    ? MinimumPathHorizon
                    : WorldTopologyRuntime.ShortestDelta(player.transform.position, routeGoal).magnitude;
                maximumNextPathHorizon = Mathf.Max(
                    MinimumPathHorizon,
                    Mathf.Min(MaxLoadedPathHorizon, failedHorizon * 0.5f));
                RequestNavigationPath();
                return;
            }

            CompleteOperation(
                OperationState.Failed,
                result.RejectedByPathCost ? "path_cost_rejected" : "path_not_found");
            return;
        }

        resolvedDestination = result.ResolvedDestination;
        hasResolvedDestination = true;
        routeReachesDestination = result.ReachesDestination;
        pathCost = result.TotalCost;
        frontierPathFailures = 0;
        maximumNextPathHorizon = MaxLoadedPathHorizon;
        waypoints = result.Waypoints ?? Array.Empty<Vector2>();
        waypointIndex = 0;
        routeReady = waypoints.Length > 0;
        operationState = OperationState.Moving;
        UpdateObservedRevisions();

        if (!routeReady)
        {
            HandleRouteEnd();
            return;
        }

        AdvanceReachedWaypoints();
    }

    private void FollowCurrentRoute()
    {
        AdvanceReachedWaypoints();
        if (operationState != OperationState.Moving || requestPending)
            return;

        if (!routeReady || waypoints == null || waypointIndex >= waypoints.Length)
        {
            HandleRouteEnd();
            return;
        }

        Vector2 delta = WorldTopologyRuntime.ShortestDelta(player.transform.position, waypoints[waypointIndex]);
        if (delta.sqrMagnitude <= WaypointArrivalDistance * WaypointArrivalDistance)
        {
            waypointIndex++;
            AdvanceReachedWaypoints();
            if (operationState == OperationState.Moving && waypointIndex >= waypoints.Length)
                HandleRouteEnd();
            return;
        }

        if (!gameController.TrySetExternalMoveInput(operationOwner, delta.normalized))
            CompleteOperation(OperationState.Failed, "control_lost");
    }

    private void AdvanceReachedWaypoints()
    {
        if (waypoints == null)
            return;

        float arrivalDistanceSqr = WaypointArrivalDistance * WaypointArrivalDistance;
        while (waypointIndex < waypoints.Length)
        {
            Vector2 delta = WorldTopologyRuntime.ShortestDelta(player.transform.position, waypoints[waypointIndex]);
            if (delta.sqrMagnitude > arrivalDistanceSqr)
                return;
            waypointIndex++;
        }

        if (operationState == OperationState.Moving)
            HandleRouteEnd();
    }

    private void HandleRouteEnd()
    {
        if (player == null)
        {
            CompleteOperation(OperationState.Failed, "player_unavailable");
            return;
        }

        Vector2 targetDelta = WorldTopologyRuntime.ShortestDelta(player.transform.position, requestedTarget);
        if (targetDelta.magnitude <= tolerance)
        {
            arrivedAtResolvedDestination = true;
            CompleteOperation(OperationState.Reached, "reached");
            return;
        }

        if (routeGoalIsFinal && routeReachesDestination && hasResolvedDestination)
        {
            float requestedToResolvedDistance = WorldTopologyRuntime.ShortestDelta(
                requestedTarget,
                resolvedDestination).magnitude;
            float resolvedDistance = WorldTopologyRuntime.ShortestDelta(
                player.transform.position,
                resolvedDestination).magnitude;
            if (requestedToResolvedDistance > tolerance &&
                resolvedDistance <= WaypointArrivalDistance + tolerance)
            {
                arrivedAtResolvedDestination = true;
                CompleteOperation(OperationState.Failed, "destination_unwalkable");
                return;
            }
        }

        completedWaypointCount = Mathf.Max(completedWaypointCount, waypoints.Length);
        routeReady = false;
        waypoints = Array.Empty<Vector2>();
        waypointIndex = 0;
        RequestNavigationPath();
    }

    #endregion

    #region 路径失效与运行态清理

    private bool HasNavigationRevisionChanged()
    {
        return navigation.PathInvalidationRevision != pathInvalidationRevision ||
               navigation.PathCostRevision != pathCostRevision;
    }

    private void UpdateObservedRevisions()
    {
        if (navigation == null)
            return;

        pathInvalidationRevision = navigation.PathInvalidationRevision;
        pathCostRevision = navigation.PathCostRevision;
    }

    private void TrackPlayerProgress(float deltaTime)
    {
        if (operationState == OperationState.WaitingForPath || player == null)
            return;

        Vector2 currentPosition = player.transform.position;
        float movedDistance = WorldTopologyRuntime.ShortestDelta(lastProgressPosition, currentPosition).magnitude;
        if (movedDistance >= ProgressDistanceEpsilon)
        {
            lastProgressPosition = currentPosition;
            noProgressSeconds = 0f;
            stalledRepathCount = 0;
            return;
        }

        noProgressSeconds += Mathf.Max(0f, deltaTime);
        if (noProgressSeconds < StallRepathSeconds)
            return;

        if (stalledRepathCount >= MaximumStallRepaths)
        {
            CompleteOperation(OperationState.Failed, "navigation_stalled");
            return;
        }

        stalledRepathCount++;
        noProgressSeconds = 0f;
        RequestNavigationPath();
    }

    private void CompleteOperation(OperationState state, string reason)
    {
        completedWaypointCount = Mathf.Max(completedWaypointCount, waypoints?.Length ?? 0);
        CancelPendingPath();
        if (gameController != null && operationOwner != null &&
            gameController.IsExternalGameplayControlOwner(operationOwner))
        {
            gameController.TrySetExternalMoveInput(operationOwner, Vector2.zero);
        }

        operationState = state;
        stopReason = reason ?? string.Empty;
        requestPending = false;
        routeReady = false;
        waypoints = Array.Empty<Vector2>();
        waypointIndex = 0;
    }

    private void CancelActiveOperation(OperationState state, string reason)
    {
        if (operationState == OperationState.WaitingForPath || operationState == OperationState.Moving)
            CompleteOperation(state, reason);
    }

    private void CancelPendingPath()
    {
        requestGeneration++;
        if (pathRequestId > 0 && navigation != null)
            navigation.CancelPath(pathRequestId);
        pathRequestId = 0;
        requestPending = false;
    }

    private void ResetOperation()
    {
        CancelPendingPath();
        operationOwner = null;
        operationState = OperationState.Idle;
        stopReason = string.Empty;
        requestedTarget = default;
        routeGoal = default;
        resolvedDestination = default;
        hasResolvedDestination = false;
        routeGoalIsFinal = false;
        routeReachesDestination = false;
        arrivedAtResolvedDestination = false;
        waypoints = Array.Empty<Vector2>();
        waypointIndex = 0;
        pathRequestCount = 0;
        replanCount = 0;
        pathCost = 0;
        completedWaypointCount = 0;
        frontierPathFailures = 0;
        maximumNextPathHorizon = MaxLoadedPathHorizon;
        tolerance = 0.25f;
        noProgressSeconds = 0f;
        stalledRepathCount = 0;
        lastProgressPosition = Vector2.zero;
        requestPending = false;
        routeReady = false;
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    #endregion
}
