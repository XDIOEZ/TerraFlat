using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using FlatWorld.Localization;
using FlatWorld.Spaceflight;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class SpaceElevatorPanelSession : IDisposable
{
    #region 终端会话与生命周期
    private static SpaceElevatorPanelSession current;
    private readonly SpaceSession session;
    private readonly Player actor;
    private readonly string terminalId, elevatorId;
    private readonly SpaceElevatorEndpoint endpoint;
    private readonly BasePanel panel;
    private readonly SpaceElevatorPanelView view;
    private readonly WarehouseInventory warehouse;
    private readonly Dictionary<Button, TMP_Text> buttonLabels = new();
    private Coroutine refresh;
    private bool disposed;
    private string feedbackSource = "把物品放入仓库，点击打包发送，到站后自动存入另一端仓库。";

    private SpaceElevatorPanelSession(SpaceSession session, Player actor, string terminalId,
        SpaceElevatorState elevator, SpaceElevatorEndpoint endpoint)
    {
        this.session = session; this.actor = actor; this.terminalId = terminalId;
        elevatorId = elevator.ElevatorId; this.endpoint = endpoint;
        GameObject prefab = GameRes.Instance.GetPrefab(RuntimeUIPrefabKeys.SpaceElevator, false);
        if (prefab == null) throw new InvalidOperationException("太空电梯正式面板缺失。");
        panel = UIManager.Instance.CreatePanelFromGameObject(prefab);
        panel.InitClosed();
        panel.PrepareForGameplayWindow();
        try
        {
            view = panel.GetComponent<SpaceElevatorPanelView>() ?? throw new InvalidOperationException("太空电梯正式视图缺失。");
            foreach (Button button in new[] { view.OpenBag, view.SendCargo, view.Ride, view.CallCabin, view.Close })
            {
                if (button == null) throw new InvalidOperationException("太空电梯正式操作按钮缺失。");
                TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
                ConfigureDynamicLabel(label); buttonLabels.Add(button, label);
            }
            Inventory_Data data = session.GetSpaceElevatorWarehouse(elevatorId, endpoint)
                ?? throw new InvalidOperationException("太空电梯仓库尚未就绪。");
            warehouse = new WarehouseInventory(CanUseWarehouse) { Data = data, item = actor, basePanel = panel };
            BindWarehouseSlots();
            warehouse.DefaultTarget_Inventory = actor.GetComponentInChildren<Mod_Hand>()?.HandInventory;
            view.OpenBag.onClick.AddListener(OpenPlayerBag);
            view.SendCargo.onClick.AddListener(SendCargo);
            view.Ride.onClick.AddListener(Ride);
            view.CallCabin.onClick.AddListener(Call);
            view.Close.onClick.AddListener(Close);
            view.Destroyed += OnViewDestroyed;
            panel.Closed += Dispose;
            FlatWorldLocalizationService.LanguageChanged += OnLanguageChanged;
            panel.Open();
            OpenPlayerBag();
            warehouse.SyncQuickTransferTarget(panel);
            InventoryPanelLayout.ApplyDefaultCraftingPosition(panel.rectTransform);
            Refresh();
            refresh = panel.StartCoroutine(RefreshLoop());
        }
        catch { Dispose(); throw; }
    }

    public static void Open(SpaceSession session, Player actor, Item terminal)
    {
        if (session == null || actor == null || terminal?.itemData == null) return;
        string terminalId = terminal.itemData.Guid.ToString(CultureInfo.InvariantCulture);
        if (!session.TryGetSpaceElevatorEndpoint(actor, terminalId, out _, out _, out _) &&
            !session.TryCreateSpaceElevator(actor, terminal, out _, out string createReason))
        { ItemActionFeedback.Show(actor, createReason); return; }
        Open(session, actor, terminalId);
    }

    public static void Open(SpaceSession session, Player actor, string terminalId)
    {
        if (session == null || actor == null) return;
        if (!session.TryGetSpaceElevatorEndpoint(actor, terminalId, out SpaceElevatorState elevator,
                out SpaceElevatorEndpoint endpoint, out string reason))
        { ItemActionFeedback.Show(actor, reason); return; }
        if (current != null && !current.disposed && current.actor == actor && current.terminalId == terminalId)
        { current.Close(); return; }
        current?.Dispose();
        current = new SpaceElevatorPanelSession(session, actor, terminalId, elevator, endpoint);
    }

    public static void CloseForTerminal(string terminalId)
    { if (current != null && current.terminalId == terminalId) current.Dispose(); }
    private void Close() { if (panel != null) panel.Close(); }
    private void OnViewDestroyed() => Dispose(false);
    private void OnLanguageChanged(string _) => Refresh();
    public void Dispose() => Dispose(true);
    private void Dispose(bool destroyPanel)
    {
        if (disposed) return;
        disposed = true;
        FlatWorldLocalizationService.LanguageChanged -= OnLanguageChanged;
        if (ReferenceEquals(current, this)) current = null;
        if (view != null) view.Destroyed -= OnViewDestroyed;
        if (warehouse != null)
        {
            warehouse.DefaultTarget_Inventory = null;
            warehouse.basePanel = null;
            warehouse.SyncQuickTransferTarget();
            warehouse.Data.Event_RefreshUI -= warehouse.RefreshUI;
            foreach (ItemSlot slot in warehouse.Data.itemSlots)
                if (slot != null) slot.onSlotDataChanged -= warehouse.HandleSlotChanged;
            foreach (ItemSlot_UI slot in warehouse.itemSlot_UI)
                if (slot != null) slot.ClearInventoryBinding(warehouse);
            warehouse.itemSlot_UI.Clear();
        }
        if (panel == null) return;
        panel.Closed -= Dispose;
        if (refresh != null) panel.StopCoroutine(refresh);
        refresh = null;
        if (destroyPanel) UIManager.ExistingInstance?.DestroyPanel(panel);
    }
    private IEnumerator RefreshLoop()
    {
        var wait = new WaitForSecondsRealtime(.2f);
        while (!disposed && panel != null && panel.IsOpen()) { yield return wait; Refresh(); }
    }
    #endregion

    #region 正式仓库插槽与背包
    private bool CanUseWarehouse() => !disposed && actor != null && panel != null && panel.IsOpen() &&
        session.TryGetSpaceElevatorEndpoint(actor, terminalId, out SpaceElevatorState elevator,
            out SpaceElevatorEndpoint currentEndpoint, out _) && elevator.ElevatorId == elevatorId && currentEndpoint == endpoint;

    private void BindWarehouseSlots()
    {
        if (view.WarehouseContent == null || view.WarehouseSlotTemplate == null || view.WarehouseScroll == null)
            throw new InvalidOperationException("太空电梯仓库插槽引用缺失。");
        var slots = new List<ItemSlot_UI>(view.WarehouseContent.GetComponentsInChildren<ItemSlot_UI>(true));
        while (slots.Count < warehouse.Data.itemSlots.Count)
        {
            ItemSlot_UI slot = UnityEngine.Object.Instantiate(view.WarehouseSlotTemplate, view.WarehouseContent).GetComponent<ItemSlot_UI>();
            slot.name = "FWUI_ElevatorWarehouseSlot" + slots.Count;
            slots.Add(slot);
        }
        for (int i = 0; i < slots.Count; i++)
        {
            bool available = i < warehouse.Data.itemSlots.Count;
            slots[i].gameObject.SetActive(available);
            if (!available) continue;
            warehouse.BindSlotUI(slots[i], i);
            warehouse.Data.itemSlots[i].onSlotDataChanged += warehouse.HandleSlotChanged;
            // 只追加本界面的监听，保留仓库原有的领域和物品观察者。
            slots[i]._OnScroll.Clear();
            slots[i]._OnScroll += warehouse.Scroll;
        }
        warehouse.Data.Event_RefreshUI += warehouse.RefreshUI;
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(view.WarehouseContent);
        view.WarehouseScroll.verticalNormalizedPosition = 1f;
    }

    private Inventory PlayerBag()
    {
        if (actor == null || actor.itemMods == null) return null;
        Mod_Inventory bag = actor.itemMods.GetMod_ByID<Mod_Inventory>(ModText.Bag);
        if (bag?.InventoryInstances == null) return null;
        foreach (Inventory inventory in bag.InventoryInstances)
            if (inventory?.Data?.itemSlots != null) return inventory;
        return null;
    }
    private void OpenPlayerBag()
    {
        Inventory bag = PlayerBag();
        if (bag != null && (bag.basePanel == null || !bag.basePanel.IsOpen())) bag.SwitchUI();
        warehouse?.SyncQuickTransferTarget(panel);
    }
    #endregion

    #region 发送与乘坐
    private void SendCargo()
    {
        if (!CanUseWarehouse()) { Refresh(); return; }
        if (warehouse.HasActiveSlotDrag)
        { feedbackSource = "请先放回正在拖动的物品，再打包发送。"; Refresh(); return; }
        bool accepted = session.TryPackAndSendSpaceElevatorCargo(actor, elevatorId, OnCargoDelivered, out string reason);
        feedbackSource = accepted ? "货物已打包出发，到站后自动存入另一端仓库。" : reason;
        Refresh();
    }
    private void OnCargoDelivered(bool success, string reason)
    {
        if (actor != null) ItemActionFeedback.Show(actor, reason);
        if (disposed) return;
        feedbackSource = reason;
        Refresh();
    }
    private void Ride()
    {
        if (!CanUseWarehouse()) { Refresh(); return; }
        bool accepted = session.TryDispatchSpaceElevator(actor, elevatorId, true, (success, reason) =>
        {
            if (actor != null) ItemActionFeedback.Show(actor, reason);
            if (disposed) return;
            feedbackSource = reason;
            if (success) Close(); else Refresh();
        }, out string reason);
        if (disposed) return;
        feedbackSource = accepted ? "已预约乘坐：请留在终端附近等候，到站后自动前往。" : reason;
        Refresh();
    }
    private void Call()
    {
        if (!CanUseWarehouse()) { Refresh(); return; }
        bool accepted = session.TryCallSpaceElevator(actor, elevatorId, out string reason);
        feedbackSource = accepted ? "已召回轿厢，请等待抵达。" : reason;
        Refresh();
    }
    #endregion

    #region 状态表现
    private void Refresh()
    {
        if (disposed || panel == null) return;
        if (actor == null || session == null) { Dispose(); return; }
        if (!session.TryGetSpaceElevatorEndpoint(actor, terminalId, out SpaceElevatorState elevator,
                out SpaceElevatorEndpoint currentEndpoint, out string reason) || elevator.ElevatorId != elevatorId || currentEndpoint != endpoint)
        { ItemActionFeedback.Show(actor, reason ?? "电梯线路已变化，请重新打开终端。"); Close(); return; }
        bool here = !elevator.Travelling && elevator.CabinEndpoint == endpoint;
        string destination = Ui(endpoint == SpaceElevatorEndpoint.Ground ? "太空站台" : "地面站台");
        view.Title.text = Ui("太空电梯");
        view.WarehouseHeading.text = Ui(endpoint == SpaceElevatorEndpoint.Ground ? "地面仓库" : "太空平台仓库");
        view.State.text = elevator.Travelling
            ? FlatWorldLocalizationService.GetUiFormat("轿厢运输中 · 剩余 {0:0.0} 秒", elevator.RemainingSeconds)
            : FlatWorldLocalizationService.GetUiFormat("当前终端：{0} · 轿厢停靠：{1}",
                Ui(endpoint == SpaceElevatorEndpoint.Ground ? "地面站台" : "太空站台"),
                Ui(elevator.CabinEndpoint == SpaceElevatorEndpoint.Ground ? "地面站台" : "太空站台"));
        if (elevator.DeliveryPending)
            view.State.text += "\n" + FlatWorldLocalizationService.GetUiFormat("待入库：{0}空间不足",
                Ui(elevator.CargoDestination == SpaceElevatorEndpoint.Ground ? "地面仓库" : "太空平台仓库"));
        view.Hint.text = Ui("拖入物品后点击打包发送；两端仓库独立保存，到站自动收货。乘坐请留在终端附近。");
        view.Feedback.text = Ui(feedbackSource ?? string.Empty);
        SetLabel(view.Ride, FlatWorldLocalizationService.GetUiFormat("乘坐前往{0}", destination));
        SetLabel(view.SendCargo, Ui("打包发送")); SetLabel(view.CallCabin, Ui("召回轿厢"));
        SetLabel(view.OpenBag, Ui("打开背包")); SetLabel(view.Close, Ui("关闭"));
        view.Ride.interactable = here;
        view.SendCargo.interactable = here && !elevator.DeliveryPending && !warehouse.HasActiveSlotDrag && HasWarehouseCargo();
        view.CallCabin.interactable = !elevator.Travelling && !here;
        view.OpenBag.interactable = PlayerBag() != null;
        Inventory_Data cargo = warehouse.Data;
        view.CargoCapacity.text = FlatWorldLocalizationService.GetUiFormat(
            "重量 {0:0.#}/{1:0.#} kg · 体积 {2:0.#}/{3:0.#} L", cargo.CurrentCarryWeight, cargo.MaxCarryWeight,
            cargo.CurrentCarryVolume, cargo.MaxCarryVolume);
    }
    private bool HasWarehouseCargo()
    {
        foreach (ItemSlot slot in warehouse.Data.itemSlots)
            if (slot?.itemData?.Stack != null && slot.Amount > 0f) return true;
        return false;
    }
    private static string Ui(string value) => FlatWorldLocalizationService.GetUiText(value);
    private static void ConfigureDynamicLabel(TMP_Text text)
    {
        if (text == null) return;
        text.richText = false;
        LocalizedTextBinder binder = text.GetComponent<LocalizedTextBinder>();
        if (binder != null) binder.enabled = false;
    }
    private void SetLabel(Button button, string text)
    {
        if (buttonLabels.TryGetValue(button, out TMP_Text label) && label != null) label.text = text;
    }
    #endregion

    #region 仓库访问边界
    private sealed class WarehouseInventory : Inventory
    {
        private readonly Func<bool> canAccess;
        public WarehouseInventory(Func<bool> canAccess) => this.canAccess = canAccess;
        private bool CanAccess => canAccess();
        public void HandleSlotChanged(ItemSlot slot) => OnItemSlotChanged(slot);
        public override bool CanAcceptQuickTransfer(ItemSlot sourceSlot, ItemSlot targetSlot) =>
            CanAccess && base.CanAcceptQuickTransfer(sourceSlot, targetSlot);
        protected override int ResolveIncomingSlotIndex(ItemSlot sourceSlot, int requestedIndex) =>
            CanAccess ? base.ResolveIncomingSlotIndex(sourceSlot, requestedIndex) : -1;
        public override void OnLeftClick(int index) { if (CanAccess) base.OnLeftClick(index); }
        public override void OnDesktopTap(int index) { if (CanAccess) base.OnDesktopTap(index); }
        public override void OnTouchTap(int index) { if (CanAccess) base.OnTouchTap(index); }
        public override bool CanTouchLongPressPutDown(int index) => CanAccess && base.CanTouchLongPressPutDown(index);
        public override bool OnTouchLongPress(int index) => CanAccess && base.OnTouchLongPress(index);
        public override void OnGamepadSubmit(int index) { if (CanAccess) base.OnGamepadSubmit(index); }
        public override void OnShiftQuickTransfer(int index) { if (CanAccess) base.OnShiftQuickTransfer(index); }
        public override void OnFlickTransfer(int index, Vector2 origin, Vector2 direction)
        { if (CanAccess) base.OnFlickTransfer(index, origin, direction); }
        public void Scroll(int index, float direction) { if (CanAccess) base.OnScroll(index, direction); }
        public override bool OnMouseDragDrop(int index, InventoryDragTransaction transaction) =>
            CanAccess && base.OnMouseDragDrop(index, transaction);
        public override InventoryDragTransaction OnMouseDragBegin(int index) =>
            CanAccess ? base.OnMouseDragBegin(index)?.WithAccessGuard(canAccess) : null;
        public override InventoryDragTransaction OnTouchHalfDragBegin(int index) =>
            CanAccess ? base.OnTouchHalfDragBegin(index)?.WithAccessGuard(canAccess) : null;
    }
    #endregion
}
