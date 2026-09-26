using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

#region JSON 声明结构

/// <summary>AI 行为图定义：JSON 选择状态节点、转换条件和节点参数，节点逻辑由注册表提供。</summary>
[Serializable]
public sealed class AIBehaviorGraphDefinition
{
    [JsonProperty("schemaVersion")] public int SchemaVersion = 1; // 行为图结构版本。
    [JsonProperty("initialState")] public string InitialState; // 首次进入的状态键。
    [JsonProperty("decisionInterval")] public float DecisionInterval = 0.15f; // 状态条件评估间隔。
    [JsonProperty("states")] public List<AIBehaviorStateDefinition> States = new(); // 状态及其有序转换。
}

/// <summary>一个状态由注册节点、节点参数、动画名和按优先级排列的转换组成。</summary>
[Serializable]
public sealed class AIBehaviorStateDefinition
{
    [JsonProperty("id")] public string Id; // JSON 状态键。
    [JsonProperty("node")] public string Node; // 注册的节点类型。
    [JsonProperty("parameters")] public JObject Parameters = new(); // 节点独立参数。
    [JsonProperty("animation")] public string Animation; // 可选状态动画。
    [JsonProperty("transitions")] public List<AIBehaviorTransitionDefinition> Transitions = new(); // 从本状态发出的有序转换。
}

/// <summary>命中条件组后切换到目标状态；数组顺序就是同一状态内的优先级。</summary>
[Serializable]
public sealed class AIBehaviorTransitionDefinition
{
    [JsonProperty("targetState")] public string TargetState; // 转换目标状态键。
    [JsonProperty("conditionMode")] public string ConditionMode = "all"; // 条件组采用 all 或 any。
    [JsonProperty("conditions")] public List<AIBehaviorConditionDefinition> Conditions = new(); // 转换条件列表。
}

/// <summary>单个可注册条件节点及其参数。</summary>
[Serializable]
public sealed class AIBehaviorConditionDefinition
{
    [JsonProperty("type")] public string Type; // 条件注册名。
    [JsonProperty("parameters")] public JObject Parameters = new(); // 条件参数。
}

#endregion

/// <summary>行为图状态节点的工厂与静态参数校验器。</summary>
public delegate AIStateNode<string> AIBehaviorNodeFactory(
    AIBehaviorGraphContext context,
    string stateId,
    JObject parameters);

/// <summary>行为图条件编译器；返回的委托在状态评估时读取最新运行态。</summary>
public delegate Func<bool> AIBehaviorConditionFactory(
    AIBehaviorGraphContext context,
    JObject parameters);

/// <summary>独立于具体物种的 JSON AI 节点与条件注册表，供 MOD 增加稳定扩展类型。</summary>
public static class AIBehaviorGraphRegistry
{
    #region 注册表

    private sealed class NodeRegistration
    {
        public AIBehaviorNodeFactory Factory; // 按 JSON 参数创建状态节点。
        public Action<JObject> Validate; // 节点参数的严格静态校验。
        public Func<JObject, float> DetectionRadius; // 节点需要的最大基础感知半径。
        public string[] RequiredCapabilities; // 节点运行所需的 Prefab 能力。
    }

    private sealed class ConditionRegistration
    {
        public AIBehaviorConditionFactory Factory; // 编译为运行时条件。
        public Action<JObject> Validate; // 条件参数的严格静态校验。
        public Func<JObject, float> DetectionRadius; // 条件需要的最大基础感知半径。
        public string[] RequiredCapabilities; // 条件运行所需的 Prefab 能力。
    }

    private static readonly Dictionary<string, NodeRegistration> Nodes =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ConditionRegistration> Conditions =
        new(StringComparer.OrdinalIgnoreCase);

    static AIBehaviorGraphRegistry()
    {
        AIBehaviorGraphBuiltins.RegisterBuiltins();
    }

    #endregion

    #region 注册与校验

    /// <summary>注册可由 JSON 组合的状态节点类型。</summary>
    public static void RegisterNode(
        string type,
        AIBehaviorNodeFactory factory,
        Action<JObject> validateParameters,
        string[] requiredCapabilities = null,
        Func<JObject, float> detectionRadius = null)
    {
        if (factory == null)
            throw new ArgumentNullException(nameof(factory));
        if (validateParameters == null)
            throw new ArgumentNullException(nameof(validateParameters));

        Register(Nodes, type, new NodeRegistration
        {
            Factory = factory,
            Validate = validateParameters,
            RequiredCapabilities = requiredCapabilities ?? Array.Empty<string>(),
            DetectionRadius = detectionRadius
        });
    }

