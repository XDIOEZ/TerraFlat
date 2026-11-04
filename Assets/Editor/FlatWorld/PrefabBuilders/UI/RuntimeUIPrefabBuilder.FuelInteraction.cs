using FlatWorld.Localization.Editor;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static FlatWorldEditorUiLayout;

public static partial class RuntimeUIPrefabBuilder
{
    /// <summary>重建通用燃料交互面板；正式运行时只实例化该 Prefab。</summary>
    [MenuItem("FlatWorld/其他工具/UI/Rebuild Fuel Interaction UI")]
    public static void RebuildFuelInteractionUI()
    {
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        string path = "Assets/2_Prefabs/2-1_UI/Gameplay/Containers/" + FuelInteractionPanel.PrefabKey + ".prefab";
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
        SaveNewPrefab(path, BuildFuelInteraction);
        EnsureRuntimePrefabAddressable(path);
        AssetDatabase.SaveAssets();
        SyncFuelInteractionUiTexts();
    }

    /// <summary>燃料投料区负责补料，公共物品槽绑定持久火种库存。</summary>
    private static GameObject BuildFuelInteraction()
    {
        GameObject root = CreateModalPanelRoot(FuelInteractionPanel.PrefabKey, new Vector2(620f, 520f));
        root.SetActive(false);
        Transform content = root.transform.Find("设置对话框");

        CreateText("标题", content, "燃料", 28f, Amber)
            .gameObject.AddComponent<LayoutElement>().preferredHeight = 48f;
        CreateText("燃料状态", content, "已熄灭　燃料 1440 / 1440 秒", 22f, Cream)
            .gameObject.AddComponent<LayoutElement>().preferredHeight = 56f;

        Slider fuelBar = InstantiateSharedControl<Slider>("UI_ProgressBar", "燃料进度", content);
        LayoutElement fuelLayout = fuelBar.GetComponent<LayoutElement>() ?? fuelBar.gameObject.AddComponent<LayoutElement>();
        fuelLayout.preferredHeight = 34f;
        fuelBar.minValue = 0f;
        fuelBar.maxValue = 1f;
        fuelBar.SetValueWithoutNotify(1f);

        GameObject slotRow = CreateUIObject("投料区域", content);
        slotRow.AddComponent<LayoutElement>().preferredHeight = 160f;
        HorizontalLayoutGroup rowLayout = slotRow.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 18f;
        rowLayout.childAlignment = TextAnchor.MiddleCenter;
        rowLayout.childControlWidth = false;
        rowLayout.childControlHeight = false;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = false;

        GameObject input = CreateUIObject("燃料输入槽", slotRow.transform, typeof(Image));
        RectTransform inputRect = (RectTransform)input.transform;
        inputRect.sizeDelta = new Vector2(128f, 128f);
        Image inputImage = input.GetComponent<Image>();
        inputImage.color = SurfaceRaised;
        inputImage.raycastTarget = true;
        AddOutline(inputImage, new Color(1f, 1f, 1f, 0.16f));

        TextMeshProUGUI slotText = CreateText("输入槽提示", input.transform, "燃料", 18f, Cream);
        Stretch(slotText.rectTransform);
        slotText.alignment = TextAlignmentOptions.Center;
        slotText.raycastTarget = false;

        GameObject slotPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/2_Prefabs/2-1_UI/Gameplay/Inventory/Components/UI_Slot.prefab");
        GameObject tinderSlot = (GameObject)PrefabUtility.InstantiatePrefab(slotPrefab, slotRow.transform);
        tinderSlot.name = "火种插槽";
        ((RectTransform)tinderSlot.transform).sizeDelta = new Vector2(128f, 128f);
        CreateSettingsHint(content, "放入火种或温度达到500℃的物品后点击点燃；拖入燃料可补充燃料。", 58f);

        GameObject actions = CreateUIObject("操作列表", content);
        actions.AddComponent<LayoutElement>().preferredHeight = 76f;
        HorizontalLayoutGroup actionsLayout = actions.AddComponent<HorizontalLayoutGroup>();
        actionsLayout.spacing = 12f;
        actionsLayout.childAlignment = TextAnchor.MiddleCenter;
        actionsLayout.childControlWidth = true;
        actionsLayout.childControlHeight = true;
        actionsLayout.childForceExpandWidth = false;
        actionsLayout.childForceExpandHeight = false;
        CreateButton("点燃按钮", actions.transform, "点燃", 160f, 64f, false);
        CreateButton("熄灭按钮", actions.transform, "熄灭", 160f, 64f, false).gameObject.SetActive(false);
        CreateButton("关闭按钮", actions.transform, "关闭", 160f, 64f, false);

        PortableBuildingPanelBuilder.ConfigureFuelInteraction(root);
        root.AddComponent<FuelInteractionPanel>();
        root.SetActive(true);
        return root;
    }

    private static void SyncFuelInteractionUiTexts() =>
        FlatWorldLocalizationSetup.SyncRuntimeUiTexts(
            "燃料",
            "燃烧中",
            "已熄灭",
            "{0}　燃料 {1:0} / {2:0} 秒",
            "火种",
            "点燃",
            "熄灭",
            "放入火种或温度达到500℃的物品后点击点燃；拖入燃料可补充燃料。",
            "没有燃料，火种无法点燃。",
            "已重新点燃。",
            "这个物品不能作为燃料。",
            "燃料已经满了。",
            "已补充燃料。");
}
