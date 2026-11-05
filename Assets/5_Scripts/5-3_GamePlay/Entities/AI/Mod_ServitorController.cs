using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using FlatWorld.Localization;
using MemoryPack;
using UnityEngine;

public enum ServitorWorkMode { Standby, Haul }

/// <summary>机仆持有自己的电池、工作指令和货仓；行为优先级由正式 JSON 行为树决定。</summary>
public sealed partial class Mod_ServitorController : Module, IItemModuleDependencyBinder, IInteractable, IContainerPortProvider
{
    #region 配置与持久状态
    [Serializable]
    private sealed class ServitorSaveState
    {
        public int Version = 1;
        public ServitorBatteryState Battery = new();
        public ServitorWorkMode Mode;
        public ActorHaulingOrder Order = new();
        public byte[] Cargo;
    }

    public const string ModuleId = "ServitorController";
    public Ex_ModData ModData = new();
    public override ModuleData _Data { get => ModData; set => ModData = (Ex_ModData)value; }
    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;
    public float BatteryCapacityJoules = 10000f;
    public float LowBatteryFraction = .2f;
    public float WorkingWatts = 10f;
    public float StandbyWatts = 2f;
    public float ChargingSearchRadius = 30f;
    public float ChargingReach = 1.25f;
    public static event Action<Mod_ServitorController, Item> TerminalRequested;

    private ServitorSaveState state;
    private Mod_Mover_AI mover;
    private Mod_DamageReceiver health;
    private Inventory cargoInventory;
    private IItemTransferPort cargoPort;
    private ActorHaulingJobDriver hauling;
    private string boundWorld;
    private uint generation;
    private bool executingWork;
    private readonly List<MachineEntity> terminalCandidates = new();
    private readonly List<IContainerPort> terminalPorts = new();

    public float BatteryPercentage => (state?.Battery.Fraction(BatteryCapacityJoules) ?? 0f) * 100f;
    public bool NeedsRecharge => state?.Battery.RechargeRequested == true;
    public bool CanExecuteWork => Ready && !NeedsRecharge && state.Battery.StoredJoules > 0f && state.Mode == ServitorWorkMode.Haul;
    public ServitorWorkMode WorkMode => state?.Mode ?? ServitorWorkMode.Standby;
    public float WorkRadius => state?.Order.Radius ?? 10f;
    public int DestinationGuid => state?.Order.Destination?.ItemGuid ?? 0;
    public string Status { get; private set; } = "待机";
    public bool Ready => IsRuntimeLoaded && item != null && !item.DestructionHandled && (health == null || health.Hp > 0f);
    public string DestinationName
    {
        get
        {
            ContainerPortAddress address = state?.Order.Destination;
            if (address == null) return FlatWorldLocalizationService.GetUiText("未指定");
            string name = GameRes.ExistingInstance != null && GameRes.ExistingInstance.TryGetItemDefinition(address.ItemId, out var definition)
                ? definition.DisplayName : address.ItemId;
            using var world = GameplayWorldRegistry.UseExistingWorldOrMachineScope(GameplayWorldRegistry.GetItemWorldKey(item));
            MachineEntity container = MachineWorld.GetById(address.MachineId);
            return container?.Snapshot?.Guid == address.ItemGuid ? $"{name} ({container.Cell.x}, {container.Cell.y})" : name;
        }
    }
    public int CargoAmount
    {
        get
        {
            int result = 0;
            if (cargoInventory != null)
                foreach (ItemSlot slot in cargoInventory.Data.itemSlots)
                    result += Inventory_Data.GetWholeStackAmount(slot?.itemData?.Stack?.Amount ?? 0f);
            return result;
        }
    }
    #endregion

    #region 生命周期与状态推进
    public void BindModuleDependencies(ItemMods modules)
    {
        mover = modules.RequireSingleModById<Mod_Mover_AI>(ModText.Mod_Mover);
        health = modules.RequireSingleModById<Mod_DamageReceiver>(ModText.Hp);
    }

