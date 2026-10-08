using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FlatWorld.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public interface IMachinePanelSession : IDisposable
{
    bool IsAlive { get; }
    bool IsOpen { get; }
    void Toggle(Item actor);
    void Close();
    void Refresh();
}

/// <summary>复用正式工作台、熔炉和容器 Prefab；只持有绑定会话，不保存玩法进度。</summary>
public sealed class MachinePanelSession : IMachinePanelSession
{
    #region 面板绑定
    private readonly MachineEntity entity;
    private readonly MachineLogic logic;
    private readonly List<BasePanel> panels = new();
    private readonly List<(Inventory Inventory, BasePanel Panel)> inventoryPanels = new();
    private readonly CraftingStationController workbenchController;
    private Button actionButton;
    private Slider progress;
    private Slider fuel;
    private TMP_Text temperature;
    private TMP_Text furnaceHint;
    private Player actor;
    private MortarInteractionView mortarView;
    private CraftingOutputPreview firePreview;
    private TMP_Text fireTemperature;
    private bool disposed;
    public bool IsAlive => !disposed && panels.Count > 0 && panels[0] != null;
    public bool IsOpen => IsAlive && panels[0].IsOpen();

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IMachinePanelSession Create(MachineEntity entity)
    {
        if (entity.Logic is VesselLogic vessel) return new VesselMachinePanelSession(vessel);
        if (entity.Logic is FluidMachineLogic fluid) return new FluidMachinePanelSession(fluid);
        if (entity.Logic is ManualProcessingLogic manual)
            return new MechanicalPanelSession("UI_HandDrill", entity, manual.Processor,
                actor => MachineWorld.RequestOperation(entity, "work", "", actor),
                () => manual.Status, () => manual.ActionLabel, () => manual.CanAct);
        if (entity.Logic is HandDrillLogic drill)
            return new MechanicalPanelSession("UI_HandDrill", entity, drill.Processor,
                actor => MachineWorld.RequestOperation(entity, "work", "", actor),
                () => drill.Status, () => drill.ActionLabel, () => drill.CanAct);
        return new MachinePanelSession(entity);
    }

    public MachinePanelSession(MachineEntity entity)
    {
        this.entity = entity ?? throw new ArgumentNullException(nameof(entity));
        logic = entity.Logic ?? throw new InvalidOperationException("机器领域状态尚未恢复。");
        try
        {
            if (logic is WorkbenchLogic workbench)
            {
                BasePanel panel = CreatePanel(logic.PanelPrefab);
                BindSlots(panel, workbench.Input, "输入");
                BindSlots(panel, workbench.Output, "输出");
                workbenchController = new CraftingStationController(panel, workbench.Input, workbench.Output,
                    workbench.Processor.Capabilities, () => workbench.RequiredClicks, () => actor,
                    authoritativeProcessor: workbench.Processor,
                    selectRecipe: id => MachineWorld.RequestOperation(entity, "select", id, actor),
                    performWork: () => MachineWorld.RequestOperation(entity, "work", "", actor));
            }
            else if (logic is FurnaceLogic furnace)
            {
                BasePanel panel = CreatePanel(logic.PanelPrefab);
                BindSlots(panel, furnace.Input, "输入");
                BindSlots(panel, furnace.Output, "输出");
                BindSlots(panel, furnace.FuelInventory, "燃料");
                actionButton = panel.GetButton("合成按钮");
                if (actionButton == null) throw new InvalidOperationException("熔炉面板缺少合成按钮。");
                actionButton.onClick.AddListener(Act);
                progress = panel.GetSlider("熔炼进度条");
                fuel = panel.GetSlider("燃料显示条");
                temperature = panel.GetText("FWUI_FurnaceTemperatureValue");
                furnaceHint = panel.GetText("FWUI_FooterHint");
            }
            else if (logic is MortarLogic mortar)
            {
                BasePanel panel = CreatePanel(logic.PanelPrefab);
                mortarView = panel.GetComponentInChildren<MortarInteractionView>(true)
                    ?? throw new InvalidOperationException("石臼正式面板缺少手势视图。");
                mortarView.SyncSlots(mortar.Bowl);
                mortarView.Struck += Strike;
                inventoryPanels.Add((mortar.Bowl, panel));
            }
            else if (logic is FireDrillLogic fireDrill)
            {
                BasePanel panel = CreatePanel(logic.PanelPrefab);
                BindSlots(panel, fireDrill.Heater.Input, "输入");
                BindSlots(panel, fireDrill.Heater.Output, "输出");
                actionButton = panel.GetButton("合成按钮")
                    ?? throw new InvalidOperationException("取火面板缺少摩擦按钮。");
                actionButton.onClick.AddListener(Act);
                firePreview = CraftingOutputPreview.Attach(panel, fireDrill.Heater.Output.itemSlot_UI[0]);
                fireTemperature = panel.GetText("FWUI_FooterHint");
            }
            else
            {
                foreach (Inventory inventory in logic.Inventories)
                {
                    BasePanel panel = CreatePanel(inventory.InventoryPanel_Prefab != null ? inventory.InventoryPanel_Prefab : logic.PanelPrefab);
                    inventory.basePanel = panel;
                    inventory.InitUI();
                    inventoryPanels.Add((inventory, panel));
                }
            }
            logic.Changed += Refresh;
        }
        catch { Dispose(); throw; }
    }

