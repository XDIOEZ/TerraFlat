using System;
using System.Collections.Generic;
using FlatWorld.AIECS;
using FlatWorld.WorldModel;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;

/// <summary>每种能力运行器只登记一次，实体数量不增加托管调度对象。</summary>
public interface IWorldEntityRuntimeModule
{
    string RuntimeModuleId { get; }
    void TickEntities(float deltaTime);
    void CompleteEntityJobs();
    void ReleaseEntities();
}

/// <summary>统一 Job 完成后提交少量存档、库存与表现副作用，不在并行任务里调用 Unity 接口。</summary>
public interface IWorldEntityPostSimulationModule
{
    void AfterEntitySimulation(float deltaTime);
}

/// <summary>正式世界实体的唯一 World 所有者；AI 是能力运行器，不再拥有另一套实体世界。</summary>
public static class WorldEntityRuntime
{
    #region 世界与能力所有权

    private sealed class RuntimeState
    {
        public World World;
        public WorldRuntime Terrain;
        public long TerrainEpoch;
        public GameSaveData Save;
        public string Dimension;
        public bool Releasing, Suspended;
        public ulong Generation, CapabilityVersion;
        public readonly List<IWorldEntityRuntimeModule> Modules = new();
        public readonly HashSet<IWorldEntityRuntimeModule> Failed = new();
        public IWorldEntityRuntimeModule[] Dispatch = Array.Empty<IWorldEntityRuntimeModule>();
        public EntityCapabilitySystem Capabilities;
        public EntityPlantModuleSystem Plants;
        public EntitySeasonPeriod[] Seasons;
        public float CapabilitySeconds;
    }

