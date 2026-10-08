using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FlatWorld.Networking;
using MemoryPack;
using Newtonsoft.Json.Linq;
using UnityEngine;

[Serializable]
public sealed class SpacesuitConfiguration
{
    #region 套装定义
    public string SetId = "core:spacesuit";
    public string[] RequiredPartTags = { "Spacesuit.Head", "Spacesuit.Torso", "Spacesuit.Hands", "Spacesuit.Legs", "Spacesuit.Feet" };
    public float LowPressureRangeIncreaseKPa = 50f;
    public float HighPressureRangeIncreaseKPa = 500f;
    public decimal OxygenMolesPerSecond = .006m;
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(SetId) || RequiredPartTags == null || RequiredPartTags.Length == 0 ||
            !float.IsFinite(LowPressureRangeIncreaseKPa) || LowPressureRangeIncreaseKPa < 0f ||
            !float.IsFinite(HighPressureRangeIncreaseKPa) || HighPressureRangeIncreaseKPa < 0f || OxygenMolesPerSecond <= 0m)
            throw new InvalidOperationException("宇航服的套装、耐压或耗氧定义无效。");
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (string tag in RequiredPartTags)
            if (string.IsNullOrWhiteSpace(tag) || !unique.Add(tag)) throw new InvalidOperationException("宇航服必需部件标签为空或重复。");
    }
    #endregion
}

[Serializable, MemoryPackable]
public sealed partial class SpacesuitState
{
    #region 实例状态
    public bool HelmetOn = true;
    public InventoryInstanceSnapshot OxygenTank;
    #endregion
}

/// <summary>手持入口与穿戴入口都使用宇航服自身的插槽和头盔状态。</summary>
public sealed class Mod_Spacesuit : Module, IInteractable, IModuleJsonParameterValidator
{
    #region 模块与交互
    public const string ModuleId = "宇航服模块";
    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.FixedInterval;
    public override float FixedTickInterval => .2f;
    public Ex_ModData_MemoryPackable modData = new() { ID = ModuleId };
    public override ModuleData _Data { get => modData; set => modData = (Ex_ModData_MemoryPackable)value; }
    public SpacesuitConfiguration Configuration = new();
    protected override void OnLoad()
    {
        Configuration.Validate();
        SpacesuitSystem.GetBinding(item.itemData, item.Owner != null ? item.Owner : item);
        item.OnAct += OpenHeld;
    }
    protected override void OnSave() => SpacesuitSystem.Flush(item.itemData);
    public override void ModUpdate(float deltaTime)
    {
        if (!GameNetwork.HasStateAuthority || item == null || item.Owner != null || deltaTime <= 0f) return;
        SpacesuitBinding suit = SpacesuitSystem.GetBinding(item.itemData, item);
        if (suit == null) return;
        // 落地服装作为真实位置宿主，内部罐的相变和超压不会冻结。
        suit.TankInventory.MachineOwner = null;
        suit.TankInventory.ModUpdate(deltaTime);
        SpacesuitSystem.Flush(item.itemData);
        FluidTankStorage.NotifyOwner(item, item.itemData);
    }
    protected override void OnUnload()
    {
        if (item != null) item.OnAct -= OpenHeld;
        SpacesuitPanelSession.CloseFor(item?.itemData);
    }
    private void OpenHeld() { if (item.Owner != null) OnInteractStart(item.Owner); }
    public bool CanInteract(Item actor) => item != null && actor != null;
    public void OnInteractStart(Item actor) => SpacesuitPanelSession.Show(item.itemData, actor);
    public void OnInteractCancel(Item actor) => SpacesuitPanelSession.CloseFor(item.itemData);
    public void ValidateJsonParameters(JObject parameters)
        => (parameters[nameof(Configuration)]?.ToObject<SpacesuitConfiguration>() ?? Configuration).Validate();
    #endregion
}

