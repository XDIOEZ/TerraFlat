using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace FlatWorld.GameTest.Map
{
    public sealed class WorldTopologySmokeTests
    {
        private static WorldTopologyBounds CreateBounds(int radius = 17, int chunkX = 16, int chunkY = 12)
        {
            var planet = new PlanetData
            {
                Radius = radius,
                ChunkSize = new Vector2Int(chunkX, chunkY),
                TopologyMode = WorldTopologyMode.Wrapped
            };
            Assert.That(WorldTopologyBounds.TryCreate(planet, out WorldTopologyBounds bounds), Is.True);
            return bounds;
        }

        [Test]
        [Category("Map.Smoke")]
        public void WrappedBoundsAlignPerAxisAndUseExclusiveUpperBound()
        {
            WorldTopologyBounds bounds = CreateBounds();
            Assert.That(bounds.HalfExtent, Is.EqualTo(new Vector2Int(32, 24)));
            Assert.That(bounds.Min, Is.EqualTo(new Vector2Int(-32, -24)));
            Assert.That(bounds.MaxExclusive, Is.EqualTo(new Vector2Int(32, 24)));
            Assert.That(bounds.Span, Is.EqualTo(new Vector2Int(64, 48)));
            Assert.That(bounds.Contains(new Vector2(-32f, -24f)), Is.True);
            Assert.That(bounds.Contains(new Vector2(32f, 0f)), Is.False);
            Assert.That(bounds.NormalizePosition(new Vector2(32f, 24f)),
                Is.EqualTo(new Vector2(-32f, -24f)));
        }

        [Test]
        [Category("Map.Smoke")]
        public void PositionCellAndChunkNormalizationCoverEdgesCornersNegativesAndMultipleSpans()
        {
            WorldTopologyBounds bounds = CreateBounds();

            Assert.That(bounds.NormalizePosition(new Vector2(33.25f, -25.5f)),
                Is.EqualTo(new Vector2(-30.75f, 22.5f)));
            Assert.That(bounds.NormalizePosition(new Vector2(-33.25f, 25.5f)),
                Is.EqualTo(new Vector2(30.75f, -22.5f)));
            Assert.That(bounds.NormalizePosition(new Vector2(32f + 64f * 4f + 2.5f, -24f - 48f * 3f - 1.25f)),
                Is.EqualTo(new Vector2(-29.5f, 22.75f)));
            Assert.That(bounds.NormalizeCell(new Vector2Int(32, -25)),
                Is.EqualTo(new Vector2Int(-32, 23)));
            Assert.That(bounds.NormalizeChunkOrigin(new Vector2Int(32, 24)),
                Is.EqualTo(new Vector2Int(-32, -24)));
        }

        [Test]
        [Category("Map.Smoke")]
        public void ShortestDeltaCrossesEachSeamInsteadOfTraversingTheWorld()
        {
            WorldTopologyBounds bounds = CreateBounds();
            Assert.That(bounds.ShortestDelta(new Vector2(31f, 0f), new Vector2(-31f, 0f)),
                Is.EqualTo(new Vector2(2f, 0f)));
            Assert.That(bounds.ShortestDelta(new Vector2(0f, -23f), new Vector2(0f, 23f)),
                Is.EqualTo(new Vector2(0f, -2f)));
            Assert.That(bounds.ShortestDelta(new Vector2Int(31, 23), new Vector2Int(-31, -23)),
                Is.EqualTo(new Vector2Int(2, 2)));
        }

        [Test]
        [Category("Map.Smoke")]
        public void TinyWorldChunkWindowIsCanonicalDeduplicatedAndNeverOutsideBounds()
        {
            WorldTopologyBounds bounds = CreateBounds(radius: 16, chunkX: 16, chunkY: 16);
            HashSet<Vector2Int> window = bounds.BuildChunkWindow(new Vector2Int(0, 0), 4);

            Assert.That(window.Count, Is.EqualTo(4), "A 2x2-chunk torus must not duplicate chunks in a 9x9 request window.");
            Assert.That(window.All(bounds.Contains), Is.True);
            Assert.That(window.All(pos => pos.x % 16 == 0 && pos.y % 16 == 0), Is.True);

            string[] saveKeys = window.Select(pos => pos.ToString()).ToArray();
            Assert.That(saveKeys.Distinct().Count(), Is.EqualTo(window.Count));
            Assert.That(saveKeys, Has.None.EqualTo(new Vector2Int(16, 0).ToString()));
        }

        [Test]
        [Category("Map.Smoke")]
        public void InfiniteWorldDoesNotConstructWrappedBounds()
        {
            var planet = new PlanetData
            {
                Radius = 1,
                ChunkSize = Vector2Int.zero,
                TopologyMode = WorldTopologyMode.Infinite
            };
            Assert.That(WorldTopologyBounds.TryCreate(planet, out _), Is.False);
        }

        [Test]
        [Category("Map.Smoke")]
        public void PeriodicTerrainNoiseRepeatsAcrossSidesCornersAndMultipleSpans()
        {
            WorldTopologyBounds bounds = CreateBounds(radius: 31, chunkX: 16, chunkY: 16);
            TerrainNoiseConfig config = TerrainNoiseConfig.Default(NoiseType.Height);
            config.coordScale = 2f;
            config.frequency = 0.05f;
            config.octaves = 5;
            Vector2 point = new(-19.375f, 11.625f);
            float baseline = TerrainNoiseKernel.Sample(
                config,
                point,
                PlanetData.DefaultNoiseScale,
                42017,
                bounds.ToDomain());

            Vector2[] images =
            {
                point + new Vector2(bounds.Span.x, 0f),
                point + new Vector2(0f, -bounds.Span.y),
                point + new Vector2(bounds.Span.x, bounds.Span.y),
                point + new Vector2(-3f * bounds.Span.x, 4f * bounds.Span.y)
            };
            foreach (Vector2 image in images)
            {
                float repeated = TerrainNoiseKernel.Sample(
                    config,
                    image,
                    PlanetData.DefaultNoiseScale,
                    42017,
                    bounds.ToDomain());
                Assert.That(repeated, Is.EqualTo(baseline).Within(0.00001f), image.ToString());
            }

            float below = TerrainNoiseKernel.Sample(
                config,
                new Vector2(bounds.Min.x - 0.01f, point.y),
                PlanetData.DefaultNoiseScale,
                42017,
                bounds.ToDomain());
            float above = TerrainNoiseKernel.Sample(
                config,
                new Vector2(bounds.MaxExclusive.x - 0.01f, point.y),
                PlanetData.DefaultNoiseScale,
                42017,
                bounds.ToDomain());
            Assert.That(below, Is.EqualTo(above).Within(0.00001f));
        }

        [Test]
        [Category("Map.Smoke")]
        public void InfiniteTerrainNoiseKeepsLegacyCNoiseSamples()
        {
            TerrainNoiseConfig config = TerrainNoiseConfig.Default(NoiseType.Temperature);
            Vector2 world = new(-125.25f, 74.5f);
            float scale = 0.0175f;
            float legacy = TerrainNoiseKernel.Sample(config, world * scale, 9127);
            float unbounded = TerrainNoiseKernel.Sample(config, world, scale, 9127, default);
            Assert.That(unbounded, Is.EqualTo(legacy).Within(0.000001f));
        }

        [Test]
        [Category("Map.Smoke")]
        public void WrappedWindFieldRepeatsAcrossWorldSpan()
        {
            WorldTopologyBounds bounds = CreateBounds(radius: 255, chunkX: 16, chunkY: 16);
            WindFieldConfig config = WindFieldConfig.Default;
            RegionalRandomWindFieldProvider provider = RegionalRandomWindFieldProvider.Instance;
            Vector2Int point = new(-143, 91);
            Vector2 first = provider.Sample(point, 71237, config, bounds).Direction;
            Vector2 repeated = provider.Sample(point + bounds.Span, 71237, config, bounds).Direction;
            Assert.That(repeated.x, Is.EqualTo(first.x).Within(0.00001f));
            Assert.That(repeated.y, Is.EqualTo(first.y).Within(0.00001f));
        }
    }
}
