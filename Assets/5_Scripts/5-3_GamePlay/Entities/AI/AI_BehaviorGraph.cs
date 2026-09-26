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
    public Mod_AnimatorController Animator { get; } // 结构动画能力。
    public float StateElapsed { get; set; } // 当前 JSON 状态已持续时间。
    public Vector3 Position => Actor.transform.position;

    public AIBehaviorGraphContext(
        Item actor,
        Mover_AI mover,
        Mod_ItemDetector detector,
        DamageReceiver health,
        Mod_AnimatorController animator)
    {
        Actor = actor ?? throw new ArgumentNullException(nameof(actor));
        Mover = mover;
        Detector = detector;
        Health = health;
        Animator = animator;
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
                AIBehaviorCapability.Animator => Animator != null,
                _ => throw new InvalidOperationException($"AI 行为图引用了未注册的预置体能力：{capability}")
            };

            if (available)
                continue;

            string moduleName = capability switch
            {
                AIBehaviorCapability.Mover => ModText.Mover_AI,
                AIBehaviorCapability.Detector => ModText.Detector,
                AIBehaviorCapability.Health => ModText.Hp,
                AIBehaviorCapability.Animator => ModText.AnimatorReceiver,
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
    }

    public Ex_ModData ModData = new(); // 保存当前 JSON 状态键。
    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = value as Ex_ModData;
    }

    [JsonProperty("behaviorGraph")]
    public AIBehaviorGraphDefinition BehaviorGraph { get; set; } // Actor JSON 声明的完整行为图。

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
        BuildRuntime(ReadSavedState());
        _isLoaded = true;
    }

    public override void ModUpdate(float deltaTime)
    {
        _runtime?.Tick(deltaTime);
    }

    public override void Save()
    {
        ModData ??= new Ex_ModData();
        ModData.WriteData(new SaveData { CurrentState = _runtime?.CurrentState });
    }

    public override void Unload()
    {
        _runtime?.Reset();
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
        _runtime?.Reset();
        BuildRuntime(currentState);
    }

    /// <summary>Actor 目录预检时验证图结构与所有已注册节点参数。</summary>
    public void ValidateJsonParameters(JObject parameters)
    {
        JToken graphToken = parameters["behaviorGraph"];
        if (graphToken == null || graphToken.Type != JTokenType.Object)
            throw new InvalidOperationException("AI_BehaviorGraph 模块必须配置 behaviorGraph 对象。");

        AIBehaviorGraphDefinition graph = graphToken.ToObject<AIBehaviorGraphDefinition>();
        AIBehaviorGraphRegistry.Validate(graph);
    }

    private void BuildRuntime(string initialState)
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
        Mod_AnimatorController animator = item.GetComponentInChildren<Mod_AnimatorController>(true);

        _context = new AIBehaviorGraphContext(item, mover, detector, health, animator);
        AIBehaviorGraphRequirements requirements = AIBehaviorGraphRegistry.Validate(BehaviorGraph);
        _context.RequireCapabilities(requirements.Capabilities);
        if (detector != null)
            detector.DetectionRadius = Mathf.Max(detector.DetectionRadius, requirements.DetectionRadius);

        _runtime = new AIBehaviorGraphRuntime(BehaviorGraph, _context, HandleStateChanged, initialState);
    }

    private string ReadSavedState()
    {
        if (ModData?.BitData == null || ModData.BitData.Length == 0)
            return null;

        SaveData saved = ModData.GetData<SaveData>();
        return string.IsNullOrWhiteSpace(saved?.CurrentState) ? null : saved.CurrentState;
    }

    private void HandleStateChanged(string previousState, string nextState)
    {
        OnStateChanged?.Invoke(previousState, nextState);
    }
}

#endregion
