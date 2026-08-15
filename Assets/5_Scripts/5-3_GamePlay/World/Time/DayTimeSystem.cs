using Sirenix.OdinInspector;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using MemoryPack;
using UnityEngine.SceneManagement;
using System;

public class DayTimeSystem : SingletonMono<DayTimeSystem>
{
    public event Action<string, float, float> TimeAdvanced;
    public event Action<string, int, int> DayChanged;

    [ShowInInspector]
    public Dictionary<string, TimeData> WorldTimeDict = new Dictionary<string, TimeData>();
    
    [ShowInInspector]
    public Dictionary<string, float> SceneLightingRateDict = new Dictionary<string, float>();

    // 全局光源引用
    [Tooltip("全局光源引用")]
    public Light2D GlobalLight;

    #region 运行时配置

    private Color defaultLightColor = Color.white;
    private bool syncTileLightLayer = true;
    private float activeChunkLightRefreshInterval = 0.25f;
    private float inactiveChunkLightRefreshInterval = 5f;
    private string appliedPresentationProfileId = string.Empty;
    private string appliedPresentationSceneName = string.Empty;
    private int appliedPresentationConfigVersion = -1;

    #endregion

    private void OnEnable()
    {
        SubscribeGameManagerEvents();
    }

    private void Start()
    {
        SubscribeGameManagerEvents();
    }

    private void OnDisable()
    {
        UnsubscribeGameManagerEvents();
    }

    private void SubscribeGameManagerEvents()
    {
        if (GameManager.Instance == null)
            return;

        GameManager.Instance.Event_GameWorldEnter -= OnGameWorldEnter;
        GameManager.Instance.Event_GameWorldExit -= OnGameWorldExit;
        GameManager.Instance.Event_GameWorldEnter += OnGameWorldEnter;
        GameManager.Instance.Event_GameWorldExit += OnGameWorldExit;
    }

    private void UnsubscribeGameManagerEvents()
    {
        if (GameManager.Instance == null)
            return;

        GameManager.Instance.Event_GameWorldEnter -= OnGameWorldEnter;
        GameManager.Instance.Event_GameWorldExit -= OnGameWorldExit;
    }

    private void OnGameWorldEnter()
    {
        if (SaveDataMgr.Instance?.SaveData == null)
            return;

        LoadFromSaveData(SaveDataMgr.Instance.SaveData.DayTimeData);
        string sceneName = SceneManager.GetActiveScene().name;
        EnsureSceneTimeData(sceneName, GameManager.Instance.ReadyTimeData);
        ApplyPresentationSettings(sceneName);
    }

    private void OnGameWorldExit()
    {
        if (SaveDataMgr.Instance?.SaveData != null)
        {
            SaveDataMgr.Instance.SaveData.DayTimeData = GetSaveData();
        }

        WorldTimeDict?.Clear();
        SceneLightingRateDict?.Clear();
        appliedPresentationProfileId = string.Empty;
        appliedPresentationSceneName = string.Empty;
        appliedPresentationConfigVersion = -1;
    }

    private void Update()
    {
        if (GameManager.Instance == null || !GameManager.Instance.IsInGameWorld)
            return;

        // 主循环推进所有独立场景的时间
        foreach (var kvp in WorldTimeDict)
        {
            string sceneName = kvp.Key;
            TimeData timeData = kvp.Value;
            
            // 只有独立场景才推进时间（没有引用其他场景的场景）
            if (string.IsNullOrEmpty(timeData.ReferenceScene))
            {
                TimeRun(sceneName, Time.deltaTime);
            }
        }
        
        // 更新当前场景的光照（假设有一个当前激活的场景）
        UpdateCurrentSceneLighting();
    }

/// <summary>
/// 推进指定场景的时间
/// </summary>
private void TimeRun(string sceneName, float deltaTime)
{
    if (WorldTimeDict.TryGetValue(sceneName, out TimeData timeData))
        AdvanceTime(
            sceneName,
            deltaTime *
            timeData.TimeScaleModifier *
            GameDifficultyService.Current.World.TimeSpeedMultiplier);
}

    /// <summary>
    /// 更新当前场景的光照
    /// </summary>
    private void UpdateCurrentSceneLighting()
    {
        // 这里需要知道当前激活的场景
        // 在实际使用中，可以通过场景管理器获取当前场景名
        string currentSceneName = GetCurrentActiveSceneName();
        if (!string.IsNullOrEmpty(currentSceneName))
        {
            ApplyPresentationSettings(currentSceneName);
            float lighting = GetLighting(currentSceneName);
            Color lightColor = GetLightColor(currentSceneName);
            SetGlobalLight(lighting, lightColor);
        }
    }

