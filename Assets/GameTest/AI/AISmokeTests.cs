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
        private enum AdvanceTestState
        {
            Advance
        }

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

        [Test]
        [Category("AI.Smoke")]
        public void ChickenGrassForagingDefaultsToOneMealEveryTwoDays()
        {
            GameObject chickenPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/Entity_AI/Chicken.prefab");
            Assert.That(chickenPrefab, Is.Not.Null);

            AI_Chicken chicken = chickenPrefab.GetComponentInChildren<AI_Chicken>(true);
            Assert.That(chicken, Is.Not.Null);
            Assert.That(chicken.enableGrassForaging, Is.True);
            Assert.That(chicken.grassSearchRadius, Is.GreaterThan(chicken.eatDistance));
            Assert.That(chicken.grassEatDuration, Is.GreaterThan(0f));
            Assert.That(chicken.grassSustenanceDays, Is.EqualTo(2f).Within(0.001f));
        }

        [Test]
        [Category("AI.Smoke")]
        public void ChickenPrefabCreatesItemDataWithStableId()
        {
            GameObject chickenPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/Entity_AI/Chicken.prefab");
            Item chickenItem = chickenPrefab != null ? chickenPrefab.GetComponent<Item>() : null;

            Assert.That(chickenPrefab, Is.Not.Null);
            Assert.That(chickenItem, Is.Not.Null);

            ItemData data = chickenItem.Get_NewItemData();
            Assert.That(data, Is.Not.Null);
            Assert.That(data.IDName, Is.EqualTo("Chicken"));
            Assert.That(data.ModuleDataDic, Is.Not.Null);
            Assert.That(data.ModuleDataDic, Is.Not.Empty);
            Assert.That(data.ModuleDataDic.Keys, Has.None.Null);
            foreach (KeyValuePair<string, ModuleData> pair in data.ModuleDataDic)
            {
                Assert.That(pair.Key, Is.Not.Empty);
                Assert.That(pair.Value, Is.Not.Null, pair.Key);
                Assert.That(pair.Value.Name, Is.EqualTo(pair.Key));
                Assert.That(pair.Value.ID, Is.Not.Null.And.Not.Empty, pair.Key);
            }
        }

        [Test]
        [Category("AI.StateMachine")]
        public void AdvanceNodeMovesUntilArrivalAndNotifiesOnce()
        {
            Vector3 actorPosition = Vector3.zero;
            Vector3 targetPosition = new(3f, 0f, 0f);
            int moveCount = 0;
            int stopCount = 0;
            int arrivalCount = 0;
            AIAdvanceStateNode<AdvanceTestState> node = new(
                AdvanceTestState.Advance,
                () => new AIAdvanceTarget(true, targetPosition),
                () => actorPosition,
                () => 0.1f,
                target =>
                {
                    moveCount++;
                    actorPosition = Vector3.MoveTowards(actorPosition, target, 1f);
                },
                () => stopCount++,
                () => arrivalCount++);

            node.Enter();
            for (int i = 0; i < 5; i++)
                node.Tick(1f);

            Assert.That(node.AnimationRole, Is.EqualTo(AIStateAnimationRole.Moving));
            Assert.That(actorPosition, Is.EqualTo(targetPosition));
            Assert.That(moveCount, Is.EqualTo(3));
            Assert.That(stopCount, Is.EqualTo(2));
            Assert.That(arrivalCount, Is.EqualTo(1));
        }

        [Test]
        [Category("AI.Smoke")]
        public void WolfPrefabAcceptsReusableAdvanceCommands()
        {
            GameObject wolfPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/Entity_AI/Wolf.prefab");
            AI_Wolf wolf = wolfPrefab != null
                ? wolfPrefab.GetComponentInChildren<AI_Wolf>(true)
                : null;

            Assert.That(wolfPrefab, Is.Not.Null);
            Assert.That(wolf, Is.Not.Null);
            Assert.That(wolf, Is.InstanceOf<IAIAdvanceCommandReceiver>());
            Assert.That((int)WolfState.Advance, Is.EqualTo(7), "推进状态必须追加在末尾，避免破坏旧存档枚举值。");
        }

        [Test]
        [Category("AI.Smoke")]
        public void GrassSustenanceCanPauseAndRestoreNutrition()
        {
            GameObject foodObject = new GameObject("ChickenGrassNutritionTest");
            try
            {
                Mod_Food food = foodObject.AddComponent<Mod_Food>();
                food.Data.nutrition = new Nutrition(10f, 10f, 10f, 10f, 10f);
                food.Data.nutritionConsumeSpeed = new GameValue_float(1f);

                food.RuntimeNutritionConsumeMultiplier = 0f;
                Assert.That(food.ConsumeNutrition(5f), Is.Zero);
                Assert.That(food.Data.nutrition.Carbohydrates, Is.EqualTo(10f));

                food.RuntimeNutritionConsumeMultiplier = 1f;
                Assert.That(food.ConsumeNutrition(5f), Is.EqualTo(5f));
                Assert.That(food.Data.nutrition.Carbohydrates, Is.EqualTo(5f));

                food.RestoreNutritionToMaximum();
                Assert.That(food.Data.nutrition.Carbohydrates, Is.EqualTo(10f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(foodObject);
            }
        }
    }
}
