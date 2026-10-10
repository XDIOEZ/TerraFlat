using System;
using System.IO;
using FlatWorld.Localization;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;
using UnityEngine.UI;

public static partial class RuntimeUIPrefabBuilder
{
    #region 太空正式面板和模块资源
    [MenuItem("FlatWorld/太空/构建飞船与下落选址页面")]
    public static void RebuildSpacePanels()
    {
        if (Application.isPlaying) throw new InvalidOperationException("运行期间不重建太空 Prefab。");
        const string modulePath = "Assets/2_Prefabs/Gameplay/Modules/Space/Module_ShipPart.prefab";
        Directory.CreateDirectory(Path.GetDirectoryName(modulePath));
        GameObject module = new("Module_ShipPart"); module.AddComponent<Mod_ShipPart>();
        try { PrefabUtility.SaveAsPrefabAsset(module, modulePath); }
        finally { UnityEngine.Object.DestroyImmediate(module); }
        EnsureRuntimePrefabAddressable(modulePath);
        foreach (string name in new[] { "UI_Ship", "UI_SpaceLanding" })
        {
            GameObject root = PrefabUtility.LoadPrefabContents(IndustrialBasePath);
            try
            {
                ConfigureIndustrialPanel(root, "UI_FluidTank"); root.name = name;
                ((RectTransform)root.transform).sizeDelta = new Vector2(900, 740);
                MechanicalPanelView view = root.GetComponent<MechanicalPanelView>();
                view.Title.text = name == "UI_Ship" ? "飞船状态与操作" : "下落选址";
                view.Status.name = name == "UI_Ship" ? "FWUI_ShipStatus" : "FWUI_LandingStatus";
                view.Status.fontSize = 20;
                var status = (RectTransform)view.StatusScroll.transform;
                status.anchorMin = new Vector2(0, .35f); status.anchorMax = new Vector2(1, 1);
                status.offsetMin = new Vector2(18, 10); status.offsetMax = new Vector2(-18, -58);
                if (name == "UI_Ship")
                {
                    ConfigureShipTerminal(root, view);
                    AddIndustrialField(root.transform, "FWUI_ShipX", "导航/落点 X", new Vector2(24, -580), new Vector2(300, 60));
                    AddIndustrialField(root.transform, "FWUI_ShipY", "导航/落点 Y", new Vector2(340, -580), new Vector2(300, 60));
                    AddIndustrialField(root.transform, "FWUI_ShipPressure", "舱内目标气压 kPa", new Vector2(656, -580), new Vector2(500, 60));
                    AddIndustrialField(root.transform, "FWUI_ShipCommand", "输入命令，help 查看帮助", new Vector2(24, -504), new Vector2(990, 60));
                    TMP_InputField command = FindShipControl<TMP_InputField>(root, "FWUI_ShipCommand");
                    command.characterLimit = 160; command.richText = false;
                    AddIndustrialButton(root.transform, "FWUI_ShipSubmit", "执行", new Vector2(1030, 286), new Vector2(126, 60), Vector2.zero, new Vector2(0, .5f));
                    string[] keys = { "Drive", "Exit", "Ignite", "Navigate", "Pick", "Cancel", "Landing", "Dock", "Undock", "Pressure" };
                    string[] labels = { "驾驶", "退出驾驶", "点火 / 停机", "按坐标导航", "点击画面选点", "取消导航", "设置下落位置", "本船确认对接", "解除当前接口", "应用供气压强" };
                    for (int i = 0; i < keys.Length; i++)
                        AddIndustrialButton(root.transform, "FWUI_Ship" + keys[i], labels[i],
                            new Vector2(24 + i % 5 * 229, 138 - i / 5 * 72), new Vector2(216, 60), Vector2.zero, new Vector2(0, .5f));
                }
                else
                {
                    GameObject map = new("FWUI_LandingMap", typeof(RectTransform), typeof(RawImage));
                    map.transform.SetParent(root.transform, false);
                    var rect = (RectTransform)map.transform;
                    LayoutIndustrialControl(rect, new Vector2(0, 198), new Vector2(300, 300), new Vector2(.5f, 0), new Vector2(.5f, .5f));
                    status.anchorMin = new Vector2(0, .49f);
                }
                SetUILayerRecursively(root); FlatWorldUITheme.Apply(root.transform); PrepareSharedControlsForSave(root);
                string path = "Assets/2_Prefabs/2-1_UI/Gameplay/Crafting/" + name + ".prefab";
                PrefabUtility.SaveAsPrefabAsset(root, path); EnsureRuntimePrefabAddressable(path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        foreach (var group in settings.groups) if (group != null) EditorUtility.SetDirty(group);
        EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets();
    }

    // 双屏只在正式资产中构建，运行时会话仅更新状态、命令和反馈。
    private static void ConfigureShipTerminal(GameObject root, MechanicalPanelView view)
    {
        RectTransform panel = (RectTransform)root.transform;
        panel.sizeDelta = new Vector2(1180, 820);
        var scale = root.GetComponent<SafeAreaScaleGroup>() ?? root.AddComponent<SafeAreaScaleGroup>();
        scale.Configure(panel, new[] { panel }, new Vector2(24, 24));
        SetShipCaption(view.Title, "飞船控制台");
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == "FWUI_Shadow" || child.name == "FWUI_Footer" || child.name == "FWUI_眉题") child.gameObject.SetActive(false);
        Image inner = view.InnerField.GetComponent<Image>();
        if (inner != null) { inner.enabled = false; inner.raycastTarget = false; }
        view.StatusScroll.transform.SetParent(root.transform, false);
        ScrollRect output = UnityEngine.Object.Instantiate(view.StatusScroll, root.transform);
        output.name = "FWUI_ShipOutputScroll";
        TMP_Text outputText = output.content.GetComponent<TMP_Text>();
        outputText.name = "FWUI_ShipOutput"; outputText.text = string.Empty;
        ConfigureShipScreen(view.StatusScroll, view.Status, new Vector2(0, 0), new Vector2(.42f, 1), new Vector2(24, 332), new Vector2(-8, -118));
        ConfigureShipScreen(output, outputText, new Vector2(.42f, 0), Vector2.one, new Vector2(8, 332), new Vector2(-24, -118));
        AddShipScreenHeading(root.transform, view.Status, "FWUI_ShipStatusHeading", "飞船状态", new Vector2(24, -80), new Vector2(460, 28));
        AddShipScreenHeading(root.transform, view.Status, "FWUI_ShipOutputHeading", "命令与反馈", new Vector2(504, -80), new Vector2(652, 28));
    }

    private static void ConfigureShipScreen(ScrollRect scroll, TMP_Text text, Vector2 min, Vector2 max, Vector2 lower, Vector2 upper)
    {
        RectTransform screen = (RectTransform)scroll.transform;
        screen.anchorMin = min; screen.anchorMax = max; screen.offsetMin = lower; screen.offsetMax = upper;
        GameObject content = new("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(scroll.viewport, false);
        RectTransform rect = (RectTransform)content.transform;
        rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one; rect.pivot = new Vector2(.5f, 1);
        rect.anchoredPosition = Vector2.zero; rect.sizeDelta = Vector2.zero;
        VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 14, 14); layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        text.transform.SetParent(content.transform, false);
        text.rectTransform.anchorMin = new Vector2(0, 1); text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.pivot = new Vector2(.5f, 1); text.rectTransform.anchoredPosition = Vector2.zero;
        text.rectTransform.sizeDelta = new Vector2(0, 28);
        ContentSizeFitter oldFit = text.GetComponent<ContentSizeFitter>();
        if (oldFit != null) UnityEngine.Object.DestroyImmediate(oldFit);
        LocalizedTextBinder binder = text.GetComponent<LocalizedTextBinder>();
        if (binder != null) UnityEngine.Object.DestroyImmediate(binder);
        text.fontSize = 20; text.enableAutoSizing = false; text.color = FlatWorldUITheme.TextPrimary;
        text.text = string.Empty;
        text.alignment = TextAlignmentOptions.TopLeft; text.richText = false;
        text.enableWordWrapping = true; text.overflowMode = TextOverflowModes.Overflow; text.raycastTarget = false;
        scroll.content = rect; scroll.horizontal = false; scroll.vertical = true;
    }

    private static void AddShipScreenHeading(Transform parent, TMP_Text template, string name, string caption, Vector2 position, Vector2 size)
    {
        TMP_Text heading = UnityEngine.Object.Instantiate(template, parent); heading.name = name;
        heading.fontSize = 21; heading.color = FlatWorldUITheme.TextSecondary;
        SetShipCaption(heading, caption);
        LayoutIndustrialControl(heading.rectTransform, position, size, new Vector2(0, 1), new Vector2(0, 1));
    }

    private static void SetShipCaption(TMP_Text text, string caption)
    {
        LocalizedTextBinder binder = text.GetComponent<LocalizedTextBinder>() ?? text.gameObject.AddComponent<LocalizedTextBinder>();
        binder.Configure(FlatWorldLocalizationService.UiTable, FlatWorldLocalizationService.GetUiTextKey(caption), caption);
        text.text = caption;
    }

    private static T FindShipControl<T>(GameObject root, string name) where T : Component
    {
        foreach (T control in root.GetComponentsInChildren<T>(true)) if (control.name == name) return control;
        throw new InvalidOperationException("飞船正式面板缺少控件：" + name);
    }
    #endregion
}
