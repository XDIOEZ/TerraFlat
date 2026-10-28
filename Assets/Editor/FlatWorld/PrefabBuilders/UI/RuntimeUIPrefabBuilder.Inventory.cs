// 按业务职责维护 Prefab，复用公共控件与布局实现。
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static FlatWorldEditorUiLayout;

public static partial class RuntimeUIPrefabBuilder
{
    #region 库存界面

    private static void ConfigureInventoryActionButtons(GameObject root)
    {
        Button organizeButton = GetInventoryActionButton(root, "整理", 148f, 50f);
        RectTransform organizeRect = organizeButton.GetComponent<RectTransform>();
        Button sortButton = GetInventoryActionButton(
            root,
            "排序",
            organizeRect.rect.width,
            organizeRect.rect.height);
        RectTransform sortRect = sortButton.GetComponent<RectTransform>();
        sortRect.anchorMin = organizeRect.anchorMin;
        sortRect.anchorMax = organizeRect.anchorMax;
        sortRect.pivot = organizeRect.pivot;
        sortRect.sizeDelta = organizeRect.sizeDelta;
        sortRect.anchoredPosition = organizeRect.anchoredPosition +
                                    new Vector2(-organizeRect.rect.width - 12f, 0f);

        LayoutElement sortLayout = sortButton.GetComponent<LayoutElement>() ??
                                   sortButton.gameObject.AddComponent<LayoutElement>();
        sortLayout.ignoreLayout = true;
        sortLayout.preferredWidth = organizeRect.rect.width;
        sortLayout.preferredHeight = organizeRect.rect.height;
        ConfigureButtonVisual(sortButton, false, "排序");
        ConfigureButtonVisual(organizeButton, false, "整理");
        ConfigureBagSearchClearButton(root);
        ConfigureInventoryCategoryFilter(root);
    }

