using System;
using System.Collections.Generic;
using System.IO;
using FlatWorld.Dialogue;
using FlatWorld.Gameplay.Progress;
using FlatWorld.Guide;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FlatWorld.GameTest.Guide
{
    /// <summary>
    /// 新手引导冒烟测试：覆盖资格、存档、乱序里程碑、Facts、JSON、Prefab 与成功事务边界。
    /// </summary>
    public sealed class NewPlayerGuideSmokeTests
    {
        #region 资产与接线

        [Test]
        [Category("Guide.Smoke")]
        public void RequiredScriptsAssetsAndPrefabWiringExist()
        {
            AssertScriptType(
                "Assets/5_Scripts/5-3_GamePlay/Guide/NewPlayerGuideController.cs",
                typeof(NewPlayerGuideController));
            AssertScriptType(
                "Assets/5_Scripts/5-3_GamePlay/Progress/GameplayProgressEvents.cs",
                typeof(GameplayProgressEvents));

            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/Player/Player.prefab");
            Assert.That(playerPrefab, Is.Not.Null);
            Assert.That(playerPrefab.GetComponents<NewPlayerGuideController>(), Has.Length.EqualTo(1));
            Assert.That(playerPrefab.GetComponents<CharacterSoliloquyController>(), Has.Length.EqualTo(1));
            Assert.That(playerPrefab.GetComponents<ConfiguredSpeechProvider>(), Has.Length.EqualTo(1));

            TextAsset guideJson = AssetDatabase.LoadAssetAtPath<TextAsset>(
                "Assets/Resources/Dialogue/Soliloquy/guide_survival.json");
            Assert.That(guideJson, Is.Not.Null);
            Assert.That(guideJson.text, Does.Contain("guide.survival.craft-spark-maker"));
            Assert.That(guideJson.text, Does.Contain("guide.survival.ignite-bonfire"));

            string buildingRecipeJson = ReadProjectFile(
                "Assets/StreamingAssets/GameConfig/Recipes/crafting/buildings.json");
            Assert.That(buildingRecipeJson, Does.Contain(NewPlayerGuideIds.SparkMakerSummoner));
            Assert.That(buildingRecipeJson, Does.Contain(NewPlayerGuideIds.BonfireSummoner));
        }

        #endregion

        #region 玩家资格

        [Test]
        [Category("Guide.Smoke")]
        public void PlayerProfileContext_OnlyLocalNewProfileIsNew()
        {
            Player player = CreatePlayerInstance("GuideProfileActor");
            try
            {
                int changeCount = 0;
                player.ProfileContextChanged += () => changeCount++;

                player.SetProfileContext(localProfile: false, profileDataWasCreated: true);
                Assert.That(player.IsLocalProfile, Is.False);
                Assert.That(player.IsNewProfile, Is.False, "远程副本不得取得教程资格。");

                player.SetProfileContext(localProfile: true, profileDataWasCreated: false);
                Assert.That(player.IsLocalProfile, Is.True);
                Assert.That(player.IsNewProfile, Is.False, "旧档案不得被位置或本地控制误判为新玩家。");

                player.SetProfileContext(localProfile: true, profileDataWasCreated: true);
                Assert.That(player.IsNewProfile, Is.True);
                Assert.That(changeCount, Is.EqualTo(3));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(player.gameObject);
            }
        }

        [Test]
        [Category("Guide.Smoke")]
        public void ControllerFacts_RespectNewOldAndRemoteEligibility()
        {
            AssertFacts(localProfile: true, wasCreated: true, expectedEnabled: "True");
            AssertFacts(localProfile: true, wasCreated: false, expectedEnabled: "False");
            AssertFacts(localProfile: false, wasCreated: true, expectedEnabled: "False");
        }

        [Test]
        [Category("Guide.Smoke")]
        public void SoliloquyController_RemotePlayerIsExplicitlyIneligible()
        {
            Player player = CreatePlayerInstance("RemoteSpeechActor");
            try
            {
                CharacterSoliloquyController controller =
                    player.GetComponent<CharacterSoliloquyController>();
                Assert.That(controller, Is.Not.Null);
                player.SetProfileContext(localProfile: false, profileDataWasCreated: true);

                bool canRunRemote = (bool)typeof(CharacterSoliloquyController)
                    .GetMethod("CanRunForActor", System.Reflection.BindingFlags.Instance |
                                                System.Reflection.BindingFlags.NonPublic)
                    .Invoke(controller, null);
                Assert.That(canRunRemote, Is.False);

                player.SetProfileContext(localProfile: true, profileDataWasCreated: true);
                bool canRunLocal = (bool)typeof(CharacterSoliloquyController)
                    .GetMethod("CanRunForActor", System.Reflection.BindingFlags.Instance |
                                                System.Reflection.BindingFlags.NonPublic)
                    .Invoke(controller, null);
                Assert.That(canRunLocal, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(player.gameObject);
            }
        }

        #endregion

        #region 命名空间安全存档

        [Test]
        [Category("Guide.Smoke")]
        public void TutorialNamespace_PreservesUnknownAndDialogueData()
        {
            Data_Player playerData = new()
            {
                ItemSpecialData = "{\"external\":{\"value\":7},\"flatworld.dialogue\":{\"completed\":[\"existing\"]}}"
            };

            NewPlayerGuideProgressStore progress = new(playerData, establishEligibility: true);
            progress.MarkMilestone(NewPlayerGuideIds.InventoryOpened);

            Assert.That(playerData.ItemSpecialData, Does.Contain("\"external\":{\"value\":7}"));
            Assert.That(
                playerData.ItemSpecialData,
                Does.Contain("\"flatworld.dialogue\":{\"completed\":[\"existing\"]}"));
            Assert.That(playerData.ItemSpecialData, Does.Contain("\"flatworld.tutorial\":"));
            Assert.That(playerData.ItemSpecialData, Does.Contain("\"eligible\":true"));
        }

        [Test]
        [Category("Guide.Smoke")]
        public void LegacyItemSpecialData_IsPreservedVerbatim()
        {
            const string legacy = "legacy-special-data:not-json";
            Data_Player playerData = new() { ItemSpecialData = legacy };

            NewPlayerGuideProgressStore progress = new(playerData, establishEligibility: true);
            progress.MarkMilestone(NewPlayerGuideIds.InventoryOpened);

            Assert.That(
                playerData.ItemSpecialData,
                Does.Contain($"\"{ItemSpecialDataJsonStore.LegacyProperty}\":\"{legacy}\""));
            Assert.That(playerData.ItemSpecialData, Does.Contain("\"flatworld.tutorial\":"));
        }

        [Test]
        [Category("Guide.Smoke")]
        public void Eligibility_PersistsAcrossReloadButOldSaveDefaultsDisabled()
        {
            Data_Player oldPlayerData = new();
            Assert.That(NewPlayerGuideProgressStore.HasEligibility(oldPlayerData), Is.False);
            Assert.That(new NewPlayerGuideProgressStore(oldPlayerData).IsEligible, Is.False);

            Data_Player newPlayerData = new();
            NewPlayerGuideProgressStore firstLoad =
                new(newPlayerData, establishEligibility: true);
            firstLoad.MarkMilestone(NewPlayerGuideIds.InventoryOpened);

            NewPlayerGuideProgressStore reloaded = new(newPlayerData);
            Assert.That(reloaded.IsEligible, Is.True);
            Assert.That(reloaded.HasMilestone(NewPlayerGuideIds.InventoryOpened), Is.True);
        }

        #endregion

        #region 里程碑归一化

        [Test]
        [Category("Guide.Smoke")]
        public void Milestones_AreIdempotentAndIgnoreUnknownIds()
        {
            NewPlayerGuideProgressStore progress =
                new(new Data_Player(), establishEligibility: true);

            Assert.That(progress.MarkMilestone(NewPlayerGuideIds.InventoryOpened), Is.True);
            Assert.That(progress.MarkMilestone(NewPlayerGuideIds.InventoryOpened), Is.False);
            Assert.That(progress.MarkMilestone("unknown-milestone"), Is.False);
            Assert.That(progress.Milestones.Count, Is.EqualTo(1));
        }

        [Test]
        [Category("Guide.Smoke")]
        public void OutOfOrderMilestones_AreRetainedAndSkippedAfterPrerequisites()
        {
            NewPlayerGuideProgressStore progress =
                new(new Data_Player(), establishEligibility: true);

            progress.MarkMilestone(NewPlayerGuideIds.SparkMakerCrafted);
            Assert.That(progress.CurrentStage, Is.EqualTo(NewPlayerGuideStage.OpenInventory));

            progress.MarkMilestone(NewPlayerGuideIds.InventoryOpened);
            Assert.That(progress.CurrentStage, Is.EqualTo(NewPlayerGuideStage.GatherSurvivalMaterials));

            progress.MarkMilestone(NewPlayerGuideIds.SurvivalMaterialsGathered);
            Assert.That(
                progress.CurrentStage,
                Is.EqualTo(NewPlayerGuideStage.PlaceSparkMaker),
                "先完成的制作里程碑应在前置条件补齐后被跳过。");
        }

        [Test]
        [Category("Guide.Smoke")]
        public void EveryOrderedMilestone_DerivesTheNextStageAndCompletionPersists()
        {
            NewPlayerGuideStage[] expectedStages =
            {
                NewPlayerGuideStage.OpenInventory,
                NewPlayerGuideStage.GatherSurvivalMaterials,
                NewPlayerGuideStage.CraftSparkMaker,
                NewPlayerGuideStage.PlaceSparkMaker,
                NewPlayerGuideStage.CreateFireSeed,
                NewPlayerGuideStage.CraftBonfire,
                NewPlayerGuideStage.PlaceBonfire,
                NewPlayerGuideStage.IgniteBonfire,
                NewPlayerGuideStage.Completed
            };

            Data_Player playerData = new();
            NewPlayerGuideProgressStore progress =
                new(playerData, establishEligibility: true);
            Assert.That(progress.CurrentStage, Is.EqualTo(expectedStages[0]));

            for (int i = 0; i < NewPlayerGuideIds.OrderedMilestones.Length; i++)
            {
                Assert.That(progress.MarkMilestone(NewPlayerGuideIds.OrderedMilestones[i]), Is.True);
                Assert.That(progress.CurrentStage, Is.EqualTo(expectedStages[i + 1]));
            }

            Assert.That(progress.IsCompleted, Is.True);
            NewPlayerGuideProgressStore reloaded = new(playerData);
            Assert.That(reloaded.IsCompleted, Is.True);
            Assert.That(reloaded.CurrentStage, Is.EqualTo(NewPlayerGuideStage.Completed));
        }

        #endregion

        #region Facts 与 JSON

        [Test]
        [Category("Guide.Smoke")]
        public void GuideController_OnlyContributesFacts()
        {
            Type guideType = typeof(NewPlayerGuideController);
            Assert.That(typeof(ICharacterSpeechContextContributor).IsAssignableFrom(guideType), Is.True);
            Assert.That(typeof(ICharacterSpeechProvider).IsAssignableFrom(guideType), Is.False);
            Assert.That(typeof(ICharacterSpeechTriggerSource).IsAssignableFrom(guideType), Is.False);

            string source = ReadProjectFile(
                "Assets/5_Scripts/5-3_GamePlay/Guide/NewPlayerGuideController.cs");
            Assert.That(source, Does.Not.Contain("CharacterSpeechRequest"));
            Assert.That(source, Does.Not.Contain(".Say("));
        }

        [Test]
        [Category("Guide.Smoke")]
        public void GuideJson_IsValidLowPriorityAndCooldownBounded()
        {
            TextAsset guideJson = AssetDatabase.LoadAssetAtPath<TextAsset>(
                "Assets/Resources/Dialogue/Soliloquy/guide_survival.json");
            Assert.That(guideJson, Is.Not.Null);

            CharacterSpeechConfigLoadResult result = CharacterSpeechConfigLoader.LoadSources(
                new[] { new CharacterSpeechConfigSource("guide_survival.json", guideJson.text) },
                logIssues: false);
            Assert.That(result.HasErrors, Is.False, string.Join("\n", result.Issues));
            Assert.That(result.Entries, Has.Count.EqualTo(9));

            HashSet<string> completionFlags = new(StringComparer.Ordinal);
            for (int i = 0; i < result.Entries.Count; i++)
            {
                CharacterSpeechConfigEntry entry = result.Entries[i];
                Assert.That(entry.Id, Does.StartWith("guide.survival."));
                Assert.That(entry.Priority, Is.Not.EqualTo(CharacterSpeechPriority.Emergency));
                Assert.That(entry.Priority, Is.EqualTo(CharacterSpeechPriority.Ambient));
                Assert.That(entry.Cooldown, Is.GreaterThanOrEqualTo(40f));
                Assert.That(entry.Once, Is.True);
                Assert.That(completionFlags.Add(entry.CompletionFlag), Is.True);
                Assert.That(entry.Triggers, Does.Contain(CharacterSpeechTrigger.StateChanged));
                Assert.That(entry.Triggers, Does.Contain(CharacterSpeechTrigger.Idle));
            }
        }

        #endregion

        #region 事件与成功事务

        [Test]
        [Category("Guide.Smoke")]
        public void GameplayProgressEvents_KeepActorAndStableId()
        {
            Player actor = CreatePlayerInstance("ProgressEventActor");
            Player receivedActor = null;
            string receivedId = null;
            Action<Player, string> handler = (eventActor, stableId) =>
            {
                receivedActor = eventActor;
                receivedId = stableId;
            };

            try
            {
                GameplayProgressEvents.CraftSucceeded += handler;
                GameplayProgressEvents.PublishCraftSucceeded(
                    actor,
                    NewPlayerGuideIds.SparkMakerSummoner);
                Assert.That(receivedActor, Is.SameAs(actor));
                Assert.That(receivedId, Is.EqualTo(NewPlayerGuideIds.SparkMakerSummoner));

                receivedActor = null;
                receivedId = null;
                GameplayProgressEvents.PublishCraftSucceeded(null, "invalid");
                GameplayProgressEvents.PublishCraftSucceeded(actor, string.Empty);
                Assert.That(receivedActor, Is.Null);
                Assert.That(receivedId, Is.Null);
            }
            finally
            {
                GameplayProgressEvents.CraftSucceeded -= handler;
                UnityEngine.Object.DestroyImmediate(actor.gameObject);
            }
        }

        [Test]
        [Category("Guide.Smoke")]
        public void ProgressPublishers_AppearOnlyAfterSuccessAnchors()
        {
            AssertOrdered(
                "Assets/5_Scripts/5-3_GamePlay/Inventory/ItemPicker.cs",
                "if (!targetInventory.Data.TryAddItem(itemData))",
                "GameplayProgressEvents.PublishPickupSucceeded");
            AssertOrdered(
                "Assets/5_Scripts/5-3_GamePlay/Inventory/Mod_HandCraftTable.cs",
                "ExecuteCrafting(inputInv, outputInv, recipe, outputItems, isMirrorMatched);",
                "GameplayProgressEvents.PublishCraftSucceeded");
            AssertOrdered(
                "Assets/5_Scripts/5-3_GamePlay/Item/Mod_HandMade.cs",
                "ExecuteCrafting(inputInv, outputInv, recipe, outputItems, isMirrorMatched);",
                "GameplayProgressEvents.PublishCraftSucceeded");
            AssertOrdered(
                "Assets/5_Scripts/5-3_GamePlay/Building/Mod_Building.cs",
                "ApplySourceAmount(authoritativeRemainingAmount);",
                "GameplayProgressEvents.PublishBuildingPlaced(actor, buildingId);");
            AssertOrdered(
                "Assets/5_Scripts/5-3_GamePlay/Tool/Mod_FireDrill.cs",
                "OutputInventory.Data.TryAddItem(fireSeedData, true);",
                "GameplayProgressEvents.PublishFireSeedCreated");
            AssertOrdered(
                "Assets/5_Scripts/5-3_GamePlay/Item/Mod_Furnace.cs",
                "mod_Fuel?.SetIgnited(true);",
                "GameplayProgressEvents.PublishFurnaceIgnited");
        }

        #endregion

        #region 辅助方法

        private static void AssertFacts(
            bool localProfile,
            bool wasCreated,
            string expectedEnabled)
        {
            Player player = CreatePlayerInstance("GuideFactActor");
            GameObject actorObject = player.gameObject;
            actorObject.SetActive(false);
            try
            {
                player.Data = new Data_Player();
                NewPlayerGuideController guide =
                    actorObject.GetComponent<NewPlayerGuideController>();
                Assert.That(guide, Is.Not.Null);
                player.SetProfileContext(localProfile, wasCreated);
                actorObject.SetActive(true);

                CharacterSpeechContext context = new(
                    actorObject.transform,
                    CharacterSpeechTrigger.Debug,
                    0f);
                guide.Contribute(context);

                Assert.That(
                    context.TryGetFact(CharacterSpeechFacts.TutorialEnabled, out string enabled),
                    Is.True);
                Assert.That(enabled, Is.EqualTo(expectedEnabled));
                Assert.That(context.TryGetFact(CharacterSpeechFacts.TutorialStage, out _), Is.True);
                Assert.That(context.TryGetFact(CharacterSpeechFacts.TutorialCompleted, out _), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(actorObject);
            }
        }

        private static void AssertScriptType(string path, Type expectedType)
        {
            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            Assert.That(script, Is.Not.Null, $"缺少脚本：{path}");
            Assert.That(script.GetClass(), Is.EqualTo(expectedType), $"脚本未解析为预期类型：{path}");
        }

        private static Player CreatePlayerInstance(string instanceName)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/Player/Player.prefab");
            Assert.That(prefab, Is.Not.Null);

            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            instance.name = instanceName;
            Player player = instance.GetComponent<Player>();
            Assert.That(player, Is.Not.Null);
            Assert.That(player.Data, Is.Not.Null);
            return player;
        }

        private static void AssertOrdered(string path, string first, string second)
        {
            string source = ReadProjectFile(path);
            int firstIndex = source.IndexOf(first, StringComparison.Ordinal);
            int secondIndex = source.IndexOf(second, StringComparison.Ordinal);
            Assert.That(firstIndex, Is.GreaterThanOrEqualTo(0), $"未找到成功锚点：{path} / {first}");
            Assert.That(secondIndex, Is.GreaterThan(firstIndex), $"事件必须位于成功锚点之后：{path} / {second}");
        }

        private static string ReadProjectFile(string projectRelativePath)
        {
            return File.ReadAllText(Path.GetFullPath(projectRelativePath));
        }

        #endregion
    }
}
