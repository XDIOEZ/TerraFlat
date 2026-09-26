using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UltEvents;

#region Actor 能力上下文

/// <summary>状态图节点读取的统一 Actor 能力；能力由 AI Prefab 上的模块提供。</summary>
public sealed class AIBehaviorGraphContext
{
    public Item Actor { get; } // 当前行为图所属实体。
    public Mover_AI Mover { get; } // 通用移动能力。
    public Mod_ItemDetector Detector { get; } // 通用感知能力。
    public DamageReceiver Health { get; } // 通用生命能力。
    public Mod_Food Food { get; } // 通用营养能力。
    public Mod_AnimatorController Animator { get; } // 结构动画能力。
    public float StateElapsed { get; set; } // 当前 JSON 状态已持续时间。
    public Vector3 Position => Actor.transform.position;
    public bool DamagedSinceStateEnter { get; private set; } // 当前状态进入后是否受过有效伤害。
    public bool HasRecentDamageThreat => _recentDamageRemain > 0f; // 受击威胁记忆是否仍有效。
    public bool ForageSatisfied { get; internal set; } // 当前觅食节点是否已完成本轮目标。
    public bool ForageAvailable { get; internal set; } = true; // 当前觅食节点是否存在可用食物。

    private readonly Dictionary<string, float> _timers = new(StringComparer.Ordinal); // 可存档通用计时器。
    private readonly List<string> _timerKeys = new(); // 无分配推进计时器。
    private readonly List<Action<float>> _backgroundTicks = new(); // 节点注册的跨状态后台计时器。
    private readonly float _damageThreatMemoryDuration; // 受击来源记忆时长。
    private readonly bool _hasDamageModules; // 是否具备攻击伤害模块。
    private DamageReceiver _damageEventSource; // 当前受击事件来源。
    private Vector3 _recentDamageOrigin; // 最近一次有效伤害来源位置。
    private float _recentDamageRemain; // 受击来源剩余记忆时间。
    private string _nutritionSustenanceTimerKey; // 草食维持期使用的计时器键。
    private Mod_TurnBack _turnBody; // 通用朝向模块。

    public AIBehaviorGraphContext(
        Item actor,
        Mover_AI mover,
        Mod_ItemDetector detector,
        DamageReceiver health,
        Mod_Food food,
        Mod_AnimatorController animator,
        float damageThreatMemoryDuration,
        IReadOnlyDictionary<string, float> savedTimers)
    {
        Actor = actor ?? throw new ArgumentNullException(nameof(actor));
        Mover = mover;
        Detector = detector;
        Health = health;
        Food = food;
        Animator = animator;
        _damageThreatMemoryDuration = Mathf.Max(0f, damageThreatMemoryDuration);
        _hasDamageModules = actor.GetComponentsInChildren<Mod_Damage>(true).Length > 0;
        _turnBody = actor.GetComponentInChildren<Mod_TurnBack>(true);

        if (savedTimers != null)
        {
            foreach (KeyValuePair<string, float> pair in savedTimers)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                    continue;
                _timers[pair.Key] = Mathf.Max(0f, pair.Value);
                _timerKeys.Add(pair.Key);
            }
        }

