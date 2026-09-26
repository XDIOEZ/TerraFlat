using System;
using FlatWorld.WorldModel;
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
    private static readonly string[] FoodCapabilities =
        { AIBehaviorCapability.Mover, AIBehaviorCapability.Detector, AIBehaviorCapability.Food, AIBehaviorCapability.Animator };
    private static readonly string[] AttackCapabilities =
        { AIBehaviorCapability.Mover, AIBehaviorCapability.Detector, AIBehaviorCapability.Animator, AIBehaviorCapability.Damage };

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
        AIBehaviorGraphRegistry.RegisterNode(
            "sleep",
            CreateSleepNode,
            ValidateSleepParameters,
            MoverCapability);
        AIBehaviorGraphRegistry.RegisterNode(
            "forage",
            CreateForageNode,
            ValidateForageParameters,
            FoodCapabilities,
            parameters => AIBehaviorJsonParameters.ReadFloat(parameters, "itemSearchDistance"));
        AIBehaviorGraphRegistry.RegisterNode(
            "attack",
            CreateAttackNode,
            ValidateAttackParameters,
            AttackCapabilities,
            parameters => AIBehaviorJsonParameters.ReadFloat(parameters, "targetDistance"));

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
        AIBehaviorGraphRegistry.RegisterCondition(
            "timerElapsed",
            CreateTimerElapsedCondition,
            ValidateTimerConditionParameters);
        AIBehaviorGraphRegistry.RegisterCondition(
            "recentDamage",
            (context, _) => () => context.HasRecentDamageThreat,
            AIBehaviorJsonParameters.ValidateNoParameters,
            HealthCapability);
        AIBehaviorGraphRegistry.RegisterCondition(
            "recentDamageExpired",
            (context, _) => () => !context.HasRecentDamageThreat,
            AIBehaviorJsonParameters.ValidateNoParameters,
            HealthCapability);
        AIBehaviorGraphRegistry.RegisterCondition(
            "damagedSinceStateEnter",
            (context, _) => () => context.DamagedSinceStateEnter,
            AIBehaviorJsonParameters.ValidateNoParameters,
            HealthCapability);
        AIBehaviorGraphRegistry.RegisterCondition(
            "isNight",
            CreateNightCondition,
            ValidateDayPeriodParameters);
        AIBehaviorGraphRegistry.RegisterCondition(
            "isDay",
            CreateDayCondition,
            ValidateDayPeriodParameters);
        AIBehaviorGraphRegistry.RegisterCondition(
            "nutritionBelow",
            CreateNutritionBelowCondition,
            ValidateNutritionParameters,
            new[] { AIBehaviorCapability.Food });
        AIBehaviorGraphRegistry.RegisterCondition(
            "nutritionAbove",
            CreateNutritionAboveCondition,
            ValidateNutritionParameters,
            new[] { AIBehaviorCapability.Food });
        AIBehaviorGraphRegistry.RegisterCondition(
            "forageNeeded",
            CreateForageNeededCondition,
            ValidateForageNeededParameters,
            new[] { AIBehaviorCapability.Food });
        AIBehaviorGraphRegistry.RegisterCondition(
            "forageSatisfied",
            (context, _) => () => context.ForageSatisfied,
            AIBehaviorJsonParameters.ValidateNoParameters);
        AIBehaviorGraphRegistry.RegisterCondition(
            "forageUnavailable",
            (context, _) => () => !context.ForageAvailable,
            AIBehaviorJsonParameters.ValidateNoParameters);
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
                if (context.TryGetRecentDamageOrigin(out Vector3 damageOrigin))
                    return damageOrigin;
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

    private static AIStateNode<string> CreateSleepNode(
        AIBehaviorGraphContext context,
        string stateId,
        JObject parameters)
    {
        string cooldownTimer = AIBehaviorJsonParameters.ReadString(parameters, "cooldownTimer");
        float cooldown = AIBehaviorJsonParameters.ReadFloat(parameters, "cooldown");
        return new AIStoppedStateNode<string>(
            stateId,
            context.Mover.StopMovement,
            _ => context.Mover.StopMovement(),
            onExit: () => context.SetTimer(cooldownTimer, cooldown));
    }

    private static AIStateNode<string> CreateForageNode(
        AIBehaviorGraphContext context,
        string stateId,
        JObject parameters)
    {
        var config = new AIBehaviorForageNode.Config
        {
            ItemSearchDistance = AIBehaviorJsonParameters.ReadFloat(parameters, "itemSearchDistance"),
            ArrivalDistance = AIBehaviorJsonParameters.ReadFloat(parameters, "arrivalDistance"),
            FoodTags = AIBehaviorJsonParameters.ReadStringArray(parameters, "foodTags"),
            AllowRuntimeGrass = AIBehaviorJsonParameters.ReadBool(parameters, "allowRuntimeGrass"),
            GrassSearchRadius = AIBehaviorJsonParameters.ReadFloat(parameters, "grassSearchRadius"),
            GrassEatDuration = AIBehaviorJsonParameters.ReadFloat(parameters, "grassEatDuration"),
            SustenanceTimer = AIBehaviorJsonParameters.ReadString(parameters, "sustenanceTimer"),
            SustenanceDays = AIBehaviorJsonParameters.ReadFloat(parameters, "sustenanceDays"),
            NutritionExitRatio = AIBehaviorJsonParameters.ReadFloat(parameters, "nutritionExitRatio"),
            SearchRetryDelay = AIBehaviorJsonParameters.ReadFloat(parameters, "searchRetryDelay"),
            MoveAnimation = AIBehaviorJsonParameters.ReadString(parameters, "moveAnimation"),
            EatAnimation = AIBehaviorJsonParameters.ReadString(parameters, "eatAnimation")
        };
        var node = new AIBehaviorForageNode(context, config);
        return new AIStateNode<string>(
            stateId,
            node.Tick,
            node.Enter,
            node.Exit,
            AIStateAnimationRole.Moving);
    }

    private static AIStateNode<string> CreateAttackNode(
        AIBehaviorGraphContext context,
        string stateId,
        JObject parameters)
    {
        var config = new AIBehaviorAttackNode.Config
        {
            TargetDistance = AIBehaviorJsonParameters.ReadFloat(parameters, "targetDistance"),
            TriggerDistance = AIBehaviorJsonParameters.ReadFloat(parameters, "triggerDistance"),
            IncludePlayers = AIBehaviorJsonParameters.ReadBool(parameters, "includePlayers"),
            ThreatTags = AIBehaviorJsonParameters.ReadStringArray(parameters, "threatTags"),
            Cooldown = AIBehaviorJsonParameters.ReadFloat(parameters, "cooldown"),
            Windup = AIBehaviorJsonParameters.ReadFloat(parameters, "windup"),
            DamageWindow = AIBehaviorJsonParameters.ReadFloat(parameters, "damageWindow"),
            Recovery = AIBehaviorJsonParameters.ReadFloat(parameters, "recovery"),
            Animation = AIBehaviorJsonParameters.ReadString(parameters, "animation")
        };
        var node = new AIBehaviorAttackNode(context, config);
        return new AIStateNode<string>(
            stateId,
            node.Tick,
            node.Enter,
            node.Exit,
            AIStateAnimationRole.Stopped);
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

    private static Func<bool> CreateTimerElapsedCondition(AIBehaviorGraphContext context, JObject parameters)
    {
        string key = AIBehaviorJsonParameters.ReadString(parameters, "key");
        return () => context.IsTimerElapsed(key);
    }

    private static Func<bool> CreateNightCondition(AIBehaviorGraphContext context, JObject parameters)
    {
        float dayStart = AIBehaviorJsonParameters.ReadFloat(parameters, "dayStartRatio");
        float dayEnd = AIBehaviorJsonParameters.ReadFloat(parameters, "dayEndRatio");
        return () => context.IsNightTime(dayStart, dayEnd);
    }

    private static Func<bool> CreateDayCondition(AIBehaviorGraphContext context, JObject parameters)
    {
        float dayStart = AIBehaviorJsonParameters.ReadFloat(parameters, "dayStartRatio");
        float dayEnd = AIBehaviorJsonParameters.ReadFloat(parameters, "dayEndRatio");
        return () => !context.IsNightTime(dayStart, dayEnd);
    }

    private static Func<bool> CreateNutritionBelowCondition(AIBehaviorGraphContext context, JObject parameters)
    {
        float ratio = AIBehaviorJsonParameters.ReadFloat(parameters, "ratio");
        return () => context.GetNutritionRate() < ratio;
    }

    private static Func<bool> CreateNutritionAboveCondition(AIBehaviorGraphContext context, JObject parameters)
    {
        float ratio = AIBehaviorJsonParameters.ReadFloat(parameters, "ratio");
        return () => context.GetNutritionRate() >= ratio;
    }

    private static Func<bool> CreateForageNeededCondition(AIBehaviorGraphContext context, JObject parameters)
    {
        float nutritionRatio = AIBehaviorJsonParameters.ReadFloat(parameters, "nutritionRatio");
        string sustenanceTimer = AIBehaviorJsonParameters.ReadString(parameters, "sustenanceTimer");
        return () => context.IsTimerElapsed(sustenanceTimer) || context.GetNutritionRate() <= nutritionRatio;
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

    private static void ValidateSleepParameters(JObject parameters)
    {
        AIBehaviorJsonParameters.ValidateObject(parameters, new[] { "cooldownTimer", "cooldown" });
        AIBehaviorJsonParameters.ReadString(parameters, "cooldownTimer");
        if (AIBehaviorJsonParameters.ReadFloat(parameters, "cooldown") < 0f)
            throw new InvalidOperationException("sleep 节点 cooldown 不能小于 0。");
    }

    private static void ValidateForageParameters(JObject parameters)
    {
        AIBehaviorJsonParameters.ValidateObject(
            parameters,
            new[]
            {
                "itemSearchDistance", "arrivalDistance", "foodTags", "allowRuntimeGrass",
                "grassSearchRadius", "grassEatDuration", "sustenanceTimer", "sustenanceDays",
                "nutritionExitRatio", "searchRetryDelay", "moveAnimation", "eatAnimation"
            });

        float itemSearchDistance = AIBehaviorJsonParameters.ReadFloat(parameters, "itemSearchDistance");
        float arrivalDistance = AIBehaviorJsonParameters.ReadFloat(parameters, "arrivalDistance");
        string[] foodTags = AIBehaviorJsonParameters.ReadStringArray(parameters, "foodTags");
        bool allowRuntimeGrass = AIBehaviorJsonParameters.ReadBool(parameters, "allowRuntimeGrass");
        float grassSearchRadius = AIBehaviorJsonParameters.ReadFloat(parameters, "grassSearchRadius");
        float grassEatDuration = AIBehaviorJsonParameters.ReadFloat(parameters, "grassEatDuration");
        AIBehaviorJsonParameters.ReadString(parameters, "sustenanceTimer");
        float sustenanceDays = AIBehaviorJsonParameters.ReadFloat(parameters, "sustenanceDays");
        float nutritionExitRatio = AIBehaviorJsonParameters.ReadFloat(parameters, "nutritionExitRatio");
        float searchRetryDelay = AIBehaviorJsonParameters.ReadFloat(parameters, "searchRetryDelay");
        AIBehaviorJsonParameters.ReadString(parameters, "moveAnimation");
        AIBehaviorJsonParameters.ReadString(parameters, "eatAnimation");

        if (itemSearchDistance <= 0f || arrivalDistance <= 0f || arrivalDistance > itemSearchDistance ||
            grassSearchRadius <= 0f || grassEatDuration < 0f || sustenanceDays <= 0f ||
            nutritionExitRatio < 0f || nutritionExitRatio > 1f || searchRetryDelay < 0.05f)
        {
            throw new InvalidOperationException("forage 节点参数超出有效范围。");
        }
        if (!allowRuntimeGrass && foodTags.Length == 0)
            throw new InvalidOperationException("forage 节点至少需要运行时草或一个 foodTags 目标来源。");
    }

    private static void ValidateAttackParameters(JObject parameters)
    {
        AIBehaviorJsonParameters.ValidateObject(
            parameters,
            new[]
            {
                "targetDistance", "triggerDistance", "includePlayers", "threatTags",
                "cooldown", "windup", "damageWindow", "recovery", "animation"
            });

        float targetDistance = AIBehaviorJsonParameters.ReadFloat(parameters, "targetDistance");
        float triggerDistance = AIBehaviorJsonParameters.ReadFloat(parameters, "triggerDistance");
        bool includePlayers = AIBehaviorJsonParameters.ReadBool(parameters, "includePlayers");
        string[] tags = AIBehaviorJsonParameters.ReadStringArray(parameters, "threatTags");
        float cooldown = AIBehaviorJsonParameters.ReadFloat(parameters, "cooldown");
        float windup = AIBehaviorJsonParameters.ReadFloat(parameters, "windup");
        float damageWindow = AIBehaviorJsonParameters.ReadFloat(parameters, "damageWindow");
        float recovery = AIBehaviorJsonParameters.ReadFloat(parameters, "recovery");
        AIBehaviorJsonParameters.ReadString(parameters, "animation");

        AIBehaviorJsonParameters.RequireThreatSource(targetDistance, targetDistance, includePlayers, tags);
        if (triggerDistance <= 0f || triggerDistance > targetDistance || cooldown < 0f || windup < 0f ||
            damageWindow <= 0f || recovery < 0f)
        {
            throw new InvalidOperationException("attack 节点参数超出有效范围。");
        }
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

    private static void ValidateTimerConditionParameters(JObject parameters)
    {
        AIBehaviorJsonParameters.ValidateObject(parameters, new[] { "key" });
        AIBehaviorJsonParameters.ReadString(parameters, "key");
    }

    private static void ValidateDayPeriodParameters(JObject parameters)
    {
        AIBehaviorJsonParameters.ValidateObject(parameters, new[] { "dayStartRatio", "dayEndRatio" });
        float dayStart = AIBehaviorJsonParameters.ReadFloat(parameters, "dayStartRatio");
        float dayEnd = AIBehaviorJsonParameters.ReadFloat(parameters, "dayEndRatio");
        if (dayStart < 0f || dayStart > 1f || dayEnd < 0f || dayEnd > 1f || dayStart >= dayEnd)
            throw new InvalidOperationException("昼夜条件要求 0 <= dayStartRatio < dayEndRatio <= 1。");
    }

    private static void ValidateNutritionParameters(JObject parameters)
    {
        AIBehaviorJsonParameters.ValidateObject(parameters, new[] { "ratio" });
        float ratio = AIBehaviorJsonParameters.ReadFloat(parameters, "ratio");
        if (ratio < 0f || ratio > 1f)
            throw new InvalidOperationException("营养比例必须在 0 到 1 之间。");
    }

    private static void ValidateForageNeededParameters(JObject parameters)
    {
        AIBehaviorJsonParameters.ValidateObject(parameters, new[] { "nutritionRatio", "sustenanceTimer" });
        float ratio = AIBehaviorJsonParameters.ReadFloat(parameters, "nutritionRatio");
        AIBehaviorJsonParameters.ReadString(parameters, "sustenanceTimer");
        if (ratio < 0f || ratio > 1f)
            throw new InvalidOperationException("forageNeeded nutritionRatio 必须在 0 到 1 之间。");
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

#region 标准觅食节点

/// <summary>
/// 通用觅食节点：优先处理到期的运行时草地摄食，否则按物品 Tag 寻找食物；
/// 只负责目标获取、移动和摄食，是否进入/退出由行为图条件决定。
/// </summary>
internal sealed class AIBehaviorForageNode
{
    internal sealed class Config
    {
        public float ItemSearchDistance; // 物品食物搜索距离。
        public float ArrivalDistance; // 进入摄食动作的距离。
        public string[] FoodTags; // 可食物品标签。
        public bool AllowRuntimeGrass; // 是否允许消费地形草。
        public float GrassSearchRadius; // 地形草搜索半径。
        public float GrassEatDuration; // 到达草地后的进食动作时长。
        public string SustenanceTimer; // 草食维持计时器键。
        public float SustenanceDays; // 一次吃草维持的世界天数。
        public float NutritionExitRatio; // 达到该营养比例后本轮觅食完成。
        public float SearchRetryDelay; // 无目标时的再次搜索间隔。
        public string MoveAnimation; // 移动动画。
        public string EatAnimation; // 摄食动画。
    }

    private readonly AIBehaviorGraphContext _context;
    private readonly Config _config;
    private Item _itemTarget;
    private ChunkTerrainData _grassTerrain;
    private Vector2Int _grassLocal;
    private Vector2Int _grassWorld;
    private bool _hasGrassTarget;
    private float _searchCooldown;
    private float _eatElapsed;

    public AIBehaviorForageNode(AIBehaviorGraphContext context, Config config)
    {
        _context = context;
        _config = config;
        if (_config.AllowRuntimeGrass)
            _context.ConfigureNutritionSustenance(_config.SustenanceTimer, _config.SustenanceDays);
    }

    public void Enter()
    {
        ClearTargets();
        _searchCooldown = 0f;
        _eatElapsed = 0f;
        _context.ForageSatisfied = false;
        _context.ForageAvailable = true;
        _context.Mover.StopMovement();
    }

    public void Tick(float deltaTime)
    {
        if (_context.ForageSatisfied)
        {
            _context.Mover.StopMovement();
            return;
        }

        _searchCooldown = Mathf.Max(0f, _searchCooldown - Mathf.Max(0f, deltaTime));

        if (_config.AllowRuntimeGrass && _context.IsTimerElapsed(_config.SustenanceTimer))
        {
            TickRuntimeGrass(deltaTime);
            return;
        }

        if (_context.GetNutritionRate() >= _config.NutritionExitRatio)
        {
            _context.ForageSatisfied = true;
            _context.ForageAvailable = true;
            _context.Mover.StopMovement();
            return;
        }

        TickItemFood();
    }

    public void Exit()
    {
        ClearTargets();
        _context.ForageSatisfied = false;
        _context.ForageAvailable = true;
        _context.Mover.StopMovement();
    }

    private void TickRuntimeGrass(float deltaTime)
    {
        if (!HasValidGrassTarget())
        {
            ClearGrassTarget();
            if (_searchCooldown > 0f)
            {
                _context.ForageAvailable = false;
                _context.Mover.StopMovement();
                return;
            }

            _searchCooldown = Mathf.Max(0.05f, _config.SearchRetryDelay);
            ChunkMgr chunkManager = ChunkMgr.Instance;
            if (chunkManager == null ||
                !chunkManager.TryFindRuntimeGrassNear(
                    _context.Position,
                    Mathf.Max(_config.ArrivalDistance, _config.GrassSearchRadius),
                    out RuntimeTerrainTileSample runtimeGrass))
            {
                _context.ForageAvailable = false;
                _context.Mover.StopMovement();
                return;
            }

            _grassTerrain = runtimeGrass.Terrain;
            _grassLocal = runtimeGrass.LocalCell;
            _grassWorld = runtimeGrass.WorldCell;
            _hasGrassTarget = true;
            _itemTarget = null;
            _eatElapsed = 0f;
        }

        _context.ForageAvailable = true;
        Vector2 targetPosition = new(_grassWorld.x + 0.5f, _grassWorld.y + 0.5f);
        float distance = WorldTopologyRuntime.Distance(_context.Position, targetPosition);
        if (distance > _config.ArrivalDistance)
        {
            Play(_config.MoveAnimation);
            _eatElapsed = 0f;
            _context.Mover.SetDestination(targetPosition);
            return;
        }

        _context.Mover.StopMovement();
        Play(_config.EatAnimation);
        _eatElapsed += Mathf.Max(0f, deltaTime);
        if (_eatElapsed < _config.GrassEatDuration)
            return;

        ChunkMgr manager = ChunkMgr.Instance;
        if (manager != null && manager.TryConsumeRuntimeGrass(_grassWorld))
        {
            _context.RestoreNutritionSustenance(_config.SustenanceTimer, _config.SustenanceDays);
            _context.ForageSatisfied = true;
            _context.ForageAvailable = true;
        }
        else
        {
            _context.ForageAvailable = false;
        }

        ClearGrassTarget();
    }

    private void TickItemFood()
    {
        if (!IsValidItemTarget(_itemTarget))
        {
            _itemTarget = null;
            if (_searchCooldown > 0f)
            {
                _context.ForageAvailable = false;
                _context.Mover.StopMovement();
                return;
            }

            _searchCooldown = Mathf.Max(0.05f, _config.SearchRetryDelay);
            _itemTarget = _context.FindClosestTaggedItem(_config.ItemSearchDistance, _config.FoodTags);
            if (!IsValidItemTarget(_itemTarget))
            {
                _itemTarget = null;
                _context.ForageAvailable = false;
                _context.Mover.StopMovement();
                return;
            }
        }

        _context.ForageAvailable = true;
        float distance = WorldTopologyRuntime.Distance(_context.Position, _itemTarget.transform.position);
        if (distance > _config.ArrivalDistance)
        {
            Play(_config.MoveAnimation);
            _context.Mover.SetDestination(_itemTarget.transform.position);
            return;
        }

        _context.Mover.StopMovement();
        Play(_config.EatAnimation);
        Mod_Food targetFood = _itemTarget.GetComponentInChildren<Mod_Food>(true);
        if (targetFood == null)
        {
            _itemTarget = null;
            _context.ForageAvailable = false;
            return;
        }

        _context.Food.Eat(targetFood);
        if (_context.GetNutritionRate() >= _config.NutritionExitRatio)
            _context.ForageSatisfied = true;

        if (!IsValidItemTarget(_itemTarget))
            _itemTarget = null;
    }

    private bool HasValidGrassTarget()
    {
        return _hasGrassTarget &&
               _grassTerrain != null &&
               !_grassTerrain.IsDisposed &&
               _grassTerrain.GetGrass(_grassLocal.x, _grassLocal.y) == ChunkTerrainData.GrassPresent;
    }

    private static bool IsValidItemTarget(Item target)
    {
        return target != null &&
               !target.DestructionHandled &&
               target.itemData?.Stack != null &&
               target.itemData.Stack.Amount > 0f;
    }

    private void ClearTargets()
    {
        _itemTarget = null;
        ClearGrassTarget();
    }

    private void ClearGrassTarget()
    {
        _grassTerrain = null;
        _grassLocal = default;
        _grassWorld = default;
        _hasGrassTarget = false;
        _eatElapsed = 0f;
    }

    private void Play(string animation)
    {
        if (!string.IsNullOrWhiteSpace(animation))
            _context.Animator.PlayAnimation(animation);
    }
}

#endregion

#region 标准攻击节点

/// <summary>通用近战攻击节点；目标筛选、攻击时序和伤害窗口均由稳定能力组合完成。</summary>
internal sealed class AIBehaviorAttackNode
{
    internal sealed class Config
    {
        public float TargetDistance;
        public float TriggerDistance;
        public bool IncludePlayers;
        public string[] ThreatTags;
        public float Cooldown;
        public float Windup;
        public float DamageWindow;
        public float Recovery;
        public string Animation;
    }

    private readonly AIBehaviorGraphContext _context;
    private readonly Config _config;
    private readonly AI_AttackController _attack = new();
    private Item _target;

    public AIBehaviorAttackNode(AIBehaviorGraphContext context, Config config)
    {
        _context = context;
        _config = config;
        _attack.Cooldown = config.Cooldown;
        _attack.WindupDuration = config.Windup;
        _attack.DamageWindow = config.DamageWindow;
        _attack.RecoveryDuration = config.Recovery;
        _attack.Bind(context.Actor);
        _attack.Reset();
        _context.RegisterBackgroundTick(_attack.Tick);
    }

    public void Enter()
    {
        _target = null;
        _attack.OnEnterAttackState();
        _context.Mover.StopMovement();
    }

    public void Tick(float _)
    {
        if (!_context.IsLivingAttackTarget(_target) ||
            !AIFleeUtility.IsWithinEscapeRange(
                _context.Position,
                _target,
                _context.Detector,
                _config.TargetDistance))
        {
            _target = _context.FindClosestThreat(
                _config.TargetDistance,
                _config.ThreatTags,
                _config.IncludePlayers);
        }

        if (!_context.IsLivingAttackTarget(_target))
        {
            _attack.StopWindow();
            _context.Mover.StopMovement();
            return;
        }

        float distance = WorldTopologyRuntime.Distance(_context.Position, _target.transform.position);
        if (distance > _config.TriggerDistance && !_attack.IsAttackLocked)
        {
            _attack.StopWindow();
            _context.Mover.StopMovement();
            return;
        }

        Vector3 targetPosition = _target.transform.position;
        _context.Mover.StopMovement();
        _context.FaceTarget(targetPosition, true);
        if (!_attack.IsAttackLocked && _attack.IsCooldownDone)
        {
            _attack.StartWindow(
                _context.Animator,
                _config.Animation,
                WorldTopologyRuntime.ShortestDelta(_context.Position, targetPosition));
        }
    }

    public void Exit()
    {
        _attack.OnExitAttackState();
        _target = null;
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

    public static string ReadString(JObject parameters, string name)
    {
        string value = RequireType(parameters, name, JTokenType.String).Value<string>();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"AI 行为图参数 {name} 必须是非空字符串。");
        return value;
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
