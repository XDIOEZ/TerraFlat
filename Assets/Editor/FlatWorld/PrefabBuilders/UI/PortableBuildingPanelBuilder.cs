using System;
using System.IO;
using System.Linq;
using FlatWorld.Localization;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;
using UnityEngine.UI;

/// <summary>
/// 为可携带设施的正式面板装配共用建筑操作，原位保留既有布局、槽位和美术。
/// 只在编辑器中克隆原面板按钮并固化引用，运行时不创建控件，也不重建物品资源。
/// </summary>
public static class PortableBuildingPanelBuilder
{
    private const string VesselPath = "Assets/2_Prefabs/2-1_UI/Gameplay/Containers/UI_WaterVessel.prefab";
    private const string MortarPath = "Assets/2_Prefabs/2-1_UI/Gameplay/Crafting/UI_StoneMortar.prefab";

    [MenuItem("FlatWorld/UI/Configure Portable Building Actions")]
    public static void ConfigureAssets()
    {
        ConfigureAsset(VesselPath, ConfigureVessel);
        ConfigureAsset(MortarPath, ConfigureMortar);
        SyncBuildingNames();
        SyncText("放到地上", "Place on Ground");
        SyncText("拆回物品", "Pack Up");
        SyncText("脏水（可直接喝）", "Dirty Water (Drinkable)");
        SyncDescription("ancient_stage.json", "ClayJar", "Holds 8 servings of water. Fresh water is stored as dirty water and can be drunk immediately or boiled into clean drinking water. Choose Place on Ground in its panel, then interact or pack up without losing water quality, amount or heating progress. Sea water can be heated to make salt.");
        SyncDescription("mortar.json", "StoneMortar", "Each strike turns one rice grain into one rice. Choose Place on Ground in the held mortar's panel, then interact with it or pack it up. All ingredients and products are retained.");
        SyncDescription("portable_buildings.json", "ClayJar_Building", "A placed clay jar. Interact to drink dirty or boiled water, empty it, pour from a held jar or pack it up. Water amount, quality and heating progress stay with the container.");
        SyncDescription("portable_buildings.json", "StoneMortar_Building", "A placed stone mortar. Interact to process one recipe per strike. All ingredients and products are retained when packing up or placing it again.");
        AssetDatabase.SaveAssets();
        Debug.Log("[PortableBuilding] 陶罐、石臼正式面板的放置/拆回按钮与引用已保存。");
    }

    /// <summary>沿用陶罐底部按钮行，每次只显示与当前物品角色对应的一个操作。</summary>
    public static void ConfigureVessel(GameObject root)
    {
        Button template = FindButton(root, "关闭按钮");
        Configure(root, template, template.transform.parent, new Vector2(158f, 64f), null);
        LayoutElement footer = template.transform.parent.GetComponent<LayoutElement>();
        footer.minHeight = footer.preferredHeight = 64f;
    }

    /// <summary>使用石臼左下角空位，不覆盖碗内投料区或分页控件。</summary>
    public static void ConfigureMortar(GameObject root)
        => Configure(root, FindButton(root, "关闭"), root.transform,
            new Vector2(160f, 60f), new Vector2(-225f, -305f));

    private static void ConfigureAsset(string path, Action<GameObject> configure)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            configure(root);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    /// <summary>暂时停用根节点后完成必需引用，重复运行不会增加重复按钮。</summary>
    private static void Configure(GameObject root, Button template, Transform parent, Vector2 size, Vector2? position)
    {
        bool active = root.activeSelf;
        root.SetActive(false);
        try
        {
            Button place = CreateButton(root, template, parent, "放置建筑按钮", "放到地上", size, position);
            Button dismantle = CreateButton(root, template, parent, "拆回建筑按钮", "拆回物品", size, position);
            BuildingPanelActions actions = root.GetComponent<BuildingPanelActions>() ?? root.AddComponent<BuildingPanelActions>();
            actions.PlaceButton = place;
            actions.DismantleButton = dismantle;
        }
        finally { root.SetActive(active); }
    }

