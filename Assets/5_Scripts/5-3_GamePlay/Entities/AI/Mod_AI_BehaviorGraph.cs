using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UltEvents;

#region Actor 能力上下文

/// <summary>状态图节点读取的 Actor 能力入口；运行态由 Blackboard 保存，物种参数由 Actor JSON 提供。</summary>
public sealed class AIBehaviorGraphContext
{
    public Item Actor { get; } // 当前行为图所属实体。
    public Mod_Mover_AI Mod_Mover { get; } // 通用移动能力。
    public Mod_ItemDetector Detector { get; } // 通用感知能力。
    public Mod_DamageReceiver Health { get; } // 通用生命能力。
    public Mod_Food Food { get; } // 通用营养能力。
    public Mod_AnimatorController Animator { get; } // 结构动画能力。
    public float StateElapsed { get => _blackboard.StateElapsed; set => _blackboard.StateElapsed = value; } // 当前状态时长。
    public Vector3 Position => Actor.transform.position;
    public bool DamagedSinceStateEnter => _blackboard.DamagedSinceStateEnter; // 当前状态受击事实。
    public bool HasRecentDamageThreat => _blackboard.HasRecentDamageThreat; // 近期受击来源是否有效。
    public bool ForageSatisfied { get => _blackboard.ForageSatisfied; internal set => _blackboard.ForageSatisfied = value; }
    public bool ForageAvailable { get => _blackboard.ForageAvailable; internal set => _blackboard.ForageAvailable = value; }
    public float RecentDamageRemaining => _blackboard.RecentDamageRemaining; // 可存档的受击记忆时长。
    public Vector3 RecentDamageOrigin => _blackboard.RecentDamageOrigin; // 可存档的受击来源。
    public event Action Damaged; // 通知当前节点重规划并提前评估状态。

    private readonly AIBehaviorGraphBlackboard _blackboard; // 可存档事实和计时器。
    private readonly AIBehaviorGraphPerception _perception; // 目标感知与刷新。
    private readonly List<Action<float>> _backgroundTicks = new(); // 节点注册的跨状态后台计时器。
    private readonly float _damageThreatMemoryDuration; // 受击来源记忆时长。
    private readonly bool _hasDamageModules; // 是否具备攻击伤害模块。
    private Mod_DamageReceiver _damageEventSource; // 当前受击事件来源。
    private string _nutritionSustenanceTimerKey; // 草食维持期使用的计时器键。
    private DayTimeSystem _worldTimeSource; // 草食计时使用的权威世界时间源。
    private Mod_TurnBack _turnBody; // 通用朝向模块。

    public AIBehaviorGraphContext(
        Item actor,
        Mod_Mover_AI mover,
        Mod_ItemDetector detector,
        Mod_DamageReceiver health,
        Mod_Food food,
        Mod_AnimatorController animator,
        float damageThreatMemoryDuration,
        float detectorRefreshInterval,
        IReadOnlyDictionary<string, float> savedTimers,
        float savedStateElapsed,
        float savedDamageRemaining,
        Vector3 savedDamageOrigin)
    {
        Actor = actor ?? throw new ArgumentNullException(nameof(actor));
        Mod_Mover = mover;
        Detector = detector;
        Health = health;
        Food = food;
        Animator = animator;
        _blackboard = new AIBehaviorGraphBlackboard(
            savedTimers, savedStateElapsed, savedDamageRemaining, savedDamageOrigin);
        _perception = new AIBehaviorGraphPerception(actor, detector, detectorRefreshInterval);
        _damageThreatMemoryDuration = Mathf.Max(0f, damageThreatMemoryDuration);
        _hasDamageModules = actor.GetComponentsInChildren<Mod_Damage>(true).Length > 0;
        _turnBody = actor.GetComponentInChildren<Mod_TurnBack>(true);
        BindDamageEvents();
    }

