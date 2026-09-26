using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>所有运行时 AI 角色的轻量标记，供跨物种仇恨筛选复用。</summary>
public interface IAIActor
{
    Item ActorItem { get; }
    bool IsAlive { get; }
}

/// <summary>外部系统下发给 AI 的推进命令。</summary>
public readonly struct AIAdvanceCommand
{
    public int TargetItemGuid { get; }
    public Vector3 TargetPosition { get; }
    public float ArrivalDistance { get; }
    public bool AttackActorsOnRoute { get; }

    public AIAdvanceCommand(
        int targetItemGuid,
        Vector3 targetPosition,
        float arrivalDistance,
        bool attackActorsOnRoute)
    {
        TargetItemGuid = targetItemGuid;
        TargetPosition = targetPosition;
        ArrivalDistance = Mathf.Max(0.05f, arrivalDistance);
        AttackActorsOnRoute = attackActorsOnRoute;
    }
}

/// <summary>可接收通用推进命令的 AI。</summary>
public interface IAIAdvanceCommandReceiver
{
    void BeginAdvance(AIAdvanceCommand command);
}

/// <summary>推进节点每帧读取的目标快照，可支持固定点或动态 Transform。</summary>
public readonly struct AIAdvanceTarget
{
    public bool IsValid { get; }
    public Vector3 Position { get; }

    public AIAdvanceTarget(bool isValid, Vector3 position)
    {
        IsValid = isValid;
        Position = position;
    }

    public static AIAdvanceTarget None => new(false, default);
}

/// <summary>状态节点对应的动画语义，用于在具体动画缺失时选择安全回退。</summary>
public enum AIStateAnimationRole
{
    Stopped,
    Moving,
    Action
}

/// <summary>节点执行状态；错误状态仅在首次进入时向 Console 报告。</summary>
public enum AIStateNodeExecutionStatus
{
    Running,
    Error
}

/// <summary>
/// 可复用 AI 状态节点。节点只负责 Enter/Tick/Exit，
/// 状态选择优先级由宿主评估器决定；状态键可用枚举或稳定字符串。
/// 节点可报告执行错误，且同一次错误状态只记录一条 Console 错误。
/// </summary>
public class AIStateNode<TState>
{
    private readonly Action _onEnter;
    private readonly Action<float> _onTick;
    private readonly Action _onExit;

    #region 节点错误诊断
    public AIStateNodeExecutionStatus ExecutionStatus { get; private set; }

    /// <summary>首次进入错误状态时报告节点及故障详情。</summary>
    protected void ReportError(string detail, UnityEngine.Object context)
    {
        if (ExecutionStatus == AIStateNodeExecutionStatus.Error)
            return;

        ExecutionStatus = AIStateNodeExecutionStatus.Error;
        Debug.LogError($"[AI 节点] {State} 进入错误状态：{detail}", context);
    }

    /// <summary>节点恢复或退出时清除错误状态。</summary>
    protected void ClearError()
    {
        ExecutionStatus = AIStateNodeExecutionStatus.Running;
    }
    #endregion

    public TState State { get; }
    public AIStateAnimationRole AnimationRole { get; }

    public AIStateNode(
        TState state,
        Action<float> onTick,
        Action onEnter = null,
        Action onExit = null,
        AIStateAnimationRole animationRole = AIStateAnimationRole.Action)
    {
        State = state;
        AnimationRole = animationRole;
        _onTick = onTick ?? throw new ArgumentNullException(nameof(onTick));
        _onEnter = onEnter;
        _onExit = onExit;
    }

    /// <summary>供具有自身 Tick 实现的智能节点继承。</summary>
    protected AIStateNode(TState state, AIStateAnimationRole animationRole)
    {
        State = state;
        AnimationRole = animationRole;
    }

    public virtual void Enter()
    {
        ClearError();
        _onEnter?.Invoke();
    }
    public virtual void Tick(float deltaTime) => _onTick(deltaTime);
    public virtual void Exit()
    {
        ClearError();
        _onExit?.Invoke();
    }
}

/// <summary>
/// 动物共用的逃离状态节点。进入状态时锁定水体安全目标，之后只在威胁方向明显改变或到达目标时重规划，
/// 避免每帧移动目标，同时让逃离方向跟随追击者调整。
/// </summary>
public sealed class AIFleeStateNode<TState> : AIStateNode<TState>
{
    private const float DirectionRetargetCheckInterval = 0.15f; // 方向变化检查间隔，不依赖感知器重扫频率。
    private const float MinimumDirectionRetargetAngle = 15f; // 方向至少改变该角度才重规划。

