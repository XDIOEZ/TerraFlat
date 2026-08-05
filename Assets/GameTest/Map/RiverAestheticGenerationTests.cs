using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FlatWorld.GameTest.Map
{
    public sealed class RiverAestheticGenerationTests
    {
        private const string MapPrefabPath = "Assets/2_Prefabs/Map/MapCore.prefab";

        [Test]
        [Category("Map.Hydrology")]
        public void DefaultRiverConfigurationIsPerformanceFirst()
        {
            ChunkGenerator_River river = LoadRiverGenerator();

            Assert.That(river.channelSpacing, Is.GreaterThanOrEqualTo(64f));
            Assert.That(river.channelHalfWidth, Is.InRange(0.5f, 3f));
            Assert.That(river.bendFrequency, Is.LessThanOrEqualTo(0.02f));
        }

        [Test]
        [Category("Map.Hydrology")]
        public void RiverQueryIsDeterministicAndUsesWorldCoordinates()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MapPrefabPath);
            GameObject instance = Object.Instantiate(prefab);
            try
            {
                ChunkGenerator_River river = instance.GetComponent<global::Map>()
                    .GetGenerator<ChunkGenerator_River>();
                river.channelSpacing = 16f;
                river.channelHalfWidth = 1f;
                river.bendAmplitude = 0f;
                river.flowDirection = Vector2.up;

                bool first = river.TryEvaluateRiverCell(new Vector2Int(0, 99), out float firstDepth);
                bool adjacentChunk = river.TryEvaluateRiverCell(new Vector2Int(0, 100), out float adjacentDepth);
                bool repeated = river.TryEvaluateRiverCell(new Vector2Int(0, 99), out float repeatedDepth);

                Assert.That(first, Is.True);
                Assert.That(adjacentChunk, Is.True, "River must continue across chunk boundaries.");
                Assert.That(repeated, Is.EqualTo(first));
                Assert.That(repeatedDepth, Is.EqualTo(firstDepth).Within(0.000001f));
                Assert.That(adjacentDepth, Is.GreaterThan(0f));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        [Category("Map.Hydrology")]
        public void LegacyHydrologyBuffersAreNotPartOfRuntimeGenerator()
        {
            Assert.That(typeof(ChunkGenerator_River).GetField("hydrologyHalo"), Is.Null);
            Assert.That(typeof(ChunkGenerator_River).GetField("hydrologyCellsPerFrame"), Is.Null);
            Assert.That(typeof(ChunkGenerator_River).GetField("biomeHydrologyRules"), Is.Null);
        }

        [TestCase(ChunkGenerator_River.RiverWriteMode.ReplaceTop, 2, 0)]
        [TestCase(ChunkGenerator_River.RiverWriteMode.AddLayer, 3, 1)]
        [TestCase(ChunkGenerator_River.RiverWriteMode.ReplaceAll, 1, 0)]
        [Category("Map.Hydrology")]
        public void RiverWriteModesProduceFreshWaterAndExpectedStackShape(
            ChunkGenerator_River.RiverWriteMode mode,
            int expectedLayerCount,
            int expectedOverflowAllocations)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MapPrefabPath);
            GameObject instance = Object.Instantiate(prefab);
            try
            {
                global::Map map = instance.GetComponent<global::Map>();
                map.Data = new Data_TileMap { position = Vector2Int.zero };
                map.Data.EnsureTileStorage(3, 1);
                map.Data.EnsureEnvironmentStorage(3, 1);
                for (int x = 0; x < 3; x++)
                    map.Data.SetEnvironmentAtLocal(x, 0, 0.5f, 25f, 0.2f, 0.7f);

                Vector2Int riverCell = Vector2Int.zero;
                map.Data.SetBaseTile(riverCell, NewTerrainTile("base"));
                map.Data.PushTile(riverCell, NewTerrainTile("overlay"));

                ChunkGenerator_River river = map.GetGenerator<ChunkGenerator_River>();
                river.writeMode = mode;
                river.channelSpacing = 8f;
                river.channelHalfWidth = 0.5f;
                river.bendAmplitude = 0f;
                river.widthVariation = 0f;
                river.flowDirection = Vector2.up;
                river.riverDepthMin = 0.5f;
                river.riverDepthMax = 0.5f;
                var context = new MapGenerationContext(
                    map,
                    new PlanetData { NoiseScale = PlanetData.DefaultNoiseScale },
                    12345,
                    new WorldAddress("river_modes", WorldAddress.SurfaceDimensionId),
                    DimensionDefinition.CreateSurface());

                river.Generate(context);

                Assert.That(map.Data.GetLayerCount(riverCell), Is.EqualTo(expectedLayerCount));
                Assert.That(map.Data.CountOverflowAllocations(), Is.EqualTo(expectedOverflowAllocations));
                Assert.That(map.Data.GetTopTile(riverCell), Is.TypeOf<TileData_Water>());
                TileData_Water water = (TileData_Water)map.Data.GetTopTile(riverCell);
                Assert.That(water.salt, Is.Zero.Within(0.001f));
                Assert.That(water.deepValue, Is.EqualTo(0.5f).Within(0.000001f));
                Assert.That(map.Data.EnvironmentLayers.Precipitation[0, 0], Is.EqualTo(1f));

                if (mode != ChunkGenerator_River.RiverWriteMode.ReplaceAll)
                    Assert.That(map.Data.GetTileAt(riverCell, 0).ID, Is.EqualTo("base"));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        [Category("Map.Hydrology")]
        public void TerrainPreviewKeepsSeaWaterWhenRiverMaskCrossesOcean()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MapPrefabPath);
            global::Map map = prefab.GetComponent<global::Map>();
            ChunkGenerator_Land land = map.LandGenerator;
            ChunkGenerator_River river = map.GetGenerator<ChunkGenerator_River>();
            var planet = new PlanetData { NoiseScale = PlanetData.DefaultNoiseScale };
            const int worldSeed = 43891;
            var preview = new TerrainPreviewSampler(land, river, planet, worldSeed);

            bool found = false;
            for (int y = -512; y <= 512 && !found; y += 3)
            {
                for (int x = -512; x <= 512; x += 3)
                {
                    Vector2Int world = new Vector2Int(x, y);
                    if (!river.TryEvaluateRiverCell(world, worldSeed, out _))
                        continue;

                    EnvironmentSample baseEnvironment = land.SampleEnvironmentAtWorld(world, worldSeed, planet);
                    if (!land.TryResolveBiome(baseEnvironment, out BiomeData biome) || biome.BiomeId != "ocean")
                        continue;

                    Assert.That(preview.TrySample(world, out TerrainPreviewSample sample), Is.True);
                    Assert.That(sample.Biome.BiomeId, Is.EqualTo("ocean"));
                    Assert.That(sample.IsRiver, Is.False);
                    Assert.That(sample.HasWater, Is.True);
                    Assert.That(sample.WaterSalt, Is.EqualTo(80f).Within(0.01f));
                    Assert.That(sample.Environment.Precipitation,
                        Is.EqualTo(baseEnvironment.Precipitation).Within(0.000001f));
                    found = true;
                    break;
                }
            }

            Assert.That(found, Is.True, "Expected to sample at least one river-mask cell over the ocean biome.");
        }

        private static ChunkGenerator_River LoadRiverGenerator()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MapPrefabPath);
            Assert.That(prefab, Is.Not.Null, $"Missing map prefab: {MapPrefabPath}");

            global::Map map = prefab.GetComponent<global::Map>();
            Assert.That(map, Is.Not.Null);

            ChunkGenerator_River river =
                map.mapGenerators.OfType<ChunkGenerator_River>().SingleOrDefault();
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
    }
}
