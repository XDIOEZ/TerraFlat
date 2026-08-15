using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

public static class TimeSystemConfigLoader
{
    #region 常量

    public const int SupportedSchemaVersion = 1;
    public const string RelativeTimeSystemRoot = "GameConfig/Time";
    public const string ConfigFileName = "time-system.json";
    public const string RelativeConfigPath = RelativeTimeSystemRoot + "/" + ConfigFileName;
    public const long MaximumConfigBytes = 1024 * 1024;

    #endregion

    #region 路径与设置

    private static readonly JsonSerializerSettings StrictJsonSettings = new JsonSerializerSettings
    {
        MissingMemberHandling = MissingMemberHandling.Error,
        DateParseHandling = DateParseHandling.None
    };

    public static string BuiltInConfigPath =>
        StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath, RelativeConfigPath);

    public static string UserOverrideConfigPath => Path.Combine(
        Application.persistentDataPath,
        "Configs",
        "TimeSystem",
        ConfigFileName);

    #endregion

    #region 加载

    public static TimeSystemConfigCatalog LoadBuiltIn()
    {
        return Deserialize(StreamingAssetsTextLoader.ReadAllText(BuiltInConfigPath));
    }

    public static IEnumerator LoadBuiltInAsync(
        Action<TimeSystemConfigCatalog> onCompleted,
        Action<Exception> onFailed)
    {
        string builtInJson = null;
        Exception readError = null;
        yield return StreamingAssetsTextLoader.ReadAllTextAsync(
            BuiltInConfigPath,
            text => builtInJson = text,
            exception => readError = exception);

        if (readError != null)
        {
            onFailed?.Invoke(readError);
            yield break;
        }

        TimeSystemConfigCatalog catalog;
        try
        {
            catalog = Deserialize(builtInJson);
        }
        catch (Exception exception)
        {
            onFailed?.Invoke(exception);
            yield break;
        }

        if (File.Exists(UserOverrideConfigPath))
        {
            try
            {
                string overrideJson = ReadUserOverrideText(UserOverrideConfigPath);
                catalog = Deserialize(overrideJson);
                Debug.Log($"[TimeSystemConfig] 已加载玩家覆盖配置：{UserOverrideConfigPath}");
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"[TimeSystemConfig] 玩家覆盖配置无效，将继续使用内建配置：{UserOverrideConfigPath}；{exception.Message}");
            }
        }

        onCompleted?.Invoke(catalog);
    }

    #endregion

    #region JSON 与校验

    public static TimeSystemConfigCatalog Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("时间系统 JSON 为空");

        TimeSystemConfigCatalog catalog = JsonConvert.DeserializeObject<TimeSystemConfigCatalog>(
            json,
            StrictJsonSettings);
        Validate(catalog);
        return catalog;
    }

    public static void Validate(TimeSystemConfigCatalog catalog)
    {
        if (catalog == null)
            throw new InvalidDataException("时间系统 JSON 根对象为空");
        if (catalog.SchemaVersion != SupportedSchemaVersion)
            throw new InvalidDataException(
                $"不支持的时间系统 schemaVersion：{catalog.SchemaVersion}");
        if (catalog.Profiles == null || catalog.Profiles.Count == 0)
            throw new InvalidDataException("时间系统至少需要一个 Profile");

        HashSet<string> profileIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < catalog.Profiles.Count; index++)
        {
            TimeSystemProfileConfig profile = catalog.Profiles[index];
            ValidateProfile(profile, index, profileIds);
        }

        if (string.IsNullOrWhiteSpace(catalog.DefaultProfileId) ||
            !profileIds.Contains(catalog.DefaultProfileId.Trim()))
        {
            throw new InvalidDataException(
                $"时间系统 defaultProfileId 不存在：{catalog.DefaultProfileId}");
        }

        catalog.DefaultProfileId = catalog.DefaultProfileId.Trim();
    }

    private static void ValidateProfile(
        TimeSystemProfileConfig profile,
        int index,
        ISet<string> profileIds)
    {
        if (profile == null)
            throw new InvalidDataException($"时间系统 Profile[{index}] 为空");

        profile.Id = profile.Id?.Trim();
        if (string.IsNullOrWhiteSpace(profile.Id))
            throw new InvalidDataException($"时间系统 Profile[{index}] 缺少 id");
        if (!profileIds.Add(profile.Id))
            throw new InvalidDataException($"时间系统包含重复 Profile ID：{profile.Id}");

        string rawMode = profile.Mode?.Trim();
        if (!TimeSystemModes.IsSupported(rawMode))
            throw new InvalidDataException($"时间系统 Profile {profile.Id} 的 mode 不受支持：{profile.Mode}");
        profile.Mode = TimeSystemModes.Normalize(rawMode);
        if (!IsFinite(profile.TimeScale) || profile.TimeScale < 0f)
            throw new InvalidDataException($"时间系统 Profile {profile.Id} 的 timeScale 无效：{profile.TimeScale}");
        if (!IsFinite(profile.DayLength) || profile.DayLength <= 0f)
            throw new InvalidDataException($"时间系统 Profile {profile.Id} 的 dayLength 无效：{profile.DayLength}");
        if (!IsFinite(profile.InitialTime))
            throw new InvalidDataException($"时间系统 Profile {profile.Id} 的 initialTime 无效：{profile.InitialTime}");
        if (profile.InitialTotalDays < 0)
            throw new InvalidDataException($"时间系统 Profile {profile.Id} 的 initialTotalDays 不能为负数");
        if (!IsFinite(profile.TimeLimitDays) || profile.TimeLimitDays < 0f)
            throw new InvalidDataException($"时间系统 Profile {profile.Id} 的 timeLimitDays 无效：{profile.TimeLimitDays}");
        if (profile.Mode == TimeSystemModes.TimeLimited && profile.TimeLimitDays <= 0f)
            throw new InvalidDataException($"限时 Profile {profile.Id} 必须配置大于 0 的 timeLimitDays");

        ValidateLightCurve(profile);
        ValidateGradient(profile);
        ValidateMoon(profile);
        ValidatePresentation(profile);
    }

    private static void ValidateLightCurve(TimeSystemProfileConfig profile)
    {
        if (profile.LightCurve == null || profile.LightCurve.Count < 2)
            throw new InvalidDataException($"时间系统 Profile {profile.Id} 的 lightCurve 至少需要 2 个关键帧");

        float previousTime = -1f;
        for (int index = 0; index < profile.LightCurve.Count; index++)
        {
            TimeSystemCurveKeyConfig key = profile.LightCurve[index];
            if (key == null ||
                !IsFinite(key.Time) ||
                !IsFinite(key.Value) ||
                !IsFinite(key.InTangent) ||
                !IsFinite(key.OutTangent) ||
                key.Time < 0f ||
                key.Time > 1f ||
                key.Time < previousTime)
            {
                throw new InvalidDataException($"时间系统 Profile {profile.Id} 的 lightCurve[{index}] 无效");
            }

            previousTime = key.Time;
        }
    }

    private static void ValidateGradient(TimeSystemProfileConfig profile)
    {
        if (profile.DayNightGradient == null ||
            profile.DayNightGradient.ColorKeys == null ||
            profile.DayNightGradient.ColorKeys.Count < 2)
        {
            throw new InvalidDataException($"时间系统 Profile {profile.Id} 的 dayNightGradient 至少需要 2 个颜色关键帧");
        }

        ValidateGradientColorKeys(profile);
        ValidateGradientAlphaKeys(profile);
    }

    private static void ValidateGradientColorKeys(TimeSystemProfileConfig profile)
    {
        float previousTime = -1f;
        for (int index = 0; index < profile.DayNightGradient.ColorKeys.Count; index++)
        {
            TimeSystemGradientColorKeyConfig key = profile.DayNightGradient.ColorKeys[index];
            if (key == null || key.Color == null || !IsFinite(key.Time) || key.Time < 0f || key.Time > 1f || key.Time < previousTime)
                throw new InvalidDataException($"时间系统 Profile {profile.Id} 的 dayNightGradient.colorKeys[{index}] 无效");

            ValidateColor(key.Color, $"{profile.Id}.dayNightGradient.colorKeys[{index}]");
            previousTime = key.Time;
        }
    }

    private static void ValidateGradientAlphaKeys(TimeSystemProfileConfig profile)
    {
        if (profile.DayNightGradient.AlphaKeys == null || profile.DayNightGradient.AlphaKeys.Count == 0)
            return;

        float previousTime = -1f;
        for (int index = 0; index < profile.DayNightGradient.AlphaKeys.Count; index++)
        {
            TimeSystemGradientAlphaKeyConfig key = profile.DayNightGradient.AlphaKeys[index];
            if (key == null ||
                !IsFinite(key.Alpha) ||
                !IsFinite(key.Time) ||
                key.Alpha < 0f ||
                key.Alpha > 1f ||
                key.Time < 0f ||
                key.Time > 1f ||
                key.Time < previousTime)
            {
                throw new InvalidDataException($"时间系统 Profile {profile.Id} 的 dayNightGradient.alphaKeys[{index}] 无效");
            }

            previousTime = key.Time;
        }
    }

    private static void ValidateMoon(TimeSystemProfileConfig profile)
    {
        TimeSystemMoonConfig moon = profile.Moon;
        if (moon == null ||
            !IsFinite(moon.CycleDays) ||
            moon.CycleDays <= 0f ||
            !IsFinite(moon.NewMoonNightIntensity) ||
            moon.NewMoonNightIntensity < 0f ||
            moon.NewMoonNightIntensity > 1f ||
            !IsFinite(moon.FullMoonNightIntensity) ||
            moon.FullMoonNightIntensity < 0f ||
            moon.FullMoonNightIntensity > 1f ||
            !IsFinite(moon.InitialPhase) ||
            moon.InitialPhase < 0f ||
            moon.InitialPhase > 1f)
        {
            throw new InvalidDataException($"时间系统 Profile {profile.Id} 的 moon 配置无效");
        }
    }

    private static void ValidatePresentation(TimeSystemProfileConfig profile)
    {
        TimeSystemPresentationConfig presentation = profile.Presentation;
        if (presentation == null || presentation.DefaultLightColor == null)
            throw new InvalidDataException($"时间系统 Profile {profile.Id} 缺少 presentation 配置");
        ValidateColor(presentation.DefaultLightColor, $"{profile.Id}.presentation.defaultLightColor");
        if (!IsFinite(presentation.ActiveChunkLightRefreshInterval) || presentation.ActiveChunkLightRefreshInterval <= 0f)
            throw new InvalidDataException($"时间系统 Profile {profile.Id} 的 activeChunkLightRefreshInterval 无效");
        if (!IsFinite(presentation.InactiveChunkLightRefreshInterval) || presentation.InactiveChunkLightRefreshInterval <= 0f)
            throw new InvalidDataException($"时间系统 Profile {profile.Id} 的 inactiveChunkLightRefreshInterval 无效");
    }

    private static void ValidateColor(TimeSystemColorConfig color, string fieldName)
    {
        if (!IsFinite(color.R) || color.R < 0f || color.R > 1f ||
            !IsFinite(color.G) || color.G < 0f || color.G > 1f ||
            !IsFinite(color.B) || color.B < 0f || color.B > 1f ||
            !IsFinite(color.A) || color.A < 0f || color.A > 1f)
        {
            throw new InvalidDataException($"颜色配置无效：{fieldName}");
        }
    }

    private static string ReadUserOverrideText(string path)
    {
        FileInfo fileInfo = new FileInfo(path);
        if (fileInfo.Length > MaximumConfigBytes)
            throw new InvalidDataException($"时间系统玩家配置超过大小限制：{fileInfo.Length} bytes");

        return File.ReadAllText(path);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    #endregion
}
