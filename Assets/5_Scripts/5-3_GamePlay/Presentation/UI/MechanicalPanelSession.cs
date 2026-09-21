using System;
using FlatWorld.Localization;
using TMPro;
using UnityEngine;

/// <summary>加工面板的通用生命周期与库存绑定；只复用视图交互，不持有加工进度或建筑业务。</summary>
public sealed class MechanicalPanelSession : IDisposable
{
    #region 面板会话
    private readonly BasePanel panel;
    private readonly MechanicalPanelView view;
    private readonly Item owner;
    private readonly MechanicalProcessor processor;
    private readonly Action<Player> action;
    private readonly Func<string> status;
    private readonly Func<string> actionLabel;
    private readonly CraftingOutputPreview preview;
    private Player actor;

    public MechanicalPanelSession(string prefabId, Item owner, MechanicalProcessor processor, Action<Player> action,
        Func<string> status, Func<string> actionLabel)
    {
        this.owner = owner; this.processor = processor; this.action = action; this.status = status; this.actionLabel = actionLabel;
        panel = UIManager.Instance.CreatePanelFromGameObject(GameRes.Instance.GetPrefab(prefabId));
        view = panel.GetComponent<MechanicalPanelView>() ?? throw new InvalidOperationException(prefabId + " 缺少正式视图绑定。");
        InventoryPanelLayout.ApplyDefaultCraftingPosition(panel.Dragger != null ? panel.Dragger.rectTransform : panel.rectTransform);
        view.ActionButton.onClick.AddListener(OnAction);
        view.CloseButton.onClick.AddListener(Close);
        view.SetProcessingVisible(processor != null);
        if (processor != null)
        {
            processor.Input.item = processor.Output.item = owner;
            processor.Input.itemSlot_UI.Clear(); processor.Output.itemSlot_UI.Clear();
            processor.Input.BindSlotUI(view.InputSlot, 0); processor.Output.BindSlotUI(view.OutputSlot, 0);
            processor.Input.SyncData(); processor.Output.SyncData();
            preview = CraftingOutputPreview.Attach(panel, view.OutputSlot);
            processor.Changed += Refresh;
        }
        panel.Close();
    }
    public void Toggle(Item playerItem)
    {
        if (panel.IsOpen()) { Close(); return; }
        actor = playerItem as Player ?? playerItem?.GetComponentInParent<Player>();
        var hand = playerItem?.GetComponentInChildren<Mod_Hand>()?.HandInventory;
        if (hand == null) throw new InvalidOperationException("加工面板缺少玩家手部库存。");
        panel.GetComponent<BuildingPanelActions>().Bind(owner);
        if (processor != null)
        {
            processor.Input.DefaultTarget_Inventory = processor.Output.DefaultTarget_Inventory = hand;
            processor.Input.RefreshUI(); processor.Output.RefreshUI();
        }
        panel.Toggle(); processor?.Input.SyncQuickTransferTarget(panel); Refresh();
    }
    private void OnAction() { action?.Invoke(actor); Refresh(); }
    public void Refresh()
    {
        if (panel == null || !panel.IsOpen()) return;
        if (GameRes.Instance.TryGetItemDefinition(owner.itemData.IDName, out var definition)) view.Title.text = definition.DisplayName;
        view.Status.text = status?.Invoke() ?? string.Empty;
        string caption = actionLabel?.Invoke() ?? string.Empty;
        view.ActionButton.gameObject.SetActive(!string.IsNullOrWhiteSpace(caption));
        view.ActionButton.GetComponentInChildren<TMP_Text>(true).text = FlatWorldLocalizationService.GetUiText(caption);
        if (processor == null) return;
        var result = processor.Preview();
        if (result.Success) preview?.Show(result.PrimaryOutput, processor.Progress01); else preview?.Clear();
    }
    public void Close()
    {
        actor = null;
        if (processor != null) processor.Input.DefaultTarget_Inventory = processor.Output.DefaultTarget_Inventory = null;
        if (panel != null) panel.Close();
        processor?.Input.SyncQuickTransferTarget(panel);
    }
    public void Dispose()
    {
        Close();
        if (processor != null)
        {
            processor.Changed -= Refresh;
            processor.Input.itemSlot_UI.Clear(); processor.Output.itemSlot_UI.Clear();
            processor.Input.item = processor.Output.item = null;
        }
        if (panel != null) UIManager.ExistingInstance?.DestroyPanel(panel);
    }
    #endregion
}