    private readonly Func<Vector3?> _resolveThreatPosition; // 当前威胁位置解析器。
    private readonly Func<Vector3, float, Vector3?> _resolveDestination; // 可达的水体安全逃离目标解析器。
    private readonly Func<float> _getRunDistance; // 当前物种的单次逃离距离。
    private readonly Action<Vector3> _moveTo; // 提交已选定目标的移动回调。
    private readonly Action _stopMovement; // 没有可用目标时停车。
    private readonly Func<Vector3> _getCurrentPosition; // 当前动物位置。
    private readonly Func<bool> _hasReachedDestination; // 导航是否已到达当前逃离目标。
    private readonly Func<WorldNavigationDestinationResult> _getDestinationResult; // 当前导航目标的正式结果。
    private readonly UnityEngine.Object _diagnosticContext; // Console 定位到对应动物。
    private readonly Action _onEnter; // 节点进入时的物种扩展回调。
    private readonly Action _onExit; // 节点退出时的物种扩展回调。
    private Vector3 _escapeDestination; // 本次逃离锁定的目标。
    private bool _hasEscapeDestination; // 当前目标是否有效。
    private Vector2 _lastAwayDirection; // 上次规划时的远离方向。
    private bool _hasLastAwayDirection; // 是否记录过有效远离方向。
    private float _directionRetargetTimer; // 下次方向检查前剩余时间。

    public AIFleeStateNode(
        TState state,
        Func<Vector3?> resolveThreatPosition,
        Func<Vector3, float, Vector3?> resolveDestination,
        Func<float> getRunDistance,
        Action<Vector3> moveTo,
        Action stopMovement,
        Func<Vector3> getCurrentPosition,
        Func<bool> hasReachedDestination,
        Func<WorldNavigationDestinationResult> getDestinationResult,
        UnityEngine.Object diagnosticContext,
        Action onEnter = null,
        Action onExit = null)
        : base(state, AIStateAnimationRole.Moving)
    {
        _resolveThreatPosition = resolveThreatPosition ?? throw new ArgumentNullException(nameof(resolveThreatPosition));
        _resolveDestination = resolveDestination ?? throw new ArgumentNullException(nameof(resolveDestination));
        _getRunDistance = getRunDistance ?? throw new ArgumentNullException(nameof(getRunDistance));
        _moveTo = moveTo ?? throw new ArgumentNullException(nameof(moveTo));
        _stopMovement = stopMovement ?? throw new ArgumentNullException(nameof(stopMovement));
        _getCurrentPosition = getCurrentPosition ?? throw new ArgumentNullException(nameof(getCurrentPosition));
        _hasReachedDestination = hasReachedDestination ?? throw new ArgumentNullException(nameof(hasReachedDestination));
        _getDestinationResult = getDestinationResult ?? throw new ArgumentNullException(nameof(getDestinationResult));
        _diagnosticContext = diagnosticContext;
        _onEnter = onEnter;
        _onExit = onExit;
    }

    public override void Enter()
    {
        base.Enter();
        _hasEscapeDestination = false;
        _hasLastAwayDirection = false;
        _directionRetargetTimer = 0f;
        _onEnter?.Invoke();
        Retarget();
    }

    public override void Tick(float deltaTime)
    {
        ReportNavigationFailure();
        _directionRetargetTimer = Mathf.Max(
            0f,
            _directionRetargetTimer - Mathf.Max(0f, deltaTime));
        if (_directionRetargetTimer <= 0f)
        {
            // 目标到达后续规划下一段；追击角度变化时更新方向；存档恢复缺威胁时继续尝试获取。
            if (!_hasEscapeDestination || _hasReachedDestination() || HasThreatDirectionChanged())
            {
                Retarget();
                return;
            }

            _directionRetargetTimer = DirectionRetargetCheckInterval;
        }

        if (!_hasEscapeDestination)
            return;

        _moveTo(_escapeDestination);
    }

    public override void Exit()
    {
        _hasEscapeDestination = false;
        _stopMovement();
        _onExit?.Invoke();
        base.Exit();
    }

