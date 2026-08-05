using FlatWorld.GameTest.Shared;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

namespace FlatWorld.GameTest.Map
{
    /// <summary>地图基础冒烟测试：保护 Chunk、Map 与地图资源入口。</summary>
    public sealed class MapSmokeTests
    {
        [Test]
        [Category("Map.Smoke")]
        public void RequiredEntryPointsAndAssetsExist()
        {
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Manager/ChunkMgr.cs", "ChunkMgr");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Chunk/Chunk.cs", "Chunk");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Map/Base/Map.cs", "Map");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Map/BlockingTilemapLayer.cs", "BlockingTilemapLayer");
            GameTestAssertions.AssertFolderContainsAsset("Assets/2_Prefabs/Map", "t:Prefab");
            GameTestAssertions.AssertAssetExists("Assets/Resources/Config/StructureCatalog_Default.asset");
        }

        [Test]
        [Category("Map.Smoke")]
        public void MapCoreHasExplicitTerrainNoiseChannels()
        {
            const string prefabPath = "Assets/2_Prefabs/Map/MapCore.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, $"缺少地图核心 Prefab：{prefabPath}");

            global::Map map = prefab.GetComponent<global::Map>();
            Assert.That(map, Is.Not.Null, "MapCore 缺少 Map 组件。");

            ChunkGenerator_Land land = map.mapGenerators.OfType<ChunkGenerator_Land>().SingleOrDefault();
            Assert.That(land, Is.Not.Null, "MapCore 缺少大陆生成器。");
            Assert.That(land.NoiseConfigs, Is.Not.Null.And.Not.Empty, "大陆生成器缺少噪声配置。");
            Assert.That(land.NoiseConfigs, Has.None.Null, "大陆生成器存在空噪声配置项。");
            Assert.That(
                land.NoiseConfigs.Select(noise => noise.noiseType),
                Is.EquivalentTo(new[]
                {
                    NoiseType.Height,
                    NoiseType.Temperature,
                    NoiseType.Precipitation
                }),
                "MapCore 应只配置高度、温度和降水噪声通道。");
            Assert.That(
                (NoiseType[])System.Enum.GetValues(typeof(NoiseType)),
                Is.EquivalentTo(new[]
                {
                    NoiseType.Height,
                    NoiseType.Temperature,
                    NoiseType.Precipitation
                }),
                "地形噪声枚举不应保留已删除的环境通道。");
            Assert.That((int)NoiseType.Height, Is.EqualTo(0));
            Assert.That((int)NoiseType.Precipitation, Is.EqualTo(2));
            Assert.That((int)NoiseType.Temperature, Is.EqualTo(3));
        }

        [Test]
        [Category("Map.Smoke")]
        public void DefaultBiomeResolverUsesStableOrderAndCoversNormalizedDomain()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/Map/MapCore.prefab");
            global::Map map = prefab.GetComponent<global::Map>();
            ChunkGenerator_Land land = map.LandGenerator;

            land.ValidateConfiguration();
            Assert.That(
                land.biomes.Select(biome => biome.BiomeId),
                Is.EqualTo(new[] { "desert", "stone", "beach", "grassland", "forest", "ocean" }));

            for (int temperatureStep = 0; temperatureStep <= 20; temperatureStep++)
            {
                for (int precipitationStep = 0; precipitationStep <= 20; precipitationStep++)
                {
                    for (int heightStep = 0; heightStep <= 20; heightStep++)
                    {
                        var sample = new EnvironmentSample(
                            temperatureStep / 20f,
                            0f,
                            precipitationStep / 20f,
                            heightStep / 20f);
                        Assert.That(
                            land.TryResolveBiome(sample, out BiomeData biome),
                            Is.True,
                            $"Unmatched biome at T={sample.Temperature}, P={sample.Precipitation}, H={sample.Height}");
                        Assert.That(biome.BiomeId, Is.Not.Empty);
                    }
                }
            }

            StructureDefinitionSO abandonedCamp = AssetDatabase.LoadAssetAtPath<StructureDefinitionSO>(
                "Assets/4_ScriptObjects/4-9_Structures/Definitions/abandoned_camp.asset");
            Assert.That(abandonedCamp, Is.Not.Null);
            Assert.That(
                abandonedCamp.AllowedBiomes.Select(biome => biome.BiomeId),
                Is.EqualTo(new[] { "grassland" }));
            Assert.That(abandonedCamp.IsBiomeAllowed(land.biomes[3]), Is.True);
            Assert.That(abandonedCamp.IsBiomeAllowed(land.biomes[4]), Is.False);
        }

        [Test]
        [Category("Map.Smoke")]
        public void BiomeResolverRejectsDuplicateIdsExcessCountAndInvalidRanges()
        {
            BiomeData first = ScriptableObject.CreateInstance<BiomeData>();
            BiomeData duplicate = ScriptableObject.CreateInstance<BiomeData>();
            try
            {
                first.BiomeId = "duplicate";
                first.Condition = new EnvironmentConditionRange();
                duplicate.BiomeId = "duplicate";
                duplicate.Condition = new EnvironmentConditionRange();

                Assert.Throws<System.InvalidOperationException>(
                    () => new BiomeResolver(new[] { first, duplicate }));
                Assert.Throws<System.InvalidOperationException>(
                    () => new BiomeResolver(Enumerable.Repeat(first, BiomeResolver.MaxBiomeCount + 1).ToArray()));

                duplicate.BiomeId = "invalid_range";
                duplicate.Condition.HeightRange = new Vector2(0.9f, 0.1f);
                Assert.Throws<System.InvalidOperationException>(
                    () => new BiomeResolver(new[] { first, duplicate }));
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(duplicate);
            }
        }

        [Test]
        [Category("Map.Smoke")]
        public void BurstTerrainAndHydrologyMatchPreviewSampler()
        {
            const float burstTolerance = 0.00001f;
            const float celsiusTolerance = 0.001f;
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/Map/MapCore.prefab");
            GameObject instance = Object.Instantiate(prefab);
            try
            {
                global::Map map = instance.GetComponent<global::Map>();
                map.Data = new Data_TileMap { position = Vector2Int.zero };
                ChunkGenerator_Land land = map.LandGenerator;
                ChunkGenerator_River river = map.GetGenerator<ChunkGenerator_River>();
                var planet = new PlanetData { NoiseScale = PlanetData.DefaultNoiseScale };
                const int worldSeed = 731927;
                var context = new MapGenerationContext(
                    map,
                    planet,
                    worldSeed,
                    new WorldAddress("terrain_consistency", WorldAddress.SurfaceDimensionId),
                    DimensionDefinition.CreateSurface());

                land.Generate(context);
                river.Generate(context);
                var preview = new TerrainPreviewSampler(land, river, planet, worldSeed);

                byte[] biomeIndices = land.CopyRuntimeBiomeIndices();
                Assert.That(biomeIndices, Has.Length.EqualTo(map.Data.Width * map.Data.Height));
                Assert.That(biomeIndices, Has.None.EqualTo(BiomeResolver.UnmatchedIndex));

                int[] sampleXs = new[]
                    {
                        0, 1, map.Data.Width / 4, map.Data.Width / 2,
                        map.Data.Width - 2, map.Data.Width - 1
                    }
                    .Where(value => value >= 0 && value < map.Data.Width)
                    .Distinct()
                    .ToArray();
                int[] sampleYs = new[]
                    {
                        0, 1, map.Data.Height / 4, map.Data.Height / 2,
                        map.Data.Height - 2, map.Data.Height - 1
                    }
                    .Where(value => value >= 0 && value < map.Data.Height)
                    .Distinct()
                    .ToArray();
                foreach (int x in sampleXs)
                {
                    foreach (int y in sampleYs)
                    {
                        Vector2Int world = map.Data.position + new Vector2Int(x, y);
                        Assert.That(preview.TrySample(world, out TerrainPreviewSample expected), Is.True);
                        Assert.That(context.TryGetResolvedBiome(x, y, out BiomeData actualBiome), Is.True);
                        Assert.That(actualBiome.BiomeId, Is.EqualTo(expected.Biome.BiomeId));
                        Assert.That(map.Data.EnvironmentLayers.Temperature[x, y],
                            Is.EqualTo(expected.Environment.Temperature).Within(burstTolerance));
                        Assert.That(map.Data.EnvironmentLayers.TemperatureCelsius[x, y],
                            Is.EqualTo(expected.Environment.TemperatureCelsius).Within(celsiusTolerance));
                        Assert.That(map.Data.EnvironmentLayers.Precipitation[x, y],
                            Is.EqualTo(expected.Environment.Precipitation).Within(burstTolerance));
                        Assert.That(map.Data.EnvironmentLayers.Height[x, y],
                            Is.EqualTo(expected.Environment.Height).Within(burstTolerance));

                        TileData top = map.Data.GetTopTile(world);
                        Assert.That(top is TileData_Water, Is.EqualTo(expected.HasWater));
                        if (top is TileData_Water water)
                        {
                            Assert.That(water.salt, Is.EqualTo(expected.WaterSalt).Within(0.001f));
                            Assert.That(water.deepValue, Is.EqualTo(expected.WaterDepth).Within(burstTolerance));
                        }
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        [Category("Map.Smoke")]
        public void TerrainGenerationSignatureIsStableAndIncludesNoiseConfiguration()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/Map/MapCore.prefab");
            GameObject instance = Object.Instantiate(prefab);
            try
            {
                global::Map map = instance.GetComponent<global::Map>();
                uint first = TerrainGenerationSignature.Calculate(map);
                uint repeated = TerrainGenerationSignature.Calculate(map);
                Assert.That(repeated, Is.EqualTo(first));

                ChunkGenerator_Land land = map.LandGenerator;
                TerrainNoiseConfig changed = land.NoiseConfigs[0];
                changed.frequency += 0.0005f;
                land.NoiseConfigs[0] = changed;
                Assert.That(TerrainGenerationSignature.Calculate(map), Is.Not.EqualTo(first));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        [Category("Map.Smoke")]
        public void ThrowingGenerationStageFailsChunkWithoutReadyOrTileLoaded()
        {
            GameObject chunkObject = new GameObject("FailedGenerationChunk");
            GameObject mapObject = new GameObject("FailedGenerationMap");
            mapObject.transform.SetParent(chunkObject.transform, false);
            try
            {
                Chunk chunk = chunkObject.AddComponent<Chunk>();
                global::Map map = mapObject.AddComponent<global::Map>();
                map.Data = new Data_TileMap();
                map.mapGenerators = new List<ChunkGeneratorBase>
                {
                    new NoOpBaseTerrainGenerator(),
                    new ThrowingHydrologyGenerator()
                };
                chunk.BeginFullLoad();
                bool readyRaised = false;
                chunk.OnChunkLoaded += _ => readyRaised = true;

                LogAssert.Expect(
                    LogType.Error,
                    new System.Text.RegularExpressions.Regex(
                        "\\[Map.GenerateByPipeline\\].*Hydrology.*ThrowingHydrologyGenerator",
                        System.Text.RegularExpressions.RegexOptions.Singleline));
                MethodInfo pipeline = typeof(global::Map).GetMethod(
                    "GenerateByPipelineCoroutine",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(pipeline, Is.Not.Null);
                var routine = (System.Collections.IEnumerator)pipeline.Invoke(
                    map,
                    new object[] { new PlanetData { NoiseScale = PlanetData.DefaultNoiseScale } });
                while (routine.MoveNext())
                {
                }

                Assert.That(map.HasGenerationFailed, Is.True);
                Assert.That(map.Data.TileLoaded, Is.False);
                Assert.That(map.IsTilemapVisualReady, Is.False);
                Assert.That(chunk.LifecycleState, Is.EqualTo(Chunk.ChunkLifecycleState.Failed));
                Assert.That(readyRaised, Is.False);
                Assert.That(map.ActiveGenerationContext.State, Is.EqualTo(MapGenerationState.Failed));
                Assert.That(
                    map.ActiveGenerationContext.CompletedStages,
                    Is.EqualTo(new[] { GenerationStage.BaseTerrain }));
            }
            finally
            {
                Object.DestroyImmediate(chunkObject);
            }
        }

        [Test]
        [Category("Map.Smoke")]
        public void InitialTilemapRenderUsesRowBatchesForGroundAndBlockingLayers()
        {
            const string floorId = "GameTest_BatchFloor";
            const string wallId = "GameTest_BatchWall";
            GameRes resources = GameRes.Instance;
            Tile floorAsset = ScriptableObject.CreateInstance<Tile>();
            Tile wallAsset = ScriptableObject.CreateInstance<Tile>();
            GameObject mapObject = new GameObject("BatchRenderMap", typeof(Grid));
            GameObject tilemapObject = new GameObject("GroundTilemap");
            tilemapObject.transform.SetParent(mapObject.transform, false);
            try
            {
                resources.tileBaseDict[floorId] = floorAsset;
                resources.tileBaseDict[wallId] = wallAsset;
                Tilemap tilemap = tilemapObject.AddComponent<Tilemap>();
                tilemapObject.AddComponent<TilemapRenderer>();
                tilemapObject.AddComponent<TilemapCollider2D>();
                BatchRenderMapProbe map = mapObject.AddComponent<BatchRenderMapProbe>();
                map.tileMap = tilemap;
                map.Data = new Data_TileMap { position = new Vector2Int(-5, 7) };
                map.Data.EnsureTileStorage(10, 10);

                for (int y = 0; y < 10; y++)
                {
                    for (int x = 0; x < 10; x++)
                    {
                        Vector2Int world = map.Data.position + new Vector2Int(x, y);
                        map.Data.SetBaseTile(world, new TileData_Universal
                        {
                            ID = floorId,
                            IsWalkable = true,
                            position = (Vector3Int)world
                        });
                    }
                }

                Vector2Int blockedCell = map.Data.position + new Vector2Int(4, 6);
                map.Data.PushTile(blockedCell, new TileData_Universal
                {
                    ID = wallId,
                    TileTag = BlockingTilemapLayer.BlockingTileTag,
                    IsWalkable = false,
                    position = (Vector3Int)blockedCell
                });

                map.LoadTileData_To_TileMap_Sync();

                Assert.That(map.Data.TileLoaded, Is.True);
                Assert.That(map.LastInitialRenderTileCount, Is.EqualTo(100));
                Assert.That(map.LastInitialRenderBatchCount, Is.EqualTo(11));
                Assert.That(map.LastInitialRenderBatchCount, Is.LessThan(map.LastInitialRenderTileCount));
                Assert.That(tilemap.GetTile((Vector3Int)blockedCell), Is.SameAs(floorAsset));
                BlockingTilemapLayer blockingLayer = map.GetComponent<BlockingTilemapLayer>();
                Assert.That(blockingLayer, Is.Not.Null);
                Assert.That(blockingLayer.BlockingTilemap.GetTile((Vector3Int)blockedCell), Is.SameAs(wallAsset));
            }
            finally
            {
                resources.tileBaseDict.Remove(floorId);
                resources.tileBaseDict.Remove(wallId);
                Object.DestroyImmediate(mapObject);
                Object.DestroyImmediate(floorAsset);
                Object.DestroyImmediate(wallAsset);
            }
        }

        [Test]
        [Category("Map.Smoke")]
        public void TerrainConditionsUseTemperaturePrecipitationAndHeightOnly()
        {
            var layers = new EnvironmentLayers();
            layers.EnsureSize(1, 1);
            layers.SetCell(0, 0, 0.5f, 18f, 0.7f, 0.6f);

            FieldInfo[] gridFields = typeof(EnvironmentLayers)
                .GetFields(BindingFlags.Instance | BindingFlags.Public)
                .Where(field => field.FieldType == typeof(float[,]))
                .ToArray();
            Assert.That(
                gridFields.Select(field => field.Name),
                Is.EquivalentTo(new[] { "Temperature", "TemperatureCelsius", "Precipitation", "Height", "Light" }));

            var range = new EnvironmentConditionRange
            {
                TemperatureRange = new Vector2(0.4f, 0.6f),
                PrecipitationRange = new Vector2(0.6f, 0.8f),
                HeightRange = new Vector2(0.5f, 0.7f)
            };

            Assert.That(range.IsMatch(layers, 0, 0), Is.True);

            range.HeightRange = new Vector2(0.7f, 1f);
            Assert.That(range.IsMatch(layers, 0, 0), Is.False);
        }

        [Test]
        [Category("Map.Smoke")]
        public void TerrainConfigurationAssetsContainNoRemovedEnvironmentRanges()
        {
            string biomeDirectory = Path.Combine(
                Application.dataPath,
                "4_ScriptObjects/4-8_Biome/BiomeData");
            string structureDirectory = Path.Combine(
                Application.dataPath,
                "4_ScriptObjects/4-9_Structures/Definitions");

            foreach (string path in Directory.GetFiles(biomeDirectory, "*.asset")
                         .Concat(Directory.GetFiles(structureDirectory, "*.asset")))
            {
                string assetPath = "Assets" + path.Substring(Application.dataPath.Length).Replace('\\', '/');
                Assert.That(AssetDatabase.LoadMainAssetAtPath(assetPath), Is.Not.Null, assetPath);

                string content = File.ReadAllText(path);
                Assert.That(content, Does.Not.Contain("HumidityRange"), path);
                Assert.That(content, Does.Not.Contain("SolidityRange"), path);
                Assert.That(content, Does.Not.Contain("HightRange"), path);
            }
        }

        [Test]
        [Category("Map.Smoke")]
        public void DefaultTerrainSamplerFindsWalkableLandForRepresentativeSeeds()
        {
            const string prefabPath = "Assets/2_Prefabs/Map/MapCore.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, $"缺少地图核心 Prefab：{prefabPath}");

            // 出生点在任何 Chunk 被创建前，直接读取 MapCore Prefab 的只读生成配置。
            global::Map map = prefab.GetComponent<global::Map>();
            ChunkGenerator_Land land = map?.LandGenerator;
            ChunkGenerator_River river = map?.GetGenerator<ChunkGenerator_River>();
            Assert.That(land, Is.Not.Null, "MapCore 缺少大陆生成器。");
            Assert.That(river, Is.Not.Null, "MapCore 缺少河流生成器。");

            PlanetData activePlanetData = new PlanetData
            {
                NoiseScale = PlanetData.DefaultNoiseScale
            };

            foreach (int seed in new[] { 1, 12345, -24680, 987654321 })
            {
                var random = new System.Random(seed);
                Vector2Int anchor = new Vector2Int(
                    random.Next(-512, 513),
                    random.Next(-512, 513));

                bool found = land.TryFindWalkableTerrainNear(
                    anchor,
                    seed,
                    activePlanetData,
                    river,
                    maxSearchRadius: 512,
                    maxSamples: 4096,
                    out Vector2Int position);

                Assert.That(found, Is.True, $"默认地形在种子 {seed} 附近未采样到安全陆地。");
                Assert.That(land.IsWalkableTerrainAtWorld(position, seed, activePlanetData, river), Is.True);
                Assert.That(river.TryEvaluateRiverCell(position, seed, out _), Is.False,
                    "安全出生点不能落在随后会生成河流的格子。 ");
            }
        }

        [Test]
        [Category("Map.Smoke")]
        public void ChunkGenerationUsesTimeSlicingAndSingleFlightLoading()
        {
            MethodInfo spawnAsync = typeof(ChunkGenerator_SpawnItems).GetMethod(
                nameof(ChunkGenerator_SpawnItems.GenerateAsync),
                new[] { typeof(MapGenerationContext), typeof(int) });
            MethodInfo structureAsync = typeof(ChunkGenerator_Structures).GetMethod(
                nameof(ChunkGenerator_Structures.GenerateAsync),
                new[] { typeof(MapGenerationContext), typeof(int) });

            Assert.That(spawnAsync?.DeclaringType, Is.EqualTo(typeof(ChunkGenerator_SpawnItems)));
            Assert.That(structureAsync?.DeclaringType, Is.EqualTo(typeof(ChunkGenerator_Structures)));

            const string worldManagerPath = "Assets/2_Prefabs/GameManager/WorldManager.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(worldManagerPath);
            Assert.That(prefab, Is.Not.Null);
            ChunkMgr chunkManager = prefab.GetComponentInChildren<ChunkMgr>(true);
            Assert.That(chunkManager, Is.Not.Null);

            FieldInfo concurrentField = typeof(ChunkMgr).GetField(
                "maxConcurrentChunkLoads",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(concurrentField, Is.Not.Null);
            Assert.That((int)concurrentField.GetValue(chunkManager), Is.EqualTo(1));
        }

        [Test]
        [Category("Map.Smoke")]
        public void DirectChunkLoadReusesAnAlreadyActiveChunk()
        {
            const string chunkManagerPath = "Assets/5_Scripts/5-3_GamePlay/Manager/ChunkMgr.cs";
            string source = File.ReadAllText(chunkManagerPath);
            int methodStart = source.IndexOf("public Chunk LoadChunk_By_Position", System.StringComparison.Ordinal);
            int createStart = source.IndexOf("// === 第二优先级", methodStart, System.StringComparison.Ordinal);

            Assert.That(methodStart, Is.GreaterThanOrEqualTo(0));
            Assert.That(createStart, Is.GreaterThan(methodStart));
            string activeLookupSection = source.Substring(methodStart, createStart - methodStart);
            Assert.That(activeLookupSection, Does.Contain("TryGetActiveChunkByPos(chunkPos, out Chunk activeChunk)"));
            Assert.That(activeLookupSection, Does.Contain("return activeChunk;"));
        }

        [Test]
        [Category("Map.Smoke")]
        public void FastRiverGeneratorProducesFreshWaterOnEligibleTerrain()
        {
            const string prefabPath = "Assets/2_Prefabs/Map/MapCore.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, $"Missing map prefab: {prefabPath}");

            GameObject instance = Object.Instantiate(prefab);
            try
            {
                global::Map map = instance.GetComponent<global::Map>();
                Assert.That(map, Is.Not.Null);

                ChunkGenerator_River river = map.mapGenerators.OfType<ChunkGenerator_River>().SingleOrDefault();
                Assert.That(river, Is.Not.Null);

                river.channelSpacing = 8f;
                river.channelHalfWidth = 2f;
                river.bendAmplitude = 0f;
                river.flowDirection = Vector2.up;

                map.Data = new Data_TileMap { position = Vector2Int.zero };
                Vector2Int chunkSize = Vector2Int.RoundToInt(ChunkMgr.GetChunkSize());
                map.Data.EnsureTileStorage(chunkSize.x, chunkSize.y);
                map.Data.EnsureEnvironmentStorage(chunkSize.x, chunkSize.y);
                MapGenerationContext context = new MapGenerationContext(
                    map,
                    new PlanetData { NoiseScale = PlanetData.DefaultNoiseScale },
                    12345,
                    new WorldAddress("river_test", WorldAddress.SurfaceDimensionId),
                    DimensionDefinition.CreateSurface());

                river.Generate(context);

                int freshWaterCells = 0;
                for (int x = 0; x < chunkSize.x; x++)
                {
                    for (int y = 0; y < chunkSize.y; y++)
                    {
                        if (map.GetTopTile(new Vector2Int(x, y)) is TileData_Water water &&
                            Mathf.Approximately(water.salt, 0f))
                        {
                            freshWaterCells++;
                            Assert.That(
                                map.Data.EnvironmentLayers.Precipitation[x, y],
                                Is.EqualTo(1f),
                                $"河道格 ({x}, {y}) 应写入最大降水。");
                        }
                    }
                }

                Assert.That(freshWaterCells, Is.GreaterThan(0),
                    "Fast river generator produced no fresh-water cells.");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        [Category("Map.Smoke")]
        public void NaturalBerryBushStockIsDeterministicallyOneOrTwo()
        {
            const string prefabPath = "Assets/2_Prefabs/Plant/Bush.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, $"缺少浆果丛 Prefab：{prefabPath}");

            GameObject instance = Object.Instantiate(prefab);
            try
            {
                BerryBush berryBush = instance.GetComponentInChildren<BerryBush>(true);
                Assert.That(berryBush, Is.Not.Null, "浆果丛 Prefab 缺少 BerryBush 组件。");
                Assert.That(berryBush.NaturalInitialBerryCountMin, Is.EqualTo(1));
                Assert.That(berryBush.NaturalInitialBerryCountMax, Is.EqualTo(2));

                berryBush.InitializeNaturalStock(0u);
                Assert.That(berryBush.CurrentBerryCount, Is.EqualTo(1));

                berryBush.InitializeNaturalStock(1u);
                Assert.That(berryBush.CurrentBerryCount, Is.EqualTo(2));

                berryBush.InitializeNaturalStock(2u);
                Assert.That(berryBush.CurrentBerryCount, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        [Category("Map.Smoke")]
        public void TerrainNoiseKernelInvalidParametersStillReturnFiniteValue()
        {
            var config = new TerrainNoiseConfig
            {
                noiseType = NoiseType.Height,
                octaves = 0,
                lacunarity = float.NaN,
                persistence = float.NegativeInfinity,
                coordScale = float.NaN,
                frequency = float.PositiveInfinity
            };

            float sample = TerrainNoiseKernel.Sample(config, new Vector2(128f, -64f), 12345);

            Assert.That(float.IsNaN(sample) || float.IsInfinity(sample), Is.False);
            Assert.That(sample, Is.InRange(0f, 1f));
        }

        [Test]
        [Category("Map.Smoke")]
        public void PlanetNoiseScaleUsesSharedValidationRules()
        {
            Assert.That(PlanetData.IsValidNoiseScale(PlanetData.DefaultNoiseScale), Is.True);
            Assert.That(PlanetData.IsValidNoiseScale(float.NaN), Is.False);
            Assert.That(PlanetData.IsValidNoiseScale(PlanetData.MaxNoiseScale + 1f), Is.False);
            Assert.That(PlanetData.NormalizeNoiseScale(float.PositiveInfinity), Is.EqualTo(PlanetData.DefaultNoiseScale));

            const string prefabPath = "Assets/2_Prefabs/Map/MapCore.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            GameObject instance = Object.Instantiate(prefab);
            try
            {
                ChunkGenerator_Land land = instance.GetComponent<global::Map>()?.LandGenerator;
                Assert.That(land, Is.Not.Null);

                float normalizedScale = ChunkGenerator_Land.ResolveNoiseScale(new PlanetData { NoiseScale = 0f });
                Assert.That(normalizedScale, Is.EqualTo(PlanetData.DefaultNoiseScale),
                    "旧存档中的零噪声缩放必须迁移为默认值，避免整张地图只有水或陆地。");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        [Category("Map.Smoke")]
        public void MapDoesNotBecomeReadyBeforeTilemapVisualCompletes()
        {
            GameObject mapObject = new GameObject("MapVisualReadyTest");
            try
            {
                global::Map map = mapObject.AddComponent<global::Map>();
                map.Data = new Data_TileMap { TileLoaded = true };

                Assert.That(map.IsTilemapVisualReady, Is.False);
                Assert.That(map.IsReadyForChunkLifecycle, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(mapObject);
            }
        }

        [Test]
        [Category("Map.Smoke")]
        public void GrassLayerFindsNearestGrassAndConsumesItOnce()
        {
            GameObject mapObject = new GameObject("GrassForageMapTest");
            try
            {
                global::Map map = mapObject.AddComponent<global::Map>();
                mapObject.AddComponent<GrassDetailLayer>();
                map.Data = new Data_TileMap { position = Vector2Int.zero };
                map.Data.EnsureTileStorage(4, 4);

                Vector2Int nearGrass = new Vector2Int(1, 1);
                Vector2Int farGrass = new Vector2Int(3, 3);
                map.Data.SetBaseTile(nearGrass, new TileData_Grass { ID = "Tile_Grass" });
                map.Data.SetBaseTile(farGrass, new TileData_Grass { ID = "Tile_Grass" });
                Assert.That(map.Data.TrySetGrassStateAtWorld(nearGrass, GrassCellState.Present), Is.True);
                Assert.That(map.Data.TrySetGrassStateAtWorld(farGrass, GrassCellState.Present), Is.True);

                Assert.That(
                    map.TryFindClosestGrass(new Vector2(0.5f, 0.5f), 8f, out Vector2Int found),
                    Is.True);
                Assert.That(found, Is.EqualTo(nearGrass));
                Assert.That(map.RemoveGrassAt(found), Is.True);
                Assert.That(map.RemoveGrassAt(found), Is.False, "同一朵草不能被重复食用。");
                Assert.That(
                    map.Data.TryGetGrassStateAtWorld(found, out GrassCellState state),
                    Is.True);
                Assert.That(state, Is.EqualTo(GrassCellState.Removed));
            }
            finally
            {
                Object.DestroyImmediate(mapObject);
            }
        }

        [Test]
        [Category("Map.Smoke")]
        public void GrassDensityUsesPrecipitation()
        {
            GameObject mapObject = new GameObject("GrassPrecipitationMapTest");
            try
            {
                global::Map map = mapObject.AddComponent<global::Map>();
                GrassDetailLayer grassLayer = mapObject.AddComponent<GrassDetailLayer>();
                map.Data = new Data_TileMap { position = Vector2Int.zero };
                map.Data.EnsureEnvironmentStorage(1, 1);
                map.Data.SetEnvironmentAtLocal(0, 0, 0.5f, 20f, 0f, 0.5f);

                MethodInfo getLocalDensity = typeof(GrassDetailLayer).GetMethod(
                    "GetLocalDensity",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(getLocalDensity, Is.Not.Null);

                float dryDensity = (float)getLocalDensity.Invoke(
                    grassLayer,
                    new object[] { map, Vector2Int.zero });

                map.Data.SetPrecipitationAtLocal(0, 0, 1f);
                float wetDensity = (float)getLocalDensity.Invoke(
                    grassLayer,
                    new object[] { map, Vector2Int.zero });

                Assert.That(wetDensity, Is.GreaterThan(dryDensity));
            }
            finally
            {
                Object.DestroyImmediate(mapObject);
            }
        }

        [Test]
        [Category("Map.Smoke")]
        public void BlockingLayerKeepsUnderlyingGroundVisual()
        {
            TileData floor = new TileData_Universal
            {
                ID = "Floor",
                IsWalkable = true,
                Penalty = 1000
            };
            TileData wall = new TileData_Universal
            {
                ID = "Wall",
                TileTag = BlockingTilemapLayer.BlockingTileTag,
                IsWalkable = false,
                Penalty = 0
            };

            Assert.That(BlockingTilemapLayer.IsBlockingTile(wall), Is.True);
            Assert.That(BlockingTilemapLayer.ResolveGroundTile(new[] { floor, wall }), Is.SameAs(floor));
        }

        [Test]
        [Category("Map.Smoke")]
        public void StructureContainerContentsCloneIsIndependent()
        {
            StructureContainerContents source = new StructureContainerContents
            {
                OverrideContents = true,
                TargetInventoryIndex = 0,
                TargetInventoryName = "背包模块",
                Items = new List<StructureContainerItemEntry>
                {
                    new StructureContainerItemEntry
                    {
                        SlotIndex = 1,
                        ItemPrefabId = "Dagger_Bone",
                        Amount = 1
                    }
                }
            };

            StructureContainerContents clone = source.Clone();
            clone.Items[0].ItemPrefabId = "Bonfire";

            Assert.AreEqual("Dagger_Bone", source.Items[0].ItemPrefabId);
            Assert.AreEqual("Bonfire", clone.Items[0].ItemPrefabId);
        }

        [Test]
        [Category("Map.Smoke")]
        public void StructureContainerContentsAffectCatalogHash()
        {
            StructureCatalogSO catalog = ScriptableObject.CreateInstance<StructureCatalogSO>();
            StructureDefinitionSO definition = ScriptableObject.CreateInstance<StructureDefinitionSO>();
            StructureTemplateSO template = ScriptableObject.CreateInstance<StructureTemplateSO>();
            try
            {
                definition.StructureId = "container_hash_test";
                template.TemplateId = "container_hash_template";
                template.ItemStamps.Add(new StructureItemStamp
                {
                    ItemPrefabId = "Chest_Wood",
                    MemberId = "chest_wood_1",
                    ContainerContents = new StructureContainerContents
                    {
                        OverrideContents = true,
                        TargetInventoryIndex = 0,
                        TargetInventoryName = "背包模块"
                    }
                });
                definition.Templates.Add(new WeightedStructureTemplate
                {
                    Template = template,
                    Weight = 1f
                });
                catalog.Definitions.Add(definition);

                uint emptyHash = catalog.CalculateContentHash();
                template.ItemStamps[0].ContainerContents.Items.Add(
                    new StructureContainerItemEntry
                    {
                        SlotIndex = 0,
                        ItemPrefabId = "Dagger_Bone",
                        Amount = 1
                    });
                uint configuredHash = catalog.CalculateContentHash();

                Assert.AreNotEqual(emptyHash, configuredHash);
            }
            finally
            {
                Object.DestroyImmediate(template);
                Object.DestroyImmediate(definition);
                Object.DestroyImmediate(catalog);
            }
        }

        private sealed class NoOpBaseTerrainGenerator : ChunkGeneratorBase
        {
            public override GenerationStage Stage => GenerationStage.BaseTerrain;

            public override System.Collections.IEnumerator GenerateAsync(
                MapGenerationContext context,
                int workBatchSize)
            {
                context.Map.Data.EnsureTileStorage(1, 1);
                yield break;
            }
        }

        private sealed class ThrowingHydrologyGenerator : ChunkGeneratorBase
        {
            public override GenerationStage Stage => GenerationStage.Hydrology;

            public override System.Collections.IEnumerator GenerateAsync(
                MapGenerationContext context,
                int workBatchSize)
            {
                yield return null;
                throw new System.InvalidOperationException("Injected hydrology failure");
            }
        }

        private sealed class BatchRenderMapProbe : global::Map
        {
            protected override bool ShouldBakePenaltyAfterTilemapLoad => false;
        }
    }
}
