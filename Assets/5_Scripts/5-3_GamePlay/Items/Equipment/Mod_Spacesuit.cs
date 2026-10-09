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
    public float PropulsionAcceleration = 3f;
    public decimal PropulsionMolesPerSecond = .12m;
    public float ImpactBufferSeconds = .5f;
    public float MaximumImpactProtection = 80f;
    public float ImpactDurabilityPerDamage = 1f;
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(SetId) || RequiredPartTags == null || RequiredPartTags.Length == 0 ||
            !float.IsFinite(LowPressureRangeIncreaseKPa) || LowPressureRangeIncreaseKPa < 0f ||
            !float.IsFinite(HighPressureRangeIncreaseKPa) || HighPressureRangeIncreaseKPa < 0f || OxygenMolesPerSecond <= 0m ||
            !float.IsFinite(PropulsionAcceleration) || PropulsionAcceleration <= 0f || PropulsionMolesPerSecond <= 0m ||
            !float.IsFinite(ImpactBufferSeconds) || ImpactBufferSeconds < 0f ||
            !float.IsFinite(MaximumImpactProtection) || MaximumImpactProtection < 0f ||
            !float.IsFinite(ImpactDurabilityPerDamage) || ImpactDurabilityPerDamage <= 0f)
            throw new InvalidOperationException("宇航服的套装、耐压、气体消耗或冲击保护定义无效。");
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
    public InventoryInstanceSnapshot PropulsionTank;
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
        suit.PropulsionTankInventory.MachineOwner = null;
        suit.PropulsionTankInventory.ModUpdate(deltaTime);
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

public class SpacesuitTankInventory : Inventory
{
    #region 单罐接收
    public override bool CanAcceptQuickTransfer(ItemSlot sourceSlot, ItemSlot targetSlot)
        => base.CanAcceptQuickTransfer(sourceSlot, targetSlot) && sourceSlot?.itemData != null &&
            sourceSlot.itemData.Stack.Amount == 1f && AcceptsTank(sourceSlot.itemData);
    protected virtual bool AcceptsTank(ItemData tank) => FluidTankStorage.IsPureOxygen(tank, requireContents: false);
    #endregion
}