    /// <summary>导航连续失败时让节点进入错误状态；成功获得路径后恢复。</summary>
    private void ReportNavigationFailure()
    {
        if (!_hasEscapeDestination)
            return;

        WorldNavigationDestinationResult result = _getDestinationResult();
        if (result == WorldNavigationDestinationResult.Failed)
        {
            ReportError(
                $"逃离寻路连续至少 {WorldNavigationAgent.PathFailureErrorThreshold} 次失败；" +
                $"当前位置={_getCurrentPosition()}，目标={_escapeDestination}。",
                _diagnosticContext);
        }
        else if (result == WorldNavigationDestinationResult.Accepted ||
                 result == WorldNavigationDestinationResult.Reached)
        {
            ClearError();
        }
    }

    /// <summary>新伤害来源出现时重新按当前威胁规划一次逃离目标。</summary>
    public void Retarget()
    {
        _directionRetargetTimer = DirectionRetargetCheckInterval;
        Vector3? threatPosition = _resolveThreatPosition();
        if (!threatPosition.HasValue || !IsFinite(threatPosition.Value))
        {
            _hasEscapeDestination = false;
            _hasLastAwayDirection = false;
            ClearError();
            _stopMovement();
            return;
        }

        Vector2 awayDirection = ResolveAwayDirection(threatPosition.Value);
        _hasLastAwayDirection = awayDirection.sqrMagnitude > 0.0001f;
        if (_hasLastAwayDirection)
            _lastAwayDirection = awayDirection;

        float runDistance = Mathf.Max(0.1f, _getRunDistance());
        Vector3? destination = _resolveDestination(threatPosition.Value, runDistance);
        if (destination.HasValue && _hasEscapeDestination &&
            WorldTopologyRuntime.SqrDistance(_escapeDestination, destination.Value) > 0.25f)
            ClearError();
        _hasEscapeDestination = destination.HasValue && IsFinite(destination.Value);
        if (_hasEscapeDestination)
        {
            _escapeDestination = destination.Value;
            _moveTo(_escapeDestination);
        }
        else
            _stopMovement();
    }

    private bool HasThreatDirectionChanged()
    {
        Vector3? threatPosition = _resolveThreatPosition();
        if (!threatPosition.HasValue || !IsFinite(threatPosition.Value))
            return false;

        Vector2 currentAwayDirection = ResolveAwayDirection(threatPosition.Value);
        if (currentAwayDirection.sqrMagnitude <= 0.0001f)
            return false;

        if (!_hasLastAwayDirection)
            return true;

        float minimumDirectionDot = Mathf.Cos(MinimumDirectionRetargetAngle * Mathf.Deg2Rad);
        return Vector2.Dot(_lastAwayDirection, currentAwayDirection) <= minimumDirectionDot;
    }

    private Vector2 ResolveAwayDirection(Vector3 threatPosition)
    {
        Vector3 currentPosition = _getCurrentPosition();
        Vector2 direction = WorldTopologyRuntime.ShortestDelta(threatPosition, currentPosition);
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.zero;
    }

    private static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
               !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
               !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}

/// <summary>所有动物共用的逃离目标与有效距离计算。</summary>
public static class AIFleeUtility
{
    private const float MaximumGroundEscapeSegmentDistance = 8f; // 地面逃离每段不超过半个 Chunk，避免请求远处不连通目标。

    /// <summary>按水体与导航连通性选择地面动物的下一段逃离目标。</summary>
    public static Vector3? ResolveNavigableGroundEscapeDestination(
        Vector3 origin,
        Vector3 threatPosition,
        float distance,
        Vector2 fallbackDirection)
    {
        Vector2 awayDirection = WorldTopologyRuntime.ShortestDelta(threatPosition, origin);
        if (awayDirection.sqrMagnitude <= 0.0001f)
            awayDirection = fallbackDirection;
        if (awayDirection.sqrMagnitude <= 0.0001f)
            awayDirection = Vector2.right;

        float segmentDistance = Mathf.Min(
            Mathf.Max(0.1f, distance),
            MaximumGroundEscapeSegmentDistance);
        if (!AI_WanderUtility.TryPickNavigableEscapeOffset(
                origin,
                awayDirection.normalized,
                segmentDistance,
                out Vector2 escapeOffset))
            return null;

        return WorldTopologyRuntime.NormalizePosition(origin + (Vector3)escapeOffset);
    }

