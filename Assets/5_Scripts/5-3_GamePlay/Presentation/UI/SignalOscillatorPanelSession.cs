using System;
using System.Globalization;
using FlatWorld.Localization;
using TMPro;
using UnityEngine.UI;

/// <summary>复用工业面板设置真实振荡频率，显示量化后的权威读数。</summary>
public sealed class SignalOscillatorPanelSession : MachinePanelSession
{
    #region 频率设置与会话清理
    private readonly MachineSignalOscillatorLogic oscillator;
    private readonly MechanicalPanelView view;
    private readonly TMP_InputField frequency;
    private readonly Button apply;
    private string feedback;
    private int displayedHalfPeriodSteps;
    private bool frequencyPending;
    private uint pendingToken;
    private string pendingArgument;

    public SignalOscillatorPanelSession(MachineEntity entity) : base(entity, bindDefaultInventories: false)
    {
        oscillator = (MachineSignalOscillatorLogic)entity.Logic;
        BasePanel panel = CreatePanel(oscillator.PanelPrefab);
        view = panel.GetComponent<MechanicalPanelView>();
        apply = panel.GetButton("FWUI_ApplyFluidSettings");
        foreach (TMP_InputField field in panel.GetComponentsInChildren<TMP_InputField>(true))
        {
            bool selected = field.name == "FWUI_FluidSettingPrimary";
            field.gameObject.SetActive(selected);
            if (selected) frequency = field;
        }
        if (view == null || apply == null || frequency == null)
            throw new InvalidOperationException("信号振荡器面板缺少正式控件。");
        view.SetProcessingVisible(false); view.SetFluidLayout(false, false, false); view.SetGasCapacityVisible(false);
        view.SetActionVisible(true); BindActionButton(view.ActionButton);
        panel.GetButton("FWUI_FluidPhase").gameObject.SetActive(false);
        panel.GetButton("FWUI_RotateFluid").gameObject.SetActive(false);
        frequency.contentType = TMP_InputField.ContentType.DecimalNumber; frequency.characterLimit = 8;
        if (frequency.placeholder is TMP_Text placeholder) placeholder.text = "Hz";
        apply.onClick.AddListener(ApplyFrequency); view.CloseButton.onClick.AddListener(Close);
        MachineWorld.OperationCompleted += OperationCompleted;
        panel.SetGameplayInputBlocking(false); panel.PrepareForGamepadNavigation(closeOnCancel: true);
    }

    protected override void OnOpened() { feedback = null; ShowActualFrequency(); }
    private void ShowActualFrequency()
    {
        displayedHalfPeriodSteps = oscillator.State.HalfPeriodSteps;
        frequency.SetTextWithoutNotify(oscillator.FrequencyHz.ToString("0.###", CultureInfo.InvariantCulture));
    }
    private void ApplyFrequency()
    {
        if (frequencyPending) return;
        feedback = null;
        frequencyPending = true; pendingArgument = frequency.text;
        if (!MachineWorld.RequestOperation(Entity, "oscillator.frequency", pendingArgument, Actor, out pendingToken) && frequencyPending)
        { frequencyPending = false; feedback = "请求未发出，请重试"; }
        Refresh();
    }
    private void OperationCompleted(MachineOperationReceipt receipt)
    {
        if (!frequencyPending || receipt.Token != pendingToken || receipt.EntityId != Entity.Id ||
            receipt.Operation != "oscillator.frequency" || receipt.Argument != pendingArgument) return;
        frequencyPending = false;
        feedback = !receipt.Confirmed ? "设置结果未确认，请重试" : receipt.Accepted ? "设置已更新" : "设备未接受此操作";
        if (receipt.Confirmed && receipt.Accepted) ShowActualFrequency();
        Refresh();
    }
    protected override void RefreshPresentation()
    {
        if (displayedHalfPeriodSteps != oscillator.State.HalfPeriodSteps) ShowActualFrequency();
        frequency.interactable = apply.interactable = !frequencyPending;
        view.Title.text = Entity.Definition.Content.Definition.DisplayName;
        float step = MachineCatalog.Settings.TickSeconds;
        view.Status.text = oscillator.Status + "\n" + FlatWorldLocalizationService.GetUiFormat(
            "频率范围 {0:0.###}～{1:0.###} Hz", 1f / (2f * MachineSignalOscillatorState.MaximumHalfPeriodSteps * step), 1f / (2f * step)) +
            (feedback == null ? "" : "\n" + FlatWorldLocalizationService.GetUiText(feedback));
        MechanicalPanelView.SetButtonCaption(apply, "设置频率");
        MechanicalPanelView.SetButtonCaption(view.ActionButton, oscillator.State.Enabled ? "停止振荡" : "启动振荡");
    }
    protected override void DisposePresentation()
    {
        MachineWorld.OperationCompleted -= OperationCompleted;
        if (view != null) view.CloseButton.onClick.RemoveListener(Close);
        if (apply != null) apply.onClick.RemoveListener(ApplyFrequency);
    }
    #endregion
}
