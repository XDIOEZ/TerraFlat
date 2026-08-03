using System.Reflection;
using FlatWorld.GameTest.Shared;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FlatWorld.GameTest.DataSave
{
    /// <summary>数据存档基础冒烟测试：保护存档入口与权威数据链脚本。</summary>
    public sealed class DataSaveSmokeTests
    {
        [Test]
        [Category("DataSave.Smoke")]
        public void RequiredEntryPointsExist()
        {
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Manager/SaveDataMgr.cs", "SaveDataMgr");
            GameTestAssertions.AssertAssetExists("Assets/5_Scripts/5-3_GamePlay/Map/Data/GameSaveData.cs");
            GameTestAssertions.AssertAssetExists("Assets/5_Scripts/5-1_Data/ItemData/ItemData.cs");
            GameTestAssertions.AssertAssetExists("Assets/5_Scripts/5-1_Data/ModData/ModuleData.cs");
        }

        [Test]
        [Category("DataSave.Player")]
        public void InitialPlayerPlacementUsesProfileCreationStateInsteadOfSavedPosition()
        {
            MethodInfo requiresInitialPlacement = typeof(GameManager).GetMethod(
                "RequiresInitialPlayerPlacement",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(requiresInitialPlacement, Is.Not.Null);

            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/Player/Player.prefab");
            Assert.That(playerPrefab, Is.Not.Null);

            GameObject playerObject = Object.Instantiate(playerPrefab);
            playerObject.name = "PlayerPositionPersistenceTest";
            Player player = playerObject.GetComponent<Player>();
            Assert.That(player, Is.Not.Null);
            Assert.That(player.Data, Is.Not.Null);

            try
            {
                player.Data.transform.position = Vector3.zero;
                player.SetProfileContext(localProfile: true, profileDataWasCreated: false);
                Assert.That(
                    requiresInitialPlacement.Invoke(null, new object[] { player }),
                    Is.False,
                    "旧玩家保存在世界原点时也必须原样恢复。");

                player.Data.transform.position = new Vector3(17f, -9f, 0f);
                player.SetProfileContext(localProfile: true, profileDataWasCreated: true);
                Assert.That(
                    requiresInitialPlacement.Invoke(null, new object[] { player }),
                    Is.True,
                    "新玩家判定必须来自档案创建状态，而不是坐标值。");
            }
            finally
            {
                Object.DestroyImmediate(playerObject);
            }
        }

        [Test]
        [Category("DataSave.Smoke")]
        public void SpawnerProgressContainsPersistentCycleAndBudgetState()
        {
            var state = new SpawnerProgressSaveData
            {
                LastProcessedTotalTime = 4321f,
                AvailableBudget = 7,
                LastBudgetRecoveryDay = 3,
                PendingReplacementCount = 2
            };

            Assert.That(state.LastProcessedTotalTime, Is.EqualTo(4321f));
            Assert.That(state.DataVersion, Is.EqualTo(1));
            Assert.That(state.AvailableBudget, Is.EqualTo(7));
            Assert.That(state.LastBudgetRecoveryDay, Is.EqualTo(3));
            Assert.That(state.PendingReplacementCount, Is.EqualTo(2));
        }

        [Test]
        [Category("DataSave.Smoke")]
        public void CustomDifficultyStateLivesOnWorldSave()
        {
            var rules = new GameDifficultyRuleValues
            {
                DropAllCarriedItems = true,
                PlayerAttackMultiplier = 1.5f,
                HungerDrainMultiplier = 1.25f,
                SpawnFrequencyMultiplier = 1.75f,
                CraftingOutputMultiplier = 2f
            };
            var saveData = new GameSaveData { Difficulty = GameDifficultyId.Custom };
            GameDifficultyCatalog.WriteCustomRules(saveData, rules);
            GameDifficultyRuleValues restored = GameDifficultyCatalog.ReadCustomRules(saveData);

            Assert.That(saveData.Difficulty, Is.EqualTo(GameDifficultyId.Custom));
            Assert.That(saveData.CustomDifficultyDataVersion, Is.EqualTo(1));
            Assert.That(saveData.CustomDifficultyDropAllCarriedItems, Is.True);
            Assert.That(restored.PlayerAttackMultiplier, Is.EqualTo(1.5f));
            Assert.That(restored.HungerDrainMultiplier, Is.EqualTo(1.25f));
            Assert.That(restored.SpawnFrequencyMultiplier, Is.EqualTo(1.75f));
            Assert.That(restored.CraftingOutputMultiplier, Is.EqualTo(2f));
            Assert.That(GameDifficultyCatalog.CreateCustom(restored).PlayerDeath.DropAllCarriedItems, Is.True);
        }

        [Test]
        [Category("DataSave.Smoke")]
        public void LegacyCustomDifficultyDefaultsNewMultipliersToOne()
        {
            var legacySave = new GameSaveData
            {
                Difficulty = GameDifficultyId.Custom,
                CustomDifficultyDataVersion = 0,
                CustomDifficultyDropAllCarriedItems = true,
                CustomPlayerAttackMultiplier = 0f,
                CustomCropGrowthMultiplier = 0f
            };

            GameDifficultyRuleValues restored = GameDifficultyCatalog.ReadCustomRules(legacySave);

            Assert.That(restored.DropAllCarriedItems, Is.True);
            Assert.That(restored.PlayerAttackMultiplier, Is.EqualTo(1f));
            Assert.That(restored.CropGrowthMultiplier, Is.EqualTo(1f));
        }

        [Test]
        [Category("DataSave.Smoke")]
        public void CultivatedCropStateRoundTripsThroughModuleData()
        {
            var source = new GrowData
            {
                growState = Mod_Grow.GrowState.成熟,
                GrowProgress = 100f,
                MaxGrowProgress = 100f,
                isCultivatedCrop = true,
                plantedTilePos = new Vector2Int(8, 13),
                isMature = true,
                isHarvested = true,
                growthStatus = Mod_Grow.GrowthStatus.Harvested,
                environmentInitialized = true,
                environmentGrowthMultiplier = 1f
            };
            var moduleData = new Ex_ModData_MemoryPackable();
            moduleData.WriteData(source);

            GrowData restored = new GrowData();
            moduleData.ReadData(ref restored);

            Assert.That(restored.GrowProgress, Is.EqualTo(100f));
            Assert.That(restored.growState, Is.EqualTo(Mod_Grow.GrowState.成熟));
            Assert.That(restored.plantedTilePos, Is.EqualTo(new Vector2Int(8, 13)));
            Assert.That(restored.isCultivatedCrop, Is.True);
            Assert.That(restored.isMature, Is.True);
            Assert.That(restored.isHarvested, Is.True);
            Assert.That(restored.growthStatus, Is.EqualTo(Mod_Grow.GrowthStatus.Harvested));
        }

        [Test]
        [Category("DataSave.Weather")]
        public void WeatherEventStateRoundTripsThroughMemoryPackContainer()
        {
            var source = new PlanetData
            {
                Name = "测试星球",
                CurrentWeather = WeatherType.Rain,
                WeatherIntensity = 0.65f,
                WeatherDataVersion = WeatherEventScheduler.CurrentDataVersion,
                WeatherPhase = WeatherPhase.RainSteady,
                WeatherPhaseStartedTotalTime = 120f,
                WeatherPhaseEndTotalTime = 360f,
                NextWeatherEventTotalTime = 0f,
                WeatherRandomCursor = 7,
                WeatherEventSequence = 3
            };
            var moduleData = new Ex_ModData_MemoryPackable();
            moduleData.WriteData(source);

            PlanetData restored = new PlanetData();
            moduleData.ReadData(ref restored);

            Assert.That(restored.CurrentWeather, Is.EqualTo(WeatherType.Rain));
            Assert.That(restored.WeatherIntensity, Is.EqualTo(0.65f));
            Assert.That(restored.WeatherPhase, Is.EqualTo(WeatherPhase.RainSteady));
            Assert.That(restored.WeatherPhaseStartedTotalTime, Is.EqualTo(120f));
            Assert.That(restored.WeatherPhaseEndTotalTime, Is.EqualTo(360f));
            Assert.That(restored.WeatherRandomCursor, Is.EqualTo(7));
            Assert.That(restored.WeatherEventSequence, Is.EqualTo(3));
        }
    }
}
