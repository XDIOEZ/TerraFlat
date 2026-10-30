using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using FlatWorld.Localization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UltEvents;

/// <summary>所有生物共用的行为树宿主，负责能力绑定、运行、死亡中断和存档。</summary>
public sealed class Mod_AnimalBehaviorTree : Module, IModuleJsonParameterValidator, IAIActor,
    IAnimalAmbientAudioActivity, IAIAdvanceCommandReceiver, IAIAdvanceCommandCancellationReceiver, IAnimalAttackRangeSource,
    ITemperatureSafetyMovement
{
    #region 配置与运行态
    [Serializable]
    private sealed class SaveData
    {
        public Dictionary<string, float> Timers;
        public JObject Capabilities;
        public float RecentDamageRemaining;
        public float RecentDamageOriginX, RecentDamageOriginY, RecentDamageOriginZ;
    }

    public const string ModuleId = "AI";
    public Ex_ModData ModData = new();
    public override ModuleData _Data { get => ModData; set => ModData = (Ex_ModData)value; }
    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.EveryFrame;
    [JsonProperty("behaviorTree")] public AnimalBehaviorTreeDefinition BehaviorTree { get; set; }
    [JsonProperty("damageThreatMemoryDuration")] public float DamageThreatMemoryDuration { get; set; } = 5f;
    [JsonProperty("detectorRefreshInterval")] public float DetectorRefreshInterval { get; set; } = .8f;
    public UltEvent<string, string> OnStateChanged = new();
    public AnimalBehaviorTreeContext Context { get; private set; }
    public Item ActorItem => item;
    public bool IsAlive => Context?.Health == null || Context.Health.Hp > 0f;
    public string CurrentAction => Context?.CurrentAction;
    public bool HasAdvanceCommand => Context != null && Context.TryGetCapability("advance", out AnimalAdvanceCapability ability) && ability.HasCommand;
    public float AttackRange => Context != null && Context.TryGetCapability("combat", out AnimalCombatCapability combat) ? combat.AttackRange : 0f;
    public bool AllowsAmbientAudio => IsAlive && IsCalmAction(CurrentAction);
    private AnimalBehaviorTreeRuntime runtime;
    private readonly List<Mod_Damage> bodyDamageSources = new();
    private bool stoppedForDeath;
    private ITemperatureSafetyMovement temperatureMovement;
    private Vector2 temperatureDestination;
    private readonly GUIContent debugContent = new();
    private string debugAction;
    private string debugLocale;
    private static GUIStyle debugStyle;
    public bool HasTemperatureSafetyRequest { get; private set; }
    public int TemperatureSafetyMovementPriority => 10000;
    public bool ShouldAdvanceTemperatureSafetyDestination => temperatureMovement?.ShouldAdvanceTemperatureSafetyDestination ?? true;
    #endregion

    #region 生命周期与能力绑定
    protected override void OnLoad() => BuildRuntime(ReadSave());

    private void BuildRuntime(SaveData saved)
    {
        AnimalBehaviorTreeTemplate template = AnimalBehaviorTreeNodeCatalog.Compile(BehaviorTree);
        Mod_Mover_AI mover = item.itemMods.GetMod_ByID<Mod_Mover_AI>(ModText.Mod_Mover_AI)
            ?? item.itemMods.GetMod_ByID<Mod_Mover_AI>(ModText.Mod_Mover);
        Context = new AnimalBehaviorTreeContext(item, mover,
            item.itemMods.GetMod_ByID<Mod_ItemDetector>(ModText.Detector),
            item.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp),
            item.itemMods.GetMod_ByID<Mod_Food>(ModText.Food),
            item.itemMods.GetMod_ByID<Mod_AnimatorController>(ModText.AnimatorReceiver),
            DamageThreatMemoryDuration, DetectorRefreshInterval,
            saved?.Timers, 0f, saved?.RecentDamageRemaining ?? 0f,
            saved == null ? default : new Vector3(saved.RecentDamageOriginX, saved.RecentDamageOriginY, saved.RecentDamageOriginZ));
        Context.ConfigureCapabilities(template.Capabilities, saved?.Capabilities);
        try
        {
            runtime = template.CreateRuntime(Context);
            runtime.ActionChanged += HandleActionChanged;
            bodyDamageSources.Clear();
            item.GetComponentsInChildren(true, bodyDamageSources);
            ResolveTemperatureMovement();
            stoppedForDeath = false;
            if (Context.Health != null) Context.Health.DeathStarted += HandleDeathStarted;
            bool detailedOverlay = false;
            foreach (Module module in item.itemMods.Mods.Values)
                if (module is IActorDebugOverlayOwner) { detailedOverlay = true; break; }
            if (!detailedOverlay) AI_DebugOverlay.Register(this, DrawTreeDebugOverlay);
        }
        catch { Context.Dispose(); Context = null; throw; }
    }

    public override void ModUpdate(float deltaTime)
    {
        if (runtime == null) return;
        if (!GameNetwork.HasStateAuthority) return;
        if (!IsAlive)
        {
            HandleDeathStarted(Context.Health);
            return;
        }
        Context.Tick(deltaTime);
        if (!IsAlive) { HandleDeathStarted(Context.Health); return; }
        runtime.Tick(deltaTime);
    }

    protected override void OnSave() => ModData.WriteData(CaptureSave());
    protected override void OnUnload()
    {
        AI_DebugOverlay.Unregister(this);
        if (Context?.Health != null) Context.Health.DeathStarted -= HandleDeathStarted;
        runtime?.Abort();
        ClearTemperatureSafetyDestination();
        Context?.Dispose();
        runtime = null;
        Context = null;
        bodyDamageSources.Clear();
    }

    public override void OnResourcesReloaded()
    {
        if (Context == null) return;
        SaveData snapshot = CaptureSave();
        OnUnload();
        BuildRuntime(snapshot);
    }

    public void ValidateJsonParameters(JObject parameters)
    {
        if (parameters?["behaviorTree"] is not JObject tree)
            throw new InvalidOperationException("生物 AI 需要 behaviorTree。");
        AnimalBehaviorTreeNodeCatalog.Compile(tree.ToObject<AnimalBehaviorTreeDefinition>());
    }

    private void HandleActionChanged(string previous, string next)
    {
        if (next == null) Context.EndAction();
        else Context.BeginAction(next);
        OnStateChanged?.Invoke(previous, next);
    }

    private void HandleDeathStarted(Mod_DamageReceiver health)
    {
        if (stoppedForDeath) return;
        stoppedForDeath = true;
        runtime?.Abort();
        ClearTemperatureSafetyDestination();
        Context?.Mod_Mover?.StopMovement();
        Context?.StopAllCombatCapabilities();
        for (int i = 0; i < bodyDamageSources.Count; i++)
            if (bodyDamageSources[i] != null && bodyDamageSources[i].item == item) bodyDamageSources[i].SetDamageEnabled(false);
    }
    #endregion

    #region 推进指令与持久状态
    public void BeginAdvance(AIAdvanceCommand command)
    {
        if (Context != null && Context.TryGetCapability("advance", out AnimalAdvanceCapability ability)) ability.BeginAdvance(command);
    }
    public void CancelAdvance()
    {
        if (Context != null && Context.TryGetCapability("advance", out AnimalAdvanceCapability ability)) ability.CancelAdvance();
    }
    private SaveData ReadSave()
        => ModData?.BitData?.Length > 0 ? ModData.GetData<SaveData>() : null;
    private SaveData CaptureSave() => new()
    {
        Timers = Context?.ExportTimers(), Capabilities = Context?.CaptureCapabilityData(),
        RecentDamageRemaining = Context?.RecentDamageRemaining ?? 0f,
        RecentDamageOriginX = Context?.RecentDamageOrigin.x ?? 0f,
        RecentDamageOriginY = Context?.RecentDamageOrigin.y ?? 0f,
        RecentDamageOriginZ = Context?.RecentDamageOrigin.z ?? 0f
    };
    #endregion

    #region 温度事实与树动作仲裁
    private void ResolveTemperatureMovement()
    {
        temperatureMovement = null;
        foreach (Module module in item.itemMods.Mods.Values)
            if (module != this && module is ITemperatureSafetyMovement candidate &&
                (temperatureMovement == null || candidate.TemperatureSafetyMovementPriority > temperatureMovement.TemperatureSafetyMovementPriority))
                temperatureMovement = candidate;
    }
    public void SetTemperatureSafetyDestination(Vector2 destination)
    {
        temperatureDestination = destination;
        HasTemperatureSafetyRequest = true;
    }
    public void ClearTemperatureSafetyDestination()
    {
        HasTemperatureSafetyRequest = false;
        temperatureMovement?.ClearTemperatureSafetyDestination();
    }
    internal void TickTemperatureSafetyMovement(float deltaTime)
    {
        temperatureMovement?.SetTemperatureSafetyDestination(temperatureDestination);
        if (temperatureMovement is IAnimalTemperatureSafetyExecutor executor) executor.TickTemperatureSafetyMovement(deltaTime);
    }
    internal void StopTemperatureSafetyMovement() => temperatureMovement?.ClearTemperatureSafetyDestination();

    private static bool IsCalmAction(string action)
        => action != null && (action == "idle" || action == "wander" || action == "forage" || action == "eat" ||
            action == "land.wander" || action == "land.forage" || action == "flight.wanderGround" ||
            action == "flight.wanderAir" || action == "flight.forage" || action == "aquatic.wander" ||
            action == "aquatic.forage" || action == "colony.patrol" || action == "colony.forage" || action == "colony.independentWander");
    #endregion

    #region 共用行为树调试显示
    private void DrawTreeDebugOverlay(Camera camera)
    {
        if (Context == null || !IsAlive) return;
        Vector3 point = camera.WorldToViewportPoint(item.transform.position + Vector3.up * .9f);
        if (point.z <= 0f || point.x < 0f || point.x > 1f || point.y < 0f || point.y > 1f) return;
        string action = CurrentAction ?? "idle";
        if (debugAction != action || debugLocale != AI_DebugOverlay.TextLocale)
        {
            debugAction = action;
            debugLocale = AI_DebugOverlay.TextLocale;
            string text = action switch
            {
                "idle" => "待机", "wander" => "闲逛", "flee" => "逃跑", "avoid" => "避让", "sleep" => "睡眠",
                "forage" => "觅食", "eat" => "进食", "chase" => "追击", "attack" => "攻击", "skill" => "技能",
                "search" => "搜索", "alert" => "警戒", "advance" => "推进", "temperature" => "温度避险",
                "produce" => "产物", "mate" => "交配", _ => action
            };
            debugContent.text = FlatWorldLocalizationService.GetUiText(text);
        }
        debugStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 12 };
        GUI.Label(new Rect(point.x * Screen.width - 90f, (1f - point.y) * Screen.height - 12f, 180f, 24f), debugContent, debugStyle);
    }
    #endregion
}
