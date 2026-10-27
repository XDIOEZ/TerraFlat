using System;
using System.Collections.Generic;
using System.Threading;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;

public static partial class GameplayTestSystemProbe
{
    #region 生成预设与世界归属规则采样

    // 世界身份、存档和实际地形均调用正式入口，Lua 持有业务断言。
    private static JObject WorldGenerationPresetSelection(JObject args)
    {
        var save = new GameSaveData { Seed = 17413 };
        var rootAddress = new global::WorldAddress("火星", "surface");
        PlanetData root = save.GetOrCreateWorldInstanceData(rootAddress);
        root.BodyId = "mars";
        root.GenerationPresetId = "core:surface.mars";
        root.AtmosphereProfileId = "core:surface.mars";
        root.GenerationNumericOverrides["testing.rootOnly"] = 7d;
        root.GenerationTextOverrides["testing.rootOnly"] = "root";
        PlanetData underground = save.GetOrCreateWorldInstanceData(rootAddress.WithDimension("cave"),
            generationPresetId: DimensionDefinition.CreateCave().GenerationProfile.ProfileId);
        string defaultPreset = underground.GenerationPresetId;
        bool independentOverrides = underground.GenerationNumericOverrides.Count == 0 &&
            underground.GenerationTextOverrides.Count == 0 &&
            !ReferenceEquals(root.GenerationNumericOverrides, underground.GenerationNumericOverrides) &&
            !ReferenceEquals(root.GenerationTextOverrides, underground.GenerationTextOverrides);
        underground.GenerationPresetId = "core:surface.earth";
        underground.GenerationNumericOverrides["terrain.seaLevel"] = .31d;
        underground.GenerationTextOverrides["testing.label"] = "地下地球地形";
        underground.MapData_Dict["underground-only"] = new MapSave();
        PlanetData standalone = save.GetOrCreateWorldInstanceData(new global::WorldAddress("standalone:lab", "orbit"));
        string genericDefaultPreset = standalone.GenerationPresetId;
        standalone.GenerationPresetId = "cave.default";
        var restored = MemoryPack.MemoryPackSerializer.Deserialize<GameSaveData>(
            MemoryPack.MemoryPackSerializer.Serialize(save));
        PlanetData loaded = restored.PlanetData_Dict[underground.Name];
        var numbers = SmallGenerationParameters();
        numbers["river.enabled"] = 0d;
        numbers["lake.large.enabled"] = 0d;
        numbers["weather.enabled"] = 0d;
        var surface = new ChunkGenerationProfileSnapshot("probe:shared-surface", 81, 4, 4, numbers,
            new Dictionary<string, string> { ["terrain.mode"] = "surface" });
        var cave = new ChunkGenerationProfileSnapshot("probe:shared-cave", 81, 4, 4, numbers,
            new Dictionary<string, string> { ["terrain.mode"] = "cave" });
        var generator = new DeterministicChunkGenerator();
        try
        {
            JObject misleadingSurface = CaptureGenerationPresetTerrain(generator, surface, "mars-cave-300m");
            JObject ordinarySurface = CaptureGenerationPresetTerrain(generator, surface, "surface");
            JObject misleadingCave = CaptureGenerationPresetTerrain(generator, cave, "orbital-garden");
            var riverProfile = surface.WithNumericParameter("river.enabled", 1d);
            var riverRequest = new ChunkGenerationRequest(1,
                new FlatWorld.WorldModel.WorldAddress("mars-cave-300m", default), save.Seed, 1, riverProfile);
            var caveRequest = new ChunkGenerationRequest(1,
                new FlatWorld.WorldModel.WorldAddress("orbital-garden", default), save.Seed, 1, cave);
            return new JObject
            {
                ["defaultUndergroundPreset"] = defaultPreset,
                ["worldKey"] = underground.Name, ["bodyId"] = underground.BodyId,
                ["presetId"] = underground.GenerationPresetId,
                ["atmosphereProfileId"] = underground.AtmosphereProfileId,
                ["independentOverrides"] = independentOverrides,
                ["rootPreset"] = root.GenerationPresetId,
                ["rootNumber"] = root.GenerationNumericOverrides["testing.rootOnly"],
                ["rootText"] = root.GenerationTextOverrides["testing.rootOnly"],
                ["rootMapCount"] = root.MapData_Dict.Count,
                ["repeatSameInstance"] = ReferenceEquals(underground,
                    save.GetOrCreateWorldInstanceData(rootAddress.WithDimension("cave"))),
                ["loadedWorldKey"] = loaded.Name, ["loadedBodyId"] = loaded.BodyId,
                ["loadedPreset"] = loaded.GenerationPresetId,
                ["loadedNumber"] = loaded.GenerationNumericOverrides["terrain.seaLevel"],
                ["loadedText"] = loaded.GenerationTextOverrides["testing.label"],
                ["loadedMapCount"] = loaded.MapData_Dict.Count,
                ["loadedOverridesIndependent"] = !ReferenceEquals(loaded.GenerationNumericOverrides,
                    underground.GenerationNumericOverrides),
                ["standaloneBodyId"] = standalone.BodyId, ["standalonePreset"] = standalone.GenerationPresetId,
                ["genericDefaultPreset"] = genericDefaultPreset,
                ["portalExtensions"] = CapturePairedWorldPortalGeneratorIsolation(),
                ["surfaceNamedCave"] = misleadingSurface, ["surfaceNamedSurface"] = ordinarySurface,
                ["caveNamedGarden"] = misleadingCave,
                ["surfaceRiverGroupPresent"] = generator.GetPendingGenerationGroupKey(riverRequest) != null,
                ["caveRiverGroupAbsent"] = generator.GetPendingGenerationGroupKey(caveRequest) == null
            };
        }
        finally { generator.BeginNewWorldHydrologyEpoch(1); }
    }