    /// <summary>注册可用于状态转换的条件类型。</summary>
    public static void RegisterCondition(
        string type,
        AIBehaviorConditionFactory factory,
        Action<JObject> validateParameters,
        string[] requiredCapabilities = null,
        Func<JObject, float> detectionRadius = null)
    {
        if (factory == null)
            throw new ArgumentNullException(nameof(factory));
        if (validateParameters == null)
            throw new ArgumentNullException(nameof(validateParameters));

        Register(Conditions, type, new ConditionRegistration
        {
            Factory = factory,
            Validate = validateParameters,
            RequiredCapabilities = requiredCapabilities ?? Array.Empty<string>(),
            DetectionRadius = detectionRadius
        });
    }

    /// <summary>严格校验节点、状态键、转换目标和条件类型，并汇总运行所需能力与感知半径。</summary>
    public static AIBehaviorGraphRequirements Validate(AIBehaviorGraphDefinition graph)
    {
        if (graph == null)
            throw new InvalidDataException("AI 行为图不能为空。");
        if (graph.SchemaVersion != 1)
            throw new InvalidDataException($"不支持 AI 行为图 schemaVersion：{graph.SchemaVersion}");
        if (float.IsNaN(graph.DecisionInterval) || float.IsInfinity(graph.DecisionInterval) || graph.DecisionInterval < 0f)
            throw new InvalidDataException("AI 行为图 decisionInterval 必须是非负有限数值。");
        if (graph.States == null || graph.States.Count == 0)
            throw new InvalidDataException("AI 行为图至少需要一个状态。");

        var stateById = new Dictionary<string, AIBehaviorStateDefinition>(StringComparer.Ordinal);
        for (int i = 0; i < graph.States.Count; i++)
        {
            AIBehaviorStateDefinition state = graph.States[i];
            if (state == null || string.IsNullOrWhiteSpace(state.Id))
                throw new InvalidDataException($"AI 行为图 states[{i}] 缺少 id。");
            if (!stateById.TryAdd(state.Id, state))
                throw new InvalidDataException($"AI 行为图状态键重复：{state.Id}");
        }

        if (string.IsNullOrWhiteSpace(graph.InitialState) || !stateById.ContainsKey(graph.InitialState))
            throw new InvalidDataException($"AI 行为图 initialState 未指向已定义状态：{graph.InitialState ?? "<null>"}");

        var capabilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        float detectionRadius = 0f;
        foreach (AIBehaviorStateDefinition state in graph.States)
        {
            if (string.IsNullOrWhiteSpace(state.Node) || !Nodes.TryGetValue(state.Node, out NodeRegistration node))
                throw new InvalidDataException($"AI 行为图状态 {state.Id} 使用了未注册节点：{state.Node ?? "<null>"}");

            JObject nodeParameters = state.Parameters ?? new JObject();
            node.Validate(nodeParameters);
            AddCapabilities(capabilities, node.RequiredCapabilities);
            detectionRadius = Mathf.Max(
                detectionRadius,
                ReadDetectionRadius(node.DetectionRadius?.Invoke(nodeParameters), state.Id));
            if (!string.IsNullOrWhiteSpace(state.Animation))
                capabilities.Add(AIBehaviorCapability.Animator);

            foreach (AIBehaviorTransitionDefinition transition in state.Transitions ?? new List<AIBehaviorTransitionDefinition>())
            {
                if (transition == null || string.IsNullOrWhiteSpace(transition.TargetState) ||
                    !stateById.ContainsKey(transition.TargetState))
                    throw new InvalidDataException($"AI 行为图状态 {state.Id} 包含无效目标状态：{transition?.TargetState ?? "<null>"}");

                if (!string.Equals(transition.ConditionMode, "all", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(transition.ConditionMode, "any", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"AI 行为图状态 {state.Id} 的 conditionMode 只能是 all 或 any。");

                if (transition.Conditions == null || transition.Conditions.Count == 0)
                    throw new InvalidDataException($"AI 行为图状态 {state.Id} 的转换条件不能为空；无条件转换请使用 always。");

                foreach (AIBehaviorConditionDefinition condition in transition.Conditions ?? new List<AIBehaviorConditionDefinition>())
                {
                    if (condition == null || string.IsNullOrWhiteSpace(condition.Type) ||
                        !Conditions.TryGetValue(condition.Type, out ConditionRegistration registeredCondition))
                        throw new InvalidDataException($"AI 行为图状态 {state.Id} 包含未注册条件：{condition?.Type ?? "<null>"}");

                    JObject conditionParameters = condition.Parameters ?? new JObject();
                    registeredCondition.Validate(conditionParameters);
                    AddCapabilities(capabilities, registeredCondition.RequiredCapabilities);
                    detectionRadius = Mathf.Max(
                        detectionRadius,
                        ReadDetectionRadius(
                            registeredCondition.DetectionRadius?.Invoke(conditionParameters),
                            state.Id));
                }
            }
        }

        return new AIBehaviorGraphRequirements(capabilities, detectionRadius);
    }

    internal static AIStateNode<string> CreateNode(
        string type,
        AIBehaviorGraphContext context,
        string stateId,
        JObject parameters)
    {
        AIStateNode<string> node = Nodes[type].Factory(context, stateId, parameters ?? new JObject());
        return node ?? throw new InvalidDataException($"AI 行为节点 {type} 返回了空节点。");
    }

    internal static Func<bool> CreateCondition(
        string type,
        AIBehaviorGraphContext context,
        JObject parameters)
    {
        Func<bool> condition = Conditions[type].Factory(context, parameters ?? new JObject());
        return condition ?? throw new InvalidDataException($"AI 行为条件 {type} 返回了空条件。");
    }

    private static void Register<TRegistration>(
        Dictionary<string, TRegistration> registrations,
        string type,
        TRegistration registration) where TRegistration : class
    {
        if (string.IsNullOrWhiteSpace(type) || registration == null)
            throw new ArgumentException("AI 行为图注册项必须包含类型名和注册内容。");
        string key = type.Trim();
        if (registrations.ContainsKey(key))
            throw new InvalidOperationException($"AI 行为图类型重复注册：{key}");
        registrations.Add(key, registration);
    }

    private static float ReadDetectionRadius(float? radius, string stateId)
    {
        float value = radius ?? 0f;
        if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
            throw new InvalidDataException($"AI 行为图状态 {stateId} 声明了无效感知半径：{value}");
        return value;
    }

    private static void AddCapabilities(HashSet<string> target, string[] source)
    {
        if (source == null)
            return;
        for (int i = 0; i < source.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(source[i]))
                target.Add(source[i]);
        }
    }

    #endregion
}

#region 预置体能力契约

/// <summary>行为图经校验后要求的运行模块能力。</summary>
public sealed class AIBehaviorGraphRequirements
{
    public IReadOnlyCollection<string> Capabilities { get; }
    public float DetectionRadius { get; }

