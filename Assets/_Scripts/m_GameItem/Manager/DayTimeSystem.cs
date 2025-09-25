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


    private void Update()
    {
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
        // 这里应该根据你的场景管理系统来实现
        // 比如从SceneManager获取当前场景名
        // 暂时返回第一个场景作为示例

        return SceneManager.GetActiveScene().name;

        //foreach (var sceneName in WorldTimeDict.Keys)
        //{
        //    return sceneName;
        //}
        //return string.Empty;
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
                LightParams = new AnimationCurve(
                    new Keyframe(0f, 0.2f),    // 夜晚
                    new Keyframe(0.25f, 1.0f), // 日出
                    new Keyframe(0.5f, 1.0f),  // 正午
                    new Keyframe(0.75f, 1.0f), // 日落
                    new Keyframe(1f, 0.2f)     // 夜晚
                )
            };
        }

        // 默认采光率为1.0（完全采光）
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
            WorldTimeDict[kvp.Key] = kvp.Value.ToTimeData();
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

[MemoryPackable]
[System.Serializable]
public partial class TimeData
{
    [Tooltip("当前时间点（单位/秒）")]
    public float CurrentTime = 0f;

    [Tooltip("一天时长（单位/秒）")]
    public float DayLength = 1440f;

    [Tooltip("光照参数曲线（时间比例到光照强度）")]
    [MemoryPackIgnore]
    public AnimationCurve LightParams = new AnimationCurve(
        new Keyframe(0f, 0.2f),
        new Keyframe(0.25f, 1.0f),
        new Keyframe(0.5f, 1.0f),
        new Keyframe(0.75f, 1.0f),
        new Keyframe(1f, 0.2f)
    );

    [Tooltip("昼夜颜色梯度（存档安全）")]
    [MemoryPackIgnore]
    public Gradient dayNightGradient = new Gradient()
    {
        colorKeys = new[]
                {
                new GradientColorKey(new Color32(30,40,90,255), 0.00f),
                new GradientColorKey(new Color32(70,50,100,255), 0.25f),
                new GradientColorKey(new Color32(255,245,230,255), 0.50f),
                new GradientColorKey(new Color32(255,150,80,255), 0.75f),
                new GradientColorKey(new Color32(30,40,90,255), 1.00f)
                },
        alphaKeys = new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) }
    };

    [Tooltip("时间倍率（支持外部事件影响）")]
    public float TimeScaleModifier = 1f;

    [Tooltip("引用场景名（如果需要引用其他场景的时间/光照）")]
    public string ReferenceScene = "";
    
    [Tooltip("总游戏天数（记录游玩了多少天）")]
    public int TotalDays = 0;
    
    public TimeData() { }
    
    /// <summary>
    /// 获取当前天数（基于当前时间计算）
    /// </summary>
    public int GetCurrentDay()
    {
        return Mathf.FloorToInt(CurrentTime / DayLength) + TotalDays;
    }
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