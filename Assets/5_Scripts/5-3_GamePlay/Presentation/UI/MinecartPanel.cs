using System;
using FlatWorld.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>矿车页只绑定真实引擎槽和货仓，乘坐、连接及动力规则仍归领域模块。</summary>
public sealed class MinecartPanel : MonoBehaviour
{
    #region 页面绑定与生命周期
    public const string PrefabKey = "UI_Minecart";
    private static MinecartPanel current;
    private BasePanel panel;
    private Mod_Minecart cart;
    private Player actor;
    private Inventory engines, cargo;
    private TMP_Text title, status, capacity, feedback;
    private Button connect, disconnect, ride;
    private float nextRefresh;
    private string feedbackSource = string.Empty;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        Mod_Minecart.MenuRequested -= Open;
        Mod_Minecart.MenuRequested += Open;
    }

    public static void Open(Mod_Minecart target, Player player)
    {
        if (target == null || !target.CanAccess(player)) return;
        if (current == null)
            current = UIManager.Instance.CreatePanelFromGameObject(GameRes.Instance.GetPrefab(PrefabKey),
                initializeClosed: true).GetComponent<MinecartPanel>();
        current.Bind(target, player);
    }

    private void Awake()
    {
        panel = GetComponent<BasePanel>();
        panel.PrepareForGameplayWindow();
        title = DynamicText("FWUI_Title"); status = DynamicText("FWUI_Status");
        capacity = DynamicText("FWUI_Capacity"); feedback = DynamicText("FWUI_Feedback");
        connect = panel.GetButton("FWUI_Connect"); disconnect = panel.GetButton("FWUI_Disconnect");
        ride = panel.GetButton("FWUI_Ride");
        panel.GetButton("FWUI_Close").onClick.AddListener(panel.Close);
        panel.GetButton("FWUI_OpenBag").onClick.AddListener(OpenBag);
        connect.onClick.AddListener(Connect); disconnect.onClick.AddListener(Disconnect);
        ride.onClick.AddListener(ToggleRide);
        panel.Closed += ClearTarget;
    }

    private TMP_Text DynamicText(string name)
    {
        TMP_Text text = panel.GetText(name);
        if (text != null && text.TryGetComponent(out LocalizedTextBinder binder)) binder.enabled = false;
        return text;
    }

    private void Bind(Mod_Minecart target, Player player)
    {
        ClearTarget(); cart = target; actor = player;
        engines = target.EngineInventory; cargo = target.CargoInventory;
        if (engines == null || cargo == null) { ClearTarget(); return; }
        BindInventory(engines, "FWUI_EngineSlot"); BindInventory(cargo, "FWUI_CargoSlot");
        feedbackSource = string.Empty;
        panel.Open();
        PlaceWindow();
        engines.Data.PanelIsOpen = cargo.Data.PanelIsOpen = true;
        engines.SyncQuickTransferTarget(panel); cargo.SyncQuickTransferTarget(panel);
        Refresh();
    }

    private void PlaceWindow()
    {
        RectTransform rect = panel.rectTransform;
        if (rect == null || rect.parent is not RectTransform) return;
        rect.anchorMin = rect.anchorMax = Vector2.one;
        rect.pivot = Vector2.one;
        rect.anchoredPosition = new Vector2(-24f, -24f);
        GetComponent<SafeAreaScaleGroup>()?.ApplyScale();
        Canvas.ForceUpdateCanvases();
    }

    private void BindInventory(Inventory inventory, string prefix)
    {
        inventory.RuntimeAccessGuard = () => cart != null && cart.CanAccess(actor) && panel != null && panel.IsOpen();
        inventory.basePanel = panel;
        inventory.DefaultTarget_Inventory = actor.GetComponentInChildren<Mod_Hand>()?.HandInventory;
        inventory.itemSlot_UI.Clear();
        ItemSlot_UI[] slots = GetComponentsInChildren<ItemSlot_UI>(true);
        int count = 0;
        foreach (ItemSlot_UI slot in slots)
        {
            if (!slot.name.StartsWith(prefix, StringComparison.Ordinal) ||
                !int.TryParse(slot.name.Substring(prefix.Length), out int index)) continue;
            bool available = index < inventory.Data.itemSlots.Count;
            slot.gameObject.SetActive(available);
            if (available) { inventory.BindSlotUI(slot, index); count++; }
        }
        if (count != inventory.Data.itemSlots.Count)
            throw new InvalidOperationException("矿车正式页面槽位数量不足，请同步布局资源。");
        inventory.RefreshUI();
    }

    private void Update()
    {
        if (cart == null || panel == null || !panel.IsOpen()) return;
        if (!cart.CanAccess(actor)) { panel.Close(); return; }
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + .2f;
        Refresh();
    }

    private void ClearTarget()
    {
        Unbind(engines); Unbind(cargo);
        cart = null; actor = null; engines = cargo = null;
    }

    private void Unbind(Inventory inventory)
    {
        if (inventory == null) return;
        inventory.RuntimeAccessGuard = () => false;
        foreach (ItemSlot_UI slot in inventory.itemSlot_UI)
            if (slot != null) slot.ClearInventoryBinding(inventory);
        inventory.itemSlot_UI.Clear();
        inventory.Data.PanelIsOpen = false;
        if (inventory.basePanel == panel) inventory.basePanel = null;
        inventory.DefaultTarget_Inventory = null;
        inventory.SyncQuickTransferTarget();
    }

    private void OnEnable() => FlatWorldLocalizationService.LanguageChanged += OnLanguageChanged;
    private void OnDisable() { FlatWorldLocalizationService.LanguageChanged -= OnLanguageChanged; ClearTarget(); }
    private void OnLanguageChanged(string _) => Refresh();
    private void OnDestroy()
    {
        ClearTarget(); if (panel != null) panel.Closed -= ClearTarget;
        if (current == this) current = null;
    }
    #endregion

    #region 库存和车辆操作
    private void OpenBag()
    {
        if (cart == null || !cart.CanAccess(actor)) return;
        Mod_Inventory bag = actor.itemMods.GetMod_ByID<Mod_Inventory>(ModText.Bag);
        Inventory inventory = bag?.inventory;
        if (inventory != null && (inventory.basePanel == null || !inventory.basePanel.IsOpen())) inventory.SwitchUI();
        engines?.SyncQuickTransferTarget(panel); cargo?.SyncQuickTransferTarget(panel);
    }
    private void Connect()
    {
        if (cart == null || !cart.CanAccess(actor)) return;
        feedbackSource = cart.TryConnectNearest(actor, out string reason) ? "矿车已连接" : reason;
        Refresh();
    }
    private void Disconnect()
    {
        if (cart == null || !cart.CanAccess(actor)) return;
        cart.Disconnect(actor); feedbackSource = "矿车已断开"; Refresh();
    }
    private void ToggleRide()
    {
        if (cart == null || !cart.CanAccess(actor)) return;
        if (cart.Carrier.Rider?.item == actor) cart.TryLeave(actor); else cart.TryBoard(actor);
        Refresh();
    }
    private void Refresh()
    {
        if (cart == null || actor == null || panel == null || !panel.IsOpen()) return;
        title.text = Ui("矿车");
        bool seated = cart.Carrier.Rider?.item == actor;
        status.text = FlatWorldLocalizationService.GetUiFormat("编组 {0} 节 · {1}", cart.TrainCount,
            Ui(seated ? "WASD 驾驶，F 下车" : cart.Carrier.Rider == null ? "空座位" : "座位已占用"));
        capacity.text = FlatWorldLocalizationService.GetUiFormat("货仓 {0:0.#}/{1:0.#} kg · {2:0.#}/{3:0.#} L",
            cargo.Data.CurrentCarryWeight, cargo.Data.MaxCarryWeight, cargo.Data.CurrentCarryVolume, cargo.Data.MaxCarryVolume);
        feedback.text = string.IsNullOrEmpty(feedbackSource)
            ? Ui("左侧安装引擎，水和燃料放入右侧货仓；关闭窗口后仍可驾驶。") : Ui(feedbackSource);
        Caption("FWUI_Close", "关闭"); Caption("FWUI_OpenBag", "打开背包");
        Caption("FWUI_Connect", "连接附近矿车"); Caption("FWUI_Disconnect", "断开连接");
        Caption("FWUI_Ride", seated ? "下车" : "上车");
        disconnect.interactable = cart.IsLinked;
        ride.interactable = seated || cart.Carrier.Rider == null;
    }
    private void Caption(string buttonName, string source)
    {
        TMP_Text label = panel.GetButton(buttonName).GetComponentInChildren<TMP_Text>(true);
        if (label != null && label.TryGetComponent(out LocalizedTextBinder binder)) binder.enabled = false;
        if (label != null) label.text = Ui(source);
    }
    private static string Ui(string text) => FlatWorldLocalizationService.GetUiText(text ?? string.Empty);
    #endregion
}
