using System.Collections;
using FlatWorld.Dialogue;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FlatWorld.GameTest.Dialogue
{
    /// <summary>
    /// 对话系统冒烟测试：验证测试场景、上下文、JSON Provider、控制器和 Presenter 的完整链路。
    /// </summary>
    public sealed class DialogueSmokeTests
    {
        private const string ScenePath = "Assets/GameTest/Scenes/Dialogue/DialogueSmokeTest.unity";

        #region 对话主流程

        [UnityTest]
        [Category("Dialogue.Smoke")]
        [Timeout(10000)]
        public IEnumerator CriticalHungerFact_ShowsConfiguredSpeech()
        {
            Assert.That(
                AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath),
                Is.Not.Null,
                $"缺少对话冒烟测试场景：{ScenePath}");

            AsyncOperation loadOperation = EditorSceneManager.LoadSceneAsyncInPlayMode(
                ScenePath,
                new LoadSceneParameters(LoadSceneMode.Single));
            while (!loadOperation.isDone)
                yield return null;

            DialogueSmokeTestProbe probe = Object.FindObjectOfType<DialogueSmokeTestProbe>();
            Assert.That(probe, Is.Not.Null, "测试场景缺少 DialogueSmokeTestProbe。");

            GameObject actor = probe.gameObject;
            Assert.That(actor.name, Is.EqualTo("DialogueSmokeActor"));
            Assert.That(actor.GetComponent<CharacterSoliloquyController>(), Is.Not.Null);
            Assert.That(actor.GetComponent<ConfiguredSpeechProvider>(), Is.Not.Null);

            float deadline = Time.realtimeSinceStartup + 3f;
            while (probe.LastRequest == null && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.That(probe.LastRequest, Is.Not.Null, "对话系统未在限定时间内生成台词。");
            Assert.That(probe.LastRequest.SourceId, Is.EqualTo("need.hunger.critical"));
            Assert.That(probe.LastRequest.Topic, Is.EqualTo("hunger.critical"));
            Assert.That(probe.LastRequest.Priority, Is.EqualTo(CharacterSpeechPriority.Critical));
            Assert.That(probe.LastRequest.Text, Is.EqualTo("我快饿扁了"));
            Assert.That(probe.IsVisible, Is.True, "Presenter 未收到有效台词。");
        }

        #endregion
    }
}