    internal AIBehaviorGraphRequirements(HashSet<string> capabilities, float detectionRadius)
    {
        Capabilities = capabilities;
        DetectionRadius = detectionRadius;
    }
}

/// <summary>预置体节点能力名称，作为 JSON 节点与运行模块之间的稳定契约。</summary>
public static class AIBehaviorCapability
{
    public const string Mover = "mover";
    public const string Detector = "detector";
    public const string Health = "health";
    public const string Animator = "animator";
}

#endregion

#region 通用状态图执行器

/// <summary>已构建状态及其 JSON 条件转换，供通用行为图运行时使用。</summary>
public sealed class AIBehaviorGraphRuntime
{
    private sealed class CompiledConditionGroup
    {
        public bool Any; // true 时采用 any，否则采用 all。
        public Func<bool>[] Conditions; // 按 JSON 顺序编译的条件。
        public string TargetState; // 条件组命中后的状态键。
    }

    private sealed class CompiledState
    {
        public AIBehaviorStateDefinition Definition; // 状态节点参数与动画。
        public List<CompiledConditionGroup> Transitions; // 按 JSON 声明顺序评估的转换。
    }

    private readonly AIStateMachine<string> _stateMachine = new(); // 共用的状态生命周期执行器。
    private readonly Dictionary<string, CompiledState> _states = new(StringComparer.Ordinal);
    private readonly AIBehaviorGraphContext _context; // 节点共享的 Actor 能力上下文。
    private readonly Action<string, string> _onStateChanged; // 状态变更观察者。
    private readonly float _decisionInterval; // JSON 配置的条件评估间隔。
    private float _decisionTimer; // 距离下一轮条件评估的剩余时间。
    private bool _isInitialized; // 当前图是否已进入初始状态。

    public string CurrentState => _stateMachine.IsInitialized ? _stateMachine.CurrentState : null;
    public float StateElapsed => _context.StateElapsed;