public sealed class SpacesuitTankInventory : Inventory
{
    #region 单罐接收
    public override bool CanAcceptQuickTransfer(ItemSlot sourceSlot, ItemSlot targetSlot)
        => base.CanAcceptQuickTransfer(sourceSlot, targetSlot) && sourceSlot?.itemData != null &&
            sourceSlot.itemData.Stack.Amount == 1f && FluidTankStorage.IsPureOxygen(sourceSlot.itemData, requireContents: false);
    #endregion
}

public sealed class SpacesuitBinding
{
    #region 实际装备状态
    public ItemData Suit { get; internal set; }
    public Item Owner { get; internal set; }
    public SpacesuitConfiguration Configuration { get; internal set; }
    public SpacesuitState State { get; internal set; }
    public SpacesuitTankInventory TankInventory { get; internal set; }
    public ItemData Tank => TankInventory.Data.itemSlots.Count == 1 ? TankInventory.Data.itemSlots[0].itemData : null;
    public bool IsEquipped => Owner != null && SpacesuitSystem.IsActuallyEquipped(Owner, Suit);
    public bool HasHelmet => IsEquipped && HasPart("Spacesuit.Head");
    public bool IsHelmetSealed => IsEquipped && State.HelmetOn && HasHelmet;
    public bool IsComplete
    {
        get
        {
            if (!IsEquipped) return false;
            foreach (string tag in Configuration.RequiredPartTags) if (!HasPart(tag)) return false;
            return true;
        }
    }
    public bool HasPart(string tag)
    {
        var equipment = Owner?.itemMods.GetMod_ByID<Mod_Equipment>(ModText.Equipment_Module);
        if (equipment?.EquipmentInventory?.Data?.itemSlots == null) return false;
        foreach (ItemSlot slot in equipment.EquipmentInventory.Data.itemSlots)
            if (slot?.itemData?.Tags?.Contains(tag) == true && slot.itemData.Tags.Contains("SpacesuitSet:" + Configuration.SetId)) return true;
        return false;
    }
    public void SupplyOxygen(Mod_Oxygen receiver, float seconds)
    {
        if (receiver == null || !receiver.CanReceiveOxygen || !IsHelmetSealed || !float.IsFinite(seconds) || seconds <= 0f ||
            !FluidTankStorage.IsPureOxygen(Tank) || !FluidTankStorage.TryGet(Tank, out var inventory, out _)) return;
        decimal requested = Configuration.OxygenMolesPerSecond * (decimal)seconds;
        decimal actual = Math.Min(requested, inventory.GetAvailableMoles(FluidIds.Oxygen, FluidPhase.Gas));
        if (actual <= 0m || !inventory.TryTakeExact(FluidIds.Oxygen, FluidPhase.Gas, actual, out _)) return;
        FluidTankStorage.Write(Tank, inventory);
        TankInventory.Data.NotifyItemStateChanged(Tank);
        SpacesuitSystem.Flush(Suit);
        FluidTankStorage.NotifyOwner(Owner, Suit);
        receiver.SupplyOxygen(receiver.GetOxygenSupplyAmount(seconds * (float)(actual / requested)));
    }
    public bool SetHelmet(bool on)
    {
        if (!GameNetwork.HasStateAuthority || Owner == null) return false;
        State.HelmetOn = on;
        SpacesuitSystem.Flush(Suit);
        SpacesuitSystem.RefreshProtection(Owner);
        FluidTankStorage.NotifyOwner(Owner, Suit);
        return true;
    }
    #endregion
}

