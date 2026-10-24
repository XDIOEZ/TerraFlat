using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class WeatherMgr
{
    #region 世界天气所有权
    private static readonly Dictionary<string, WeatherMgr> worldManagers = new(StringComparer.Ordinal);
    private static WeatherMgr templateManager;
    private static readonly List<CaptureLease> activeCaptures = new();
    private string ownedWorld;
    private bool presentationActive;
    public string WorldKey => ownedWorld;
    public bool IsLocalWeatherWorld => !string.IsNullOrEmpty(ownedWorld) &&
        string.Equals(ownedWorld, GameplayWorldRegistry.LocalPresentationWorldKey, StringComparison.Ordinal);
    public static WeatherMgr Instance => GetForWorld(GameplayWorldRegistry.ExecutionWorldKey);
    public static WeatherMgr GetInstance() => Instance;

    private void Awake()
    {
        if (templateManager == null) templateManager = this;
        // 预制体子节点跟随 WorldManager 生命周期，独立世界根节点才自行跨场景保留。
        if (transform.parent == null) DontDestroyOnLoad(gameObject);
    }

    public static WeatherMgr GetForWorld(string worldKey, bool create = true)
    {
        if (string.IsNullOrWhiteSpace(worldKey) || SpaceMgr.IsSpaceScene(worldKey)) return null;
        if (worldManagers.TryGetValue(worldKey, out WeatherMgr manager) && manager != null) return manager;
        if (!create || !Application.isPlaying || GameManager.Instance?.IsInGameWorld != true) return null;
        if (templateManager == null) templateManager = FindObjectOfType<WeatherMgr>(true);
        if (templateManager != null && string.IsNullOrEmpty(templateManager.ownedWorld)) manager = templateManager;
        else
        {
            manager = new GameObject("WeatherWorld_" + worldKey).AddComponent<WeatherMgr>();
            if (templateManager != null && manager != templateManager) manager.CopyConfiguration(templateManager);
        }
        manager.ownedWorld = worldKey;
        worldManagers[worldKey] = manager;
        foreach (CaptureLease capture in activeCaptures) capture.Add(manager);
        using (manager.EnterOwnedWorld()) manager.ApplyGameWorldLifecycleState(true);
        manager.SetPresentationActive(manager.IsLocalWeatherWorld && manager._weatherRuntimeAllowed);
        return manager;
    }

    // 各世界独立持有队列和随机时钟，只共享作者配置。
    private void CopyConfiguration(WeatherMgr source)
    {
        EnableDebugLog = source.EnableDebugLog;
        _cloudyAmbientLightMultiplier = source._cloudyAmbientLightMultiplier;
        _cloudySunLightMultiplier = source._cloudySunLightMultiplier;
        _weatherTransitionSeconds = source._weatherTransitionSeconds;
        _precipitationTransitionSeconds = source._precipitationTransitionSeconds;
        _deterministicSeedOverride = source._deterministicSeedOverride;
        surfaceRainWaterPerSecond = source.surfaceRainWaterPerSecond;
        meltWaterPerSnowDepth = source.meltWaterPerSnowDepth;
        lightningDamageRadius = source.lightningDamageRadius;
        lightningMaximumDamage = source.lightningMaximumDamage;
    }

    private Vector2 ObservationPosition()
    {
        if (IsLocalWeatherWorld && Camera.main != null)
            return WorldLocalPresentation.ToLogical((Vector2)Camera.main.transform.position);
        if (ItemMgr.Instance != null)
            foreach (Player player in ItemMgr.Instance.Player_DIC.Values)
            {
                if (player == null) continue;
                if (FlatWorld.Spaceflight.SpaceSession.Current?.TryGetNetworkObserver(player.ProfileName,
                    out FlatWorld.Spaceflight.SpaceNetworkObserver observer) == true && !observer.InSpace && observer.WorldKey == ownedWorld)
                    return observer.Position.LocalMeters.ToVector2();
                if (player.Data?.CurrentSceneName == ownedWorld) return player.Data.transform.position;
            }
        return Vector2.zero;
    }

    private IDisposable EnterOwnedWorld() => !string.IsNullOrEmpty(ownedWorld) &&
        GameplayWorldRegistry.TryGet(ownedWorld, out _) ? GameplayWorldRegistry.UseWorld(ownedWorld) : null;

    private void SetPresentationActive(bool active)
    {
        if (presentationActive == active) return;
        if (!active)
        {
            lightningEffect?.Shutdown();
            DeactivateWeatherFeedback();
            _debugPanelVisible = false;
        }
        presentationActive = active;
        if (!active) return;
        ActivateWeatherAudio();
        if (lightningEffect == null) lightningEffect = gameObject.AddComponent<LightningEffectController>();
        lightningEffect.Initialize(this);
        RefreshWeatherFeedback();
    }

    public static void SelectLocalPresentation(string worldKey)
    {
        foreach (WeatherMgr manager in worldManagers.Values)
            if (manager != null && !string.Equals(manager.ownedWorld, worldKey, StringComparison.Ordinal))
                manager.SetPresentationActive(false);
        if (worldManagers.TryGetValue(worldKey ?? string.Empty, out WeatherMgr local) && local != null)
            using (local.EnterOwnedWorld()) local.SetPresentationActive(local._weatherRuntimeAllowed);
    }

    public static void ReleaseAll()
    {
        var managers = new List<WeatherMgr>(worldManagers.Values);
        foreach (WeatherMgr manager in managers)
        {
            if (manager == null) continue;
            using (manager.EnterOwnedWorld()) manager.ShutdownWeatherEventSystem();
            manager._weatherRuntimeAllowed = false;
            manager.SetPresentationActive(false);
            manager.regionalEngine = null;
            manager.regionalPlanet = null;
            manager.surfaceWorkWorld = null;
            manager.surfaceWork.Dispose();
            manager.ownedWorld = null;
            if (manager != templateManager) Destroy(manager.gameObject);
        }
        worldManagers.Clear();
        LightningStrikeSystem.Reset();
    }
    #endregion

    #region 全世界保存屏障
    private sealed class CaptureLease : IDisposable
    {
        private List<IDisposable> leases = new();
        public CaptureLease()
        {
            activeCaptures.Add(this);
            foreach (WeatherMgr manager in worldManagers.Values) Add(manager);
        }
        public void Add(WeatherMgr manager)
        { if (manager != null) leases.Add(manager.BeginSurfaceSaveCapture()); }
        public void Dispose()
        {
            if (leases == null) return;
            foreach (IDisposable lease in leases) lease.Dispose();
            leases = null;
            activeCaptures.Remove(this);
        }
    }
    public static IDisposable BeginAllSurfaceSaveCapture() => new CaptureLease();
    public static void FlushAllSurfaceWork()
    {
        foreach (WeatherMgr manager in worldManagers.Values)
            if (manager != null)
                using (manager.EnterOwnedWorld()) { manager.FlushSurfaceWork(); manager.PersistRegionalStates(); }
    }
    public static IEnumerator FlushAllSurfaceWorkForAutoSave(AutoSaveWorkBudget budget)
    {
        foreach (WeatherMgr manager in new List<WeatherMgr>(worldManagers.Values))
        {
            if (manager == null) continue;
            IEnumerator routine = manager.FlushSurfaceWorkForAutoSave(budget);
            try
            {
                while (true)
                {
                    bool next;
                    object current;
                    using (manager.EnterOwnedWorld()) { next = routine.MoveNext(); current = next ? routine.Current : null; }
                    if (!next) break;
                    yield return current;
                }
                using (manager.EnterOwnedWorld()) manager.PersistRegionalStates();
            }
            finally { using (manager.EnterOwnedWorld()) (routine as IDisposable)?.Dispose(); }
        }
    }
    #endregion
}
