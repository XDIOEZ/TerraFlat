using System.IO;
using System.Linq;
using FlatWorld.GameTest.Shared;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FlatWorld.GameTest.UI
{
    /// <summary>UI 基础冒烟测试：保护 UI 管理器、面板和根 Prefab 入口。</summary>
    public sealed class UISmokeTests
    {


[Test]
        [Category("UI.Smoke")]
        [Category("Smoke")]
        public void CancelRoutingSelectsTopmostTemporaryPanelAndSkipsHudAndSettings()
        {
            GameObject rootObject = new GameObject("CancelRoutingRoot", typeof(RectTransform));
            try
            {
                BasePanel hud = CreateTestPanel(rootObject.transform, "HUD", true, false);
                BasePanel bag = CreateTestPanel(rootObject.transform, "Bag", true);
                BasePanel settings = CreateTestPanel(rootObject.transform, "Settings", true);

                Assert.That(
                    UIManager.FindTopmostCancelPanel(rootObject.transform, settings),
                    Is.SameAs(bag));

                bag.Close();
                Assert.That(
                    UIManager.FindTopmostCancelPanel(rootObject.transform, settings),
                    Is.Null);
                Assert.That(hud.IsOpen(), Is.True, "常驻 HUD 不应成为 Escape 关闭目标。");
                Assert.That(settings.IsOpen(), Is.True, "设置面板由调用方单独切换，不应在额外面板阶段关闭。");
            }
            finally
            {
                Object.DestroyImmediate(rootObject);
            }
        }






        [Test]
        [Category("UI.Layout")]
        public void CharacterStatusPanelContainsTemperatureAndFitsSupportedScreens()
        {
            const string prefabPath = "Assets/2_Prefabs/2-1_UI/ModsUI/UI_Food.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, $"缺少角色参数面板：{prefabPath}");

            RectTransform rootRect = prefab.GetComponent<RectTransform>();
            RectTransform panelRect = prefab.GetComponentsInChildren<RectTransform>(true)
                .Single(rect => rect.name == "Panel");
            Assert.That(rootRect.anchorMin, Is.EqualTo(Vector2.zero));
            Assert.That(rootRect.anchorMax, Is.EqualTo(Vector2.one),
                "参数面板根节点应铺满 Canvas，拖拽坐标才能与 Canvas 坐标一致。");
            Assert.That(panelRect.sizeDelta, Is.EqualTo(new Vector2(382f, 324f)));

            string[] rowNames = { "碳水", "脂肪", "蛋白质", "水", "维生素", "体温" };
            Slider[] sliders = prefab.GetComponentsInChildren<Slider>(true);
            for (int i = 0; i < rowNames.Length; i++)
            {
                Slider slider = sliders.Single(item => item.name == rowNames[i]);
                RectTransform row = slider.GetComponent<RectTransform>();
                Assert.That(row.anchorMin, Is.EqualTo(new Vector2(0f, 1f)), rowNames[i]);
                Assert.That(row.anchorMax, Is.EqualTo(new Vector2(0f, 1f)), rowNames[i]);
                Assert.That(row.anchoredPosition,
                    Is.EqualTo(new Vector2(28f, -(86f + i * 38f))), rowNames[i]);
                Assert.That(row.sizeDelta, Is.EqualTo(new Vector2(326f, 26f)), rowNames[i]);
                Assert.That(slider.interactable, Is.False, $"{rowNames[i]} 只是状态显示，不应可拖动。");
            }

            TextMeshProUGUI temperatureText = prefab.GetComponentsInChildren<TextMeshProUGUI>(true)
                .SingleOrDefault(text => text.name == "DataText_体温");
            Assert.That(temperatureText, Is.Not.Null);
            Assert.That(temperatureText.raycastTarget, Is.False);
            Assert.That(prefab.GetComponentsInChildren<TextMeshProUGUI>(true)
                .Single(text => text.name == "FWUI_Card标题").text, Is.EqualTo("角色参数"));

            Vector2[] resolutions =
            {
                new Vector2(2560f, 1440f),
                new Vector2(1920f, 1080f),
                new Vector2(1600f, 900f),
                new Vector2(1280f, 720f),
                new Vector2(1024f, 768f)
            };
            foreach (Vector2 resolution in resolutions)
            {
                float widthScale = resolution.x / 1920f;
                float logicalHeight = resolution.y / widthScale;
                Rect canvasBounds = new Rect(-960f, -logicalHeight * 0.5f, 1920f, logicalHeight);
                Rect panelBounds = new Rect(
                    panelRect.anchoredPosition - panelRect.sizeDelta * 0.5f,
                    panelRect.sizeDelta);
                Assert.That(panelBounds.xMin, Is.GreaterThanOrEqualTo(canvasBounds.xMin + 20f), resolution.ToString());
                Assert.That(panelBounds.xMax, Is.LessThanOrEqualTo(canvasBounds.xMax - 20f), resolution.ToString());
                Assert.That(panelBounds.yMin, Is.GreaterThanOrEqualTo(canvasBounds.yMin + 20f), resolution.ToString());
                Assert.That(panelBounds.yMax, Is.LessThanOrEqualTo(canvasBounds.yMax - 20f), resolution.ToString());
            }
        }

        private static void AssertPrefabContains(string prefabPath, params string[] expectedNames)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, $"缺少 UI Prefab：{prefabPath}");

            string[] objectNames = prefab.GetComponentsInChildren<Transform>(true)
                .Select(item => item.name)
                .ToArray();
            foreach (string expectedName in expectedNames)
                Assert.That(objectNames, Does.Contain(expectedName), $"{prefabPath} 缺少节点：{expectedName}");
        }

        private static BasePanel CreateTestPanel(
            Transform parent,
            string name,
            bool prepareForCancel,
            bool closeOnEscape = true)
        {
            GameObject panelObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasGroup),
                typeof(BasePanel));
            panelObject.transform.SetParent(parent, false);

            BasePanel panel = panelObject.GetComponent<BasePanel>();
            panel.Init();
            if (prepareForCancel)
                panel.PrepareForGamepadNavigation(closeOnEscape: closeOnEscape);
            panel.Open();
            return panel;
        }
    }
}
