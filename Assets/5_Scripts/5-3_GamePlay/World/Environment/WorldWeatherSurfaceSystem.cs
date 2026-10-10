using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using UnityEngine;

public static class WorldWeatherSurfaceSystem
{
    #region 区块结算

    private static readonly List<int> SoilChanged = new();
    private static readonly List<int> SnowChanged = new();
    private static SnowCoverConfig snowConfig;

    // 天气来源只提供真实降水积分，土壤与积雪各自维护状态。
    public static void AdvanceChunk(ChunkRuntime chunk, WeatherMgr weather, float seconds,
        Func<Int2, RegionalWeatherAdvanceResult> resolveExposure)
    {
        if (!GameNetwork.HasStateAuthority || chunk == null || weather == null || resolveExposure == null ||
            !float.IsFinite(seconds) || seconds <= 0f || chunk.DataStatus != ChunkDataStatus.Ready ||
            chunk.Terrain == null || chunk.Terrain.IsDisposed ||
            ChunkMgr.ExistingInstance?.IsTerrainRestoredForEnvironment(chunk) != true)
            return;

        SnowCoverConfig config = GetSnowConfig();
        float freezingTemperature = weather.PrecipitationFreezingTemperature;
        TemperatureMgr temperature = TemperatureMgr.Instance;
        ChunkTerrainData terrain = chunk.Terrain;
        SoilChanged.Clear();
        SnowChanged.Clear();
        try
        {
            for (int y = 0; y < terrain.Height; y++)
            for (int x = 0; x < terrain.Width; x++)
            {
                TerrainCell surface = TerrainSupportLayer.GetSurfaceCell(terrain, x, y);
                if (surface.GroundTileId == 0 || WorldLiquidSystem.GetSurfaceDepth(terrain, x, y) > 0f)
                    continue;

                var world = new Int2(chunk.Address.ChunkOrigin.X + x, chunk.Address.ChunkOrigin.Y + y);
                RegionalWeatherAdvanceResult exposure = resolveExposure(world);
                float precipitationSeconds = Mathf.Max(0f, exposure.RainExposureSeconds + exposure.SnowExposureSeconds);
                float previousWeatherSnow = WorldSnowSystem.GetWeatherDepth(terrain, x, y);
                bool edited = terrain.TryGetEnvironmentValue(WorldSnowInteraction.EditedLayer, x, y,
                    out float editedValue) && editedValue > 0f;
                terrain.TryGetEnvironmentValue(WorldSnowInteraction.DepthLayer, x, y, out float playerDepth);
                if (precipitationSeconds <= 0f && previousWeatherSnow <= 0f && (!edited || playerDepth <= 0f))
                    continue;
                if (!float.IsFinite(precipitationSeconds) || temperature == null ||
                    !temperature.TryGetAmbientTemperature(new Vector2(world.X + 0.5f, world.Y + 0.5f),
                        out float currentTemperature) || !float.IsFinite(currentTemperature))
                    continue;

                var local = new Vector2Int(x, y);
                var sample = new RuntimeTerrainTileSample(chunk.Address, terrain,
                    new Vector2Int(world.X, world.Y), local, terrain.GetCell(x, y), terrain.GetTopTileId(x, y));
                int index = y * terrain.Width + x;
                float nextWeatherSnow = previousWeatherSnow;
                float waterPoints = 0f;
                bool snowChanged = false;
                if (currentTemperature <= freezingTemperature)
                {
                    nextWeatherSnow = Mathf.Clamp01(previousWeatherSnow +
                        precipitationSeconds * Mathf.Max(0f, config.AccumulationPerSecond));
                }
                else
                {
                    waterPoints = precipitationSeconds * Mathf.Max(0f, weather.SurfaceRainWaterPerSecond);
                    float meltBudget = (currentTemperature - freezingTemperature) *
                        Mathf.Max(0f, config.MeltPerDegreeSecond) * seconds;
                    nextWeatherSnow = Mathf.Max(0f, previousWeatherSnow - meltBudget);
                    float melted = previousWeatherSnow - nextWeatherSnow;
                    if (edited)
                    {
                        terrain.TryGetEnvironmentValue(WorldSnowInteraction.SeasonLayer, x, y, out float baseline);
                        float before = Mathf.Max(0f, playerDepth + previousWeatherSnow - baseline);
                        float after = Mathf.Max(0f, playerDepth + nextWeatherSnow - baseline);
                        melted = before - after;
                        float additional = Mathf.Min(after, Mathf.Max(0f, meltBudget - melted));
                        if (additional > 0f)
                        {
                            playerDepth = Mathf.Max(0f, playerDepth - additional);
                            terrain.SetEnvironmentValue(WorldSnowInteraction.DepthLayer, x, y, playerDepth);
                            snowChanged = true;
                            melted += additional;
                        }
                    }
                    waterPoints += melted * Mathf.Max(0f, weather.MeltWaterPerSnowDepth);
                }
                if (nextWeatherSnow != previousWeatherSnow)
                {
                    terrain.SetEnvironmentValue(WorldSnowSystem.WeatherDepthLayer, x, y, nextWeatherSnow);
                    snowChanged = true;
                }
                if (snowChanged)
                    SnowChanged.Add(index);
                if (waterPoints > 0f && FarmlandSystem.SupplySurfaceWater(sample, waterPoints, false))
                    SoilChanged.Add(index);
            }
            if (SoilChanged.Count > 0 || SnowChanged.Count > 0)
                SaveDataMgr.Instance.RecordWeatherSurfaceChunk(chunk, SoilChanged, SnowChanged);
        }
        finally
        {
            SoilChanged.Clear();
            SnowChanged.Clear();
        }
    }

    #endregion

    #region 共享积雪配置

    private static SnowCoverConfig GetSnowConfig()
    {
        if (snowConfig == null)
            snowConfig = Resources.Load<SnowCoverConfig>("Weather/SnowCoverConfig");
        if (snowConfig == null)
            throw new MissingReferenceException("缺少 Weather/SnowCoverConfig 积雪配置。");
        return snowConfig;
    }

    #endregion
}
