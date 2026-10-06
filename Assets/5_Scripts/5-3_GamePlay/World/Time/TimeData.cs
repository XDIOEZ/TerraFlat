using MemoryPack;
using UnityEngine;

[MemoryPackable]
[System.Serializable]
public partial class TimeData
{
    #region 物理周期与日历
    public const float DefaultRotationPeriodSeconds = 1440f;
    public const float DefaultOrbitalPeriodSeconds = DefaultRotationPeriodSeconds * 24f;
    public const float DefaultAxialTiltDegrees = 23.44f;
    public const float DefaultOrbitalEccentricity = 0f;

    [Tooltip("当前时间点（单位/秒）")]
    public float CurrentTime = 0f;

    public float RotationPeriodSeconds { get; private set; } = DefaultRotationPeriodSeconds;

    public float OrbitalPeriodSeconds { get; private set; } = DefaultOrbitalPeriodSeconds;

    [Tooltip("星球自转轴相对公转轨道法线的倾角；控制季节温差强弱与极区放大")]
    public float AxialTiltDegrees = DefaultAxialTiltDegrees;

    [Tooltip("椭圆轨道离心率；0 为圆轨道，越接近 1 远近日点差异越强")]
    public float OrbitalEccentricity = DefaultOrbitalEccentricity;

    [MemoryPackIgnore]
    public float DayLength => RotationPeriodSeconds;

    [MemoryPackIgnore]
    public double YearLengthDays => (double)OrbitalPeriodSeconds / RotationPeriodSeconds;

    [MemoryPackIgnore]
    public double OrbitalCycles => System.Math.Max(0d, (GetTotalGameTimeSeconds() + OrbitalOffsetSeconds) / OrbitalPeriodSeconds);

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

    [Tooltip("随世界保存的四季温差；季长只由物理周期派生")]
    public SeasonCycleSettings Seasons = new();

    public double OrbitalOffsetSeconds; // 修改公转周期时保持当前年份与公转进度的同轴起点。
    public System.Collections.Generic.List<SeasonCalendarHistoryEntry> SeasonHistory = new(); // 物理参数修改前的秒制历史。

    /// <summary>创建与读档共用物理参数入口，不保存派生日长或年长。</summary>
    internal void InitializePhysicalPeriods(float rotation, float orbital)
    {
        ValidatePeriod(rotation);
        ValidatePeriod(orbital);
        RotationPeriodSeconds = rotation;
        OrbitalPeriodSeconds = orbital;
        Seasons.ApplyOrbitalDurations(YearLengthDays, OrbitalEccentricity);
    }

    /// <summary>改变日长时保持绝对游戏秒数及公转进度，重新换算已完成的自转圈数。</summary>
    public void SetRotationPeriod(float seconds)
    {
        ValidatePeriod(seconds);
        if (seconds == RotationPeriodSeconds) return;
        double now = GetTotalGameTimeSeconds();
        SeasonCalendar.RecordHistory(this, now);
        RotationPeriodSeconds = seconds;
        SetTotalGameTimeSeconds(now);
        Seasons.ApplyOrbitalDurations(YearLengthDays, OrbitalEccentricity);
    }

    /// <summary>改变年长时保持绝对秒数、当前年份和公转进度，一天长度不变。</summary>
    public void SetOrbitalPeriod(float seconds)
    {
        ValidatePeriod(seconds);
        if (seconds == OrbitalPeriodSeconds) return;
        double now = GetTotalGameTimeSeconds();
        double cycles = OrbitalCycles;
        SeasonCalendar.RecordHistory(this, now);
        OrbitalPeriodSeconds = seconds;
        OrbitalOffsetSeconds = cycles * seconds - now;
        Seasons.ApplyOrbitalDurations(YearLengthDays, OrbitalEccentricity);
    }

    private static void ValidatePeriod(float seconds)
    {
        if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 1f)
            throw new System.ArgumentOutOfRangeException(nameof(seconds), "物理周期必须为至少 1 的有限游戏秒数。");
    }

    /// <summary>用同一秒制时间轴推进和跳时，避免大跨度跳时丢掉日内余数。</summary>
    public void SetTotalGameTimeSeconds(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0d || seconds / DayLength >= int.MaxValue)
            throw new System.ArgumentOutOfRangeException(nameof(seconds));
        TotalDays = (int)System.Math.Floor(seconds / DayLength);
        CurrentTime = (float)(seconds - (double)TotalDays * DayLength);
        if (CurrentTime >= DayLength) { TotalDays++; CurrentTime = 0f; }
    }

    public double GetTotalGameTimeSeconds() => System.Math.Max(0, TotalDays) * (double)DayLength + CurrentTime;

    #endregion

    #region 运行时复制与合法化
    
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
            RotationPeriodSeconds = RotationPeriodSeconds,
            OrbitalPeriodSeconds = OrbitalPeriodSeconds,
            AxialTiltDegrees = AxialTiltDegrees,
            OrbitalEccentricity = OrbitalEccentricity,
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
            InitialMoonPhase = InitialMoonPhase,
            Seasons = Seasons.Copy(),
            OrbitalOffsetSeconds = OrbitalOffsetSeconds,
            SeasonHistory = SeasonCalendar.CopyHistory(SeasonHistory)
        };
    }

    public void EnsureTimeSystemDefaults()
    {
        if (float.IsNaN(RotationPeriodSeconds) || float.IsInfinity(RotationPeriodSeconds) || RotationPeriodSeconds < 1f)
            RotationPeriodSeconds = DefaultRotationPeriodSeconds;
        if (float.IsNaN(OrbitalPeriodSeconds) || float.IsInfinity(OrbitalPeriodSeconds) || OrbitalPeriodSeconds < 1f)
            OrbitalPeriodSeconds = DefaultOrbitalPeriodSeconds;
        if (float.IsNaN(AxialTiltDegrees) || float.IsInfinity(AxialTiltDegrees) || AxialTiltDegrees < 0f || AxialTiltDegrees > 90f)
            AxialTiltDegrees = DefaultAxialTiltDegrees;
        if (float.IsNaN(OrbitalEccentricity) || float.IsInfinity(OrbitalEccentricity) || OrbitalEccentricity < 0f || OrbitalEccentricity >= 1f)
            OrbitalEccentricity = DefaultOrbitalEccentricity;

        CurrentTime = Mathf.Repeat(CurrentTime, RotationPeriodSeconds);
        Seasons ??= new SeasonCycleSettings();
        Seasons.ApplyOrbitalDurations(YearLengthDays, OrbitalEccentricity);

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
        return (float)GetTotalGameTimeSeconds();
    }
    #endregion
}
