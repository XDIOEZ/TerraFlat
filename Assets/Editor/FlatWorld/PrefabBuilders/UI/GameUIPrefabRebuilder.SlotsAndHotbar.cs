// 按业务职责维护 Prefab，复用公共控件与布局实现。
using System;
using System.Collections.Generic;
using FlatWorld.Localization;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static FlatWorldEditorUiLayout;

public static partial class GameUIPrefabRebuilder
{
    #region 库存槽位与快捷栏

    private static void BuildHotbar(GameObject root)
    {
        RectTransform rect = root.GetComponent<RectTransform>();
        if (rect == null)
            return;

        // 快捷栏跟随 PanelRoot 的普通面板层级；需要取用快捷栏的面板由背景 Image 自行放行射线。
        Canvas canvas = root.GetComponent<Canvas>();
        if (canvas == null)
            throw new InvalidOperationException("UI_HotBar 缺少 Canvas，无法承载快捷栏槽位射线。");
        canvas.overrideSorting = false;
        canvas.sortingOrder = UIManager.GameplayHudSortingOrder;

        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, UIUserSettings.DefaultHotbarBottomSpacing);
        // 加宽一个槽位和间距，保持十格快捷栏的格子尺寸与两侧留白。
        rect.sizeDelta = new Vector2(982f, 104f);

        Image image = EnsureImage(root);
        image.color = new Color(0.025f, 0.043f, 0.058f, 0.92f);
        image.sprite = null;
        image.type = Image.Type.Simple;
        AddOutline(image, new Color(0.83f, 0.49f, 0.23f, 0.34f));

        GridLayoutGroup grid = root.GetComponent<GridLayoutGroup>();
        if (grid != null)
        {
            grid.cellSize = new Vector2(82f, 82f);
            grid.spacing = new Vector2(10f, 0f);
            grid.padding = new RectOffset(18, 18, 12, 10);
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.childAlignment = TextAnchor.MiddleCenter;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 10;
        }

        // 快捷栏内嵌的是旧版槽位实例，必须同时清掉其绿色像素底图，
        // 否则统一色值仍会被底图相乘成棕绿，和行囊动态槽位不一致。
        for (int i = 0; i < root.transform.childCount; i++)
        {
            Transform child = root.transform.GetChild(i);
            if (child.GetComponent("ItemSlot_UI") != null)
                BuildSlot(child.gameObject);
        }

