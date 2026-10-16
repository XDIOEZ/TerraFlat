using TMPro;
using FlatWorld.Localization;
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
    public Slider BatteryCharge; // 电池专用的自下而上只读电量条。
    public TMP_Text BatteryPercentage;
    public Slider GasCapacity; // 气罐以实际气压相对安全上限显示只读容量。
    public TMP_Text GasPercentage;
    public Button ActionButton;
    public Button DismantleButton; // 已安装组件的拆回入口。
    public Button CloseButton;
    public ItemSlot_UI InputSlot;
    public ItemSlot_UI OutputSlot;
    public GameObject[] ProcessingVisuals;
    public bool PreservePrefabLayout; // 工业与装备页的正文和配置行使用各自正式资产布局。

    private bool processingVisible = true; // 当前布局是否展示加工槽区。

    /// <summary>动力源和传动件只显示操作，避免留下无意义的加工槽区。</summary>
    public void SetProcessingVisible(bool visible)
    {
        processingVisible = visible;
        foreach (var visual in ProcessingVisuals) visual.SetActive(visible);
        InnerField.gameObject.SetActive(true);
        if (PreservePrefabLayout) return;

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
        if (processingVisible || PreservePrefabLayout) return;

        ((RectTransform)DismantleButton.transform).anchoredPosition = new Vector2(visible ? -86 : 0, 29);
    }
    public static void SetButtonCaption(Button button, string caption)
    {
        TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
        if (label == null) return;
        LocalizedTextBinder binder = label.GetComponent<LocalizedTextBinder>();
        if (binder != null)
        {
            binder.Configure(FlatWorldLocalizationService.UiTable, FlatWorldLocalizationService.GetUiTextKey(caption), caption);
            binder.enabled = true;
        }
        label.text = FlatWorldLocalizationService.GetUiText(caption);
    }
    #endregion

    #region 电池电量显示
    private const float BatteryStatusReserveWidth = 174f;
    private int displayedBatteryPercentage = -1;

    public void SetBatteryVisible(bool visible)
    {
        if (BatteryCharge == null)
        {
            if (visible) throw new System.InvalidOperationException(name + " 缺少正式电池电量控件。");
            return;
        }
        if (visible && BatteryPercentage == null)
            throw new System.InvalidOperationException(name + " 缺少正式电池百分比文字。");
        BatteryCharge.gameObject.SetActive(visible);
        if (PreservePrefabLayout || processingVisible) return;
        ((RectTransform)StatusScroll.transform).offsetMax = new Vector2(
            visible ? -BatteryStatusReserveWidth : -StatusPadding, -StatusPadding);
    }

    // 电量只用于显示，数值和填充共用同一份实际储能比例。
    public void SetBatteryCharge(float charge01)
    {
        charge01 = Mathf.Clamp01(charge01);
        BatteryCharge.SetValueWithoutNotify(charge01);
        int percentage = Mathf.RoundToInt(charge01 * 100f);
        if (displayedBatteryPercentage == percentage) return;
        displayedBatteryPercentage = percentage;
        BatteryPercentage.SetText("{0}%", percentage);
    }
    #endregion

    #region 气罐容量显示
    private int displayedGasPercentage = -1;

    public void SetGasCapacityVisible(bool visible)
    {
        if (GasCapacity == null)
        {
            if (visible) throw new System.InvalidOperationException(name + " 缺少正式气罐容量控件。");
            return;
        }
        if (visible && GasPercentage == null)
            throw new System.InvalidOperationException(name + " 缺少正式气罐百分比文字。");
        GasCapacity.gameObject.SetActive(visible);
        if (!visible) return;

        var statusRect = (RectTransform)StatusScroll.transform;
        statusRect.anchorMin = Vector2.zero;
        statusRect.anchorMax = Vector2.one;
        statusRect.pivot = new Vector2(.5f, .5f);
        statusRect.offsetMin = new Vector2(StatusPadding, StatusPadding);
        statusRect.offsetMax = new Vector2(-272, -StatusPadding);
    }

    // 填充最多占满罐体，超压仍保留真实百分比并标红。
    public void SetGasCapacity(float pressureRatio)
    {
        pressureRatio = Mathf.Max(0, pressureRatio);
        GasCapacity.SetValueWithoutNotify(Mathf.Clamp01(pressureRatio));
        GasCapacity.targetGraphic.color = pressureRatio > 1f
            ? new Color(.85f, .25f, .2f, 1) : new Color(.18f, .58f, .35f, 1);
        int percentage = Mathf.RoundToInt(pressureRatio * 100f);
        if (displayedGasPercentage == percentage) return;
        displayedGasPercentage = percentage;
        GasPercentage.SetText("{0}%", percentage);
    }
    #endregion
}
