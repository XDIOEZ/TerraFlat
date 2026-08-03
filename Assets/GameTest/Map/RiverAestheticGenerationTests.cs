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
            Assert.That(river.spawnRiverStones, Is.False);
        }

        [Test]
        [Category("Map.Hydrology")]
        public void RiverQueryIsDeterministicAndUsesWorldCoordinates()
        {
            ChunkGenerator_River river = LoadRiverGenerator();
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

        [Test]
        [Category("Map.Hydrology")]
        public void LegacyHydrologyBuffersAreNotPartOfRuntimeGenerator()
        {
            Assert.That(typeof(ChunkGenerator_River).GetField("hydrologyHalo"), Is.Null);
            Assert.That(typeof(ChunkGenerator_River).GetField("hydrologyCellsPerFrame"), Is.Null);
            Assert.That(typeof(ChunkGenerator_River).GetField("biomeHydrologyRules"), Is.Null);
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
    }
}
