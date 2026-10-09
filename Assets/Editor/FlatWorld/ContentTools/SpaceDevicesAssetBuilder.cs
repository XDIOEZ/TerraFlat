using System;
using System.IO;
using FlatWorld.Localization;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.UI;

/// <summary>显式构建培育器资源和双气罐宇航服页，保存正式 Prefab 与 Addressables 分组。</summary>
public static class SpaceDevicesAssetBuilder
{
    #region 显式资源构建
    private const string ModulePath = "Assets/2_Prefabs/Gameplay/Modules/Space/Module_Cultivator.prefab";
    private const string UiFolder = "Assets/2_Prefabs/2-1_UI/Gameplay/Crafting/";
    private const string SlotPath = "Assets/2_Prefabs/2-1_UI/Gameplay/Inventory/Components/UI_Slot.prefab";
    private const string ButtonPath = "Assets/2_Prefabs/2-1_UI/Common/Controls/UI_Button.prefab";
    [MenuItem("FlatWorld/太空/构建培育器与推进气罐页面")]
    public static void Build()
    {
        if (Application.isPlaying) throw new InvalidOperationException("运行期间不重建太空设备 Prefab，请退出 Play Mode 后执行。");
        Directory.CreateDirectory(Path.GetDirectoryName(ModulePath));
        AssetDatabase.Refresh();
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ModulePath) == null)
        {
            var module = new GameObject("Module_Cultivator");
            module.AddComponent<Mod_Cultivator>();
            try { PrefabUtility.SaveAsPrefabAsset(module, ModulePath); }
            finally { UnityEngine.Object.DestroyImmediate(module); }
        }
        Register(ModulePath, "Module_Cultivator");
        BuildCultivatorPanel();
        UpdateSpacesuitPanel();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[SpaceDevices] 培育器模块、正式页面和宇航服独立推进槽已保存。");
    }
    private static void Register(string path, string address)
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings
            ?? throw new InvalidOperationException("太空设备资源缺少 Addressables 设置。");
        var entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(path), settings.DefaultGroup);
        entry.address = address;
        entry.SetLabel("Prefab", true, true);
        EditorUtility.SetDirty(entry.parentGroup);
        EditorUtility.SetDirty(settings);
    }
    #endregion

    #region 培育器正式槽位布局
    private static void BuildCultivatorPanel()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(UiFolder + "UI_Mechanical.prefab");
        try
        {
            root.name = "UI_Cultivator";
            var mechanical = root.GetComponent<MechanicalPanelView>()
                ?? throw new InvalidOperationException("培育器页面基础资源缺少机械视图。");
            mechanical.PreservePrefabLayout = true;
            ((RectTransform)root.transform).sizeDelta = new Vector2(960, 780);
            mechanical.SetProcessingVisible(false);
            mechanical.InputSlot.gameObject.SetActive(false);
            mechanical.OutputSlot.gameObject.SetActive(false);
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (child.name.StartsWith("FWUI_FlowArrow", StringComparison.Ordinal)) child.gameObject.SetActive(false);
            CultivatorPanelView view = root.GetComponent<CultivatorPanelView>() ?? root.AddComponent<CultivatorPanelView>();
            view.SeedSlots = new ItemSlot_UI[4];
            view.HarvestSlots = new ItemSlot_UI[9];
            view.ClearPlotButtons = new Button[4];
            Transform inner = mechanical.InnerField;
            for (int i = 0; i < 4; i++)
            {
                float x = -350 + i * 190;
                view.SeedSlots[i] = AddSlot(inner, "种子_" + (i + 1), new Vector2(x, 195));
                view.ClearPlotButtons[i] = AddButton(inner, "清除植株_" + (i + 1), "清除植株", new Vector2(x, 130), new Vector2(140, 36));
                AddLabel(mechanical.Status, inner, "种植槽标题_" + (i + 1), "种植槽 " + (i + 1), new Vector2(x, 263), new Vector2(160, 35));
            }
            view.FertilizerSlot = AddSlot(inner, "肥料_1", new Vector2(380, 195));
            AddLabel(mechanical.Status, inner, "肥料标题", "肥料（可选）", new Vector2(380, 263), new Vector2(160, 35));
            for (int i = 0; i < 9; i++)
                view.HarvestSlots[i] = AddSlot(inner, "收获_" + (i + 1), new Vector2(-350 + i % 3 * 90, 35 - i / 3 * 90));
            AddLabel(mechanical.Status, inner, "收获标题", "收获物品", new Vector2(-260, 94), new Vector2(270, 32));
            Layout((RectTransform)mechanical.StatusScroll.transform, new Vector2(165, -55), new Vector2(550, 310));
            view.Status = mechanical.Status;
            mechanical.Status.fontSize = 21;
            mechanical.Status.enableAutoSizing = false;
            mechanical.Status.text = "放入非树种子，接电后通过管道或水桶供水。";
            mechanical.Title.text = "培育仓";
            mechanical.SetActionVisible(true);
            MechanicalPanelView.SetButtonCaption(mechanical.ActionButton, "收获成熟作物");
            view.WaterButton = AddButton(root.transform, "水桶补水", "倒入手持容器的水", new Vector2(-280, 29), new Vector2(250, 46), bottom: true);
            FlatWorldUITheme.Apply(root.transform);
            PrefabUtility.SaveAsPrefabAsset(root, UiFolder + "UI_Cultivator.prefab");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        Register(UiFolder + "UI_Cultivator.prefab", "UI_Cultivator");
    }
    private static ItemSlot_UI AddSlot(Transform parent, string name, Vector2 position)
    {
        GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>(SlotPath)
            ?? throw new InvalidOperationException("培育器页面缺少公共物品槽模板。");
        GameObject slot = (GameObject)PrefabUtility.InstantiatePrefab(template, parent);
        slot.name = name;
        Layout((RectTransform)slot.transform, position, new Vector2(76, 76));
        foreach (Component component in slot.GetComponentsInChildren<Component>(true))
            if (component != null) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        return slot.GetComponent<ItemSlot_UI>() ?? throw new InvalidOperationException("公共物品槽缺少 ItemSlot_UI。");
    }
    private static Button AddButton(Transform parent, string name, string caption, Vector2 position, Vector2 size, bool bottom = false)
    {
        GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>(ButtonPath)
            ?? throw new InvalidOperationException("太空设备页面缺少公共按钮模板。");
        GameObject button = (GameObject)PrefabUtility.InstantiatePrefab(template, parent);
        button.name = name;
        Layout((RectTransform)button.transform, position, size, bottom);
        Button result = button.GetComponent<Button>();
        MechanicalPanelView.SetButtonCaption(result, caption);
        foreach (Component component in button.GetComponentsInChildren<Component>(true))
            if (component != null) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        return result;
    }
    private static void AddLabel(TMP_Text source, Transform parent, string name, string caption, Vector2 position, Vector2 size)
    {
        TMP_Text label = UnityEngine.Object.Instantiate(source, parent);
        label.name = name;
        var binder = label.GetComponent<LocalizedTextBinder>();
        if (binder != null) UnityEngine.Object.DestroyImmediate(binder);
        var fitter = label.GetComponent<ContentSizeFitter>();
        if (fitter != null) UnityEngine.Object.DestroyImmediate(fitter);
        Layout(label.rectTransform, position, size);
        label.fontSize = 22;
        label.enableAutoSizing = false;
        label.alignment = TextAlignmentOptions.Center;
        label.text = caption;
        label.raycastTarget = false;
    }
    private static void Layout(RectTransform control, Vector2 position, Vector2 size, bool bottom = false)
    {
        control.anchorMin = control.anchorMax = new Vector2(.5f, bottom ? 0f : .5f);
        control.pivot = new Vector2(.5f, .5f);
        control.anchoredPosition = position;
        control.sizeDelta = size;
    }
    #endregion

    #region 宇航服双罐祖先可见性
    private static void UpdateSpacesuitPanel()
    {
        string path = UiFolder + "UI_Spacesuit.prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var view = root.GetComponent<MechanicalPanelView>();
            Transform inner = view.InnerField;
            foreach (GameObject visual in view.ProcessingVisuals) visual.SetActive(false);
            foreach (string name in new[] { "供氧槽标题", "推进槽标题" })
            {
                Transform existing = inner.Find(name);
                if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);
            }
            view.InputSlot.transform.SetParent(inner, false);
            view.OutputSlot.transform.SetParent(inner, false);
            view.InputSlot.gameObject.SetActive(true);
            view.OutputSlot.gameObject.SetActive(true);
            AddLabel(view.Status, inner, "供氧槽标题", "供氧气罐", new Vector2(-160, 165), new Vector2(220, 36));
            AddLabel(view.Status, inner, "推进槽标题", "推进气罐", new Vector2(160, 165), new Vector2(220, 36));
            view.ProcessingVisuals = new[] { view.InputSlot.gameObject, view.OutputSlot.gameObject,
                inner.Find("供氧槽标题").gameObject, inner.Find("推进槽标题").gameObject };
            Layout((RectTransform)view.InputSlot.transform, new Vector2(-160, 95), new Vector2(86, 86));
            Layout((RectTransform)view.OutputSlot.transform, new Vector2(160, 95), new Vector2(86, 86));
            FlatWorldUITheme.Apply(root.transform);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        Register(path, "UI_Spacesuit");
    }
    #endregion
}
