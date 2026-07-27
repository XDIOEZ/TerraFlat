using Sirenix.OdinInspector;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using MemoryPack;
using UnityEngine.SceneManagement;

public class DayTimeSystem : SingletonMono<DayTimeSystem>
{
    [ShowInInspector]
    public Dictionary<string, TimeData> WorldTimeDict = new Dictionary<string, TimeData>();
    
    [ShowInInspector]
    public Dictionary<string, float> SceneLightingRateDict = new Dictionary<string, float>();

    // 全局光源引用
    [Tooltip("全局光源引用")]
    public Light2D GlobalLight;
    
    // 默认光源设置
    [Tooltip("默认光源颜色")]
    public Color DefaultLightColor = Color.white;

    [Header("时间 -> 地块光照层")]
    [Tooltip("是否将昼夜光照同步到已加载地块的光照层")]
    public bool SyncTileLightLayer = true;

    [Tooltip("玩家可见/激活区块的光照层刷新间隔")]
    [Min(0.05f)]
    public float ActiveChunkLightRefreshInterval = 0.25f;

    [Tooltip("已实例化但失活区块的低频刷新间隔；未加载存档不更新")]
    [Min(0.1f)]
    public float InactiveChunkLightRefreshInterval = 5f;

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
        EnsureSceneTimeData(SceneManager.GetActiveScene().name, GameManager.Instance.ReadyTimeData);
    }

    private void OnGameWorldExit()
    {
        if (SaveDataMgr.Instance?.SaveData != null)
        {
            SaveDataMgr.Instance.SaveData.DayTimeData = GetSaveData();
        }

        WorldTimeDict?.Clear();
        SceneLightingRateDict?.Clear();
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
    if (!WorldTimeDict.TryGetValue(sceneName, out TimeData timeData))
        return;

    // 根据时间倍率推进时间
    float oldTime = timeData.CurrentTime;
    timeData.CurrentTime += deltaTime * timeData.TimeScaleModifier;
    
    // 处理时间溢出（超过一天时长则循环）
    if (timeData.CurrentTime >= timeData.DayLength)
    {
        int daysPassed = Mathf.FloorToInt(timeData.CurrentTime / timeData.DayLength);
        timeData.TotalDays += daysPassed;
        timeData.CurrentTime %= timeData.DayLength;
    }
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

        if (SyncTileLightLayer)
        {
            LightLayerMgr.Instance.SetTimeLighting(
                GlobalLight,
                intensity,
                color,
                ActiveChunkLightRefreshInterval,
                InactiveChunkLightRefreshInterval);
        }
    }

    private Color GetLightColor(string sceneName)
    {
        if (!WorldTimeDict.TryGetValue(sceneName, out var td))
            return DefaultLightColor;

        // 0-1 的昼夜进度
        float t = Mathf.Clamp01(td.CurrentTime / td.DayLength);
        return td.dayNightGradient.Evaluate(t);
    }
    #region 公共接口

    /// <summary>
    /// 获取场景当前时间
    /// </summary>
    public float GetCurrentTime(string sceneName)
    {
        if (!WorldTimeDict.TryGetValue(sceneName, out TimeData timeData))
            return 0f;

        // 如果该场景引用了其他场景，则返回被引用场景的时间
        if (!string.IsNullOrEmpty(timeData.ReferenceScene))
        {
            return GetCurrentTime(timeData.ReferenceScene);
        }

        return timeData.CurrentTime;
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
        }

        // 最终光照 = 基础光照 × 采光率
        return baseLightIntensity * lightingRate;
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
            timeData = new TimeData();
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
            timeData = new TimeData();
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
            timeData = new TimeData();
            WorldTimeDict[sceneName] = timeData;
        }

        timeData.CurrentTime = timeValue;
    }

    /// <summary>
    /// 设置光照依赖
    /// </summary>
    public void SetReferenceScene(string sceneName, string refName)
    {
        if (!WorldTimeDict.TryGetValue(sceneName, out TimeData timeData))
        {
            timeData = new TimeData();
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
            WorldTimeDict[sceneName] = new TimeData
            {
                DayLength = dayLength,
                TimeScaleModifier = timeScale,
                LightParams = TimeData.CreateDefaultLightCurve()
            };
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
            TimeData sourceTimeData = readyTimeData ?? new TimeData();
            WorldTimeDict[sceneName] = FastCloner.FastCloner.DeepClone(sourceTimeData);

            Debug.Log($"[DayTimeSystem] 未找到场景时间数据，已拷贝 ReadyTimeData：{sceneName}");
        }

        WorldTimeDict[sceneName].EnsureDarkNightWindow();

        if (!SceneLightingRateDict.ContainsKey(sceneName))
        {
            SceneLightingRateDict[sceneName] = 1.0f;
        }
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
    // ↓↓↓ 新增：把梯度颜色一起存进来
    public SerializableGradient DayNightGradient;

    [MemoryPackConstructor]
    public SerializableTimeData(float currentTime,
                                float dayLength,
                                SerializableKeyframe[] lightParamsKeys,
                                float timeScaleModifier,
                                string referenceScene,
                                SerializableGradient dayNightGradient)
    {
        CurrentTime = currentTime;
        DayLength = dayLength;
        LightParamsKeys = lightParamsKeys;
        TimeScaleModifier = timeScaleModifier;
        ReferenceScene = referenceScene ?? "";
        DayNightGradient = dayNightGradient;
    }

    // 从运行时 TimeData 抽数据
    public SerializableTimeData(TimeData timeData)
    {
        CurrentTime = timeData.CurrentTime;
        DayLength = timeData.DayLength;
        TimeScaleModifier = timeData.TimeScaleModifier;
        ReferenceScene = timeData.ReferenceScene ?? "";

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

        return new TimeData
        {
            CurrentTime = CurrentTime,
            DayLength = DayLength,
            LightParams = curve,
            TimeScaleModifier = TimeScaleModifier,
            ReferenceScene = ReferenceScene,
            dayNightGradient = DayNightGradient.ToGradient()// 字段赋值（内部用）
        };
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
