using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static partial class RuntimeUIPrefabBuilder
{
    #region 太空电梯正式面板
    [MenuItem("FlatWorld/其他工具/太空/构建太空电梯页面")]
    public static void RebuildSpaceElevatorPanel()
    {
        if (Application.isPlaying) throw new InvalidOperationException("运行期间不重建太空电梯 Prefab。");
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        const string path = PrefabRoot + "Gameplay/Crafting/UI_SpaceElevator.prefab";
        var root = new GameObject(RuntimeUIPrefabKeys.SpaceElevator, typeof(RectTransform));
        root.SetActive(false);
        try
        {
            root.layer = 5;
            RectTransform rect = (RectTransform)root.transform;
            rect.sizeDelta = new Vector2(760, 800);
            BasePanel panel = root.AddComponent<BasePanel>();
            panel.canvasGroup = root.GetComponent<CanvasGroup>(); panel.rectTransform = rect;
            panel.PanelName = RuntimeUIPrefabKeys.SpaceElevator;
            panel.Dragger = root.AddComponent<UI_Drag>(); panel.Dragger.rectTransform = rect;
            panel.CanDrag = true;
            root.AddComponent<SafeAreaScaleGroup>().Configure(rect, new[] { rect }, new Vector2(24, 24));
            Image background = InstantiateSharedControl<Image>(RuntimeUIPrefabKeys.PanelBackground, "FWUI_PanelBackground", root.transform);
            Stretch(background.rectTransform);
            GameObject hit = CreateUIObject("FWUI_WindowDragSurface", root.transform, typeof(CanvasRenderer), typeof(UIWindowDragSurface));
            Stretch((RectTransform)hit.transform);
            hit.GetComponent<UIWindowDragSurface>().Configure(rect, 70, 12);
            var view = root.AddComponent<SpaceElevatorPanelView>();
            view.Title = CreateElevatorText(root.transform, "FWUI_ElevatorTitle", "太空电梯", 28, 24, -18, 560, 44);
            view.State = CreateElevatorText(root.transform, "FWUI_ElevatorState", string.Empty, 20, 24, -76, 712, 64);
            view.Hint = CreateElevatorText(root.transform, "FWUI_ElevatorHint", string.Empty, 18, 24, -144, 712, 40);
            view.WarehouseHeading = CreateElevatorText(root.transform, "FWUI_ElevatorWarehouseHeading", string.Empty, 22, 24, -198, 500, 32);
            view.CargoCapacity = CreateElevatorText(root.transform, "FWUI_ElevatorCargoCapacity", string.Empty, 18, 24, -238, 712, 28);
            view.Feedback = CreateElevatorText(root.transform, "FWUI_ElevatorFeedback", string.Empty, 18, 24, -748, 712, 40);
            view.Close = CreateElevatorButton(root.transform, "FWUI_ElevatorClose", "关闭", 620, -18, 116, 44);
            view.OpenBag = CreateElevatorButton(root.transform, "FWUI_ElevatorOpenBag", "打开背包", 552, -192, 184, 40);
            BuildElevatorWarehouse(view, root.transform);
            view.SendCargo = CreateElevatorButton(root.transform, "FWUI_ElevatorSend", "打包发送", 24, -690, 228, 48);
            view.Ride = CreateElevatorButton(root.transform, "FWUI_ElevatorRide", "乘坐前往太空站台", 266, -690, 228, 48);
            view.CallCabin = CreateElevatorButton(root.transform, "FWUI_ElevatorCall", "召回轿厢", 508, -690, 228, 48);
            root.SetActive(true);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            PrefabUtility.SaveAsPrefabAsset(root, path);
            EnsureRuntimePrefabAddressable(path);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    private static void BuildElevatorWarehouse(SpaceElevatorPanelView view, Transform parent)
    {
        GameObject viewport = CreateUIObject("FWUI_ElevatorWarehouse", parent,
            typeof(CanvasRenderer), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
        RectTransform viewportRect = (RectTransform)viewport.transform;
        PlaceElevatorControl(viewportRect, 24, -270, 712, 408);
        viewport.GetComponent<Image>().color = new Color(0, 0, 0, .02f);
        var contentObject = CreateUIObject("FWUI_ElevatorWarehouseContent", viewport.transform,
            typeof(GridLayoutGroup), typeof(ContentSizeFitter));
        RectTransform content = (RectTransform)contentObject.transform;
        content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f, 1);
        content.anchoredPosition = Vector2.zero; content.sizeDelta = new Vector2(0, 408);
        GridLayoutGroup grid = contentObject.GetComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(96, 96); grid.spacing = new Vector2(8, 8);
        grid.padding = new RectOffset(48, 48, 0, 0);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = SpaceElevatorPanelView.WarehouseColumns;
        contentObject.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        ScrollRect scroll = viewport.GetComponent<ScrollRect>();
        scroll.viewport = viewportRect; scroll.content = content;
        scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 32;
        GameObject slotPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(InventoryComponentsRoot + "UI_Slot.prefab")
            ?? throw new InvalidOperationException("背包正式插槽 Prefab 缺失。");
        // 静态仓库默认复用二十四个背包插槽，更多容量由同一模板扩展滚动内容。
        for (int index = 0; index < SpaceElevatorPanelView.DefaultWarehouseSlots; index++)
        {
            GameObject slot = (GameObject)PrefabUtility.InstantiatePrefab(slotPrefab, content);
            slot.name = "FWUI_ElevatorWarehouseSlot" + index;
            if (index == 0) view.WarehouseSlotTemplate = (RectTransform)slot.transform;
        }
        view.WarehouseContent = content; view.WarehouseScroll = scroll;
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
    }
    private static TextMeshProUGUI CreateElevatorText(Transform parent, string name, string caption, float size,
        float x, float y, float width, float height)
    {
        TextMeshProUGUI text = CreateText(name, parent, caption, size, Cream);
        text.richText = false; text.enableWordWrapping = true; text.overflowMode = TextOverflowModes.Truncate;
        text.raycastTarget = false; text.alignment = TextAlignmentOptions.TopLeft;
        PlaceElevatorControl(text.rectTransform, x, y, width, height);
        return text;
    }
    private static Button CreateElevatorButton(Transform parent, string name, string caption, float x, float y,
        float width, float height)
    {
        Button button = CreateButton(name, parent, caption, width, height, false);
        PlaceElevatorControl((RectTransform)button.transform, x, y, width, height);
        TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
        FlatWorldUITextLayout.Configure(label, 20f); label.enableWordWrapping = false; label.overflowMode = TextOverflowModes.Ellipsis;
        label.richText = false; label.raycastTarget = false;
        return button;
    }
    private static void PlaceElevatorControl(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, y); rect.sizeDelta = new Vector2(width, height);
    }
    #endregion
}