public static class SpacesuitSystem
{
    #region 实例缓存与保护来源
    private sealed class Cache { public byte[] Bytes; public SpacesuitBinding Binding; public GameRes Resources; public int ResourceRevision = -1; }
    private sealed class Protection
    {
        public readonly object Token = new();
        public Mod_Pressure Module;
        public int SuitGuid;
        public bool Active;
        public float LowIncrease, HighIncrease;
    }
    private static readonly ConditionalWeakTable<Ex_ModData_MemoryPackable, Cache> states = new();
    private static readonly ConditionalWeakTable<Item, Protection> protections = new();
    public static SpacesuitBinding GetBinding(ItemData suit, Item owner)
    {
        Ex_ModData_MemoryPackable binary = FluidTankStorage.FindBinary(suit, Mod_Spacesuit.ModuleId);
        if (binary == null) return null;
        Cache cache = states.GetValue(binary, _ => new());
        if (cache.Binding == null)
        {
            var config = FluidTankStorage.ReadConfiguration<SpacesuitConfiguration>(suit, Mod_Spacesuit.ModuleId, "Configuration") ?? new();
            config.Validate();
            var inventory = new SpacesuitTankInventory { item = owner, Data = new Inventory_Data(new List<ItemSlot> { new(0) }, "供氧气罐") };
            inventory.Data.SetUnlimitedStackSize(false);
            inventory.Data.SetUnlimitedSlots(false);
            inventory.InitData();
            inventory.Data.itemSlots[0].SlotMaxVolume = 1f;
            cache.Binding = new SpacesuitBinding { Suit = suit, Owner = owner, Configuration = config, TankInventory = inventory };
            inventory.Data.Event_OnDataChanged += _ =>
            {
                Flush(cache.Binding.Suit);
                FluidTankStorage.NotifyOwner(cache.Binding.Owner, cache.Binding.Suit);
            };
        }
        SpacesuitBinding binding = cache.Binding;
        binding.Suit = suit;
        binding.Owner = owner;
        binding.TankInventory.item = owner;
        GameRes resources = GameRes.ExistingInstance;
        int revision = resources != null ? resources.ResourceReloadVersion : 0;
        if (!ReferenceEquals(cache.Resources, resources) || cache.ResourceRevision != revision)
        {
            var config = FluidTankStorage.ReadConfiguration<SpacesuitConfiguration>(suit, Mod_Spacesuit.ModuleId, "Configuration") ?? new();
            config.Validate();
            binding.Configuration = config;
            cache.Resources = resources;
            cache.ResourceRevision = revision;
        }
        if (binding.State == null || !ReferenceEquals(cache.Bytes, binary.BitData))
        {
            SpacesuitState incoming = binary.GetData<SpacesuitState>() ?? new();
            if (incoming.OxygenTank != null && incoming.OxygenTank.SlotCount > 1)
                throw new InvalidOperationException("宇航服快照只能包含一个供氧气罐槽。");
            // 空快照必须清空同一实际槽位，不能沿用缓存里的旧氧气罐。
            if (incoming.OxygenTank == null) binding.TankInventory.Data.itemSlots[0].itemData = null;
            else incoming.OxygenTank.RestoreTo(binding.TankInventory.Data,
                data => data.SharedConfiguration == null ? ItemDefinitionRuntime.RebasePersistedData(resources, data) : data);
            ItemData restoredTank = binding.Tank;
            if (restoredTank != null)
            {
                if (restoredTank.Stack?.Amount != 1f || !FluidTankStorage.TryGet(restoredTank, out var contents, out var tankConfig) || !tankConfig.SupportsSpacesuit)
                    throw new InvalidOperationException("宇航服快照中的物品不符合独立供氧气罐定义。");
                foreach (FluidComponentState component in contents.State.Components)
                    if (component.FluidId != FluidIds.Oxygen && component.GasMoles + component.LiquidMoles > 0m)
                        throw new InvalidOperationException("宇航服快照中的气罐不能含其他物质。");
            }
            binding.State = incoming;
            binding.TankInventory.Data.itemSlots[0].SlotMaxVolume = 1f;
            binding.TankInventory.Data.SetUnlimitedStackSize(false);
            binding.TankInventory.Data.SetUnlimitedSlots(false);
            cache.Bytes = binary.BitData;
        }
        return binding;
    }
    public static void Flush(ItemData suit)
    {
        var binary = FluidTankStorage.FindBinary(suit, Mod_Spacesuit.ModuleId);
        if (binary == null || !states.TryGetValue(binary, out Cache cache) || cache.Binding?.State == null) return;
        cache.Binding.State.OxygenTank = InventoryInstanceSnapshot.Capture(cache.Binding.TankInventory.Data);
        binary.WriteData(cache.Binding.State);
        cache.Bytes = binary.BitData;
    }
    public static bool IsActuallyEquipped(Item owner, ItemData suit)
    {
        var equipment = owner?.itemMods.GetMod_ByID<Mod_Equipment>(ModText.Equipment_Module);
        if (equipment?.EquipmentInventory?.Data?.itemSlots == null) return false;
        foreach (ItemSlot slot in equipment.EquipmentInventory.Data.itemSlots) if (ReferenceEquals(slot?.itemData, suit)) return true;
        return false;
    }
    public static bool TryGetEquipped(Item owner, out SpacesuitBinding binding)
    {
        binding = null;
        var equipment = owner?.itemMods.GetMod_ByID<Mod_Equipment>(ModText.Equipment_Module);
        if (equipment?.EquipmentInventory?.Data?.itemSlots == null) return false;
        foreach (ItemSlot slot in equipment.EquipmentInventory.Data.itemSlots)
            if (slot?.itemData != null && FluidTankStorage.FindBinary(slot.itemData, Mod_Spacesuit.ModuleId) != null)
            { binding = GetBinding(slot.itemData, owner); return true; }
        return false;
    }
    public static void RefreshProtection(Item owner)
    {
        if (owner == null) return;
        Protection source = protections.GetValue(owner, _ => new());
        Mod_Pressure pressure = owner.itemMods.GetMod_ByID<Mod_Pressure>(Mod_Pressure.ModuleId);
        bool active = TryGetEquipped(owner, out SpacesuitBinding suit) && suit.IsComplete && suit.IsHelmetSealed;
        float low = active ? suit.Configuration.LowPressureRangeIncreaseKPa : 0f;
        float high = active ? suit.Configuration.HighPressureRangeIncreaseKPa : 0f;
        if (source.Module != pressure || source.Active != active || source.SuitGuid != (suit?.Suit.Guid ?? 0) ||
            source.LowIncrease != low || source.HighIncrease != high)
        {
            if (source.Module != null) source.Module.RemoveSafePressureRangeModifier(source.Token);
            source.Module = pressure; source.Active = active; source.SuitGuid = suit?.Suit.Guid ?? 0;
            source.LowIncrease = low; source.HighIncrease = high;
            if (active && pressure != null) pressure.SetSafePressureRangeModifier(source.Token,
                suit.Configuration.LowPressureRangeIncreaseKPa, suit.Configuration.HighPressureRangeIncreaseKPa);
        }
    }
    public static void ClearProtection(Item owner)
    {
        if (owner == null || !protections.TryGetValue(owner, out Protection source)) return;
        if (source.Module != null) source.Module.RemoveSafePressureRangeModifier(source.Token);
        protections.Remove(owner);
    }
    #endregion
}

public sealed class SpacesuitInventoryRule : IModuleDataRule
{
    #region 插槽气罐冷载荷
    public bool CanStep(ModuleDataTickContext context) => GameNetwork.HasStateAuthority &&
        (context.Owner != null || context.MachineOwner != null) && context.ItemData != null;
    public void Step(ModuleDataTickContext context)
    {
        SpacesuitBinding suit = SpacesuitSystem.GetBinding(context.ItemData, context.Owner);
        if (suit == null) return;
        suit.TankInventory.MachineOwner = context.MachineOwner;
        // 内部气罐只随真实持有者推进，不会因衣服放进背包而冻结超压。
        suit.TankInventory.ModUpdate(context.DeltaTime);
        SpacesuitSystem.Flush(context.ItemData);
        context.InventoryData.NotifyItemStateChanged(context.ItemData);
    }
    #endregion
}
