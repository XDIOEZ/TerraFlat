using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>机械与手钻正式面板的序列化视图契约；具体面板为独立 Prefab，运行时不创建视觉层级。</summary>
public sealed class MechanicalPanelView : MonoBehaviour
{
    #region 正式面板引用
    private const float FullPanelHeight = 480f; // 完整加工面板为状态与按钮分出独立底栏。
    private const float CompactPanelHeight = 250f; // 纯机械状态面板只保留状态卡和操作区。
    private const float FullInnerFieldHeight = 280f; // 加工槽区沿用正式加工面板高度。
    private const float CompactInnerFieldHeight = 100f; // 紧凑面板内容底板贴合状态卡。

    public TMP_Text Title;
    public TMP_Text Status;
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
        RectTransform statusRect = Status.rectTransform;
        var buttonRect = (RectTransform)ActionButton.transform;
        var dismantleRect = (RectTransform)DismantleButton.transform;
        if (visible)
        {
            panelRect.sizeDelta = new Vector2(panelRect.sizeDelta.x, FullPanelHeight);
            InnerField.sizeDelta = new Vector2(InnerField.sizeDelta.x, FullInnerFieldHeight);

            statusRect.anchorMin = statusRect.anchorMax = new Vector2(0, 0);
            statusRect.pivot = new Vector2(0, 0.5f);
            statusRect.anchoredPosition = new Vector2(24, 83);
            statusRect.sizeDelta = new Vector2(598, 42);

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

        panelRect.sizeDelta = new Vector2(panelRect.sizeDelta.x, CompactPanelHeight);
        InnerField.sizeDelta = new Vector2(InnerField.sizeDelta.x, CompactInnerFieldHeight);
        statusRect.anchorMin = statusRect.anchorMax = new Vector2(0.5f, 0.5f);
        statusRect.pivot = new Vector2(0.5f, 0.5f);
        statusRect.sizeDelta = new Vector2(580, 36);

        buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(.5f, .5f);
        buttonRect.pivot = new Vector2(0.5f, 0.5f);
        buttonRect.sizeDelta = new Vector2(172, 48);
        buttonRect.anchoredPosition = new Vector2(0, -39);

        dismantleRect.anchorMin = dismantleRect.anchorMax = new Vector2(0.5f, 0);
        dismantleRect.pivot = new Vector2(0.5f, 0.5f);
        dismantleRect.sizeDelta = new Vector2(160, 46);
        dismantleRect.anchoredPosition = new Vector2(0, 29);
        SetActionVisible(false);
    }

    /// <summary>按操作是否可用切换按钮，并为紧凑面板留出清晰的状态与操作行。</summary>
    public void SetActionVisible(bool visible)
    {
        ActionButton.gameObject.SetActive(visible);
        if (processingVisible) return;

        Status.rectTransform.anchoredPosition = new Vector2(0, visible ? 12 : -17);
    }
    #endregion
}