    internal static JObject CaptureGenerationPresetTerrain(DeterministicChunkGenerator generator,
        ChunkGenerationProfileSnapshot profile, string dimension)
    {
        using var world = new WorldRuntime("probe:preset:" + dimension, 1);
        var address = new FlatWorld.WorldModel.WorldAddress(dimension, default);
        ChunkGenerationRequest request = world.BeginChunkGeneration(address, 17413, profile);
        using ChunkGenerationResult generated = generator.Generate(request, CancellationToken.None);
        if (!world.TryCommit(generated, out string reason) || !world.TryGetChunk(address, out ChunkRuntime chunk))
            throw new InvalidOperationException("生成预设规则区块提交失败：" + reason);
        TerrainHeightShadowManager.TryGetSurfaceHeight(chunk.Terrain, 0, 0, dimension, out TerrainSurfaceHeight height);
        TerrainHeightShadowManager.TryGetSurfaceHeight(chunk.Terrain, 0, 0, "another-cave-name", out TerrainSurfaceHeight otherHeight);
        return new JObject
        {
            ["worldKey"] = world.WorldId,
            ["dimension"] = address.DimensionId,
            ["terrainMode"] = chunk.Terrain.GenerationMode.ToString(),
            ["hash"] = chunk.Terrain.ComputeStableHash().ToString(),
            ["portalCount"] = chunk.Ecology.Count,
            ["caveLayer"] = chunk.Terrain.TryGetEnvironmentValue("caveDirtGround", 0, 0, out _),
            ["heightLevel"] = height.Level, ["heightLevelOtherName"] = otherHeight.Level,
            ["width"] = chunk.Terrain.Width, ["height"] = chunk.Terrain.Height
        };
    }

    private static JObject CapturePairedWorldPortalGeneratorIsolation()
    {
        var numbers = SmallGenerationParameters();
        numbers["cave.portal.enabled"] = 1d;
        numbers["cave.portal.chunkChance"] = 1d;
        numbers["cave.portal.safeRadius"] = 1d;
        numbers["cave.portal.baseSeed"] = 17413d;
        var source = new ChunkGenerationProfileSnapshot("probe:portal-source", 81, 4, 4, numbers,
            new Dictionary<string, string> { ["terrain.mode"] = "surface",
                ["cave.portal.itemId"] = "MineEntrance", ["cave.portal.targetDimensionId"] = "paired-lab" });
        DeterministicChunkGenerator CreateSourceGenerator(bool blocked) => new(terrainDecorator: (request, terrain, token) =>
        {
            for (int y = 0; y < terrain.Height; y++)
            for (int x = 0; x < terrain.Width; x++)
            {
                terrain.SetCell(x, y, new TerrainCell(request.Profile.Settings.GroundTileId, 0,
                    blocked ? 9 : 0, (int)SurfaceBiomeKind.Grassland, 1,
                    blocked ? TerrainCellFlags.Blocking : TerrainCellFlags.Walkable));
                terrain.SetLiquid(x, y, 0, 0);
                terrain.SetEnvironmentValue("structure", x, y, 0f);
            }
        });
        var blockedSource = CreateSourceGenerator(true);
        var openSource = CreateSourceGenerator(false);
        var destinationGenerator = new DeterministicChunkGenerator();
        ChunkGenerationProfileSnapshot PairWith(DeterministicChunkGenerator generator) =>
            new("probe:portal-destination", 81, 4, 4, numbers,
                new Dictionary<string, string> { ["terrain.mode"] = "surface",
                    ["cave.portal.itemId"] = "CaveExit", ["cave.portal.targetDimensionId"] = "source-garden" },
                portalPairing: new CavePortalPairingSnapshot("source-garden", 17413, source,
                    sourceTerrainGenerator: generator));
        try
        {
            var blocked = PairWith(blockedSource);
            var open = PairWith(openSource);
            return new JObject
            {
                ["samePairingFingerprint"] = blocked.PortalPairing.Fingerprint == open.PortalPairing.Fingerprint,
                ["blockedSource"] = CaptureGenerationPresetTerrain(blockedSource, source, "source-garden"),
                ["openSource"] = CaptureGenerationPresetTerrain(openSource, source, "source-garden"),
                ["blockedDestination"] = CaptureGenerationPresetTerrain(destinationGenerator, blocked, "paired-lab"),
                ["openDestination"] = CaptureGenerationPresetTerrain(destinationGenerator, open, "paired-lab")
            };
        }
        finally
        {
            blockedSource.BeginNewWorldHydrologyEpoch(1);
            openSource.BeginNewWorldHydrologyEpoch(1);
            destinationGenerator.BeginNewWorldHydrologyEpoch(1);
        }
    }

    #endregion
}
