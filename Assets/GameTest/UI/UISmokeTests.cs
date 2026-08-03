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
        public void RequiredEntryPointsAndAssetsExist()
        {
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-5_UI/UIManager.cs", "UIManager");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-5_UI/BasePanel.cs", "BasePanel");
            GameTestAssertions.AssertAssetExists("Assets/Resources/UI/UIRoot.prefab");
            GameTestAssertions.AssertFolderContainsAsset("Assets/2_Prefabs/2-1_UI", "t:Prefab");
        }

        [Test]
        [Category("UI.Smoke")]
        public void RuntimePrefabsUseTheSharedEventSystem()
        {
            string[] prefabPaths =
            {
                "Assets/Resources/UI/UIRoot.prefab",
                "Assets/2_Prefabs/GameManager/WorldManager.prefab"
            };

            foreach (string prefabPath in prefabPaths)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Assert.That(prefab, Is.Not.Null, $"Missing runtime prefab: {prefabPath}");
                Assert.That(prefab.GetComponentInChildren<EventSystem>(true), Is.Null,
                    $"{prefabPath} must use EventSystemGuard's shared EventSystem.");
                Assert.That(prefab.GetComponentsInChildren<BaseInputModule>(true), Is.Empty,
                    $"{prefabPath} must not create another UI input module.");
            }
        }

        [UnityEngine.TestTools.UnityTest]
        [Category("UI.Smoke")]
        public System.Collections.IEnumerator GameStartCreatesMainMenuWithExactlyOneEventSystem()
        {
            UnityEngine.AsyncOperation loadOperation =
                UnityEngine.SceneManagement.SceneManager.LoadSceneAsync("GameStartScene");
            while (!loadOperation.isDone)
                yield return null;

            EventSystem[] eventSystems = null;
            GameObject panelRoot = null;
            BasePanel mainMenu = null;

            for (int frame = 0; frame < 60; frame++)
            {
                eventSystems = Object.FindObjectsOfType<EventSystem>(true);
                panelRoot = GameObject.Find("PanelRoot");
                mainMenu = Object.FindObjectsOfType<BasePanel>(true)
                    .FirstOrDefault(panel => panel.PanelName == GameManager.MainMenuPanelKey);

                if (eventSystems.Length == 1 && panelRoot != null &&
                    mainMenu != null && mainMenu.gameObject.activeInHierarchy)
                    break;

                yield return null;
            }

            Assert.That(eventSystems, Has.Length.EqualTo(1),
                "GameStartScene must have exactly one EventSystem.");
            Assert.That(panelRoot, Is.Not.Null,
                "UIManager failed to keep the runtime PanelRoot alive.");
            Assert.That(mainMenu, Is.Not.Null,
                "GameStartIndex failed to create the main menu panel.");
            Assert.That(mainMenu.gameObject.activeInHierarchy, Is.True,
                "The main menu panel was created but is not visible.");
        }

        [Test]
        [Category("UI.Smoke")]
        public void TmpSettingsProvideChineseFallbackForRuntimeGeneratedText()
        {
            const string fontPath =
                "Assets/Plugins/TextMesh Pro/Fonts/fusion-pixel-12px-monospaced-zh_hans.asset";
            TMP_FontAsset chineseFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);

            Assert.That(chineseFont, Is.Not.Null, $"Missing Chinese TMP font: {fontPath}");
            Assert.That(TMP_Settings.fallbackFontAssets, Does.Contain(chineseFont));

            int[] reportedCharacters = { 0x5F00, 0x7BA1, 0x7406, 0x5DE5, 0x5177 };
            foreach (int character in reportedCharacters)
                Assert.That(chineseFont.HasCharacter(character), Is.True,
                    $"Chinese TMP font is missing U+{character:X4}");
        }

        [Test]
        [Category("UI.Smoke")]
        public void BasePanelExposesGamepadNavigationContract()
        {
            Assert.That(typeof(ICancelHandler).IsAssignableFrom(typeof(BasePanel)), Is.True);
            Assert.That(
                typeof(BasePanel).GetMethod(nameof(BasePanel.PrepareForGamepadNavigation)),
                Is.Not.Null);
            Assert.That(
                typeof(BasePanel).GetMethod(nameof(BasePanel.SetEscapeShortcutEnabled)),
                Is.Not.Null);
            Assert.That(typeof(BasePanel).GetProperty(nameof(BasePanel.IsCancelShortcutTarget)), Is.Not.Null);
            Assert.That(
                typeof(UIManager).GetMethod(nameof(UIManager.TryCloseTopmostCancelPanel)),
                Is.Not.Null);
            Assert.That(typeof(BasePanel).GetEvent(nameof(BasePanel.Opened)), Is.Not.Null);
            Assert.That(typeof(BasePanel).GetEvent(nameof(BasePanel.Closed)), Is.Not.Null);
        }

        [Test]
        [Category("UI.Smoke")]
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
        [Category("UI.Smoke")]
        public void RuntimeSettingsUseInspectablePrefabs()
        {
            AssertPrefabContains(
                "Assets/2_Prefabs/2-1_UI/Runtime/Settings/UI_AudioSettings.prefab",
                "MasterVolume", "MusicVolume", "SfxVolume", "UIVolume", "AmbientVolume", "VoiceVolume");
            AssertPrefabContains(
                "Assets/2_Prefabs/2-1_UI/Runtime/Settings/UI_InterfaceSettings.prefab",
                "界面缩放", "安全区域适配", "恢复默认按钮", "完成按钮");
            AssertPrefabContains(
                "Assets/2_Prefabs/2-1_UI/Runtime/Settings/UI_AutoSaveSettings.prefab",
                "自动保存间隔下拉列表", "自动保存间隔输入框", "应用按钮");
            AssertPrefabContains(
                "Assets/2_Prefabs/2-1_UI/Runtime/Settings/UI_DifficultySettings.prefab",
                "难度_Simple", "难度_Hard", "应用按钮");
            AssertPrefabContains(
                "Assets/2_Prefabs/2-1_UI/Runtime/Settings/UI_InputBindingSettings.prefab",
                "设备分页", "键鼠分页按钮", "手柄分页按钮",
                "绑定列表", "Content", "恢复默认按钮", "完成按钮");
            AssertPrefabContains(
                "Assets/2_Prefabs/2-1_UI/Runtime/Settings/UI_InputBindingRow.prefab",
                "操作名称", "绑定值", "修改按钮");
            AssertPrefabContains(
                "Assets/2_Prefabs/2-1_UI/Runtime/System/UI_WorldLoading.prefab",
                GameManager.WorldLoadingTitleKey,
                GameManager.WorldLoadingStatusKey,
                GameManager.WorldLoadingProgressKey,
                GameManager.WorldLoadingProgressTextKey,
                "加载提示");

            AssertPrefabContains(
                "Assets/2_Prefabs/2-1_UI/Menu_UI/Info_Button_List.prefab",
                "音量调节", "UI设置", "自动保存", "游戏难度", "按键绑定");
        }

        [Test]
        [Category("UI.Smoke")]
        public void RuntimeUIScriptsDoNotBuildVisualTrees()
        {
            string[] scriptPaths =
            {
                "Assets/5_Scripts/5-3_GamePlay/UI/AudioSettingsPanelLauncher.cs",
                "Assets/5_Scripts/5-3_GamePlay/UI/UISettingsPanelLauncher.cs",
                "Assets/5_Scripts/5-3_GamePlay/UI/AutoSaveSettingsPanelLauncher.cs",
                "Assets/5_Scripts/5-3_GamePlay/UI/DifficultySettingsPanelLauncher.cs",
                "Assets/5_Scripts/5-3_GamePlay/UI/InputBindingPanelLauncher.cs",
                "Assets/5_Scripts/5-5_UI/UIManager.cs"
            };

            foreach (string scriptPath in scriptPaths)
            {
                string source = File.ReadAllText(scriptPath);
                Assert.That(source, Does.Not.Contain("AddComponent<Canvas>"), scriptPath);
                Assert.That(source, Does.Not.Contain("AddComponent<Image>"), scriptPath);
                Assert.That(source, Does.Not.Contain("AddComponent<Button>"), scriptPath);
                Assert.That(source, Does.Not.Contain("AddComponent<TextMeshProUGUI>"), scriptPath);
            }
        }

        [Test]
        [Category("UI.Smoke")]
        public void NewGamePrefabContainsDifficultyEntryAndPages()
        {
            const string prefabPath = "Assets/2_Prefabs/2-1_UI/Menu_UI/UI_NewGame.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, $"缺少新世界 Prefab：{prefabPath}");

            string[] buttonNames = prefab.GetComponentsInChildren<Button>(true)
                .Select(button => button.name)
                .ToArray();
            string[] objectNames = prefab.GetComponentsInChildren<Transform>(true)
                .Select(item => item.name)
                .ToArray();
            string[] sliderNames = prefab.GetComponentsInChildren<Slider>(true)
                .Select(slider => slider.name)
                .ToArray();

            Assert.That(buttonNames, Does.Contain(GameManager.NewGameDifficultyButtonKey));
            Assert.That(buttonNames, Does.Contain(GameManager.NewGameDifficultyOfficialTabKey));
            Assert.That(buttonNames, Does.Contain(GameManager.NewGameDifficultyCustomTabKey));
            Assert.That(GameDifficultyCatalog.All.All(definition => buttonNames.Contains(
                $"官方难度预设_{definition.Id}")), Is.True);
            Assert.That(objectNames, Does.Contain(GameManager.NewGameDifficultyOfficialPageKey));
            Assert.That(objectNames, Does.Contain(GameManager.NewGameDifficultyCustomPageKey));
            Assert.That(objectNames, Does.Contain(GameManager.NewGameDifficultyCombatPageKey));
            Assert.That(objectNames, Does.Contain(GameManager.NewGameDifficultySurvivalPageKey));
            Assert.That(objectNames, Does.Contain(GameManager.NewGameDifficultyWorldPageKey));
            Assert.That(objectNames, Does.Contain(GameManager.NewGameDifficultyProductionPageKey));
            Assert.That(buttonNames, Does.Contain(GameManager.NewGameDifficultyCombatCategoryKey));
            Assert.That(buttonNames, Does.Contain(GameManager.NewGameDifficultySurvivalCategoryKey));
            Assert.That(buttonNames, Does.Contain(GameManager.NewGameDifficultyWorldCategoryKey));
            Assert.That(buttonNames, Does.Contain(GameManager.NewGameDifficultyProductionCategoryKey));
            Assert.That(prefab.GetComponentsInChildren<Toggle>(true).Any(toggle =>
                toggle.name == GameManager.NewGameDifficultyDropToggleKey), Is.True);
            Assert.That(sliderNames, Does.Contain(GameManager.NewGameDifficultyPlayerAttackSliderKey));
            Assert.That(sliderNames, Does.Contain(GameManager.NewGameDifficultyCreatureAttackSliderKey));
            Assert.That(sliderNames, Does.Contain(GameManager.NewGameDifficultyCreatureHealthSliderKey));
            Assert.That(sliderNames, Does.Contain(GameManager.NewGameDifficultyHungerDrainSliderKey));
            Assert.That(sliderNames, Does.Contain(GameManager.NewGameDifficultyTimeSpeedSliderKey));
            Assert.That(sliderNames, Does.Contain(GameManager.NewGameDifficultySpawnFrequencySliderKey));
            Assert.That(sliderNames, Does.Contain(GameManager.NewGameDifficultyCropGrowthSliderKey));
            Assert.That(sliderNames, Does.Contain(GameManager.NewGameDifficultyCraftingOutputSliderKey));
            Assert.That(sliderNames.Length, Is.GreaterThanOrEqualTo(16));
        }

        [Test]
        [Category("UI.Smoke")]
        public void NewGamePrefabUsesSharedTerrainScaleDefault()
        {
            const string prefabPath = "Assets/2_Prefabs/2-1_UI/Menu_UI/UI_NewGame.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, $"缺少新世界 Prefab：{prefabPath}");

            TMP_InputField noiseInput = prefab.GetComponentsInChildren<TMP_InputField>(true)
                .SingleOrDefault(input => input.name == GameManager.NewGameNoiseInputKey);
            Assert.That(noiseInput, Is.Not.Null, "新世界 Prefab 缺少世界坐标缩放输入框。");
            Assert.That(noiseInput.contentType, Is.EqualTo(TMP_InputField.ContentType.DecimalNumber));
            Assert.That(float.TryParse(noiseInput.text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float defaultValue), Is.True);
            Assert.That(defaultValue, Is.EqualTo(PlanetData.DefaultNoiseScale));
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