    /// <summary>在行囊搜索框右侧安装手机端可直接点击的扫把清空按钮。</summary>
    private static void ConfigureBagSearchClearButton(GameObject root)
    {
        Transform searchTransform = FindTransform(root.transform, "FWUI_BagSearch");
        TMP_InputField searchInput = searchTransform != null
            ? searchTransform.GetComponent<TMP_InputField>()
            : null;
        if (searchInput == null)
            throw new MissingComponentException("UI_Bag.prefab 缺少 FWUI_BagSearch 输入框。");

        RectTransform searchRect = searchInput.GetComponent<RectTransform>();
        searchRect.anchorMin = new Vector2(0f, 1f);
        searchRect.anchorMax = new Vector2(0f, 1f);
        searchRect.anchoredPosition = new Vector2(390f, -39f);
        searchRect.sizeDelta = new Vector2(360f, 54f);

        Transform existing = searchTransform.Find("清空搜索");
        Button clearButton = existing != null ? existing.GetComponent<Button>() : null;
        if (clearButton == null)
        {
            GameObject buttonObject = CreateUIObject(
                "清空搜索",
                searchTransform,
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            clearButton = buttonObject.GetComponent<Button>();
        }

        RectTransform clearRect = clearButton.GetComponent<RectTransform>();
        clearRect.anchorMin = new Vector2(1f, 0.5f);
        clearRect.anchorMax = new Vector2(1f, 0.5f);
        clearRect.pivot = new Vector2(1f, 0.5f);
        clearRect.anchoredPosition = new Vector2(-5f, 0f);
        clearRect.sizeDelta = new Vector2(44f, 44f);

        Image background = clearButton.GetComponent<Image>() ?? clearButton.gameObject.AddComponent<Image>();
        background.sprite = null;
        background.type = Image.Type.Simple;
        background.color = SurfaceRaised;
        background.raycastTarget = true;
        clearButton.targetGraphic = background;
        clearButton.navigation = new Navigation { mode = Navigation.Mode.None };
        ConfigureButtonColors(clearButton);

        RectTransform textViewport = searchInput.textViewport;
        if (textViewport != null)
        {
            Vector2 viewportSize = textViewport.sizeDelta;
            viewportSize.x = -72f;
            textViewport.sizeDelta = viewportSize;
            Vector2 viewportPosition = textViewport.anchoredPosition;
            viewportPosition.x = -24f;
            textViewport.anchoredPosition = viewportPosition;
        }

        Transform iconTransform = clearButton.transform.Find("扫把图标");
        RectTransform iconRect;
        if (iconTransform == null)
        {
            GameObject iconObject = CreateUIObject("扫把图标", clearButton.transform);
            iconRect = iconObject.GetComponent<RectTransform>();
        }
        else
        {
            iconRect = iconTransform.GetComponent<RectTransform>();
        }

        iconRect.anchorMin = new Vector2(0.5f, 0.5f);
        iconRect.anchorMax = new Vector2(0.5f, 0.5f);
        iconRect.pivot = new Vector2(0.5f, 0.5f);
        iconRect.anchoredPosition = Vector2.zero;
        iconRect.sizeDelta = new Vector2(24f, 24f);
        iconRect.localRotation = Quaternion.Euler(0f, 0f, -34f);

        ConfigureBroomPart(iconRect, "扫把柄", new Vector2(3f, 18f), new Vector2(0f, 4f), Cream);
        ConfigureBroomPart(iconRect, "扫把头", new Vector2(14f, 8f), new Vector2(0f, -8f), Cream);
    }

    /// <summary>创建或更新扫把图标的一块纯色几何。</summary>
    private static void ConfigureBroomPart(
        RectTransform parent,
        string name,
        Vector2 size,
        Vector2 position,
        Color color)
    {
        Transform existing = parent.Find(name);
        Image image = existing != null ? existing.GetComponent<Image>() : null;
        if (image == null)
            image = CreateImage(name, parent, color);

        image.sprite = null;
        image.type = Image.Type.Simple;
        image.color = color;
        image.raycastTarget = false;
        RectTransform rect = image.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    /// <summary>获取现有行囊操作按钮，缺少时基于公共控件创建默认尺寸。</summary>
    private static Button GetInventoryActionButton(GameObject root, string buttonName, float defaultWidth, float defaultHeight)
    {
        Transform existing = FindTransform(root.transform, buttonName);
        Button button = existing != null
            ? existing.GetComponent<Button>()
            : CreateButton(buttonName, root.transform, buttonName, defaultWidth, defaultHeight, false);
        if (button == null)
            throw new MissingComponentException($"UI_Bag.prefab 的{buttonName}节点缺少 Button。");

        LayoutElement layout = button.GetComponent<LayoutElement>() ?? button.gameObject.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;
        if (existing != null)
            return button;

        RectTransform rect = button.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-22f, 8f);
        rect.sizeDelta = new Vector2(defaultWidth, defaultHeight);
        layout.preferredWidth = defaultWidth;
        layout.preferredHeight = defaultHeight;
        ConfigureButtonVisual(button, false, buttonName);
        return button;
    }

    /// <summary>仅维护制作输出预览图层；嵌入式制作槽位仍可复用这一入口。</summary>
    internal static void AddCraftingPreviewLayers(GameObject root)
    {
        ItemSlot_UI slot = root.GetComponent<ItemSlot_UI>();
        Image reference = slot != null ? slot.image : root.GetComponentInChildren<Image>(true);
        if (reference == null)
            throw new MissingReferenceException($"{root.name} 缺少物品图标 Image。");

        Image ghost = EnsurePreviewImage(root.transform, reference, "Crafting Output Ghost");
        ghost.color = new Color(1f, 1f, 1f, 0.28f);
        ghost.gameObject.SetActive(false);

        Image reveal = EnsurePreviewImage(root.transform, reference, "Crafting Output Reveal");
        reveal.color = Color.white;
        reveal.type = Image.Type.Filled;
        reveal.fillMethod = Image.FillMethod.Vertical;
        reveal.fillOrigin = (int)Image.OriginVertical.Bottom;
        reveal.fillAmount = 0f;
        reveal.gameObject.SetActive(false);

        int referenceIndex = reference.transform.GetSiblingIndex();
        ghost.transform.SetSiblingIndex(referenceIndex + 1);
        reveal.transform.SetSiblingIndex(referenceIndex + 2);
    }

    private static Image EnsurePreviewImage(Transform root, Image reference, string name)
    {
        Transform existing = FindTransform(root, name);
        Image image = existing != null ? existing.GetComponent<Image>() : null;
        if (image == null)
        {
            image = CreateImage(name, reference.transform.parent, Color.white);
            CopyRect(reference.rectTransform, image.rectTransform);
        }

        image.raycastTarget = false;
        image.preserveAspect = true;
        return image;
    }

    /// <summary>为库存槽固化手机整组放置的方形环长按进度，尺寸略大于手部槽以露出手指边缘。</summary>
    internal static void EnsureTouchLongPressProgress(Transform root, ItemSlot_UI slot)
    {
        if (slot == null)
            throw new MissingComponentException($"{root.name} 缺少 ItemSlot_UI。");

        Transform existingFrame = FindTransform(root, "LongPress Hold Frame");
        Image frame = existingFrame != null ? existingFrame.GetComponent<Image>() : null;
        if (frame == null)
            frame = CreateImage("LongPress Hold Frame", root, FlatWorldUITheme.Surface);

        RectTransform frameRect = frame.rectTransform;
        frameRect.anchorMin = new Vector2(0.5f, 0.5f);
        frameRect.anchorMax = new Vector2(0.5f, 0.5f);
        frameRect.pivot = new Vector2(0.5f, 0.5f);
        frameRect.anchoredPosition = Vector2.zero;
        frameRect.sizeDelta = new Vector2(112f, 112f);
        Color frameColor = FlatWorldUITheme.Surface;
        frameColor.a = 0.18f;
        frame.color = frameColor;
        frame.raycastTarget = false;
        frame.sprite = null;
        frame.type = Image.Type.Simple;
        frame.preserveAspect = false;

        Outline frameOutline = frame.GetComponent<Outline>() ?? frame.gameObject.AddComponent<Outline>();
        frameOutline.effectColor = FlatWorldUITheme.Border;
        frameOutline.effectDistance = FlatWorldUITheme.BorderOutlineDistance;
        frameOutline.useGraphicAlpha = true;

        Transform legacyFill = FindTransform(frame.transform, "LongPress Hold Fill");
        if (legacyFill != null && FindTransform(frame.transform, "LongPress Hold Top") == null)
            legacyFill.name = "LongPress Hold Top";

        Color edgeColor = FlatWorldUITheme.Accent;
        edgeColor.a = 0.9f;
        Image top = EnsureTouchLongPressProgressEdge(frame.transform, "LongPress Hold Top", edgeColor);
        Image right = EnsureTouchLongPressProgressEdge(frame.transform, "LongPress Hold Right", edgeColor);
        Image bottom = EnsureTouchLongPressProgressEdge(frame.transform, "LongPress Hold Bottom", edgeColor);
        Image left = EnsureTouchLongPressProgressEdge(frame.transform, "LongPress Hold Left", edgeColor);

        ConfigureTouchLongPressProgressEdge(top.rectTransform, new Vector2(0f, 0.5f), new Vector2(-52f, 52f), new Vector2(104f, 6f));
        ConfigureTouchLongPressProgressEdge(right.rectTransform, new Vector2(0.5f, 1f), new Vector2(52f, 52f), new Vector2(6f, 104f));
        ConfigureTouchLongPressProgressEdge(bottom.rectTransform, new Vector2(1f, 0.5f), new Vector2(52f, -52f), new Vector2(104f, 6f));
        ConfigureTouchLongPressProgressEdge(left.rectTransform, new Vector2(0.5f, 0f), new Vector2(-52f, -52f), new Vector2(6f, 104f));

        frame.gameObject.SetActive(false);
        frame.transform.SetAsLastSibling();
        slot.ConfigureTouchLongPressProgressVisuals(frame.gameObject, top, right, bottom, left);
    }

    /// <summary>创建或复用一条无射线的纯色方形进度边。</summary>
    private static Image EnsureTouchLongPressProgressEdge(Transform parent, string name, Color color)
    {
        Transform existing = FindTransform(parent, name);
        Image edge = existing != null ? existing.GetComponent<Image>() : null;
        if (edge == null)
            edge = CreateImage(name, parent, color);

        edge.color = color;
        edge.raycastTarget = false;
        edge.sprite = null;
        edge.type = Image.Type.Simple;
        edge.fillAmount = 1f;
        edge.preserveAspect = false;
        return edge;
    }

    /// <summary>每条边用起点 Pivot 缩放，四条边依次组成顺时针方形环。</summary>
    private static void ConfigureTouchLongPressProgressEdge(RectTransform rect, Vector2 pivot, Vector2 position, Vector2 size)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
    }

    #endregion
}
