using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>标准 JSON AI 节点与条件；新增玩法能力时通过注册表扩展，不改图执行器。</summary>
internal static class AIBehaviorGraphBuiltins
{
    #region 标准节点和条件注册

    private static readonly string[] MoverCapability = { AIBehaviorCapability.Mover };
    private static readonly string[] DetectorCapabilities =
        { AIBehaviorCapability.Mover, AIBehaviorCapability.Detector };
    private static readonly string[] HealthCapability = { AIBehaviorCapability.Health };

    public static void RegisterBuiltins()
    {
        AIBehaviorGraphRegistry.RegisterNode(
            "stop",
            CreateStopNode,
            AIBehaviorJsonParameters.ValidateNoParameters,
            MoverCapability);
        AIBehaviorGraphRegistry.RegisterNode(
            "wander",
            CreateWanderNode,
            ValidateWanderParameters,
            MoverCapability);
        AIBehaviorGraphRegistry.RegisterNode(
            "approach",
            CreateApproachNode,
            ValidateApproachParameters,
            DetectorCapabilities,
            parameters => AIBehaviorJsonParameters.ReadFloat(parameters, "distance"));
        AIBehaviorGraphRegistry.RegisterNode(
            "flee",
            CreateFleeNode,
            ValidateFleeParameters,
            DetectorCapabilities,
            parameters => AIBehaviorJsonParameters.ReadFloat(parameters, "threatDistance"));

        AIBehaviorGraphRegistry.RegisterCondition(
            "always",
            (_, _) => () => true,
            AIBehaviorJsonParameters.ValidateNoParameters);
        AIBehaviorGraphRegistry.RegisterCondition(
            "healthBelow",
            CreateHealthBelowCondition,
            ValidateHealthParameters,
            HealthCapability);
        AIBehaviorGraphRegistry.RegisterCondition(
            "healthAbove",
            CreateHealthAboveCondition,
            ValidateHealthParameters,
            HealthCapability);
        AIBehaviorGraphRegistry.RegisterCondition(
            "stateElapsedAtLeast",
            CreateStateElapsedCondition,
            ValidateStateElapsedParameters);
        AIBehaviorGraphRegistry.RegisterCondition(
            "threatWithinDistance",
            CreateThreatWithinCondition,
            ValidateThreatConditionParameters,
            new[] { AIBehaviorCapability.Detector },
            parameters => AIBehaviorJsonParameters.ReadFloat(parameters, "distance"));
        AIBehaviorGraphRegistry.RegisterCondition(
            "threatOutsideDistance",
            CreateThreatOutsideCondition,
            ValidateThreatConditionParameters,
            new[] { AIBehaviorCapability.Detector },
            parameters => AIBehaviorJsonParameters.ReadFloat(parameters, "distance"));
    }

    private static AIStateNode<string> CreateStopNode(
        AIBehaviorGraphContext context,
        string stateId,
        JObject parameters)
    {
        return new AIStoppedStateNode<string>(
            stateId,
            context.Mover.StopMovement,
            _ => context.Mover.StopMovement());
    }

    private static AIStateNode<string> CreateWanderNode(
        AIBehaviorGraphContext context,
        string stateId,
        JObject parameters)
    {
        var config = new AIBehaviorWanderNode.Config
        {
            Radius = AIBehaviorJsonParameters.ReadFloat(parameters, "radius"),
            StopDistance = AIBehaviorJsonParameters.ReadFloat(parameters, "stopDistance"),
            PauseMin = AIBehaviorJsonParameters.ReadFloat(parameters, "pauseMin"),
            PauseMax = AIBehaviorJsonParameters.ReadFloat(parameters, "pauseMax"),
            MinimumDistance = AIBehaviorJsonParameters.ReadFloat(parameters, "minimumDistance"),
            AvoidHighPenalty = AIBehaviorJsonParameters.ReadBool(parameters, "avoidHighPenalty"),
            DangerPenalty = AIBehaviorJsonParameters.ReadInt(parameters, "dangerPenalty"),
            SampleCount = AIBehaviorJsonParameters.ReadInt(parameters, "sampleCount"),
            PenaltyWeight = AIBehaviorJsonParameters.ReadFloat(parameters, "penaltyWeight")
        };
        var node = new AIBehaviorWanderNode(context, config);
        return new AIStateNode<string>(
            stateId,
            node.Tick,
            node.Enter,
            node.Exit,
            AIStateAnimationRole.Moving);
    }

