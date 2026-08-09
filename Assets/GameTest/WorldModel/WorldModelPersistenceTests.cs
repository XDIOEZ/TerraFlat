using System.Collections.Generic;
using System.Threading;
using FlatWorld.WorldModel;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace FlatWorld.GameTest.WorldModel
{
    [Category("WorldModel.Persistence")]
    public sealed class WorldModelPersistenceTests
    {
        private static readonly ChunkGenerationProfileSnapshot Profile =
            new("persistence", 5, 8, 8, new Dictionary<string, double>
            {
                ["terrain.groundTileId"] = 1,
                ["terrain.waterTileId"] = 2,
                ["terrain.waterThreshold"] = 0.5,
                ["terrain.biomeCount"] = 4,
                ["grass.density"] = 1
            });

        [Test]
        public void SnapshotDuringGenerationExcludesHalfGeneratedChunk()
        {
            using var world = new WorldRuntime("save", 1);
            var address = new FlatWorld.WorldModel.WorldAddress("surface", new Int2(0, 0));
            world.BeginChunkGeneration(address, 10, Profile);
            Assert.That(world.CaptureSnapshot().Chunks.Count, Is.Zero);
        }

        [Test]
        [Category("WorldModel.Grass")]
        [Category("Smoke")]
        public void ConsumingGrassUpdatesLayerAndNotifiesOnce()
        {
            using var world = CreateCommittedWorld(12);
            ChunkRuntime chunk = null;
            foreach (ChunkRuntime value in world.Chunks.Values)
                chunk = value;

            Assert.That(chunk, Is.Not.Null);
            ChunkTerrainData terrain = chunk.Terrain;
            Vector2Int grassCell = default;
            bool foundGrass = false;
            for (int y = 0; y < terrain.Height && !foundGrass; y++)
            for (int x = 0; x < terrain.Width; x++)
            {
                if (terrain.GetGrass(x, y) != ChunkTerrainData.GrassPresent)
                    continue;

                grassCell = new Vector2Int(x, y);
                foundGrass = true;
                break;
            }

            Assert.That(foundGrass, Is.True, "测试区块必须包含至少一格草层数据。");
            int changeCount = 0;
            TerrainChangeKind lastChangeKind = default;
            terrain.Changed += change =>
            {
                changeCount++;
                lastChangeKind = change.Kind;
            };

            Assert.That(terrain.TryConsumeGrass(grassCell.x, grassCell.y), Is.True);
            Assert.That(terrain.GetGrass(grassCell.x, grassCell.y),
                Is.EqualTo(ChunkTerrainData.GrassEmpty));
            Assert.That(changeCount, Is.EqualTo(1));
            Assert.That(lastChangeKind, Is.EqualTo(TerrainChangeKind.Grass));
            Assert.That(terrain.TryConsumeGrass(grassCell.x, grassCell.y), Is.False);
            Assert.That(changeCount, Is.EqualTo(1), "同一格草不能重复消费或重复刷新。");
        }

        [Test]
        public void ChunkViewCanBindUnbindAndRebindWithoutDuplicateLeases()
        {
            using var world = CreateCommittedWorld(10);
            ChunkRuntime chunk = null;
            foreach (ChunkRuntime value in world.Chunks.Values)
                chunk = value;
            Assert.That(chunk, Is.Not.Null);

            GameObject viewObject = new("ChunkView_BindingTest");
            try
            {
                ChunkView view = viewObject.AddComponent<ChunkView>();
                for (int i = 0; i < 3; i++)
                {
                    view.Bind(world, chunk);
                    view.Bind(world, chunk);
                    Assert.That(chunk.PresentationLeaseCount, Is.EqualTo(1));
                    Assert.That(chunk.NavigationLeaseCount, Is.EqualTo(1));
                    Assert.That(chunk.PresentationStatus, Is.EqualTo(ChunkPresentationStatus.Bound));
                    view.Unbind();
                    Assert.That(chunk.PresentationLeaseCount, Is.Zero);
                    Assert.That(chunk.NavigationLeaseCount, Is.Zero);
                    Assert.That(chunk.PresentationStatus, Is.EqualTo(ChunkPresentationStatus.Unbound));
                }
            }
            finally
            {
                Object.DestroyImmediate(viewObject);
            }
        }

        [Test]
        public void DimensionCatalogMapsProfilesAndPureChunkViews()
        {
            DimensionCatalogSO catalog = AssetDatabase.LoadAssetAtPath<DimensionCatalogSO>(
                "Assets/Resources/Config/DimensionCatalog_Default.asset");
            Assert.That(catalog, Is.Not.Null);
            ChunkGenerationProfileSO surfaceProfile = null;
            foreach (string dimensionId in new[] { "surface", "cave" })
            {
                DimensionDefinition definition = catalog.Find(dimensionId);
                Assert.That(definition, Is.Not.Null, dimensionId);
                Assert.That(definition.GenerationProfile, Is.Not.Null, dimensionId);
                Assert.That(
                    definition.GenerationProfile.GenerationSignature,
                    Is.EqualTo(DeterministicChunkGenerator.CurrentGenerationSignature));
                Assert.That(definition.ChunkViewPrefab, Is.Not.Null, dimensionId);
                Assert.That(definition.ChunkViewPrefab.GetComponent<global::Map>(), Is.Null,
                    "ChunkView prefab must not carry the legacy Map authority.");
                if (dimensionId == "surface")
                {
                    surfaceProfile = definition.GenerationProfile;
                }
                else
                {
                    Assert.That(definition.GenerationProfile.CreateSnapshot().Settings.Mode,
                        Is.EqualTo(ChunkGenerationMode.Cave));
                    Assert.That(definition.GenerationProfile,
                        Is.Not.SameAs(surfaceProfile),
                        "矿洞必须使用独立 Cave Profile，不能复用地表 Profile。");
                }
            }
        }

        [Test]
        public void ChunkViewPrefabRepeatedBindingRendersAndClearsEveryModelLayer()
        {
            ChunkView prefab = AssetDatabase.LoadAssetAtPath<ChunkView>(
                "Assets/2_Prefabs/WorldModel/ChunkView.prefab");
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponent<ChunkTilemapRenderer>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<ChunkEnvironmentTilemapRenderer>(), Is.Not.Null);
            ChunkGrassRenderer grassRenderer = prefab.GetComponent<ChunkGrassRenderer>();
            Assert.That(grassRenderer, Is.Not.Null);
            Assert.That(grassRenderer.IsConfigured, Is.True,
                "Chunk grass must use the dedicated detail atlas, not the ground tile.");
            Assert.That(prefab.GetComponent<ChunkCollisionRenderer>(), Is.Not.Null);
            Assert.That(prefab.GetComponentsInChildren<Tilemap>(true).Length, Is.EqualTo(4));

            using var world = CreateCommittedWorld(44);
            ChunkRuntime chunk = null;
            foreach (ChunkRuntime value in world.Chunks.Values)
                chunk = value;
            ChunkView view = Object.Instantiate(prefab);
            try
            {
                ulong hash = chunk.Terrain.ComputeStableHash();
                for (int i = 0; i < 3; i++)
                {
                    view.Bind(world, chunk, includeNavigation: false);
                    view.Bind(world, chunk, includeNavigation: false);
                    Tilemap ground = view.transform.Find("Ground").GetComponent<Tilemap>();
                    Tilemap grass = view.transform.Find("Grass").GetComponent<Tilemap>();
                    Assert.That(ground.GetUsedTilesCount(), Is.GreaterThan(0));
                    Assert.That(grass.GetUsedTilesCount(), Is.GreaterThan(1),
                        "Visible grass must render multiple atlas variants.");
                    Assert.That(chunk.PresentationLeaseCount, Is.EqualTo(1));
                    Assert.That(chunk.NavigationLeaseCount, Is.Zero);
                    Assert.That(chunk.Terrain.ComputeStableHash(), Is.EqualTo(hash));
                    view.Unbind();
                    foreach (Tilemap tilemap in view.GetComponentsInChildren<Tilemap>(true))
                        Assert.That(tilemap.GetUsedTilesCount(), Is.Zero);
                    Assert.That(view.GetComponentInChildren<TilemapCollider2D>(true).enabled, Is.False);
                    Assert.That(chunk.PresentationLeaseCount, Is.Zero);
                }
            }
            finally
            {
                Object.DestroyImmediate(view.gameObject);
            }
        }

        [Test]
        public void DefaultSurfaceProfileProducesRiversAndGrassAcrossLocalWindow()
        {
            ChunkGenerationProfileSO profileAsset =
                AssetDatabase.LoadAssetAtPath<ChunkGenerationProfileSO>(
                    "Assets/Resources/Config/WorldModel/ChunkGenerationProfile_Surface.asset");
            Assert.That(profileAsset, Is.Not.Null);
            ChunkGenerationProfileSnapshot profile = profileAsset.CreateSnapshot();
            Assert.That(profile.Settings.SurfaceClimateAlgorithm,
                Is.EqualTo(SurfaceClimateAlgorithm.LegacyLand));
            Assert.That(profile.Settings.HeightNoise.CoordinateScale,
                Is.EqualTo(2d).Within(0.000001d));
            Assert.That(profile.Settings.HeightNoise.Frequency,
                Is.EqualTo(0.05d).Within(0.000001d));
            Assert.That(profile.Settings.HeightNoise.Octaves, Is.EqualTo(5));
            Assert.That(profile.Settings.HeightNoise.Persistence,
                Is.EqualTo(0.45d).Within(0.000001d));
            Assert.That(profile.Settings.HeightNoise.OffsetX,
                Is.EqualTo(9000d).Within(0.000001d));
            Assert.That(profile.Settings.HeightSecondaryBoostEnabled, Is.True);
            Assert.That(profile.Settings.HeightSecondaryBoostStrength,
                Is.EqualTo(1d).Within(0.000001d));
            Assert.That(profile.Settings.MountainLevel,
                Is.EqualTo(0.72d).Within(0.000001d));
            Assert.That(profile.Settings.SeaLevel,
                Is.EqualTo(0.5d).Within(0.000001d));
            Assert.That(profile.Settings.BeachLevel,
                Is.EqualTo(0.51d).Within(0.000001d));
            Assert.That(profile.Settings.PrecipitationNoise.CoordinateScale,
                Is.EqualTo(10d).Within(0.000001d));
            Assert.That(profile.Settings.PrecipitationNoise.Frequency,
                Is.EqualTo(0.02d).Within(0.000001d));
            Assert.That(profile.Settings.PrecipitationNoise.Octaves, Is.EqualTo(4));
            Assert.That(profile.Settings.TemperatureNoise.CoordinateScale,
                Is.EqualTo(10d).Within(0.000001d));
            Assert.That(profile.Settings.TemperatureNoise.Frequency,
                Is.EqualTo(0.015d).Within(0.000001d));
            Assert.That(profile.Settings.TemperatureNoise.Octaves, Is.EqualTo(4));
            Assert.That(profile.Settings.TemperatureCelsiusMin, Is.EqualTo(0d));
            Assert.That(profile.Settings.TemperatureCelsiusMax, Is.EqualTo(50d));
            Assert.That(profile.Settings.WindRegionSize,
                Is.EqualTo(256d).Within(0.000001d));
            Assert.That(profile.Settings.WindSeedSalt, Is.EqualTo(1779033703));
            Assert.That(profile.Settings.OrographicSampleDistance,
                Is.EqualTo(64d).Within(0.000001d));
            Assert.That(profile.Settings.OrographicSampleCount, Is.EqualTo(4));
            Assert.That(profile.Settings.WindwardRainGain,
                Is.EqualTo(0.8d).Within(0.000001d));
            Assert.That(profile.Settings.LeewardRainLoss,
                Is.EqualTo(0.6d).Within(0.000001d));
            Assert.That(profile.Settings.RiverAlgorithm,
                Is.EqualTo(RiverGenerationAlgorithm.HeightDriven));
            Assert.That(profile.Settings.RiverHydrologyRegionSize, Is.EqualTo(256));
            Assert.That(profile.Settings.RiverRunoffCellSize, Is.EqualTo(64));
            Assert.That(profile.Settings.RiverRunoffSampleStride, Is.EqualTo(8));
            Assert.That(profile.Settings.RiverMaxTraceSteps, Is.EqualTo(384));
            Assert.That(profile.Settings.RiverMinimumVisibleCourseLength, Is.EqualTo(96));
            Assert.That(profile.Settings.RiverStartFlow,
                Is.EqualTo(0.405d).Within(0.000001d));
            Assert.That(profile.Settings.RiverTributaryStartFlow,
                Is.EqualTo(0.195d).Within(0.000001d));
            Assert.That(profile.Settings.RiverFullWidthFlow,
                Is.EqualTo(1.2d).Within(0.000001d));
            Assert.That(profile.Settings.RiverMaxWidth, Is.EqualTo(7));
            Assert.That(
                profile.Settings.RiverMeanderTieTolerance,
                Is.EqualTo(0d).Within(0.000001d));
            Assert.That(profile.Settings.RiverMeanderStrength,
                Is.EqualTo(0.85d).Within(0.000001d));
            Assert.That(profile.Settings.RiverMeanderScale,
                Is.EqualTo(48d).Within(0.000001d));
            Assert.That(profile.Settings.RiverValleyDetailWeight,
                Is.EqualTo(4d).Within(0.000001d));
            Assert.That(profile.Settings.RiverLookAheadWeight,
                Is.EqualTo(0.55d).Within(0.000001d));
            Assert.That(profile.Settings.RiverLookAheadDistance, Is.EqualTo(6));
            Assert.That(profile.Settings.RiverFloodplainStartFlow,
                Is.EqualTo(0.405d).Within(0.000001d));
            Assert.That(profile.Settings.RiverFloodplainMaxRadius, Is.EqualTo(8));
            Assert.That(profile.Settings.RiverMinLakeCells, Is.EqualTo(18));
            Assert.That(profile.Settings.RiverMaxLakeCells, Is.EqualTo(220));
            Assert.That(profile.Settings.RiverMaxLakeLevelRise,
                Is.EqualTo(0.045d).Within(0.000001d));
            Assert.That(profile.Settings.RiverLakeMinFlow,
                Is.EqualTo(0.35d).Within(0.000001d));
            Assert.That(profile.NumericParameters.ContainsKey("river.noiseScale"), Is.False);

            using var world = new WorldRuntime("surface-coverage", 1);
            var generator = new DeterministicChunkGenerator();
            const int chunkRadius = 4;
            for (int chunkY = -chunkRadius; chunkY <= chunkRadius; chunkY++)
            for (int chunkX = -chunkRadius; chunkX <= chunkRadius; chunkX++)
            {
                var address = new FlatWorld.WorldModel.WorldAddress(
                    "surface",
                    new Int2(chunkX * profile.Width, chunkY * profile.Height));
                ChunkGenerationRequest request = world.BeginChunkGeneration(address, 424242, profile);
                using ChunkGenerationResult result = generator.Generate(request, CancellationToken.None);
                Assert.That(world.TryCommit(result, out string rejection), Is.True, rejection);
            }

            int riverCells = 0;
            int floodplainCells = 0;
            int alluvialCells = 0;
            int grassCells = 0;
            int typedFreshWaterCells = 0;
            foreach (ChunkRuntime chunk in world.Chunks.Values)
            {
                ChunkTerrainData terrain = chunk.Terrain;
                for (int y = 0; y < terrain.Height; y++)
                for (int x = 0; x < terrain.Width; x++)
                {
                    if (terrain.TryGetEnvironmentValue("riverDepth", x, y, out float depth) &&
                        depth > 0f)
                    {
                        riverCells++;
                        Assert.That(
                            terrain.TryGetEnvironmentValue("riverKind", x, y, out float kind),
                            Is.True);
                        Assert.That(kind, Is.EqualTo(1f).Within(0.001f)
                            .Or.EqualTo(2f).Within(0.001f));
                        Assert.That(
                            terrain.TryGetEnvironmentValue("riverSurfaceLevel", x, y, out _),
                            Is.True);
                        typedFreshWaterCells++;
                    }
                    if (terrain.TryGetEnvironmentValue("riverFloodplain", x, y,
                            out float floodplain) && floodplain > 0f)
                    {
                        floodplainCells++;
                        if ((terrain.GetCell(x, y).Flags & TerrainCellFlags.Water) == 0 &&
                            terrain.GetCell(x, y).GroundTileId == profile.Settings.SandTileId)
                            alluvialCells++;
                    }
                    Assert.That(
                        terrain.TryGetEnvironmentValue("mountain", x, y, out float mountain),
                        Is.True);
                    Assert.That(mountain, Is.InRange(0f, 1f));
                    if (terrain.GetGrass(x, y) == 2)
                        grassCells++;
                }
            }

            Assert.That(riverCells, Is.GreaterThan(100),
                "The default local window must contain clearly visible fresh-water coverage.");
            Assert.That(floodplainCells, Is.GreaterThan(100),
                "Merged low-slope rivers must expose a continuous floodplain layer.");
            Assert.That(alluvialCells, Is.GreaterThan(0),
                "The floodplain must include visible walkable alluvial sediment tiles.");
            Assert.That(grassCells, Is.GreaterThan(500),
                "The default local window must contain visible grass detail coverage.");
            Assert.That(typedFreshWaterCells, Is.EqualTo(riverCells),
                "Every fresh-water cell must expose riverKind and riverSurfaceLevel.");
        }

        [Test]
        public void WorldCoordinateScaleRescalesTerrainAndRiverDistancesTogether()
        {
            ChunkGenerationProfileSO profileAsset =
                AssetDatabase.LoadAssetAtPath<ChunkGenerationProfileSO>(
                    "Assets/Resources/Config/WorldModel/ChunkGenerationProfile_Surface.asset");
            Assert.That(profileAsset, Is.Not.Null);
            ChunkGenerationProfileSnapshot baseline = profileAsset.CreateSnapshot();
            ChunkGenerationSettingsSnapshot stretched = baseline
                .WithNumericParameter("world.coordinateScale", 0.005d)
                .Settings;
            ChunkGenerationSettingsSnapshot compressed = baseline
                .WithNumericParameter("world.coordinateScale", 0.02d)
                .Settings;

            Assert.That(stretched.WorldCoordinateDistanceScale, Is.EqualTo(2d));
            Assert.That(stretched.TerrainScale, Is.EqualTo(0.00425d).Within(0.0000001d));
            Assert.That(stretched.ClimateScale, Is.EqualTo(0.002d).Within(0.0000001d));
            Assert.That(stretched.RiverRunoffCellSize, Is.EqualTo(128));
            Assert.That(stretched.RiverRunoffSampleStride, Is.EqualTo(16));
            Assert.That(stretched.RiverMaxTraceSteps, Is.EqualTo(768));
            Assert.That(stretched.RiverMinimumVisibleCourseLength, Is.EqualTo(192));
            Assert.That(stretched.RiverMaxWidth, Is.EqualTo(10));
            Assert.That(stretched.RiverLookAheadDistance, Is.EqualTo(12));
            Assert.That(stretched.RiverMeanderScale, Is.EqualTo(96d));

            Assert.That(compressed.WorldCoordinateDistanceScale, Is.EqualTo(0.5d));
            Assert.That(compressed.TerrainScale, Is.EqualTo(0.017d).Within(0.0000001d));
            Assert.That(compressed.ClimateScale, Is.EqualTo(0.008d).Within(0.0000001d));
            Assert.That(compressed.RiverRunoffCellSize, Is.EqualTo(32));
            Assert.That(compressed.RiverRunoffSampleStride, Is.EqualTo(4));
            Assert.That(compressed.RiverMaxTraceSteps, Is.EqualTo(192));
            Assert.That(compressed.RiverMinimumVisibleCourseLength, Is.EqualTo(48));
            Assert.That(compressed.RiverMaxWidth, Is.EqualTo(5));
            Assert.That(compressed.RiverLookAheadDistance, Is.EqualTo(3));
            Assert.That(compressed.RiverMeanderScale, Is.EqualTo(24d));
        }

        [Test]
        public void SurfaceMountainRuleUsesWalkableStoneGround()
        {
            ChunkGenerationProfileSO profileAsset =
                AssetDatabase.LoadAssetAtPath<ChunkGenerationProfileSO>(
                    "Assets/Resources/Config/WorldModel/ChunkGenerationProfile_Surface.asset");
            Assert.That(profileAsset, Is.Not.Null);
            ChunkGenerationProfileSnapshot profile = profileAsset.CreateSnapshot()
                .WithNumericParameter("terrain.seaLevel", 0d)
                .WithNumericParameter("terrain.beachLevel", 0d)
                .WithNumericParameter("terrain.mountainLevel", 0d)
                .WithNumericParameter("river.enabled", 0d)
                .WithNumericParameter("structure.enabled", 0d);
            var address = new FlatWorld.WorldModel.WorldAddress("surface", new Int2(0, 0));
            using var world = new WorldRuntime("mountain-stone-coverage", 1);
            ChunkGenerationRequest request = world.BeginChunkGeneration(
                address, 424242, profile);
            using ChunkGenerationResult result = new DeterministicChunkGenerator().Generate(
                request, CancellationToken.None);
            Assert.That(world.TryCommit(result, out string rejection), Is.True, rejection);
            Assert.That(world.TryGetChunk(address, out ChunkRuntime chunk), Is.True);

            for (int y = 0; y < chunk.Terrain.Height; y++)
            for (int x = 0; x < chunk.Terrain.Width; x++)
            {
                TerrainCell cell = chunk.Terrain.GetCell(x, y);
                Assert.That(cell.GroundTileId, Is.EqualTo(profile.Settings.StoneTileId));
                Assert.That((cell.Flags & TerrainCellFlags.Walkable) != 0, Is.True);
                Assert.That((cell.Flags & TerrainCellFlags.Water) == 0, Is.True);
                Assert.That(cell.BiomeId, Is.EqualTo(7));
                Assert.That(chunk.Terrain.TryGetEnvironmentValue(
                    "mountain", x, y, out float mountain), Is.True);
                Assert.That(mountain, Is.EqualTo(1f));
                Assert.That(chunk.Terrain.GetGrass(x, y), Is.EqualTo(1));
            }
        }

        [Test]
        public void HeightDrivenRiversMoveWhenOnlyHeightMapChanges()
        {
            HashSet<Int2> broadTerrainRivers = CaptureRiverMask(
                CreateHeightDrivenProfile("height-driven-broad", 0.0085d));
            HashSet<Int2> detailedTerrainRivers = CaptureRiverMask(
                CreateHeightDrivenProfile("height-driven-detailed", 0.021d));

            Assert.That(broadTerrainRivers, Is.Not.Empty);
            Assert.That(detailedTerrainRivers, Is.Not.Empty);
            Assert.That(
                broadTerrainRivers.SetEquals(detailedTerrainRivers),
                Is.False,
                "只改变高度图后河道必须随坡向改变，不能继续由独立函数固定绘制。");
        }

        [Test]
        public void HeightDrivenRiversReuseOneBoundedRegionAcrossAdjacentChunks()
        {
            var profile = new ChunkGenerationProfileSnapshot(
                "height-driven-cache",
                DeterministicChunkGenerator.CurrentGenerationSignature,
                16,
                16,
                new Dictionary<string, double>
                {
                    ["terrain.seaLevel"] = 0,
                    ["terrain.beachLevel"] = 0,
                    ["river.enabled"] = 1,
                    ["river.hydrologyRegionSize"] = 64,
                    ["river.maxCachedRegions"] = 2,
                    ["river.runoffCellSize"] = 16,
                    ["river.runoffSampleStride"] = 8,
                    ["river.maxTraceSteps"] = 32,
                    ["river.minimumVisibleCourseLength"] = 0,
                    ["river.infiltrationFloor"] = 0,
                    ["river.startFlow"] = 0.01,
                    ["river.tributaryStartFlow"] = 0.01,
                    ["river.fullWidthFlow"] = 0.5,
                    ["river.maxWidth"] = 1,
                    ["structure.enabled"] = 0,
                    ["grass.density"] = 0
                },
                new Dictionary<string, string>
                {
                    ["river.algorithm"] = "heightDriven"
                });
            using var world = new WorldRuntime("height-driven-cache", 1);
            var generator = new DeterministicChunkGenerator();
            ChunkGenerationRequest first = world.BeginChunkGeneration(
                new FlatWorld.WorldModel.WorldAddress("surface", new Int2(0, 0)),
                1234,
                profile);
            ChunkGenerationRequest second = world.BeginChunkGeneration(
                new FlatWorld.WorldModel.WorldAddress("surface", new Int2(16, 0)),
                1234,
                profile);

            using ChunkGenerationResult firstResult = generator.Generate(first, CancellationToken.None);
            using ChunkGenerationResult secondResult = generator.Generate(second, CancellationToken.None);

            Assert.That(generator.CachedHeightDrivenRegionCount, Is.EqualTo(1),
                "同一 64×64 水文区域内的相邻区块不应重复追踪整张河网。");
        }

        [Test]
        public void DefaultSmallWrappedRuntimeTraversalContainsFreshWaterRivers()
        {
            ChunkGenerationProfileSO profileAsset =
                AssetDatabase.LoadAssetAtPath<ChunkGenerationProfileSO>(
                    "Assets/Resources/Config/WorldModel/ChunkGenerationProfile_Surface.asset");
            Assert.That(profileAsset, Is.Not.Null);
            ChunkGenerationProfileSnapshot profile = profileAsset.CreateSnapshot();
            var topology = new ChunkGenerationTopologySnapshot(
                new Int2(-64, -64), new Int2(128, 128));
            var addresses = new HashSet<FlatWorld.WorldModel.WorldAddress>();

            int startOriginX = FloorToChunkOrigin(topology.NormalizeX(362), profile.Width);
            int startOriginY = FloorToChunkOrigin(topology.NormalizeY(-109), profile.Height);
            for (int offsetY = -2; offsetY <= 2; offsetY++)
            for (int offsetX = -2; offsetX <= 2; offsetX++)
            {
                addresses.Add(new FlatWorld.WorldModel.WorldAddress(
                    "surface",
                    new Int2(
                        topology.NormalizeX(startOriginX + offsetX * profile.Width),
                        topology.NormalizeY(startOriginY + offsetY * profile.Height))));
            }

            float routeStep = profile.Width / Mathf.Sqrt(2f);
            for (int waypoint = 1; waypoint <= 8; waypoint++)
            {
                int originX = FloorToChunkOrigin(
                    topology.NormalizeX(Mathf.FloorToInt(362.5f - routeStep * waypoint)),
                    profile.Width);
                int originY = FloorToChunkOrigin(
                    topology.NormalizeY(Mathf.FloorToInt(-108.5f - routeStep * waypoint)),
                    profile.Height);
                addresses.Add(new FlatWorld.WorldModel.WorldAddress(
                    "surface", new Int2(originX, originY)));
            }

            using var world = new WorldRuntime("small-wrapped-river-coverage", 1);
            var generator = new DeterministicChunkGenerator();
            foreach (FlatWorld.WorldModel.WorldAddress address in addresses)
            {
                // GameManager stores the stable FNV-1a value of the user-facing seed "424242".
                ChunkGenerationRequest request = world.BeginChunkGeneration(
                    address, -780190301, profile, topology);
                using ChunkGenerationResult result = generator.Generate(
                    request, CancellationToken.None);
                Assert.That(world.TryCommit(result, out string rejection), Is.True, rejection);
            }

            int riverCells = 0;
            int totalCells = 0;
            foreach (ChunkRuntime chunk in world.Chunks.Values)
            {
                ChunkTerrainData terrain = chunk.Terrain;
                totalCells += terrain.CellCount;
                for (int y = 0; y < terrain.Height; y++)
                for (int x = 0; x < terrain.Width; x++)
                {
                    if (terrain.TryGetEnvironmentValue("riverDepth", x, y, out float depth) &&
                        depth > 0f)
                        riverCells++;
                }
            }

            Assert.That(riverCells, Is.GreaterThan(0),
                "The default small wrapped runtime traversal must not stay completely dry.");
            Assert.That(riverCells, Is.LessThan(totalCells / 3),
                "Fresh-water channels must not flood most of the streamed traversal window.");
        }

        [Test]
        public void DefaultWrappedTraversalDoesNotCrossACompletelyDryRegion()
        {
            ChunkGenerationProfileSO profileAsset =
                AssetDatabase.LoadAssetAtPath<ChunkGenerationProfileSO>(
                    "Assets/Resources/Config/WorldModel/ChunkGenerationProfile_Surface.asset");
            ChunkGenerationProfileSnapshot profile = profileAsset.CreateSnapshot();
            var topology = new ChunkGenerationTopologySnapshot(
                new Int2(-512, -512), new Int2(1024, 1024));
            var addresses = new HashSet<FlatWorld.WorldModel.WorldAddress>();

            for (int offsetY = -2; offsetY <= 2; offsetY++)
            for (int offsetX = -2; offsetX <= 2; offsetX++)
            {
                addresses.Add(new FlatWorld.WorldModel.WorldAddress(
                    "surface", new Int2(352 + offsetX * 16, -112 + offsetY * 16)));
            }

            float routeStep = 24f / Mathf.Sqrt(2f);
            for (int waypoint = 1; waypoint <= 12; waypoint++)
            {
                int originX = Mathf.FloorToInt((362.5f - routeStep * waypoint) / 16f) * 16;
                int originY = Mathf.FloorToInt((-108.5f - routeStep * waypoint) / 16f) * 16;
                addresses.Add(new FlatWorld.WorldModel.WorldAddress(
                    "surface",
                    new Int2(topology.NormalizeX(originX), topology.NormalizeY(originY))));
            }

            using var world = new WorldRuntime("wrapped-coverage", 1);
            var generator = new DeterministicChunkGenerator();
            foreach (FlatWorld.WorldModel.WorldAddress address in addresses)
            {
                ChunkGenerationRequest request = world.BeginChunkGeneration(
                    address, 424242, profile, topology);
                using ChunkGenerationResult result = generator.Generate(
                    request, CancellationToken.None);
                Assert.That(world.TryCommit(result, out string rejection), Is.True, rejection);
            }

            int riverCells = 0;
            int grassCells = 0;
            foreach (ChunkRuntime chunk in world.Chunks.Values)
            {
                ChunkTerrainData terrain = chunk.Terrain;
                for (int y = 0; y < terrain.Height; y++)
                for (int x = 0; x < terrain.Width; x++)
                {
                    if (terrain.TryGetEnvironmentValue("riverDepth", x, y, out float depth) &&
                        depth > 0f)
                        riverCells++;
                    if (terrain.GetGrass(x, y) == 2)
                        grassCells++;
                }
            }

            Assert.That(riverCells, Is.GreaterThan(100),
                "The deterministic wrapped traversal must encounter visible fresh water.");
            Assert.That(grassCells, Is.GreaterThan(500),
                "The deterministic wrapped traversal must retain grass detail coverage.");
        }

        private static WorldRuntime CreateCommittedWorld(int seed)
        {
            var world = new WorldRuntime("save", 1);
            var address = new FlatWorld.WorldModel.WorldAddress("surface", new Int2(0, 0));
            ChunkGenerationRequest request = world.BeginChunkGeneration(address, seed, Profile);
            using ChunkGenerationResult result = new DeterministicChunkGenerator().Generate(
                request, CancellationToken.None);
            Assert.That(world.TryCommit(result, out string rejection), Is.True, rejection);
            return world;
        }

        private static ChunkGenerationProfileSnapshot CreateHeightDrivenProfile(
            string profileId,
            double terrainScale)
        {
            return new ChunkGenerationProfileSnapshot(
                profileId,
                10,
                96,
                96,
                new Dictionary<string, double>
                {
                    ["terrain.groundTileId"] = 1,
                    ["terrain.waterTileId"] = 2,
                    ["terrain.saltWaterTileId"] = 6,
                    ["terrain.sandTileId"] = 3,
                    ["terrain.seaLevel"] = 0,
                    ["terrain.beachLevel"] = 0,
                    ["terrain.noiseScale"] = terrainScale,
                    ["terrain.octaves"] = 4,
                    ["climate.noiseScale"] = 0.004,
                    ["climate.octaves"] = 3,
                    ["river.enabled"] = 1,
                    ["river.runoffCellSize"] = 32,
                    ["river.runoffSampleStride"] = 8,
                    ["river.maxTraceSteps"] = 64,
                    ["river.minimumVisibleCourseLength"] = 0,
                    ["river.infiltrationFloor"] = 0,
                    ["river.startFlow"] = 0.01,
                    ["river.tributaryStartFlow"] = 0.01,
                    ["river.fullWidthFlow"] = 0.5,
                    ["river.maxWidth"] = 1,
                    ["river.meanderTieTolerance"] = 0.002,
                    ["structure.enabled"] = 0,
                    ["grass.density"] = 0
                },
                new Dictionary<string, string>
                {
                    ["river.algorithm"] = "heightDriven"
                });
        }

        private static HashSet<Int2> CaptureRiverMask(ChunkGenerationProfileSnapshot profile)
        {
            var address = new FlatWorld.WorldModel.WorldAddress(
                "surface",
                new Int2(-profile.Width / 2, -profile.Height / 2));
            using var world = new WorldRuntime(profile.ProfileId, 1);
            ChunkGenerationRequest request = world.BeginChunkGeneration(address, 314159, profile);
            using ChunkGenerationResult result = new DeterministicChunkGenerator().Generate(
                request,
                CancellationToken.None);
            Assert.That(world.TryCommit(result, out string rejection), Is.True, rejection);
            Assert.That(world.TryGetChunk(address, out ChunkRuntime chunk), Is.True);

            var mask = new HashSet<Int2>();
            for (int y = 0; y < chunk.Terrain.Height; y++)
            {
                for (int x = 0; x < chunk.Terrain.Width; x++)
                {
                    if (!chunk.Terrain.TryGetEnvironmentValue("riverDepth", x, y, out float depth) ||
                        depth <= 0f)
                    {
                        continue;
                    }

                    mask.Add(new Int2(address.ChunkOrigin.X + x, address.ChunkOrigin.Y + y));
                }
            }

            return mask;
        }

        private static int FloorToChunkOrigin(int value, int chunkSize)
        {
            return Mathf.FloorToInt(value / (float)chunkSize) * chunkSize;
        }
    }
}
