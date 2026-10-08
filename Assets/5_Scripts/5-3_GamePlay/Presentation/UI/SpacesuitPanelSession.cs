using System;
using System.Collections;
using System.Collections.Generic;
using FlatWorld.Localization;
using FlatWorld.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>同一件宇航服的正式页面只绑定真实插槽，换罐沿原库存交换事务返还旧罐。</summary>
public sealed class SpacesuitPanelSession : IDisposable
{
    #region 页面生命周期
    private static readonly Dictionary<int, SpacesuitPanelSession> sessions = new();
    private readonly ItemData suit;
    private readonly Item actor;
    private readonly BasePanel panel;
    private readonly MechanicalPanelView view;
    private SpacesuitBinding binding;
    private Coroutine refresh;
    private bool disposed;
    private SpacesuitPanelSession(ItemData suit, Item actor)
    {
        this.suit = suit; this.actor = actor;
        binding = SpacesuitSystem.GetBinding(suit, actor) ?? throw new InvalidOperationException("没有宇航服实例状态。");
        panel = UIManager.Instance.CreatePanelFromGameObject(GameRes.Instance.GetPrefab("UI_Spacesuit"));
        view = panel.GetComponent<MechanicalPanelView>() ?? throw new InvalidOperationException("宇航服面板缺少正式视图。");
        view.SetProcessingVisible(true);
        view.OutputSlot.gameObject.SetActive(false);
        view.DismantleButton.gameObject.SetActive(false);
        view.SetActionVisible(true);
        binding.TankInventory.itemSlot_UI.Clear();
        binding.TankInventory.BindSlotUI(view.InputSlot, 0);
        binding.TankInventory.SyncData();
        view.CloseButton.onClick.AddListener(Close);
        view.ActionButton.onClick.AddListener(ToggleHelmet);
        panel.Opened += OnOpened;
        panel.Closed += OnClosed;
        panel.InitClosed();
        InventoryPanelLayout.ApplyDefaultCraftingPosition(panel.Dragger != null ? panel.Dragger.rectTransform : panel.rectTransform);
    }
    public static void Show(ItemData suit, Item actor)
    {
        if (suit == null || actor == null || FluidTankStorage.FindBinary(suit, Mod_Spacesuit.ModuleId) == null) return;
        if (sessions.TryGetValue(suit.Guid, out var existing) && (!ReferenceEquals(existing.suit, suit) || existing.actor != actor))
        { existing.Dispose(); existing = null; }
        if (!sessions.TryGetValue(suit.Guid, out SpacesuitPanelSession session))
            sessions[suit.Guid] = session = new(suit, actor);
        if (session.panel.IsOpen()) session.Close(); else session.panel.Open();
    }
    public static void CloseFor(ItemData suit)
    {
        if (suit != null && sessions.TryGetValue(suit.Guid, out var session)) session.Dispose();
    }
    public static void CloseForOwner(Item owner)
    {
        foreach (var session in new List<SpacesuitPanelSession>(sessions.Values)) if (session.actor == owner) session.Dispose();
    }
    private void OnOpened()
    {
        binding.TankInventory.DefaultTarget_Inventory = actor.GetComponentInChildren<Mod_Hand>()?.HandInventory;
        binding.TankInventory.RefreshUI();
        binding.TankInventory.SyncQuickTransferTarget(panel);
        Refresh();
        refresh = panel.StartCoroutine(RefreshLoop());
    }
    private void OnClosed()
    {
        if (refresh != null && panel != null) panel.StopCoroutine(refresh);
        refresh = null;
        binding.TankInventory.DefaultTarget_Inventory = null;
        binding.TankInventory.SyncQuickTransferTarget(panel);
    }
    private IEnumerator RefreshLoop()
    {
        var wait = new WaitForSecondsRealtime(.2f);
        while (!disposed && panel != null && panel.IsOpen())
        {
            yield return wait;
            if (!StillOwned()) { Close(); break; }
            Refresh();
        }
    }
    private bool StillOwned()
    {
        if (actor == null) return false;
        if (actor is Player player)
            foreach (ItemData data in MachineInventoryCommands.OwnedSpacesuits(player)) if (ReferenceEquals(data, suit)) return true;
        return false;
    }
    private void ToggleHelmet()
    {
        SpacesuitCommands.SetHelmet(actor, suit.Guid, !binding.State.HelmetOn);
        Refresh();
    }
    private void Refresh()
    {
        binding = SpacesuitSystem.GetBinding(suit, actor);
        view.Title.text = FlatWorldLocalizationService.GetUiFormat("{0} · 宇航服", suit.GameName);
        decimal standardLiters = 0m;
        if (FluidTankStorage.TryGet(binding.Tank, out FluidInventory inventory, out _)) standardLiters = FluidUnits.MolToStandardLiters(inventory.GetGasMoles(FluidIds.Oxygen));
        Mod_Oxygen oxygen = actor.itemMods.GetMod_ByID<Mod_Oxygen>(Mod_Oxygen.ModuleId);
        string protection = FlatWorldLocalizationService.GetUiText(binding.IsComplete && binding.IsHelmetSealed ? "生效" : "失效");
        double pressure = AtmosphereService.TryGetForWorld(actor.gameObject.scene.name, out var atmosphere) ? AtmosphereService.PressureKPa(atmosphere) : 0d;
        string range = "";
        if (actor.itemMods.GetMod_ByID<Mod_Pressure>(Mod_Pressure.ModuleId) is Mod_Pressure pressureModule)
        { pressureModule.GetSafePressureRange(out float min, out float max); range = "\n" + FlatWorldLocalizationService.GetUiFormat("安全气压 {0:0.#}～{1:0.#} kPa", min, max); }
        binding.TankInventory.RefreshUI();
        view.Status.text = FlatWorldLocalizationService.GetUiFormat("头盔 {0} · 套装 {1}\n气压保护 {2} · 环境 {3:0.##} kPa{4}\n呼吸 {5} · 罐内氧气 {6:0.###} 标准L\n角色氧气 {7:0.#}/{8:0.#}",
            FlatWorldLocalizationService.GetUiText(binding.State.HelmetOn ? "佩戴" : "摘下"), FlatWorldLocalizationService.GetUiText(binding.IsComplete ? "完整" : "不完整"),
            protection, pressure, range, FlatWorldLocalizationService.GetUiText(binding.IsHelmetSealed ? "罐内供氧" : "星球大气"), standardLiters, oxygen?.CurrentValue ?? 0f, oxygen?.MaxValue ?? 0f) +
            (binding.IsHelmetSealed && standardLiters <= 0m ? "\n" + FlatWorldLocalizationService.GetUiText("氧气罐为空，正在消耗角色氧气") : "");
        SetButtonLabel(view.ActionButton, binding.State.HelmetOn ? "摘下头盔" : "戴回头盔");
        view.ActionButton.interactable = StillOwned();
    }
    internal static void SetButtonLabel(Button button, string label)
    {
        MechanicalPanelView.SetButtonCaption(button, label);
    }
    private void Close() { if (panel != null) panel.Close(); }
    public void Dispose()
    {
        if (disposed) return;
        Close(); disposed = true;
        binding.TankInventory.itemSlot_UI.Clear();
        sessions.Remove(suit.Guid);
        if (panel != null)
        {
            panel.Opened -= OnOpened; panel.Closed -= OnClosed;
            view.CloseButton.onClick.RemoveListener(Close); view.ActionButton.onClick.RemoveListener(ToggleHelmet);
            UIManager.ExistingInstance?.DestroyPanel(panel);
        }
    }
    #endregion
}

