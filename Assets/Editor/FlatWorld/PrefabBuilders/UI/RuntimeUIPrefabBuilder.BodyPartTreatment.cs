using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static partial class RuntimeUIPrefabBuilder
{
    #region 医疗专属面板

    [MenuItem("FlatWorld/UI/Rebuild Body Part Treatment UI")]
    public static void RebuildBodyPartTreatmentUI()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new System.InvalidOperationException("请先退出 Play 再装配医疗面板。");
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        string path = "Assets/2_Prefabs/2-1_UI/Gameplay/Player/" + BodyPartTreatmentPanel.PrefabKey + ".prefab";
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
        SaveNewPrefab(path, BuildBodyPartTreatment);
        EnsureRuntimePrefabAddressable(path);
        AssetDatabase.SaveAssets();
        FlatWorld.Localization.Editor.FlatWorldLocalizationSetup.SyncRuntimeUiTexts(
            "选择治疗部位", "选择受伤部位；关闭窗口或切换用品将取消引导，不消耗物品。",
            "正在固定{0}　{1:0.0} / {2:0.0} 秒", "治疗未完成，物品未消耗。请重新选择部位。",
            "请将医疗用品拿在手上", "该用品不能治疗此部位", "该部位耐久已满", "该部位正在恢复",
            "头部", "胸部", "腹部", "骨盆", "左手", "右手", "左腿", "右腿");
    }

    private static GameObject BuildBodyPartTreatment()
    {
        GameObject root = CreateModalPanelRoot(BodyPartTreatmentPanel.PrefabKey, new Vector2(700f, 740f));
        root.SetActive(false);
        Transform content = root.transform.Find("设置对话框");
        TextMeshProUGUI title = CreateText("治疗标题", content, "选择治疗部位", 28f, Amber);
        title.gameObject.AddComponent<LayoutElement>().preferredHeight = 42f;
        GameObject scroll = CreateUIObject("部位列表", content, typeof(Image), typeof(ScrollRect));
        scroll.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
        scroll.GetComponent<Image>().raycastTarget = true;
        scroll.AddComponent<LayoutElement>().flexibleHeight = 1f;
        GameObject viewport = CreateUIObject("视口", scroll.transform, typeof(RectMask2D));
        Stretch((RectTransform)viewport.transform);
        GameObject entries = CreateUIObject("部位容器", viewport.transform, typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        RectTransform entriesRect = (RectTransform)entries.transform;
        entriesRect.anchorMin = new Vector2(0f, 1f);
        entriesRect.anchorMax = Vector2.one;
        entriesRect.pivot = new Vector2(0.5f, 1f);
        entriesRect.sizeDelta = Vector2.zero;
        VerticalLayoutGroup layout = entries.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandHeight = false;
        entries.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        ScrollRect scrollRect = scroll.GetComponent<ScrollRect>();
        scrollRect.viewport = (RectTransform)viewport.transform;
        scrollRect.content = entriesRect;
        scrollRect.horizontal = false;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        Button template = CreateButton("部位按钮模板", entries.transform, "部位", 640f, 64f, false);
        template.gameObject.SetActive(false);
        TextMeshProUGUI status = CreateText("治疗状态", content, "选择治疗部位", 22f, Cream);
        status.gameObject.AddComponent<LayoutElement>().preferredHeight = 72f;
        Slider progress = CreateSlider("引导进度", content);
        progress.GetComponent<LayoutElement>().preferredHeight = 28f;
        progress.minValue = 0f;
        progress.maxValue = 1f;
        progress.interactable = false;
        Button cancel = CreateButton("取消治疗", content, "关闭", 640f, 64f, false);
        BodyPartTreatmentPanel panel = root.AddComponent<BodyPartTreatmentPanel>();
        panel.PartRoot = entriesRect;
        panel.PartTemplate = template;
        panel.CancelButton = cancel;
        panel.Title = title;
        panel.Status = status;
        panel.Progress = progress;
        root.AddComponent<SafeAreaScaleGroup>().Configure((RectTransform)content,
            new[] { (RectTransform)content }, new Vector2(24f, 24f));
        root.SetActive(true);
        return root;
    }

    #endregion
}
