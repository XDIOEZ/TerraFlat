using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FlatWorld.Localization;
using FlatWorld.AIECS.Gameplay;
using FlatWorld.Gameplay.Events;
using FlatWorld.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.Events;
using UnityEngine.UI;

public sealed partial class GMReflectionConsole
{
    #region GM 纵向滚动容器

    // 只共用视口与内容节点骨架，各列表继续配置自己的边距、布局和滚动行为。
    private static GameObject CreateScrollViewportWithTopAnchoredContent(
        GameObject scrollRoot, ScrollRect scroll, Vector2 offsetMin, Vector2 offsetMax)
    {
        GameObject viewport = CreateUiObject("Viewport", scrollRoot.transform);
        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = offsetMin;
        viewportRect.offsetMax = offsetMax;
        viewport.AddComponent<RectMask2D>();
        scroll.viewport = viewportRect;

        GameObject content = CreateUiObject("Content", viewport.transform);
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        return content;
    }

    private static Transform ConfigureVerticalScroll(GameObject root, float inset, out ScrollRect scroll)
    {
        scroll = root.AddComponent<ItemStepScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 28f;

        GameObject content = CreateScrollViewportWithTopAnchoredContent(root, scroll,
            new Vector2(inset, inset), new Vector2(-inset, -inset));
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.sizeDelta = Vector2.zero;

        VerticalLayoutGroup contentLayout = content.AddComponent<VerticalLayoutGroup>();
        contentLayout.padding = new RectOffset(14, 14, 12, 12);
        contentLayout.spacing = 9f;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;

        ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = contentRect;
        return content.transform;
    }

    // 多个调试列表共用自动隐藏的纵向滚动条外观。
    private static Scrollbar CreateVerticalAutoHideScrollbar(
        GameObject root,
        ScrollRect scroll,
        Vector2 offsetMin,
        Vector2 offsetMax)
    {
        GameObject scrollbarObject = CreateUiObject("Scrollbar", root.transform);
        RectTransform scrollbarRect = scrollbarObject.GetComponent<RectTransform>();
        scrollbarRect.anchorMin = new Vector2(1f, 0f);
        scrollbarRect.anchorMax = new Vector2(1f, 1f);
        scrollbarRect.pivot = new Vector2(1f, 0.5f);
        scrollbarRect.offsetMin = offsetMin;
        scrollbarRect.offsetMax = offsetMax;
        scrollbarObject.AddComponent<Image>().color = GmSurfaceLow;

        GameObject slidingArea = CreateUiObject("Sliding Area", scrollbarObject.transform);
        RectTransform slidingRect = slidingArea.GetComponent<RectTransform>();
        slidingRect.anchorMin = Vector2.zero;
        slidingRect.anchorMax = Vector2.one;
        slidingRect.offsetMin = new Vector2(2f, 2f);
        slidingRect.offsetMax = new Vector2(-2f, -2f);

        GameObject handle = CreateUiObject("Handle", slidingArea.transform);
        RectTransform handleRect = handle.GetComponent<RectTransform>();
        handleRect.anchorMin = Vector2.zero;
        handleRect.anchorMax = Vector2.one;
        handleRect.offsetMin = Vector2.zero;
        handleRect.offsetMax = Vector2.zero;
        Image handleImage = handle.AddComponent<Image>();
        handleImage.color = GmSelection;

        Scrollbar scrollbar = scrollbarObject.AddComponent<Scrollbar>();
        scrollbar.handleRect = handleRect;
        scrollbar.targetGraphic = handleImage;
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        return scrollbar;
    }

    #endregion

    #region 分页共用控件

    private Transform CreateActionGrid(
        Transform parent,
        int columns,
        float cellWidth,
        float cellHeight,
        int itemCount)
    {
        GameObject gridObject = CreateUiObject("Action Grid", parent);
        GridLayoutGroup grid = gridObject.AddComponent<GridLayoutGroup>();
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = columns;
        grid.cellSize = new Vector2(cellWidth, cellHeight);
        grid.spacing = new Vector2(8f, 8f);
        grid.childAlignment = TextAnchor.UpperLeft;
        gmResponsiveGrids.Add(new GmResponsiveGrid
        {
            Grid = grid,
            MaxColumns = Mathf.Max(1, columns),
            CellHeight = cellHeight
        });
        SetGridHeight(gridObject.transform, itemCount, columns, cellHeight, 8f);
        return gridObject.transform;
    }