public sealed class SpacesuitPropulsionTankInventory : SpacesuitTankInventory
{
    #region 独立推进气罐
    protected override bool AcceptsTank(ItemData tank) => FluidTankStorage.TryGet(tank, out var contents, out var config) &&
        config.SupportsSpacesuit && contents.LiquidMoles == 0m;
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
    public SpacesuitPropulsionTankInventory PropulsionTankInventory { get; internal set; }
    public ItemData Tank => TankInventory.Data.itemSlots.Count == 1 ? TankInventory.Data.itemSlots[0].itemData : null;
    public ItemData PropulsionTank => PropulsionTankInventory.Data.itemSlots.Count == 1 ? PropulsionTankInventory.Data.itemSlots[0].itemData : null;
    public bool IsEquipped => Owner != null && SpacesuitSystem.IsActuallyEquipped(Owner, Suit);
    public bool HasHelmet => IsEquipped && HasPart("Spacesuit.Head");
    public bool IsHelmetSealed => IsEquipped && Suit.Durability > 0f && State.HelmetOn && HasHelmet;
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
            if (slot?.itemData?.Tags?.Contains(tag) == true && slot.itemData.Durability > 0f &&
                slot.itemData.Tags.Contains("SpacesuitSet:" + Configuration.SetId)) return true;
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
    public bool TryApplyPropulsion(float seconds, Vector2 worldDirection, out Vector2 deltaVelocity)
    {
        deltaVelocity = Vector2.zero;
        if (!GameNetwork.HasStateAuthority || !IsEquipped || Suit.Durability <= 0f || !float.IsFinite(seconds) || seconds <= 0f ||
            !float.IsFinite(worldDirection.x) || !float.IsFinite(worldDirection.y) || worldDirection.sqrMagnitude <= .000001f ||
            !FluidTankStorage.TryGet(PropulsionTank, out FluidInventory source, out _) || source.LiquidMoles > 0m) return false;
        // 推进只扣独立喷气罐，保留有限余气对应的那部分真实推力。
        decimal available = 0m;
        foreach (FluidComponentState component in source.State.Components)
            available += source.GetAvailableMoles(component.FluidId, FluidPhase.Gas);
        decimal requested = Configuration.PropulsionMolesPerSecond * (decimal)(seconds * Mathf.Clamp01(worldDirection.magnitude));
        decimal actual = Math.Min(available, requested);
        if (actual <= 0m) return false;
        FluidInventory candidate = new(MachinePersistence.Clone(source.State));
        foreach (FluidComponentState component in source.State.Components)
        {
            decimal quantity = source.GetAvailableMoles(component.FluidId, FluidPhase.Gas);
            decimal consumed = actual * quantity / available;
            if (consumed > 0m && !candidate.TryTakeExact(component.FluidId, FluidPhase.Gas, consumed, out _)) return false;
        }
        FluidTankStorage.Write(PropulsionTank, candidate);
        PropulsionTankInventory.Data.NotifyItemStateChanged(PropulsionTank);
        SpacesuitSystem.Flush(Suit);
        FluidTankStorage.NotifyOwner(Owner, Suit);
        deltaVelocity = Vector2.ClampMagnitude(worldDirection, 1f) * Configuration.PropulsionAcceleration * seconds * (float)(actual / requested);
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
            var propulsion = new SpacesuitPropulsionTankInventory
                { item = owner, Data = new Inventory_Data(new List<ItemSlot> { new(0) }, "推进气罐") };
            propulsion.Data.SetUnlimitedStackSize(false);
            propulsion.Data.SetUnlimitedSlots(false);
            propulsion.InitData();
            propulsion.Data.itemSlots[0].SlotMaxVolume = 1f;
            cache.Binding = new SpacesuitBinding
                { Suit = suit, Owner = owner, Configuration = config, TankInventory = inventory, PropulsionTankInventory = propulsion };
            inventory.Data.Event_OnDataChanged += _ =>
            {
                Flush(cache.Binding.Suit);
                FluidTankStorage.NotifyOwner(cache.Binding.Owner, cache.Binding.Suit);
            };
            propulsion.Data.Event_OnDataChanged += _ =>
            {
                Flush(cache.Binding.Suit);
                FluidTankStorage.NotifyOwner(cache.Binding.Owner, cache.Binding.Suit);
            };
        }
        SpacesuitBinding binding = cache.Binding;
        binding.Suit = suit;
        binding.Owner = owner;
        binding.TankInventory.item = owner;
        binding.PropulsionTankInventory.item = owner;
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
            if (incoming.PropulsionTank != null && incoming.PropulsionTank.SlotCount > 1)
                throw new InvalidOperationException("宇航服快照只能包含一个推进气罐槽。");
            if (incoming.PropulsionTank == null) binding.PropulsionTankInventory.Data.itemSlots[0].itemData = null;
            else incoming.PropulsionTank.RestoreTo(binding.PropulsionTankInventory.Data,
                data => data.SharedConfiguration == null ? ItemDefinitionRuntime.RebasePersistedData(resources, data) : data);
            if (binding.PropulsionTank != null && (binding.PropulsionTank.Stack?.Amount != 1f ||
                !FluidTankStorage.TryGet(binding.PropulsionTank, out var propulsionContents, out var propulsionConfig) ||
                !propulsionConfig.SupportsSpacesuit || propulsionContents.LiquidMoles > 0m))
                throw new InvalidOperationException("宇航服推进槽的物品不符合独立喷气罐定义。");
            binding.TankInventory.Data.itemSlots[0].SlotMaxVolume = 1f;
            binding.TankInventory.Data.SetUnlimitedStackSize(false);
            binding.TankInventory.Data.SetUnlimitedSlots(false);
            binding.PropulsionTankInventory.Data.itemSlots[0].SlotMaxVolume = 1f;
            binding.PropulsionTankInventory.Data.SetUnlimitedStackSize(false);
            binding.PropulsionTankInventory.Data.SetUnlimitedSlots(false);
            cache.Bytes = binary.BitData;
        }
        return binding;
    }
    public static void Flush(ItemData suit)
    {
        var binary = FluidTankStorage.FindBinary(suit, Mod_Spacesuit.ModuleId);
        if (binary == null || !states.TryGetValue(binary, out Cache cache) || cache.Binding?.State == null) return;
        cache.Binding.State.OxygenTank = InventoryInstanceSnapshot.Capture(cache.Binding.TankInventory.Data);
        cache.Binding.State.PropulsionTank = InventoryInstanceSnapshot.Capture(cache.Binding.PropulsionTankInventory.Data);
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
    public static bool TryApplyPropulsion(Item owner, float deltaTime, Vector2 worldDirection, out Vector2 deltaVelocity)
    {
        deltaVelocity = Vector2.zero;
        return TryGetEquipped(owner, out SpacesuitBinding binding) &&
            binding.TryApplyPropulsion(deltaTime, worldDirection, out deltaVelocity);
    }
    public static float AbsorbLandingImpact(Item owner, float incomingDamage)
    {
        if (!float.IsFinite(incomingDamage) || incomingDamage < 0f) throw new ArgumentOutOfRangeException(nameof(incomingDamage));
        if (!GameNetwork.HasStateAuthority || incomingDamage == 0f || !TryGetEquipped(owner, out SpacesuitBinding suit) ||
            !suit.IsEquipped || suit.Suit.Durability <= 0f || suit.Configuration.ImpactBufferSeconds <= 0f ||
            suit.Configuration.MaximumImpactProtection <= 0f) return incomingDamage;
        Ex_ModData savedHealth = null;
        foreach (ModuleData data in suit.Suit.ModuleDataDic.Values)
            if (data.ModuleId == ModText.Hp && data.Enabled) { savedHealth = data as Ex_ModData; break; }
        if (savedHealth == null) throw new InvalidOperationException("宇航服冲击保护缺少真实生命状态：" + suit.Suit.IDName);
        Mod_DamageReceiver.DamageReceiver_SaveData health = savedHealth.GetData<Mod_DamageReceiver.DamageReceiver_SaveData>()
            ?? FluidTankStorage.ReadConfiguration<Mod_DamageReceiver.DamageReceiver_SaveData>(suit.Suit, ModText.Hp, "Data")
            ?? throw new InvalidOperationException("宇航服冲击保护缺少初始生命配置：" + suit.Suit.IDName);
        double stopping = FlatWorld.Spaceflight.SpaceGameplaySettings.Current.ImpactStoppingSeconds;
        if (!double.IsFinite(stopping) || stopping <= 0d) throw new InvalidOperationException("落地冲击缓冲时间必须为正数。");
        float remainingDurability = Mathf.Max(0f, Mathf.Min(health.Hp, suit.Suit.Durability));
        // 延长停撞时间减轻冲击，吸收上限同时受真实护甲耐久约束。
        float requested = incomingDamage * (float)(suit.Configuration.ImpactBufferSeconds / (stopping + suit.Configuration.ImpactBufferSeconds));
        float absorbed = Mathf.Min(requested, suit.Configuration.MaximumImpactProtection,
            remainingDurability / suit.Configuration.ImpactDurabilityPerDamage);
        if (absorbed <= 0f) return incomingDamage;
        health.Hp = Mathf.Max(0f, remainingDurability - absorbed * suit.Configuration.ImpactDurabilityPerDamage);
        savedHealth.WriteData(health);
        suit.Suit.Durability = health.Hp;
        suit.Suit.MaxDurability = health.MaxHp;
        RefreshProtection(owner);
        FluidTankStorage.NotifyOwner(owner, suit.Suit);
        return Mathf.Max(0f, incomingDamage - absorbed);
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
        suit.PropulsionTankInventory.MachineOwner = context.MachineOwner;
        suit.PropulsionTankInventory.ModUpdate(context.DeltaTime);
        SpacesuitSystem.Flush(context.ItemData);
        context.InventoryData.NotifyItemStateChanged(context.ItemData);
    }
    #endregion
}