    private static AIStateNode<string> CreateFleeNode(
        AIBehaviorGraphContext context,
        string stateId,
        JObject parameters)
    {
        float runDistance = AIBehaviorJsonParameters.ReadFloat(parameters, "distance");
        float threatDistance = AIBehaviorJsonParameters.ReadFloat(parameters, "threatDistance");
        bool includePlayers = AIBehaviorJsonParameters.ReadBool(parameters, "includePlayers");
        string[] threatTags = AIBehaviorJsonParameters.ReadStringArray(parameters, "threatTags");

        return new AIFleeStateNode<string>(
            stateId,
            () =>
            {
                Item threat = context.FindClosestThreat(threatDistance, threatTags, includePlayers);
                return threat != null ? threat.transform.position : (Vector3?)null;
            },
            (threatPosition, distance) => AIFleeUtility.ResolveNavigableGroundEscapeDestination(
                context.Position,
                threatPosition,
                distance,
                WorldTopologyRuntime.ShortestDelta(threatPosition, context.Position)),
            () => runDistance,
            destination => context.Mover.SetDestination(destination),
            context.Mover.StopMovement,
            () => context.Position,
            () => context.Mover.HasReachedTarget,
            () => context.Mover.DestinationResult,
            context.Mover);
    }

    private static AIStateNode<string> CreateApproachNode(
        AIBehaviorGraphContext context,
        string stateId,
        JObject parameters)
    {
        float distance = AIBehaviorJsonParameters.ReadFloat(parameters, "distance");
        float arrivalDistance = AIBehaviorJsonParameters.ReadFloat(parameters, "arrivalDistance");
        bool includePlayers = AIBehaviorJsonParameters.ReadBool(parameters, "includePlayers");
        string[] threatTags = AIBehaviorJsonParameters.ReadStringArray(parameters, "threatTags");

        return new AIAdvanceStateNode<string>(
            stateId,
            () =>
            {
                Item target = context.FindClosestThreat(distance, threatTags, includePlayers);
                return target != null
                    ? new AIAdvanceTarget(true, target.transform.position)
                    : AIAdvanceTarget.None;
            },
            () => context.Position,
            () => arrivalDistance,
            destination => context.Mover.SetDestination(destination),
            context.Mover.StopMovement);
    }

    private static Func<bool> CreateHealthBelowCondition(AIBehaviorGraphContext context, JObject parameters)
    {
        float ratio = AIBehaviorJsonParameters.ReadFloat(parameters, "ratio");
        return () => ReadHealthRatio(context.Health) < ratio;
    }

    private static Func<bool> CreateHealthAboveCondition(AIBehaviorGraphContext context, JObject parameters)
    {
        float ratio = AIBehaviorJsonParameters.ReadFloat(parameters, "ratio");
        return () => ReadHealthRatio(context.Health) > ratio;
    }

    private static float ReadHealthRatio(DamageReceiver health)
    {
        return health.MaxHp <= 0f ? 0f : Mathf.Clamp01(health.Hp / health.MaxHp);
    }

    private static Func<bool> CreateStateElapsedCondition(AIBehaviorGraphContext context, JObject parameters)
    {
        float seconds = AIBehaviorJsonParameters.ReadFloat(parameters, "seconds");
        return () => context.StateElapsed >= seconds;
    }