public static class SpacesuitCommands
{
    #region 头盔权威意图
    public static event Func<Player, int, bool, bool> RemoteHelmetRequested;
    public static bool SetHelmet(Item actor, int suitGuid, bool on)
    {
        if (actor is not Player player) return false;
        if (!GameNetwork.HasStateAuthority) return RemoteHelmetRequested?.Invoke(player, suitGuid, on) == true;
        foreach (ItemData suit in MachineInventoryCommands.OwnedSpacesuits(player))
            if (suit.Guid == suitGuid)
            {
                bool accepted = SpacesuitSystem.GetBinding(suit, player).SetHelmet(on);
                if (accepted) ItemNetworkStateSerialization.NotifyRuntimeStateChanged(player);
                return accepted;
            }
        return false;
    }
    #endregion
}

public static class PortableFluidPanel
{
    #region 便携气罐状态页
    private static readonly Dictionary<int, MechanicalPanelSession> sessions = new();
    public static void Show(Item tank, Item actor)
    {
        if (tank == null || actor == null) return;
        if (!sessions.TryGetValue(tank.itemData.Guid, out var panel))
            sessions[tank.itemData.Guid] = panel = new MechanicalPanelSession("UI_FluidTank", tank, null, null,
                () => Status(tank.itemData, actor), () => "", refreshStatusPeriodically: true);
        panel.Toggle(actor);
    }
    public static void Close(Item tank)
    {
        if (tank != null && sessions.TryGetValue(tank.itemData.Guid, out var panel)) { panel.Dispose(); sessions.Remove(tank.itemData.Guid); }
    }
    public static string Status(ItemData data, Item actor)
    {
        if (!FluidTankStorage.TryGet(data, out var inventory, out var config)) return FlatWorldLocalizationService.GetUiText("气罐已经破裂");
        double pressure = inventory.GetPressureKPa(config.VolumeLiters, config.MinimumGasSpaceLiters);
        var lines = new List<string> { FlatWorldLocalizationService.GetUiFormat("气量 {0:0.###} 标准L", FluidUnits.MolToStandardLiters(inventory.GasMoles)),
            FlatWorldLocalizationService.GetUiFormat("液体 {0:0.###} L · 总容积 {1:0.###} L", inventory.GetLiquidLiters(), config.VolumeLiters),
            FlatWorldLocalizationService.GetUiFormat("温度 {0:0.##} ℃", inventory.GetTemperatureKelvin() - 273.15), FlatWorldLocalizationService.GetUiFormat("气压 {0:0.##}/{1:0.##} kPa", pressure, config.MaxSafePressureKPa) };
        foreach (FluidComponentState component in inventory.State.Components)
            lines.Add(FlatWorldLocalizationService.GetUiFormat("{0}：气 {1:0.###} mol · 液 {2:0.###} mol", FlatWorldLocalizationService.GetUiText(FluidCatalog.Default.Find(component.FluidId).DisplayName), component.GasMoles, component.LiquidMoles));
        if (pressure > config.MaxSafePressureKPa) lines.Add(FlatWorldLocalizationService.GetUiText("超压：罐壁持续受损，归零时爆炸"));
        return string.Join("\n", lines);
    }
    #endregion
}
