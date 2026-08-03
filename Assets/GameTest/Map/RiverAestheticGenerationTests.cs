using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FlatWorld.GameTest.Map
{
    public sealed class RiverAestheticGenerationTests
    {
        private const string MapPrefabPath = "Assets/2_Prefabs/Map/MapCore.prefab";
        private const string DesertBiomePath =
            "Assets/4_ScriptObjects/4-8_Biome/BiomeData/热带_沙漠.asset";

        [Test]
        [Category("Map.Hydrology")]
        public void DefaultHydrologyFavorsSparseReadableWaterways()
        {
            ChunkGenerator_River river = LoadRiverGenerator();

            Assert.That(river.sourceSpacing, Is.GreaterThanOrEqualTo(64));
            Assert.That(river.minRiverTraceCells, Is.GreaterThanOrEqualTo(20));
            Assert.That(river.maxRiverTraceSteps, Is.LessThanOrEqualTo(256));
            Assert.That(river.minLakeCells, Is.GreaterThanOrEqualTo(12));
            Assert.That(river.lakeChance, Is.InRange(0.1f, 0.6f));
        }

        [Test]
        [Category("Map.Hydrology")]
        public void DesertStronglySuppressesRiverAndLakeOrigins()
        {
            ChunkGenerator_River river = LoadRiverGenerator();
            BiomeData desert = AssetDatabase.LoadAssetAtPath<BiomeData>(DesertBiomePath);
            Assert.That(desert, Is.Not.Null);

            ChunkGenerator_River.BiomeHydrologyRule rule =
                river.biomeHydrologyRules.SingleOrDefault(candidate => candidate.biome == desert);
            Assert.That(rule, Is.Not.Null, "沙漠必须显式配置河湖权重，不能依赖名称硬编码。");
            Assert.That(rule.riverSourceWeight, Is.LessThanOrEqualTo(0.1f));
            Assert.That(rule.lakeWeight, Is.LessThanOrEqualTo(0.2f));
        }

        [Test]
        [Category("Map.Hydrology")]
        public void DryDesertCandidateIsFarLessLikelyThanWetCandidate()
        {
            ChunkGenerator_River river = LoadRiverGenerator();
            MethodInfo method = typeof(ChunkGenerator_River).GetMethod(
                "CalculateHeadwaterChance",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);

            float wetChance = (float)method.Invoke(river, new object[] { 0.8f, 1f });
            float dryDesertChance = (float)method.Invoke(river, new object[] { 0.35f, 0.06f });

            Assert.That(wetChance, Is.GreaterThan(0f));
            Assert.That(dryDesertChance, Is.LessThan(wetChance * 0.02f));
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