    protected override void OnLoad()
    {
        if (!float.IsFinite(BatteryCapacityJoules) || BatteryCapacityJoules <= 0f ||
            !float.IsFinite(LowBatteryFraction) || LowBatteryFraction <= 0f || LowBatteryFraction >= 1f ||
            !float.IsFinite(WorkingWatts) || WorkingWatts < 0f || !float.IsFinite(StandbyWatts) || StandbyWatts < 0f ||
            !float.IsFinite(ChargingSearchRadius) || ChargingSearchRadius <= 0f || !float.IsFinite(ChargingReach) || ChargingReach <= 0f)
            throw new InvalidOperationException("机仆电池或充电参数无效。");
        BindModuleDependencies(item.itemMods);
        state = ModData.GetData<ServitorSaveState>();
        if (state == null) state = new ServitorSaveState { Battery = new ServitorBatteryState { StoredJoules = BatteryCapacityJoules } };
        if (state.Version != 1) throw new InvalidOperationException("不支持的机仆状态版本。");
        state.Battery ??= new ServitorBatteryState(); state.Order ??= new ActorHaulingOrder();
        state.Battery.Consume(0f, BatteryCapacityJoules, LowBatteryFraction);
        generation = item.RuntimeGeneration;
        boundWorld = GameplayWorldRegistry.GetItemWorldKey(item);
        // 跨世界恢复不能沿用原世界仓库，电池与货仓仍按原快照恢复。
        if (state.Order.Destination != null && state.Order.WorldKey != boundWorld)
        {
            state.Order.Destination = null; state.Order.WorldKey = null; state.Mode = ServitorWorkMode.Standby;
        }
        Inventory_Data cargo = state.Cargo?.Length > 0
            ? MemoryPackSerializer.Deserialize<StandaloneInventorySaveSnapshot>(state.Cargo).Restore()
            : new Inventory_Data(new List<ItemSlot> { new(0), new(1), new(2), new(3) }, "ServitorCargo");
        cargoInventory = new Inventory { item = item, Data = cargo, StorageMaxWeightKg = 40f, StorageMaxVolumeCubicMeters = .06f };
        cargoInventory.InitData();
        cargoPort = new InventoryItemTransferPort(cargoInventory, item,
            new ContainerPortConfiguration { Id = "servitor-cargo", Access = ContainerAccessKind.Machine,
                OwnerOnly = true, Reach = 2f }, () => Ready && item.RuntimeGeneration == generation);
        hauling = new ActorHaulingJobDriver(item, mover, cargoPort);
        health.DeathStarted += HandleDeath;
        SpatialInteractionRegistry.Register(this, .75f);
    }

    public void TickServitorState(float seconds)
    {
        if (!Ready || !GameNetwork.HasStateAuthority) return;
        string world = GameplayWorldRegistry.GetItemWorldKey(item);
        if (world != boundWorld)
        {
            hauling.Suspend(); StopCharging(); boundWorld = world;
            // 仓库属于原世界，跨世界保留货物但必须重新下达搬运订单。
            state.Order.Destination = null; state.Order.WorldKey = null; state.Mode = ServitorWorkMode.Standby;
            PersistCommand();
        }
        chargingClock += Mathf.Max(0f, seconds);
        bool receivingPower = chargeLease?.LastSuppliedWatts > 0f && IsChargingContactValid();
        if (!receivingPower)
            state.Battery.Consume((executingWork || mover.HasActiveDestination ? WorkingWatts : StandbyWatts) * Mathf.Max(0f, seconds),
                BatteryCapacityJoules, LowBatteryFraction);
        if (state.Battery.StoredJoules <= 0f) mover.StopMovement();
    }

    protected override void OnSave()
    {
        if (state == null) return;
        cargoInventory?.Save();
        state.Cargo = cargoInventory == null ? state.Cargo : MemoryPackSerializer.Serialize(StandaloneInventorySaveSnapshot.Capture(cargoInventory.Data));
        ModData.WriteData(state);
    }

    protected override void OnUnload()
    {
        SpatialInteractionRegistry.Unregister(this);
        if (health != null) health.DeathStarted -= HandleDeath;
        hauling?.Dispose(); StopCharging();
        cargoInventory?.UnbindController(); cargoInventory?.UnbindRuntimeDataEvents();
        if (cargoInventory != null) cargoInventory.item = null;
        hauling = null; cargoPort = null; cargoInventory = null; executingWork = false;
        terminalCandidates.Clear(); terminalPorts.Clear();
        mover = null; health = null; state = null; boundWorld = null;
    }

    public override void OnResourcesReloaded()
    { if (state == null) return; OnSave(); OnUnload(); OnLoad(); }

    private void HandleDeath(Mod_DamageReceiver _)
    {
        hauling?.Suspend(); StopCharging();
        if (!GameNetwork.HasStateAuthority || cargoInventory == null) return;
        for (int i = cargoInventory.Data.itemSlots.Count - 1; i >= 0; i--)
        {
            ItemSlot slot = cargoInventory.Data.itemSlots[i];
            if (slot.itemData != null)
                InventoryWorldDrop.TryDrop(slot, Inventory_Data.GetWholeStackAmount(slot.itemData.Stack.Amount),
                    item, item.transform.position, item.transform.position, 0f, inventory: cargoInventory.Data);
        }
    }

