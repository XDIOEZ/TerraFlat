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

    [Tooltip("创建该世界时使用的 JSON 时间系统 Profile ID")]
    public string TimeSystemProfileId = "standard";

    [Tooltip("时间系统模式")]
    public string TimeSystemMode = TimeSystemModes.Unlimited;

    [Tooltip("限时模式的绝对结束游戏时间；0 表示不限时")]
    public float TimeLimitTotalGameTime = 0f;

    [Tooltip("月相完整周期对应的游戏天数")]
    public float LunarCycleDays = 29.53f;

    [Tooltip("新月时的夜间全局光照强度")]
    public float NewMoonNightIntensity = 0.035f;

    [Tooltip("满月时的夜间全局光照强度")]
    public float FullMoonNightIntensity = 0.18f;

    [Tooltip("新世界第 0 天的月相位置")]
    public float InitialMoonPhase = 0.5f;
    
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
            TotalDays = TotalDays,
            TimeSystemProfileId = TimeSystemProfileId,
            TimeSystemMode = TimeSystemMode,
            TimeLimitTotalGameTime = TimeLimitTotalGameTime,
            LunarCycleDays = LunarCycleDays,
            NewMoonNightIntensity = NewMoonNightIntensity,
            FullMoonNightIntensity = FullMoonNightIntensity,
            InitialMoonPhase = InitialMoonPhase
        };
    }

    public void EnsureTimeSystemDefaults()
    {
        if (string.IsNullOrWhiteSpace(TimeSystemProfileId))
            TimeSystemProfileId = "standard";

        if (!TimeSystemModes.IsSupported(TimeSystemMode))
            TimeSystemMode = TimeSystemModes.Unlimited;
        else
            TimeSystemMode = TimeSystemModes.Normalize(TimeSystemMode);

        if (float.IsNaN(TimeLimitTotalGameTime) ||
            float.IsInfinity(TimeLimitTotalGameTime) ||
            TimeLimitTotalGameTime < 0f)
        {
            TimeLimitTotalGameTime = 0f;
        }

        if (float.IsNaN(LunarCycleDays) || float.IsInfinity(LunarCycleDays) || LunarCycleDays <= 0f)
            LunarCycleDays = 29.53f;
        if (float.IsNaN(NewMoonNightIntensity) || float.IsInfinity(NewMoonNightIntensity))
            NewMoonNightIntensity = 0.035f;
        if (float.IsNaN(FullMoonNightIntensity) || float.IsInfinity(FullMoonNightIntensity))
            FullMoonNightIntensity = 0.18f;
        if (float.IsNaN(InitialMoonPhase) || float.IsInfinity(InitialMoonPhase))
            InitialMoonPhase = 0.5f;

        NewMoonNightIntensity = Mathf.Clamp01(NewMoonNightIntensity);
        FullMoonNightIntensity = Mathf.Clamp01(FullMoonNightIntensity);
        InitialMoonPhase = Mathf.Repeat(InitialMoonPhase, 1f);
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
        // 默认完全黑夜占一天的20%，两端各10%；日出和日落各占10%。
        return new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.10f, 0f),
            new Keyframe(0.20f, 1f),
            new Keyframe(0.5f, 1f),
            new Keyframe(0.80f, 1f),
            new Keyframe(0.90f, 0f),
            new Keyframe(1f, 0f));
    }

    public void EnsureDarkNightWindow()
    {
        if (LightParams == null || LightParams.length == 0)
            LightParams = CreateDefaultLightCurve();
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
