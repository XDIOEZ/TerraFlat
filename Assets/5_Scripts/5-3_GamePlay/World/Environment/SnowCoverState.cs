using System;
using FlatWorld.WorldModel;
using MemoryPack;
using UnityEngine;

/// <summary>静态季节覆盖量容器；区域天气积雪保存在区块环境层。</summary>
[Serializable, MemoryPackable]
public partial class SnowCoverState
{
    public const int MinimumTemperature = -80;
    public const int BandCount = 161;
    public float[] Coverage = new float[BandCount]; // -80～80℃ 基础气候的持续覆盖。
    public bool Initialized; // 是否建立世界时间游标。
    public float LastTotalTime; // 在其他维度度过的时间也需按原天气边界补算。

    /// <summary>从相邻气候档读取覆盖量，并统一量化为 0.1～1.0 的十层积雪。</summary>
    public float Sample(float baselineTemperature)
    {
        float band = Mathf.Clamp(baselineTemperature - MinimumTemperature, 0f, BandCount - 1);
        int lower = Mathf.FloorToInt(band);
        float coverage = Mathf.Lerp(Coverage[lower], Coverage[Mathf.Min(lower + 1, BandCount - 1)], band - lower);
        return SnowDepthLayer.Quantize(coverage);
    }
}

/// <summary>雪层统一查询入口：生成雪、区域天气雪与玩家编辑分别保存，查询不推进模拟。</summary>
public static class WorldSnowSystem
{
    #region 查询

    public const string WeatherDepthLayer = "snow.weather.depth";

    /// <summary>空雪层直接读取数组确认，避免无雪区块逐格执行地表与液体查询。</summary>
    internal static bool HasPotentialSnow(ChunkTerrainData terrain)
    {
        if (terrain == null || terrain.IsDisposed)
            return false;
        // 玩家编辑层即使暂时没有雪，也保留原计算路径处理天气与历史差量。
        return HasPositiveValues(terrain, SnowDepthLayer.LayerId) ||
               HasPositiveValues(terrain, WeatherDepthLayer) ||
               HasPositiveValues(terrain, WorldSnowInteraction.EditedLayer);
    }

    private static bool HasPositiveValues(ChunkTerrainData terrain, string layerId)
    {
        if (!terrain.TryGetEnvironmentLayer(layerId, out ReadOnlyMemory<float> values))
            return false;
        ReadOnlySpan<float> cells = values.Span;
        for (int i = 0; i < cells.Length; i++)
            if (cells[i] > 0f) return true;
        return false;
    }

    public static float GetWeatherDepth(ChunkTerrainData terrain, int x, int y)
    {
        if (terrain == null || terrain.IsDisposed ||
            !terrain.TryGetEnvironmentValue(WeatherDepthLayer, x, y, out float depth))
            return 0f;
        return Mathf.Clamp01(depth);
    }

    public static float GetTransientDepth(ChunkTerrainData terrain, int x, int y,
        SnowCoverState seasonalSnow, float baselineOffset)
        => Mathf.Clamp01(GetSeasonalDepth(terrain, x, y, seasonalSnow, baselineOffset) + GetWeatherDepth(terrain, x, y));

    internal static float GetSeasonalDepth(ChunkTerrainData terrain, int x, int y,
        SnowCoverState seasonalSnow, float baselineOffset)
    {
        float seasonalDepth = 0f;
        if (seasonalSnow != null &&
            terrain.TryGetEnvironmentValue("temperature.celsius", x, y, out float temperature))
            seasonalDepth = seasonalSnow.Sample(temperature + baselineOffset);
        return seasonalDepth;
    }

    public static float GetNaturalDepth(ChunkTerrainData terrain, int x, int y)
    {
        if (terrain == null || terrain.IsDisposed ||
            !terrain.TryGetEnvironmentValue(SnowDepthLayer.LayerId, x, y, out float depth))
            return 0f;
        return SnowDepthLayer.Quantize(depth);
    }

    // 支撑面遮住的天然地雪不参与融雪或转成平台上的动态覆盖。
    internal static float GetExposedNaturalDepth(ChunkTerrainData terrain, int x, int y)
        => TerrainSupportLayer.GetTileId(terrain, x, y) == 0 ? GetNaturalDepth(terrain, x, y) : 0f;

