using System;
using System.Collections.Generic;
using UnityEngine;

public static class TimeSystemModes
{
    public const string Unlimited = "unlimited";
    public const string TimeLimited = "timeLimited";

    public static bool IsSupported(string mode)
    {
        return string.Equals(mode, Unlimited, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(mode, TimeLimited, StringComparison.OrdinalIgnoreCase);
    }

    public static string Normalize(string mode)
    {
        return string.Equals(mode, TimeLimited, StringComparison.OrdinalIgnoreCase)
            ? TimeLimited
            : Unlimited;
    }
}

[Serializable]
public sealed class TimeSystemConfigCatalog
{
    #region 配置字段

    public int SchemaVersion;
    public string DefaultProfileId;
    public List<TimeSystemProfileConfig> Profiles = new();

    #endregion
}

[Serializable]
public sealed class TimeSystemProfileConfig
{
    #region 配置字段

    public string Id;
    public string DisplayName;
    public string Description;
    public string Mode = TimeSystemModes.Unlimited;
    public float TimeScale = 1f;
    public float DayLength = 1440f;
    public float InitialTime = 360f;
    public int InitialTotalDays;
    public float TimeLimitDays;
    public string ReferenceScene = string.Empty;
    public List<TimeSystemCurveKeyConfig> LightCurve = new();
    public TimeSystemGradientConfig DayNightGradient = new();
    public TimeSystemMoonConfig Moon = new();
    public TimeSystemPresentationConfig Presentation = new();

    #endregion

    #region 运行时转换

    public TimeData CreateTimeData()
    {
        float dayLength = Mathf.Max(1f, DayLength);
        string normalizedMode = TimeSystemModes.Normalize(Mode);
        TimeData timeData = new TimeData
        {
            CurrentTime = Mathf.Repeat(InitialTime, dayLength),
            DayLength = dayLength,
            LightParams = CreateLightCurve(),
            dayNightGradient = CreateGradient(),
            TimeScaleModifier = Mathf.Max(0f, TimeScale),
            ReferenceScene = ReferenceScene?.Trim() ?? string.Empty,
            TotalDays = Mathf.Max(0, InitialTotalDays),
            TimeSystemProfileId = Id?.Trim() ?? string.Empty,
            TimeSystemMode = normalizedMode,
            LunarCycleDays = Mathf.Max(1f, Moon?.CycleDays ?? 29.53f),
            NewMoonNightIntensity = Mathf.Clamp01(Moon?.NewMoonNightIntensity ?? 0.035f),
            FullMoonNightIntensity = Mathf.Clamp01(Moon?.FullMoonNightIntensity ?? 0.18f),
            InitialMoonPhase = Mathf.Repeat(Moon?.InitialPhase ?? 0.5f, 1f)
        };

        timeData.EnsureTimeSystemDefaults();
        if (normalizedMode == TimeSystemModes.TimeLimited && TimeLimitDays > 0f)
        {
            timeData.TimeLimitTotalGameTime =
                timeData.GetTotalGameTime() + TimeLimitDays * timeData.DayLength;
        }

        return timeData;
    }

    public TimeSystemPresentationSettings CreatePresentationSettings()
    {
        TimeSystemPresentationConfig config = Presentation ?? new TimeSystemPresentationConfig();
        return new TimeSystemPresentationSettings
        {
            DefaultLightColor = config.DefaultLightColor?.ToColor() ?? Color.white,
            SyncTileLightLayer = config.SyncTileLightLayer,
            ActiveChunkLightRefreshInterval = Mathf.Max(0.05f, config.ActiveChunkLightRefreshInterval),
            InactiveChunkLightRefreshInterval = Mathf.Max(0.1f, config.InactiveChunkLightRefreshInterval)
        };
    }

    private AnimationCurve CreateLightCurve()
    {
        if (LightCurve == null || LightCurve.Count == 0)
            return TimeData.CreateDefaultLightCurve();

        Keyframe[] keys = new Keyframe[LightCurve.Count];
        for (int index = 0; index < LightCurve.Count; index++)
        {
            TimeSystemCurveKeyConfig key = LightCurve[index];
            keys[index] = new Keyframe(
                key.Time,
                key.Value,
                key.InTangent,
                key.OutTangent);
        }

        return new AnimationCurve(keys)
        {
            preWrapMode = WrapMode.Clamp,
            postWrapMode = WrapMode.Clamp
        };
    }

