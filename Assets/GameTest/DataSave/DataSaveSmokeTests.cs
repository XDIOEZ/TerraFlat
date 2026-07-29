using FlatWorld.GameTest.Shared;
using NUnit.Framework;

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
    }
}
