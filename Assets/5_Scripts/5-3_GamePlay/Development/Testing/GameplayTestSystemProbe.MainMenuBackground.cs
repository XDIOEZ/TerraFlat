#if UNITY_EDITOR
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static partial class GameplayTestSystemProbe
{
    #region 主菜单轮播规则采样
    [InitializeOnLoadMethod]
    private static void RegisterMainMenuBackgroundRules() =>
        Register("ui_settings", "main_menu_background_slideshow", CaptureMainMenuBackgroundSlideshow);

    private static JObject CaptureMainMenuBackgroundSlideshow(JObject args)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/2_Prefabs/2-1_UI/MainMenu/Core/UI_MainMenu.prefab");
        var source = prefab.GetComponentInChildren<MainMenuBackgroundSlideshowController>(true);
        var serialized = new SerializedObject(source);
        var references = serialized.FindProperty("backgrounds");
        var sprites = new Sprite[references.arraySize];
        var paths = new JArray();
        for (int i = 0; i < sprites.Length; i++)
        {
            sprites[i] = (Sprite)references.GetArrayElementAtIndex(i).objectReferenceValue;
            paths.Add(AssetDatabase.GetAssetPath(sprites[i]));
        }

        var scene = CreateGameplayTestProbeScene("MainMenuBackgroundRules");
        var fixture = new GameObject("[LuaMainMenuBackground]", typeof(RectTransform), typeof(CanvasGroup));
        fixture.SetActive(false);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(fixture, scene);
        float previousScale = Time.timeScale;
        Random.State previousRandom = Random.state;
        try
        {
            var panel = fixture.AddComponent<BasePanel>();
            var background = Object.Instantiate(source.gameObject, fixture.transform, false);
            var controller = background.GetComponent<MainMenuBackgroundSlideshowController>();
            var bottom = background.transform.Find("世界远景").GetComponent<Image>();
            var top = background.transform.Find("世界远景交叉淡化").GetComponent<Image>();
            controller.ConfigureBackgroundSlideshow(panel, sprites, bottom, top);
            EnableGameplayTestProbeLifecycleCallbacks(controller);
            EnableGameplayTestProbeLifecycleCallbacks(panel);
            fixture.SetActive(true);
            // 重现正式 UIManager 的先实例化、后 Init、再 Start 顺序。
            if (Flag(args, "initializeClosed", false)) panel.InitClosed();
            else panel.Init();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = typeof(MainMenuBackgroundSlideshowController);
            type.GetMethod("Start", flags).Invoke(controller, null);
            var advance = type.GetMethod("AdvanceBackgroundSlideshow", flags);
            var currentIndex = type.GetField("currentIndex", flags);
            var currentLayer = type.GetField("currentLayer", flags);
            var incomingLayer = type.GetField("incomingLayer", flags);
            JObject Sample()
            {
                var current = (Image)currentLayer.GetValue(controller);
                var incoming = (Image)incomingLayer.GetValue(controller);
                return new JObject
                {
                    ["index"] = (int)currentIndex.GetValue(controller),
                    ["elapsed"] = (float)type.GetField("elapsed", flags).GetValue(controller),
                    ["running"] = (bool)type.GetField("isRunning", flags).GetValue(controller),
                    ["fading"] = (bool)type.GetField("isFading", flags).GetValue(controller),
                    ["bottomAlpha"] = current.color.a,
                    ["topAlpha"] = incoming.color.a,
                    ["topEnabled"] = incoming.enabled,
                    ["bottomMatchesIndex"] = (int)currentIndex.GetValue(controller) >= 0 && current.sprite == sprites[(int)currentIndex.GetValue(controller)],
                    ["nextMatchesIndex"] = (int)currentIndex.GetValue(controller) >= 0 && incoming.sprite == sprites[((int)currentIndex.GetValue(controller) + 1) % sprites.Length],
                    ["layerOrder"] = current.transform.GetSiblingIndex() == 0 && incoming.transform.GetSiblingIndex() == 1
                };
            }
            var result = new JObject
            {
                ["paths"] = paths,
                ["uniqueCount"] = sprites.Distinct().Count(),
                ["validCount"] = sprites.Count(sprite => sprite != null),
                ["holdDuration"] = serialized.FindProperty("holdDuration").floatValue,
                ["fadeDuration"] = serialized.FindProperty("fadeDuration").floatValue,
                ["prefabBindings"] = serialized.FindProperty("menuPanel").objectReferenceValue == prefab.GetComponent<BasePanel>() &&
                    serialized.FindProperty("currentLayer").objectReferenceValue == source.transform.Find("世界远景").GetComponent<Image>() &&
                    serialized.FindProperty("incomingLayer").objectReferenceValue == source.transform.Find("世界远景交叉淡化").GetComponent<Image>(),
                ["inputTransparent"] = background.GetComponentsInChildren<Image>(true).All(image => !image.raycastTarget),
                ["fullScreen"] = background.GetComponent<FullScreenRectController>() != null,
                ["envelope"] = new[] { bottom, top }.All(image => image.GetComponent<AspectRatioFitter>().aspectMode == AspectRatioFitter.AspectMode.EnvelopeParent),
                ["initial"] = Sample()
            };
            Time.timeScale = (float)Number(args, "timeScale", 1);
            var samples = new JArray();
            foreach (JObject step in (JArray)args["steps"])
            {
                switch ((string)step["action"])
                {
                    case "close": panel.Close(); break;
                    case "open": panel.Open(); break;
                    case "disable": background.SetActive(false); break;
                    case "enable": background.SetActive(true); break;
                    default: advance.Invoke(controller, new object[] { (float)Number(step, "delta", 0) }); break;
                }
                samples.Add(Sample());
            }
            result["samples"] = samples;
            return result;
        }
        finally
        {
            Time.timeScale = previousScale;
            Random.state = previousRandom;
            Object.DestroyImmediate(fixture);
            CloseGameplayTestProbeScene(scene);
        }
    }
    #endregion
}
#endif