    /// <summary>
    /// 获取当前激活的场景名（需要根据实际项目实现）
    /// </summary>
    private string GetCurrentActiveSceneName()
    {
        return SceneManager.GetActiveScene().name;
    }

    /// <summary>
    /// 设置全局光源的强度和颜色
    /// </summary>
    private void SetGlobalLight(float intensity, Color color)
    {
        if (GlobalLight != null)
        {
            GlobalLight.intensity = intensity;
            GlobalLight.color = color;
        }

        if (syncTileLightLayer)
        {
            LightLayerMgr.Instance.SetTimeLighting(
                GlobalLight,
                intensity,
                color,
            activeChunkLightRefreshInterval,
            inactiveChunkLightRefreshInterval);
        }
    }

    private Color GetLightColor(string sceneName)
    {
        if (!TryGetResolvedTimeData(sceneName, out _, out TimeData timeData))
            return defaultLightColor;

        // 0-1 的昼夜进度
        float t = Mathf.Clamp01(timeData.CurrentTime / timeData.DayLength);
        return timeData.dayNightGradient.Evaluate(t);
    }

    private void ApplyPresentationSettings(string sceneName)
    {
        if (!TryGetResolvedTimeData(sceneName, out _, out TimeData timeData))
            return;

        string profileId = string.IsNullOrWhiteSpace(timeData.TimeSystemProfileId)
            ? TimeSystemConfigService.DefaultProfileId
            : timeData.TimeSystemProfileId;
        if (string.Equals(appliedPresentationProfileId, profileId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(appliedPresentationSceneName, sceneName, StringComparison.Ordinal) &&
            appliedPresentationConfigVersion == TimeSystemConfigService.Version)
        {
            return;
        }

        if (!TimeSystemConfigService.TryGetPresentationSettings(
                profileId,
                out TimeSystemPresentationSettings settings))
        {
            settings = TimeSystemPresentationSettings.CreateDefault();
        }

        defaultLightColor = settings.DefaultLightColor;
        syncTileLightLayer = settings.SyncTileLightLayer;
        activeChunkLightRefreshInterval = settings.ActiveChunkLightRefreshInterval;
        inactiveChunkLightRefreshInterval = settings.InactiveChunkLightRefreshInterval;
        appliedPresentationProfileId = profileId ?? string.Empty;
        appliedPresentationSceneName = sceneName;
        appliedPresentationConfigVersion = TimeSystemConfigService.Version;
    }
    #region 公共接口

    /// <summary>
    /// 获取场景当前时间
    /// </summary>
    public float GetCurrentTime(string sceneName)
    {
        return TryGetResolvedTimeData(sceneName, out _, out TimeData timeData)
            ? timeData.CurrentTime
            : 0f;
    }

    /// <summary>
    /// 解析场景时间引用，循环引用或缺失数据时返回 false。
    /// </summary>
    public bool TryGetResolvedTimeData(string sceneName, out string resolvedSceneName, out TimeData timeData)
    {
        resolvedSceneName = sceneName;
        timeData = null;

        for (int depth = 0; depth < 16; depth++)
        {
            if (string.IsNullOrEmpty(resolvedSceneName) ||
                WorldTimeDict == null ||
                !WorldTimeDict.TryGetValue(resolvedSceneName, out timeData) ||
                timeData == null)
            {
                return false;
            }

            if (string.IsNullOrEmpty(timeData.ReferenceScene))
                return true;

            if (timeData.ReferenceScene == resolvedSceneName)
                return false;

            resolvedSceneName = timeData.ReferenceScene;
        }

        timeData = null;
        return false;
    }

    /// <summary>
    /// 获取最终光照强度
    /// </summary>
    public float GetLighting(string sceneName)
    {
        // 获取采光率
        float lightingRate = 1.0f;
        SceneLightingRateDict.TryGetValue(sceneName, out lightingRate);

        // 获取时间数据
        if (!WorldTimeDict.TryGetValue(sceneName, out TimeData timeData))
            return 0f;

        float baseLightIntensity;

        // 如果该场景引用了其他场景，则使用被引用场景的光照参数
        if (!string.IsNullOrEmpty(timeData.ReferenceScene))
        {
            baseLightIntensity = GetLighting(timeData.ReferenceScene);
        }
        else
        {
            // 使用自身光照参数计算基础光照强度
            float timeRatio = timeData.CurrentTime / timeData.DayLength;
            baseLightIntensity = timeData.LightParams.Evaluate(timeRatio);
            baseLightIntensity = Mathf.Max(baseLightIntensity, GetMoonlightIntensity(timeData));
        }

        // 维度光照值是上限而不是固定值：白天不会超过矿洞亮度，夜晚仍跟随地表继续变暗。
        float lighting = baseLightIntensity * lightingRate;
        if (DimensionManager.Instance.TryGetDefinitionForWorldKey(sceneName, out DimensionDefinition dimension) &&
            dimension.UseFixedLighting)
        {
            lighting = Mathf.Min(lighting, Mathf.Clamp01(dimension.FixedLighting));
        }

        return lighting;
    }

    private float GetMoonlightIntensity(TimeData timeData)
    {
        if (timeData == null)
            return 0f;

        float dayLength = Mathf.Max(1f, timeData.DayLength);
        float currentDayProgress = Mathf.Repeat(timeData.CurrentTime, dayLength) / dayLength;
        float elapsedDays = Mathf.Max(0, timeData.TotalDays) + currentDayProgress;
        float cycleDays = Mathf.Max(1f, timeData.LunarCycleDays);
        float moonPhase = Mathf.Repeat(timeData.InitialMoonPhase + elapsedDays / cycleDays, 1f);
        float illumination = 0.5f - 0.5f * Mathf.Cos(moonPhase * Mathf.PI * 2f);

        return Mathf.Lerp(
            Mathf.Clamp01(timeData.NewMoonNightIntensity),
            Mathf.Clamp01(timeData.FullMoonNightIntensity),
            illumination);
    }
    
    /// <summary>
    /// 手动设置当前场景光照（用于特定场景切换时）
    /// </summary>
    public void SetCurrentSceneLighting(string sceneName)
    {
        float lighting = GetLighting(sceneName);
        Color lightColor = GetLightColor(sceneName);
        SetGlobalLight(lighting, lightColor);
    }

    /// <summary>
    /// 修改一天时长
    /// </summary>
    public void SetDayLength(string sceneName, float minutes)
    {
        if (!WorldTimeDict.TryGetValue(sceneName, out TimeData timeData))
        {
            timeData = CreateConfiguredTimeData();
            WorldTimeDict[sceneName] = timeData;
        }

        timeData.DayLength = minutes;
    }

    /// <summary>
    /// 修改时间倍率
    /// </summary>
    public void SetTimeScale(string sceneName, float multiplier)
    {
        if (!WorldTimeDict.TryGetValue(sceneName, out TimeData timeData))
        {
            timeData = CreateConfiguredTimeData();
            WorldTimeDict[sceneName] = timeData;
        }

        timeData.TimeScaleModifier = multiplier;
    }

    /// <summary>
    /// 强制跳转时间
    /// </summary>
    public void JumpToTime(string sceneName, float timeValue)
    {
        if (!WorldTimeDict.TryGetValue(sceneName, out TimeData timeData))
        {
            timeData = CreateConfiguredTimeData();
            WorldTimeDict[sceneName] = timeData;
        }

        float dayLength = Mathf.Max(1f, timeData.DayLength);
        int oldDay = timeData.GetCurrentDay();
        float oldTotalTime = timeData.GetTotalGameTime();
        int additionalDays = Mathf.Max(0, Mathf.FloorToInt(timeValue / dayLength));

        timeData.TotalDays += additionalDays;
        timeData.CurrentTime = Mathf.Repeat(timeValue, dayLength);

        TimeAdvanced?.Invoke(sceneName, oldTotalTime, timeData.GetTotalGameTime());
        if (oldDay != timeData.GetCurrentDay())
            DayChanged?.Invoke(sceneName, oldDay, timeData.GetCurrentDay());
    }

    /// <summary>
    /// 推进指定场景的游戏时间，并正确结算跨过的全部游戏日。
    /// </summary>
    public void AdvanceTime(string sceneName, float gameSeconds)
    {
        if (gameSeconds <= 0f ||
            WorldTimeDict == null ||
            !WorldTimeDict.TryGetValue(sceneName, out TimeData timeData) ||
            timeData == null)
        {
            return;
        }

        timeData.EnsureTimeSystemDefaults();
        gameSeconds = ClampAdvanceToTimeLimit(timeData, gameSeconds);
        if (gameSeconds <= 0f)
            return;

        float dayLength = Mathf.Max(1f, timeData.DayLength);
        int oldDay = timeData.GetCurrentDay();
        float oldTotalTime = timeData.GetTotalGameTime();
        float nextTime = timeData.CurrentTime + gameSeconds;
        int daysPassed = Mathf.Max(0, Mathf.FloorToInt(nextTime / dayLength));

        timeData.TotalDays += daysPassed;
        timeData.CurrentTime = Mathf.Repeat(nextTime, dayLength);

        float newTotalTime = timeData.GetTotalGameTime();
        TimeAdvanced?.Invoke(sceneName, oldTotalTime, newTotalTime);
        if (oldDay != timeData.GetCurrentDay())
            DayChanged?.Invoke(sceneName, oldDay, timeData.GetCurrentDay());
    }

    /// <summary>
    /// 设置光照依赖
    /// </summary>
    public void SetReferenceScene(string sceneName, string refName)
    {
        if (!WorldTimeDict.TryGetValue(sceneName, out TimeData timeData))
        {
            timeData = CreateConfiguredTimeData();
            WorldTimeDict[sceneName] = timeData;
        }

        timeData.ReferenceScene = refName;
    }

    /// <summary>
    /// 设置采光率
    /// </summary>
    public void SetLightingRate(string sceneName, float rate)
    {
        SceneLightingRateDict[sceneName] = Mathf.Clamp01(rate);
    }

    /// <summary>
    /// 初始化场景时间数据
    /// </summary>
    public void InitializeSceneTimeData(string sceneName, float dayLength = 24f, float timeScale = 1f)
    {
        if (!WorldTimeDict.ContainsKey(sceneName))
        {
            TimeData timeData = CreateConfiguredTimeData();
            timeData.DayLength = Mathf.Max(1f, dayLength);
            timeData.CurrentTime = Mathf.Repeat(timeData.CurrentTime, timeData.DayLength);
            timeData.TimeScaleModifier = Mathf.Max(0f, timeScale);
            WorldTimeDict[sceneName] = timeData;
        }

        // 默认采光率为1.0（完全采光）
        if (!SceneLightingRateDict.ContainsKey(sceneName))
        {
            SceneLightingRateDict[sceneName] = 1.0f;
        }
    }

    public void EnsureSceneTimeData(string sceneName, TimeData readyTimeData)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogWarning("[DayTimeSystem] 无法初始化时间数据：sceneName 为空");
            return;
        }