        BuildHotbarItemName(root);
    }

    /// <summary>名称底板位于快捷栏上方 14 像素，320×52，不参与十格布局或触控射线。</summary>
    private static void BuildHotbarItemName(GameObject root)
    {
        RectTransform plate = FindDirectRect(root.transform, "手持物名称提示") ??
                              CreateRect("手持物名称提示", root.transform);
        plate.anchorMin = plate.anchorMax = new Vector2(0.5f, 1f);
        plate.pivot = new Vector2(0.5f, 0f);
        plate.anchoredPosition = new Vector2(0f, 14f);
        plate.sizeDelta = new Vector2(320f, 52f);
        plate.localScale = Vector3.one;
        LayoutElement layout = plate.GetComponent<LayoutElement>();
        if (layout == null)
            layout = plate.gameObject.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;

        Image background = EnsureImage(plate.gameObject);
        background.sprite = null;
        background.color = new Color(0.22f, 0.22f, 0.22f, 0.65f);
        background.raycastTarget = false;
        CanvasGroup group = plate.GetComponent<CanvasGroup>();
        if (group == null)
            group = plate.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        Transform existingLabel = plate.Find("手持物名称");
        TextMeshProUGUI label = existingLabel != null
            ? existingLabel.GetComponent<TextMeshProUGUI>()
            : CreateText("手持物名称", plate, string.Empty, 32f, Color.white, FontStyles.Normal, TextAlignmentOptions.Center);
        Stretch(label.rectTransform);
        label.rectTransform.offsetMin = new Vector2(18f, 4f);
        label.rectTransform.offsetMax = new Vector2(-18f, -4f);
        label.text = string.Empty;
        label.font = font;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = 32f;
        label.enableAutoSizing = true;
        label.fontSizeMin = FlatWorldUITextLayout.MinimumFontSize(32f);
        label.fontSizeMax = 32f;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.richText = false;
        label.raycastTarget = false;
        if (label.GetComponent<LocalizedTextBinder>() == null)
            label.gameObject.AddComponent<LocalizedTextBinder>();

        HotbarItemNameHUD presenter = root.GetComponent<HotbarItemNameHUD>();
        if (presenter == null)
            presenter = root.AddComponent<HotbarItemNameHUD>();
        SerializedObject serialized = new SerializedObject(presenter);
        serialized.FindProperty("nameGroup").objectReferenceValue = group;
        serialized.FindProperty("nameText").objectReferenceValue = label;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        SetUILayerRecursively(plate.gameObject);
    }

    private static void BuildHandSlot(GameObject root)
    {
        Button slot = root.GetComponentInChildren<Button>(true);
        if (slot != null)
        {
            BuildSlot(slot.gameObject);
            RuntimeUIPrefabBuilder.EnsureTouchLongPressProgress(
                slot.transform,
                slot.GetComponent<ItemSlot_UI>());
        }
        NormalizeTypography(root.transform);
    }

    /// <summary>手部专用槽位在通用槽位视觉之上额外承载长按整组放置进度。</summary>
    private static void BuildHandSlotComponent(GameObject root)
    {
        BuildSlot(root);
        RuntimeUIPrefabBuilder.EnsureTouchLongPressProgress(
            root.transform,
            root.GetComponent<ItemSlot_UI>());
    }

    /// <summary>使用灰阶简约表面构建统一物品槽位，并保留物品图标、数量和制作预览层契约。</summary>
    private static void BuildSlot(GameObject root)
    {
        RectTransform rect = root.GetComponent<RectTransform>();
        if (rect != null)
            rect.sizeDelta = new Vector2(96f, 96f);

        Image image = EnsureImage(root);
        image.color = Surface;
        image.sprite = null;
        image.type = Image.Type.Simple;
        image.preserveAspect = false;

        // 槽位只保留一层轻边界；焦点/拖拽的强反馈仍由 ItemSlot_UI 负责。
        Outline rootOutline = image.GetComponent<Outline>();
        if (rootOutline == null)
            rootOutline = image.gameObject.AddComponent<Outline>();
        rootOutline.effectColor = Border;
            rootOutline.effectDistance = FlatWorldUITheme.BorderOutlineDistance;
        rootOutline.useGraphicAlpha = true;

        Button button = root.GetComponent<Button>();
        if (button != null)
        {
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            button.spriteState = default;

            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.94f, 0.94f, 0.94f, 1f);
            colors.pressedColor = new Color(0.74f, 0.74f, 0.74f, 1f);
            colors.selectedColor = new Color(0.89f, 0.84f, 0.55f, 1f);
            colors.disabledColor = new Color(0.46f, 0.46f, 0.46f, 0.52f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.1f;
            button.colors = colors;
        }

        RuntimeUIPrefabBuilder.AddCraftingPreviewLayers(root);

        ItemSlot_UI slot = root.GetComponent<ItemSlot_UI>();
        if (slot != null)
        {
            if (slot.image != null)
            {
                slot.image.rectTransform.sizeDelta = new Vector2(72f, 72f);
                slot.image.color = Color.white;
                slot.image.preserveAspect = true;
                slot.image.raycastTarget = false;
            }

            if (slot.text != null)
            {
                RectTransform amountRect = slot.text.rectTransform;
                amountRect.anchorMin = new Vector2(1f, 0f);
                amountRect.anchorMax = new Vector2(1f, 0f);
                amountRect.pivot = new Vector2(1f, 0f);
                amountRect.anchoredPosition = new Vector2(-7f, 7f);
                amountRect.sizeDelta = new Vector2(42f, 24f);

                slot.text.fontSize = 20f;
                slot.text.enableAutoSizing = true;
                slot.text.fontSizeMin = 12f;
                slot.text.fontSizeMax = 20f;
                slot.text.alignment = TextAlignmentOptions.BottomRight;
                slot.text.enableWordWrapping = false;
                slot.text.raycastTarget = false;
                slot.text.color = Cream;
                AddOutline(slot.text, new Color(0.12f, 0.12f, 0.12f, 0.96f));
            }
        }

        Image ghost = FindNamedImage(root.transform, "Crafting Output Ghost");
        Image reveal = FindNamedImage(root.transform, "Crafting Output Reveal");
        if (ghost != null)
            ghost.rectTransform.sizeDelta = new Vector2(72f, 72f);
        if (reveal != null)
            reveal.rectTransform.sizeDelta = new Vector2(72f, 72f);
    }

    private static int RepairBaseSlotPreview(GameObject root)
    {
        RuntimeUIPrefabBuilder.AddCraftingPreviewLayers(root);
        return 1;
    }

    private static int EnsureCraftingOutputPreviewLayers(Transform root)
    {
        int repairedSlots = 0;
        ItemSlot_UI[] slots = root.GetComponentsInChildren<ItemSlot_UI>(true);
        foreach (ItemSlot_UI slot in slots)
        {
            if (slot == null || !slot.name.StartsWith("输出_", StringComparison.Ordinal))
                continue;

            RuntimeUIPrefabBuilder.AddCraftingPreviewLayers(slot.gameObject);
            repairedSlots++;
        }

        return repairedSlots;
    }

    private static void ValidateCraftingPreviewSlot(ItemSlot_UI slot, string prefabPath, List<string> failures)
    {
        if (slot == null)
        {
            failures.Add($"{prefabPath} 缺少 ItemSlot_UI");
            return;
        }

        Image reference = slot.image;
        Image ghost = FindNamedImage(slot.transform, "Crafting Output Ghost");
        Image reveal = FindNamedImage(slot.transform, "Crafting Output Reveal");
        string context = $"{prefabPath}/{slot.name}";

        if (reference == null)
            failures.Add($"{context} 缺少物品图标引用");
        if (ghost == null)
            failures.Add($"{context} 缺少 Crafting Output Ghost");
        if (reveal == null)
            failures.Add($"{context} 缺少 Crafting Output Reveal");
        if (reference == null || ghost == null || reveal == null)
            return;

        if (ghost.gameObject.activeSelf || reveal.gameObject.activeSelf)
            failures.Add($"{context} 预览图层默认必须隐藏");
        if (ghost.raycastTarget || reveal.raycastTarget)
            failures.Add($"{context} 预览图层不得拦截射线");
        if (!ghost.preserveAspect || !reveal.preserveAspect)
            failures.Add($"{context} 预览图层必须保持宽高比");
        if (reveal.type != Image.Type.Filled ||
            reveal.fillMethod != Image.FillMethod.Vertical ||
            reveal.fillOrigin != (int)Image.OriginVertical.Bottom)
        {
            failures.Add($"{context} Reveal 必须由下向上填充");
        }

        if (ghost.transform.parent != reference.transform.parent ||
            reveal.transform.parent != reference.transform.parent)
        {
            failures.Add($"{context} 预览图层必须与物品图标同级");
        }
    }

    private static Image FindNamedImage(Transform root, string name)
    {
        Image[] images = root.GetComponentsInChildren<Image>(true);
        foreach (Image image in images)
        {
            if (image != null && image.name == name)
                return image;
        }

        return null;
    }

    #endregion
}
