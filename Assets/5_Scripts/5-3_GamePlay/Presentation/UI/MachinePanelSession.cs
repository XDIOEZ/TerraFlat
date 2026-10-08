using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public interface IMachinePanelSession : IDisposable
{
    #region 面板会话能力

    bool IsAlive { get; }
    bool IsOpen { get; }
    void Toggle(Item actor);
    void Close();
    void Refresh();

    #endregion
}

/// <summary>通用机器面板生命周期和真实库存绑定；具体表现由独立会话扩展。</summary>
public class MachinePanelSession : IMachinePanelSession
{
    #region 面板绑定
    private readonly List<BasePanel> panels = new();
    private readonly List<(Inventory Inventory, BasePanel Panel)> inventoryPanels = new();
    private Button actionButton;
    private bool disposed;
    protected MachineEntity Entity { get; }
    protected MachineLogic Logic { get; }
    protected Player Actor { get; private set; }
    public bool IsAlive => !disposed && panels.Count > 0 && panels[0] != null;
    public bool IsOpen => IsAlive && panels[0].IsOpen();

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IMachinePanelSession Create(MachineEntity entity) => MachinePanelFactoryRegistry.Create(entity);

    public MachinePanelSession(MachineEntity entity) : this(entity, bindDefaultInventories: true) { }

    protected MachinePanelSession(MachineEntity entity, bool bindDefaultInventories)
    {
        Entity = entity ?? throw new ArgumentNullException(nameof(entity));
        Logic = entity.Logic ?? throw new InvalidOperationException("机器领域状态尚未恢复。");
        try
        {
            if (bindDefaultInventories)
            {
                foreach (Inventory inventory in Logic.Inventories)
                {
                    BasePanel panel = CreatePanel(inventory.InventoryPanel_Prefab != null ? inventory.InventoryPanel_Prefab : Logic.PanelPrefab);
                    TrackInventory(inventory, panel);
                    inventory.basePanel = panel;
                    inventory.InitUI();
                }
            }
            Logic.Changed += Refresh;
        }
        catch { Dispose(); throw; }
    }

    protected BasePanel CreatePanel(GameObject prefab)
    {
        if (prefab == null) throw new InvalidOperationException("机器正式面板缺失：" + Entity.Definition.Id);
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

    protected void BindSlots(BasePanel panel, Inventory inventory, string prefix)
    {
        TrackInventory(inventory, panel);
        inventory.itemSlot_UI.Clear();
        for (int i = 0; i < inventory.Data.itemSlots.Count; i++)
        {
            Button button = panel.GetButton(prefix + "_" + (i + 1)) ?? panel.GetButton(prefix + " " + (i + 1));
            ItemSlot_UI slot = button != null ? button.GetComponent<ItemSlot_UI>() : null;
            if (slot == null) throw new InvalidOperationException(panel.name + " 缺少 " + prefix + "_" + (i + 1));
            inventory.BindSlotUI(slot, i);
        }
        inventory.SyncData();
    }

    // 各专属绑定器登记同一真实库存，关闭只解除交互引用。
    protected void TrackInventory(Inventory inventory, BasePanel panel) => inventoryPanels.Add((inventory, panel));

    protected void BindActionButton(Button button)
    {
        actionButton = button ?? throw new ArgumentNullException(nameof(button));
        actionButton.onClick.AddListener(Act);
    }
    #endregion

    #region 开关与清理
    public void Toggle(Item player)
    {
        if (!IsAlive) return;
        if (IsOpen) { Close(); return; }
        Actor = player as Player ?? player?.GetComponentInParent<Player>();
        Inventory hand = player?.GetComponentInChildren<Mod_Hand>()?.HandInventory;
        if (Actor == null || hand == null) throw new InvalidOperationException("机器交互缺少玩家或手部库存。");
        MachineWorld.RequestOperation(Entity, "begin-interaction", "", Actor);
        foreach (BasePanel panel in panels)
        {
            panel.GetComponent<BuildingPanelActions>()?.BindMechanical(Entity);
            if (panel.TryGetText("窗口信息", out TextMeshProUGUI title))
                title.text = Entity.Definition.Content?.Definition.DisplayName ?? Entity.Definition.Id;
            panel.Open();
        }
        foreach (var binding in inventoryPanels)
        {
            binding.Inventory.DefaultTarget_Inventory = hand;
            binding.Inventory.SyncQuickTransferTarget(binding.Panel);
            binding.Inventory.RefreshUI();
        }
        Refresh();
        OnOpened();
    }

    private void Act() => RequestWork();
    protected bool RequestWork() => MachineWorld.RequestOperation(Entity, "work", "", Actor);

    public void Refresh()
    {
        if (!IsOpen) return;
        RefreshPresentation();
        if (actionButton != null) actionButton.interactable = Logic.CanAct;
    }

    protected virtual void RefreshPresentation() { }
    protected virtual void OnOpened() { }
    protected virtual void DisposePresentation() { }

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
        Actor = null;
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
        Logic.Changed -= Refresh;
        DisposePresentation();
        if (actionButton != null) actionButton.onClick.RemoveListener(Act);
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
        inventoryPanels.Clear();
        actionButton = null;
    }
    #endregion
}
