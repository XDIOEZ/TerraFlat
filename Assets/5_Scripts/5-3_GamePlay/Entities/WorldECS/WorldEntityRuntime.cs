using System;
using System.Collections.Generic;
using FlatWorld.AIECS;
using FlatWorld.WorldModel;
using Unity.Entities;
using Unity.Mathematics;
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

    private static World world;
    private static WorldRuntime terrainWorld;
    private static long terrainEpoch;
    private static GameSaveData save;
    private static string dimensionId;
    private static bool releasing;
    private static readonly List<IWorldEntityRuntimeModule> modules = new();
    private static readonly HashSet<IWorldEntityRuntimeModule> failed = new();
    private static IWorldEntityRuntimeModule[] dispatch = Array.Empty<IWorldEntityRuntimeModule>();
    private static EntityCapabilitySystem capabilities;
    private static EntityPlantModuleSystem plants;
    private static EntitySeasonPeriod[] seasonPeriods;
    private static float capabilitySeconds;
    private const float CapabilityInterval = 0.25f;
    public static ulong Generation { get; private set; } = 1;
    public static World Current => world != null && world.IsCreated ? world : null;
    public static int ModuleCount => modules.Count;
    public static int FailedModuleCount => failed.Count;
    public static string DimensionId => dimensionId;
    public static bool DevelopmentSuspended { get; set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => ReleaseWorld();

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
        if (releasing || Current == null || !ReferenceEquals(world, expectedWorld))
            throw new InvalidOperationException("不能把能力运行器登记到已经失效的实体世界。");
        if (modules.Contains(module)) return;
        foreach (IWorldEntityRuntimeModule existing in modules)
            if (existing.RuntimeModuleId == module.RuntimeModuleId)
                throw new InvalidOperationException("实体能力运行器重复：" + module.RuntimeModuleId);
        modules.Add(module);
        dispatch = modules.ToArray();
    }

    public static void Unregister(IWorldEntityRuntimeModule module)
    {
        if (module == null) return;
        failed.Remove(module);
        if (modules.Remove(module)) dispatch = modules.ToArray();
    }

    #endregion

    #region 统一调度与释放

    /// <summary>由 ItemMgr 的正式玩法门禁只推进一次；运行器内部按能力查询并行，不扫描无关实体。</summary>
    public static void Tick(float deltaTime)
    {
        if (Current == null || releasing || DevelopmentSuspended || deltaTime <= 0f) return;
        ChunkMgr manager = ChunkMgr.ExistingInstance;
        if (manager == null || manager.IsWorldRuntimeShuttingDown ||
            !ReferenceEquals(terrainWorld, manager.WorldRuntime) || terrainWorld.Epoch != terrainEpoch)
            return;

        IWorldEntityRuntimeModule[] currentDispatch = dispatch;
        for (int i = 0; i < currentDispatch.Length; i++)
        {
            IWorldEntityRuntimeModule module = currentDispatch[i];
            if (!modules.Contains(module) || failed.Contains(module)) continue;
            try
            {
                module.TickEntities(deltaTime);
                module.CompleteEntityJobs();
            }
            catch (Exception exception)
            {
                // 失败能力保留状态供保存/排查，只停止它的后续 Tick，避免反复错误和静默丢实体。
                failed.Add(module);
                Debug.LogError($"[WorldEntityRuntime] 能力 {module.RuntimeModuleId} 已停止推进：{exception}");
            }
        }
        TickCommonCapabilities(deltaTime);
        foreach (IWorldEntityRuntimeModule module in currentDispatch)
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
        capabilities.GameTime = clock.TotalDays * (double)clock.DayLength + clock.CurrentTime;
        capabilities.DayLength = clock.DayLength;
        capabilities.Seasonal = DimensionManager.ExistingInstance?.ActiveDefinition?.SuppressWeather != true;
        capabilities.DifficultyGrowthMultiplier = GameDifficultyService.Current.Production.CropGrowthMultiplier;
        WeatherMgr weather = WeatherMgr.Instance;
        float intensity = weather != null ? Mathf.Clamp01(weather.CurrentWeatherIntensity) : 0f;
        capabilities.RainIntensity = weather != null && weather.CurrentWeather == WeatherType.Rain ? intensity : 0f;
        capabilities.WeatherMultiplier = weather == null ? 1f : weather.CurrentWeather switch
        {
            WeatherType.Cloudy => Mathf.Lerp(1f, 0.95f, intensity),
            WeatherType.Storm => Mathf.Lerp(1f, 0.85f, intensity),
            _ => 1f
        };
        UpdateSeasonSnapshot(clock);
        capabilities.Update();
        capabilities.Complete();
        plants.StepSeconds = capabilities.StepSeconds;
        plants.GameTime = capabilities.GameTime;
        plants.DayLength = capabilities.DayLength;
        plants.Seasonal = capabilities.Seasonal;
        plants.GrowthDifficulty = capabilities.DifficultyGrowthMultiplier;
        plants.WeatherMultiplier = capabilities.WeatherMultiplier;
        plants.RainGrowthIntensity = capabilities.RainIntensity;
        plants.RainIntensity = weather != null && (weather.CurrentWeather == WeatherType.Rain || weather.CurrentWeather == WeatherType.Storm) ? intensity : 0f;
        plants.Update();
        plants.Complete();
    }

    private static void UpdateSeasonSnapshot(TimeData clock)
    {
        int count = (clock.SeasonHistory?.Count ?? 0) + 1;
        bool changed = seasonPeriods == null || seasonPeriods.Length != count;
        if (changed) seasonPeriods = new EntitySeasonPeriod[count];
        for (int i = 0; i < count; i++)
        {
            SeasonCalendarHistoryEntry old = i < count - 1 ? clock.SeasonHistory[i] : null;
            EntitySeasonPeriod next = SeasonCalendar.CreateEntityPeriod(old?.Settings ?? clock.Seasons,
                old?.OffsetDays ?? clock.SeasonOffsetDays, old?.EndWorldDay ?? double.PositiveInfinity);
            EntitySeasonPeriod previous = seasonPeriods[i];
            if (previous.EndDay != next.EndDay || previous.OffsetDays != next.OffsetDays ||
                !previous.Days.Equals(next.Days) || !previous.Temperatures.Equals(next.Temperatures)) changed = true;
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
            DevelopmentSuspended = false;
            terrainWorld = null;
            save = null;
            dimensionId = null;
            modules.Clear();
            failed.Clear();
            dispatch = Array.Empty<IWorldEntityRuntimeModule>();
            Generation++;
            releasing = false;
        }
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