    public void CollectContainerPorts(List<IContainerPort> ports)
    { if (cargoPort?.IsValid == true) ports.Add(cargoPort); }
    #endregion

    #region 工作节点与对象终端命令
    public void TickWork(float seconds)
    {
        if (!CanExecuteWork || !GameNetwork.HasStateAuthority) return;
        executingWork = true;
        hauling.Tick(state.Order, seconds); Status = hauling.Status;
    }
    public void StopWork() { executingWork = false; hauling?.Suspend(); }
    public void TickStandby() { mover?.StopMovement(); Status = "待机"; }

    public bool CanOperate(Item player) => Ready && player != null && ActorProfileRegistry.IsLocallyControlled(player) && !player.DestructionHandled &&
        GameplayWorldRegistry.GetItemWorldKey(player) == GameplayWorldRegistry.GetItemWorldKey(item) &&
        player.gameObject.scene == item.gameObject.scene && WorldTopologyRuntime.SqrDistance(player.transform.position, item.transform.position) <= 9f;
    public bool CanInteract(Item playerItem) => CanOperate(playerItem);
    public void OnInteractStart(Item playerItem)
    { if (CanOperate(playerItem)) TerminalRequested?.Invoke(this, playerItem); }
    public void OnInteractCancel(Item playerItem) { }

    public bool SetWorkMode(Item player, ServitorWorkMode mode)
    {
        if (!CanOperate(player) || !GameNetwork.HasStateAuthority || !Enum.IsDefined(typeof(ServitorWorkMode), mode)) return false;
        StopWork(); state.Mode = mode; PersistCommand(); return true;
    }
    public bool SetWorkRadius(Item player, float radius)
    {
        if (!CanOperate(player) || !GameNetwork.HasStateAuthority || !float.IsFinite(radius)) return false;
        StopWork(); state.Order.Radius = Mathf.Clamp(radius, 3f, 30f); PersistCommand(); return true;
    }
    public bool RequestRecharge(Item player)
    {
        if (!CanOperate(player) || !GameNetwork.HasStateAuthority) return false;
        if (state.Battery.StoredJoules < BatteryCapacityJoules) state.Battery.RechargeRequested = true;
        StopWork(); PersistCommand(); return true;
    }

    // 终端只枚举本机附近的真实仓储对象，轮选不会改变当前世界或打开全局管理界面。
    public bool SelectNextNearbyStorage(Item player)
    {
        if (!CanOperate(player) || !GameNetwork.HasStateAuthority) return false;
        using var world = GameplayWorldRegistry.UseExistingWorldOrMachineScope(GameplayWorldRegistry.GetItemWorldKey(item));
        MachineWorld.CollectNodes(terminalCandidates);
        terminalCandidates.Sort((a, b) => a.Id.CompareTo(b.Id));
        var choices = new List<ContainerPortAddress>();
        foreach (MachineEntity machine in terminalCandidates)
        {
            if (machine.Definition.Kind != "storage" || WorldTopologyRuntime.SqrDistance(machine.Position, item.transform.position) > 900f) continue;
            MachineWorld.WakeForInteraction(machine);
            if (machine.Logic is not StorageLogic storage) continue;
            terminalPorts.Clear(); storage.CollectContainerPorts(terminalPorts);
            foreach (IContainerPort port in terminalPorts)
                if (port is IItemTransferPort && port.IsValid && (port.Configuration.Direction & ContainerPortDirection.Input) != 0 &&
                    (port.Configuration.Access & ContainerAccessKind.Machine) != 0 && !port.Configuration.OwnerOnly)
                    choices.Add(new ContainerPortAddress { MachineId = machine.Id, ItemGuid = machine.Snapshot.Guid,
                        ItemId = machine.Snapshot.IDName, PortId = port.Reference.PortId });
        }
        terminalCandidates.Clear(); terminalPorts.Clear();
        if (choices.Count == 0) return false;
        int previous = choices.FindIndex(address => address.MachineId == state.Order.Destination?.MachineId && address.PortId == state.Order.Destination?.PortId);
        StopWork(); state.Order.Destination = choices[(previous + 1) % choices.Count]; state.Order.WorldKey = MachineWorld.WorldKey;
        PersistCommand(); return true;
    }

    private void PersistCommand() { OnSave(); ItemNetworkStateSerialization.NotifyRuntimeStateChanged(item); }
    #endregion
}
