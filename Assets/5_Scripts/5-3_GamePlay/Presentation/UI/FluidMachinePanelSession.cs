using System;
using System.Collections;
using System.Globalization;
using FlatWorld.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>工业面板只绑定正式控件，配置和容器取液均沿现有权威命令提交。</summary>
public sealed class FluidMachinePanelSession : IMachinePanelSession
{
    #region 正式面板绑定
    private readonly FluidMachineLogic logic;
    private readonly BasePanel panel;
    private readonly MechanicalPanelView view;
    private readonly TMP_InputField primary, secondary;
    private readonly Button apply, phase, rotate;
    private readonly Inventory input, output;
    private Player actor;
    private Mod_WaterVessel HeldLiquidVessel => actor?.itemMods?.GetMod_ByID<Mod_HotBar>(ModText.Hotbar)?.CurentSelectItem
        ?.itemMods?.GetMod_ByID<Mod_WaterVessel>(Mod_WaterVessel.ModuleId);
    private Coroutine refresh;
    private bool disposed;
    public bool IsAlive => !disposed && panel != null;
    public bool IsOpen => IsAlive && panel.IsOpen();

    public FluidMachinePanelSession(FluidMachineLogic logic)
    {
        this.logic = logic;
        panel = UIManager.Instance.CreatePanelFromGameObject(logic.PanelPrefab);
        view = panel.GetComponent<MechanicalPanelView>() ?? throw new InvalidOperationException("工业正式面板缺少 MechanicalPanelView。");
        primary = FindField("FWUI_FluidSettingPrimary"); secondary = FindField("FWUI_FluidSettingSecondary");
        apply = panel.GetButton("FWUI_ApplyFluidSettings"); phase = panel.GetButton("FWUI_FluidPhase"); rotate = panel.GetButton("FWUI_RotateFluid");
        if (primary == null || secondary == null || apply == null || phase == null || rotate == null)
            throw new InvalidOperationException("工业正式面板缺少配置控件。");
        input = logic.PortableTank ?? logic.FilterMaterial; output = logic.Residue;
        view.SetProcessingVisible(false);
        SetSection(view.InputSlot, input != null); SetSection(view.OutputSlot, output != null);
        Bind(input, view.InputSlot); Bind(output, view.OutputSlot);
        view.ActionButton.onClick.AddListener(Act); view.CloseButton.onClick.AddListener(Close);
        apply.onClick.AddListener(ApplySettings); phase.onClick.AddListener(ChangePhase); rotate.onClick.AddListener(Rotate);
        panel.InitClosed(); panel.SetGameplayInputBlocking(false); panel.PrepareForGamepadNavigation(closeOnCancel: true);
        InventoryPanelLayout.ApplyDefaultCraftingPosition(panel.Dragger != null ? panel.Dragger.rectTransform : panel.rectTransform);
        panel.Opened += OnOpened; panel.Closed += OnClosed; logic.Changed += Refresh;
    }
    private TMP_InputField FindField(string name)
    { foreach (var field in panel.GetComponentsInChildren<TMP_InputField>(true)) if (field.name == name) return field; return null; }
    private void SetSection(ItemSlot_UI slot, bool visible)
    {
        bool isInput = slot == view.InputSlot;
        slot.gameObject.SetActive(visible);
        foreach (GameObject visual in view.ProcessingVisuals)
            if (visual.name.Contains(isInput ? "INPUT" : "OUTPUT", StringComparison.Ordinal) || visual == slot.gameObject)
                visual.SetActive(visible);
    }
    private static void Bind(Inventory inventory, ItemSlot_UI slot)
    { if (inventory == null) return; inventory.itemSlot_UI.Clear(); inventory.BindSlotUI(slot, 0); inventory.SyncData(); }
    public void Toggle(Item player)
    {
        if (!IsAlive) return;
        if (IsOpen) { Close(); return; }
        actor = player as Player ?? player?.GetComponentInParent<Player>();
        Inventory hand = player?.GetComponentInChildren<Mod_Hand>()?.HandInventory;
        if (actor == null || hand == null) throw new InvalidOperationException("工业交互缺少玩家和手部库存。");
        MachineWorld.RequestOperation(logic.Entity, "begin-interaction", "", actor);
        panel.GetComponent<BuildingPanelActions>()?.BindMechanical(logic.Entity);
        if (input != null) { input.DefaultTarget_Inventory = hand; input.SyncQuickTransferTarget(panel); input.RefreshUI(); }
        if (output != null) { output.DefaultTarget_Inventory = hand; output.SyncQuickTransferTarget(panel); output.RefreshUI(); }
        bool probe = logic.Definition.Kind is "mechanical-probe" or "electronic-probe";
        bool settings = probe || logic.Definition.Kind is "selector" or "compressor";
        primary.gameObject.SetActive(settings); secondary.gameObject.SetActive(probe); apply.gameObject.SetActive(settings);
        primary.SetTextWithoutNotify(probe ? logic.State.DisconnectPressureKPa.ToString(CultureInfo.InvariantCulture) :
            logic.Definition.Kind == "selector" ? logic.State.SelectedGasId : logic.State.CompressionTargetKPa.ToString(CultureInfo.InvariantCulture));
        secondary.SetTextWithoutNotify(logic.State.ReconnectPressureKPa.ToString(CultureInfo.InvariantCulture));
        phase.gameObject.SetActive(logic.Definition.Kind == "outlet");
        panel.Open(); Refresh();
    }
    private void Act()
    {
        if (logic.LiquidResiduePort != null) logic.TransferLiquidResidueTo(HeldLiquidVessel, actor);
        else MachineWorld.RequestOperation(logic.Entity, "work", "", actor);
        Refresh();
    }
    private void ApplySettings()
    {
        string operation = logic.Definition.Kind is "mechanical-probe" or "electronic-probe" ? "fluid.probe" :
            logic.Definition.Kind == "selector" ? "fluid.select" : "fluid.pressure";
        string argument = operation == "fluid.probe" ? primary.text + "|" + secondary.text : primary.text;
        MachineWorld.RequestOperation(logic.Entity, operation, argument, actor); Refresh();
    }
    private void ChangePhase()
    {
        string next = logic.State.OutletPhase == "both" ? "gas" : logic.State.OutletPhase == "gas" ? "liquid" : "both";
        MachineWorld.RequestOperation(logic.Entity, "fluid.phase", next, actor); Refresh();
    }
    private void Rotate() { MachineWorld.RequestOperation(logic.Entity, "fluid.rotate", "", actor); Refresh(); }
    public void Refresh()
    {
        if (!IsOpen) return;
        view.Title.text = FlatWorldLocalizationService.Get(FlatWorldLocalizationService.GetItemLabelKey(logic.Entity.Definition.Id),
            logic.Entity.Definition.Content?.Definition.DisplayName ?? logic.Entity.Definition.Id);
        view.Status.text = logic.Status;
        string caption = logic.ActionLabel;
        view.SetActionVisible(!string.IsNullOrEmpty(caption));
        view.ActionButton.interactable = logic.LiquidResiduePort == null || logic.CanTransferLiquidResidueTo(HeldLiquidVessel, actor);
        MechanicalPanelView.SetButtonCaption(view.ActionButton, caption);
        MechanicalPanelView.SetButtonCaption(phase, logic.State.OutletPhase == "gas" ? "只输出气体" : logic.State.OutletPhase == "liquid" ? "只输出液体" : "输出气体和液体");
        rotate.interactable = !logic.State.Ruptured && string.IsNullOrEmpty(logic.State.RuptureBudgetId);
    }
    private void OnOpened() { if (refresh == null) refresh = panel.StartCoroutine(RefreshReadings()); }
    private IEnumerator RefreshReadings() { var delay = new WaitForSecondsRealtime(.2f); while (IsOpen) { Refresh(); yield return delay; } refresh = null; }
    private void OnClosed()
    {
        if (refresh != null && panel != null) panel.StopCoroutine(refresh); refresh = null;
        actor = null;
        if (input != null) { input.DefaultTarget_Inventory = null; input.SyncQuickTransferTarget(null); }
        if (output != null) { output.DefaultTarget_Inventory = null; output.SyncQuickTransferTarget(null); }
    }
    public void Close() { OnClosed(); if (panel != null) panel.Close(); }
    public void Dispose()
    {
        if (disposed) return; Close(); disposed = true; logic.Changed -= Refresh;
        if (input != null) input.itemSlot_UI.Clear(); if (output != null) output.itemSlot_UI.Clear();
        if (panel == null) return;
        panel.Opened -= OnOpened; panel.Closed -= OnClosed;
        view.ActionButton.onClick.RemoveListener(Act); view.CloseButton.onClick.RemoveListener(Close);
        apply.onClick.RemoveListener(ApplySettings); phase.onClick.RemoveListener(ChangePhase); rotate.onClick.RemoveListener(Rotate);
        UIManager.ExistingInstance?.DestroyPanel(panel);
    }
    #endregion
}