    public static float GetSurfaceDepth(ChunkTerrainData terrain, int x, int y,
        SnowCoverState seasonalSnow, float baselineOffset)
    {
        if (terrain == null || terrain.IsDisposed ||
            WorldLiquidSystem.GetSurfaceDepth(terrain, x, y) > 0f)
            return 0f;

        return ResolveSurfaceDepth(terrain, x, y, TerrainSupportLayer.GetSurfaceCell(terrain, x, y),
            seasonalSnow, baselineOffset);
    }

    /// <summary>批量绘制复用调用方已经取得的有效地表，避免为同格重复解析支撑面。</summary>
    internal static float GetSurfaceDepth(ChunkTerrainData terrain, int x, int y, TerrainCell surface,
        SnowCoverState seasonalSnow, float baselineOffset)
    {
        if (terrain == null || terrain.IsDisposed ||
            WorldLiquidSystem.GetSurfaceDepth(terrain, x, y) > 0f)
            return 0f;
        return ResolveSurfaceDepth(terrain, x, y, surface, seasonalSnow, baselineOffset);
    }

    private static float ResolveSurfaceDepth(ChunkTerrainData terrain, int x, int y, TerrainCell surface,
        SnowCoverState seasonalSnow, float baselineOffset)
    {
        if (surface.GroundTileId == 0)
            return 0f;

        float naturalDepth = GetExposedNaturalDepth(terrain, x, y);
        float seasonalDepth = GetTransientDepth(terrain, x, y, seasonalSnow, baselineOffset);

        // 玩家编辑后只叠加区域天气雪的净变化，不能重新铺回已铲走的覆盖。
        if (terrain.TryGetEnvironmentValue(WorldSnowInteraction.EditedLayer, x, y, out float edited) && edited > 0f)
        {
            terrain.TryGetEnvironmentValue(WorldSnowInteraction.DepthLayer, x, y, out float depth);
            terrain.TryGetEnvironmentValue(WorldSnowInteraction.SeasonLayer, x, y, out float previousSeason);
            return Mathf.Max(0, Mathf.RoundToInt((depth + seasonalDepth - previousSeason) * SnowDepthLayer.LayerCount)) *
                   SnowDepthLayer.LayerStep;
        }

        return SnowDepthLayer.Quantize(Mathf.Max(naturalDepth, seasonalDepth));
    }

    public static float GetSurfaceDepth(Vector3 worldPosition)
    {
        ChunkMgr manager = ChunkMgr.ExistingInstance;
        if (manager == null || !manager.TryGetRuntimeTerrainTile(worldPosition, out RuntimeTerrainTileSample sample))
            return 0f;

        SnowCoverState seasonalSnow = null;
        float baselineOffset = 0f;
        WeatherMgr.ExistingInstance?.TryGetSnowCoverageContext(out seasonalSnow, out baselineOffset);
        return GetSurfaceDepth(sample.Terrain, sample.LocalCell.x, sample.LocalCell.y,
            seasonalSnow, baselineOffset);
    }

    #endregion
}

/// <summary>纯积雪模拟；基础气候叠加季节和当前天气，冻结温度以下降水积雪，零度以上按温差融化。</summary>
public static class SnowCoverSimulation
{
    /// <summary>只推进本次真实经历的天气时段，不根据最终天气补写整个离线区间。</summary>
    public static void Advance(SnowCoverState state, SnowCoverConfig config, float temperatureOffset, float precipitation, float seconds)
    {
        if (state.Coverage == null || state.Coverage.Length != SnowCoverState.BandCount)
            throw new InvalidOperationException("积雪数据格式不兼容。");
        for (int i = 0; i < state.Coverage.Length; i++)
        {
            float temperature = i + SnowCoverState.MinimumTemperature + temperatureOffset;
            float change = temperature <= config.FreezingTemperature
                ? precipitation * config.AccumulationPerSecond
                : -(temperature - config.FreezingTemperature) * config.MeltPerDegreeSecond;
            state.Coverage[i] = Mathf.Clamp01(state.Coverage[i] + change * seconds);
        }
    }
}
