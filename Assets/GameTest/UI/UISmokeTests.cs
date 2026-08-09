using System;
using System.IO;
using System.Linq;
using FlatWorld.GameTest.Shared;
using InputSystem;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace FlatWorld.GameTest.UI
{
    /// <summary>UI 基础冒烟测试：保护 UI 管理器、面板和根 Prefab 入口。</summary>
    public sealed class UISmokeTests
    {

        [Test]
        [Category("UI.Smoke")]
        public void RuntimeUiNavigationDoesNotBindKeyboardMovement()
        {
            PlayerInputActions inputActions = new PlayerInputActions();

            try
            {
                EventSystemGuard.SynchronizeUIInputBindings(inputActions.asset);
                InputAction navigate = inputActions.asset
                    .FindActionMap("FlatWorldUI", false)?
                    .FindAction("Navigate", false)
                    ?? throw new AssertionException("运行时 UI 缺少 Navigate 动作。");
                string[] paths = navigate.bindings
                    .Select(binding => binding.effectivePath)
                    .Where(path => !string.IsNullOrEmpty(path))
                    .ToArray();

                Assert.That(
                    paths.Any(path => path.IndexOf("<Gamepad>", StringComparison.OrdinalIgnoreCase) >= 0),
                    Is.True,
                    "手柄导航绑定不应被移除。");
                Assert.That(
                    paths.Any(path => path.IndexOf("<Keyboard>", StringComparison.OrdinalIgnoreCase) >= 0),
                    Is.False,
                    "W/A/S/D 不得进入 UI Navigate 动作并移动背包焦点。");
            }
            finally
            {
                inputActions.Dispose();
            }
        }

        [Test]
        [Category("UI.Smoke")]
        public void RuntimeUiCancelDoesNotBindInventoryToggleKey()
        {
            PlayerInputActions inputActions = new PlayerInputActions();

            try
            {
                EventSystemGuard.SynchronizeUIInputBindings(inputActions.asset);
                InputAction cancel = inputActions.asset
                    .FindActionMap("FlatWorldUI", false)?
                    .FindAction("Cancel", false)
                    ?? throw new AssertionException("运行时 UI 缺少 Cancel 动作。");
                string[] paths = cancel.bindings
                    .Select(binding => binding.effectivePath)
                    .Where(path => !string.IsNullOrEmpty(path))
                    .ToArray();

                Assert.That(paths, Does.Contain("<Keyboard>/escape"));
                Assert.That(paths, Does.Not.Contain("<Keyboard>/b"),
                    "键盘 B 只能交给背包开关，不能同时触发 UI 取消。");
            }
            finally
            {
                inputActions.Dispose();
            }
        }

        [Test]
        [Category("UI.Smoke")]
        public void SaveItemKeepsConfirmedSelectionWhenGamepadFocusMoves()
        {
            GameObject itemObject = new GameObject(
                "存档条目测试",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(GameSaveItemView));
            GameObject accentObject = new GameObject(
                "选择强调线",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            accentObject.transform.SetParent(itemObject.transform, false);

            try
            {
                GameSaveItemView itemView = itemObject.GetComponent<GameSaveItemView>()
                    ?? throw new AssertionException("测试条目缺少 GameSaveItemView。");
                Image background = itemObject.GetComponent<Image>()
                    ?? throw new AssertionException("测试条目缺少背景 Image。");
                Image accent = accentObject.GetComponent<Image>()
                    ?? throw new AssertionException("测试条目缺少强调线 Image。");
                itemView.Background = background;
                itemView.SelectionAccent = accent;

                itemView.SetSelected(true);
                itemView.OnSelect(new BaseEventData(null));
                itemView.OnDeselect(new BaseEventData(null));

                Assert.That(itemView.SelectionAccent.enabled, Is.True,
                    "手柄焦点离开后，已确认的存档选择不应丢失。");

                itemView.SetSelected(false);
                Assert.That(itemView.SelectionAccent.enabled, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(accentObject);
                Object.DestroyImmediate(itemObject);
            }
        }

        [Test]
        [Category("UI.Smoke")]
        public void SaveItemPrefabUsesAutomaticGamepadNavigation()
        {
            const string prefabPath = "Assets/2_Prefabs/2-1_UI/存档选择按钮.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, $"缺少存档条目 Prefab：{prefabPath}");

            Button button = prefab.GetComponent<Button>()
                ?? throw new AssertionException("存档条目 Prefab 缺少 Button。");
            Assert.That(button.transition, Is.EqualTo(Selectable.Transition.None));
            Assert.That(button.navigation.mode, Is.EqualTo(UnityEngine.UI.Navigation.Mode.Automatic));
        }

        [Test]
        [Category("UI.Smoke")]
        [Category("Smoke")]
        public void WorldStreamingSettingsPrefabAndEntryFollowNamingContract()
        {
            AssertPrefabContains(
                "Assets/2_Prefabs/2-1_UI/Runtime/Settings/UI_WorldStreamingSettings.prefab",
                "性能模式下拉列表",
                "状态文本",
                "取消按钮",
                "应用按钮");
            AssertPrefabContains(
                "Assets/2_Prefabs/2-1_UI/Menu_UI/Info_Button_List.prefab",
                "流送性能");

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/2-1_UI/Runtime/Settings/UI_WorldStreamingSettings.prefab");
            RectTransform rect = prefab.GetComponent<RectTransform>();
            Assert.That(rect.sizeDelta.x, Is.LessThanOrEqualTo(680f));
            Assert.That(rect.sizeDelta.y, Is.LessThanOrEqualTo(420f));
            Assert.That(prefab.GetComponent<VerticalLayoutGroup>(), Is.Not.Null);
        }

        [Test]
        [Category("UI.Smoke")]
        [Category("UI.Layout")]
        public void MainMenuSettingsVisualPrefabFollowsNamingAndLayoutContract()
        {
            const string mainMenuPath = "Assets/2_Prefabs/2-1_UI/Menu_UI/UI_Hello.prefab";
            const string settingsPath = "Assets/2_Prefabs/2-1_UI/Runtime/Settings/UI_MainMenuSettings.prefab";

            AssertPrefabContains(mainMenuPath, GameManager.MainMenuSettingsButtonKey);
            AssertPrefabContains(
                settingsPath,
                "关闭按钮",
                "窗口大小下拉列表",
                "显示模式下拉列表",
                "画质预设下拉列表",
                "特效质量下拉列表",
                "游戏语言下拉列表",
                "恢复默认按钮",
                "返回按钮");

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(settingsPath);
            RectTransform rootRect = prefab.GetComponent<RectTransform>();
            Assert.That(rootRect.anchorMin, Is.EqualTo(Vector2.zero));
            Assert.That(rootRect.anchorMax, Is.EqualTo(Vector2.one));

            RectTransform dialog = prefab.GetComponentsInChildren<RectTransform>(true)
                .Single(rect => rect.name == "设置对话框");
            Assert.That(dialog.sizeDelta, Is.EqualTo(new Vector2(720f, 600f)));
            Assert.That(dialog.GetComponent<VerticalLayoutGroup>(), Is.Not.Null);
        }

        [Test]
        [Category("UI.Smoke")]
        [Category("Smoke")]
        public void PlayerWorldCoordinateHudPrefabAndPlayerBindingFollowContract()
        {
            const string hudPath = "Assets/2_Prefabs/2-1_UI/Runtime/System/UI_PlayerWorldCoordinate.prefab";
            const string playerPath = "Assets/2_Prefabs/Player/Player.prefab";

            AssertPrefabContains(hudPath, "背景", "强调线", "坐标标题", "坐标文本");

            GameObject hudPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(hudPath)
                ?? throw new AssertionException($"缺少坐标 HUD Prefab：{hudPath}");
            RectTransform rootRect = hudPrefab.GetComponent<RectTransform>()
                ?? throw new AssertionException("坐标 HUD 根节点缺少 RectTransform。");
            Assert.That(rootRect.anchorMin, Is.EqualTo(new Vector2(0f, 1f)));
            Assert.That(rootRect.anchorMax, Is.EqualTo(new Vector2(0f, 1f)));
            Assert.That(rootRect.pivot, Is.EqualTo(new Vector2(0f, 1f)));
            Assert.That(rootRect.anchoredPosition, Is.EqualTo(new Vector2(32f, -32f)));
            Assert.That(rootRect.sizeDelta, Is.EqualTo(new Vector2(296f, 72f)));

            foreach (Graphic graphic in hudPrefab.GetComponentsInChildren<Graphic>(true))
                Assert.That(graphic.raycastTarget, Is.False, $"坐标 HUD 不应拦截输入：{graphic.name}");

            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(playerPath)
                ?? throw new AssertionException($"缺少玩家 Prefab：{playerPath}");
            Assert.That(playerPrefab.GetComponent<PlayerWorldCoordinateHUD>(), Is.Not.Null,
                "Player 必须挂载 PlayerWorldCoordinateHUD，才能在进入世界后自动创建坐标面板。");
        }

        [Test]
        [Category("UI.Smoke")]
        [Category("Smoke")]
        public void CoordinateDisplaySettingsAndActionListPagerFollowContract()
        {
            const string displaySettingsPath =
                "Assets/2_Prefabs/2-1_UI/Runtime/Settings/UI_CoordinateDisplaySettings.prefab";
            const string actionListPath =
                "Assets/2_Prefabs/2-1_UI/Menu_UI/Info_Button_List.prefab";

            AssertPrefabContains(
                displaySettingsPath,
                "世界坐标模式按钮",
                "经纬度模式按钮",
                "状态文本",
                "完成按钮");
            AssertPrefabContains(
                actionListPath,
                SettingsActionListPagination.InterfacePageName,
                SettingsActionListPagination.WorldPageName,
                SettingsActionListPagination.SessionPageName,
                SettingsActionListPagination.PreviousButtonName,
                SettingsActionListPagination.NextButtonName,
                SettingsActionListPagination.PageTextName,
                "显示设置");

            GameObject displaySettings = AssetDatabase.LoadAssetAtPath<GameObject>(displaySettingsPath)
                ?? throw new AssertionException($"缺少显示设置 Prefab：{displaySettingsPath}");
            RectTransform displayRoot = displaySettings.GetComponent<RectTransform>()
                ?? throw new AssertionException("显示设置根节点缺少 RectTransform。");
            Assert.That(displayRoot.sizeDelta, Is.EqualTo(new Vector2(620f, 360f)));
            Assert.That(displaySettings.GetComponent<VerticalLayoutGroup>(), Is.Not.Null);

            GameObject actionList = AssetDatabase.LoadAssetAtPath<GameObject>(actionListPath)
                ?? throw new AssertionException($"缺少设置入口 Prefab：{actionListPath}");
            ScrollRect scroll = actionList.GetComponentsInChildren<ScrollRect>(true)
                .Single(item => item.name == "Scroll View");
            Assert.That(scroll.vertical, Is.False, "分页列表不能继续依赖纵向滚动。");

            RectTransform content = actionList.GetComponentsInChildren<RectTransform>(true)
                .Single(item => item.name == "Content");
            Assert.That(content.GetComponent<GridLayoutGroup>(), Is.Null);
            Assert.That(content.GetComponent<ContentSizeFitter>(), Is.Null);

            string[] pageNames =
            {
                SettingsActionListPagination.InterfacePageName,
                SettingsActionListPagination.WorldPageName,
                SettingsActionListPagination.SessionPageName
            };
            for (int index = 0; index < pageNames.Length; index++)
            {
                RectTransform page = actionList.GetComponentsInChildren<RectTransform>(true)
                    .Single(item => item.name == pageNames[index]);
                Assert.That(page.anchorMin, Is.EqualTo(Vector2.zero), pageNames[index]);
                Assert.That(page.anchorMax, Is.EqualTo(Vector2.one), pageNames[index]);
                Assert.That(page.GetComponent<VerticalLayoutGroup>(), Is.Not.Null, pageNames[index]);
            }

            Button displayEntry = actionList.GetComponentsInChildren<Button>(true)
                .Single(item => item.name == "显示设置");
            Assert.That(
                displayEntry.transform.parent.name,
                Is.EqualTo(SettingsActionListPagination.InterfacePageName));
        }


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
