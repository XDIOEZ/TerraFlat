using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FlatWorld.GameTest.Map
{
    public sealed class RiverAestheticGenerationTests
    {
        private const string MapPrefabPath = "Assets/2_Prefabs/Map/MapCore.prefab";

        [TearDown]
        public void TearDown()
        {
            ChunkGenerator_River.ClearHydrologyCache();
        }

        [Test]
        [Category("Map.Hydrology")]
        public void DefaultConfigurationUsesRegionalHeightDrivenHydrology()
        {
            ChunkGenerator_River river = LoadRiverGenerator();

            Assert.That(river.hydrologyRegionSize, Is.EqualTo(256));
            Assert.That(river.runoffCellSize, Is.EqualTo(64));
            Assert.That(river.runoffSampleStride, Is.EqualTo(8));
            Assert.That(river.maxTraceSteps, Is.EqualTo(512));
            Assert.That(river.maxRiverWidth, Is.EqualTo(5));
            Assert.That(river.minLakeCells, Is.EqualTo(18));
            Assert.That(river.maxLakeCells, Is.EqualTo(220));
            Assert.That(river.maxCachedRegions, Is.EqualTo(9));
            Assert.That(TerrainGenerationSignature.CurrentVersion, Is.EqualTo(5));

            string[] removedMaskFields =
            {
                "channelSpacing", "channelHalfWidth", "bendFrequency",
                "bendAmplitude", "widthVariation", "flowDirection"
            };
            foreach (string field in removedMaskFields)
                Assert.That(typeof(ChunkGenerator_River).GetField(field), Is.Null, field);
        }

        [Test]
        [Category("Map.Hydrology")]
        public void RegionalWindIsDeterministicSeededAndSmoothAcrossRegionBoundaries()
        {
            IWindFieldProvider provider = RegionalRandomWindFieldProvider.Instance;
            WindFieldConfig config = WindFieldConfig.Default;
            Vector2Int point = new(187, -91);

            Vector2 first = provider.Sample(point, 42017, config).Direction;
            Vector2 repeated = provider.Sample(point, 42017, config).Direction;
            Assert.That(repeated.x, Is.EqualTo(first.x).Within(0.000001f));
            Assert.That(repeated.y, Is.EqualTo(first.y).Within(0.000001f));
            Assert.That(first.magnitude, Is.EqualTo(1f).Within(0.0001f));

            bool seedChangedField = false;
            for (int i = 0; i < 8; i++)
            {
                Vector2Int samplePoint = new(i * 79 - 240, i * 53 - 170);
                Vector2 a = provider.Sample(samplePoint, 42017, config).Direction;
                Vector2 b = provider.Sample(samplePoint, 42018, config).Direction;
                seedChangedField |= Vector2.Dot(a, b) < 0.99f;
            }
            Assert.That(seedChangedField, Is.True);

            Vector2 left = provider.Sample(new Vector2Int(255, 83), 42017, config).Direction;
            Vector2 right = provider.Sample(new Vector2Int(257, 83), 42017, config).Direction;
            Assert.That(Vector2.Angle(left, right), Is.LessThan(2f));
        }

        [Test]
        [Category("Map.Hydrology")]
        public void OrographicClimateIncreasesWindwardRainAndReducesLeewardRain()
        {
            const float baseRain = 0.5f;
            float windward = ClimateFieldKernel.ApplyOrographicPrecipitation(
                baseRain, 0.75f, 0.55f, 0.58f, 0.8f, 0.6f);
            float leeward = ClimateFieldKernel.ApplyOrographicPrecipitation(
                baseRain, 0.45f, 0.55f, 0.75f, 0.8f, 0.6f);

            Assert.That(windward, Is.GreaterThan(baseRain));
            Assert.That(leeward, Is.LessThan(baseRain));
        }

        [Test]
        [Category("Map.Hydrology")]
        public void HydrologyIsDeterministicAndWorldSeedChangesEqualHeightChoices()
        {
            GameObject instance = InstantiateMap(out ChunkGenerator_River river);
            try
            {
                ConfigureFast(river);
                var land = new AnalyticLandGenerator(
                    world => Mathf.Clamp01(0.82f - world.x * 0.0015f),
                    _ => 1f);

                string first = CaptureRegion(land, river, 53191);
                int cacheCountAfterFirst = ChunkGenerator_River.CachedRegionCount;
                string repeated = CaptureRegion(land, river, 53191);
                string differentSeed = CaptureRegion(land, river, 53192);

                Assert.That(repeated, Is.EqualTo(first));
                Assert.That(ChunkGenerator_River.CachedRegionCount,
                    Is.EqualTo(cacheCountAfterFirst + 1),
                    "Repeated seed must reuse its region; only the different seed adds one cache entry.");
                Assert.That(differentSeed, Is.Not.EqualTo(first));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        [Category("Map.Hydrology")]
        public void TributariesMergeWidenAndContinueAcrossHydrologyRegions()
        {
            GameObject instance = InstantiateMap(out ChunkGenerator_River river);
            try
            {
                ConfigureFast(river);
                river.fullWidthFlow = 3f;
                var land = new AnalyticLandGenerator(
                    world => Mathf.Clamp01(
                        0.82f - world.x * 0.0015f + Mathf.Abs(world.y - 31.5f) * 0.002f),
                    _ => 1f);
                Bind(land, river, 88271);

                float maximumFlow = 0f;
                Vector2Int maximumPosition = default;
                int widestColumn = 0;
                for (int x = 0; x < 128; x++)
                {
                    int columnWater = 0;
                    for (int y = 0; y < 64; y++)
                    {
                        if (!river.TrySampleHydrologyCell(new Vector2Int(x, y), 88271, out HydrologyCellSample sample))
                            continue;
                        columnWater++;
                        if (sample.Flow > maximumFlow)
                        {
                            maximumFlow = sample.Flow;
                            maximumPosition = new Vector2Int(x, y);
                        }
                    }
                    widestColumn = Mathf.Max(widestColumn, columnWater);
                }

                bool crossesBoundary = Enumerable.Range(0, 64).Any(y =>
                    river.TrySampleHydrologyCell(new Vector2Int(63, y), 88271, out HydrologyCellSample left) &&
                    river.TrySampleHydrologyCell(new Vector2Int(64, y), 88271, out HydrologyCellSample right) &&
                    left.WaterKind == HydrologyWaterKind.River &&
                    right.WaterKind == HydrologyWaterKind.River);

                Assert.That(maximumFlow, Is.GreaterThan(1f), $"No merged runoff near {maximumPosition}.");
                Assert.That(widestColumn, Is.GreaterThanOrEqualTo(3));
                Assert.That(crossesBoundary, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        [Category("Map.Hydrology")]
        public void DiagonalRunoffAddsOrthogonalBridgeCell()
        {
            GameObject instance = InstantiateMap(out ChunkGenerator_River river);
            try
            {
                ConfigureFast(river);
                river.maxRiverWidth = 1;
                var land = new AnalyticLandGenerator(
                    world => Mathf.Clamp01(0.9f - world.x * 0.002f - world.y * 0.002f),
                    _ => 1f);
                Bind(land, river, 44551);

                Assert.That(river.TrySampleHydrologyCell(new Vector2Int(2, 2), 44551, out _), Is.True);
                Assert.That(river.TrySampleHydrologyCell(new Vector2Int(3, 3), 44551, out _), Is.True);
                bool hasBridge =
                    river.TrySampleHydrologyCell(new Vector2Int(3, 2), 44551, out _) ||
                    river.TrySampleHydrologyCell(new Vector2Int(2, 3), 44551, out _);
                Assert.That(hasBridge, Is.True, "Diagonal D8 steps must be 4-neighbour connected.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        [Category("Map.Hydrology")]
        public void BoundedBasinCreatesLakeAndUsesLowestSpillOutlet()
        {
            GameObject instance = InstantiateMap(out ChunkGenerator_River river);
            try
            {
                ConfigureFast(river);
                river.maxLakeCells = 80;
                river.maxLakeLevelRise = 0.05f;
                var land = new AnalyticLandGenerator(SpillBasinHeight, _ => 1f);
                Bind(land, river, 314159);

                var lakes = new List<Vector2Int>();
                var downstreamRivers = new List<Vector2Int>();
                for (int y = 0; y < 64; y++)
                {
                    for (int x = 0; x < 64; x++)
                    {
                        if (!river.TrySampleHydrologyCell(new Vector2Int(x, y), 314159, out HydrologyCellSample sample))
                            continue;
                        if (sample.WaterKind == HydrologyWaterKind.Lake)
                            lakes.Add(new Vector2Int(x, y));
                        else if (sample.WaterKind == HydrologyWaterKind.River && x > 35)
                            downstreamRivers.Add(new Vector2Int(x, y));
                    }
                }

                Assert.That(lakes.Count, Is.InRange(river.minLakeCells, river.maxLakeCells));
                Assert.That(lakes.Any(cell => cell.x == 32 && cell.y == 32), Is.True);
                Assert.That(downstreamRivers, Is.Not.Empty, "The lake spill outlet must continue downstream.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        [Category("Map.Hydrology")]
        public void FreshWaterWritingProtectsSeaAndDoesNotRewritePrecipitation()
        {
            GameObject instance = InstantiateMap(out ChunkGenerator_River river);
            try
            {
                ConfigureFast(river);
                global::Map map = instance.GetComponent<global::Map>();
                var land = new AnalyticLandGenerator(
                    world => Mathf.Clamp01(0.82f - world.x * 0.0015f),
                    _ => 1f);
                map.mapGenerators = new List<ChunkGeneratorBase> { land, river };
                land.Init(map);
                river.Init(map);
                map.Data = new Data_TileMap { position = Vector2Int.zero };
                map.Data.EnsureTileStorage(16, 16);
                map.Data.EnsureEnvironmentStorage(16, 16);
                for (int y = 0; y < 16; y++)
                {
                    for (int x = 0; x < 16; x++)
                    {
                        map.Data.SetBaseTile(new Vector2Int(x, y), NewTerrainTile("land"));
                        map.Data.SetEnvironmentAtLocal(x, y, 0.5f, 20f, 0.37f, land.SampleHeightAtWorld(new Vector2Int(x, y), 7761));
                    }
                }

                Bind(land, river, 7761);
                List<Vector2Int> waterCells = Enumerable.Range(0, 16)
                    .SelectMany(y => Enumerable.Range(0, 16).Select(x => new Vector2Int(x, y)))
                    .Where(cell => river.TrySampleHydrologyCell(cell, 7761, out _))
                    .Take(2)
                    .ToList();
                Assert.That(waterCells, Has.Count.EqualTo(2));

                Vector2Int oceanPosition = waterCells[0];
                var ocean = (TileData_Water)river.riverTileBlock.tileDataTemplate.Clone();
                ocean.ID = "ocean";
                ocean.Name = "ocean";
                ocean.salt = 80f;
                ocean.deepValue = 1f;
                map.Data.ClearCell(oceanPosition);
                map.Data.SetBaseTile(oceanPosition, (TileData)ocean);
                var context = new MapGenerationContext(
                    map,
                    new PlanetData { NoiseScale = PlanetData.DefaultNoiseScale },
                    7761,
                    new WorldAddress("hydrology_write", WorldAddress.SurfaceDimensionId),
                    DimensionDefinition.CreateSurface());
                river.Generate(context);

                Assert.That(((TileData_Water)map.Data.GetTopTile(oceanPosition)).salt, Is.EqualTo(80f));
                Assert.That(map.Data.GetTopTile(waterCells[1]), Is.TypeOf<TileData_Water>());
                Assert.That(((TileData_Water)map.Data.GetTopTile(waterCells[1])).salt, Is.Zero);
                for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                    Assert.That(map.Data.EnvironmentLayers.Precipitation[x, y], Is.EqualTo(0.37f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        [Category("Map.Hydrology")]
        public void CacheReusesBoundsCancelsAndReleasesRegionalWork()
        {
            GameObject instance = InstantiateMap(out ChunkGenerator_River river);
            try
            {
                ConfigureFast(river);
                river.maxCachedRegions = 3;
                var land = new AnalyticLandGenerator(
                    world => Mathf.Clamp01(0.85f - world.x * 0.0002f),
                    _ => 1f);
                Bind(land, river, 7654);
                for (int region = 0; region < 6; region++)
                    river.TrySampleHydrologyCell(new Vector2Int(region * 64 + 8, 8), 7654, out _);
                Assert.That(ChunkGenerator_River.CachedRegionCount, Is.LessThanOrEqualTo(3));
                Assert.That(ChunkGenerator_River.CompletedCachedRegionCount,
                    Is.EqualTo(ChunkGenerator_River.CachedRegionCount));

                ChunkGenerator_River.ClearHydrologyCache();
                global::Map map = instance.GetComponent<global::Map>();
                map.mapGenerators = new List<ChunkGeneratorBase> { land, river };
                land.Init(map);
                river.Init(map);
                map.Data = new Data_TileMap { position = Vector2Int.zero };
                map.Data.EnsureTileStorage(16, 16);
                map.Data.EnsureEnvironmentStorage(16, 16);
                var context = new MapGenerationContext(
                    map,
                    new PlanetData { NoiseScale = PlanetData.DefaultNoiseScale },
                    7654,
                    new WorldAddress("cancel_hydrology", WorldAddress.SurfaceDimensionId),
                    DimensionDefinition.CreateSurface());
                IEnumerator routine = river.GenerateAsync(context, 1);
                Assert.That(routine.MoveNext(), Is.True);
                context.Cancel("test");
                Assert.That(routine.MoveNext(), Is.False);
                Assert.That(ChunkGenerator_River.CachedRegionCount, Is.EqualTo(1));
                Assert.That(ChunkGenerator_River.CompletedCachedRegionCount, Is.Zero);

                ChunkGenerator_River.ClearHydrologyCache();
                Assert.That(ChunkGenerator_River.CachedRegionCount, Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        [Category("Map.Smoke")]
        public void HydrologyRegionLoadOrderDoesNotChangeBoundaryResults()
        {
            GameObject instance = InstantiateMap(out ChunkGenerator_River river);
            try
            {
                ConfigureFast(river);
                var land = new AnalyticLandGenerator(
                    world => Mathf.Clamp01(
                        0.82f - world.x * 0.0015f + Mathf.Abs(world.y - 31.5f) * 0.002f),
                    _ => 1f);
                Vector2Int[] forwardOrder =
                {
                    new(60, 28), new(63, 31), new(64, 31), new(67, 34)
                };
                Vector2Int[] reverseOrder = forwardOrder.Reverse().ToArray();

                Dictionary<Vector2Int, string> forward = CaptureCells(land, river, 91827, forwardOrder);
                ChunkGenerator_River.ClearHydrologyCache();
                Dictionary<Vector2Int, string> reverse = CaptureCells(land, river, 91827, reverseOrder);

                Assert.That(reverse, Is.EquivalentTo(forward));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static Dictionary<Vector2Int, string> CaptureCells(
            ChunkGenerator_Land land,
            ChunkGenerator_River river,
            int worldSeed,
            IEnumerable<Vector2Int> positions)
        {
            Bind(land, river, worldSeed);
            var output = new Dictionary<Vector2Int, string>();
            foreach (Vector2Int position in positions)
            {
                bool hasWater = river.TrySampleHydrologyCell(position, worldSeed, out HydrologyCellSample sample);
                output[position] = hasWater
                    ? $"{(int)sample.WaterKind}:{sample.Flow:F6}:{sample.Depth:F6}:{sample.SurfaceLevel:F6}"
                    : "0";
            }
            return output;
        }

        private static string CaptureRegion(
            ChunkGenerator_Land land,
            ChunkGenerator_River river,
            int worldSeed)
        {
            Bind(land, river, worldSeed);
            var values = new List<string>();
            for (int y = 0; y < 64; y += 2)
            for (int x = 0; x < 64; x += 2)
            {
                bool hasWater = river.TrySampleHydrologyCell(
                    new Vector2Int(x, y),
                    worldSeed,
                    out HydrologyCellSample sample);
                values.Add(hasWater
                    ? $"{(int)sample.WaterKind}:{sample.Flow:F3}:{sample.Depth:F3}"
                    : "0");
            }
            return string.Join("|", values);
        }

        private static void Bind(ChunkGenerator_Land land, ChunkGenerator_River river, int worldSeed)
        {
            _ = new TerrainPreviewSampler(
                land,
                river,
                new PlanetData { NoiseScale = PlanetData.DefaultNoiseScale },
                worldSeed);
        }

        private static float SpillBasinHeight(Vector2Int world)
        {
            if (world.y == 32 && world.x >= 36)
                return Mathf.Clamp(0.61f - (world.x - 36) * 0.0015f, 0.51f, 0.95f);
            int ring = Mathf.Max(Mathf.Abs(world.x - 32), Mathf.Abs(world.y - 32));
            return Mathf.Clamp(0.6f + ring * 0.005f, 0.51f, 0.95f);
        }

        private static void ConfigureFast(ChunkGenerator_River river)
        {
            river.hydrologyRegionSize = 64;
            river.runoffCellSize = 16;
            river.runoffSampleStride = 4;
            river.maxTraceSteps = 32;
            river.seaLevel = 0.5f;
            river.infiltrationFloor = 0f;
            river.riverStartFlow = 0.1f;
            river.fullWidthFlow = 2f;
            river.maxRiverWidth = 5;
            river.meanderTieTolerance = 0.002f;
            river.minLakeCells = 18;
            river.maxLakeCells = 64;
            river.maxLakeLevelRise = 0.045f;
            river.lakeMinFlow = 0.1f;
            river.maxCachedRegions = 9;
        }

        private static GameObject InstantiateMap(out ChunkGenerator_River river)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MapPrefabPath);
            Assert.That(prefab, Is.Not.Null, $"Missing map prefab: {MapPrefabPath}");
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            river = instance.GetComponent<global::Map>().GetGenerator<ChunkGenerator_River>();
            Assert.That(river, Is.Not.Null);
            return instance;
        }

        private static ChunkGenerator_River LoadRiverGenerator()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MapPrefabPath);
            Assert.That(prefab, Is.Not.Null, $"Missing map prefab: {MapPrefabPath}");
            ChunkGenerator_River river = prefab.GetComponent<global::Map>()
                .mapGenerators.OfType<ChunkGenerator_River>()
                .SingleOrDefault();
            Assert.That(river, Is.Not.Null);
            return river;
        }

        private static TileData NewTerrainTile(string id)
        {
            return new TileData_Universal
            {
                ID = id,
                Name = id,
                IsWalkable = true
            };
        }

        private sealed class AnalyticLandGenerator : ChunkGenerator_Land
        {
            private readonly Func<Vector2Int, float> _height;
            private readonly Func<Vector2Int, float> _rain;

            public AnalyticLandGenerator(
                Func<Vector2Int, float> height,
                Func<Vector2Int, float> rain)
            {
                _height = height;
                _rain = rain;
            }

            public override void ValidateConfiguration()
            {
            }

            public override float SampleHeightAtWorld(
                Vector2Int worldPosition,
                int worldSeed,
                PlanetData sourcePlanetData = null)
            {
                return Mathf.Clamp01(_height(worldPosition));
            }

            public override ClimateSample SampleClimateAtWorld(
                Vector2Int worldPosition,
                int worldSeed,
                PlanetData sourcePlanetData)
            {
                float height = SampleHeightAtWorld(worldPosition, worldSeed, sourcePlanetData);
                float rain = Mathf.Clamp01(_rain(worldPosition));
                return new ClimateSample(
                    new EnvironmentSample(0.5f, 20f, rain, height),
                    rain,
                    new WindSample(Vector2.right));
            }
        }
    }
}
