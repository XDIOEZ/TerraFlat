using FlatWorld.WorldModel;
using UnityEngine;

public static class ChunkWeatherConfiguration
{
    #region 冻结天气配置

    private const string ResourcePath = "Config/Weather/regional-weather";
    private static RegionalWeatherSettings configuration;

    // 生成器和天气调度共用同一份配置，后台只持有纯数值快照。
    public static RegionalWeatherSettings Load()
    {
        if (configuration == null)
            configuration = LoadSourceConfiguration();
        return configuration.Snapshot();
    }

    [BuiltInContentBake]
    public static void BakeBuiltInConfiguration() => _ = LoadSourceConfiguration();

    private static RegionalWeatherSettings LoadSourceConfiguration()
    {
        RegionalWeatherSettings loaded = BuiltInContentBinaryCatalog.ReadResource(
            ResourcePath, ParseResourceConfiguration);
        if (loaded == null)
            throw new MissingReferenceException("缺少 Config/Weather/regional-weather 区块天气配置。");
        SnowCoverConfig snow = Resources.Load<SnowCoverConfig>("Weather/SnowCoverConfig");
        if (snow == null)
            throw new MissingReferenceException("区块天气或积雪配置不完整。");
        loaded.FreezingTemperature = snow.FreezingTemperature;
        return loaded.Snapshot();
    }

    private static RegionalWeatherSettings ParseResourceConfiguration(string json)
        => JsonConfigurationCache.FromUnityJson<RegionalWeatherSettings>(json)
           ?? throw new MissingReferenceException("区块天气或积雪配置不完整。");

    #endregion
}
