using System.IO;
using System.Linq;
using FlatWorld.GameTest.Shared;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
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
    }
}
