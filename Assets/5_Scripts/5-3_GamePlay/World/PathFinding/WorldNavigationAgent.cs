using System;
using UnityEngine;

/// <summary>导航代理对当前目的地请求给出的正式结果。</summary>
public enum WorldNavigationDestinationResult
{
    None,
    Pending,
    Accepted,
    Reached,
    RejectedByPathCost
}

/// <summary>
/// Lightweight movement client for WorldNavigationManager. It owns only one request and one
/// compact waypoint list, so thousands of agents do not each run their own path search.
/// </summary>
[DisallowMultipleComponent]
public sealed class WorldNavigationAgent : MonoBehaviour
{
    private const float MinimumWaypointDistance = 0.3f;
    private const float MinimumDestinationChangeDistance = 0.5f;
    private static readonly System.Collections.Generic.List<WorldNavigationAgent> ActiveAgentRegistry = new();

    [SerializeField, Min(0.01f)] private float stopDistance = 0.5f;
    [SerializeField, Min(0.01f)] private float waypointDistance = MinimumWaypointDistance;
    [SerializeField, Min(0.05f)] private float repathInterval = 0.25f;
    [SerializeField, Min(0.1f)] private float maxRetryInterval = 2f;
    [SerializeField, Min(0.001f)] private float destinationChangeThreshold = MinimumDestinationChangeDistance;
    [SerializeField, Min(0.1f)] private float stalledRepathDelay = 0.8f;
    [SerializeField, Min(0.001f)] private float progressDistanceThreshold = 0.02f;

    private Rigidbody2D body;
    private Mover surfaceMover;
    private WorldNavigationManager navigationManager;
    private Vector2[] waypoints = Array.Empty<Vector2>();
    private Vector2 destination;
    private Vector2 submittedDestination;
    private Vector2 activePathDestination;
    private Vector2 resolvedDestination;
    private Vector2 lastProgressPosition;
    private Vector2 fallbackVelocity;
    private int waypointIndex;
    private int requestId;
    private int pathRevision = -1;
    private int pathCostRevision = -1;
    private int pathInvalidationRevision = -1;
    private int queuedValidationRevision = -1;
    private int immediatelyValidatedWaypointIndex = -1;
    private int lastFailureRevision = -1;
    // 当前目标使用的路径代价上限，int.MaxValue 表示不限制。
    private int destinationPathCostLimitExclusive = int.MaxValue;
    // 当前异步请求提交时的路径代价上限。
    private int submittedPathCostLimitExclusive = int.MaxValue;
    private int consecutiveFailures;
    private float nextRequestTime;
    private float nextPathValidationTime;
    private float lastProgressTime;
    private float schedulingPhase;
    private bool hasDestination;
    private bool hasPath;
    private bool pathReachesDestination;
    private bool destinationDirty;

    public float MaxSpeed { get; set; } = 3f;
    public bool CanMove { get; set; } = true;
    public bool HasDestination => hasDestination;
    public bool HasPath => hasPath;
    public bool PathPending => requestId > 0;
    public bool ReachedDestination { get; private set; } = true;
    public Vector2 Destination => destination;
    public Vector2 ResolvedDestination => resolvedDestination;
    public Vector2 Velocity => body != null ? body.velocity : fallbackVelocity;
    public int PathRevision => pathRevision;
    public WorldNavigationDestinationResult DestinationResult { get; private set; }

    private bool DestinationRejectedByPathCost =>
        DestinationResult == WorldNavigationDestinationResult.RejectedByPathCost;

    internal static System.Collections.Generic.IReadOnlyList<WorldNavigationAgent> ActiveAgents =>
        ActiveAgentRegistry;

    private Vector2 CurrentPosition => body != null ? body.position : (Vector2)transform.position;
    private float EffectiveDestinationChangeDistance =>
        Mathf.Max(MinimumDestinationChangeDistance, destinationChangeThreshold);
    private float EffectiveWaypointDistance =>
        Mathf.Max(MinimumWaypointDistance, waypointDistance);

