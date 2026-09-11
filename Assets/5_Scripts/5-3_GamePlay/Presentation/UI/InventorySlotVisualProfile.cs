using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 库存面板可选的槽位视觉配置。
/// 面板通过挂载该组件覆盖通用 UI_Slot 的外观，避免为了单个背包主题修改所有工作台、装备栏和快捷栏。
/// 只负责 Sprite、尺寸与文字表现，不参与库存事务、拖拽、选择或数据同步。
/// </summary>
public sealed class InventorySlotVisualProfile : MonoBehaviour
{
    #region 槽位素材

    [Tooltip("普通状态槽位 Sprite")]
    [SerializeField] private Sprite normalSprite;

    [Tooltip("悬停状态槽位 Sprite")]
    [SerializeField] private Sprite highlightedSprite;

    [Tooltip("按下状态槽位 Sprite")]
    [SerializeField] private Sprite pressedSprite;

    [Tooltip("选中状态槽位 Sprite")]
    [SerializeField] private Sprite selectedSprite;

    [Tooltip("禁用状态槽位 Sprite")]
    [SerializeField] private Sprite disabledSprite;

    #endregion

    #region 尺寸与文字

    [Tooltip("槽位根节点参考尺寸；GridLayoutGroup 仍可覆盖最终布局尺寸")]
    [SerializeField] private Vector2 slotSize = new Vector2(80f, 80f);

    [Tooltip("槽位内物品图标尺寸")]
    [SerializeField] private Vector2 iconSize = new Vector2(58f, 58f);

    [Tooltip("堆叠数量文字颜色")]
    [SerializeField] private Color amountColor = new Color32(238, 242, 242, 255);

    [Tooltip("堆叠数量描边颜色")]
    [SerializeField] private Color amountOutlineColor = new Color32(20, 29, 37, 245);

    #endregion

    /// <summary>由编辑器构建器写入正式素材与尺寸。</summary>
    public void Configure(
        Sprite normal,
        Sprite highlighted,
        Sprite pressed,
        Sprite selected,
        Sprite disabled,
        Vector2 configuredSlotSize,
        Vector2 configuredIconSize)
    {
        normalSprite = normal;
        highlightedSprite = highlighted;
        pressedSprite = pressed;
        selectedSprite = selected;
        disabledSprite = disabled;
        slotSize = configuredSlotSize;
        iconSize = configuredIconSize;
    }

    /// <summary>把面板主题应用到一个实际槽位；不改 ItemSlot_UI 的事件和业务引用。</summary>
    public void Apply(ItemSlot_UI slot)
    {
        if (slot == null || normalSprite == null)
            return;

        RectTransform slotRect = slot.GetComponent<RectTransform>();
        if (slotRect != null)
            slotRect.sizeDelta = slotSize;

        Image background = slot.GetComponent<Image>();
        if (background != null)
        {
            background.sprite = normalSprite;
            background.type = Image.Type.Sliced;
            background.preserveAspect = false;
            background.color = Color.white;
            background.pixelsPerUnitMultiplier = 1f;
        }

        Button button = slot.GetComponent<Button>();
        if (button != null && background != null)
        {
            button.targetGraphic = background;
            button.transition = Selectable.Transition.SpriteSwap;
            SpriteState state = button.spriteState;
            state.highlightedSprite = highlightedSprite != null ? highlightedSprite : normalSprite;
            state.pressedSprite = pressedSprite != null ? pressedSprite : normalSprite;
            state.selectedSprite = selectedSprite != null ? selectedSprite : state.highlightedSprite;
            state.disabledSprite = disabledSprite != null ? disabledSprite : normalSprite;
            button.spriteState = state;
        }

        if (slot.image != null)
        {
            slot.image.rectTransform.sizeDelta = iconSize;
            slot.image.preserveAspect = true;
            slot.image.raycastTarget = false;
        }

        ConfigureAmountText(slot.text);
        ResizePreviewLayer(slot.transform, "Crafting Output Ghost");
        ResizePreviewLayer(slot.transform, "Crafting Output Reveal");
    }

    /// <summary>统一数量文字，保证在深色像素槽位上仍清晰可读。</summary>
    private void ConfigureAmountText(TMP_Text amount)
    {
        if (amount == null)
            return;

        RectTransform rect = amount.rectTransform;
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-7f, 7f);
        rect.sizeDelta = new Vector2(38f, 22f);

        amount.fontSize = 18f;
        amount.enableAutoSizing = true;
        amount.fontSizeMin = 11f;
        amount.fontSizeMax = 18f;
        amount.alignment = TextAlignmentOptions.BottomRight;
        amount.enableWordWrapping = false;
        amount.raycastTarget = false;
        amount.color = amountColor;

        Outline outline = amount.GetComponent<Outline>();
        if (outline != null)
        {
            outline.effectColor = amountOutlineColor;
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = true;
        }
    }

    /// <summary>制作预览层与物品图标保持同一可视范围。</summary>
    private void ResizePreviewLayer(Transform slotRoot, string layerName)
    {
        Transform child = slotRoot.Find(layerName);
        RectTransform rect = child != null ? child.GetComponent<RectTransform>() : null;
        if (rect != null)
            rect.sizeDelta = iconSize;
    }
}
