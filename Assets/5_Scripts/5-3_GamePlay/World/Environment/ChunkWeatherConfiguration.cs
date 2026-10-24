using FlatWorld.WorldModel;
using UnityEngine;

public static class ChunkWeatherConfiguration
{
    #region 冻结天气配置

    private static RegionalWeatherSettings configuration;

    // 生成器和天气调度共用同一份配置，后台只持有纯数值快照。
    public static RegionalWeatherSettings Load()
    {
        if (configuration == null)
        {
            TextAsset asset = Resources.Load<TextAsset>("Config/Weather/regional-weather");
            if (asset == null)
                throw new MissingReferenceException("缺少 Config/Weather/regional-weather 区块天气配置。");
            RegionalWeatherSettings loaded = JsonConfigurationCache.FromUnityJson<RegionalWeatherSettings>(asset.text);
            SnowCoverConfig snow = Resources.Load<SnowCoverConfig>("Weather/SnowCoverConfig");
            if (loaded == null || snow == null)
                throw new MissingReferenceException("区块天气或积雪配置不完整。");
            loaded.FreezingTemperature = snow.FreezingTemperature;
            configuration = loaded.Snapshot();
        }
        return configuration.Snapshot();
    }

    #endregion
}