        BindDamageEvents();
    }

    /// <summary>按能力清单验证 AI Prefab，缺少节点依赖时在加载阶段明确报错。</summary>
    public void RequireCapabilities(IReadOnlyCollection<string> capabilities)
    {
        foreach (string capability in capabilities)
        {
            bool available = capability switch
            {
                AIBehaviorCapability.Mover => Mover != null,
                AIBehaviorCapability.Detector => Detector != null,
                AIBehaviorCapability.Health => Health != null,
                AIBehaviorCapability.Food => Food != null,
                AIBehaviorCapability.Animator => Animator != null,
                AIBehaviorCapability.Damage => _hasDamageModules,
                _ => throw new InvalidOperationException($"AI 行为图引用了未注册的预置体能力：{capability}")
            };

            if (available)
                continue;

            string moduleName = capability switch
            {
                AIBehaviorCapability.Mover => ModText.Mover_AI,
                AIBehaviorCapability.Detector => ModText.Detector,
                AIBehaviorCapability.Health => ModText.Hp,
                AIBehaviorCapability.Food => ModText.Food,
                AIBehaviorCapability.Animator => ModText.AnimatorReceiver,
                AIBehaviorCapability.Damage => "Mod_Damage",
                _ => capability
            };
            throw new InvalidOperationException(
                $"Actor {Actor.itemData?.IDName ?? Actor.name} 的 AI 行为图需要预置体模块 {moduleName}。");
        }
    }

    /// <summary>从当前感知快照中按配置查找最近、可见且位于目标倍率范围内的威胁。</summary>
    public Item FindClosestThreat(float distance, string[] tags, bool includePlayers)
    {
        if (Detector == null)
            throw new InvalidOperationException("AI 行为图查找威胁需要 Mod_ItemDetector。");

        List<Item> detectedItems = Detector.CurrentItemsInArea;
        if (detectedItems == null)
            return null;

        Item closest = null;
        float closestDistance = float.MaxValue;
        for (int i = 0; i < detectedItems.Count; i++)
        {
            Item candidate = detectedItems[i];
            if (candidate == null || candidate == Actor)
                continue;

            bool isPlayer = candidate is Player;
            if ((!includePlayers || !isPlayer) && !HasAnyTag(candidate, tags))
                continue;

            if (!AIFleeUtility.IsWithinEscapeRange(Position, candidate, Detector, distance))
                continue;

            float candidateDistance = WorldTopologyRuntime.SqrDistance(Position, candidate.transform.position);
            if (candidateDistance >= closestDistance)
                continue;

            closest = candidate;
            closestDistance = candidateDistance;
        }

        return closest;
    }

    /// <summary>按标签和有效感知距离查找最近物品目标。</summary>
    public Item FindClosestTaggedItem(float distance, string[] tags)
    {
        if (Detector == null)
            throw new InvalidOperationException("AI 行为图查找物品需要 Mod_ItemDetector。");

        List<Item> detectedItems = Detector.CurrentItemsInArea;
        if (detectedItems == null)
            return null;

        Item closest = null;
        float closestDistance = float.MaxValue;
        for (int i = 0; i < detectedItems.Count; i++)
        {
            Item candidate = detectedItems[i];
            if (candidate == null || candidate == Actor || candidate.DestructionHandled ||
                !HasAnyTag(candidate, tags))
                continue;

            float effectiveDistance = Mod_ItemDetector.CalculateEffectiveDetectionRadius(distance, candidate);
            float distanceSqr = WorldTopologyRuntime.SqrDistance(Position, candidate.transform.position);
            if (distanceSqr > effectiveDistance * effectiveDistance || distanceSqr >= closestDistance)
                continue;

            closest = candidate;
            closestDistance = distanceSqr;
        }

        return closest;
    }

    /// <summary>读取当前营养比例。</summary>
    public float GetNutritionRate()
    {
        return Food?.Data?.nutrition == null
            ? 0f
            : Mathf.Clamp01(Food.Data.nutrition.GetFoodRate());
    }

    /// <summary>进入新状态时清除仅属于上一状态的瞬时事实。</summary>
    public void BeginState()
    {
        DamagedSinceStateEnter = false;
    }

    /// <summary>推进行为图共享计时器和受击记忆。</summary>
    public void Tick(float deltaTime)
    {
        float step = Mathf.Max(0f, deltaTime);
        for (int i = 0; i < _timerKeys.Count; i++)
        {
            string key = _timerKeys[i];
            if (_timers[key] > 0f)
                _timers[key] = Mathf.Max(0f, _timers[key] - step);
        }

        if (_recentDamageRemain > 0f)
            _recentDamageRemain = Mathf.Max(0f, _recentDamageRemain - step);

        for (int i = 0; i < _backgroundTicks.Count; i++)
            _backgroundTicks[i]?.Invoke(step);

        ApplyNutritionSustenanceState();
    }

    /// <summary>注册必须跨状态持续推进的轻量计时器，例如攻击冷却。</summary>
    public void RegisterBackgroundTick(Action<float> tick)
    {
        if (tick == null || _backgroundTicks.Contains(tick))
            return;
        _backgroundTicks.Add(tick);
    }

    /// <summary>写入或刷新一个可存档计时器。</summary>
    public void SetTimer(string key, float seconds)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("AI 行为图计时器键不能为空。", nameof(key));

        if (!_timers.ContainsKey(key))
            _timerKeys.Add(key);
        _timers[key] = Mathf.Max(0f, seconds);
    }

    public bool IsTimerElapsed(string key)
    {
        return string.IsNullOrWhiteSpace(key) || !_timers.TryGetValue(key, out float remain) || remain <= 0f;
    }

    public Dictionary<string, float> ExportTimers()
    {
        return new Dictionary<string, float>(_timers, StringComparer.Ordinal);
    }

    /// <summary>按世界一天长度初始化并维护草食维持期。</summary>
    public void ConfigureNutritionSustenance(string timerKey, float days)
    {
        if (Food == null || string.IsNullOrWhiteSpace(timerKey) || days <= 0f)
            return;

        if (!string.IsNullOrWhiteSpace(_nutritionSustenanceTimerKey) &&
            !string.Equals(_nutritionSustenanceTimerKey, timerKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("同一 AI_BehaviorGraph 只能配置一套营养维持计时器。");
        }

        _nutritionSustenanceTimerKey = timerKey;
        if (!_timers.ContainsKey(timerKey))
            SetTimer(timerKey, GetCurrentDayLength() * days);
        ApplyNutritionSustenanceState();
    }

    /// <summary>完成一次草食摄取并恢复营养维持期。</summary>
    public void RestoreNutritionSustenance(string timerKey, float days)
    {
        Food?.RestoreNutritionToMaximum();
        SetTimer(timerKey, GetCurrentDayLength() * Mathf.Max(0.1f, days));
        ApplyNutritionSustenanceState();
    }

    /// <summary>读取当前世界昼夜比例。</summary>
    public bool IsNightTime(float dayStartRatio, float dayEndRatio)
    {
        DayTimeSystem timeSystem = DayTimeSystem.Instance;
        if (timeSystem == null ||
            !timeSystem.WorldTimeDict.TryGetValue(Actor.gameObject.scene.name, out TimeData timeData) ||
            timeData == null)
        {
            return false;
        }

        float dayLength = Mathf.Max(1f, timeData.DayLength);
        float normalized = Mathf.Repeat(timeSystem.GetCurrentTime(Actor.gameObject.scene.name), dayLength) / dayLength;
        return normalized < dayStartRatio || normalized > dayEndRatio;
    }

    /// <summary>优先返回近期受击来源位置，供逃离节点在攻击者脱离 Detector 后继续撤离。</summary>
    public bool TryGetRecentDamageOrigin(out Vector3 origin)
    {
        origin = _recentDamageOrigin;
        return _recentDamageRemain > 0f;
    }

    /// <summary>立即面向目标；攻击节点不修改移动目标。</summary>
    public void FaceTarget(Vector3 targetPosition, bool immediate)
    {
        if (_turnBody == null)
            return;

        Vector2 direction = WorldTopologyRuntime.ShortestDelta(Position, targetPosition);
        if (direction.sqrMagnitude < 0.0001f)
            return;
        direction.Normalize();

        if (immediate)
            _turnBody.ResetTurnState();
        _turnBody.TurnBodyToDirection(direction);
        if (immediate)
        {
            _turnBody.UpdateTurn(float.MaxValue);
            _turnBody.UpdateAllTransformDirections();
        }
    }

    public bool IsLivingAttackTarget(Item target)
    {
        if (target == null || target == Actor || target.DestructionHandled)
            return false;
        DamageReceiver receiver = target.itemMods?.GetMod_ByID<DamageReceiver>(ModText.Hp);
        return receiver != null && receiver.Hp > 0f && FactionRelationService.CanAttack(Actor, target);
    }

    /// <summary>解绑事件并恢复由行为图临时控制的营养倍率。</summary>
    public void Dispose()
    {
        if (_damageEventSource != null)
            _damageEventSource.OnDamageReceived -= HandleDamageReceived;
        _damageEventSource = null;
        if (Food != null && !string.IsNullOrWhiteSpace(_nutritionSustenanceTimerKey))
            Food.RuntimeNutritionConsumeMultiplier = 1f;
    }

    private void BindDamageEvents()
    {
        if (Health == null)
            return;
        _damageEventSource = Health;
        _damageEventSource.OnDamageReceived += HandleDamageReceived;
    }

    private void HandleDamageReceived(DamageReceiverDamageInfo info)
    {
        if (info == null || info.DamageValue <= 0f || info.Attacker == Actor)
            return;

        DamagedSinceStateEnter = true;
        if (_damageThreatMemoryDuration <= 0f || !DamageThreatOrigin.TryResolve(info, out Vector2 origin))
            return;

        _recentDamageOrigin = origin;
        _recentDamageRemain = _damageThreatMemoryDuration;
    }

    private void ApplyNutritionSustenanceState()
    {
        if (Food == null || string.IsNullOrWhiteSpace(_nutritionSustenanceTimerKey))
            return;
        Food.RuntimeNutritionConsumeMultiplier = IsTimerElapsed(_nutritionSustenanceTimerKey) ? 1f : 0f;
    }

    private float GetCurrentDayLength()
    {
        const float fallbackDayLength = 1440f;
        DayTimeSystem timeSystem = DayTimeSystem.Instance;
        if (timeSystem == null ||
            !timeSystem.WorldTimeDict.TryGetValue(Actor.gameObject.scene.name, out TimeData timeData) ||
            timeData == null)
        {
            return fallbackDayLength;
        }
        return Mathf.Max(1f, timeData.DayLength);
    }

    private static bool HasAnyTag(Item candidate, string[] tags)
    {
        List<string> candidateTags = candidate.itemData?.Tags;
        if (candidateTags == null || tags == null)
            return false;

        for (int tagIndex = 0; tagIndex < tags.Length; tagIndex++)
        {
            string expected = tags[tagIndex];
            if (string.IsNullOrWhiteSpace(expected))
                continue;
            for (int candidateIndex = 0; candidateIndex < candidateTags.Count; candidateIndex++)
            {
                if (string.Equals(candidateTags[candidateIndex], expected, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }
}

#endregion

#region JSON 行为图模块

/// <summary>将 JSON 行为图绑定到标准 AI Prefab 模块，并交给通用状态机执行。</summary>
public sealed class AI_BehaviorGraph : Module, IModuleJsonParameterValidator, IAIActor
{
    [Serializable]
    private sealed class SaveData
    {
        public string CurrentState; // 上次保存时的 JSON 状态键。
        public Dictionary<string, float> Timers = new(); // 节点共享的可存档计时器。
    }

    public Ex_ModData ModData = new(); // 保存当前 JSON 状态键。
    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = value as Ex_ModData;
    }

    [JsonProperty("behaviorGraph")]
    public AIBehaviorGraphDefinition BehaviorGraph { get; set; } // Actor JSON 声明的完整行为图。

    [JsonProperty("damageThreatMemoryDuration")]
    public float DamageThreatMemoryDuration { get; set; } = 5f; // 受击后保持逃离来源的时长。

    public UltEvent<string, string> OnStateChanged = new(); // 状态切换事件，供外部扩展监听。

    public Item ActorItem => item;
    public bool IsAlive => _context?.Health == null || _context.Health.Hp > 0f;

    private AIBehaviorGraphContext _context; // 本轮 Load 绑定的 Actor 能力。
    private AIBehaviorGraphRuntime _runtime; // 已编译的通用行为图。
    private bool _isLoaded; // 是否已建立运行图。

    public override ModuleTickMode TickMode => ModuleTickMode.EveryFrame;

    public override void Awake()
    {
        ModData ??= new Ex_ModData();
        if (string.IsNullOrWhiteSpace(ModData.ID))
            ModData.ID = "Module_AI_BehaviorGraph";
        base.Awake();
    }

    public override void Load()
    {
        ModData ??= new Ex_ModData();
        SaveData saved = ReadSaveData();
        BuildRuntime(saved?.CurrentState, saved?.Timers);
        _isLoaded = true;
    }

    public override void ModUpdate(float deltaTime)
    {
        _runtime?.Tick(deltaTime);
    }

    public override void Save()
    {
        ModData ??= new Ex_ModData();
        ModData.WriteData(new SaveData
        {
            CurrentState = _runtime?.CurrentState,
            Timers = _context?.ExportTimers() ?? new Dictionary<string, float>()
        });
    }

    public override void Unload()
    {
        _runtime?.Reset();
        _context?.Dispose();
        _runtime = null;
        _context = null;
        _isLoaded = false;
    }

    /// <summary>资源热重载时按新 JSON 重建节点，并保留仍然存在的当前状态。</summary>
    public override void OnResourcesReloaded()
    {
        if (!_isLoaded)
            return;

        string currentState = _runtime?.CurrentState;
        Dictionary<string, float> timers = _context?.ExportTimers();
        _runtime?.Reset();
        _context?.Dispose();
        BuildRuntime(currentState, timers);
    }

    /// <summary>Actor 目录预检时验证图结构与所有已注册节点参数。</summary>
    public void ValidateJsonParameters(JObject parameters)
    {
        JToken graphToken = parameters["behaviorGraph"];
        if (graphToken == null || graphToken.Type != JTokenType.Object)
            throw new InvalidOperationException("AI_BehaviorGraph 模块必须配置 behaviorGraph 对象。");

        AIBehaviorGraphDefinition graph = graphToken.ToObject<AIBehaviorGraphDefinition>();
        AIBehaviorGraphRegistry.Validate(graph);

        JToken damageMemoryToken = parameters["damageThreatMemoryDuration"];
        if (damageMemoryToken != null)
        {
            if (damageMemoryToken.Type != JTokenType.Float && damageMemoryToken.Type != JTokenType.Integer)
                throw new InvalidOperationException("AI_BehaviorGraph damageThreatMemoryDuration 必须是数值。");
            float value = damageMemoryToken.Value<float>();
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                throw new InvalidOperationException("AI_BehaviorGraph damageThreatMemoryDuration 必须是非负有限数值。");
        }
    }

    private void BuildRuntime(string initialState, IReadOnlyDictionary<string, float> savedTimers)
    {
        if (item == null || item.itemMods == null)
            throw new InvalidOperationException("AI_BehaviorGraph 尚未绑定所属 Item 模块表。");
        if (BehaviorGraph == null)
            throw new InvalidOperationException($"Actor {item.name} 的 AI JSON 没有 behaviorGraph。");

        Mover_AI mover = item.itemMods.GetMod_ByID<Mover_AI>(ModText.Mover_AI);
        if (mover == null)
            mover = item.itemMods.GetMod_ByID<Mover_AI>(ModText.Mover);
        Mod_ItemDetector detector = item.itemMods.GetMod_ByID<Mod_ItemDetector>(ModText.Detector);
        DamageReceiver health = item.itemMods.GetMod_ByID<DamageReceiver>(ModText.Hp);
        Mod_Food food = item.itemMods.GetMod_ByID<Mod_Food>(ModText.Food);
        Mod_AnimatorController animator = item.GetComponentInChildren<Mod_AnimatorController>(true);

        _context = new AIBehaviorGraphContext(
            item,
            mover,
            detector,
            health,
            food,
            animator,
            DamageThreatMemoryDuration,
            savedTimers);
        AIBehaviorGraphRequirements requirements = AIBehaviorGraphRegistry.Validate(BehaviorGraph);
        _context.RequireCapabilities(requirements.Capabilities);
        if (detector != null)
            detector.DetectionRadius = Mathf.Max(detector.DetectionRadius, requirements.DetectionRadius);

        _runtime = new AIBehaviorGraphRuntime(BehaviorGraph, _context, HandleStateChanged, initialState);
    }

    private SaveData ReadSaveData()
    {
        if (ModData?.BitData == null || ModData.BitData.Length == 0)
            return null;

        return ModData.GetData<SaveData>();
    }

    private void HandleStateChanged(string previousState, string nextState)
    {
        OnStateChanged?.Invoke(previousState, nextState);
    }
}

#endregion
