using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using FlatWorld.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>对讲机复用正式工业面板与实际电池库存，输入消息沿聊天焦点提交。</summary>
public sealed class RadioTransceiverPanelSession : IDisposable
{
    #region 页面与实际插槽
    private static readonly Dictionary<int, RadioTransceiverPanelSession> sessions = new();
    private readonly ItemData device;
    private readonly Item actor;
    private readonly BasePanel panel;
    private readonly MechanicalPanelView view;
    private readonly TMP_InputField channel;
    private readonly Button apply, power, talk;
    private RadioTransceiverBinding binding;
    private Coroutine refresh;
    private bool disposed;

    private RadioTransceiverPanelSession(ItemData device, Item actor)
    {
        this.device = device; this.actor = actor;
        binding = RadioTransceiverSystem.BindRuntime(device, actor) ?? throw new InvalidOperationException("对讲机状态缺失。");
        panel = UIManager.Instance.CreatePersistentPanelFromGameObject(GameRes.Instance.GetPrefab("UI_IndustrialFluid"));
        view = panel.GetComponent<MechanicalPanelView>() ?? throw new InvalidOperationException("对讲机面板缺少控件。");
        channel = FindField(panel, "FWUI_FluidSettingPrimary");
        apply = panel.GetButton("FWUI_ApplyFluidSettings");
        power = panel.GetButton("FWUI_FluidPhase");
        talk = panel.GetButton("FWUI_RotateFluid");
        if (channel == null || apply == null || power == null || talk == null) throw new InvalidOperationException("对讲机面板缺少配置控件。");
        view.SetProcessingVisible(false);
        view.SetFluidLayout(false, false, true);
        view.SetGasCapacityVisible(false);
        view.SetActionVisible(false);
        view.DismantleButton.gameObject.SetActive(false);
        foreach (GameObject visual in view.ProcessingVisuals)
            if (visual.name.Contains("INPUT", StringComparison.Ordinal)) visual.SetActive(true);
        foreach (TMP_InputField field in panel.GetComponentsInChildren<TMP_InputField>(true)) field.gameObject.SetActive(field == channel);
        foreach (ItemSlot_UI slot in panel.GetComponentsInChildren<ItemSlot_UI>(true)) slot.gameObject.SetActive(slot == view.InputSlot);
        foreach (TMP_Text label in panel.GetComponentsInChildren<TMP_Text>(true))
            if (label.name == "FWUI_SectionTitle_INPUT")
            {
                label.GetComponent<LocalizedTextBinder>()?.Configure(FlatWorldLocalizationService.UiTable,
                    FlatWorldLocalizationService.GetUiTextKey("电池槽"), "电池槽");
                label.text = FlatWorldLocalizationService.GetUiText("电池槽");
            }
        binding.Inventory.itemSlot_UI.Clear();
        binding.Inventory.BindSlotUI(view.InputSlot, 0);
        binding.Inventory.SyncData();
        channel.contentType = TMP_InputField.ContentType.IntegerNumber;
        channel.characterLimit = 2;
        if (channel.placeholder is TMP_Text placeholder) placeholder.text = "1–16";
        apply.onClick.AddListener(ApplyChannel); power.onClick.AddListener(TogglePower); talk.onClick.AddListener(OpenTalk);
        view.CloseButton.onClick.AddListener(Close);
        panel.Opened += OnOpened; panel.Closed += OnClosed;
        panel.InitClosed(); panel.SetGameplayInputBlocking(false); panel.PrepareForGamepadNavigation(closeOnCancel: true);
        InventoryPanelLayout.ApplyDefaultCraftingPosition(panel.Dragger != null ? panel.Dragger.rectTransform : panel.rectTransform);
    }

