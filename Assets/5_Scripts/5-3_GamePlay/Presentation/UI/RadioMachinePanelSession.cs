using System;
using System.Globalization;
using FlatWorld.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>无线机器复用正式工业面板，所有操作仍走机器权威命令。</summary>
public sealed class RadioMachinePanelSession : MachinePanelSession
{
    #region 设备配置与广播
    private static readonly string[] messageIds = { "distress", "warning", "announcement", null };
    private static readonly string[] messageLabels = { "求救", "预警", "通告", "文字" };
    private readonly MachineRadioDeviceLogic radio;
    private readonly BasePanel panel;
    private readonly MechanicalPanelView view;
    private readonly TMP_InputField channel;
    private readonly Button apply, mode, broadcast;
    private int messageIndex;
    private string feedback;
    private Item textSender;

    public static IMachinePanelSession CreateForMachine(MachineEntity entity) => new RadioMachinePanelSession(entity);

    private RadioMachinePanelSession(MachineEntity entity) : base(entity, bindDefaultInventories: false)
    {
        radio = entity.Logic as MachineRadioDeviceLogic ?? throw new InvalidOperationException("无线电设备逻辑缺失。");
        panel = CreatePanel(GameRes.Instance.GetPrefab("UI_IndustrialFluid"));
        view = panel.GetComponent<MechanicalPanelView>() ?? throw new InvalidOperationException("无线电设备面板缺少控件。");
        channel = RadioTransceiverPanelSession.FindField(panel, "FWUI_FluidSettingPrimary");
        apply = panel.GetButton("FWUI_ApplyFluidSettings");
        mode = panel.GetButton("FWUI_FluidPhase");
        broadcast = panel.GetButton("FWUI_RotateFluid");
        if (channel == null || apply == null || mode == null || broadcast == null)
            throw new InvalidOperationException("无线电设备面板缺少配置控件。");
        view.SetProcessingVisible(false);
        view.SetFluidLayout(false, false, false);
        view.SetGasCapacityVisible(false);
        view.SetActionVisible(true);
        foreach (TMP_InputField field in panel.GetComponentsInChildren<TMP_InputField>(true)) field.gameObject.SetActive(field == channel);
        channel.contentType = TMP_InputField.ContentType.IntegerNumber; channel.characterLimit = 2;
        if (channel.placeholder is TMP_Text placeholder) placeholder.text = "1–16";
        view.ActionButton.onClick.AddListener(TogglePower); view.CloseButton.onClick.AddListener(Close);
        apply.onClick.AddListener(ApplyChannel); mode.onClick.AddListener(ChangeModeOrMessage); broadcast.onClick.AddListener(Broadcast);
        broadcast.gameObject.SetActive(radio is MachineRadioStationLogic);
        MachineWorld.OperationCompleted += OperationCompleted;
        panel.SetGameplayInputBlocking(false); panel.PrepareForGamepadNavigation(closeOnCancel: true);
    }

    protected override void OnOpened()
    {
        feedback = null;
        channel.SetTextWithoutNotify(radio.State.Channel.ToString(CultureInfo.InvariantCulture));
        Refresh();
    }
    private void ApplyChannel()
    {
        if (!int.TryParse(channel.text, out int value) || value < RadioSignalService.MinimumChannel || value > RadioSignalService.MaximumChannel)
        { feedback = "频道必须为 1～16"; Refresh(); return; }
        Request("radio.channel", value.ToString(CultureInfo.InvariantCulture));
    }
    private void TogglePower() => Request("radio.power", (!radio.State.Enabled).ToString());
    private void ChangeModeOrMessage()
    {
        if (radio is MachineRadioStationLogic)
        { messageIndex = (messageIndex + 1) % messageIds.Length; Refresh(); return; }
        Request("radio.mode", radio.State.Transmit ? "rx" : "tx");
    }
    private void Broadcast()
    {
        if (messageIds[messageIndex] != null) { Request("radio.broadcast", messageIds[messageIndex]); return; }
        textSender = Actor;
        Item actor = textSender;
        if (actor == null) return;
        Close();
        RadioTextInputBridge.Open(actor, text => MachineWorld.RequestOperation(Entity, "radio.text", text, actor));
    }
    private void Request(string operation, string argument)
    {
        feedback = null;
        if (!MachineWorld.RequestOperation(Entity, operation, argument, Actor)) feedback = "设备未接受此操作";
        Refresh();
    }
    private void OperationCompleted(MachineOperationReceipt receipt)
    {
        if (receipt.EntityId != Entity.Id || !receipt.Operation.StartsWith("radio.", StringComparison.Ordinal)) return;
        if (receipt.Operation == "radio.text" && textSender != null)
        {
            if (!receipt.Accepted) RadioTextDelivery.ReportFeedback(textSender, "无线电操作失败，请检查电源和频道");
            textSender = null;
        }
        feedback = receipt.Accepted ? receipt.Operation == "radio.broadcast" ? "广播已发送" : "设置已更新"
            : string.IsNullOrEmpty(receipt.Reason) ? "设备未接受此操作" : receipt.Reason;
        Refresh();
    }
    protected override void RefreshPresentation()
    {
        view.Title.text = Entity.Definition.Content?.Definition.DisplayName ?? Entity.Definition.Id;
        view.Status.text = radio.Status + (string.IsNullOrEmpty(feedback) ? "" : "\n" + FlatWorldLocalizationService.GetUiText(feedback));
        MechanicalPanelView.SetButtonCaption(apply, "设置频道");
        MechanicalPanelView.SetButtonCaption(view.ActionButton, radio.State.Enabled ? "关机" : "开机");
        MechanicalPanelView.SetButtonCaption(mode, radio is MachineRadioStationLogic ? "广播：" + messageLabels[messageIndex]
            : radio.State.Transmit ? "发射模式" : "接收模式");
        MechanicalPanelView.SetButtonCaption(broadcast, "发送广播");
    }
    protected override void DisposePresentation()
    {
        MachineWorld.OperationCompleted -= OperationCompleted;
        if (panel == null) return;
        view.ActionButton.onClick.RemoveListener(TogglePower); view.CloseButton.onClick.RemoveListener(Close);
        apply.onClick.RemoveListener(ApplyChannel); mode.onClick.RemoveListener(ChangeModeOrMessage); broadcast.onClick.RemoveListener(Broadcast);
    }
    #endregion
}