        if (WorldTimeDict == null)
            WorldTimeDict = new Dictionary<string, TimeData>();

        if (SceneLightingRateDict == null)
            SceneLightingRateDict = new Dictionary<string, float>();

        if (!WorldTimeDict.ContainsKey(sceneName) || WorldTimeDict[sceneName] == null)
        {
            TimeData sourceTimeData = readyTimeData ?? CreateConfiguredTimeData();
            WorldTimeDict[sceneName] = sourceTimeData.CreateRuntimeCopy();

            Debug.Log($"[DayTimeSystem] 未找到场景时间数据，已拷贝 ReadyTimeData：{sceneName}");
        }

        WorldTimeDict[sceneName].EnsureTimeSystemDefaults();
        WorldTimeDict[sceneName].EnsureDarkNightWindow();

        if (!SceneLightingRateDict.ContainsKey(sceneName))
        {
            SceneLightingRateDict[sceneName] = 1.0f;
        }
    }

    private TimeData CreateConfiguredTimeData()
    {
        if (TimeSystemConfigService.TryCreateDefaultTimeData(
                out TimeData configuredTimeData,
                out _))
        {
            return configuredTimeData;
        }

        TimeData fallback = new TimeData();
        fallback.EnsureTimeSystemDefaults();
        return fallback;
    }

    private static float ClampAdvanceToTimeLimit(TimeData timeData, float gameSeconds)
    {
        if (!string.Equals(timeData.TimeSystemMode, TimeSystemModes.TimeLimited, StringComparison.OrdinalIgnoreCase) ||
            timeData.TimeLimitTotalGameTime <= 0f)
        {
            return gameSeconds;
        }

        float remaining = timeData.TimeLimitTotalGameTime - timeData.GetTotalGameTime();
        return Mathf.Min(gameSeconds, Mathf.Max(0f, remaining));
    }

    #endregion

    #region 存档相关

    /// <summary>
    /// 获取用于存档的时间系统数据
    /// </summary>
    public DayTimeSaveData GetSaveData()
    {
        var saveData = new DayTimeSaveData();
        
        // 序列化时间数据
        foreach (var kvp in WorldTimeDict)
        {
            saveData.WorldTimeDict[kvp.Key] = new SerializableTimeData(kvp.Value);
        }
        
        // 复制采光率数据
        foreach (var kvp in SceneLightingRateDict)
        {
            saveData.SceneLightingRateDict[kvp.Key] = kvp.Value;
        }
        
        return saveData;
    }

