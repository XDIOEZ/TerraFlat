using System;
using System.IO;
using System.Linq;
using FlatWorld.Localization;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;
using UnityEngine.UI;

/// <summary>定向生成机械模块与独立加工面板，并登记 Addressables 和双语文本；不修改场景或已有面板。</summary>
public static class MechanicalContentBuilder
{
    #region 资源构建
    private const string ModuleFolder = "Assets/2_Prefabs/Gameplay/Modules/Mechanical";
    private const string UiFolder = "Assets/2_Prefabs/2-1_UI/Gameplay/Crafting";
    [MenuItem("FlatWorld/机械动力/构建首版资源")]
    public static void Build()
    {
        Directory.CreateDirectory(ModuleFolder);
        CreateModule<Mod_HandDrill>("Module_HandDrill");
        CreateModule<Mod_MechanicalNode>("Module_MechanicalNode");
        CreatePanel("UI_HandDrill", "手钻", "钻孔");
        CreatePanel("UI_Mechanical", "机械动力", "摇动");
        SyncNames();
        foreach (var pair in Texts) SyncText(pair[0], pair[1]);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Mechanical] 模块、独立面板、Addressables 与中英文条目装配完成。");
    }

    private static void CreateModule<T>(string name) where T : Module
    {
        string path = ModuleFolder + "/" + name + ".prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
        {
            var root = new GameObject(name);
            root.AddComponent<T>();
            try { PrefabUtility.SaveAsPrefabAsset(root, path); }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        Register(path, name);
    }

    private static void CreatePanel(string name, string title, string caption)
    {
        string path = UiFolder + "/" + name + ".prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null &&
            !AssetDatabase.CopyAsset(UiFolder + "/UI_FireDrill.prefab", path))
            throw new InvalidOperationException("无法创建独立机械面板：" + path);
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            root.SetActive(false); root.name = name;
            var view = root.GetComponent<MechanicalPanelView>() ?? root.AddComponent<MechanicalPanelView>();
            view.Title = Find<TMP_Text>(root, "FWUI_标题");
            view.Status = Find<TMP_Text>(root, "FWUI_FooterHint");
            view.ActionButton = Find<Button>(root, "合成按钮");
            view.CloseButton = Find<Button>(root, "关闭");
            view.InputSlot = Find<ItemSlot_UI>(root, "输入_1");
            view.OutputSlot = Find<ItemSlot_UI>(root, "输出_1");
            string[] processingNames = { "输入", "输出", "FWUI_FlowArrow_394", "FWUI_Section_INPUT", "FWUI_Section_OUTPUT",
                "FWUI_SectionTitle_INPUT", "FWUI_SectionTitle_OUTPUT", "FWUI_SectionRule_INPUT", "FWUI_SectionRule_OUTPUT",
                "FWUI_SectionMarker_INPUT", "FWUI_SectionMarker_OUTPUT" };
            view.ProcessingVisuals = processingNames.Select(value => Find<Transform>(root, value).gameObject).ToArray();
            var oldFireProgress = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(value => value.name == "Progress");
            if (oldFireProgress != null) UnityEngine.Object.DestroyImmediate(oldFireProgress.gameObject);
            foreach (var text in new[] { view.Title, view.Status, view.ActionButton.GetComponentInChildren<TMP_Text>(true) })
            {
                var binder = text.GetComponent<LocalizedTextBinder>();
                if (binder != null) UnityEngine.Object.DestroyImmediate(binder);
            }
            view.Title.text = title; view.Status.text = ""; view.Status.fontSize = 16;
            view.Status.enableAutoSizing = true; view.Status.fontSizeMin = 10; view.Status.fontSizeMax = 16;
            view.ActionButton.GetComponentInChildren<TMP_Text>(true).text = caption;
            var actionRect = (RectTransform)view.ActionButton.transform;
            actionRect.sizeDelta = new Vector2(actionRect.sizeDelta.x, 60);
            // 清理模板中不属于钻孔/机械操作的装饰说明。
            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
                if (text.name == "FWUI_眉题" || text.name.StartsWith("FWUI_SectionEyebrow", StringComparison.Ordinal))
                    text.gameObject.SetActive(false);
            root.SetActive(true);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        Register(path, name);
    }

    private static T Find<T>(GameObject root, string name) where T : Component
        => root.GetComponentsInChildren<T>(true).FirstOrDefault(value => value.name == name)
           ?? throw new InvalidOperationException(root.name + " 缺少控件 " + name);

    private static void Register(string path, string address)
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        var entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(path), settings.DefaultGroup);
        entry.address = address; entry.SetLabel("Prefab", true, true);
        EditorUtility.SetDirty(entry.parentGroup); EditorUtility.SetDirty(settings);
    }

    [MenuItem("FlatWorld/机械动力/校验首版资源")]
    public static void ValidateAssets()
    {
        foreach (string id in new[] { "Module_HandDrill", "Module_MechanicalNode" })
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(ModuleFolder + "/" + id + ".prefab");
            if (asset == null || asset.GetComponent<Module>() == null) throw new InvalidOperationException(id + " 未装配。");
        }
        foreach (string id in new[] { "UI_HandDrill", "UI_Mechanical" })
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(UiFolder + "/" + id + ".prefab");
            var view = asset.GetComponent<MechanicalPanelView>();
            if (view == null || view.Title == null || view.Status == null || view.ActionButton == null ||
                view.CloseButton == null || view.InputSlot == null || view.OutputSlot == null ||
                view.ProcessingVisuals == null || view.ProcessingVisuals.Length != 11 || view.ProcessingVisuals.Any(value => value == null) ||
                asset.GetComponent<BuildingPanelActions>() == null) throw new InvalidOperationException(id + " 引用不完整。");
        }
        MechanicalCatalog.EnsureLoaded();
        Debug.Log("[Mechanical] 正式 Prefab 引用与机械目录校验通过。");
    }
    #endregion

    #region 定向双语条目
    private static readonly string[][] Texts =
    {
        new[] { "手钻", "Hand Drill" }, new[] { "机械动力", "Mechanical Power" }, new[] { "钻孔", "Drill" },
        new[] { "摇动", "Crank" }, new[] { "断开", "Disengage" }, new[] { "接合", "Engage" },
        new[] { "切换传动比", "Change Ratio" }, new[] { "旋转建筑", "Rotate Building" },
        new[] { "加工进度 {0:0}%", "Progress {0:0}%" },
        new[] { "{0} · 转速 {1:0} · 动力 {2:0.#}/{3:0.#}", "{0} · RPM {1:0} · Power {2:0.#}/{3:0.#}" },
        new[] { " · 传动比 {0:0.##}", " · Ratio {0:0.##}" },
        new[] { "无动力", "No Power" }, new[] { "过载", "Overloaded" }, new[] { "运行中", "Running" },
        new[] { "传动比冲突", "Ratio Conflict" }, new[] { "休眠", "Sleeping" }, new[] { "停止", "Stopped" }
    };
    private static void SyncText(string chinese, string english)
        => SyncEntry("FlatWorldUI", FlatWorldLocalizationService.GetUiTextKey(chinese), chinese, english);

    private static void SyncNames()
    {
        string path = "Assets/Localization/ItemNames.en.json";
        JObject document = JObject.Parse(File.ReadAllText(path));
        JObject names = (JObject)document["names"];
        string[] ids = { "HandCrank", "WaterWheel", "Windmill", "Shaft_Wood", "Gear_Wood", "Gearbox_Wood", "Clutch", "Shaft_Copper", "Gear_Copper", "Gearbox_Copper", "Shaft_Iron", "Gear_Iron", "Gearbox_Iron", "CrossShaft", "Millstone", "MechanicalBellows", "Sawmill", "MechanicalHammer", "HandDrill" };
        string[] english = { "Hand Crank", "Water Wheel", "Windmill", "Wooden Shaft", "Wooden Gear", "Wooden Gearbox", "Clutch", "Copper Shaft", "Copper Gear", "Copper Gearbox", "Iron Shaft", "Iron Gear", "Iron Gearbox", "Shaft Bridge", "Millstone", "Mechanical Bellows", "Sawmill", "Mechanical Hammer", "Hand Drill" };
        for (int i = 0; i < ids.Length; i++) { names[ids[i]] = english[i]; names[ids[i] + "_Summoner"] = english[i]; }
        names["DrilledStoneSlab"] = "Drilled Stone Slab"; names["DrilledStone"] = "Drilled Stone";
        File.WriteAllText(path, document.ToString() + "\n", new System.Text.UTF8Encoding(false));
        foreach (var definition in ItemDefinitionCatalogLoader.LoadBuiltInDefinitions())
        {
            if (definition.Abstract || (!ids.Contains(definition.Id) && !ids.Any(id => definition.Id == id + "_Summoner") &&
                definition.Id != "DrilledStoneSlab" && definition.Id != "DrilledStone")) continue;
            SyncEntry("FlatWorld", string.IsNullOrWhiteSpace(definition.LabelKey)
                ? FlatWorldLocalizationService.GetItemLabelKey(definition.Id) : definition.LabelKey,
                definition.GameName, (string)names[definition.Id]);
        }
    }
    private static void SyncEntry(string collectionName, string key, string chinese, string english)
    {
        var collection = LocalizationEditorSettings.GetStringTableCollection(collectionName);
        if (collection == null) throw new InvalidOperationException("缺少本地化表 " + collectionName);
        foreach (string locale in new[] { "zh-CN", "en" })
        {
            var table = (StringTable)collection.GetTable(new LocaleIdentifier(locale));
            table.AddEntry(key, locale == "en" ? english : chinese); EditorUtility.SetDirty(table);
        }
        EditorUtility.SetDirty(collection.SharedData);
    }
    #endregion
}
