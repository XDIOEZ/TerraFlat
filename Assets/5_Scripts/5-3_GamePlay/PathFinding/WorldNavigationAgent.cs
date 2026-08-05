using System;
using UnityEngine;

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
    private int pathInvalidationRevision = -1;
    private int queuedValidationRevision = -1;
    private int immediatelyValidatedWaypointIndex = -1;
    private int lastFailureRevision = -1;
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

    public void SetDestination(Vector2 target, bool forceRepath = false)
    {
        bool firstDestination = !hasDestination;
        float destinationChangeDistance = EffectiveDestinationChangeDistance;
        bool changed = firstDestination ||
                       (target - submittedDestination).sqrMagnitude >
                       destinationChangeDistance * destinationChangeDistance;

        destination = target;
        hasDestination = true;
        CanMove = true;
        ReachedDestination = false;

        if (changed)
            consecutiveFailures = 0;

        if (!firstDestination &&
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

        if (!changed && !forceRepath)
            return;

        destinationDirty = true;

        if (forceRepath)
        {
            CancelPendingRequest();
            InvalidateCurrentPath();
            nextRequestTime = 0f;
        }
    }

    public void Stop(bool clearDestination = false)
    {
        CanMove = false;
        ReachedDestination = true;
        CancelPendingRequest();
        if (hasDestination &&
            (!hasPath || (destination - activePathDestination).sqrMagnitude > 0.0001f))
            destinationDirty = true;
        ApplyVelocity(Vector2.zero, 0f);

        if (!clearDestination)
            return;

        CancelPendingRequest();
        hasDestination = false;
        hasPath = false;
        pathReachesDestination = false;
        destinationDirty = false;
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

        if (hasPath && pathRevision != navigation.GridRevision)
            ValidateStalePath(navigation, current);

        if (!hasPath &&
            consecutiveFailures > 0 &&
            lastFailureRevision != navigation.GridRevision)
        {
            consecutiveFailures = 0;
            lastFailureRevision = navigation.GridRevision;
            nextRequestTime = Time.unscaledTime;
        }

        if ((destinationDirty || !hasPath) &&
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
            pathRevision != navigation.GridRevision &&
            !ValidateStalePath(navigation, current))
        {
            ApplyVelocity(Vector2.zero, deltaTime);
            return;
        }

        if (!hasPath || waypointIndex >= waypoints.Length)
        {
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
        Vector2 delta = waypoint - current;
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
            result.Waypoints.Length == 0)
        {
            if (!hasPath)
                ApplyVelocity(Vector2.zero, 0f);
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
            (destination - submittedDestination).sqrMagnitude >
            destinationChangeDistance * destinationChangeDistance &&
            WorldNavigationGrid.WorldToCell(destination) !=
            WorldNavigationGrid.WorldToCell(submittedDestination);

        Vector2 current = CurrentPosition;
        int initialWaypoint = 0;
        if (navigation.IsWalkable(current))
        {
            initialWaypoint = -1;
            int furthestCandidate = Mathf.Min(result.Waypoints.Length - 1, 8);
            for (int i = furthestCandidate; i >= 0; i--)
            {
                if (!navigation.HasGridLineOfSight(current, result.Waypoints[i]))
                    continue;

                initialWaypoint = i;
                break;
            }

            if (initialWaypoint < 0)
            {
                destinationDirty = true;
                consecutiveFailures = Mathf.Min(consecutiveFailures + 1, 8);
                lastFailureRevision = navigation.GridRevision;
                nextRequestTime = Time.unscaledTime + Mathf.Max(0.05f, repathInterval);
                return;
            }
        }

        waypoints = result.Waypoints;
        waypointIndex = initialWaypoint;
        resolvedDestination = result.ResolvedDestination;
        activePathDestination = result.RequestedDestination;
        pathReachesDestination = result.ReachesDestination;
        pathRevision = result.GridRevision;
        pathInvalidationRevision = navigation.PathInvalidationRevision;
        queuedValidationRevision = -1;
        immediatelyValidatedWaypointIndex = -1;
        hasPath = true;
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
            if ((waypoints[waypointIndex] - current).sqrMagnitude > threshold * threshold)
                break;

            waypointIndex++;
        }

        if (waypointIndex >= waypoints.Length)
            hasPath = false;
    }

    private bool ValidateStalePath(WorldNavigationManager navigation, Vector2 current)
    {
        if (!hasPath || pathRevision == navigation.GridRevision)
            return hasPath;

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
            destinationDirty = true;
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
        destinationDirty = true;
        nextRequestTime = Time.unscaledTime;
        return false;
    }

    private void RecoverIfStalled(Vector2 current)
    {
        if ((current - lastProgressPosition).sqrMagnitude >=
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
        ForceRepath();
    }

    private void CompleteDestination()
    {
        CancelPendingRequest();
        ReachedDestination = true;
        hasPath = false;
        pathReachesDestination = false;
        destinationDirty = false;
        waypointIndex = 0;
        waypoints = Array.Empty<Vector2>();
        ApplyVelocity(Vector2.zero, 0f);
    }

    private bool HasReachedCurrentDestination(Vector2 current)
        => (current - destination).sqrMagnitude <= stopDistance * stopDistance;

    private bool HasReachedResolvedDestination(Vector2 current)
    {
        if (!pathReachesDestination ||
            (destination - activePathDestination).sqrMagnitude > 0.0001f)
        {
            return false;
        }

        return (current - resolvedDestination).sqrMagnitude <= stopDistance * stopDistance;
    }

    private void InvalidateCurrentPath()
    {
        hasPath = false;
        pathReachesDestination = false;
        waypointIndex = 0;
        waypoints = Array.Empty<Vector2>();
        pathRevision = -1;
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
            body.velocity = velocity;
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
        for (int i = firstWaypoint; i < waypoints.Length; i++)
        {
            Vector2 waypoint = waypoints[i];
            Vector3 point = new(waypoint.x, waypoint.y, z);
            if ((point - output[^1]).sqrMagnitude > 0.0001f)
                output.Add(point);
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
