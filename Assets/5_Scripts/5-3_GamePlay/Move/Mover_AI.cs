using Pathfinding;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// AI 公共移动模块。统一负责目的地提交、到达判定和卡住后的重新寻路。
/// 所有动物 AI 都应通过 SetDestination/StopMovement 控制移动。
/// </summary>
public class Mover_AI : Mover
{
    #region Inspector
    [Title("AI 移动")]
    [Header("移动目标")]
    public Transform target;

    [Header("到达判定")]
    [MinValue(0.01f)]
    public float stopDistance = 0.5f;

    [Tooltip("目的地变化超过该距离时视为新请求")]
    [MinValue(0.001f)]
    public float destinationChangeThreshold = 0.05f;

    [Header("卡住恢复")]
    [Tooltip("在有移动命令但位置没有有效变化时，多久强制重新寻路")]
    [MinValue(0.1f)]
    public float stalledRepathDelay = 0.8f;

    [Tooltip("视为产生移动进展的最小距离")]
    [MinValue(0.001f)]
    public float progressDistanceThreshold = 0.02f;

    [Tooltip("实际速度低于该值时，动画视为已经停止移动")]
    [MinValue(0f)]
    public float animationMoveSpeedThreshold = 0.03f;

    [Header("运行时状态")]
    public bool CanMove = true;
    public bool HasReachedTarget;

    [ShowInInspector, ReadOnly]
    public IAstarAI aiPath;
    #endregion

    #region Runtime
    private bool _hasDestination;
    private Vector2 _lastSubmittedDestination;
    private Vector2 _lastProgressPosition;
    private float _lastProgressTime;
    #endregion

    public float SpeedValue => Speed.Value;
    public bool HasActiveDestination => _hasDestination;
    public bool IsPathPending => aiPath != null && aiPath.pathPending;
    public bool IsActuallyMoving =>
        CanMove &&
        _hasDestination &&
        !HasReachedTarget &&
        aiPath != null &&
        !aiPath.isStopped &&
        aiPath.velocity.sqrMagnitude > animationMoveSpeedThreshold * animationMoveSpeedThreshold;

    public override void Load()
    {
        base.Load();
        aiPath = item.GetComponent<IAstarAI>();

        if (aiPath == null)
        {
            Debug.LogError($"[{nameof(Mover_AI)}] 未找到 IAstarAI，移动模块已禁用。目标物体: {name}", this);
            CanMove = false;
            return;
        }

        Vector2 currentPosition = transform.position;
        TargetPosition = currentPosition;
        _lastSubmittedDestination = currentPosition;
        _lastProgressPosition = currentPosition;
        _lastProgressTime = Time.time;
        _hasDestination = false;
        HasReachedTarget = true;
        aiPath.isStopped = true;
    }

    public override void ModUpdate(float deltaTime)
    {
        if (aiPath == null)
            return;

        if (target != null)
            SetDestination(target.position);

        if (!CanMove || !_hasDestination)
        {
            aiPath.isStopped = true;
            return;
        }

        aiPath.maxSpeed = SpeedValue;
        aiPath.destination = TargetPosition;
        aiPath.isStopped = false;

        Vector2 currentPosition = transform.position;
        float directDistance = Vector2.Distance(currentPosition, TargetPosition);

        // 世界坐标已足够接近时可以立即完成，不依赖路径状态。
        if (directDistance <= stopDistance)
        {
            CompleteDestination();
            return;
        }

        // 路径计算期间 remainingDistance 可能暂时为 0。
        // 此时绝不能把 0 当作已经到达，这正是旧逻辑中途停走的根源。
        if (aiPath.pathPending)
        {
            HasReachedTarget = false;
            return;
        }

        bool hasValidRemainingDistance =
            aiPath.hasPath &&
            !float.IsNaN(aiPath.remainingDistance) &&
            !float.IsInfinity(aiPath.remainingDistance);

        float directDistanceGuard = Mathf.Max(stopDistance * 2f, stopDistance + 0.25f);
        bool reachedByPath =
            directDistance <= directDistanceGuard &&
            (aiPath.reachedDestination ||
             (hasValidRemainingDistance &&
              aiPath.remainingDistance <= stopDistance));

        if (reachedByPath)
        {
            CompleteDestination();
            return;
        }

        HasReachedTarget = false;
        RecoverIfStalled(currentPosition);
    }

    /// <summary>提交或更新目的地，并保证停止过的寻路组件恢复移动。</summary>
    public void SetDestination(Vector2 destination, bool forceRepath = false)
    {
        bool isNewRequest =
            !_hasDestination ||
            (destination - _lastSubmittedDestination).sqrMagnitude >
            destinationChangeThreshold * destinationChangeThreshold;

        TargetPosition = destination;
        CanMove = true;
        HasReachedTarget = false;
        _hasDestination = true;

        if (aiPath == null)
            return;

        aiPath.maxSpeed = SpeedValue;
        aiPath.destination = destination;
        aiPath.isStopped = false;

        if (!isNewRequest && !forceRepath)
            return;

        _lastSubmittedDestination = destination;
        _lastProgressPosition = transform.position;
        _lastProgressTime = Time.time;

        // 不取消正在进行的路径请求；新目的地会由自动重寻路接管。
        if (!aiPath.pathPending && (forceRepath || !aiPath.hasPath))
            aiPath.SearchPath();
    }

    /// <summary>停止当前移动，但保留最后目的地，便于之后恢复。</summary>
    public void StopMovement()
    {
        CanMove = false;
        HasReachedTarget = true;

        if (aiPath != null)
            aiPath.isStopped = true;
    }

    public override void Move(Vector2 targetPosition, float deltaTime = 0f)
    {
        SetDestination(targetPosition);
    }

    private void CompleteDestination()
    {
        HasReachedTarget = true;
        if (aiPath != null)
            aiPath.isStopped = true;
    }

    private void RecoverIfStalled(Vector2 currentPosition)
    {
        float progressThresholdSqr = progressDistanceThreshold * progressDistanceThreshold;
        if ((currentPosition - _lastProgressPosition).sqrMagnitude >= progressThresholdSqr)
        {
            _lastProgressPosition = currentPosition;
            _lastProgressTime = Time.time;
            return;
        }

        if (Time.time - _lastProgressTime < stalledRepathDelay)
            return;

        _lastProgressPosition = currentPosition;
        _lastProgressTime = Time.time;

        if (!aiPath.pathPending)
            aiPath.SearchPath();
    }
}
