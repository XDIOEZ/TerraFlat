using System.Reflection;
using System.Linq;
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
        [Category("DataSave.Smoke")]
        public void EnvironmentLayersRoundTripKeepsFiveSupportedGrids()
        {
            var source = new EnvironmentLayers();
            source.EnsureSize(2, 2);
            source.SetCell(1, 0, 0.2f, 7f, 0.8f, 0.6f);
            source.SetLight(1, 0, 0.4f);

            var moduleData = new Ex_ModData_MemoryPackable();
            moduleData.WriteData(source);
            var restored = new EnvironmentLayers();
            moduleData.ReadData(ref restored);

            Assert.That(restored, Is.Not.Null);
            Assert.That(restored.IsValidSize(2, 2), Is.True);
            Assert.That(restored.Temperature[1, 0], Is.EqualTo(0.2f));
            Assert.That(restored.TemperatureCelsius[1, 0], Is.EqualTo(7f));
            Assert.That(restored.Precipitation[1, 0], Is.EqualTo(0.8f));
            Assert.That(restored.Height[1, 0], Is.EqualTo(0.6f));
            Assert.That(restored.Light[1, 0], Is.EqualTo(0.4f));

            FieldInfo[] gridFields = typeof(EnvironmentLayers)
                .GetFields(BindingFlags.Instance | BindingFlags.Public)
                .Where(field => field.FieldType == typeof(float[,]))
                .ToArray();
            Assert.That(
                gridFields.Select(field => field.Name),
                Is.EquivalentTo(new[] { "Temperature", "TemperatureCelsius", "Precipitation", "Height", "Light" }));
        }

        [Test]
        [Category("DataSave.Smoke")]
        public void TileStackMapRoundTripKeepsEmptyThroughOverflowCellsAndEnvironment()
        {
            var source = new Data_TileMap
            {
                IDName = "GameTest_TileMap",
                position = new Vector2Int(-8, 12),
                TileLoaded = true
            };
            source.EnsureTileStorage(2, 2);
            source.EnsureEnvironmentStorage(2, 2);
            source.SetEnvironmentAtLocal(1, 1, 0.25f, 12.5f, 0.75f, 0.6f);
            source.SetLightAtLocal(1, 1, 0.4f);
            Vector2Int oneLayer = source.position + new Vector2Int(1, 0);
            Vector2Int twoLayers = source.position + new Vector2Int(0, 1);
            Vector2Int fourLayers = source.position + new Vector2Int(1, 1);
            source.SetBaseTile(oneLayer, NewTile("one"));
            source.SetBaseTile(twoLayers, NewTile("two_base"));
            source.PushTile(twoLayers, NewTile("two_overlay"));
            source.SetBaseTile(fourLayers, NewTile("four_0"));
            source.PushTile(fourLayers, NewTile("four_1"));
            source.PushTile(fourLayers, NewTile("four_2"));
            source.PushTile(fourLayers, NewTile("four_3"));
            source.TrySetGrassStateAtWorld(fourLayers, GrassCellState.Present);

            var container = new Ex_ModData_MemoryPackable();
            container.WriteData<ItemData>(source);
            ItemData restoredBase = container.GetData<ItemData>();

            Assert.That(restoredBase, Is.TypeOf<Data_TileMap>());
            Data_TileMap restored = (Data_TileMap)restoredBase;
            Assert.That(restored.position, Is.EqualTo(source.position));
            Assert.That(restored.TileLoaded, Is.True);
            Assert.That(restored.Width, Is.EqualTo(2));
            Assert.That(restored.Height, Is.EqualTo(2));
            Assert.That(restored.GetLayerCount(source.position), Is.Zero);
            Assert.That(restored.GetLayerCount(oneLayer), Is.EqualTo(1));
            Assert.That(restored.GetLayerCount(twoLayers), Is.EqualTo(2));
            Assert.That(restored.GetLayerCount(fourLayers), Is.EqualTo(4));
            Assert.That(restored.GetTileAt(fourLayers, 0).ID, Is.EqualTo("four_0"));
            Assert.That(restored.GetTileAt(fourLayers, 3).ID, Is.EqualTo("four_3"));
            Assert.That(restored.CountNonEmptyCells(), Is.EqualTo(3));
            Assert.That(restored.CountOverflowAllocations(), Is.EqualTo(1));
            Assert.That(restored.EnvironmentLayers.IsValidSize(2, 2), Is.True);
            Assert.That(restored.EnvironmentLayers.Temperature[1, 1], Is.EqualTo(0.25f));
            Assert.That(restored.EnvironmentLayers.TemperatureCelsius[1, 1], Is.EqualTo(12.5f));
            Assert.That(restored.EnvironmentLayers.Precipitation[1, 1], Is.EqualTo(0.75f));
            Assert.That(restored.EnvironmentLayers.Height[1, 1], Is.EqualTo(0.6f));
            Assert.That(restored.EnvironmentLayers.Light[1, 1], Is.EqualTo(0.4f));
            Assert.That(restored.TryGetGrassStateAtWorld(fourLayers, out GrassCellState grass), Is.True);
            Assert.That(grass, Is.EqualTo(GrassCellState.Present));
        }

        [Test]
        [Category("DataSave.Smoke")]
        public void SaveFormatVersionTwoRejectsLegacyAndHeaderlessPayloads()
        {
            Assert.That(ReadPrivateVersion("CompactSaveVersion"), Is.EqualTo(2));
            Assert.That(ReadPrivateVersion("ModdedSaveVersion"), Is.EqualTo(2));

            SaveDataMgr existing = Object.FindObjectOfType<SaveDataMgr>();
            GameObject owner = null;
            SaveDataMgr manager = existing;
            if (manager == null)
            {
                owner = new GameObject("SaveVersionTest");
                manager = owner.AddComponent<SaveDataMgr>();
            }

            try
            {
                MethodInfo deserializeCore = typeof(SaveDataMgr).GetMethod(
                    "DeserializeCoreSavePayload",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo deserializeSave = typeof(SaveDataMgr).GetMethod(
                    "DeserializeSavePayload",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(deserializeCore, Is.Not.Null);
                Assert.That(deserializeSave, Is.Not.Null);

                var compactContainer = new Ex_ModData_MemoryPackable();
                compactContainer.WriteData(new CompactSaveEnvelope
                {
                    Version = 1,
                    CoreSaveData = new byte[] { 1 }
                });
                AssertIncompatible(
                    deserializeCore,
                    manager,
                    Prefix(new byte[] { (byte)'F', (byte)'W', (byte)'D', (byte)'2' }, compactContainer.BitData));

                var moddedContainer = new Ex_ModData_MemoryPackable();
                moddedContainer.WriteData(new ModdedSaveEnvelope
                {
                    Version = 1,
                    CoreSavePayload = new byte[] { 1 }
                });
                AssertIncompatible(
                    deserializeSave,
                    manager,
                    Prefix(new byte[] { (byte)'F', (byte)'W', (byte)'D', (byte)'3' }, moddedContainer.BitData));

                AssertIncompatible(deserializeCore, manager, new byte[] { 1, 2, 3, 4 });
            }
            finally
            {
                if (owner != null)
                    Object.DestroyImmediate(owner);
            }
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

        private static TileData NewTile(string id)
        {
            return new TileData_Universal
            {
                ID = id,
                Name = id,
                IsWalkable = true
            };
        }

        private static int ReadPrivateVersion(string fieldName)
        {
            FieldInfo field = typeof(SaveDataMgr).GetField(
                fieldName,
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (int)field.GetRawConstantValue();
        }

        private static byte[] Prefix(byte[] prefix, byte[] body)
        {
            var payload = new byte[prefix.Length + body.Length];
            System.Buffer.BlockCopy(prefix, 0, payload, 0, prefix.Length);
            System.Buffer.BlockCopy(body, 0, payload, prefix.Length, body.Length);
            return payload;
        }

        private static void AssertIncompatible(MethodInfo method, SaveDataMgr manager, byte[] payload)
        {
            TargetInvocationException exception = Assert.Throws<TargetInvocationException>(
                () => method.Invoke(manager, new object[] { payload }));
            Assert.That(exception.InnerException, Is.TypeOf<SaveVersionIncompatibleException>());
            Assert.That(exception.InnerException.Message, Does.Contain("迁移"));
            Assert.That(exception.InnerException.Message, Does.Contain("覆盖"));
            Assert.That(exception.InnerException.Message, Does.Contain("删除"));
        }
    }
}