    private BasePanel CreatePanel(GameObject prefab)
    {
        if (prefab == null) throw new InvalidOperationException("机器正式面板缺失：" + entity.Definition.Id);
        BasePanel panel = UIManager.Instance.CreatePanelFromGameObject(prefab);
        panels.Add(panel);
        panel.InitClosed();
        // 工作设施沿用非模态库存面板，避免输入锁取消当前交互后立即关窗。
        panel.SetGameplayInputBlocking(false);
        panel.PrepareForGamepadNavigation(closeOnCancel: true);
        InventoryPanelLayout.ApplyDefaultCraftingPosition(panel.Dragger != null ? panel.Dragger.rectTransform : panel.rectTransform);
        panel.Closed += OnPanelClosed;
        return panel;
    }

    private void BindSlots(BasePanel panel, Inventory inventory, string prefix)
    {
        inventory.itemSlot_UI.Clear();
        for (int i = 0; i < inventory.Data.itemSlots.Count; i++)
        {
            Button button = panel.GetButton(prefix + "_" + (i + 1)) ?? panel.GetButton(prefix + " " + (i + 1));
            ItemSlot_UI slot = button != null ? button.GetComponent<ItemSlot_UI>() : null;
            if (slot == null) throw new InvalidOperationException(panel.name + " 缺少 " + prefix + "_" + (i + 1));
            inventory.BindSlotUI(slot, i);
        }
        inventory.SyncData();
        inventoryPanels.Add((inventory, panel));
    }
    #endregion

    #region 开关与清理
    public void Toggle(Item player)
    {
        if (!IsAlive) return;
        if (IsOpen) { Close(); return; }
        actor = player as Player ?? player?.GetComponentInParent<Player>();
        Inventory hand = player?.GetComponentInChildren<Mod_Hand>()?.HandInventory;
        if (actor == null || hand == null) throw new InvalidOperationException("机器交互缺少玩家或手部库存。");
        MachineWorld.RequestOperation(entity, "begin-interaction", "", actor);
        foreach (BasePanel panel in panels)
        {
            panel.GetComponent<BuildingPanelActions>()?.BindMechanical(entity);
            if (panel.TryGetText("窗口信息", out TextMeshProUGUI title))
                title.text = entity.Definition.Content?.Definition.DisplayName ?? entity.Definition.Id;
            panel.Open();
        }
        foreach (var binding in inventoryPanels)
        {
            binding.Inventory.DefaultTarget_Inventory = hand;
            binding.Inventory.SyncQuickTransferTarget(binding.Panel);
            binding.Inventory.RefreshUI();
        }
        Refresh();
        mortarView?.ResetPresentation();
    }

    private void Act() => MachineWorld.RequestOperation(entity, "work", "", actor);
    private void Strike()
    {
        if (MachineWorld.RequestOperation(entity, "work", "", actor)) mortarView?.PlayProcessingDust();
    }

    public void Refresh()
    {
        if (!IsOpen) return;
        if (logic is FurnaceLogic furnace)
        {
            if (progress != null) progress.value = furnace.Progress01;
            if (fuel != null) fuel.value = furnace.FuelRatio;
            if (temperature != null) temperature.text = furnace.Status;
            if (furnaceHint != null) furnaceHint.text = furnace.GetProcessingHint();
        }
        if (logic is MortarLogic mortar) mortarView?.SyncSlots(mortar.Bowl);
        if (logic is FireDrillLogic fireDrill && firePreview != null)
        {
            ItemData preview = fireDrill.Heater.PreviewOutput();
            if (preview != null) firePreview.Show(preview, fireDrill.Progress01);
            else firePreview.Clear();
            if (fireTemperature != null) fireTemperature.text = fireDrill.Status;
        }
        if (actionButton != null) actionButton.interactable = logic.CanAct;
    }

    private void OnPanelClosed()
    {
        bool anyOpen = false;
        foreach (BasePanel panel in panels) anyOpen |= panel != null && panel.IsOpen();
        if (anyOpen) return;
        ClearInteraction();
    }

    private void ClearInteraction()
    {
        foreach (var binding in inventoryPanels)
        {
            binding.Inventory.DefaultTarget_Inventory = null;
            binding.Inventory.SyncQuickTransferTarget(null);
        }
        actor = null;
    }

    public void Close()
    {
        foreach (BasePanel panel in panels) if (panel != null) panel.Close();
        ClearInteraction();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Close();
        logic.Changed -= Refresh;
        workbenchController?.Dispose();
        if (actionButton != null) actionButton.onClick.RemoveListener(Act);
        if (mortarView != null) mortarView.Struck -= Strike;
        firePreview?.Clear();
        foreach (var binding in inventoryPanels)
        {
            binding.Inventory.itemSlot_UI.Clear();
            if (binding.Inventory.basePanel == binding.Panel) binding.Inventory.basePanel = null;
        }
        foreach (BasePanel panel in panels)
        {
            if (panel == null) continue;
            panel.Closed -= OnPanelClosed;
            UIManager.ExistingInstance?.DestroyPanel(panel);
        }
        panels.Clear();
    }
    #endregion
}