    public AIBehaviorGraphRuntime(
        AIBehaviorGraphDefinition definition,
        AIBehaviorGraphContext context,
        Action<string, string> onStateChanged,
        string initialState = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _onStateChanged = onStateChanged;
        AIBehaviorGraphRequirements requirements = AIBehaviorGraphRegistry.Validate(definition);
        _context.RequireCapabilities(requirements.Capabilities);
        _decisionInterval = definition.DecisionInterval;

        foreach (AIBehaviorStateDefinition state in definition.States)
        {
            _stateMachine.Register(AIBehaviorGraphRegistry.CreateNode(
                state.Node,
                _context,
                state.Id,
                state.Parameters));

            var compiledState = new CompiledState
            {
                Definition = state,
                Transitions = new List<CompiledConditionGroup>()
            };
            foreach (AIBehaviorTransitionDefinition transition in state.Transitions ?? new List<AIBehaviorTransitionDefinition>())
            {
                var conditions = new Func<bool>[transition.Conditions?.Count ?? 0];
                for (int i = 0; i < conditions.Length; i++)
                {
                    AIBehaviorConditionDefinition condition = transition.Conditions[i];
                    conditions[i] = AIBehaviorGraphRegistry.CreateCondition(
                        condition.Type,
                        _context,
                        condition.Parameters);
                }
                compiledState.Transitions.Add(new CompiledConditionGroup
                {
                    Any = string.Equals(transition.ConditionMode, "any", StringComparison.OrdinalIgnoreCase),
                    Conditions = conditions,
                    TargetState = transition.TargetState
                });
            }
            _states.Add(state.Id, compiledState);
        }

        string stateToEnter = string.IsNullOrWhiteSpace(initialState) ? definition.InitialState : initialState;
        if (!_states.ContainsKey(stateToEnter))
            throw new InvalidDataException($"AI 行为图初始状态未定义：{stateToEnter}");

        _decisionTimer = 0f;
        _context.StateElapsed = 0f;
        _stateMachine.Initialize(stateToEnter);
        _isInitialized = true;
        PlayStateAnimation(stateToEnter);
    }

    /// <summary>按 JSON 转换优先级评估状态条件，再逐帧推进当前节点。</summary>
    public void Tick(float deltaTime)
    {
        if (!_isInitialized)
            return;

        float step = Mathf.Max(0f, deltaTime);
        _context.StateElapsed += step;
        _decisionTimer -= step;
        if (_decisionInterval <= 0f || _decisionTimer <= 0f)
        {
            _decisionTimer = _decisionInterval > 0f ? _decisionInterval : 0f;
            if (TryResolveTransition(_stateMachine.CurrentState, out string nextState))
                TransitionTo(nextState);
        }

        _stateMachine.Tick(step);
    }

    /// <summary>结束当前节点并清除宿主委托。</summary>
    public void Reset()
    {
        if (!_isInitialized)
            return;

        _stateMachine.Reset();
        _context.StateElapsed = 0f;
        _isInitialized = false;
    }

    private bool TryResolveTransition(string stateId, out string targetState)
    {
        targetState = null;
        CompiledState state = _states[stateId];
        foreach (CompiledConditionGroup group in state.Transitions)
        {
            bool matched = group.Any ? false : true;
            if (group.Any)
            {
                for (int i = 0; i < group.Conditions.Length; i++)
                {
                    if (!group.Conditions[i]())
                        continue;
                    matched = true;
                    break;
                }
            }
            else
            {
                for (int i = 0; i < group.Conditions.Length; i++)
                {
                    if (group.Conditions[i]())
                        continue;
                    matched = false;
                    break;
                }
            }

            if (!matched)
                continue;

            targetState = group.TargetState;
            return true;
        }
        return false;
    }

    private void TransitionTo(string nextState)
    {
        string previousState = _stateMachine.CurrentState;
        if (string.Equals(previousState, nextState, StringComparison.Ordinal))
            return;

        _stateMachine.TransitionTo(nextState, () => _context.StateElapsed = 0f);
        _decisionTimer = _decisionInterval;
        PlayStateAnimation(nextState);
        _onStateChanged?.Invoke(previousState, nextState);
    }

    private void PlayStateAnimation(string stateId)
    {
        string animation = _states[stateId].Definition.Animation;
        if (!string.IsNullOrWhiteSpace(animation))
            _context.Animator.PlayAnimation(animation);
    }
}

#endregion
