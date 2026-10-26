using TMPro;
using UnityEngine;

/// <summary>只维护正式飞船高度尺 Prefab 的读数、指针和安全区布局。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class ShipFlightHudView : MonoBehaviour
{
    #region 正式视觉引用
    [SerializeField] private RectTransform pointer;
    [SerializeField] private TMP_Text titleText, heightText, hintText;
    [SerializeField] private TMP_Text[] tickTexts;
    [SerializeField] private UnityEngine.UI.Graphic[] pointerGraphics;
    [SerializeField] private Color normalColor = new(.9333333f, .9333333f, .9333333f, 1f);
    [SerializeField] private Color attentionColor = new(.8431373f, .772549f, .4156863f, 1f);
    [SerializeField] private Vector2 referenceSize = new(240f, 460f);
    private RectTransform viewRect;
    private string lastHeight, lastHint;
    private bool lastAttention;
    private readonly Vector3[] controlCorners = new Vector3[4];
    public bool IsReady => pointer != null && titleText != null && heightText != null && hintText != null && tickTexts?.Length == 5;

    public void Configure(RectTransform valuePointer, TMP_Text title, TMP_Text height, TMP_Text hint,
        TMP_Text[] ticks, UnityEngine.UI.Graphic[] indicators, Color normal, Color attention)
    {
        pointer = valuePointer; titleText = title; heightText = height; hintText = hint;
        tickTexts = ticks; pointerGraphics = indicators; normalColor = normal; attentionColor = attention;
    }
    #endregion

    #region 读数与安全区
    public void SetLabels(string title, string[] ticks)
    {
        if (!IsReady) return;
        titleText.text = title;
        for (int i = 0; i < tickTexts.Length; i++) tickTexts[i].text = ticks[i];
    }

    public void SetReadout(string height, string hint, float normalizedHeight, bool attention)
    {
        if (!IsReady) return;
        if (lastHeight != height) heightText.text = lastHeight = height;
        if (lastHint != hint) hintText.text = lastHint = hint;
        Vector2 anchor = new(0f, Mathf.Clamp01(normalizedHeight));
        pointer.anchorMin = pointer.anchorMax = anchor;
        if (lastAttention != attention || heightText.color != (attention ? attentionColor : normalColor))
        {
            Color color = attention ? attentionColor : normalColor;
            heightText.color = color;
            hintText.color = attentionColor;
            if (pointerGraphics != null)
                foreach (UnityEngine.UI.Graphic graphic in pointerGraphics) if (graphic != null) graphic.color = color;
            lastAttention = attention;
        }
    }

    // 先为顶部信息和底部触控留出空间，缩放整组视觉避免刻度与提示互相挤压。
    public void FitToSafeArea(RectTransform safeArea, float protectedTop, RectTransform lowerControl,
        RectTransform statusBars = null)
    {
        if (safeArea == null) return;
        viewRect ??= GetComponent<RectTransform>();
        float top = Mathf.Clamp(protectedTop, 1f, safeArea.rect.height - 16f);
        float bottom = 24f;
        float availableHeight = Mathf.Max(1f, top - bottom);
        float scale = Mathf.Min(1f, availableHeight / referenceSize.y,
            Mathf.Max(1f, safeArea.rect.width - 32f) / referenceSize.x);
        float x = Mathf.Min(320f, Mathf.Max(16f, safeArea.rect.width - referenceSize.x * scale - 16f));
        void AvoidReservedControlRectangle(RectTransform reserved)
        {
            if (reserved == null || !reserved.gameObject.activeInHierarchy) return;
            reserved.GetWorldCorners(controlCorners);
            float minX = float.PositiveInfinity, maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
            foreach (Vector3 corner in controlCorners)
            {
                Vector3 point = safeArea.InverseTransformPoint(corner);
                minX = Mathf.Min(minX, point.x - safeArea.rect.xMin);
                maxX = Mathf.Max(maxX, point.x - safeArea.rect.xMin);
                maxY = Mathf.Max(maxY, point.y - safeArea.rect.yMin);
            }
            if (maxX + 16f > x && minX - 16f < x + referenceSize.x * scale)
            {
                if (maxX + 32f + referenceSize.x * scale <= safeArea.rect.width) x = maxX + 16f;
                else
                {
                    bottom = Mathf.Clamp(maxY + 16f, 24f, Mathf.Max(24f, top - 1f));
                    scale = Mathf.Min(scale, Mathf.Max(1f, top - bottom) / referenceSize.y);
                }
            }
        }
        // 触控与生存状态条共用真实矩形避让，移动或缩放面板后不再覆盖落地提示。
        AvoidReservedControlRectangle(lowerControl);
        AvoidReservedControlRectangle(statusBars);
        viewRect.anchorMin = viewRect.anchorMax = Vector2.zero;
        viewRect.pivot = new Vector2(0f, .5f);
        viewRect.sizeDelta = referenceSize;
        viewRect.localScale = Vector3.one * scale;
        viewRect.anchoredPosition = new Vector2(x, (top + bottom) * .5f);
    }
    #endregion
}
