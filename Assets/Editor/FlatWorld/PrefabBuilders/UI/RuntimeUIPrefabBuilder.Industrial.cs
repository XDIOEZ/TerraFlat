using System;
using System.Linq;
using FlatWorld.Localization;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static partial class RuntimeUIPrefabBuilder
{
    #region 工业与气罐正式页面
    private const string IndustrialBasePath = "Assets/2_Prefabs/2-1_UI/Gameplay/Crafting/UI_Mechanical.prefab";
    private const string EquipmentPagePath = "Assets/2_Prefabs/2-1_UI/Gameplay/Inventory/Panels/UI_Equipment.prefab";

    /// <summary>明确执行时重建完整正式资产；控件继续继承项目公共 Prefab。</summary>
    [MenuItem("FlatWorld/UI/Rebuild Industrial Fluid And Spacesuit Panels")]
    public static void RebuildIndustrialPanels()
    {
        if (Application.isPlaying) throw new InvalidOperationException("请在退出 PlayMode 后手动重建页面；运行游戏期间只修改静态资产。");
        foreach (string name in new[] { "UI_IndustrialFluid", "UI_Spacesuit", "UI_FluidTank" })
        {
            GameObject root = PrefabUtility.LoadPrefabContents(IndustrialBasePath);
            try
            {
                ConfigureIndustrialPanel(root, name);
                string path = "Assets/2_Prefabs/2-1_UI/Gameplay/Crafting/" + name + ".prefab";
                PrefabUtility.SaveAsPrefabAsset(root, path); EnsureRuntimePrefabAddressable(path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        GameObject equipment = PrefabUtility.LoadPrefabContents(EquipmentPagePath);
        try
        {
            if (!equipment.GetComponentsInChildren<Button>(true).Any(button => button.name == "FWUI_ConfigureSpacesuit"))
                AddIndustrialButton(equipment.transform, "FWUI_ConfigureSpacesuit", "宇航服配置", new Vector2(0, 28), new Vector2(190, 42),
                    new Vector2(.5f, 0), new Vector2(.5f, .5f));
            PrefabUtility.SaveAsPrefabAsset(equipment, EquipmentPagePath);
        }
        finally { PrefabUtility.UnloadPrefabContents(equipment); }
        AssetDatabase.SaveAssets();
    }
    private static void ConfigureIndustrialPanel(GameObject root, string name)
    {
        root.name = name;
        var view = root.GetComponent<MechanicalPanelView>() ?? throw new InvalidOperationException("机械基础面板缺少正式视图。");
        view.PreservePrefabLayout = true;
        ((RectTransform)root.transform).sizeDelta = new Vector2(760, name == "UI_IndustrialFluid" ? 680 : 600);
        view.ProcessingVisuals = root.GetComponentsInChildren<Transform>(true)
            .Where(child => child.name.Contains("INPUT", StringComparison.Ordinal) || (name == "UI_IndustrialFluid" || name == "UI_Spacesuit") && child.name.Contains("OUTPUT", StringComparison.Ordinal))
            .Select(child => child.gameObject).Concat(name == "UI_IndustrialFluid" || name == "UI_Spacesuit"
                ? new[] { view.InputSlot.gameObject, view.OutputSlot.gameObject } : new[] { view.InputSlot.gameObject }).Distinct().ToArray();
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name.StartsWith("FWUI_FlowArrow", StringComparison.Ordinal) || name != "UI_IndustrialFluid" && name != "UI_Spacesuit" && child.name.Contains("OUTPUT", StringComparison.Ordinal)) child.gameObject.SetActive(false);
        view.OutputSlot.gameObject.SetActive(name == "UI_IndustrialFluid" || name == "UI_Spacesuit");
        if (name == "UI_Spacesuit")
            for (Transform ancestor = view.OutputSlot.transform.parent; ancestor != null && ancestor != root.transform; ancestor = ancestor.parent)
                ancestor.gameObject.SetActive(true);
        view.InputSlot.gameObject.SetActive(name != "UI_FluidTank");
        if (name == "UI_FluidTank") foreach (GameObject visual in view.ProcessingVisuals) visual.SetActive(false);
        view.DismantleButton.gameObject.SetActive(false); view.ActionButton.gameObject.SetActive(name != "UI_FluidTank");
        RectTransform status = (RectTransform)view.StatusScroll.transform;
        status.anchorMin = Vector2.zero; status.anchorMax = Vector2.one; status.pivot = new Vector2(.5f, .5f);
        status.anchoredPosition = new Vector2(0, name == "UI_FluidTank" ? 0 : -140);
        status.sizeDelta = new Vector2(-24, name == "UI_FluidTank" ? -24 : -304);
        if (name != "UI_IndustrialFluid") return;
        foreach (GameObject visual in view.ProcessingVisuals)
            ((RectTransform)visual.transform).anchoredPosition += Vector2.down * 34;
        AddIndustrialField(root.transform, "FWUI_FluidSettingPrimary", "气体ID / 断开压强 / 充装压强", new Vector2(24, -78), new Vector2(245, 40));
        AddIndustrialField(root.transform, "FWUI_FluidSettingSecondary", "恢复压强 kPa", new Vector2(280, -78), new Vector2(200, 40));
        AddIndustrialButton(root.transform, "FWUI_ApplyFluidSettings", "应用配置", new Vector2(494, -78), new Vector2(240, 40), new Vector2(0, 1), new Vector2(0, 1));
        AddIndustrialButton(root.transform, "FWUI_FluidPhase", "输出气体和液体", new Vector2(24, 29), new Vector2(190, 46), Vector2.zero, new Vector2(0, .5f));
        AddIndustrialButton(root.transform, "FWUI_RotateFluid", "旋转方向", new Vector2(226, 29), new Vector2(150, 46), Vector2.zero, new Vector2(0, .5f));
    }
    private static void AddIndustrialField(Transform parent, string name, string hint, Vector2 position, Vector2 size)
    {
        TMP_InputField field = InstantiateSharedControl<TMP_InputField>("UI_InputField", name, parent);
        LayoutIndustrialControl((RectTransform)field.transform, position, size, new Vector2(0, 1), new Vector2(0, 1));
        field.characterLimit = 128;
        if (field.placeholder is TMP_Text placeholder) placeholder.text = hint;
        RecordSharedInstance(field.gameObject);
    }
    private static void AddIndustrialButton(Transform parent, string name, string caption, Vector2 position, Vector2 size, Vector2 anchor, Vector2 pivot)
    {
        Button button = InstantiateSharedControl<Button>("UI_Button", name, parent);
        LayoutIndustrialControl((RectTransform)button.transform, position, size, anchor, pivot);
        TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
        LocalizedTextBinder binder = label.GetComponent<LocalizedTextBinder>();
        if (binder != null) binder.Configure(FlatWorldLocalizationService.UiTable, FlatWorldLocalizationService.GetUiTextKey(caption), caption);
        else label.text = caption;
        RecordSharedInstance(button.gameObject);
    }
    private static void LayoutIndustrialControl(RectTransform control, Vector2 position, Vector2 size, Vector2 anchor, Vector2 pivot)
    { control.anchorMin = control.anchorMax = anchor; control.pivot = pivot; control.anchoredPosition = position; control.sizeDelta = size; }
    #endregion
}
