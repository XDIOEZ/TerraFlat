using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>机械与手钻正式面板的序列化视图契约；具体面板为独立 Prefab，运行时不创建视觉层级。</summary>
public sealed class MechanicalPanelView : MonoBehaviour
{
    #region 正式面板引用
    public TMP_Text Title;
    public TMP_Text Status;
    public Button ActionButton;
    public Button CloseButton;
    public ItemSlot_UI InputSlot;
    public ItemSlot_UI OutputSlot;
    public GameObject[] ProcessingVisuals;
    /// <summary>动力源和传动件只显示操作，避免留下无意义的加工槽区。</summary>
    public void SetProcessingVisible(bool visible)
    {
        foreach (var visual in ProcessingVisuals) visual.SetActive(visible);
        if (visible) return;
        var panelRect = (RectTransform)transform;
        panelRect.sizeDelta = new Vector2(panelRect.sizeDelta.x, 250);
        var buttonRect = (RectTransform)ActionButton.transform;
        buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(.5f, .5f);
        buttonRect.anchoredPosition = new Vector2(0, -15);
    }
    #endregion
}