    private static Func<bool> CreateThreatWithinCondition(AIBehaviorGraphContext context, JObject parameters)
    {
        float distance = AIBehaviorJsonParameters.ReadFloat(parameters, "distance");
        bool includePlayers = AIBehaviorJsonParameters.ReadBool(parameters, "includePlayers");
        string[] tags = AIBehaviorJsonParameters.ReadStringArray(parameters, "threatTags");
        return () => context.FindClosestThreat(distance, tags, includePlayers) != null;
    }

    private static Func<bool> CreateThreatOutsideCondition(AIBehaviorGraphContext context, JObject parameters)
    {
        float distance = AIBehaviorJsonParameters.ReadFloat(parameters, "distance");
        bool includePlayers = AIBehaviorJsonParameters.ReadBool(parameters, "includePlayers");
        string[] tags = AIBehaviorJsonParameters.ReadStringArray(parameters, "threatTags");
        return () => context.FindClosestThreat(distance, tags, includePlayers) == null;
    }

    private static void ValidateWanderParameters(JObject parameters)
    {
        AIBehaviorJsonParameters.ValidateObject(
            parameters,
            new[]
            {
                "radius", "stopDistance", "pauseMin", "pauseMax", "minimumDistance",
                "avoidHighPenalty", "dangerPenalty", "sampleCount", "penaltyWeight"
            });
        float radius = AIBehaviorJsonParameters.ReadFloat(parameters, "radius");
        float stopDistance = AIBehaviorJsonParameters.ReadFloat(parameters, "stopDistance");
        float pauseMin = AIBehaviorJsonParameters.ReadFloat(parameters, "pauseMin");
        float pauseMax = AIBehaviorJsonParameters.ReadFloat(parameters, "pauseMax");
        float minimumDistance = AIBehaviorJsonParameters.ReadFloat(parameters, "minimumDistance");
        int dangerPenalty = AIBehaviorJsonParameters.ReadInt(parameters, "dangerPenalty");
        int sampleCount = AIBehaviorJsonParameters.ReadInt(parameters, "sampleCount");
        float penaltyWeight = AIBehaviorJsonParameters.ReadFloat(parameters, "penaltyWeight");
        AIBehaviorJsonParameters.ReadBool(parameters, "avoidHighPenalty");

        if (radius <= 0f || stopDistance <= 0f || stopDistance > radius ||
            pauseMin < 0f || pauseMax < pauseMin || minimumDistance < 0f || minimumDistance > radius ||
            dangerPenalty < 0 || sampleCount < 1 || penaltyWeight < 0f)
            throw new InvalidOperationException("wander 节点参数超出有效范围。");
    }

    private static void ValidateFleeParameters(JObject parameters)
    {
        AIBehaviorJsonParameters.ValidateObject(
            parameters,
            new[] { "distance", "threatDistance", "includePlayers", "threatTags" });
        float distance = AIBehaviorJsonParameters.ReadFloat(parameters, "distance");
        float threatDistance = AIBehaviorJsonParameters.ReadFloat(parameters, "threatDistance");
        bool includePlayers = AIBehaviorJsonParameters.ReadBool(parameters, "includePlayers");
        string[] tags = AIBehaviorJsonParameters.ReadStringArray(parameters, "threatTags");
        AIBehaviorJsonParameters.RequireThreatSource(distance, threatDistance, includePlayers, tags);
    }

    private static void ValidateApproachParameters(JObject parameters)
    {
        AIBehaviorJsonParameters.ValidateObject(
            parameters,
            new[] { "distance", "arrivalDistance", "includePlayers", "threatTags" });
        float distance = AIBehaviorJsonParameters.ReadFloat(parameters, "distance");
        float arrivalDistance = AIBehaviorJsonParameters.ReadFloat(parameters, "arrivalDistance");
        bool includePlayers = AIBehaviorJsonParameters.ReadBool(parameters, "includePlayers");
        string[] tags = AIBehaviorJsonParameters.ReadStringArray(parameters, "threatTags");
        AIBehaviorJsonParameters.RequireThreatSource(distance, distance, includePlayers, tags);
        if (arrivalDistance <= 0f || arrivalDistance > distance)
            throw new InvalidOperationException("approach 节点的 arrivalDistance 必须大于 0 且不大于目标搜索 distance。");
    }