    /// <summary>按能力清单验证 AI Prefab，缺少节点依赖时在加载阶段明确报错。</summary>
    public void RequireCapabilities(IReadOnlyCollection<string> capabilities)
    {
        foreach (string capability in capabilities)
        {
            bool available = capability switch
            {
                AIBehaviorCapability.Mod_Mover => Mod_Mover != null,
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
                AIBehaviorCapability.Mod_Mover => ModText.Mod_Mover_AI,
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
        return _perception.FindClosestThreat(distance, tags, includePlayers);
    }

    /// <summary>按标签和有效感知距离查找最近物品目标。</summary>
    public Item FindClosestTaggedItem(float distance, string[] tags)
    {
        return _perception.FindClosestTaggedFood(distance, tags);
    }

    /// <summary>在威胁筛选后按阵营和生命值选择可攻击目标，避免友方遮挡更远的敌人。</summary>
    public Item FindClosestAttackTarget(float distance, string[] tags, bool includePlayers)
    {
        return _perception.FindClosestAttackTarget(distance, tags, includePlayers);
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
        _blackboard.BeginState();
    }

    /// <summary>推进行为图共享计时器和受击记忆。</summary>
    public void Tick(float deltaTime)
    {
        float step = Mathf.Max(0f, deltaTime);
        _blackboard.Tick(step, _nutritionSustenanceTimerKey);
        _perception.Tick(step);
        EnsureWorldTimeBinding();

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
        _blackboard.SetTimer(key, seconds);
    }

    public bool IsTimerElapsed(string key)
    {
        return _blackboard.IsTimerElapsed(key);
    }

    public float GetTimerRemaining(string key)
    {
        return _blackboard.GetTimerRemaining(key);
    }

    public Dictionary<string, float> ExportTimers()
    {
        return _blackboard.ExportTimers();
    }

    /// <summary>按世界一天长度初始化并维护草食维持期。</summary>
    public void ConfigureNutritionSustenance(string timerKey, float days)
    {
        if (Food == null || string.IsNullOrWhiteSpace(timerKey) || days <= 0f)
            return;

        if (!string.IsNullOrWhiteSpace(_nutritionSustenanceTimerKey) &&
            !string.Equals(_nutritionSustenanceTimerKey, timerKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("同一 Mod_AI_BehaviorGraph 只能配置一套营养维持计时器。");
        }

        _nutritionSustenanceTimerKey = timerKey;
        if (!_blackboard.HasTimer(timerKey))
            SetTimer(timerKey, GetCurrentDayLength() * days);
        EnsureWorldTimeBinding();
        ApplyNutritionSustenanceState();
    }

    /// <summary>完成一次草食摄取并恢复营养维持期。</summary>
    public void RestoreNutritionSustenance(string timerKey, float days)
    {
        Food.RestoreNutritionToMaximum();
        SetTimer(timerKey, GetCurrentDayLength() * days);
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
        return _blackboard.TryGetRecentDamageOrigin(out origin);
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
        return _perception.IsLivingAttackTarget(target);
    }

    /// <summary>解绑事件并恢复由行为图临时控制的营养倍率。</summary>
    public void Dispose()
    {
        if (_damageEventSource != null)
            _damageEventSource.OnDamageReceived -= HandleDamageReceived;
        _damageEventSource = null;
        if (_worldTimeSource != null)
            _worldTimeSource.TimeAdvanced -= HandleWorldTimeAdvanced;
        _worldTimeSource = null;
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

        bool hasOrigin = DamageThreatOrigin.TryResolve(info, out Vector2 origin);
        _blackboard.RecordDamage(_damageThreatMemoryDuration, hasOrigin, origin);
        Damaged?.Invoke();
    }

    /// <summary>时间系统可能晚于 Actor 加载，按当前场景和系统实例保持唯一订阅。</summary>
    private void EnsureWorldTimeBinding()
    {
        if (string.IsNullOrWhiteSpace(_nutritionSustenanceTimerKey))
            return;
        DayTimeSystem current = DayTimeSystem.Instance;
        if (_worldTimeSource == current)
            return;
        if (_worldTimeSource != null)
            _worldTimeSource.TimeAdvanced -= HandleWorldTimeAdvanced;
        _worldTimeSource = current;
        if (_worldTimeSource != null)
            _worldTimeSource.TimeAdvanced += HandleWorldTimeAdvanced;
    }

    /// <summary>草食维持期按世界权威推进量计时，支持加速和跳时。</summary>
    private void HandleWorldTimeAdvanced(string sceneName, float oldTotalTime, float newTotalTime)
    {
        if (!string.Equals(sceneName, Actor.gameObject.scene.name, StringComparison.Ordinal))
            return;
        _blackboard.TickWorldTimer(_nutritionSustenanceTimerKey, newTotalTime - oldTotalTime);
        ApplyNutritionSustenanceState();
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

}

#endregion

#region JSON 行为图模块

/// <summary>将 JSON 行为图绑定到标准 AI Prefab 模块，并交给通用状态机执行。</summary>
public sealed class Mod_AI_BehaviorGraph : Module, IModuleJsonParameterValidator, IAIActor
{
    [Serializable]
    private sealed class SaveData
    {
        public string CurrentState; // 上次保存时的 JSON 状态键。
        public float StateElapsed; // 当前状态已执行秒数。
        public Dictionary<string, float> Timers = new(); // 节点共享的可存档计时器。
        public float RecentDamageRemaining; // 受击来源记忆时长。
        public float RecentDamageOriginX; // 受击来源的 X 坐标。
        public float RecentDamageOriginY; // 受击来源的 Y 坐标。
        public float RecentDamageOriginZ; // 受击来源的 Z 坐标。
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

    [JsonProperty("detectorRefreshInterval")]
    public float DetectorRefreshInterval { get; set; } = 0.8f; // 感知快照刷新间隔。

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
        BuildRuntime(saved);
        _isLoaded = true;
    }

    public override void ModUpdate(float deltaTime)
    {
        _runtime?.Tick(deltaTime);
    }

    public override void Save()
    {
        ModData ??= new Ex_ModData();
        ModData.WriteData(CaptureSaveData());
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

        SaveData snapshot = CaptureSaveData();
        _runtime?.Reset();
        _context?.Dispose();
        BuildRuntime(snapshot);
    }

    /// <summary>Actor 目录预检时验证图结构与所有已注册节点参数。</summary>
    public void ValidateJsonParameters(JObject parameters)
    {
        JToken graphToken = parameters["behaviorGraph"];
        if (graphToken == null || graphToken.Type != JTokenType.Object)
            throw new InvalidOperationException("Mod_AI_BehaviorGraph 模块必须配置 behaviorGraph 对象。");

        AIBehaviorGraphDefinition graph = graphToken.ToObject<AIBehaviorGraphDefinition>();
        AIBehaviorGraphRegistry.Validate(graph);

        JToken damageMemoryToken = parameters["damageThreatMemoryDuration"];
        if (damageMemoryToken != null)
        {
            if (damageMemoryToken.Type != JTokenType.Float && damageMemoryToken.Type != JTokenType.Integer)
                throw new InvalidOperationException("Mod_AI_BehaviorGraph damageThreatMemoryDuration 必须是数值。");
            float value = damageMemoryToken.Value<float>();
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                throw new InvalidOperationException("Mod_AI_BehaviorGraph damageThreatMemoryDuration 必须是非负有限数值。");
        }
        JToken refreshToken = parameters["detectorRefreshInterval"];
        if (refreshToken != null)
        {
            if (refreshToken.Type != JTokenType.Float && refreshToken.Type != JTokenType.Integer)
                throw new InvalidOperationException("Mod_AI_BehaviorGraph detectorRefreshInterval 必须是数值。");
            float value = refreshToken.Value<float>();
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
                throw new InvalidOperationException("Mod_AI_BehaviorGraph detectorRefreshInterval 必须是正有限数值。");
        }
    }

    private void BuildRuntime(SaveData saved)
    {
        if (item == null || item.itemMods == null)
            throw new InvalidOperationException("Mod_AI_BehaviorGraph 尚未绑定所属 Item 模块表。");
        if (BehaviorGraph == null)
            throw new InvalidOperationException($"Actor {item.name} 的 AI JSON 没有 behaviorGraph。");

        Mod_Mover_AI mover = item.itemMods.GetMod_ByID<Mod_Mover_AI>(ModText.Mod_Mover_AI);
        if (mover == null)
            mover = item.itemMods.GetMod_ByID<Mod_Mover_AI>(ModText.Mod_Mover);
        Mod_ItemDetector detector = item.itemMods.GetMod_ByID<Mod_ItemDetector>(ModText.Detector);
        Mod_DamageReceiver health = item.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
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
            DetectorRefreshInterval,
            saved?.Timers,
            saved?.StateElapsed ?? 0f,
            saved?.RecentDamageRemaining ?? 0f,
            saved == null ? default : new Vector3(
                saved.RecentDamageOriginX, saved.RecentDamageOriginY, saved.RecentDamageOriginZ));
        try
        {
            AIBehaviorGraphRequirements requirements = AIBehaviorGraphRegistry.Validate(BehaviorGraph);
            _context.RequireCapabilities(requirements.Capabilities);
            if (detector != null)
                detector.DetectionRadius = Mathf.Max(detector.DetectionRadius, requirements.DetectionRadius);

            _runtime = new AIBehaviorGraphRuntime(
                BehaviorGraph, _context, HandleStateChanged, saved?.CurrentState, saved?.StateElapsed ?? 0f);
        }
        catch
        {
            _context.Dispose();
            _context = null;
            throw;
        }
    }

    private SaveData ReadSaveData()
    {
        if (ModData?.BitData == null || ModData.BitData.Length == 0)
            return null;

        return ModData.GetData<SaveData>();
    }

    /// <summary>保存与资源热重载共用同一份行为图运行态快照。</summary>
    private SaveData CaptureSaveData()
    {
        Vector3 damageOrigin = _context?.RecentDamageOrigin ?? default;
        return new SaveData
        {
            CurrentState = _runtime?.CurrentState,
            StateElapsed = _context?.StateElapsed ?? 0f,
            Timers = _context?.ExportTimers() ?? new Dictionary<string, float>(),
            RecentDamageRemaining = _context?.RecentDamageRemaining ?? 0f,
            RecentDamageOriginX = damageOrigin.x,
            RecentDamageOriginY = damageOrigin.y,
            RecentDamageOriginZ = damageOrigin.z
        };
    }

    private void HandleStateChanged(string previousState, string nextState)
    {
        OnStateChanged?.Invoke(previousState, nextState);
    }
}

#endregion