    /// <summary>按威胁位置、后退方向和地形安全策略计算一次逃离目标。</summary>
    public static Vector3 ResolveEscapeDestination(
        Vector3 origin,
        Vector3 threatPosition,
        float distance,
        Vector2 fallbackDirection)
    {
        Vector2 awayDirection = WorldTopologyRuntime.ShortestDelta(threatPosition, origin);
        if (awayDirection.sqrMagnitude <= 0.0001f)
            awayDirection = fallbackDirection;
        if (awayDirection.sqrMagnitude <= 0.0001f)
            awayDirection = Vector2.right;

        Vector2 escapeOffset = AI_WanderUtility.PickWaterAwareEscapeOffset(
            origin,
            awayDirection.normalized,
            Mathf.Max(0.1f, distance));
        return WorldTopologyRuntime.NormalizePosition(origin + (Vector3)escapeOffset);
    }

    /// <summary>用目标体型倍率和感知视线判断鸟类是否仍处在逃离范围。</summary>
    public static bool IsWithinEscapeRange(
        Vector2 observerPosition,
        Item threat,
        Mod_ItemDetector detector,
        float baseDistance)
    {
        if (threat == null || detector == null)
            return false;

        float effectiveDistance = Mod_ItemDetector.CalculateEffectiveDetectionRadius(
            Mathf.Max(0f, baseDistance),
            threat);
        return WorldTopologyRuntime.SqrDistance(observerPosition, threat.transform.position) <=
                   effectiveDistance * effectiveDistance &&
               detector.HasLineOfSight(threat);
    }
}

/// <summary>
/// 可复用“推进”智能节点：持续解析目标并提交寻路，到达后只回调一次；
/// 目标暂时失效时会安全停车，不替具体物种决定战斗、逃跑等状态优先级。
/// </summary>
public sealed class AIAdvanceStateNode<TState> : AIStateNode<TState>
{
    private readonly Func<AIAdvanceTarget> _resolveTarget;
    private readonly Func<Vector3> _getCurrentPosition;
    private readonly Func<float> _getArrivalDistance;
    private readonly Action<Vector3> _moveTo;
    private readonly Action _stopMovement;
    private readonly Action _onEnter;
    private readonly Action _onArrived;
    private readonly Action _onExit;
    private bool _arrivalNotified;

    public AIAdvanceStateNode(
        TState state,
        Func<AIAdvanceTarget> resolveTarget,
        Func<Vector3> getCurrentPosition,
        Func<float> getArrivalDistance,
        Action<Vector3> moveTo,
        Action stopMovement,
        Action onArrived = null,
        Action onEnter = null,
        Action onExit = null)
        : base(state, AIStateAnimationRole.Moving)
    {
        _resolveTarget = resolveTarget ?? throw new ArgumentNullException(nameof(resolveTarget));
        _getCurrentPosition = getCurrentPosition ?? throw new ArgumentNullException(nameof(getCurrentPosition));
        _getArrivalDistance = getArrivalDistance ?? throw new ArgumentNullException(nameof(getArrivalDistance));
        _moveTo = moveTo ?? throw new ArgumentNullException(nameof(moveTo));
        _stopMovement = stopMovement ?? throw new ArgumentNullException(nameof(stopMovement));
        _onArrived = onArrived;
        _onEnter = onEnter;
        _onExit = onExit;
    }

    public override void Enter()
    {
        base.Enter();
        _arrivalNotified = false;
        _onEnter?.Invoke();
    }

    public override void Tick(float deltaTime)
    {
        AIAdvanceTarget target = _resolveTarget();
        if (!target.IsValid || !IsFinite(target.Position))
        {
            _arrivalNotified = false;
            _stopMovement();
            return;
        }

        float arrivalDistance = Mathf.Max(0.05f, _getArrivalDistance());
        Vector2 offset = WorldTopologyRuntime.ShortestDelta(_getCurrentPosition(), target.Position);
        if (offset.sqrMagnitude <= arrivalDistance * arrivalDistance)
        {
            _stopMovement();
            if (!_arrivalNotified)
            {
                _arrivalNotified = true;
                _onArrived?.Invoke();
            }
            return;
        }

        _arrivalNotified = false;
        _moveTo(target.Position);
    }

    public override void Exit()
    {
        _arrivalNotified = false;
        _onExit?.Invoke();
        base.Exit();
    }

    private static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
               !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
               !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}

