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

/// <summary>
/// 可复用 AI 状态节点。节点只负责 Enter/Tick/Exit，
/// 状态选择优先级仍由具体动物的 EvaluateNextState 决定。
/// </summary>
public class AIStateNode<TState> where TState : struct, Enum
{
    private readonly Action _onEnter;
    private readonly Action<float> _onTick;
    private readonly Action _onExit;

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

    public virtual void Enter() => _onEnter?.Invoke();
    public virtual void Tick(float deltaTime) => _onTick(deltaTime);
    public virtual void Exit() => _onExit?.Invoke();
}

/// <summary>
/// 可复用“推进”智能节点：持续解析目标并提交寻路，到达后只回调一次；
/// 目标暂时失效时会安全停车，不替具体物种决定战斗、逃跑等状态优先级。
/// </summary>
public sealed class AIAdvanceStateNode<TState> : AIStateNode<TState> where TState : struct, Enum
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
        Vector3 offset = target.Position - _getCurrentPosition();
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
public sealed class AIStoppedStateNode<TState> : AIStateNode<TState> where TState : struct, Enum
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
/// 只需提供自己的状态枚举、条件和少量行为回调。
/// </summary>
public sealed class AIStateMachine<TState> where TState : struct, Enum
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
        where TState : struct, Enum
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
