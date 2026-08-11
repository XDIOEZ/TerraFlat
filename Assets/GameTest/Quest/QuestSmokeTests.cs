using System.Collections.Generic;
using System.IO;
using System.Linq;
using FlatWorld.Gameplay.Progress;
using FlatWorld.Gameplay.Quests;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace FlatWorld.GameTest.Quest
{
    /// <summary>任务系统冒烟测试：保护 JSON 扩展目录和玩家命名空间进度的最小稳定契约。</summary>
    public sealed class QuestSmokeTests
    {
        #region 清理

        [TearDown]
        public void TearDown()
        {
            QuestCatalog.Reset();
        }

        #endregion

        #region 目录

        [Test]
        [Category("Quest.Smoke")]
        [Category("Smoke")]
        public void StarterCatalog_RegistersKnownExtensibleHandlers()
        {
            string json = File.ReadAllText(
                Path.GetFullPath("Assets/StreamingAssets/GameConfig/Quests/starter.json"));
            QuestCatalogDto catalog = QuestCatalogLoader.DeserializeCatalog(json);

            QuestCatalog.ReplaceBuiltIns(catalog.Quests);
            QuestCatalog.FinalizeRegistration();

            Assert.That(QuestCatalog.IsReady, Is.True);
            Assert.That(
                QuestCatalog.TryGet("flatworld:first_chipped_tool", out QuestDefinition definition),
                Is.True);
            Assert.That(definition.Stages[0].Objectives[0].Type, Is.EqualTo("signal.count"));
            Assert.That(
                definition.Stages[0].Objectives[0].LabelKey,
                Is.EqualTo("quest.flatworld.first_chipped_tool.objective.craft_chipped_tool"));
            Assert.That(definition.Stages[0].Objectives[0].Label, Is.Not.Empty);
            Assert.That(definition.Rewards[0].Type, Is.EqualTo("item.grant"));
        }

        [Test]
        [Category("Quest.Smoke")]
        public void DebugCatalog_StaysManualAndCoversRepresentativeObjectiveFlows()
        {
            string manifestJson = File.ReadAllText(
                Path.GetFullPath("Assets/StreamingAssets/GameConfig/Quests/quest-manifest.json"));
            QuestManifestDto manifest = QuestCatalogLoader.DeserializeManifest(manifestJson);
            Assert.That(
                manifest.Packages.Any(package =>
                    package.Enabled && package.Path == "debug-tests.json"),
                Is.True,
                "测试任务分包必须由正式任务清单加载。");

            string json = File.ReadAllText(
                Path.GetFullPath("Assets/StreamingAssets/GameConfig/Quests/debug-tests.json"));
            QuestCatalogDto catalog = QuestCatalogLoader.DeserializeCatalog(json);
            Assert.That(catalog.Quests, Has.Count.EqualTo(4));
            Assert.That(catalog.Quests.All(definition => definition.DebugOnly), Is.True);
            Assert.That(
                catalog.Quests.All(definition => definition.AcceptMode == QuestModes.Manual),
                Is.True,
                "调试任务只能由 GM 显式开启。");
            Assert.That(
                catalog.Quests.SelectMany(definition => definition.Stages)
                    .SelectMany(stage => stage.Objectives)
                    .Select(objective => objective.Type),
                Does.Contain("inventory.owns"));
            Assert.That(catalog.Quests.Any(definition => definition.Stages.Count > 1), Is.True);
            Assert.That(
                catalog.Quests.Select(definition => definition.TurnInMode),
                Does.Contain(QuestModes.Auto).And.Contain(QuestModes.Manual));

            QuestCatalog.ReplaceBuiltIns(catalog.Quests);
            QuestCatalog.FinalizeRegistration();
            Assert.That(QuestCatalog.IsReady, Is.True);

            string runtimeSource = File.ReadAllText(
                Path.GetFullPath(
                    "Assets/5_Scripts/5-3_GamePlay/Core/Quests/PlayerQuestRuntime.cs"));
            Assert.That(runtimeSource, Does.Contain("definition.DebugOnly ||"),
                "debugOnly 任务即使误配为 auto 也不能自动进入普通玩家进度。");
        }

        #endregion

        #region 进度存档

        [Test]
        [Category("Quest.Save")]
        public void ProgressStore_RoundTripPreservesOtherNamespacesAndRemovedModRecords()
        {
            var playerData = new Data_Player
            {
                ItemSpecialData =
                    "{\"flatworld.tutorial\":{\"stage\":2},\"flatworld.quests\":{" +
                    "\"version\":1,\"quests\":{\"removed_mod:quest\":{" +
                    "\"definitionVersion\":1,\"status\":\"Completed\"," +
                    "\"currentStageId\":\"done\",\"objectiveProgress\":{}," +
                    "\"completionCount\":1,\"rewardsClaimed\":true," +
                    "\"futureField\":\"kept\"}}}}"
            };

            QuestProgressSaveDocument document = QuestProgressStore.Load(playerData);
            document.Quests["flatworld:test"] = new QuestProgressSaveRecord
            {
                DefinitionVersion = 1,
                Status = QuestStatus.Active,
                CurrentStageId = "start",
                ObjectiveProgress = new Dictionary<string, float>
                {
                    ["start/count"] = 0.5f
                }
            };
            QuestProgressStore.Save(playerData, document);

            QuestProgressSaveDocument restored = QuestProgressStore.Load(playerData);
            Assert.That(restored.Quests.ContainsKey("removed_mod:quest"), Is.True);
            Assert.That(restored.Quests.ContainsKey("flatworld:test"), Is.True);
            Assert.That(
                restored.Quests["removed_mod:quest"].ExtensionData["futureField"].Value<string>(),
                Is.EqualTo("kept"));
            Assert.That(
                ItemSpecialDataJsonStore.ReadRoot(playerData.ItemSpecialData)
                    ["flatworld.tutorial"]?.Value<int>("stage"),
                Is.EqualTo(2));
        }

        #endregion
    }
}
