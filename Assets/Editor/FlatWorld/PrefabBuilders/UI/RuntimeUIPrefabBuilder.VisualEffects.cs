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
        Debug.Log("[Runtime UI] 已固化视觉特效分页及水体风格选项。");
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
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return root;
    }

    #endregion
}
