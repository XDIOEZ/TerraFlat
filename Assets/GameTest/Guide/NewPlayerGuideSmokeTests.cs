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


        #endregion

        #region 玩家资格




        #endregion

        #region 命名空间安全存档



        [Test]
        [Category("Guide.Smoke")]
        [Category("Smoke")]
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




        #endregion

        #region Facts 与 JSON



        #endregion

        #region 事件与成功事务



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
                player.BindData(new Data_Player());
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
            Assert.That(firstIndex, Is.GreaterThanOrEqualTo(0), $"未找到成功锚点：{path} / {first}");
            int secondIndex = source.IndexOf(
                second,
                firstIndex + first.Length,
                StringComparison.Ordinal);
            Assert.That(secondIndex, Is.GreaterThan(firstIndex), $"事件必须位于成功锚点之后：{path} / {second}");
        }

        private static string ReadProjectFile(string projectRelativePath)
        {
            return File.ReadAllText(Path.GetFullPath(projectRelativePath));
        }

        #endregion
    }
}
