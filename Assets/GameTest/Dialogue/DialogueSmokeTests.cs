using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
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

        [Test]
        [Category("Dialogue.Smoke")]
        public void RuntimeDialogueUIUsesInspectablePrefabs()
        {
            AssertPrefabContains(
                "Assets/2_Prefabs/2-1_UI/Runtime/Dialogue/UI_PlayerChatInput.prefab",
                "Text Area", "Placeholder", "Text");
            AssertPrefabContains(
                "Assets/2_Prefabs/2-1_UI/Runtime/Dialogue/UI_CharacterSpeechBubble.prefab",
                "Tail", "Message");

            string chatSource = File.ReadAllText(
                "Assets/5_Scripts/5-3_GamePlay/Dialogue/PlayerChatInputController.cs");
            string bubbleSource = File.ReadAllText(
                "Assets/5_Scripts/5-3_GamePlay/Dialogue/ScreenSpaceSpeechBubblePresenter.cs");
            Assert.That(chatSource, Does.Not.Contain("new GameObject"));
            Assert.That(bubbleSource, Does.Not.Contain("new GameObject"));
        }

        [Test]
        [Category("Dialogue.Weather")]
        public void PlayerPrefabProvidesWeatherFactsAndRainLines()
        {
            const string playerPath = "Assets/2_Prefabs/Player/Player.prefab";
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(playerPath);
            Assert.That(player, Is.Not.Null, $"缺少玩家 Prefab：{playerPath}");
            Assert.That(player.GetComponent<WeatherExposureSpeechProvider>(), Is.Not.Null);

            TextAsset weatherConfig = AssetDatabase.LoadAssetAtPath<TextAsset>(
                "Assets/Resources/Dialogue/Soliloquy/weather_rain.json");
            Assert.That(weatherConfig, Is.Not.Null);

            CharacterSpeechConfigLoadResult result = CharacterSpeechConfigLoader.LoadSources(
                new[] { new CharacterSpeechConfigSource("weather_rain.json", weatherConfig.text) },
                logIssues: false);
            Assert.That(result.Issues, Is.Empty);
            Assert.That(result.Entries.Select(entry => entry.Id), Does.Contain("weather.rain.exposed"));
            Assert.That(result.Entries.Select(entry => entry.Id), Does.Contain("weather.rain.recovery"));
        }

        [Test]
        [Category("Dialogue.Smoke")]
        public void WeatherHeatSourceScanSkipsItemsWithoutFuelModules()
        {
            GameObject actor = new("WeatherExposureActor");
            GameObject nearbyItemObject = new("NonFuelNearbyItem");

            try
            {
                WeatherExposureSpeechProvider provider = actor.AddComponent<WeatherExposureSpeechProvider>();
                nearbyItemObject.AddComponent<WeatherExposureTestItem>();
                nearbyItemObject.AddComponent<CircleCollider2D>();
                nearbyItemObject.transform.position = new Vector3(10000f, 10000f, 0f);
                actor.transform.position = nearbyItemObject.transform.position;
                Physics2D.SyncTransforms();

                MethodInfo scanMethod = typeof(WeatherExposureSpeechProvider).GetMethod(
                    "FindNearbyIgnitedHeatSource",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(scanMethod, Is.Not.Null);

                bool hasHeatSource = (bool)scanMethod.Invoke(provider, null);
                Assert.That(hasHeatSource, Is.False);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Object.DestroyImmediate(actor);
                Object.DestroyImmediate(nearbyItemObject);
            }
        }

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

        private static void AssertPrefabContains(string prefabPath, params string[] expectedNames)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, $"缺少对话 UI Prefab：{prefabPath}");
            string[] objectNames = prefab.GetComponentsInChildren<Transform>(true)
                .Select(item => item.name)
                .ToArray();
            foreach (string expectedName in expectedNames)
                Assert.That(objectNames, Does.Contain(expectedName), $"{prefabPath} 缺少节点：{expectedName}");
        }

        #endregion
    }

    public sealed class WeatherExposureTestItem : Item
    {
        private Data_GeneralItem data = new()
        {
            IDName = "WeatherExposureTestItem",
            Stack = new ItemStack()
        };

        public override ItemData itemData
        {
            get => data;
            set => data = (Data_GeneralItem)value;
        }
    }
}
