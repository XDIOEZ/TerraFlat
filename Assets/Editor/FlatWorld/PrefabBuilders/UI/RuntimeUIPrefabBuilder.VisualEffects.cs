using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>视觉特效设置的正式 Prefab 构建入口，只更新本页及设置主面板的页签。</summary>
public static partial class RuntimeUIPrefabBuilder
{
    #region 视觉特效分页构建

    /// <summary>定向固化视觉特效页面、序列化按钮引用和主设置入口。</summary>
    [MenuItem("FlatWorld/UI/Rebuild Visual Effects Settings UI")]
    public static void RebuildVisualEffectsSettingsUI()
    {
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
            throw new MissingReferenceException($"缺少设置字体：{FontPath}");

        SaveVisualEffectsSettingsPrefab();
        UpdateExistingPrefab(MainMenuCoreRoot + "UI_ActionList.prefab", root =>
        {
            ScrollRect scroll = FindTransform(root.transform, "Scroll View").GetComponent<ScrollRect>();
            Transform page = EnsureEmbeddedActionListPage(scroll.content,
                SettingsActionListPagination.VisualEffectsPageName,
                SettingsPanelsRoot + RuntimeUIPrefabKeys.VisualEffectsSettings + ".prefab");
            page.SetSiblingIndex(6);
            EnsureActionListTabBar(root.transform);
        });
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Runtime UI] 已固化视觉特效分页及投影、透视、水体选项。");
    }

    /// <summary>保存设置页，并按正式运行时 Prefab 契约登记 Addressables。</summary>
    private static void SaveVisualEffectsSettingsPrefab()
    {
        string path = SettingsPanelsRoot + RuntimeUIPrefabKeys.VisualEffectsSettings + ".prefab";
        SaveNewPrefab(path, BuildVisualEffectsSettings);
        EnsureRuntimePrefabAddressable(path);
    }

    /// <summary>用两个 64 像素互斥按钮展示水体风格，说明只描述选择后的视觉差异。</summary>
    private static GameObject BuildVisualEffectsSettings()
    {
        GameObject root = CreateSettingsPageRoot(RuntimeUIPrefabKeys.VisualEffectsSettings,
            out Transform content);
        CreateSettingsHeader(content, "视觉特效");
        CreateSettingsHint(content, "调整会立即应用并自动保存。", 48f);

        GameObject occlusionRow = CreateRow("物品透视行", content, 72f);
        TextMeshProUGUI occlusionLabel = CreateText("物品透视标签", occlusionRow.transform, "物品透视", 22f, Cream);
        occlusionLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        Toggle occlusion = CreateToggle("物品透视开关", occlusionRow.transform);
        LayoutElement occlusionLayout = occlusion.GetComponent<LayoutElement>();
        occlusionLayout.minHeight = 60f;
        occlusionLayout.preferredHeight = 64f;
        occlusionLayout.minWidth = 88f;
        occlusion.isOn = false;
        CreateSettingsHint(content, "开启后，遮挡玩家的树木会局部透明。", 52f);

        GameObject sunRow = CreateRow("太阳长投影行", content, 72f);
        TextMeshProUGUI sunLabel = CreateText("太阳长投影标签", sunRow.transform, "太阳长投影", 22f, Cream);
        sunLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        Toggle sunShadows = CreateToggle("太阳长投影开关", sunRow.transform);
        LayoutElement sunLayout = sunShadows.GetComponent<LayoutElement>();
        sunLayout.minHeight = 60f;
        sunLayout.preferredHeight = 64f;
        sunLayout.minWidth = 88f;
        sunShadows.isOn = SunShadowSettings.DefaultEnabled;
        CreateSettingsHint(content, "随太阳方向投射长阴影。低配设备可关闭以减少渲染开销。", 60f);

        GameObject blurRow = CreateRow("阴影柔化行", content, 72f);
        TextMeshProUGUI blurLabel = CreateText("阴影柔化标签", blurRow.transform, "阴影柔化", 22f, Cream);
        blurLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        Toggle blurShadows = CreateToggle("阴影柔化开关", blurRow.transform);
        LayoutElement blurLayout = blurShadows.GetComponent<LayoutElement>();
        blurLayout.minHeight = 60f;
        blurLayout.preferredHeight = 64f;
        blurLayout.minWidth = 88f;
        blurShadows.isOn = SunShadowSettings.DefaultBlurEnabled;

        GameObject blurStrengthRow = CreateRow("模糊程度行", content, 72f);
        TextMeshProUGUI blurStrengthLabel = CreateText("模糊程度标签", blurStrengthRow.transform,
            "模糊程度", 22f, Cream);
        blurStrengthLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = 150f;
        Slider blurStrength = CreateSlider("模糊程度滑块", blurStrengthRow.transform);
        blurStrength.minValue = SunShadowSettings.MinBlurStrength;
        blurStrength.maxValue = SunShadowSettings.MaxBlurStrength;
        blurStrength.value = SunShadowSettings.DefaultBlurStrength;
        TextMeshProUGUI blurStrengthValue = CreateText("模糊程度数值", blurStrengthRow.transform,
            "30%", 18f, Amber);
        blurStrengthValue.alignment = TextAlignmentOptions.MidlineRight;
        blurStrengthValue.gameObject.AddComponent<LayoutElement>().preferredWidth = 65f;

        GameObject elevationRow = CreateRow("地面层级阴影行", content, 72f);
        TextMeshProUGUI elevationLabel = CreateText("地面层级阴影标签", elevationRow.transform,
            "地面层级阴影", 22f, Cream);
        elevationLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        Toggle elevationShadows = CreateToggle("地面层级阴影开关", elevationRow.transform);
        LayoutElement elevationLayout = elevationShadows.GetComponent<LayoutElement>();
        elevationLayout.minHeight = 60f;
        elevationLayout.preferredHeight = 64f;
        elevationLayout.minWidth = 88f;
        elevationShadows.isOn = GroundElevationShadowSettings.DefaultEnabled;

        GameObject elevationWidthRow = CreateRow("阴影宽度行", content, 72f);
        TextMeshProUGUI elevationWidthLabel = CreateText("阴影宽度标签", elevationWidthRow.transform,
            "阴影宽度", 22f, Cream);
        elevationWidthLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = 150f;
        Slider elevationWidth = CreateSlider("阴影宽度滑块", elevationWidthRow.transform);
        elevationWidth.minValue = GroundElevationShadowSettings.MinWidth;
        elevationWidth.maxValue = GroundElevationShadowSettings.MaxWidth;
        elevationWidth.value = GroundElevationShadowSettings.DefaultWidth;
        TextMeshProUGUI elevationWidthValue = CreateText("阴影宽度数值", elevationWidthRow.transform,
            "20%", 18f, Amber);
        elevationWidthValue.alignment = TextAlignmentOptions.MidlineRight;
        elevationWidthValue.gameObject.AddComponent<LayoutElement>().preferredWidth = 65f;

        TextMeshProUGUI label = CreateText("水体风格标签", content, "水体风格", 22f, Cream);
        label.gameObject.AddComponent<LayoutElement>().preferredHeight = 36f;
        GameObject row = CreateRow("水体风格行", content, 72f);
        Button stylized = CreateSettingsButton("风格化水体按钮", row.transform, "风格化", 260f, 64f, false);
        Button realistic = CreateSettingsButton("写实水体按钮", row.transform, "写实", 260f, 64f, true);
        SetButtonLabelSize(stylized, 20f);
        SetButtonLabelSize(realistic, 20f);
        CreateSettingsHint(content, "风格化：鲜明水色、清晰浪纹与泡沫。", 52f);
        CreateSettingsHint(content, "写实：柔和水色、连续波浪与细碎反光。", 52f);

        Transform footer = CreateRow("底部操作", content, 72f).transform;
        Button reset = CreateSettingsButton("恢复默认按钮", footer, "恢复默认", 200f, 64f, false);
        SetButtonLabelSize(reset, 18f);
        var controller = root.AddComponent<VisualEffectsSettingsPanelLauncher>();
        var serialized = new SerializedObject(controller);
        serialized.FindProperty("stylizedButton").objectReferenceValue = stylized;
        serialized.FindProperty("realisticButton").objectReferenceValue = realistic;
        serialized.FindProperty("resetButton").objectReferenceValue = reset;
        serialized.FindProperty("occlusionToggle").objectReferenceValue = occlusion;
        serialized.FindProperty("sunShadowToggle").objectReferenceValue = sunShadows;
        serialized.FindProperty("sunShadowBlurToggle").objectReferenceValue = blurShadows;
        serialized.FindProperty("sunShadowBlurSlider").objectReferenceValue = blurStrength;
        serialized.FindProperty("sunShadowBlurValueText").objectReferenceValue = blurStrengthValue;
        serialized.FindProperty("groundElevationToggle").objectReferenceValue = elevationShadows;
        serialized.FindProperty("groundElevationWidthSlider").objectReferenceValue = elevationWidth;
        serialized.FindProperty("groundElevationWidthValueText").objectReferenceValue = elevationWidthValue;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return root;
    }

    #endregion
}
