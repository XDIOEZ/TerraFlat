using System;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;
using UnityEngine.UI;

/// <summary>生成通用可阅读书籍的正式模块 Prefab、双页 UI Prefab 和矿工笔记书页本地化内容。</summary>
public static class ReadableBookAssetBuilder
{
    private const string PanelPath = "Assets/2_Prefabs/2-1_UI/Gameplay/Screens/UI_ReadableBook.prefab";
    private const string ModulePath = "Assets/2_Prefabs/Gameplay/Modules/Items/Module_ReadableBook.prefab";
    private const string FontPath = "Assets/Plugins/TextMesh Pro/Fonts/fusion-pixel-12px-monospaced-zh_hans.asset";

    [MenuItem("FlatWorld/内容配置/生成可阅读书籍资源")]
    public static void Build()
    {
        BuildPanelPrefab();
        BuildModulePrefab();
        Register(PanelPath, RuntimeUIPrefabKeys.ReadableBook, "Prefab");
        Register(ModulePath, "Module_ReadableBook", "Prefab");
        SyncMinerNotePages();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[ReadableBook] 双页书籍 UI、阅读模块和矿工笔记书页文本已生成。");
    }

    private static void BuildPanelPrefab()
    {
        RectTransform root = StretchRect(RuntimeUIPrefabKeys.ReadableBook, null);
        root.gameObject.SetActive(false);

        Image overlay = root.gameObject.AddComponent<Image>();
        overlay.color = new Color32(12, 12, 12, 178);
        overlay.raycastTarget = true;

        BasePanel basePanel = root.gameObject.AddComponent<BasePanel>();
        basePanel.PanelName = RuntimeUIPrefabKeys.ReadableBook;
        basePanel.rectTransform = root;
        basePanel.canvasGroup = root.GetComponent<CanvasGroup>();
        basePanel.canvasGroup.alpha = 0f;
        basePanel.canvasGroup.interactable = false;
        basePanel.canvasGroup.blocksRaycasts = false;

        RectTransform frame = Rect("书籍框", root, new Vector2(1080f, 650f), Vector2.zero);
        Image frameImage = frame.gameObject.AddComponent<Image>();
        frameImage.color = new Color32(39, 39, 39, 255);
        frameImage.raycastTarget = false;

        TextMeshProUGUI title = Text("书籍标题", frame, "书籍", new Vector2(900f, 48f), new Vector2(0f, 286f), 28f,
            new Color32(238, 238, 238, 255), TextAlignmentOptions.Center, false);

        RectTransform leftPage = CreatePage("左页", frame, new Vector2(-252.5f, 5f));
        RectTransform rightPage = CreatePage("右页", frame, new Vector2(252.5f, 5f));

        RectTransform spine = Rect("书脊", frame, new Vector2(16f, 500f), new Vector2(0f, 5f));
        Image spineImage = spine.gameObject.AddComponent<Image>();
        spineImage.color = new Color32(88, 84, 76, 255);
        spineImage.raycastTarget = false;

        TextMeshProUGUI leftBody = PageBody("左页正文", leftPage);
        TextMeshProUGUI rightBody = PageBody("右页正文", rightPage);
        TextMeshProUGUI leftNumber = Text("左页页码", leftPage, "1", new Vector2(80f, 32f), new Vector2(0f, -224f), 18f,
            new Color32(55, 54, 49, 255), TextAlignmentOptions.Center, false);
        TextMeshProUGUI rightNumber = Text("右页页码", rightPage, "2", new Vector2(80f, 32f), new Vector2(0f, -224f), 18f,
            new Color32(55, 54, 49, 255), TextAlignmentOptions.Center, false);

        Button previous = Button("上一页", frame, "‹", new Vector2(76f, 60f), new Vector2(-78f, -286f));
        Button next = Button("下一页", frame, "›", new Vector2(76f, 60f), new Vector2(78f, -286f));
        Button("关闭", frame, "×", new Vector2(72f, 60f), new Vector2(494f, 286f));

        ReadableBookPanel view = root.gameObject.AddComponent<ReadableBookPanel>();
        SerializedObject serialized = new SerializedObject(view);
        serialized.FindProperty("titleText").objectReferenceValue = title;
        serialized.FindProperty("leftPageText").objectReferenceValue = leftBody;
        serialized.FindProperty("rightPageText").objectReferenceValue = rightBody;
        serialized.FindProperty("leftPageNumber").objectReferenceValue = leftNumber;
        serialized.FindProperty("rightPageNumber").objectReferenceValue = rightNumber;
        serialized.FindProperty("previousButton").objectReferenceValue = previous;
        serialized.FindProperty("nextButton").objectReferenceValue = next;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        root.gameObject.SetActive(true);
        try
        {
            PrefabUtility.SaveAsPrefabAsset(root.gameObject, PanelPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root.gameObject);
        }
    }