    private static Button CreateButton(GameObject root, Button template, Transform parent,
        string name, string caption, Vector2 size, Vector2? position)
    {
        Button existing = root.GetComponentsInChildren<Button>(true).FirstOrDefault(button => button.name == name);
        Button button = existing != null ? existing : UnityEngine.Object.Instantiate(template, parent);
        button.name = name;
        button.onClick = new Button.ButtonClickedEvent();
        button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
        RectTransform rect = (RectTransform)button.transform;
        rect.sizeDelta = size;
        if (position.HasValue)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = position.Value;
        }
        LayoutElement layout = button.GetComponent<LayoutElement>();
        if (layout != null)
        {
            layout.minWidth = layout.preferredWidth = size.x;
            layout.minHeight = layout.preferredHeight = size.y;
        }
        // 克隆的关闭文本不能保留旧本地化键，正式面板的自动本地化将按新文字绑定。
        foreach (LocalizedTextBinder binder in button.GetComponentsInChildren<LocalizedTextBinder>(true))
            UnityEngine.Object.DestroyImmediate(binder);
        TMP_Text text = button.GetComponentInChildren<TMP_Text>(true);
        text.text = caption;
        text.fontSize = 22f;
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = new Vector2(6f, 0f);
        text.rectTransform.offsetMax = new Vector2(-6f, 0f);
        text.alignment = TextAlignmentOptions.Center;
        FlatWorldUITheme.Apply(button.transform);
        button.gameObject.SetActive(false);
        return button;
    }

    private static Button FindButton(GameObject root, string name)
        => root.GetComponentsInChildren<Button>(true).FirstOrDefault(button => button.name == name)
            ?? throw new InvalidOperationException($"面板 {root.name} 缺少按钮模板 {name}。");

    /// <summary>从正式目录与英文名真源定向生成建筑名称，避免面板装配依赖无关物品的翻译完成度。</summary>
    private static void SyncBuildingNames()
    {
        var definitions = ItemDefinitionCatalogLoader.LoadBuiltInDefinitions();
        JObject names = (JObject)JObject.Parse(File.ReadAllText("Assets/Localization/ItemNames.en.json"))["names"];
        foreach (string id in new[] { "ClayJar_Building", "StoneMortar_Building" })
        {
            ItemDefinitionDto definition = definitions.Single(entry => entry.Id == id && !entry.Abstract);
            string english = (string)names[id];
            if (string.IsNullOrWhiteSpace(definition.GameName) || string.IsNullOrWhiteSpace(english))
                throw new InvalidDataException($"建筑 {id} 缺少中文或英文名称。");
            string key = string.IsNullOrWhiteSpace(definition.LabelKey)
                ? FlatWorldLocalizationService.GetItemLabelKey(id) : definition.LabelKey;
            SyncEntry("FlatWorld", key, definition.GameName, english);
        }
    }

    /// <summary>只同步两个新增静态 UI 文案，不重建其它内容或翻译条目。</summary>
    private static void SyncText(string chinese, string english)
        => SyncEntry("FlatWorldUI", FlatWorldLocalizationService.GetUiTextKey(chinese), chinese, english);

    /// <summary>中文说明直接读取当前 JSON，避免工具中的过期文案覆盖内容真源。</summary>
    private static void SyncDescription(string fileName, string id, string english)
    {
        JObject item = JObject.Parse(File.ReadAllText("Assets/StreamingAssets/GameConfig/Items/shells/" + fileName))
            ["items"].Children<JObject>().Single(entry => (string)entry["id"] == id);
        SyncEntry("FlatWorld", FlatWorldLocalizationService.GetItemDescriptionKey(id), (string)item["description"], english);
    }

    private static void SyncEntry(string collectionName, string key, string chinese, string english)
    {
        var collection = LocalizationEditorSettings.GetStringTableCollection(collectionName);
        foreach (string code in new[] { "zh-CN", "en" })
        {
            var table = (StringTable)collection.GetTable(new LocaleIdentifier(code));
            table.AddEntry(key, code == "en" ? english : chinese);
            EditorUtility.SetDirty(table);
        }
        EditorUtility.SetDirty(collection.SharedData);
    }
}
