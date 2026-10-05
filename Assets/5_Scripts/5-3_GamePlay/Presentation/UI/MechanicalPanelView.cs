using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>机械与手钻正式面板的序列化视图契约；具体面板为独立 Prefab，运行时不创建视觉层级。</summary>
public sealed class MechanicalPanelView : MonoBehaviour
{
    #region 正式面板引用
    private const float FullPanelHeight = 480f; // 完整加工面板为状态与按钮分出独立底栏。
    private const float CompactPanelHeight = 250f; // 纯机械状态面板只保留状态卡和操作区。
    private const float StatusPadding = 12f; // 状态区始终留在正文框内，不再按整个窗口居中。

    public TMP_Text Title;
    public TMP_Text Status;
    public ScrollRect StatusScroll; // 信息较多时允许纵向滚动，避免状态文字继续横向挤成一行。
    public RectTransform InnerField; // 加工内容底板，随面板模式同步缩放。
    public Button ActionButton;
    public Button DismantleButton; // 已安装组件的拆回入口。
    public Button CloseButton;
    public ItemSlot_UI InputSlot;
    public ItemSlot_UI OutputSlot;
    public GameObject[] ProcessingVisuals;

    private bool processingVisible = true; // 当前布局是否展示加工槽区。

    /// <summary>动力源和传动件只显示操作，避免留下无意义的加工槽区。</summary>
    public void SetProcessingVisible(bool visible)
    {
        processingVisible = visible;
        foreach (var visual in ProcessingVisuals) visual.SetActive(visible);
        InnerField.gameObject.SetActive(true);

        var panelRect = (RectTransform)transform;
        var statusRect = (RectTransform)StatusScroll.transform;
        var buttonRect = (RectTransform)ActionButton.transform;
        var dismantleRect = (RectTransform)DismantleButton.transform;
        panelRect.sizeDelta = new Vector2(panelRect.sizeDelta.x, visible ? FullPanelHeight : CompactPanelHeight);
        InnerField.anchorMin = Vector2.zero;
        InnerField.anchorMax = Vector2.one;
        InnerField.pivot = new Vector2(0.5f, 0.5f);
        InnerField.offsetMin = new Vector2(14, 64);
        InnerField.offsetMax = new Vector2(-14, -72);

        if (visible)
        {
            statusRect.anchorMin = Vector2.zero;
            statusRect.anchorMax = new Vector2(1, 0);
            statusRect.pivot = new Vector2(0.5f, 0);
            statusRect.anchoredPosition = new Vector2(0, StatusPadding);
            statusRect.sizeDelta = new Vector2(-2 * StatusPadding, 64);

            buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(1, 0);
            buttonRect.pivot = new Vector2(1, 0.5f);
            buttonRect.anchoredPosition = new Vector2(-24, 29);
            buttonRect.sizeDelta = new Vector2(160, 46);

            dismantleRect.anchorMin = dismantleRect.anchorMax = new Vector2(1, 0);
            dismantleRect.pivot = new Vector2(1, 0.5f);
            dismantleRect.anchoredPosition = new Vector2(-196, 29);
            dismantleRect.sizeDelta = new Vector2(160, 46);
            SetActionVisible(false);
            return;
        }

        statusRect.anchorMin = Vector2.zero;
        statusRect.anchorMax = Vector2.one;
        statusRect.pivot = new Vector2(0.5f, 0.5f);
        statusRect.offsetMin = new Vector2(StatusPadding, StatusPadding);
        statusRect.offsetMax = new Vector2(-StatusPadding, -StatusPadding);

        buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0.5f, 0);
        buttonRect.pivot = new Vector2(0.5f, 0.5f);
        buttonRect.sizeDelta = new Vector2(160, 46);
        buttonRect.anchoredPosition = new Vector2(86, 29);

        dismantleRect.anchorMin = dismantleRect.anchorMax = new Vector2(0.5f, 0);
        dismantleRect.pivot = new Vector2(0.5f, 0.5f);
        dismantleRect.sizeDelta = new Vector2(160, 46);
        dismantleRect.anchoredPosition = new Vector2(0, 29);
        SetActionVisible(false);
    }

    /// <summary>操作与拆回按钮共用独立底栏，显隐不再挤压或移动正文。</summary>
    public void SetActionVisible(bool visible)
    {
        ActionButton.gameObject.SetActive(visible);
        if (processingVisible) return;

        ((RectTransform)DismantleButton.transform).anchoredPosition = new Vector2(visible ? -86 : 0, 29);
    }
    #endregion
}