    private static void BuildModulePrefab()
    {
        GameObject root = new GameObject("Module_ReadableBook");
        try
        {
            Mod_ReadableBook module = root.AddComponent<Mod_ReadableBook>();
            module.ModData.ID = Mod_ReadableBook.ModuleId;
            module.ModData.Name = Mod_ReadableBook.ModuleId;
            PrefabUtility.SaveAsPrefabAsset(root, ModulePath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static RectTransform CreatePage(string name, Transform parent, Vector2 position)
    {
        RectTransform page = Rect(name, parent, new Vector2(485f, 500f), position);
        Image image = page.gameObject.AddComponent<Image>();
        image.color = new Color32(210, 203, 181, 255);
        image.raycastTarget = false;

        Outline outline = page.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color32(92, 88, 78, 255);
        outline.effectDistance = new Vector2(2f, -2f);
        outline.useGraphicAlpha = false;
        return page;
    }

    private static TextMeshProUGUI PageBody(string name, Transform parent)
    {
        TextMeshProUGUI body = Text(name, parent, string.Empty, new Vector2(421f, 410f), new Vector2(0f, 18f), 24f,
            new Color32(38, 37, 34, 255), TextAlignmentOptions.TopLeft, false);
        body.enableWordWrapping = true;
        body.enableAutoSizing = true;
        body.fontSizeMin = 18f;
        body.fontSizeMax = 24f;
        body.overflowMode = TextOverflowModes.Ellipsis;
        body.margin = new Vector4(8f, 10f, 8f, 8f);
        return body;
    }

    private static Button Button(string name, Transform parent, string label, Vector2 size, Vector2 position)
    {
        RectTransform rect = Rect(name, parent, size, position);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = new Color32(84, 84, 84, 255);
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        Text(name + "文字", rect, label, size, Vector2.zero, 30f,
            new Color32(238, 238, 238, 255), TextAlignmentOptions.Center, false);
        return button;
    }

    private static TextMeshProUGUI Text(
        string name,
        Transform parent,
        string value,
        Vector2 size,
        Vector2 position,
        float fontSize,
        Color32 color,
        TextAlignmentOptions alignment,
        bool raycastTarget)
    {
        TextMeshProUGUI text = Rect(name, parent, size, position).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (text.font == null)
            throw new InvalidOperationException($"可阅读书籍 UI 缺少字体资源：{FontPath}");
        text.text = value;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = raycastTarget;
        return text;
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 position)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        rect.localScale = Vector3.one;
        return rect;
    }

    private static RectTransform StretchRect(string name, Transform parent)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        if (parent != null)
            rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
        return rect;
    }

    private static void Register(string path, string address, string label)
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
            throw new InvalidOperationException("项目缺少 AddressableAssetSettings，无法注册可阅读书籍资源。");

        string guid = AssetDatabase.AssetPathToGUID(path);
        if (string.IsNullOrWhiteSpace(guid))
            throw new InvalidOperationException($"可阅读书籍资源尚未导入：{path}");

        var entry = settings.CreateOrMoveEntry(guid, settings.DefaultGroup);
        entry.address = address;
        entry.SetLabel(label, true, true);
        EditorUtility.SetDirty(settings);
    }

    private static void SyncMinerNotePages()
    {
        SyncText("item.MinersNote.page.1",
            "铁矿\n\n先用石镐采铜。\n有了铜镐以后，再去开采铁矿。",
            "Iron Ore\n\nMine copper first with a stone pickaxe. Once you have a copper pickaxe, use it to mine iron ore.");
        SyncText("item.MinersNote.page.2",
            "粗铁铁镐\n\n粗铁加木柄与绳子，可制作粗铁铁镐。",
            "Crude Iron Pickaxe\n\nCombine crude iron with a wooden handle and rope to make a crude iron pickaxe.");
        SyncText("item.MinersNote.page.3",
            "青铜\n\n铜和锡按 9 : 1 冶炼，可以得到青铜。",
            "Bronze\n\nSmelt copper and tin at a 9:1 ratio to make bronze.");
        SyncText("item.MinersNote.page.4",
            "下洞前\n\n备足陶罐饮水和食物，再去洞穴寻找更深的矿脉。",
            "Before Going Deeper\n\nBring enough drinking water in a clay vessel and enough food before searching deeper cave veins.");
    }

    private static void SyncText(string key, string chinese, string english)
    {
        StringTableCollection collection = LocalizationEditorSettings.GetStringTableCollection("FlatWorld");
        if (collection == null)
            throw new InvalidOperationException("找不到 FlatWorld String Table Collection。");

        foreach (string code in new[] { "zh-CN", "en" })
        {
            StringTable table = (StringTable)collection.GetTable(new LocaleIdentifier(code));
            if (table == null)
                throw new InvalidOperationException($"FlatWorld 缺少 {code} String Table。");
            table.AddEntry(key, code == "en" ? english : chinese);
            EditorUtility.SetDirty(table);
        }
        EditorUtility.SetDirty(collection.SharedData);
    }
}