/// <summary>
/// 从存档数据恢复时间系统
/// </summary>
public void LoadFromSaveData(DayTimeSaveData saveData)
{
    if (saveData != null)
    {
        // 确保字典已初始化
        if (WorldTimeDict == null)
            WorldTimeDict = new Dictionary<string, TimeData>();
            
        if (SceneLightingRateDict == null)
            SceneLightingRateDict = new Dictionary<string, float>();
            
        // 清空现有数据
        WorldTimeDict.Clear();
        SceneLightingRateDict.Clear();
        
        // 恢复时间数据
        foreach (var kvp in saveData.WorldTimeDict)
        {
            TimeData timeData = kvp.Value.ToTimeData();
            timeData?.EnsureTimeSystemDefaults();
            timeData?.EnsureDarkNightWindow();
            WorldTimeDict[kvp.Key] = timeData;
        }
        
        // 恢复采光率数据
        foreach (var kvp in saveData.SceneLightingRateDict)
        {
            SceneLightingRateDict[kvp.Key] = kvp.Value;
        }
    }
}

    #endregion
}

// 可序列化的 TimeData 版本，用于 MemoryPack 序列化
[MemoryPackable]
public partial class SerializableTimeData
{
    public float CurrentTime;
    public float DayLength;
    public SerializableKeyframe[] LightParamsKeys;
    public float TimeScaleModifier;
    public string ReferenceScene;
    public int TotalDays;
    public SerializableGradient DayNightGradient;
    public string TimeSystemProfileId;
    public string TimeSystemMode;
    public float TimeLimitTotalGameTime;
    public float LunarCycleDays;
    public float NewMoonNightIntensity;
    public float FullMoonNightIntensity;
    public float InitialMoonPhase;

