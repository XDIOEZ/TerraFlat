using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FlatWorld.Networking;
using MemoryPack;
using UnityEngine;

[Serializable, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class RadioTransceiverState
{
    #region 对讲机实例状态
    [MemoryPackOrder(0)] public int Version = 1;
    [MemoryPackOrder(1)] public bool IsOn;
    [MemoryPackOrder(2)] public int Channel = 1;
    [MemoryPackOrder(3)] public InventoryInstanceSnapshot Battery;
    #endregion
}

/// <summary>手持对讲机随真实物品加载和卸载，不在背包中自动运行。</summary>
public sealed class Mod_RadioTransceiver : Module, IInteractable
{
    #region 手持生命周期
    public const string ModuleId = "对讲机模块";
    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.FixedInterval;
    public override float FixedTickInterval => .2f;
    public Ex_ModData_MemoryPackable modData = new() { ID = ModuleId };
    public override ModuleData _Data { get => modData; set => modData = (Ex_ModData_MemoryPackable)value; }

    protected override void OnLoad()
    {
        RadioTransceiverSystem.BindRuntime(item.itemData, item.Owner);
        item.OnAct += OpenHeld;
    }
    protected override void OnSave() => RadioTransceiverSystem.Flush(item?.itemData);
    public override void ModUpdate(float deltaTime)
    {
        if (item != null)
            RadioTransceiverSystem.Advance(item.itemData, item.Owner, deltaTime);
    }
    protected override void OnUnload()
    {
        if (item != null) item.OnAct -= OpenHeld;
        RadioTransceiverPanelSession.CloseFor(item?.itemData);
        RadioTransceiverSystem.Unbind(item?.itemData, item?.Owner);
    }
    private void OpenHeld() { if (item?.Owner != null) OnInteractStart(item.Owner); }
    public bool CanInteract(Item actor) => item != null && item.InHand && item.Owner == actor;
    public void OnInteractStart(Item actor) { if (CanInteract(actor)) RadioTransceiverPanelSession.Show(item.itemData, actor); }
    public void OnInteractCancel(Item actor) => RadioTransceiverPanelSession.CloseFor(item?.itemData);
    #endregion
}

public sealed class RadioBatteryInventory : Inventory
{
    #region 单件电池槽
    public ItemData Device;
    public override bool CanAcceptQuickTransfer(ItemSlot sourceSlot, ItemSlot targetSlot) =>
        base.CanAcceptQuickTransfer(sourceSlot, targetSlot) && RadioBatteryStorage.TryGetRemainingJoules(sourceSlot?.itemData, out _);
    #endregion
}

public sealed class RadioTransceiverBinding
{
    #region 持有者与信号端点
    public ItemData Device;
    public Item Owner;
    public RadioTransceiverState State;
    public RadioBatteryInventory Inventory;
    internal RadioSignalService Service;
    internal int RegisteredChannel;
    public ItemData Battery => Inventory.Data.itemSlots[0].itemData;
    public double RemainingJoules => RadioBatteryStorage.TryGetRemainingJoules(Battery, out double value) ? value : 0d;
    public bool IsListening => State.IsOn && RemainingJoules > 0d && IsHeldOrEquipped &&
        (!GameNetwork.HasStateAuthority || Service != null);
    public bool IsHeldOrEquipped
    {
        get
        {
            if (Owner == null) return false;
            var hand = Owner.itemMods.GetMod_ByID<Mod_HotBar>(ModText.Hotbar)?.CurentSelectItem;
            var heldModule = hand?.itemMods.GetMod_ByID<Mod_RadioTransceiver>(Mod_RadioTransceiver.ModuleId);
            return hand != null && hand.InHand && ReferenceEquals(hand.itemData, Device) && heldModule?.IsRuntimeLoaded == true ||
                Owner.itemMods.GetMod_ByID<Mod_Equipment>(ModText.Equipment_Module)?.IsRuntimeLoaded == true &&
                SpacesuitSystem.IsActuallyEquipped(Owner, Device);
        }
    }

    internal void Unregister()
    {
        Service?.Unregister(Device.Guid);
        Service = null;
        RegisteredChannel = 0;
    }

    internal void ReconcileEndpoint()
    {
        string key = Owner != null ? GameplayWorldRegistry.GetItemWorldKey(Owner) : null;
        RadioSignalService next = GameNetwork.HasStateAuthority && IsHeldOrEquipped && !string.IsNullOrEmpty(key)
            ? MachineWorld.GetRadioServiceForWorld(key) : null;
        if (!State.IsOn || RemainingJoules <= 0d) next = null;
        if (Service != next || RegisteredChannel != State.Channel) Unregister();
        if (next == null || Service != null) return;
        if (next.Register(Device.Guid, State.Channel, () => Owner.transform.position,
                RadioEndpointCapabilities.TransmitText | RadioEndpointCapabilities.ReceiveText,
                onText: message => RadioTextDelivery.Deliver(Owner, message)))
        { Service = next; RegisteredChannel = State.Channel; }
    }
    #endregion
}

/// <summary>对讲机的真实电池槽与运行端点由物品实例共同持有。</summary>
public static class RadioTransceiverSystem
{
    #region 物品绑定与持久化
    public const double ListeningWatts = .5d;
    public const double TransmissionJoules = 2d;
    private sealed class Cache
    {
        public byte[] Bytes;
        public RadioTransceiverBinding Binding;
        public readonly HashSet<ItemData> Visiting = new();
        public bool HasSavedSnapshot, SavedOn;
        public int SavedChannel;
        public ulong SavedInventorySignature, SavedPayloadSignature;
    }
    private static readonly ConditionalWeakTable<Ex_ModData_MemoryPackable, Cache> caches = new();

    public static RadioTransceiverBinding BindRuntime(ItemData device, Item owner)
    {
        RadioTransceiverBinding binding = Bind(device, owner);
        if (binding != null)
        {
            binding.Inventory.InitData();
            binding.Inventory.Data.itemSlots[0].SlotMaxVolume = 1f;
        }
        return binding;
    }

    public static void RefreshCachedBinding(ItemData device, Item owner)
    {
        Ex_ModData_MemoryPackable binary = FluidTankStorage.FindBinary(device, Mod_RadioTransceiver.ModuleId);
        if (binary != null && caches.TryGetValue(binary, out _)) Bind(device, owner);
    }

    public static RadioTransceiverBinding Bind(ItemData device, Item owner)
    {
        if (FluidTankStorage.FindBinary(device, Mod_RadioTransceiver.ModuleId) is not Ex_ModData_MemoryPackable binary || !binary.Enabled)
            return null;
        Cache cache = caches.GetValue(binary, _ => new Cache());
        if (cache.Binding == null)
        {
            var inventory = new RadioBatteryInventory
            {
                item = owner,
                Data = new Inventory_Data(new List<ItemSlot> { new(0) }, "对讲机电池")
            };
            inventory.Data.SetUnlimitedStackSize(false);
            inventory.Data.SetUnlimitedSlots(false);
            inventory.Data.itemSlots[0].SlotMaxVolume = 1f;
            inventory.Data.SetItemAcceptancePolicy((incoming, slot) => RadioBatteryStorage.TryGetRemainingJoules(incoming, out _));
            inventory.InitData();
            inventory.Data.itemSlots[0].SlotMaxVolume = 1f;
            cache.Binding = new RadioTransceiverBinding { Device = device, Owner = owner, Inventory = inventory };
            inventory.Data.Event_OnDataChanged += _ =>
            {
                Flush(cache.Binding.Device);
                cache.Binding.ReconcileEndpoint();
                FluidTankStorage.NotifyOwner(cache.Binding.Owner, cache.Binding.Device);
            };
        }
        RadioTransceiverBinding binding = cache.Binding;
        if (binding.Owner != owner) binding.Unregister();
        binding.Device = device;
        binding.Owner = owner;
        binding.Inventory.item = owner;
        binding.Inventory.Device = device;
        if (binding.State == null || !ReferenceEquals(cache.Bytes, binary.BitData))
        {
            RadioTransceiverState state = binary.GetData<RadioTransceiverState>() ?? new RadioTransceiverState();
            if (state.Version != 1 || state.Channel < RadioSignalService.MinimumChannel ||
                state.Channel > RadioSignalService.MaximumChannel || state.Battery?.SlotCount > 1)
                throw new InvalidOperationException("对讲机状态或电池槽无效。");
            if (state.Battery == null) binding.Inventory.Data.itemSlots[0].itemData = null;
            else state.Battery.RestoreTo(binding.Inventory.Data, data => data.SharedConfiguration == null
                ? ItemDefinitionRuntime.RebasePersistedData(GameRes.ExistingInstance, data) : data);
            if (binding.Battery != null && !RadioBatteryStorage.TryGetRemainingJoules(binding.Battery, out _))
                throw new InvalidOperationException("对讲机电池槽包含不支持的物品。");
            binding.State = state;
            cache.Bytes = binary.BitData;
            cache.HasSavedSnapshot = false;
        }
        return binding;
    }

    public static void Flush(ItemData device)
    {
        Ex_ModData_MemoryPackable binary = FluidTankStorage.FindBinary(device, Mod_RadioTransceiver.ModuleId);
        if (binary == null || !caches.TryGetValue(binary, out Cache cache) || cache.Binding?.State == null) return;
        RadioTransceiverBinding binding = cache.Binding;
        if (!ReferenceEquals(cache.Bytes, binary.BitData))
        {
            binding = Bind(device, binding.Owner);
            if (binding == null) return;
        }
        cache.Visiting.Clear();
        bool tracked = ItemRuntimeStateFingerprint.TryCalculateInventory(binding.Inventory.Data, cache.Visiting, out ulong signature);
        if (tracked && cache.HasSavedSnapshot && cache.SavedInventorySignature == signature &&
            cache.SavedOn == binding.State.IsOn && cache.SavedChannel == binding.State.Channel &&
            cache.SavedPayloadSignature == ItemRuntimeStateFingerprint.CalculateBinaryPayload(binary.BitData)) return;
        binding.State.Battery = InventoryInstanceSnapshot.Capture(binding.Inventory.Data);
        binary.WriteData(binding.State);
        cache.Bytes = binary.BitData;
        cache.HasSavedSnapshot = tracked;
        cache.SavedInventorySignature = signature;
        cache.SavedPayloadSignature = ItemRuntimeStateFingerprint.CalculateBinaryPayload(cache.Bytes);
        cache.SavedOn = binding.State.IsOn;
        cache.SavedChannel = binding.State.Channel;
    }

    public static void Unbind(ItemData device, Item owner)
    {
        Ex_ModData_MemoryPackable binary = FluidTankStorage.FindBinary(device, Mod_RadioTransceiver.ModuleId);
        if (binary == null || !caches.TryGetValue(binary, out Cache cache) || cache.Binding == null ||
            owner != null && cache.Binding.Owner != owner) return;
        Flush(device);
        cache.Binding.Unregister();
        cache.Binding.Inventory.UnbindRuntimeDataEvents();
    }
    #endregion

    #region 运行、开关与发话
    public static void Advance(ItemData device, Item owner, float seconds)
    {
        if (!GameNetwork.HasStateAuthority || !float.IsFinite(seconds) || seconds <= 0f) return;
        RadioTransceiverBinding binding = Bind(device, owner);
        if (binding == null) return;
        if (!binding.IsHeldOrEquipped) { binding.Unregister(); return; }
        if (binding.State.IsOn && binding.RemainingJoules > 0d)
        {
            double consumed = Math.Min(binding.RemainingJoules, ListeningWatts * seconds);
            if (RadioBatteryStorage.TryConsumeExactJoules(binding.Battery, consumed))
            {
                Flush(device);
                FluidTankStorage.NotifyOwner(owner, device);
            }
        }
        binding.ReconcileEndpoint();
    }

    public static bool SetConfiguration(ItemData device, Item owner, bool isOn, int channel)
    {
        if (!GameNetwork.HasStateAuthority || channel < RadioSignalService.MinimumChannel || channel > RadioSignalService.MaximumChannel)
            return false;
        RadioTransceiverBinding binding = Bind(device, owner);
        if (binding == null || !binding.IsHeldOrEquipped) return false;
        binding.State.IsOn = isOn;
        binding.State.Channel = channel;
        Flush(device);
        binding.ReconcileEndpoint();
        FluidTankStorage.NotifyOwner(owner, device);
        return true;
    }

    public static bool TrySend(ItemData device, Item owner, string sender, string text)
    {
        if (!GameNetwork.HasStateAuthority || string.IsNullOrWhiteSpace(text) || text.Length > RadioSignalService.MaximumTextLength)
            return false;
        RadioTransceiverBinding binding = Bind(device, owner);
        if (binding == null || !binding.IsHeldOrEquipped || !binding.State.IsOn || binding.RemainingJoules < TransmissionJoules) return false;
        binding.ReconcileEndpoint();
        if (binding.Service == null || !RadioBatteryStorage.TryConsumeExactJoules(binding.Battery, TransmissionJoules)) return false;
        bool sent = binding.Service.SendText(device.Guid, sender, text);
        Flush(device);
        binding.ReconcileEndpoint();
        FluidTankStorage.NotifyOwner(owner, device);
        return sent;
    }
    #endregion
}