    private void Awake()
    {
        schedulingPhase = (GetInstanceID() & 15) / 16f;
        Bind(GetComponent<Rigidbody2D>());
        nextRequestTime = Time.unscaledTime + schedulingPhase * Mathf.Max(0.05f, repathInterval);
    }

    private void OnEnable()
    {
        if (!ActiveAgentRegistry.Contains(this))
            ActiveAgentRegistry.Add(this);
    }

    private void OnDisable()
    {
        ActiveAgentRegistry.Remove(this);
        CancelPendingRequest();
        ApplyVelocity(Vector2.zero, 0f);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetActiveAgentRegistry()
    {
        ActiveAgentRegistry.Clear();
    }

    public void Bind(Rigidbody2D rigidbody2D)
    {
        Bind(rigidbody2D, null);
    }

    public void Bind(Rigidbody2D rigidbody2D, WorldNavigationManager manager)
    {
        body = rigidbody2D != null ? rigidbody2D : GetComponent<Rigidbody2D>();
        surfaceMover = body != null ? body.GetComponentInParent<Mover>() : null;
        if (manager != null)
            navigationManager = manager;
        lastProgressPosition = CurrentPosition;
        lastProgressTime = Time.time + schedulingPhase * 0.2f;
    }

    public void Configure(
        float arrivalDistance,
        float destinationThreshold,
        float stuckDelay,
        float progressThreshold)
    {
        stopDistance = Mathf.Max(0.01f, arrivalDistance);
        stalledRepathDelay = Mathf.Max(0.1f, stuckDelay);
        progressDistanceThreshold = Mathf.Max(0.001f, progressThreshold);
        waypointDistance = Mathf.Max(
            MinimumWaypointDistance,
            Mathf.Min(0.75f, stopDistance));
        destinationChangeThreshold = Mathf.Max(
            MinimumDestinationChangeDistance,
            destinationThreshold);
    }

    /// <summary>提交不限制总代价的普通移动目标。</summary>
    public void SetDestination(Vector2 target, bool forceRepath = false)
    {
        SetDestinationInternal(target, int.MaxValue, forceRepath);
    }

    /// <summary>提交只有总代价严格小于上限时才接受的移动目标。</summary>
    public void SetCostLimitedDestination(
        Vector2 target,
        int maximumPathCostExclusive,
        bool forceRepath = false)
    {
        SetDestinationInternal(target, Mathf.Max(1, maximumPathCostExclusive), forceRepath);
    }

    /// <summary>统一维护目标变化、代价策略切换和重寻路状态。</summary>
    private void SetDestinationInternal(
        Vector2 target,
        int maximumPathCostExclusive,
        bool forceRepath)
    {
        bool firstDestination = !hasDestination;
        bool pathCostPolicyChanged =
            destinationPathCostLimitExclusive != maximumPathCostExclusive;
        float destinationChangeDistance = EffectiveDestinationChangeDistance;
        bool changed = firstDestination ||
                       WorldTopologyRuntime.SqrDistance(target, submittedDestination) >
                       destinationChangeDistance * destinationChangeDistance;

        destination = target;
        hasDestination = true;
        CanMove = true;
        ReachedDestination = false;

        if (pathCostPolicyChanged)
        {
            destinationPathCostLimitExclusive = maximumPathCostExclusive;
            CancelPendingRequest();
            InvalidateCurrentPath();
            DestinationResult = WorldNavigationDestinationResult.Pending;
            nextRequestTime = 0f;
        }

        if (changed)
        {
            consecutiveFailures = 0;
            DestinationResult = WorldNavigationDestinationResult.Pending;
        }

        if (!pathCostPolicyChanged &&
            !firstDestination &&
            hasPath &&
            pathReachesDestination &&
            WorldNavigationGrid.WorldToCell(target) == WorldNavigationGrid.WorldToCell(activePathDestination) &&
            WorldNavigationGrid.WorldToCell(resolvedDestination) == WorldNavigationGrid.WorldToCell(target))
        {
            resolvedDestination = target;
            activePathDestination = target;
            if (requestId <= 0)
                submittedDestination = target;
            if (waypoints.Length > 0)
                waypoints[^1] = target;

            if (!forceRepath)
                return;
        }

        if (!changed && !forceRepath && !pathCostPolicyChanged)
            return;

        destinationDirty = true;

        if (forceRepath)
        {
            CancelPendingRequest();
            InvalidateCurrentPath();
            DestinationResult = WorldNavigationDestinationResult.Pending;
            nextRequestTime = 0f;
        }
    }

    public void Stop(bool clearDestination = false)
    {
        CanMove = false;
        ReachedDestination = true;
        CancelPendingRequest();
        if (hasDestination &&
            (!hasPath || WorldTopologyRuntime.SqrDistance(destination, activePathDestination) > 0.0001f))
            destinationDirty = true;
        ApplyVelocity(Vector2.zero, Time.deltaTime);

        if (!clearDestination)
            return;

        CancelPendingRequest();
        hasDestination = false;
        hasPath = false;
        pathReachesDestination = false;
        destinationDirty = false;
        DestinationResult = WorldNavigationDestinationResult.None;
        destinationPathCostLimitExclusive = int.MaxValue;
        submittedPathCostLimitExclusive = int.MaxValue;
        waypoints = Array.Empty<Vector2>();
        waypointIndex = 0;
        activePathDestination = default;
    }

    public void ForceRepath()
    {
        if (!hasDestination)
            return;

        CancelPendingRequest();
        InvalidateCurrentPath();
        DestinationResult = WorldNavigationDestinationResult.Pending;
        destinationDirty = true;
        nextRequestTime = 0f;
    }

    public void Tick(float deltaTime)
    {
        if (!CanMove || !hasDestination)
        {
            ApplyVelocity(Vector2.zero, deltaTime);
            return;
        }

        Vector2 current = CurrentPosition;
        if (HasReachedCurrentDestination(current) || HasReachedResolvedDestination(current))
        {
            CompleteDestination();
            return;
        }

        WorldNavigationManager navigation = navigationManager != null
            ? navigationManager
            : WorldNavigationManager.Instance;
        navigationManager = navigation;
        if (navigation == null || !navigation.IsNavigationReady)
        {
            ApplyVelocity(Vector2.zero, deltaTime);
            return;
        }

        if (hasPath &&
            (pathRevision != navigation.GridRevision ||
             pathCostRevision != navigation.PathCostRevision))
            ValidateStalePath(navigation, current);

        if (!hasPath &&
            consecutiveFailures > 0 &&
            lastFailureRevision != navigation.GridRevision)
        {
            consecutiveFailures = 0;
            lastFailureRevision = navigation.GridRevision;
            nextRequestTime = Time.unscaledTime;
        }

        bool shouldRequestPath =
            destinationDirty || (!hasPath && !DestinationRejectedByPathCost);
        if (shouldRequestPath &&
            requestId <= 0 &&
            Time.unscaledTime >= nextRequestTime)
            SubmitPathRequest(navigation, current);

        if (!hasPath || waypointIndex >= waypoints.Length)
        {
            ApplyVelocity(Vector2.zero, deltaTime);
            return;
        }

        AdvanceReachedWaypoints(current);
        if (hasPath &&
            (pathRevision != navigation.GridRevision ||
             pathCostRevision != navigation.PathCostRevision) &&
            !ValidateStalePath(navigation, current))
        {
            ApplyVelocity(Vector2.zero, deltaTime);
            return;
        }

        if (!hasPath || waypointIndex >= waypoints.Length)
        {
            if (DestinationRejectedByPathCost)
            {
                ReachedDestination = false;
                ApplyVelocity(Vector2.zero, deltaTime);
                return;
            }

            if (HasReachedCurrentDestination(current) || HasReachedResolvedDestination(current))
                CompleteDestination();

            else
            {
                InvalidateCurrentPath();
                ReachedDestination = false;
                destinationDirty = true;
                if (requestId <= 0)
                    nextRequestTime = Time.unscaledTime;
                ApplyVelocity(Vector2.zero, deltaTime);
            }
            return;
        }

        Vector2 waypoint = waypoints[waypointIndex];
        Vector2 delta = WorldTopologyRuntime.ShortestDelta(current, waypoint);
        float distance = delta.magnitude;
        if (distance <= 0.0001f)
        {
            ApplyVelocity(Vector2.zero, deltaTime);
            return;
        }

        float speed = Mathf.Max(0f, MaxSpeed);
        if (deltaTime > 0f)
            speed = Mathf.Min(speed, distance / deltaTime);

        ApplyVelocity(delta / distance * speed, deltaTime);
        RecoverIfStalled(current);
    }

    private void SubmitPathRequest(WorldNavigationManager navigation, Vector2 current)
    {
        if (requestId > 0)
            return;

        navigationManager = navigation;
        submittedDestination = destination;
        submittedPathCostLimitExclusive = destinationPathCostLimitExclusive;
        DestinationResult = WorldNavigationDestinationResult.Pending;
        destinationDirty = false;
        nextRequestTime = Time.unscaledTime + Mathf.Max(0.05f, repathInterval);
        requestId = navigation.RequestPath(current, submittedDestination, OnPathCompleted);
    }

    private void OnPathCompleted(WorldNavigationPathResult result)
    {
        if (result.RequestId != requestId)
            return;

        requestId = 0;
        WorldNavigationManager navigation = navigationManager;
        if (!result.Success ||
            navigation == null ||
            result.GridRevision != navigation.GridRevision ||
            result.PathCostRevision != navigation.PathCostRevision ||
            result.Waypoints.Length == 0)
        {
            if (!hasPath)
                ApplyVelocity(Vector2.zero, Time.deltaTime);
            destinationDirty = true;
            consecutiveFailures = Mathf.Min(consecutiveFailures + 1, 8);
            lastFailureRevision = result.GridRevision;
            float retryDelay = Mathf.Min(
                Mathf.Max(repathInterval, 0.05f) * Mathf.Pow(2f, Mathf.Min(consecutiveFailures, 4)),
                Mathf.Max(0.1f, maxRetryInterval));
            nextRequestTime = Mathf.Max(nextRequestTime, Time.unscaledTime + retryDelay);
            return;
        }

        float destinationChangeDistance = EffectiveDestinationChangeDistance;
        bool destinationMovedToAnotherCell =
            WorldTopologyRuntime.SqrDistance(destination, submittedDestination) >
            destinationChangeDistance * destinationChangeDistance &&
            WorldNavigationGrid.WorldToCell(destination) !=
            WorldNavigationGrid.WorldToCell(submittedDestination);

        if (result.TotalCost >= submittedPathCostLimitExclusive)
        {
            // 拒绝新路线时不覆盖仍在执行的旧路线；目标再次明显移动后才重新评估。
            DestinationResult = WorldNavigationDestinationResult.RejectedByPathCost;
            destinationDirty = destinationMovedToAnotherCell;
            if (destinationDirty)
                nextRequestTime = Time.unscaledTime;
            if (!hasPath)
                ApplyVelocity(Vector2.zero, Time.deltaTime);
            return;
        }

        // 首个路点已经由带权寻路和带权平滑生成；不再按几何 LOS 跨路点跳跃，
        // 避免把绕开的河流或其他高代价地形重新拉直穿过。
        int initialWaypoint = 0;

        waypoints = result.Waypoints;
        waypointIndex = initialWaypoint;
        resolvedDestination = result.ResolvedDestination;
        activePathDestination = result.RequestedDestination;
        pathReachesDestination = result.ReachesDestination;
        pathRevision = result.GridRevision;
        pathCostRevision = result.PathCostRevision;
        pathInvalidationRevision = navigation.PathInvalidationRevision;
        queuedValidationRevision = -1;
        immediatelyValidatedWaypointIndex = -1;
        hasPath = true;
        DestinationResult = WorldNavigationDestinationResult.Accepted;
        ReachedDestination = false;
        consecutiveFailures = 0;

        if (destinationMovedToAnotherCell)
        {
            destinationDirty = true;
            nextRequestTime = Time.unscaledTime;
        }
        else if (WorldNavigationGrid.WorldToCell(destination) ==
                 WorldNavigationGrid.WorldToCell(activePathDestination) &&
                 WorldNavigationGrid.WorldToCell(resolvedDestination) ==
                 WorldNavigationGrid.WorldToCell(destination))
        {
            resolvedDestination = destination;
            activePathDestination = destination;
            waypoints[^1] = destination;
            destinationDirty = false;
        }

        lastProgressPosition = CurrentPosition;
        lastProgressTime = Time.time;
    }

    private void AdvanceReachedWaypoints(Vector2 current)
    {
        while (waypointIndex < waypoints.Length)
        {
            bool finalWaypoint = waypointIndex == waypoints.Length - 1;
            float threshold = finalWaypoint ? stopDistance : EffectiveWaypointDistance;
            if (WorldTopologyRuntime.SqrDistance(current, waypoints[waypointIndex]) > threshold * threshold)
                break;

            waypointIndex++;
        }

        if (waypointIndex >= waypoints.Length)
            hasPath = false;
    }

    private bool ValidateStalePath(WorldNavigationManager navigation, Vector2 current)
    {
        if (!hasPath ||
            (pathRevision == navigation.GridRevision &&
             pathCostRevision == navigation.PathCostRevision))
            return hasPath;

        if (pathCostRevision != navigation.PathCostRevision)
        {
            if (DestinationRejectedByPathCost)
            {
                // 已决定走完旧路线时，纯代价变化不应强制替换这条仍可通行的路线。
                pathCostRevision = navigation.PathCostRevision;
            }
            else
            {
                // 代价版本变化代表旧路线已不再保证最优，必须重新提交带权寻路。
                InvalidateCurrentPath();
                destinationDirty = true;
                nextRequestTime = Time.unscaledTime;
                return false;
            }
        }

        int currentInvalidationRevision = navigation.PathInvalidationRevision;
        if (pathInvalidationRevision == currentInvalidationRevision)
        {
            // Loading a neighbouring chunk or removing an obstacle only opens cells. Existing
            // routes remain valid, so hundreds of agents can accept the revision without LOS work.
            pathRevision = navigation.GridRevision;
            queuedValidationRevision = -1;
            immediatelyValidatedWaypointIndex = -1;
            return true;
        }

        if (queuedValidationRevision != currentInvalidationRevision)
        {
            queuedValidationRevision = currentInvalidationRevision;
            immediatelyValidatedWaypointIndex = -1;
            nextPathValidationTime = Time.unscaledTime +
                                     schedulingPhase * Mathf.Max(0.05f, repathInterval);
        }

        // A newly placed building must stop an agent before its next movement step. The more
        // expensive validation of the complete remaining route stays staggered across agents.
        if (waypointIndex < 0 || waypointIndex >= waypoints.Length ||
            (immediatelyValidatedWaypointIndex != waypointIndex &&
             !navigation.HasGridLineOfSight(current, waypoints[waypointIndex])))
        {
            InvalidateCurrentPath();
            destinationDirty = !DestinationRejectedByPathCost;
            if (destinationDirty)
                nextRequestTime = Time.unscaledTime;
            return false;
        }

        immediatelyValidatedWaypointIndex = waypointIndex;
        if (Time.unscaledTime < nextPathValidationTime)
            return true;

        if (navigation.IsPathStillValid(current, waypoints, waypointIndex))
        {
            pathRevision = navigation.GridRevision;
            pathInvalidationRevision = currentInvalidationRevision;
            queuedValidationRevision = -1;
            immediatelyValidatedWaypointIndex = -1;
            return true;
        }

        InvalidateCurrentPath();
        destinationDirty = !DestinationRejectedByPathCost;
        if (destinationDirty)
            nextRequestTime = Time.unscaledTime;
        return false;
    }

    private void RecoverIfStalled(Vector2 current)
    {
        if (WorldTopologyRuntime.SqrDistance(lastProgressPosition, current) >=
            progressDistanceThreshold * progressDistanceThreshold)
        {
            lastProgressPosition = current;
            lastProgressTime = Time.time;
            return;
        }

        if (Time.time - lastProgressTime < stalledRepathDelay)
            return;

        lastProgressPosition = current;
        lastProgressTime = Time.time;
        if (DestinationRejectedByPathCost)
        {
            // 旧路线已经无法继续时直接结束，不为已判定超限的目标创建新路线。
            InvalidateCurrentPath();
            return;
        }

        ForceRepath();
    }

    private void CompleteDestination()
    {
        CancelPendingRequest();
        ReachedDestination = true;
        DestinationResult = WorldNavigationDestinationResult.Reached;
        hasPath = false;
        pathReachesDestination = false;
        destinationDirty = false;
        waypointIndex = 0;
        waypoints = Array.Empty<Vector2>();
        ApplyVelocity(Vector2.zero, Time.deltaTime);
    }

    private bool HasReachedCurrentDestination(Vector2 current)
        => WorldTopologyRuntime.SqrDistance(current, destination) <= stopDistance * stopDistance;

    private bool HasReachedResolvedDestination(Vector2 current)
    {
        if (!pathReachesDestination ||
            WorldTopologyRuntime.SqrDistance(destination, activePathDestination) > 0.0001f)
        {
            return false;
        }

        return WorldTopologyRuntime.SqrDistance(current, resolvedDestination) <= stopDistance * stopDistance;
    }

    private void InvalidateCurrentPath()
    {
        hasPath = false;
        pathReachesDestination = false;
        waypointIndex = 0;
        waypoints = Array.Empty<Vector2>();
        pathRevision = -1;
        pathCostRevision = -1;
        pathInvalidationRevision = -1;
        queuedValidationRevision = -1;
        immediatelyValidatedWaypointIndex = -1;
    }

    private void CancelPendingRequest()
    {
        if (requestId <= 0)
            return;

        navigationManager?.CancelPath(requestId);
        requestId = 0;
    }

    private void ApplyVelocity(Vector2 velocity, float deltaTime)
    {
        fallbackVelocity = velocity;
        if (body != null)
        {
            if (deltaTime <= 0f)
            {
                body.velocity = Vector2.zero;
                return;
            }

            surfaceMover ??= body.GetComponentInParent<Mover>();
            body.velocity = surfaceMover == null
                ? velocity
                : surfaceMover.SmoothSurfaceVelocity(body.velocity, velocity, deltaTime);
            return;
        }

        if (deltaTime > 0f && velocity.sqrMagnitude > 0f)
            transform.position += (Vector3)(velocity * deltaTime);
    }

    internal bool CopyRemainingDebugPath(System.Collections.Generic.List<Vector3> output)
    {
        output.Clear();
        if (!isActiveAndEnabled || !hasPath || waypointIndex >= waypoints.Length)
            return false;

        float z = transform.position.z - 0.05f;
        Vector2 current = CurrentPosition;
        output.Add(new Vector3(current.x, current.y, z));

        int firstWaypoint = Mathf.Max(0, waypointIndex);
        Vector2 previous = current;
        for (int i = firstWaypoint; i < waypoints.Length; i++)
        {
            Vector2 waypoint = WorldTopologyRuntime.NearestImagePosition(previous, waypoints[i]);
            Vector3 point = new(waypoint.x, waypoint.y, z);
            if ((point - output[^1]).sqrMagnitude > 0.0001f)
                output.Add(point);
            previous = waypoint;
        }

        return output.Count > 1;
    }

    internal bool TryGetDebugDestination(out Vector3 point)
    {
        Vector2 target = destination;
        point = new Vector3(target.x, target.y, transform.position.z - 0.05f);
        return isActiveAndEnabled && hasDestination && !ReachedDestination;
    }
}