    [MemoryPackConstructor]
    public SerializableTimeData(float currentTime,
                                float dayLength,
                                SerializableKeyframe[] lightParamsKeys,
                                float timeScaleModifier,
                                string referenceScene,
                                SerializableGradient dayNightGradient,
                                int totalDays,
                                string timeSystemProfileId,
                                string timeSystemMode,
                                float timeLimitTotalGameTime,
                                float lunarCycleDays,
                                float newMoonNightIntensity,
                                float fullMoonNightIntensity,
                                float initialMoonPhase)
    {
        CurrentTime = currentTime;
        DayLength = dayLength;
        LightParamsKeys = lightParamsKeys;
        TimeScaleModifier = timeScaleModifier;
        ReferenceScene = referenceScene ?? "";
        DayNightGradient = dayNightGradient;
        TotalDays = Mathf.Max(0, totalDays);
        TimeSystemProfileId = timeSystemProfileId ?? "";
        TimeSystemMode = timeSystemMode ?? TimeSystemModes.Unlimited;
        TimeLimitTotalGameTime = timeLimitTotalGameTime;
        LunarCycleDays = lunarCycleDays;
        NewMoonNightIntensity = newMoonNightIntensity;
        FullMoonNightIntensity = fullMoonNightIntensity;
        InitialMoonPhase = initialMoonPhase;
    }

    // 从运行时 TimeData 抽数据
    public SerializableTimeData(TimeData timeData)
    {
        CurrentTime = timeData.CurrentTime;
        DayLength = timeData.DayLength;
        TimeScaleModifier = timeData.TimeScaleModifier;
        ReferenceScene = timeData.ReferenceScene ?? "";
        TotalDays = Mathf.Max(0, timeData.TotalDays);
        TimeSystemProfileId = timeData.TimeSystemProfileId ?? "";
        TimeSystemMode = timeData.TimeSystemMode ?? TimeSystemModes.Unlimited;
        TimeLimitTotalGameTime = timeData.TimeLimitTotalGameTime;
        LunarCycleDays = timeData.LunarCycleDays;
        NewMoonNightIntensity = timeData.NewMoonNightIntensity;
        FullMoonNightIntensity = timeData.FullMoonNightIntensity;
        InitialMoonPhase = timeData.InitialMoonPhase;

        // AnimationCurve → 数组
        if (timeData.LightParams != null && timeData.LightParams.keys != null)
        {
            var keys = timeData.LightParams.keys;
            LightParamsKeys = new SerializableKeyframe[keys.Length];
            for (int i = 0; i < keys.Length; i++)
                LightParamsKeys[i] = new SerializableKeyframe(keys[i]);
        }

        // Gradient → 可序列化版本
        DayNightGradient = SerializableGradient.CreateFrom(timeData.dayNightGradient);
    }

