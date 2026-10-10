using System;
using FlatWorld.WorldModel;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

public partial class ChunkMgr
{
    #region 空气湿度查询

    private WorldAirHumidityField airHumidityField;
    private WorldRuntime airHumidityWorld;
    private long airHumidityEpoch;
    private ChunkGenerationProfileSnapshot airHumidityProfile;
    private ChunkGenerationTopologySnapshot airHumidityTopology;
    private string airHumidityDimension;
    private GameRes airHumidityResources;
    private int airHumidityResourceVersion;

    /// <summary>只读区块生成时固定的地理空气湿度，玩家改水不会改变这层。</summary>
    public bool TryGetGeographicAirHumidity(Vector2 worldPosition, out float humidity)
    {
        humidity = 0f;
        return float.IsFinite(worldPosition.x) && float.IsFinite(worldPosition.y) &&
               TryGetRuntimeTerrainTile(worldPosition, out RuntimeTerrainTileSample sample) &&
               sample.Terrain.TryGetEnvironmentValue(AirHumidityKernel.GeographicLayer,
                   sample.LocalCell.x, sample.LocalCell.y, out humidity);
    }

    /// <summary>查询当前实际水体形成的空气湿度，供实时天气等消费者以后复用。</summary>
    public bool TryGetAirHumidity(Vector2 worldPosition, out float humidity)
    {
        humidity = 0f;
        return float.IsFinite(worldPosition.x) && float.IsFinite(worldPosition.y) &&
               PrepareAirHumidityField() &&
               airHumidityField.TrySample(new Int2(Mathf.FloorToInt(worldPosition.x),
                   Mathf.FloorToInt(worldPosition.y)), out humidity);
    }

    private bool PrepareAirHumidityField()
    {
        WorldRuntime world = WorldRuntime;
        ChunkGenerationProfileSnapshot profile = ActiveGenerationProfile;
        GameRes resources = GameRes.ExistingInstance;
        if (IsWorldRuntimeShuttingDown || !IsSurfaceStreamingScene || world == null ||
            profile == null || resources == null)
            return false;
        string dimension = ResolveCurrentDimensionId();
        ChunkGenerationTopologySnapshot topology = ResolveActiveGenerationTopology();
        if (airHumidityField != null && ReferenceEquals(world, airHumidityWorld) &&
            world.Epoch == airHumidityEpoch && ReferenceEquals(profile, airHumidityProfile) &&
            dimension == airHumidityDimension && ReferenceEquals(resources, airHumidityResources) &&
            resources.ResourceReloadVersion == airHumidityResourceVersion &&
            topology.IsWrapped == airHumidityTopology.IsWrapped &&
            topology.Min == airHumidityTopology.Min && topology.Span == airHumidityTopology.Span)
            return true;

        ReleaseAirHumidityField();
        airHumidityWorld = world;
        airHumidityEpoch = world.Epoch;
        airHumidityProfile = profile;
        airHumidityTopology = topology;
        airHumidityDimension = dimension;
        airHumidityResources = resources;
        airHumidityResourceVersion = resources.ResourceReloadVersion;
        airHumidityField = new WorldAirHumidityField(world, dimension, profile.Width, profile.Height,
            topology, AirHumiditySettings.FromProfile(profile),
            id => resources.TryGetLiquidDefinition(id, out LiquidDefinition liquid) &&
                  liquid.WorldWater?.WaterContact == true,
            ResolveRestoredAirHumidityTerrain);
        return true;
    }

    private ChunkTerrainData ResolveRestoredAirHumidityTerrain(RuntimeWorldAddress address)
    {
        // 读档差量尚未恢复的 Ready 区块暂不参与湿度，避免抽干湖泊短暂恢复供湿。
        return TryGetChunkRuntime(address, out ChunkRuntime chunk) &&
               chunk.DataStatus == ChunkDataStatus.Ready && chunk.Terrain != null &&
               !chunk.Terrain.IsDisposed &&
               activeRuntimeBindings.TryGetValue(address, out RuntimeChunkBinding binding) &&
               ReferenceEquals(binding.RestoredTerrainChunk, chunk)
            ? chunk.Terrain : null;
    }

    private void ReleaseAirHumidityField()
    {
        airHumidityField?.Dispose();
        airHumidityField = null;
        airHumidityWorld = null;
        airHumidityProfile = null;
        airHumidityResources = null;
        airHumidityDimension = null;
        airHumidityTopology = default;
        airHumidityEpoch = 0;
        airHumidityResourceVersion = 0;
    }

    #endregion
}
