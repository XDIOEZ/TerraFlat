using FlatWorld.GameTest.Shared;
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FlatWorld.GameTest.AI
{
    /// <summary>AI 基础冒烟测试：保护状态机、感知和生物资源入口。</summary>
    public sealed class AISmokeTests
    {
        [Test]
        [Category("AI.Smoke")]
        public void RequiredEntryPointsAndAssetsExist()
        {
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/AI/AI_StateMachineRunner.cs", "AI_StateMachineRunner");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/AI/Mod_ItemDetector.cs", "Mod_ItemDetector");
            GameTestAssertions.AssertAssetExists("Assets/Resources/Config/SpawnerConfig.asset");
            GameTestAssertions.AssertFolderContainsAsset("Assets/2_Prefabs/Entity_AI", "t:Prefab");
        }

        [Test]
        [Category("AI.Smoke")]
        public void SpawnWeightsAreNormalized()
        {
            SpawnerConfig config = ScriptableObject.CreateInstance<SpawnerConfig>();
            try
            {
                config.SpawnEntries = new List<SpawnerConfig.SpawnEntry>
                {
                    new() { PrefabName = "Chicken", Probability = 0.5f },
                    new() { PrefabName = "WildBoar", Probability = 0.2f },
                    new() { PrefabName = "Wolf", Probability = 0.5f }
                };

                var counts = new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["Chicken"] = 0,
                    ["WildBoar"] = 0,
                    ["Wolf"] = 0
                };
                var random = new System.Random(20260729);
                const int sampleCount = 12000;
                for (int i = 0; i < sampleCount; i++)
                    counts[config.DetermineSpawnType((float)random.NextDouble())]++;

                Assert.That(counts["Chicken"] / (float)sampleCount, Is.EqualTo(5f / 12f).Within(0.025f));
                Assert.That(counts["WildBoar"] / (float)sampleCount, Is.EqualTo(2f / 12f).Within(0.025f));
                Assert.That(counts["Wolf"] / (float)sampleCount, Is.EqualTo(5f / 12f).Within(0.025f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        [Test]
        [Category("AI.Smoke")]
        public void EcologyGroupsHaveUniqueSpeciesAndPersistentIds()
        {
            SpawnerConfig wildlife = Resources.Load<SpawnerConfig>("Config/SpawnerConfig");
            SpawnerConfig wolves = Resources.Load<SpawnerConfig>("Config/SpawnerConfig_Wolves");
            SpawnerConfig ghosts = Resources.Load<SpawnerConfig>("Config/SpawnerConfig_Ghost");

            Assert.That(wildlife, Is.Not.Null);
            Assert.That(wolves, Is.Not.Null);
            Assert.That(ghosts, Is.Not.Null);
            Assert.That(wildlife.EcologyGroup, Is.EqualTo(SpawnerEcologyGroup.Animals));
            Assert.That(wolves.EcologyGroup, Is.EqualTo(SpawnerEcologyGroup.CommonEnemies));
            Assert.That(ghosts.EcologyGroup, Is.EqualTo(SpawnerEcologyGroup.NightEnemies));

            var persistentIds = new HashSet<string>(StringComparer.Ordinal);
            var speciesIds = new HashSet<string>(StringComparer.Ordinal);
            SpawnerConfig[] configs = { wildlife, wolves, ghosts };
            foreach (SpawnerConfig config in configs)
            {
                Assert.That(config.PersistentId, Is.Not.Empty);
                Assert.That(persistentIds.Add(config.PersistentId), Is.True, $"重复 PersistentId: {config.PersistentId}");
                Assert.That(config.GroupAliveLimit, Is.GreaterThan(0));
                Assert.That(config.MaxEcologyBudget, Is.GreaterThan(0));

                foreach (SpawnerConfig.SpawnEntry entry in config.SpawnEntries)
                {
                    Assert.That(speciesIds.Add(entry.PrefabName), Is.True, $"物种重复配置: {entry.PrefabName}");
                    Assert.That(entry.EcologyCost, Is.GreaterThan(0));
                    Assert.That(entry.SpeciesAliveLimit, Is.GreaterThan(0));
                }
            }

            CollectionAssert.AreEquivalent(new[] { "Chicken", "WildBoar", "Wolf", "Ghost" }, speciesIds);
        }

        [Test]
        [Category("AI.Smoke")]
        public void GhostPerceptionCoversItsMaximumSpawnDistance()
        {
            SpawnerConfig ghosts = Resources.Load<SpawnerConfig>("Config/SpawnerConfig_Ghost");
            GameObject ghostPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/Entity_AI/Ghost.prefab");

            Assert.That(ghosts, Is.Not.Null);
            Assert.That(ghostPrefab, Is.Not.Null);

            AI_Ghost ghostAI = ghostPrefab.GetComponentInChildren<AI_Ghost>(true);
            Assert.That(ghostAI, Is.Not.Null);

            SerializedProperty perceptionRadius = new SerializedObject(ghostAI)
                .FindProperty("perceptionRadius");
            Assert.That(perceptionRadius, Is.Not.Null);
            Assert.That(
                perceptionRadius.floatValue,
                Is.GreaterThanOrEqualTo(ghosts.MaxSpawnDistance),
                "幽灵感知距离未覆盖最大生成距离，出生后可能永远无法主动追击玩家。");
        }
    }
}
