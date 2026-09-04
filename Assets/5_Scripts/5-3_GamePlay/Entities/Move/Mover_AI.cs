using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// AI 公共移动模块。对上层保持原有目标、停止和到达接口，内部使用无限地图导航。
/// </summary>
public class Mover_AI : Mover
{
    private const float MinimumDestinationChangeDistance = 0.5f;

    [Title("AI 移动")]
    [Header("移动目标")]
    public Transform target;

    [Header("到达判定")]
    [MinValue(0.01f)]
    public float stopDistance = 0.5f;

    [Tooltip("目的地变化超过该距离时视为新请求")]
    [MinValue(0.001f)]
    public float destinationChangeThreshold = MinimumDestinationChangeDistance;

    [Header("卡住恢复")]
    [MinValue(0.1f)]
    public float stalledRepathDelay = 0.8f;

    [MinValue(0.001f)]
    public float progressDistanceThreshold = 0.02f;

    [Tooltip("实际速度低于该值时，动画视为停止")]
    [MinValue(0f)]
    public float animationMoveSpeedThreshold = 0.03f;

    [Header("运行时状态")]
    public bool CanMove = true;
    public bool HasReachedTarget;

    [ShowInInspector, ReadOnly]
    public WorldNavigationAgent NavigationAgent { get; private set; }

    private bool hasDestination;
    private Vector2 lastSubmittedDestination;
    private float EffectiveDestinationChangeDistance =>
        Mathf.Max(MinimumDestinationChangeDistance, destinationChangeThreshold);

    public float SpeedValue => Speed.Value;
    public bool HasActiveDestination => hasDestination;
    public bool IsPathPending => NavigationAgent != null && NavigationAgent.PathPending;
    public WorldNavigationDestinationResult DestinationResult => NavigationAgent != null
        ? NavigationAgent.DestinationResult
        : hasDestination
            ? WorldNavigationDestinationResult.Pending
            : WorldNavigationDestinationResult.None;
    public bool IsActuallyMoving =>
        CanMove &&
        hasDestination &&
        !HasReachedTarget &&
        NavigationAgent != null &&
        NavigationAgent.Velocity.sqrMagnitude >
        animationMoveSpeedThreshold * animationMoveSpeedThreshold;

    public override void Load()
    {
        base.Load();

        GameObject agentObject = item != null ? item.gameObject : gameObject;
        NavigationAgent = agentObject.GetComponent<WorldNavigationAgent>();
        if (NavigationAgent == null)
            NavigationAgent = agentObject.AddComponent<WorldNavigationAgent>();

        NavigationAgent.Bind(rb);
        NavigationAgent.Configure(
            stopDistance,
            EffectiveDestinationChangeDistance,
            stalledRepathDelay,
            progressDistanceThreshold);

        Vector2 currentPosition = rb != null ? rb.position : (Vector2)agentObject.transform.position;
        TargetPosition = currentPosition;
        lastSubmittedDestination = currentPosition;
        hasDestination = false;
        HasReachedTarget = true;
        CanMove = true;
        NavigationAgent.Stop(clearDestination: true);
    }

    public override void ModUpdate(float deltaTime)
    {
        if (NavigationAgent == null)
            return;

        if (target != null)
            SetDestination(target.position);

        NavigationAgent.MaxSpeed = SpeedValue;
        NavigationAgent.CanMove = CanMove && hasDestination;
        NavigationAgent.Tick(deltaTime);

        if (!CanMove || !hasDestination)
        {
            HasReachedTarget = true;
            return;
        }

        HasReachedTarget = NavigationAgent.ReachedDestination;
    }

    /// <summary>提交不限制路径总代价的普通移动目标。</summary>
    public void SetDestination(Vector2 destination, bool forceRepath = false)
    {
        SubmitDestination(destination, int.MaxValue, forceRepath);
    }

    /// <summary>提交只有路径总代价严格小于上限时才接受的移动目标。</summary>
    public WorldNavigationDestinationResult SetCostLimitedDestination(
        Vector2 destination,
        int maximumPathCostExclusive,
        bool forceRepath = false)
    {
        SubmitDestination(destination, Mathf.Max(1, maximumPathCostExclusive), forceRepath);
        return DestinationResult;
    }

    /// <summary>统一同步移动模块状态并把目标交给导航代理。</summary>
    private void SubmitDestination(
        Vector2 destination,
        int maximumPathCostExclusive,
        bool forceRepath)
    {
        bool isNewRequest =
            !hasDestination ||
            (destination - lastSubmittedDestination).sqrMagnitude >
            EffectiveDestinationChangeDistance * EffectiveDestinationChangeDistance;

        TargetPosition = destination;
        CanMove = true;
        HasReachedTarget = false;
        hasDestination = true;

        if (NavigationAgent == null)
            return;

        NavigationAgent.MaxSpeed = SpeedValue;
        if (maximumPathCostExclusive == int.MaxValue)
            NavigationAgent.SetDestination(destination, forceRepath);
        else
            NavigationAgent.SetCostLimitedDestination(
                destination,
                maximumPathCostExclusive,
                forceRepath);

        if (isNewRequest || forceRepath)
            lastSubmittedDestination = destination;
    }

    public void StopMovement()
    {
        CanMove = false;
        HasReachedTarget = true;
        NavigationAgent?.Stop();
    }

    public void ForceRepath()
    {
        NavigationAgent?.ForceRepath();
    }

    public override void Move(Vector2 targetPosition, float deltaTime = 0f)
    {
        SetDestination(targetPosition);
    }
}