    // 还原回运行时 TimeData
    public TimeData ToTimeData()
    {
        // 重建曲线
        var curve = new AnimationCurve();
        if (LightParamsKeys != null)
        {
            var keys = new Keyframe[LightParamsKeys.Length];
            for (int i = 0; i < LightParamsKeys.Length; i++)
                keys[i] = LightParamsKeys[i].ToKeyframe();
            curve = new AnimationCurve(keys);
        }

        TimeData timeData = new TimeData
        {
            CurrentTime = CurrentTime,
            DayLength = DayLength,
            LightParams = curve,
            TimeScaleModifier = TimeScaleModifier,
            ReferenceScene = ReferenceScene,
            TotalDays = Mathf.Max(0, TotalDays),
            dayNightGradient = DayNightGradient?.ToGradient() ?? new TimeData().dayNightGradient,
            TimeSystemProfileId = TimeSystemProfileId,
            TimeSystemMode = TimeSystemMode,
            TimeLimitTotalGameTime = TimeLimitTotalGameTime,
            LunarCycleDays = LunarCycleDays,
            NewMoonNightIntensity = NewMoonNightIntensity,
            FullMoonNightIntensity = FullMoonNightIntensity,
            InitialMoonPhase = InitialMoonPhase
        };

        timeData.EnsureTimeSystemDefaults();
        return timeData;
    }
}

[MemoryPackable]
public partial class SerializableKeyframe
{
    public float time;
    public float value;
    public float inTangent;
    public float outTangent;
    
    public SerializableKeyframe() { }
    
    [MemoryPackConstructor]
    public SerializableKeyframe(float time, float value, float inTangent, float outTangent)
    {
        this.time = time;
        this.value = value;
        this.inTangent = inTangent;
        this.outTangent = outTangent;
    }
    
    public SerializableKeyframe(Keyframe keyframe)
    {
        time = keyframe.time;
        value = keyframe.value;
        inTangent = keyframe.inTangent;
        outTangent = keyframe.outTangent;
    }
    
    public Keyframe ToKeyframe()
    {
        return new Keyframe(time, value, inTangent, outTangent);
    }
}

[MemoryPackable]
public partial class DayTimeSaveData
{
    public Dictionary<string, SerializableTimeData> WorldTimeDict = new Dictionary<string, SerializableTimeData>();
    public Dictionary<string, float> SceneLightingRateDict = new Dictionary<string, float>();
    
    public DayTimeSaveData() 
    {
        WorldTimeDict = new Dictionary<string, SerializableTimeData>();
        SceneLightingRateDict = new Dictionary<string, float>();
    }
    
    [MemoryPackConstructor]
    public DayTimeSaveData(Dictionary<string, SerializableTimeData> worldTimeDict, 
                          Dictionary<string, float> sceneLightingRateDict)
    {
        WorldTimeDict = worldTimeDict ?? new Dictionary<string, SerializableTimeData>();
        SceneLightingRateDict = sceneLightingRateDict ?? new Dictionary<string, float>();
    }
}

[MemoryPackable]
public partial class SerializableGradient
{
    [MemoryPackInclude] public GradientColorKey[] colorKeys;
    [MemoryPackInclude] public GradientAlphaKey[] alphaKeys;

    [System.NonSerialized] private Gradient gradient;

    // 1. 必须的无参构造
    public SerializableGradient() { }


    // 2. 不需要“Gradient g”构造了，改成静态方法
    public static SerializableGradient CreateFrom(Gradient g)
    {
        return new SerializableGradient
        {
            colorKeys = g.colorKeys,
            alphaKeys = g.alphaKeys,
            gradient = g
        };
    }

    public Gradient ToGradient()
    {
        if (gradient == null)
        {
            gradient = new Gradient();
            gradient.SetKeys(colorKeys, alphaKeys);
        }
        return gradient;
    }
}
