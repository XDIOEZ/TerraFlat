using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using UnityEngine;
using Unity.Profiling;

public static class WorldWeatherSurfaceSystem
{
    #region 区块结算

    private static readonly List<int> SoilChanged = new();
    private static readonly List<int> SnowChanged = new();
    private static SnowCoverConfig snowConfig;
    private static float[] ambientTemperatures = Array.Empty<float>();
    private static int[] activeCells = Array.Empty<int>();
    private static SurfaceWaterSupply[] waterSupplies = Array.Empty<SurfaceWaterSupply>();
    private static readonly ProfilerMarker AdvanceChunkMarker = new("FlatWorld.Weather.SurfaceChunk");
    private static readonly ProfilerMarker ExposureMarker = new("FlatWorld.Weather.SurfaceExposure");
    private static readonly ProfilerMarker TemperatureMarker = new("FlatWorld.Weather.SurfaceTemperatures");
    private static readonly ProfilerMarker WaterMarker = new("FlatWorld.Weather.SurfaceWater");
    private static readonly ProfilerMarker PersistMarker = new("FlatWorld.Weather.PersistSurfaceChunk");

    // 整个区块共享降水积分，地块只负责实际浇水、积雪和融雪。
    public static void AdvanceChunk(ChunkRuntime chunk, WeatherMgr weather, float seconds,
        float precipitationSeconds)
    {
        if (!GameNetwork.HasStateAuthority || chunk == null || weather == null ||
            !float.IsFinite(seconds) || seconds <= 0f ||
            !float.IsFinite(precipitationSeconds) || precipitationSeconds < 0f ||
            chunk.DataStatus != ChunkDataStatus.Ready ||
            chunk.Terrain == null || chunk.Terrain.IsDisposed ||
            ChunkMgr.ExistingInstance?.IsTerrainRestoredForEnvironment(chunk) != true)
            return;

        using var profilerScope = AdvanceChunkMarker.Auto();
        ChunkTerrainData terrain = chunk.Terrain;
        // 未产生过天气雪或玩家雪的晴天区块无需逐格结算。
        if (precipitationSeconds <= 0f &&
            !terrain.TryGetEnvironmentValue(WorldSnowSystem.WeatherDepthLayer, 0, 0, out _) &&
            !terrain.TryGetEnvironmentValue(WorldSnowInteraction.DepthLayer, 0, 0, out _))
            return;

        SnowCoverConfig config = GetSnowConfig();
        float freezingTemperature = weather.PrecipitationFreezingTemperature;
        TemperatureMgr temperature = TemperatureMgr.Instance;
        if (temperature == null)
            return;
        if (ambientTemperatures.Length < terrain.CellCount)
        {
            ambientTemperatures = new float[terrain.CellCount];
            activeCells = new int[terrain.CellCount];
            waterSupplies = new SurfaceWaterSupply[terrain.CellCount];
        }
        int activeCount;
        using (ExposureMarker.Auto())
            activeCount = CollectActiveCells(chunk, precipitationSeconds);
        if (activeCount == 0)
            return;
        // 修改土壤和雪层前统一冻结当地温度，邻区热源依赖每块只检查一次。
        using (TemperatureMarker.Auto())
            if (!temperature.TrySampleAmbientChunk(chunk, activeCells.AsSpan(0, activeCount), ambientTemperatures))
                return;
        SoilChanged.Clear();
        SnowChanged.Clear();
        try
        {
            int waterSupplyCount = 0;
            for (int i = 0; i < activeCount; i++)
            {
                int index = activeCells[i];
                int x = index % terrain.Width;
                int y = index / terrain.Width;
                float previousWeatherSnow = WorldSnowSystem.GetWeatherDepth(terrain, x, y);
                bool edited = terrain.TryGetEnvironmentValue(WorldSnowInteraction.EditedLayer, x, y,
                    out float editedValue) && editedValue > 0f;
                terrain.TryGetEnvironmentValue(WorldSnowInteraction.DepthLayer, x, y, out float playerDepth);
                float currentTemperature = ambientTemperatures[index];
                if (!float.IsFinite(currentTemperature))
                    continue;

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
                if (waterPoints > 0f)
                    waterSupplies[waterSupplyCount++] = new SurfaceWaterSupply(index, waterPoints);
            }
            if (waterSupplyCount > 0)
                using (WaterMarker.Auto())
                    FarmlandSystem.SupplySurfaceWaterBatch(chunk, waterSupplies.AsSpan(0, waterSupplyCount), SoilChanged, true);
            if (SoilChanged.Count > 0 || SnowChanged.Count > 0)
                using (PersistMarker.Auto())
                    SaveDataMgr.Instance.RecordWeatherSurfaceChunk(chunk, SoilChanged, SnowChanged);
            if (waterSupplyCount > 0)
                FarmlandSystem.PublishSurfaceWaterBatch(terrain);
        }
        finally
        {
            SoilChanged.Clear();
            SnowChanged.Clear();
        }
    }

    // 先筛选受本区块降水或已有积雪影响的地面格，不再逐格查询天气。
    private static int CollectActiveCells(ChunkRuntime chunk, float precipitationSeconds)
    {
        ChunkTerrainData terrain = chunk.Terrain;
        int count = 0;
        for (int y = 0; y < terrain.Height; y++)
        for (int x = 0; x < terrain.Width; x++)
        {
            float weatherSnow = WorldSnowSystem.GetWeatherDepth(terrain, x, y);
            bool edited = terrain.TryGetEnvironmentValue(WorldSnowInteraction.EditedLayer, x, y,
                out float editedValue) && editedValue > 0f;
            terrain.TryGetEnvironmentValue(WorldSnowInteraction.DepthLayer, x, y, out float playerDepth);
            bool hasSnow = weatherSnow > 0f || edited && playerDepth > 0f;
            if (precipitationSeconds <= 0f && !hasSnow)
                continue;
            TerrainCell surface = TerrainSupportLayer.GetSurfaceCell(terrain, x, y);
            if (surface.GroundTileId == 0 || WorldLiquidSystem.GetSurfaceDepth(terrain, x, y) > 0f)
                continue;
            int index = y * terrain.Width + x;
            activeCells[count++] = index;
        }
        return count;
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