    private static void SetGridHeight(
        Transform grid,
        int itemCount,
        int columns,
        float cellHeight,
        float spacing)
    {
        int rows = itemCount > 0 ? Mathf.CeilToInt(itemCount / (float)columns) : 0;
        float height = rows > 0 ? rows * cellHeight + (rows - 1) * spacing : 0f;
        LayoutElement layout = grid.GetComponent<LayoutElement>() ?? grid.gameObject.AddComponent<LayoutElement>();
        layout.preferredHeight = height;
        layout.minHeight = height;
    }

    private static void AddPageIntro(Transform parent, string title, string description)
    {
        TextMeshProUGUI heading = CreateText(parent, title, 18f, GmTextPrimary);
        heading.fontStyle = FontStyles.Bold;
        heading.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

        TextMeshProUGUI paragraph = CreateText(parent, description.Trim(), 12f, GmTextSecondary);
        paragraph.enableWordWrapping = true;
        paragraph.overflowMode = TextOverflowModes.Ellipsis;
        paragraph.gameObject.AddComponent<LayoutElement>().preferredHeight = 32f;
    }

    private static TextMeshProUGUI AddPageHint(Transform parent, string value, float height)
    {
        TextMeshProUGUI hint = CreateText(parent, value, 12f, GmTextSecondary);
        hint.enableWordWrapping = true;
        hint.overflowMode = TextOverflowModes.Ellipsis;
        hint.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
        return hint;
    }

    private Button CreateSearchableButton(
        Transform parent,
        GmPageId pageId,
        string label,
        string keywords,
        UnityAction action,
        float height = 36f)
    {
        Button button = CreateButton(parent, label, action, 0f, height);
        RegisterSearchEntry(pageId, label, keywords, button.transform as RectTransform);
        return button;
    }

    private void CreatePlayerMoveSpeedControl(Transform parent)
    {
        GameObject row = CreateUiObject("Player Move Speed Multiplier", parent);
        Image background = row.AddComponent<Image>();
        background.color = GmSurface;
        Outline outline = row.AddComponent<Outline>();
        StyleGmOutline(outline);

        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(6, 6, 3, 3);
        layout.spacing = 5f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        TextMeshProUGUI label = CreateText(
            row.transform,
            "移速倍率",
            12f,
            GmTextPrimary);
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;
        label.gameObject.AddComponent<LayoutElement>().preferredWidth = 78f;

        playerMoveSpeedInput = CreateInputField(row.transform, "倍率", 84f, false);
        playerMoveSpeedInput.contentType = TMP_InputField.ContentType.DecimalNumber;
        playerMoveSpeedInput.characterLimit = 7;
        playerMoveSpeedInput.textComponent.alignment = TextAlignmentOptions.Center;
        playerMoveSpeedInput.onSubmit.AddListener(_ => ApplyPlayerMoveSpeedInput());

        playerMoveSpeedApplyButton = CreateButton(
            row.transform,
            "应用",
            ApplyPlayerMoveSpeedInput,
            60f,
            30f);

        RegisterSearchEntry(
            GmPageId.Player,
            "玩家移速倍率",
            "玩家 移动速度 跑图 speed multiplier 倍率",
            row.transform as RectTransform);
    }

    private void CreateChunkLoadSpeedControl(Transform parent)
    {
        GameObject row = CreateUiObject("Chunk Load Speed Multiplier", parent);
        Image background = row.AddComponent<Image>();
        background.color = GmSurface;
        Outline outline = row.AddComponent<Outline>();
        StyleGmOutline(outline);

        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(6, 6, 3, 3);
        layout.spacing = 5f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        TextMeshProUGUI label = CreateText(
            row.transform,
            "加载倍率",
            12f,
            GmTextPrimary);
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;
        label.gameObject.AddComponent<LayoutElement>().preferredWidth = 60f;

        chunkLoadSpeedInput = CreateInputField(row.transform, "倍率/无限", 58f, false);
        chunkLoadSpeedInput.contentType = TMP_InputField.ContentType.Standard;
        chunkLoadSpeedInput.characterLimit = 12;
        chunkLoadSpeedInput.textComponent.alignment = TextAlignmentOptions.Center;
        chunkLoadSpeedInput.onSubmit.AddListener(_ => ApplyChunkLoadSpeedInput());

        chunkLoadSpeedApplyButton = CreateButton(
            row.transform,
            "应用",
            ApplyChunkLoadSpeedInput,
            48f,
            30f);

        chunkLoadSpeedUnlimitedButton = CreateButton(
            row.transform,
            "无限",
            ToggleUnlimitedChunkLoadSpeed,
            54f,
            30f);

        RegisterSearchEntry(
            GmPageId.World,
            "区块加载倍率",
            "区块 加载 生成 speed multiplier 倍率 无限 无限制",
            row.transform as RectTransform);
    }

    #endregion

}
