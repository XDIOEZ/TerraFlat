using FlatWorld.Audio;
using UnityEngine;

/// <summary>动物环境短音表现；发声配置来自 Actor，声音跟随真实动物并交给空间音频服务衰减。</summary>
[DisallowMultipleComponent]
public sealed class Mod_AnimalAmbientAudio : Module, IItemModuleDependencyBinder,
    ISimulationRangeAware, IItemPoolLifecycle
{
    #region 配置与运行态
    public const string ModuleId = "Mod_AnimalAmbientAudio";
    [SerializeField] private Ex_ModData data = new();
    public string cueId;
    [Min(0.25f)] public float intervalMinSeconds = 8f;
    [Min(0.25f)] public float intervalMaxSeconds = 18f;
    [Min(0.25f)] public float initialDelayMinSeconds = 1f;
    [Min(0.25f)] public float initialDelayMaxSeconds = 18f;
    [Min(0.01f)] public float volumeScale = 1f;

    private readonly AnimalAmbientAudioSchedule schedule = new();
    private IAIActor actor;
    private IAnimalAmbientAudioActivity activity;
    private Mod_DamageReceiver health;
    private Mod_DamageReceiver deathSource;
    private AudioHandle ownedVoice;
    private bool simulationPaused;

    public override ModuleData _Data
    {
        get => data;
        set => data = value as Ex_ModData ?? new Ex_ModData();
    }
    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.FixedInterval;
    public override float FixedTickInterval => 0.25f;
    public string CueId => cueId;
    public float SecondsUntilNextAttempt => schedule.RemainingSeconds;
    public bool HasOwnedVoice => ownedVoice.IsPlaying;
    public bool IsAmbientAudioEligible => IsRuntimeLoaded && Enabled && isActiveAndEnabled &&
        !simulationPaused && item != null && item.isActiveAndEnabled && item.IsInitialized &&
        !item.DestructionHandled && !item.IsInPool && health != null && health.Hp > 0f &&
        actor?.IsAlive == true && (activity == null || activity.AllowsAmbientAudio);
    #endregion

    #region 装配与生命周期
    public override void Awake()
    {
        data ??= new Ex_ModData();
        data.ID = ModuleId;
        base.Awake();
    }

    public void BindModuleDependencies(ItemMods modules)
    {
        actor = modules.RequireSingleCapability<IAIActor>();
        activity = actor as IAnimalAmbientAudioActivity;
        health = modules.RequireSingleModById<Mod_DamageReceiver>(ModText.Hp);
    }

    protected override void OnLoad()
    {
        StopOwnedVoice();
        UnbindDeath();
        BindModuleDependencies(item.itemMods);
        deathSource = health;
        deathSource.DeathStarted += HandleDeath;
        simulationPaused = false;
        ResetSchedule();
    }

    protected override void OnSave() { }

    protected override void OnUnload()
    {
        ReleaseRuntime();
        actor = null;
        activity = null;
        health = null;
    }

    public override void OnResourcesReloaded()
    {
        if (!IsRuntimeLoaded) return;
        StopOwnedVoice();
        ResetSchedule();
    }

    private void OnDisable()
    {
        StopOwnedVoice();
        schedule.Clear();
    }

    private void OnEnable()
    {
        if (IsRuntimeLoaded) ResetSchedule();
    }

    private void OnDestroy() => ReleaseRuntime();
    public void OnItemTakenFromPool() => ReleaseRuntime();
    public void OnItemReturnedToPool() => ReleaseRuntime();

    public void OnSimulationRangePaused()
    {
        simulationPaused = true;
        StopOwnedVoice();
        schedule.Clear();
    }

    public void OnSimulationRangeResumed()
    {
        simulationPaused = false;
        ResetSchedule();
    }
    #endregion

    #region 发声与清理
    public override void ModUpdate(float deltaTime)
    {
        if (!isActiveAndEnabled || !IsRuntimeLoaded) return;
        bool eligible = IsAmbientAudioEligible;
        if (!eligible) StopOwnedVoice();
        if (!schedule.Advance(deltaTime, eligible, ownedVoice.IsPlaying) ||
            string.IsNullOrWhiteSpace(cueId)) return;

        AudioPlayOptions options = AudioPlayOptions.Attached(item.transform, volumeScale);
        options.OverrideLoop = true;
        options.Loop = false;
        ownedVoice = AudioService.Instance.Play(cueId, options);
    }

    private void ResetSchedule()
    {
        uint seed = unchecked((uint)(item?.itemData?.Guid ?? GetInstanceID()));
        schedule.Reset(seed ^ RuntimeGeneration, intervalMinSeconds, intervalMaxSeconds,
            initialDelayMinSeconds, initialDelayMaxSeconds);
    }

    private void HandleDeath(Mod_DamageReceiver source)
    {
        StopOwnedVoice();
        schedule.Clear();
    }

    private void StopOwnedVoice()
    {
        ownedVoice.Stop();
        ownedVoice = AudioHandle.Invalid;
    }

    private void UnbindDeath()
    {
        if (deathSource != null) deathSource.DeathStarted -= HandleDeath;
        deathSource = null;
    }

    private void ReleaseRuntime()
    {
        StopOwnedVoice();
        schedule.Clear();
        UnbindDeath();
    }
    #endregion
}