    internal static TMP_InputField FindField(BasePanel panel, string name)
    {
        foreach (TMP_InputField field in panel.GetComponentsInChildren<TMP_InputField>(true)) if (field.name == name) return field;
        return null;
    }
    public static void Show(ItemData device, Item actor)
    {
        if (!RadioTransceiverCommands.TryFindActiveDevice(actor, device?.Guid ?? 0, out _)) return;
        if (sessions.TryGetValue(device.Guid, out RadioTransceiverPanelSession existing) &&
            (existing.panel == null || existing.actor != actor || !ReferenceEquals(existing.device, device))) existing.Dispose();
        if (!sessions.TryGetValue(device.Guid, out RadioTransceiverPanelSession session))
            sessions.Add(device.Guid, session = new RadioTransceiverPanelSession(device, actor));
        if (session.panel.IsOpen()) session.Close(); else session.panel.Open();
    }
    public static void CloseFor(ItemData device)
    { if (device != null && sessions.TryGetValue(device.Guid, out RadioTransceiverPanelSession session)) session.Dispose(); }

    private void OnOpened()
    {
        binding.Inventory.DefaultTarget_Inventory = actor.GetComponentInChildren<Mod_Hand>()?.HandInventory;
        binding.Inventory.SyncQuickTransferTarget(panel);
        channel.SetTextWithoutNotify(binding.State.Channel.ToString(CultureInfo.InvariantCulture));
        Refresh(); refresh = panel.StartCoroutine(RefreshLoop());
    }
    private IEnumerator RefreshLoop()
    {
        var delay = new WaitForSecondsRealtime(.2f);
        while (!disposed && panel != null && panel.IsOpen())
        {
            if (!RadioTransceiverCommands.TryFindActiveDevice(actor, device.Guid, out _)) { Close(); break; }
            Refresh(); yield return delay;
        }
        refresh = null;
    }
    private void OnClosed()
    {
        if (refresh != null && panel != null) panel.StopCoroutine(refresh);
        refresh = null;
        binding.Inventory.DefaultTarget_Inventory = null;
        binding.Inventory.SyncQuickTransferTarget(null);
    }
    private void ApplyChannel()
    {
        if (int.TryParse(channel.text, out int value)) RadioTransceiverCommands.SetConfiguration(actor, device.Guid, binding.State.IsOn, value);
        Refresh();
    }
    private void TogglePower()
    { RadioTransceiverCommands.SetConfiguration(actor, device.Guid, !binding.State.IsOn, binding.State.Channel); Refresh(); }
    private void OpenTalk()
    {
        if (!binding.State.IsOn || binding.RemainingJoules < RadioTransceiverSystem.TransmissionJoules) return;
        Close();
        RadioTextInputBridge.Open(actor, text => RadioTransceiverCommands.TrySend(actor, device.Guid, text));
    }
    private void Refresh()
    {
        binding = RadioTransceiverSystem.Bind(device, actor);
        if (binding == null) { Close(); return; }
        view.Title.text = FlatWorldLocalizationService.GetUiText("对讲机");
        view.Status.text = FlatWorldLocalizationService.GetUiFormat("频道 {0} · {1}\n电池 {2:0.#} / 3600 J",
            binding.State.Channel, FlatWorldLocalizationService.GetUiText(binding.State.IsOn ? "开机" : "关机"), binding.RemainingJoules);
        view.Status.text += "\n" + FlatWorldLocalizationService.GetUiText(binding.IsListening ? "收听中" : "未收听");
        MechanicalPanelView.SetButtonCaption(apply, "设置频道");
        MechanicalPanelView.SetButtonCaption(power, binding.State.IsOn ? "关机" : "开机");
        MechanicalPanelView.SetButtonCaption(talk, "发送文字");
        talk.interactable = binding.State.IsOn && binding.RemainingJoules >= RadioTransceiverSystem.TransmissionJoules;
        binding.Inventory.RefreshUI();
    }
    private void Close() { if (panel != null) panel.Close(); }
    public void Dispose()
    {
        if (disposed) return;
        Close(); disposed = true; sessions.Remove(device.Guid);
        binding.Inventory.itemSlot_UI.Clear();
        if (panel == null) return;
        panel.Opened -= OnOpened; panel.Closed -= OnClosed;
        apply.onClick.RemoveListener(ApplyChannel); power.onClick.RemoveListener(TogglePower); talk.onClick.RemoveListener(OpenTalk);
        view.CloseButton.onClick.RemoveListener(Close);
        UIManager.ExistingInstance?.DestroyPanel(panel);
    }
    #endregion
}