    private Gradient CreateGradient()
    {
        TimeSystemGradientConfig config = DayNightGradient;
        if (config == null || config.ColorKeys == null || config.ColorKeys.Count == 0)
            return new TimeData().dayNightGradient;

        GradientColorKey[] colorKeys = new GradientColorKey[config.ColorKeys.Count];
        for (int index = 0; index < config.ColorKeys.Count; index++)
        {
            TimeSystemGradientColorKeyConfig key = config.ColorKeys[index];
            colorKeys[index] = new GradientColorKey(key.Color.ToColor(), key.Time);
        }

        List<TimeSystemGradientAlphaKeyConfig> alphaConfig = config.AlphaKeys;
        GradientAlphaKey[] alphaKeys;
        if (alphaConfig == null || alphaConfig.Count == 0)
        {
            alphaKeys = new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f)
            };
        }
        else
        {
            alphaKeys = new GradientAlphaKey[alphaConfig.Count];
            for (int index = 0; index < alphaConfig.Count; index++)
            {
                TimeSystemGradientAlphaKeyConfig key = alphaConfig[index];
                alphaKeys[index] = new GradientAlphaKey(key.Alpha, key.Time);
            }
        }

        Gradient gradient = new Gradient();
        gradient.SetKeys(colorKeys, alphaKeys);
        return gradient;
    }

    #endregion
}

[Serializable]
public sealed class TimeSystemCurveKeyConfig
{
    public float Time;
    public float Value;
    public float InTangent;
    public float OutTangent;
}

[Serializable]
public sealed class TimeSystemGradientConfig
{
    public List<TimeSystemGradientColorKeyConfig> ColorKeys = new();
    public List<TimeSystemGradientAlphaKeyConfig> AlphaKeys = new();
}

[Serializable]
public sealed class TimeSystemGradientColorKeyConfig
{
    public TimeSystemColorConfig Color = new();
    public float Time;
}

[Serializable]
public sealed class TimeSystemGradientAlphaKeyConfig
{
    public float Alpha = 1f;
    public float Time;
}

[Serializable]
public sealed class TimeSystemMoonConfig
{
    public float CycleDays = 29.53f;
    public float NewMoonNightIntensity = 0.035f;
    public float FullMoonNightIntensity = 0.18f;
    public float InitialPhase = 0.5f;
}

[Serializable]
public sealed class TimeSystemPresentationConfig
{
    public TimeSystemColorConfig DefaultLightColor = new() { R = 1f, G = 1f, B = 1f, A = 1f };
    public bool SyncTileLightLayer = true;
    public float ActiveChunkLightRefreshInterval = 0.25f;
    public float InactiveChunkLightRefreshInterval = 5f;
}

[Serializable]
public sealed class TimeSystemColorConfig
{
    public float R = 1f;
    public float G = 1f;
    public float B = 1f;
    public float A = 1f;

    public Color ToColor()
    {
        return new Color(R, G, B, A);
    }
}

public sealed class TimeSystemPresentationSettings
{
    public Color DefaultLightColor = Color.white;
    public bool SyncTileLightLayer = true;
    public float ActiveChunkLightRefreshInterval = 0.25f;
    public float InactiveChunkLightRefreshInterval = 5f;

    public static TimeSystemPresentationSettings CreateDefault()
    {
        return new TimeSystemPresentationSettings();
    }
}

public static class TimeSystemConfigService
{
    #region 状态

    public static TimeSystemConfigCatalog Catalog { get; private set; }
    public static int Version { get; private set; }
    public static bool IsLoaded => Catalog != null;
    public static string DefaultProfileId => Catalog?.DefaultProfileId ?? string.Empty;
    public static IReadOnlyList<TimeSystemProfileConfig> Profiles =>
        Catalog != null
            ? Catalog.Profiles
            : Array.Empty<TimeSystemProfileConfig>();

    #endregion

    #region 公共接口

    public static void ReplaceCatalog(TimeSystemConfigCatalog catalog)
    {
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        Version++;
    }

    public static bool TryGetProfile(string profileId, out TimeSystemProfileConfig profile)
    {
        profile = null;
        if (Catalog?.Profiles == null || Catalog.Profiles.Count == 0)
            return false;

        string targetId = string.IsNullOrWhiteSpace(profileId)
            ? Catalog.DefaultProfileId
            : profileId.Trim();
        for (int index = 0; index < Catalog.Profiles.Count; index++)
        {
            TimeSystemProfileConfig candidate = Catalog.Profiles[index];
            if (candidate != null && string.Equals(candidate.Id, targetId, StringComparison.OrdinalIgnoreCase))
            {
                profile = candidate;
                return true;
            }
        }

        return false;
    }

    public static bool TryCreateTimeData(
        string profileId,
        out TimeData timeData,
        out string error)
    {
        timeData = null;
        error = string.Empty;
        if (!TryGetProfile(profileId, out TimeSystemProfileConfig profile))
        {
            error = $"找不到时间系统配置 Profile：{profileId}";
            return false;
        }

        timeData = profile.CreateTimeData();
        return true;
    }

    public static bool TryCreateDefaultTimeData(out TimeData timeData, out string error)
    {
        return TryCreateTimeData(DefaultProfileId, out timeData, out error);
    }

    public static bool TryGetPresentationSettings(
        string profileId,
        out TimeSystemPresentationSettings settings)
    {
        settings = null;
        if (!TryGetProfile(profileId, out TimeSystemProfileConfig profile))
            return false;

        settings = profile.CreatePresentationSettings();
        return true;
    }

    #endregion
}
