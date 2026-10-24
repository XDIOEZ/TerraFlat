using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>完整 Item 工作站同样使用正式候选列表，面板不拥有配方和进度。</summary>
public sealed class ManualProcessingItemPanelSession : IMachinePanelSession
{
    #region 工作站面板会话
    private readonly BasePanel panel;
    private readonly Item owner;
    private readonly ManualProcessingRuntime runtime;
    private readonly CraftingStationController controller;
    private readonly TMP_Text status;
    private Player actor;
    private bool disposed;
    public bool IsAlive => !disposed && panel != null;
    public bool IsOpen => IsAlive && panel.IsOpen();

    public ManualProcessingItemPanelSession(Item owner, ManualProcessingRuntime runtime, float workPerClick, Action<Player> work)
    {
        this.owner = owner;
        this.runtime = runtime;
        try
        {
            panel = UIManager.Instance.CreatePanelFromGameObject(GameRes.Instance.GetPrefab("UI_Anvil"));
            panel.InitClosed();
            panel.PrepareForGameplayWindow();
            panel.Closed += ClearInteraction;
            InventoryPanelLayout.ApplyDefaultCraftingPosition(panel.Dragger != null ? panel.Dragger.rectTransform : panel.rectTransform);
            if (runtime.Tool != null) Bind(runtime.Tool, "工具");
            Bind(runtime.Processor.Input, "输入");
            Bind(runtime.Processor.Output, "输出");
            status = panel.GetText("FWUI_FooterHint");
            status?.GetComponent<FlatWorld.Localization.LocalizedTextBinder>()?.ConfigureRuntime(() =>
                FlatWorld.Localization.FlatWorldLocalizationService.GetUiFormat("耐久：{0} / {1}",
                    owner.itemData.Durability, owner.itemData.MaxDurability));
            controller = new CraftingStationController(panel, runtime.Processor.Input, runtime.Processor.Output,
                runtime.Processor.Capabilities, () => Mathf.CeilToInt(runtime.Processor.RequiredWork / workPerClick), () => actor,
                authoritativeProcessor: runtime.Processor, selectRecipe: id => runtime.SelectRecipe(id),
                performWork: () => work(actor));
            runtime.Processor.Changed += Refresh;
        }
        catch { Dispose(); throw; }
    }

    private void Bind(Inventory inventory, string prefix)
    {
        inventory.item = owner;
        inventory.itemSlot_UI.Clear();
        foreach (ItemSlot_UI slot in panel.GetComponentsInChildren<ItemSlot_UI>(true))
        {
            if (!slot.name.StartsWith(prefix + "_", StringComparison.Ordinal) &&
                !slot.name.StartsWith(prefix + " ", StringComparison.Ordinal)) continue;
            if (!int.TryParse(slot.name.Substring(prefix.Length + 1), out int index)) continue;
            slot.gameObject.SetActive(index == 1);
            if (index == 1) inventory.BindSlotUI(slot, 0);
        }
        inventory.SyncData();
    }

    public void Toggle(Item player)
    {
        if (!IsAlive) return;
        if (IsOpen) { Close(); return; }
        actor = player as Player;
        Inventory hand = player?.GetComponentInChildren<Mod_Hand>()?.HandInventory;
        if (actor == null || hand == null) throw new InvalidOperationException("锻造面板缺少玩家或手部库存。");
        panel.GetComponent<BuildingPanelActions>()?.Bind(owner);
        runtime.Processor.Input.DefaultTarget_Inventory = runtime.Processor.Output.DefaultTarget_Inventory = hand;
        if (runtime.Tool != null) runtime.Tool.DefaultTarget_Inventory = hand;
        panel.Open();
        runtime.Processor.Input.SyncQuickTransferTarget(panel);
        runtime.Tool?.SyncQuickTransferTarget(panel);
        Refresh();
    }

    public void Refresh()
    {
        if (IsOpen && status != null) status.text = FlatWorld.Localization.FlatWorldLocalizationService.GetUiFormat(
            "耐久：{0} / {1}", owner.itemData.Durability, owner.itemData.MaxDurability);
    }

    public void Close()
    {
        if (panel != null) panel.Close();
        ClearInteraction();
    }

    private void ClearInteraction()
    {
        actor = null;
        runtime.Processor.Input.DefaultTarget_Inventory = runtime.Processor.Output.DefaultTarget_Inventory = null;
        if (runtime.Tool != null) runtime.Tool.DefaultTarget_Inventory = null;
        runtime.Processor.Input.SyncQuickTransferTarget(null);
        runtime.Tool?.SyncQuickTransferTarget(null);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Close();
        runtime.Processor.Changed -= Refresh;
        controller?.Dispose();
        runtime.Processor.Input.itemSlot_UI.Clear(); runtime.Processor.Output.itemSlot_UI.Clear();
        runtime.Tool?.itemSlot_UI.Clear();
        if (panel != null)
        {
            panel.Closed -= ClearInteraction;
            UIManager.ExistingInstance?.DestroyPanel(panel);
        }
    }
    #endregion
}