/// <summary>
/// 进入后必须保持静止的通用节点，适用于待机、睡眠、进食、警觉等状态。
/// </summary>
public sealed class AIStoppedStateNode<TState> : AIStateNode<TState>
{
    public AIStoppedStateNode(
        TState state,
        Action stopMovement,
        Action<float> onTick,
        Action onEnter = null,
        Action onExit = null,
        AIStateAnimationRole animationRole = AIStateAnimationRole.Stopped)
        : base(
            state,
            deltaTime =>
            {
                stopMovement();
                onTick?.Invoke(deltaTime);
            },
            onEnter,
            onExit,
            animationRole)
    {
        if (stopMovement == null)
            throw new ArgumentNullException(nameof(stopMovement));
    }
}

/// <summary>
/// 与动物种类无关的状态机容器。不同动物可以复用相同节点类型，
/// 只需提供自己的状态键、条件和少量行为回调；键可以是枚举或字符串。
/// </summary>
public sealed class AIStateMachine<TState>
{
    private readonly Dictionary<TState, AIStateNode<TState>> _nodes =
        new Dictionary<TState, AIStateNode<TState>>();

    private AIStateNode<TState> _currentNode;

    public bool IsInitialized { get; private set; }
    public TState CurrentState { get; private set; }

    public void Register(AIStateNode<TState> node)
    {
        if (node == null)
            throw new ArgumentNullException(nameof(node));

        if (_nodes.ContainsKey(node.State))
            throw new InvalidOperationException($"AI 状态节点重复注册: {node.State}");

        _nodes.Add(node.State, node);
    }

    public void Initialize(TState initialState)
    {
        _currentNode = GetRequiredNode(initialState);
        CurrentState = initialState;
        IsInitialized = true;
        _currentNode.Enter();
    }

    /// <summary>结束本轮实体运行态，保留只绑定当前 AI 实例的节点与委托供回池后复用。</summary>
    public void Reset()
    {
        if (IsInitialized)
            _currentNode?.Exit();
        _currentNode = null;
        CurrentState = default;
        IsInitialized = false;
    }

    /// <summary>
    /// 状态退出与进入之间执行切换回调，保证宿主先完成状态字段和资源清理，
    /// 新节点再开始工作。
    /// </summary>
    public void TransitionTo(TState nextState, Action transitionCallback)
    {
        if (!IsInitialized)
            throw new InvalidOperationException("AI 状态机尚未初始化。");

        if (EqualityComparer<TState>.Default.Equals(nextState, CurrentState))
            return;

        AIStateNode<TState> nextNode = GetRequiredNode(nextState);
        _currentNode.Exit();
        transitionCallback?.Invoke();
        CurrentState = nextState;
        _currentNode = nextNode;
        _currentNode.Enter();
    }

    public void Tick(float deltaTime)
    {
        if (!IsInitialized || _currentNode == null)
            return;

        _currentNode.Tick(deltaTime);
    }

    public AIStateAnimationRole GetAnimationRole(TState state)
    {
        return GetRequiredNode(state).AnimationRole;
    }

    private AIStateNode<TState> GetRequiredNode(TState state)
    {
        if (_nodes.TryGetValue(state, out AIStateNode<TState> node))
            return node;

        throw new InvalidOperationException($"AI 状态 {state} 没有注册对应节点。");
    }
}

/// <summary>
/// AI 状态机统一运行入口：评估、切换、更新当前节点。
/// </summary>
public static class AI_StateMachineRunner
{
    public static void EvaluateAndTick<TState>(
        AIStateMachine<TState> stateMachine,
        TState currentState,
        Func<TState> evaluateNextState,
        Action<TState> switchState,
        float deltaTime,
        Func<TState, bool> canTransition = null)
    {
        if (stateMachine == null)
            throw new ArgumentNullException(nameof(stateMachine));

        TState nextState = evaluateNextState();
        if (!EqualityComparer<TState>.Default.Equals(nextState, currentState) &&
            (canTransition == null || canTransition(nextState)))
            switchState(nextState);

        stateMachine.Tick(deltaTime);
    }

    /// <summary>保留旧入口，避免项目内尚未迁移的调用失效。</summary>
    public static void EvaluateAndTick<TState>(
        TState currentState,
        Func<TState> evaluateNextState,
        Action<TState> switchState,
        Action<float> tickCurrentState,
        float deltaTime)
    {
        TState nextState = evaluateNextState();
        if (!EqualityComparer<TState>.Default.Equals(nextState, currentState))
            switchState(nextState);

        tickCurrentState(deltaTime);
    }
}
