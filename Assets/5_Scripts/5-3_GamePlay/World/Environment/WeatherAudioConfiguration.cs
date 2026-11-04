using System;
using UnityEngine;

/// <summary>天气声音的调音参数独立于生成规则，由地表和洞口表现共用。</summary>
[Serializable, MemoryPack.MemoryPackable]
public sealed partial class WeatherAudioConfiguration
{
    #region JSON 天气声音配置

    private const string ResourcePath = "Config/Weather/weather-audio";
    private static WeatherAudioConfiguration configuration;

    public float RainMaximumVolume;
    public float WindStartStrength;
    public float WindMaximumVolume;
    public float WindVolumeExponent;
    public float WindVolumeResponsePerSecond;
    public float WindCrossFadeSeconds;
    public float ThunderNearRadius;
    public float ThunderPropagationSpeed;

    public static WeatherAudioConfiguration Load() => configuration ??= ReadSourceConfiguration();

    [BuiltInContentBake]
    public static void BakeBuiltInConfiguration() => _ = ReadSourceConfiguration();

    private static WeatherAudioConfiguration ReadSourceConfiguration()
        => BuiltInContentBinaryCatalog.ReadResource(ResourcePath, Parse)
           ?? throw new MissingReferenceException("缺少 Config/Weather/weather-audio 天气声音配置。");

    internal static WeatherAudioConfiguration Parse(string json)
    {
        WeatherAudioConfiguration loaded = JsonConfigurationCache.FromUnityJson<WeatherAudioConfiguration>(json);
        if (!float.IsFinite(loaded.RainMaximumVolume) || !float.IsFinite(loaded.WindStartStrength) ||
            !float.IsFinite(loaded.WindMaximumVolume) || !PositiveFinite(loaded.WindVolumeExponent) ||
            !PositiveFinite(loaded.WindCrossFadeSeconds) || !PositiveFinite(loaded.ThunderPropagationSpeed) ||
            !float.IsFinite(loaded.ThunderNearRadius) || !float.IsFinite(loaded.WindVolumeResponsePerSecond) ||
            loaded.WindVolumeResponsePerSecond < 0f)
            throw new ArgumentOutOfRangeException(nameof(json), "天气声音参数须为有限数；幂次、淡化时长和传播速度须为正，音量响应速率不得为负。");
        return loaded;
    }

    private static bool PositiveFinite(float value) => value > 0f && float.IsFinite(value);

    #endregion
}
