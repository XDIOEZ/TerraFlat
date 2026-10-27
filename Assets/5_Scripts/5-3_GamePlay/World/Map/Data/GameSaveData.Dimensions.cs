public partial class GameSaveData
{
    #region 世界实例数据
    // 世界只继承根世界的几何与环境，生成默认值由创建方明确传入。
    public PlanetData GetOrCreateWorldInstanceData(WorldAddress address, PlanetData initialRootData = null,
        string generationPresetId = null)
    {
        string worldKey = address.WorldKey;
        if (PlanetData_Dict.TryGetValue(worldKey, out PlanetData existing) && existing != null) return existing;
        PlanetData_Dict.TryGetValue(address.RootWorldKey, out PlanetData root);
        root ??= initialRootData?.Name == address.RootWorldKey ? initialRootData : null;
        PlanetData world = root == null ? new PlanetData() : new PlanetData
        {
            Radius = root.Radius, ChunkSize = root.ChunkSize, TopologyMode = root.TopologyMode,
            SpatialDistanceScale = root.SpatialDistanceScale, AutoGenerateMap = root.AutoGenerateMap,
            GlobalTemperature = root.GlobalTemperature, WindStrength = root.WindStrength,
            RainTemperatureOffset = root.RainTemperatureOffset, CloudyTemperatureOffset = root.CloudyTemperatureOffset,
            StormTemperatureOffset = root.StormTemperatureOffset, BodyId = root.BodyId, name = root.name,
            PrefabName = root.PrefabName, SelfRotateSpeed = root.SelfRotateSpeed,
            PhysicalRadiusMeters = root.PhysicalRadiusMeters,
            SurfaceGravityMetersPerSecondSquared = root.SurfaceGravityMetersPerSecondSquared,
            AtmosphereProfileId = root.AtmosphereProfileId, Atmosphere = root.Atmosphere?.Clone()
        };
        world.Name = worldKey;
        world.GenerationPresetId = generationPresetId ?? "surface.default";
        return PlanetData_Dict[worldKey] = world;
    }
    #endregion

    #region 世界共有的维度入口状态
    // 入口配对与封堵归世界所有，角色只保存各自的最后位置。
    public string DimensionStateJson = "{}";
    #endregion
}