    private static readonly Dictionary<string, RuntimeState> worlds = new(StringComparer.Ordinal);
    private static readonly List<string> tickWorlds = new();
    private static ulong nextGeneration;
    private static RuntimeState releaseOverride;
    private static string cachedWorldKey;
    private static RuntimeState cachedState;
    private static RuntimeState State
    {
        get
        {
            if (releaseOverride != null) return releaseOverride;
            string key = GameplayWorldRegistry.ExecutionWorldKey ?? string.Empty;
            if (cachedState != null && string.Equals(cachedWorldKey, key, StringComparison.Ordinal)) return cachedState;
            if (!worlds.TryGetValue(key, out RuntimeState state))
                worlds[key] = state = new RuntimeState { Generation = ++nextGeneration };
            cachedWorldKey = key; return cachedState = state;
        }
    }
    private static World world { get => State.World; set => State.World = value; }
    private static WorldRuntime terrainWorld { get => State.Terrain; set => State.Terrain = value; }
    private static long terrainEpoch { get => State.TerrainEpoch; set => State.TerrainEpoch = value; }
    private static GameSaveData save { get => State.Save; set => State.Save = value; }
    private static string dimensionId { get => State.Dimension; set => State.Dimension = value; }
    private static bool releasing { get => State.Releasing; set => State.Releasing = value; }
    private static List<IWorldEntityRuntimeModule> modules => State.Modules;
    private static HashSet<IWorldEntityRuntimeModule> failed => State.Failed;
    private static IWorldEntityRuntimeModule[] dispatch { get => State.Dispatch; set => State.Dispatch = value; }
    private static EntityCapabilitySystem capabilities { get => State.Capabilities; set => State.Capabilities = value; }
    private static EntityPlantModuleSystem plants { get => State.Plants; set => State.Plants = value; }
    private static EntitySeasonPeriod[] seasonPeriods { get => State.Seasons; set => State.Seasons = value; }
    private static float capabilitySeconds { get => State.CapabilitySeconds; set => State.CapabilitySeconds = value; }
    private const float CapabilityInterval = 0.25f;
    private static readonly ProfilerMarker ModulesMarker = new("WorldEntityRuntime.Modules");
    private static readonly ProfilerMarker JobCompletionMarker = new("WorldEntityRuntime.CompleteJobs");
    private static readonly ProfilerMarker CapabilitiesMarker = new("WorldEntityRuntime.Capabilities");
    private static readonly ProfilerMarker PostSimulationMarker = new("WorldEntityRuntime.PostSimulation");
    public static ulong Generation { get => State.Generation; private set => State.Generation = value; }
    public static ulong CapabilityVersion { get => State.CapabilityVersion; private set => State.CapabilityVersion = value; }
    public static World Current => world != null && world.IsCreated ? world : null;
    public static int ModuleCount => modules.Count;
    public static int FailedModuleCount => failed.Count;
    public static string DimensionId => dimensionId;
    public static bool DevelopmentSuspended { get => State.Suspended; set => State.Suspended = value; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => ReleaseAll();

    /// <summary>自然物可先于动物创建世界；世界身份来自存档、维度和地形纪元，而非玩家 GameObject。</summary>
    public static World GetOrCreate(string dimension)
    {
        ChunkMgr manager = ChunkMgr.ExistingInstance;
        GameSaveData currentSave = SaveDataMgr.Instance?.SaveData;
        WorldRuntime currentTerrain = manager?.WorldRuntime;
        if (releasing || manager == null || manager.IsWorldRuntimeShuttingDown ||
            currentTerrain == null || currentSave == null || string.IsNullOrWhiteSpace(dimension))
            throw new InvalidOperationException("正式实体世界需要有效的存档、地形纪元和维度。");

        if (Current != null && ReferenceEquals(terrainWorld, currentTerrain) &&
            terrainEpoch == currentTerrain.Epoch && ReferenceEquals(save, currentSave) &&
            string.Equals(dimensionId, dimension, StringComparison.Ordinal))
            return world;

        ReleaseWorld();
        terrainWorld = currentTerrain;
        terrainEpoch = currentTerrain.Epoch;
        save = currentSave;
        dimensionId = dimension;
        world = new World("FlatWorld 统一实体世界 / " + dimension);
        capabilities = world.GetOrCreateSystemManaged<EntityCapabilitySystem>();
        plants = world.GetOrCreateSystemManaged<EntityPlantModuleSystem>();
        return world;
    }

    /// <summary>同名能力只能有一个运行器；快照只在登记变化时更新，不在每帧分配。</summary>
    public static void Register(World expectedWorld, IWorldEntityRuntimeModule module)
    {
        if (module == null || string.IsNullOrWhiteSpace(module.RuntimeModuleId))
            throw new ArgumentException("实体能力运行器缺少稳定 ID。", nameof(module));
        RuntimeState owner = null;
        foreach (RuntimeState state in worlds.Values)
            if (ReferenceEquals(state.World, expectedWorld)) { owner = state; break; }
        if (owner == null || owner.Releasing || expectedWorld?.IsCreated != true)
            throw new InvalidOperationException("不能把能力运行器登记到已经失效的实体世界。");
        if (owner.Modules.Contains(module)) return;
        foreach (IWorldEntityRuntimeModule existing in owner.Modules)
            if (string.Equals(existing.RuntimeModuleId, module.RuntimeModuleId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("实体能力运行器重复：" + module.RuntimeModuleId);
        owner.Modules.Add(module);
        owner.Dispatch = owner.Modules.ToArray();
    }

    public static void Unregister(IWorldEntityRuntimeModule module)
    {
        if (module == null) return;
        foreach (RuntimeState state in worlds.Values)
        {
            state.Failed.Remove(module);
            if (state.Modules.Remove(module)) state.Dispatch = state.Modules.ToArray();
        }
    }

    #endregion

    #region 统一调度与释放

    /// <summary>由 ItemMgr 的正式玩法门禁只推进一次；运行器内部按能力查询并行，不扫描无关实体。</summary>
    public static void Tick(float deltaTime)
    {
        if (deltaTime <= 0f) return;
        tickWorlds.Clear();
        foreach (var pair in worlds) if (pair.Value.World?.IsCreated == true) tickWorlds.Add(pair.Key);
        // 一个玩法调度入口逐世界推进，后台地表不因主机进入太空而停止成长。
        foreach (string key in tickWorlds)
        {
            if (GameplayWorldRegistry.TryGet(key, out _))
            {
                using var scope = GameplayWorldRegistry.UseWorld(key);
                TickCurrent(deltaTime);
            }
            else if (key == GameplayWorldRegistry.ExecutionWorldKey) TickCurrent(deltaTime);
        }
    }

    private static void TickCurrent(float deltaTime)
    {
        if (Current == null || releasing || DevelopmentSuspended || deltaTime <= 0f) return;
        ChunkMgr manager = ChunkMgr.ExistingInstance;
        if (manager == null || manager.IsWorldRuntimeShuttingDown ||
            !ReferenceEquals(terrainWorld, manager.WorldRuntime) || terrainWorld.Epoch != terrainEpoch)
            return;

        IWorldEntityRuntimeModule[] currentDispatch = dispatch;
        using (ModulesMarker.Auto()) for (int i = 0; i < currentDispatch.Length; i++)
        {
            IWorldEntityRuntimeModule module = currentDispatch[i];
            if (!modules.Contains(module) || failed.Contains(module)) continue;
            try
            {
                module.TickEntities(deltaTime);
            }
            catch (Exception exception)
            {
                // 失败能力保留状态供保存/排查，只停止它的后续 Tick，避免反复错误和静默丢实体。
                failed.Add(module);
                Debug.LogError($"[WorldEntityRuntime] 能力 {module.RuntimeModuleId} 已停止推进：{exception}");
            }
        }
        // 全部运行器提交后才进入共同完成边界，允许无冲突任务先排入依赖链。
        using (JobCompletionMarker.Auto()) foreach (IWorldEntityRuntimeModule module in currentDispatch)
        {
            if (!modules.Contains(module)) continue;
            try { module.CompleteEntityJobs(); }
            catch (Exception exception)
            {
                if (failed.Add(module)) Debug.LogError($"[WorldEntityRuntime] 能力收尾失败 {module.RuntimeModuleId}：{exception}");
            }
        }
        using (CapabilitiesMarker.Auto()) TickCommonCapabilities(deltaTime);
        using (PostSimulationMarker.Auto()) foreach (IWorldEntityRuntimeModule module in currentDispatch)
        {
            if (!modules.Contains(module) || failed.Contains(module) || module is not IWorldEntityPostSimulationModule post) continue;
            try { post.AfterEntitySimulation(deltaTime); }
            catch (Exception exception) { failed.Add(module); Debug.LogError($"[WorldEntityRuntime] 能力提交失败 {module.RuntimeModuleId}：{exception}"); }
        }
    }

    /// <summary>结构变更与保存之前完成全部能力的 Native 访问。</summary>
    public static void CompleteJobs()
    {
        foreach (IWorldEntityRuntimeModule module in dispatch) module.CompleteEntityJobs();
        if (Current != null) capabilities?.Complete();
        if (Current != null) plants?.Complete();
    }

    /// <summary>成长、耐候等通用能力有自己的低频脉冲，动物和资源共用这些组件时也只计算一次。</summary>
    private static void TickCommonCapabilities(float deltaTime)
    {
        capabilitySeconds += deltaTime;
        if (capabilitySeconds < CapabilityInterval || DayTimeSystem.Instance == null ||
            !DayTimeSystem.Instance.TryGetActiveTimeData(out TimeData clock) || clock.DayLength <= 0f)
            return;
        capabilities.StepSeconds = capabilitySeconds;
        capabilitySeconds = 0f;
        capabilities.GameTime = clock.GetTotalGameTimeSeconds();
        capabilities.DayLength = clock.DayLength;
        capabilities.Seasonal = DimensionManager.ExistingInstance?.ActiveDefinition?.SuppressWeather != true;
        capabilities.DifficultyGrowthMultiplier = GameDifficultyService.Current.Production.CropGrowthMultiplier;
        UpdateSeasonSnapshot(clock);
        capabilities.Update();
        plants.StepSeconds = capabilities.StepSeconds;
        plants.GameTime = capabilities.GameTime;
        plants.DayLength = capabilities.DayLength;
        plants.Seasonal = capabilities.Seasonal;
        plants.GrowthDifficulty = capabilities.DifficultyGrowthMultiplier;
        plants.DependOn(capabilities.PendingJobs);
        try { plants.Update(); }
        finally { plants.Complete(); }
        CapabilityVersion++;
    }

    private static void UpdateSeasonSnapshot(TimeData clock)
    {
        int count = (clock.SeasonHistory?.Count ?? 0) + 1;
        bool changed = seasonPeriods == null || seasonPeriods.Length != count;
        if (changed) seasonPeriods = new EntitySeasonPeriod[count];
        for (int i = 0; i < count; i++)
        {
            SeasonCalendarHistoryEntry old = i < count - 1 ? clock.SeasonHistory[i] : null;
            EntitySeasonPeriod next = old != null ? SeasonCalendar.CreateEntityPeriod(old) : SeasonCalendar.CreateEntityPeriod(clock);
            EntitySeasonPeriod previous = seasonPeriods[i];
            if (previous.EndTimeSeconds != next.EndTimeSeconds || previous.OffsetSeconds != next.OffsetSeconds ||
                !previous.Fractions.Equals(next.Fractions) || !previous.Temperatures.Equals(next.Temperatures) ||
                previous.TiltScale != next.TiltScale ||
                previous.OrbitalEccentricity != next.OrbitalEccentricity ||
                previous.RotationPeriodSeconds != next.RotationPeriodSeconds ||
                previous.OrbitalPeriodSeconds != next.OrbitalPeriodSeconds) changed = true;
            seasonPeriods[i] = next;
        }
        if (changed) { capabilities.SetSeasons(seasonPeriods); plants.SetSeasons(seasonPeriods); }
    }

    /// <summary>区块先保存解绑，然后运行器释放自己的实体，最后由唯一所有者销毁 World。</summary>
    public static void ReleaseWorld()
    {
        if (releasing) return;
        releasing = true;
        try
        {
            IWorldEntityRuntimeModule[] currentDispatch = dispatch;
            // 单个 Job 收尾失败不能跳过其它运行器及 World 的释放。
            if (Current != null)
            {
                TryReleaseResource("通用能力 Job", () => capabilities?.Complete());
                TryReleaseResource("植物能力 Job", () => plants?.Complete());
            }
            foreach (IWorldEntityRuntimeModule module in currentDispatch)
            {
                TryReleaseResource(module.RuntimeModuleId + " Job", module.CompleteEntityJobs);
            }
            foreach (IWorldEntityRuntimeModule module in currentDispatch)
            {
                TryReleaseResource(module.RuntimeModuleId, module.ReleaseEntities);
            }
            if (world != null && world.IsCreated)
                TryReleaseResource("World", world.Dispose);
        }
        finally
        {
            world = null;
            capabilities = null;
            plants = null;
            seasonPeriods = null;
            capabilitySeconds = 0f;
            CapabilityVersion = 0;
            DevelopmentSuspended = false;
            terrainWorld = null;
            save = null;
            dimensionId = null;
            modules.Clear();
            failed.Clear();
            dispatch = Array.Empty<IWorldEntityRuntimeModule>();
            Generation = ++nextGeneration;
            releasing = false;
        }
    }

    public static void ReleaseWorld(string worldKey)
    {
        if (worldKey == null || !worlds.TryGetValue(worldKey, out RuntimeState state)) return;
        if (GameplayWorldRegistry.TryGet(worldKey, out _))
        {
            using var scope = GameplayWorldRegistry.UseWorld(worldKey);
            ReleaseWorld();
        }
        else
        {
            RuntimeState previous = releaseOverride;
            releaseOverride = state;
            try { ReleaseWorld(); }
            finally { releaseOverride = previous; }
        }
        worlds.Remove(worldKey);
        if (cachedWorldKey == worldKey) { cachedWorldKey = null; cachedState = null; }
    }

    public static void ReleaseAll()
    {
        var keys = new List<string>(worlds.Keys);
        foreach (string key in keys) ReleaseWorld(key);
        worlds.Clear(); tickWorlds.Clear(); releaseOverride = null;
        cachedWorldKey = null; cachedState = null;
    }

    /// <summary>只在退出边界隔离异常，保留具体失败步骤并继续清理独立资源。</summary>
    private static void TryReleaseResource(string stage, Action release)
    {
        try { release(); }
        catch (Exception exception)
        {
            Debug.LogException(new InvalidOperationException("[WorldEntityRuntime] 释放失败：" + stage, exception));
        }
    }

    #endregion
}