    private static void ValidateThreatConditionParameters(JObject parameters)
    {
        AIBehaviorJsonParameters.ValidateObject(
            parameters,
            new[] { "distance", "includePlayers", "threatTags" });
        float distance = AIBehaviorJsonParameters.ReadFloat(parameters, "distance");
        bool includePlayers = AIBehaviorJsonParameters.ReadBool(parameters, "includePlayers");
        string[] tags = AIBehaviorJsonParameters.ReadStringArray(parameters, "threatTags");
        AIBehaviorJsonParameters.RequireThreatSource(distance, distance, includePlayers, tags);
    }

    private static void ValidateHealthParameters(JObject parameters)
    {
        AIBehaviorJsonParameters.ValidateObject(parameters, new[] { "ratio" });
        float ratio = AIBehaviorJsonParameters.ReadFloat(parameters, "ratio");
        if (ratio < 0f || ratio > 1f)
            throw new InvalidOperationException("healthBelow/healthAbove 的 ratio 必须在 0 到 1 之间。");
    }

    private static void ValidateStateElapsedParameters(JObject parameters)
    {
        AIBehaviorJsonParameters.ValidateObject(parameters, new[] { "seconds" });
        if (AIBehaviorJsonParameters.ReadFloat(parameters, "seconds") < 0f)
            throw new InvalidOperationException("stateElapsedAtLeast 的 seconds 不能小于 0。");
    }

    #endregion
}

#region 标准闲逛节点

/// <summary>闲逛节点运行态；每次进入只创建一个安全目标，到达后等待再选下一个。</summary>
internal sealed class AIBehaviorWanderNode
{
    internal sealed class Config
    {
        public float Radius; // 目标采样范围。
        public float StopDistance; // 到达目标后的停车距离。
        public float PauseMin; // 到达后最短停留时间。
        public float PauseMax; // 到达后最长停留时间。
        public float MinimumDistance; // 目标与当前位置的最小间距。
        public bool AvoidHighPenalty; // 是否过滤高代价地块。
        public int DangerPenalty; // 危险地块代价阈值。
        public int SampleCount; // 安全偏移候选数。
        public float PenaltyWeight; // 地块代价权重。
    }

    private readonly AIBehaviorGraphContext _context; // 当前 Actor 能力。
    private readonly Config _config; // 闲逛节点参数快照。
    private Vector3 _target; // 当前导航目标。
    private float _pauseRemaining; // 到达目标后的剩余停留时间。
    private bool _hasTarget; // 是否正在前往目标。

    public AIBehaviorWanderNode(AIBehaviorGraphContext context, Config config)
    {
        _context = context;
        _config = config;
    }

    public void Enter()
    {
        _hasTarget = false;
        _pauseRemaining = 0f;
        _context.Mover.StopMovement();
    }

    public void Tick(float deltaTime)
    {
        if (_hasTarget)
        {
            float distance = WorldTopologyRuntime.Distance(_context.Position, _target);
            if (distance <= _config.StopDistance || _context.Mover.HasReachedTarget)
            {
                _hasTarget = false;
                _pauseRemaining = UnityEngine.Random.Range(_config.PauseMin, _config.PauseMax);
                _context.Mover.StopMovement();
                return;
            }

            _context.Mover.SetDestination(_target);
            return;
        }

        _pauseRemaining = Mathf.Max(0f, _pauseRemaining - Mathf.Max(0f, deltaTime));
        if (_pauseRemaining > 0f)
        {
            _context.Mover.StopMovement();
            return;
        }

        Vector2 offset = UnityEngine.Random.insideUnitCircle * _config.Radius;
        offset = AI_WanderUtility.PickSaferOffset(
            _context.Position,
            offset,
            _config.Radius,
            _config.AvoidHighPenalty,
            _config.SampleCount,
            (uint)_config.DangerPenalty,
            _config.PenaltyWeight,
            _config.MinimumDistance);
        _target = WorldTopologyRuntime.NormalizePosition(_context.Position + (Vector3)offset);
        _hasTarget = true;
        _context.Mover.SetDestination(_target);
    }

