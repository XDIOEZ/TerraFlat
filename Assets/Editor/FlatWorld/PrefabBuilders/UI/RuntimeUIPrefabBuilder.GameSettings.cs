using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static partial class RuntimeUIPrefabBuilder
{
    #region 游戏设置分页

    /// <summary>定向重建游戏设置页，并保留两个主设置面板的其他内容。</summary>
    [MenuItem("FlatWorld/UI/Rebuild Game Settings UI")]
    public static void RebuildGameSettingsUI()
    {
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            Debug.LogError($"[Runtime UI] 缺少统一字体：{FontPath}");
            return;
        }

        SaveGameSettingsPrefab();
        UpdateExistingPrefab(MainMenuCoreRoot + "UI_ActionList.prefab", EnsureGameSettingsPage);
        UpdateExistingPrefab(SettingsPanelsRoot + RuntimeUIPrefabKeys.MainMenuSettings + ".prefab", EnsureGameSettingsPage);
        FlatWorld.Localization.Editor.FlatWorldLocalizationSetup.SyncRuntimeUiTexts(
            "游戏设置", "默认区块宽度", "默认区块高度", GameSettingsPanel.Description);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static void SaveGameSettingsPrefab()
    {
        Directory.CreateDirectory(SettingsPanelsRoot);
        string path = SettingsPanelsRoot + RuntimeUIPrefabKeys.GameSettings + ".prefab";
        SaveNewPrefab(path, BuildGameSettings);
        EnsureRuntimePrefabAddressable(path);
    }

    /// <summary>复用正式滚动页和公共滑块，尺寸范围由星球数据契约提供。</summary>
    private static GameObject BuildGameSettings()
    {
        GameObject root = CreateSettingsPageRoot(RuntimeUIPrefabKeys.GameSettings, out Transform content);
        CreateSettingsHeader(content, "游戏设置");
        CreateSettingsHint(content, GameSettingsPanel.Description, 64f);
        CreateGameSettingsDimensionRow(content, GameSettingsPanel.WidthSliderName);
        CreateGameSettingsDimensionRow(content, GameSettingsPanel.HeightSliderName);
        Transform footer = CreateFooter(content);
        footer.GetComponent<LayoutElement>().preferredHeight = 64f;
        CreateSettingsButton("恢复默认按钮", footer, "恢复默认", 200f, 64f, false);
        root.AddComponent<GameSettingsPanel>();
        return root;
    }

    private static void CreateGameSettingsDimensionRow(Transform content, string name)
    {
        GameObject row = CreateRow(name + "行", content, 64f);
        CreateRowLabel(row.transform, name, 140f);
        Slider slider = CreateSlider(name, row.transform);
        slider.minValue = PlanetData.MinChunkDimension;
        slider.maxValue = PlanetData.MaxChunkDimension;
        slider.wholeNumbers = true;
        slider.value = NewWorldUserSettings.DefaultChunkDimension;
        TextMeshProUGUI value = CreateText(name + "数值", row.transform,
            NewWorldUserSettings.DefaultChunkDimension.ToString(), 17f, Amber);
        value.alignment = TextAlignmentOptions.MidlineRight;
        value.gameObject.AddComponent<LayoutElement>().preferredWidth = 76f;
    }

    private static void EnsureGameSettingsPage(GameObject root)
    {
        ScrollRect scroll = FindTransform(root.transform, "Scroll View")?.GetComponent<ScrollRect>();
        Transform tabBar = FindTransform(root.transform, SettingsActionListPagination.TabBarName);
        if (scroll == null || scroll.content == null || tabBar == null)
            throw new MissingReferenceException("设置主面板缺少滚动内容或分页栏。");

        EnsureEmbeddedActionListPage(scroll.content, SettingsActionListPagination.GamePageName,
            SettingsPanelsRoot + RuntimeUIPrefabKeys.GameSettings + ".prefab");
        Button tab = EnsureActionListTabButton(root.transform, tabBar,
            SettingsActionListPagination.GameTabButtonName, "游戏设置");
        tab.transform.SetSiblingIndex(0);
    }

    #endregion
}
