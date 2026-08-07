using MemoryPack;
using UnityEngine;

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
    public AnimationCurve LightParams = CreateDefaultLightCurve();

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
    /// 创建运行时安全副本。AnimationCurve 和 Gradient 都持有 Unity 原生资源，
    /// 不能交给通用反射深拷贝，否则多个托管包装器可能重复释放同一原生指针。
    /// </summary>
    public TimeData CreateRuntimeCopy()
    {
        return new TimeData
        {
            CurrentTime = CurrentTime,
            DayLength = DayLength,
            LightParams = CopyAnimationCurve(LightParams),
            dayNightGradient = CopyGradient(dayNightGradient),
            TimeScaleModifier = TimeScaleModifier,
            ReferenceScene = ReferenceScene,
            TotalDays = TotalDays
        };
    }

    private static AnimationCurve CopyAnimationCurve(AnimationCurve source)
    {
        if (source == null)
            return null;

        return new AnimationCurve(source.keys)
        {
            preWrapMode = source.preWrapMode,
            postWrapMode = source.postWrapMode
        };
    }

    private static Gradient CopyGradient(Gradient source)
    {
        if (source == null)
            return null;

        var copy = new Gradient
        {
            mode = source.mode
        };
        copy.SetKeys(source.colorKeys, source.alphaKeys);
        return copy;
    }
    
    /// <summary>
    /// 获取当前天数（基于当前时间计算）
    /// </summary>
    public int GetCurrentDay()
    {
        return Mathf.FloorToInt(CurrentTime / DayLength) + TotalDays;
    }

    public static AnimationCurve CreateDefaultLightCurve()
    {
        return new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.15f, 0f),
            new Keyframe(0.25f, 1f),
            new Keyframe(0.5f, 1f),
            new Keyframe(0.75f, 1f),
            new Keyframe(0.85f, 0f),
            new Keyframe(1f, 0f));
    }

    public void EnsureDarkNightWindow()
    {
        if (LightParams == null || LightParams.length == 0 || IsLegacyDefaultLightCurve())
            LightParams = CreateDefaultLightCurve();
    }

    private bool IsLegacyDefaultLightCurve()
    {
        Keyframe[] keys = LightParams.keys;
        if (keys.Length != 5)
            return false;

        return Mathf.Approximately(keys[0].time, 0f) &&
               Mathf.Approximately(keys[1].time, 0.25f) &&
               Mathf.Approximately(keys[2].time, 0.5f) &&
               Mathf.Approximately(keys[3].time, 0.75f) &&
               Mathf.Approximately(keys[4].time, 1f) &&
               keys[0].value <= 0.2001f &&
               Mathf.Approximately(keys[1].value, 1f) &&
               Mathf.Approximately(keys[2].value, 1f) &&
               Mathf.Approximately(keys[3].value, 1f) &&
               keys[4].value <= 0.2001f;
    }

    /// <summary>
    /// 获取总游戏时间（单位：秒）
    /// = 总天数 * 一天时长 + 当前天内经过时间
    /// </summary>
    public float GetTotalGameTime()
    {
        float currentTimeInDay = CurrentTime % DayLength;
        if (currentTimeInDay < 0f)
            currentTimeInDay += DayLength;

        return Mathf.Max(0, TotalDays) * DayLength + currentTimeInDay;
    }
}
