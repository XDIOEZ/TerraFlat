using UnityEngine;

/// <summary>统一管理背包与制作面板首次打开时的默认布局。</summary>
public static class InventoryPanelLayout
{
    public const float PanelMargin = 18f;
    private const float DefaultBagWidth = 1004f;
    private const float FullScreenCraftingThreshold = 0.65f;

    /// <summary>首次摆放背包时按真实快捷栏上沿对齐，所有坐标统一到安全区父节点。</summary>
    public static void ApplyDefaultBagPosition(RectTransform panel, RectTransform hotbar = null)
    {
        if (panel == null || panel.parent is not RectTransform parent)
            return;

        Canvas.ForceUpdateCanvases();
        Vector3[] corners = new Vector3[4];
        panel.GetWorldCorners(corners);
        Vector3 bottomLeft = parent.InverseTransformPoint(corners[0]);
        Vector3 topRight = parent.InverseTransformPoint(corners[2]);
        float bottom = parent.rect.yMax - (topRight.y - bottomLeft.y);
        if (hotbar != null)
        {
            hotbar.GetWorldCorners(corners);
            bottom = Mathf.Max(parent.InverseTransformPoint(corners[1]).y,
                parent.InverseTransformPoint(corners[2]).y);
            bottom = Mathf.Min(bottom, parent.rect.yMax - (topRight.y - bottomLeft.y));
            bottom = Mathf.Max(bottom, parent.rect.yMin);
        }
        panel.anchoredPosition += new Vector2(parent.rect.xMin - bottomLeft.x, bottom - bottomLeft.y);
    }

    /// <summary>把制作面板放到画布右侧，与背包保持统一间距。</summary>
    public static void ApplyDefaultCraftingPosition(RectTransform panel)
    {
        SetCraftingPosition(panel);
    }

    /// <summary>按背包与制作面板的组合宽度计算制作面板位置，窄屏时回退到屏幕右侧。</summary>
    private static void SetCraftingPosition(RectTransform panel)
    {
        if (panel == null)
            return;

        RectTransform parent = panel.parent as RectTransform;
        if (parent == null)
            return;

        Canvas.ForceUpdateCanvases();
        float canvasWidth = parent.rect.width > 0f ? parent.rect.width : Screen.width;
        float panelWidth = panel.rect.width > 0f ? panel.rect.width : panel.sizeDelta.x;
        if (panelWidth >= canvasWidth * FullScreenCraftingThreshold)
        {
            panel.anchoredPosition = new Vector2(0f, panel.anchoredPosition.y);
            return;
        }

        float bagWidth = DefaultBagWidth;
        float craftingWidth = panelWidth;
        float pairWidth = bagWidth + PanelMargin + craftingWidth;
        float availableWidth = Mathf.Max(0f, canvasWidth - PanelMargin * 2f);
        float x;
        if (pairWidth <= availableWidth)
        {
            float pairLeft = -pairWidth * 0.5f;
            x = pairLeft + bagWidth + PanelMargin + craftingWidth * 0.5f;
        }
        else
        {
            float halfWidth = Mathf.Max(0f, canvasWidth * 0.5f - PanelMargin - panelWidth * 0.5f);
            x = halfWidth;
        }

        panel.anchoredPosition = new Vector2(x, panel.anchoredPosition.y);
    }
}
