using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static partial class RuntimeUIPrefabBuilder
{
    /// <summary>只构建季节设置页、其世界入口与日历 HUD，保留其他设置页及现有节点。</summary>
    [MenuItem("FlatWorld/UI/Rebuild Season Settings UI")]
    public static void RebuildSeasonSettingsUI()
    {
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        string path = SettingsPanelsRoot + RuntimeUIPrefabKeys.SeasonSettings + ".prefab";
        SaveNewPrefab(path, BuildSeasonSettings);
        EnsureRuntimePrefabAddressable(path);
        UpdateExistingPrefab(MainMenuCoreRoot + "UI_ActionList.prefab", EnsureSeasonSettingsPage);
        SavePlayerWorldCoordinatePrefab();
        AssetDatabase.SaveAssets();
    }

    /// <summary>用正式控件建立四季天数草稿页，各输入与按钮满足移动端触控高度。</summary>
    private static GameObject BuildSeasonSettings()
    {
        GameObject root = CreateSettingsPageRoot(RuntimeUIPrefabKeys.SeasonSettings, out Transform content);
        CreateSettingsHeader(content, "季节长度");
        CreateSettingsHint(content, "默认每季 6 天，一年 24 天。可分别调整四季，当前季节进度会保留。", 58f);
        string[] seasons = { "春季", "夏季", "秋季", "冬季" };
        for (int index = 0; index < seasons.Length; index++)
        {
            GameObject row = CreateRow("季节行_" + index, content, 74f);
            CreateRowLabel(row.transform, seasons[index], 130f);
            TMP_InputField field = CreateInputField("季节天数_" + index, row.transform, "游戏天数");
            field.contentType = TMP_InputField.ContentType.DecimalNumber;
            field.text = "6";
            LayoutElement layout = field.gameObject.AddComponent<LayoutElement>();
            layout.preferredWidth = 380f;
            layout.preferredHeight = 64f;
        }
        TextMeshProUGUI status = CreateText("状态文本", content, "一年共 24 天", 17f, Teal);
        status.gameObject.AddComponent<LayoutElement>().preferredHeight = 54f;
        Transform footer = CreateFooter(content);
        SetButtonLabelSize(CreateSettingsButton("取消按钮", footer, "取消", 132f, 60f, false), 18f);
        SetButtonLabelSize(CreateSettingsButton("应用按钮", footer, "应用", 144f, 60f, true), 18f);
        root.AddComponent<SeasonSettingsPanel>();
        return root;
    }

    /// <summary>在现有世界设置页加入季节入口，并绑定独立的正式嵌套 Prefab。</summary>
    private static void EnsureSeasonSettingsPage(GameObject root)
    {
        ScrollRect scroll = FindTransform(root.transform, "Scroll View")?.GetComponent<ScrollRect>();
        if (scroll == null || scroll.content == null)
            throw new MissingReferenceException("世界设置缺少滚动内容容器。");
        Transform worldPage = EnsureActionListPage(scroll.content, SettingsActionListPagination.WorldPageName);
        if (FindTransform(worldPage, "季节长度") == null)
            CreateSettingsButton("季节长度", worldPage, "季节长度", 532f, 64f, false);
        string path = SettingsPanelsRoot + RuntimeUIPrefabKeys.SeasonSettings + ".prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
        {
            SaveNewPrefab(path, BuildSeasonSettings);
            EnsureRuntimePrefabAddressable(path);
        }
        Transform page = EnsureEmbeddedActionListPage(scroll.content, SeasonSettingsPanel.PageName, path);
        page.SetSiblingIndex(11);
        page.gameObject.SetActive(false);
    }
}