    public void Exit()
    {
        _hasTarget = false;
        _pauseRemaining = 0f;
        _context.Mover.StopMovement();
    }
}

#endregion

#region 节点参数解析

/// <summary>严格读取状态节点/条件参数，阻止拼写错误悄悄进入运行时。</summary>
internal static class AIBehaviorJsonParameters
{
    public static void ValidateNoParameters(JObject parameters)
    {
        ValidateObject(parameters, Array.Empty<string>());
    }

    public static void ValidateObject(JObject parameters, string[] allowedProperties)
    {
        if (parameters == null)
            throw new InvalidOperationException("AI 行为图节点 parameters 必须是 JSON 对象。");

        foreach (JProperty property in parameters.Properties())
        {
            bool allowed = false;
            for (int i = 0; i < allowedProperties.Length; i++)
            {
                if (string.Equals(property.Name, allowedProperties[i], StringComparison.OrdinalIgnoreCase))
                {
                    allowed = true;
                    break;
                }
            }
            if (!allowed)
                throw new InvalidOperationException($"AI 行为图包含未知节点参数：{property.Name}");
        }

        for (int i = 0; i < allowedProperties.Length; i++)
        {
            if (parameters.GetValue(allowedProperties[i], StringComparison.OrdinalIgnoreCase) == null)
                throw new InvalidOperationException($"AI 行为图缺少必填节点参数：{allowedProperties[i]}");
        }
    }

    public static float ReadFloat(JObject parameters, string name)
    {
        JToken token = RequireType(parameters, name, JTokenType.Float, JTokenType.Integer);
        float value = token.Value<float>();
        if (float.IsNaN(value) || float.IsInfinity(value))
            throw new InvalidOperationException($"AI 行为图参数 {name} 必须是有限数值。");
        return value;
    }

    public static int ReadInt(JObject parameters, string name)
    {
        return RequireType(parameters, name, JTokenType.Integer).Value<int>();
    }

    public static bool ReadBool(JObject parameters, string name)
    {
        return RequireType(parameters, name, JTokenType.Boolean).Value<bool>();
    }

    public static string[] ReadStringArray(JObject parameters, string name)
    {
        JArray token = (JArray)RequireType(parameters, name, JTokenType.Array);
        var values = new string[token.Count];
        for (int i = 0; i < values.Length; i++)
        {
            JToken item = token[i];
            if (item == null || item.Type != JTokenType.String || string.IsNullOrWhiteSpace(item.Value<string>()))
                throw new InvalidOperationException($"AI 行为图参数 {name}[{i}] 必须是非空字符串。");
            values[i] = item.Value<string>();
        }
        return values;
    }

    public static void RequireThreatSource(float runDistance, float threatDistance, bool includePlayers, string[] tags)
    {
        if (runDistance <= 0f || threatDistance <= 0f)
            throw new InvalidOperationException("逃离距离与威胁距离必须大于 0。");
        if (!includePlayers && (tags == null || tags.Length == 0))
            throw new InvalidOperationException("威胁条件需要配置 includePlayers 或至少一个 threatTags。");
    }

    private static JToken RequireType(JObject parameters, string name, params JTokenType[] allowedTypes)
    {
        JToken token = parameters?.GetValue(name, StringComparison.OrdinalIgnoreCase);
        if (token == null)
            throw new InvalidOperationException($"AI 行为图缺少必填参数：{name}");
        for (int i = 0; i < allowedTypes.Length; i++)
        {
            if (token.Type == allowedTypes[i])
                return token;
        }
        throw new InvalidOperationException($"AI 行为图参数 {name} 类型无效：{token.Type}");
    }
}

#endregion
