using System;
using FlatWorld.Localization;

/// <summary>每个种植槽绑定独立种子库存，按钮仅提交权威机器命令。</summary>
public sealed class CultivatorPanelSession : MachinePanelSession
{
    #region 正式页面绑定
    private readonly CultivatorLogic cultivator;
    private readonly CultivatorPanelView view;
    private readonly MechanicalPanelView mechanical;
    private readonly UnityEngine.Events.UnityAction[] clearHandlers;
    public CultivatorPanelSession(MachineEntity entity) : base(entity, bindDefaultInventories: false)
    {
        try
        {
            cultivator = (CultivatorLogic)Logic;
            BasePanel panel = CreatePanel(cultivator.PanelPrefab);
            view = panel.GetComponent<CultivatorPanelView>()
                ?? throw new InvalidOperationException("培育器正式页面缺少槽位视图。");
            mechanical = panel.GetComponent<MechanicalPanelView>()
                ?? throw new InvalidOperationException("培育器正式页面缺少机械状态视图。");
            if (view.SeedSlots?.Length != cultivator.Configuration.PlotCount ||
                view.HarvestSlots?.Length != cultivator.Configuration.HarvestSlotCount || view.FertilizerSlot == null ||
                view.ClearPlotButtons?.Length != cultivator.Configuration.PlotCount || view.WaterButton == null || view.Status == null)
                throw new InvalidOperationException("培育器正式页面槽位与配置不符。");
            Bind(cultivator.Seeds, view.SeedSlots, panel);
            Bind(cultivator.Harvest, view.HarvestSlots, panel);
            Bind(cultivator.Fertilizer, new[] { view.FertilizerSlot }, panel);
            BindActionButton(mechanical.ActionButton);
            view.WaterButton.onClick.AddListener(FillWater);
            clearHandlers = new UnityEngine.Events.UnityAction[view.ClearPlotButtons.Length];
            for (int i = 0; i < clearHandlers.Length; i++)
            {
                int index = i;
                clearHandlers[i] = () => MachineWorld.RequestOperation(Entity, "cultivator.clear", index.ToString(), Actor);
                view.ClearPlotButtons[i].onClick.AddListener(clearHandlers[i]);
            }
        }
        catch { Dispose(); throw; }
    }
    private void Bind(Inventory inventory, ItemSlot_UI[] slots, BasePanel panel)
    {
        TrackInventory(inventory, panel);
        inventory.itemSlot_UI.Clear();
        for (int i = 0; i < slots.Length; i++) inventory.BindSlotUI(slots[i], i);
        inventory.SyncData();
    }
    private void FillWater() => MachineWorld.RequestOperation(Entity, "cultivator.fill", "", Actor);
    protected override void RefreshPresentation()
    {
        view.Status.text = cultivator.Status;
        mechanical.Title.text = FlatWorldLocalizationService.GetUiText("培育仓");
        MechanicalPanelView.SetButtonCaption(mechanical.ActionButton, cultivator.ActionLabel);
        cultivator.Seeds.RefreshUI();
        cultivator.Harvest.RefreshUI();
        cultivator.Fertilizer.RefreshUI();
        for (int i = 0; i < view.ClearPlotButtons.Length; i++)
            view.ClearPlotButtons[i].interactable = cultivator.State.Plots[i].CropId != null;
    }
    protected override void DisposePresentation()
    {
        if (view == null) return;
        if (view.WaterButton != null) view.WaterButton.onClick.RemoveListener(FillWater);
        if (clearHandlers != null)
            for (int i = 0; i < clearHandlers.Length; i++)
                if (view.ClearPlotButtons[i] != null) view.ClearPlotButtons[i].onClick.RemoveListener(clearHandlers[i]);
    }
    #endregion
}
