using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FlatWorld.Networking;
using MemoryPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

[Serializable]
public sealed class FluidTankConfiguration
{
    #region 载体配置
    public string MaterialId = "core:iron";
    public double VolumeLiters = 4d;
    public double MinimumGasSpaceLiters = .04d;
    public double MaxSafePressureKPa = 600d;
    public float OverpressureDamagePerSecond = 2f;
    public float DamageIntervalSeconds = 1f;
    public double PhaseChangeMolesPerSecond = .2d;
    public bool SupportsSpacesuit = true;
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(MaterialId) || !FluidUnits.IsFinite(VolumeLiters) || VolumeLiters <= 0d ||
            !FluidUnits.IsFinite(MinimumGasSpaceLiters) || MinimumGasSpaceLiters <= 0d || MinimumGasSpaceLiters >= VolumeLiters ||
            !FluidUnits.IsFinite(MaxSafePressureKPa) || MaxSafePressureKPa <= 0d ||
            !float.IsFinite(OverpressureDamagePerSecond) || OverpressureDamagePerSecond < 0f ||
            !float.IsFinite(DamageIntervalSeconds) || DamageIntervalSeconds <= 0f ||
            !FluidUnits.IsFinite(PhaseChangeMolesPerSecond) || PhaseChangeMolesPerSecond <= 0d)
            throw new InvalidOperationException("独立气罐的材质、容积、耐压或损伤配置无效。");
    }
    #endregion
}

[Serializable, MemoryPackable]
public partial class FluidTankState
{
    #region 实例状态
    public FluidInventoryState Contents = new();
    public float OverpressureClock;
    public bool Ruptured;
    #endregion
}

/// <summary>世界独立气罐与库存中的冷载荷读取同一份持久状态。</summary>
public sealed class Mod_FluidTank : Module, IItemModuleDependencyBinder, IModuleJsonParameterValidator, IInteractable
{
    #region 气罐模块
    public const string ModuleId = "流体气罐模块";
    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.FixedInterval;
    public override float FixedTickInterval => .2f;
    public Ex_ModData_MemoryPackable modData = new() { ID = ModuleId };
    public override ModuleData _Data { get => modData; set => modData = (Ex_ModData_MemoryPackable)value; }
    public FluidTankConfiguration Configuration = new();
    private Mod_DamageReceiver health;
    public void BindModuleDependencies(ItemMods modules) => health = modules.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
    protected override void OnLoad()
    {
        Configuration.Validate();
        if (health == null) throw new InvalidOperationException("气罐必须声明同一份生命模块。");
        health.DeathStarted += OnTankDeath;
        FluidTankStorage.Ensure(item.itemData);
        item.OnAct += OpenHeld;
    }
    protected override void OnSave() => FluidTankStorage.Flush(item.itemData);
    protected override void OnUnload()
    {
        if (health != null) health.DeathStarted -= OnTankDeath;
        if (item != null) item.OnAct -= OpenHeld;
        PortableFluidPanel.Close(item);
        health = null;
    }
    public override void ModUpdate(float deltaTime)
    {
        if (!GameNetwork.HasStateAuthority || item == null || !IsRuntimeLoaded) return;
        FluidTankStorage.StepOverpressure(item.itemData, item.Owner != null ? item.Owner : item, deltaTime,
            RemoveLiveTank, health);
    }
    private void OnTankDeath(Mod_DamageReceiver receiver)
    {
        receiver.ConsumeCurrentDeath();
        FluidTankStorage.Rupture(item.itemData, item.Owner != null ? item.Owner : item, RemoveLiveTank);
    }
    private void OpenHeld()
    {
        if (item.Owner != null &&
            item.itemMods.GetMod_ByID<Mod_Building>(ModText.Building)?.IsPlacementModeActive != true)
            OnInteractStart(item.Owner);
    }
    private bool RemoveLiveTank()
    {
        if (item == null || ItemMgr.Instance == null) return false;
        Item tank = item;
        ItemData data = tank.itemData;
        Item owner = tank.Owner;
        if (owner != null)
        {
            if (!InventoryContextResolver.TryResolveContainingInventory(owner, data, out Inventory inventory)) return false;
            bool removed = false;
            foreach (ItemSlot slot in inventory.Data.itemSlots)
                if (slot.itemData != null && (ReferenceEquals(slot.itemData, data) ||
                    data.Guid != 0 && slot.itemData.Guid == data.Guid && slot.itemData.IDName == data.IDName))
                {
                    removed = inventory.Data.TryConsumeFromSlot(slot, 1, out _);
                    break;
                }
            if (!removed) return false;
        }
        if (tank != null) ItemMgr.Instance.DespawnItem(tank, false);
        return true;
    }
    public bool CanInteract(Item actor) => item != null && actor != null;
    public void OnInteractStart(Item actor) => PortableFluidPanel.Show(item, actor);
    public void OnInteractCancel(Item actor) => PortableFluidPanel.Close(item);
    public void ValidateJsonParameters(JObject parameters)
    {
        var config = parameters[nameof(Configuration)]?.ToObject<FluidTankConfiguration>() ?? Configuration;
        config.Validate();
    }
    #endregion
}

/// <summary>只读定义与实际二进制状态各有明确所有者，不从气罐名字推断供氧资格。</summary>
public static class FluidTankStorage
{
    #region 状态访问
    private sealed class Cache
    {
        public byte[] Bytes;
        public FluidTankState State;
        public FluidInventory Inventory;
        public long ValidatedRevision;
        public FluidInventoryState SavedContents;
        public long SavedRevision;
        public float SavedOverpressureClock;
        public bool SavedRuptured;
        public bool HasSnapshot;
    }
    private sealed class Clock { public double Last = double.NegativeInfinity; public uint MachineStep; }
    private static readonly ConditionalWeakTable<Ex_ModData_MemoryPackable, Cache> states = new();
    private static readonly ConditionalWeakTable<Ex_ModData_MemoryPackable, Clock> clocks = new();
    private static readonly ConditionalWeakTable<RuntimeItemModuleDefinition, FluidTankConfiguration> configurations = new();
    private static readonly ConditionalWeakTable<RuntimeItemModuleDefinition, FluidTankConfiguration>.CreateValueCallback createConfiguration = ReadTankConfiguration;
    private static readonly FluidTankConfiguration defaultConfiguration = new();
    public static bool TryGet(ItemData item, out FluidInventory inventory, out FluidTankConfiguration config)
    {
        if (!TryGetStorage(item, out inventory, out FluidTankConfiguration shared)) { config = null; return false; }
        // 对外仍返回独立配置，调用方修改字段不能污染另一只气罐或后续 Tick。
        config = new FluidTankConfiguration
        {
            MaterialId = shared.MaterialId, VolumeLiters = shared.VolumeLiters,
            MinimumGasSpaceLiters = shared.MinimumGasSpaceLiters, MaxSafePressureKPa = shared.MaxSafePressureKPa,
            OverpressureDamagePerSecond = shared.OverpressureDamagePerSecond, DamageIntervalSeconds = shared.DamageIntervalSeconds,
            PhaseChangeMolesPerSecond = shared.PhaseChangeMolesPerSecond, SupportsSpacesuit = shared.SupportsSpacesuit
        };
        return true;
    }
    private static bool TryGetStorage(ItemData item, out FluidInventory inventory, out FluidTankConfiguration config)
    {
        inventory = null; config = null;
        if (FindBinary(item, Mod_FluidTank.ModuleId) is not Ex_ModData_MemoryPackable binary) return false;
        config = GetTankConfiguration(item);
        FluidTankState state = GetState(binary);
        if (state.Ruptured) return false;
        Cache cache = states.GetValue(binary, _ => new Cache());
        if (cache.Inventory == null || !ReferenceEquals(cache.Inventory.State, state.Contents) ||
            !ReferenceEquals(cache.Inventory.Catalog, FluidCatalog.Default))
        {
            cache.Inventory = new FluidInventory(state.Contents);
            cache.ValidatedRevision = state.Contents.StateRevision;
        }
        else if (cache.ValidatedRevision != state.Contents.StateRevision)
        {
            FluidInventory.ValidateState(state.Contents, cache.Inventory.Catalog);
            cache.ValidatedRevision = state.Contents.StateRevision;
        }
        inventory = cache.Inventory;
        return true;
    }
    private static FluidTankConfiguration GetTankConfiguration(ItemData item)
    {
        if (GameRes.ExistingInstance != null &&
            GameRes.ExistingInstance.TryGetItemDefinition(item.IDName, out RuntimeItemDefinition definition))
            for (int index = 0; index < definition.ModuleDefinitions.Count; index++)
            {
                RuntimeItemModuleDefinition declaration = definition.ModuleDefinitions[index];
                if (declaration.Enabled && declaration.ModuleId == Mod_FluidTank.ModuleId && !string.IsNullOrWhiteSpace(declaration.ParametersJson))
                    return configurations.GetValue(declaration, createConfiguration);
            }
        return defaultConfiguration;
    }
    private static FluidTankConfiguration ReadTankConfiguration(RuntimeItemModuleDefinition declaration)
    {
        // 声明不可变，资源重载产生新声明后自然重新解析，不缓存可变生命等通用配置。
        FluidTankConfiguration config = JObject.Parse(declaration.ParametersJson)["Configuration"]?.ToObject<FluidTankConfiguration>() ?? new();
        config.Validate();
        return config;
    }
    // 计重只读真实组分，复用运行状态，不解析容量配置或创建库存包装器。
    public static bool TryReadContents(ItemData item, out FluidInventoryState contents)
    {
        contents = null;
        if (FindBinary(item, Mod_FluidTank.ModuleId) is not Ex_ModData_MemoryPackable binary) return false;
        FluidTankState state = GetState(binary);
        if (state.Ruptured) return false;
        contents = state.Contents;
        return true;
    }
    public static FluidTankState Ensure(ItemData item)
    {
        var binary = FindBinary(item, Mod_FluidTank.ModuleId) ?? throw new InvalidOperationException("物品没有气罐模块。");
        return GetState(binary);
    }
    public static void Write(ItemData item, FluidInventory inventory)
    {
        FluidTankState state = Ensure(item);
        state.Contents = inventory.State;
        Flush(item);
    }
    public static void Flush(ItemData item)
    {
        var binary = FindBinary(item, Mod_FluidTank.ModuleId);
        if (binary == null) return;
        FluidTankState state = GetState(binary);
        Cache cache = states.GetValue(binary, _ => new Cache());
        if (cache.HasSnapshot && ReferenceEquals(cache.SavedContents, state.Contents) &&
            cache.SavedRevision == state.Contents.StateRevision && cache.SavedOverpressureClock == state.OverpressureClock &&
            cache.SavedRuptured == state.Ruptured) return;
        binary.WriteData(state);
        cache.Bytes = binary.BitData;
        RememberSnapshot(cache, state);
    }
    private static FluidTankState GetState(Ex_ModData_MemoryPackable binary)
    {
        Cache cache = states.GetValue(binary, _ => new Cache());
        if (cache.State == null || !ReferenceEquals(cache.Bytes, binary.BitData))
        {
            FluidTankState state = binary.GetData<FluidTankState>() ?? new FluidTankState();
            // 新载荷入缓存前校验一次，计重不再每步创建校验集合。
            FluidInventory.ValidateState(state.Contents, FluidCatalog.Default);
            cache.State = state;
            cache.Bytes = binary.BitData;
            cache.Inventory = null;
            if (binary.BitData != null && binary.BitData.Length > 0) RememberSnapshot(cache, state);
            else cache.HasSnapshot = false;
        }
        return cache.State;
    }
    private static void RememberSnapshot(Cache cache, FluidTankState state)
    {
        cache.SavedContents = state.Contents;
        cache.SavedRevision = state.Contents.StateRevision;
        cache.SavedOverpressureClock = state.OverpressureClock;
        cache.SavedRuptured = state.Ruptured;
        cache.HasSnapshot = true;
    }
    public static Ex_ModData_MemoryPackable FindBinary(ItemData item, string id)
    {
        if (item?.ModuleDataDic == null) return null;
        foreach (ModuleData data in item.ModuleDataDic.Values)
            if (data.ModuleId == id && data.Enabled) return data as Ex_ModData_MemoryPackable;
        return null;
    }
    public static T ReadConfiguration<T>(ItemData item, string moduleId, string field) where T : class
    {
        if (GameRes.ExistingInstance == null || item == null ||
            !GameRes.ExistingInstance.TryGetItemDefinition(item.IDName, out RuntimeItemDefinition definition)) return null;
        foreach (RuntimeItemModuleDefinition declaration in definition.ModuleDefinitions)
            if (declaration.Enabled && declaration.ModuleId == moduleId && !string.IsNullOrWhiteSpace(declaration.ParametersJson))
            {
                JObject parameters = JObject.Parse(declaration.ParametersJson);
                return parameters[field]?.ToObject<T>();
            }
        return null;
    }
    public static bool IsPureOxygen(ItemData item, bool requireContents = true)
    {
        if (!TryGet(item, out var inventory, out var config) || !config.SupportsSpacesuit || inventory.LiquidMoles > 0m) return false;
        foreach (FluidComponentState component in inventory.State.Components)
            if (component.GasMoles > 0m && component.FluidId != FluidIds.Oxygen) return false;
        return !requireContents || inventory.GetGasMoles(FluidIds.Oxygen) > 0m;
    }
    #endregion

    #region 超压与一次性破裂
    public static bool StepOverpressure(ItemData item, Item carrier, float seconds, Func<bool> remove,
        Mod_DamageReceiver liveHealth = null, bool notifyOwner = true)
    {
        if (carrier == null) return false;
        bool changed = StepOverpressureAt(item, carrier.gameObject.scene.name,
            WorldLocalPresentation.ToLogical(carrier.transform.position), carrier, seconds, remove, liveHealth);
        if (changed && notifyOwner) NotifyOwner(carrier, item);
        return changed;
    }
    public static bool StepOverpressure(ItemData item, MachineEntity machine, float seconds, Func<bool> remove)
    {
        if (machine == null || !machine.Active) return false;
        return StepOverpressureAt(item, MachineWorld.WorldKey, machine.Position, null, seconds, remove, null);
    }
    private static bool StepOverpressureAt(ItemData item, string worldKey, Vector2 center, Item carrier,
        float seconds, Func<bool> remove, Mod_DamageReceiver liveHealth)
    {
        if (!GameNetwork.HasStateAuthority || string.IsNullOrWhiteSpace(worldKey) || !float.IsFinite(seconds) || seconds <= 0f ||
            !TryGetStorage(item, out FluidInventory inventory, out FluidTankConfiguration config)) return false;
        var binary = FindBinary(item, Mod_FluidTank.ModuleId);
        Clock clock = clocks.GetValue(binary, _ => new Clock());
        double now = Time.timeAsDouble;
        if (clock.Last == now && (carrier != null || clock.MachineStep == MachineWorld.TransportStep)) return false;
        clock.Last = now;
        clock.MachineStep = MachineWorld.TransportStep;
        FluidTankState state = Ensure(item);
        long previousContentRevision = inventory.State.ContentRevision;
        float previousDurability = item.Durability;
        float previousMaxDurability = item.MaxDurability;
        // 冷库存和世界实例走同一次相变结算，之后才按真实液相占容积测压。
        FluidThermodynamics.AdvancePhaseChange(inventory, config.VolumeLiters, config.MinimumGasSpaceLiters,
            seconds, config.PhaseChangeMolesPerSecond);
        double pressure = inventory.GetPressureKPa(config.VolumeLiters, config.MinimumGasSpaceLiters);
        if (pressure <= config.MaxSafePressureKPa)
        {
            state.OverpressureClock = 0f;
            Flush(item);
            return inventory.State.ContentRevision != previousContentRevision;
        }
        state.OverpressureClock += seconds;
        while (state.OverpressureClock >= config.DamageIntervalSeconds && !state.Ruptured)
        {
            state.OverpressureClock -= config.DamageIntervalSeconds;
            float damage = config.OverpressureDamagePerSecond * config.DamageIntervalSeconds;
            if (liveHealth != null)
            {
                liveHealth.ApplyStructuralDamage(damage);
                if (liveHealth == null || state.Ruptured) return true;
            }
            else
            {
                Ex_ModData healthData = FindHealth(item);
                if (healthData == null) throw new InvalidOperationException("库存气罐缺少真实生命状态：" + item.IDName);
                Mod_DamageReceiver.DamageReceiver_SaveData health = healthData.GetData<Mod_DamageReceiver.DamageReceiver_SaveData>()
                    ?? ReadConfiguration<Mod_DamageReceiver.DamageReceiver_SaveData>(item, ModText.Hp, "Data")
                    ?? throw new InvalidOperationException("气罐缺少初始生命配置：" + item.IDName);
                health.Hp = Mathf.Max(0f, health.Hp - damage);
                healthData.WriteData(health);
                item.Durability = health.Hp;
                item.MaxDurability = health.MaxHp;
                if (health.Hp <= 0f) { Flush(item); RuptureAt(item, worldKey, center, remove); return true; }
            }
        }
        Flush(item);
        if (liveHealth != null) { item.Durability = liveHealth.Hp; item.MaxDurability = liveHealth.MaxHp; }
        return inventory.State.ContentRevision != previousContentRevision || state.Ruptured ||
            item.Durability != previousDurability || item.MaxDurability != previousMaxDurability;
    }
    public static void Rupture(ItemData item, Item carrier, Func<bool> remove)
    {
        if (carrier == null) return;
        RuptureAt(item, carrier.gameObject.scene.name, WorldLocalPresentation.ToLogical(carrier.transform.position), remove);
    }
    private static void RuptureAt(ItemData item, string worldKey, Vector2 logicalCenter, Func<bool> remove)
    {
        if (!GameNetwork.HasStateAuthority || !TryGet(item, out var inventory, out var config)) return;
        FluidTankState state = Ensure(item);
        if (state.Ruptured) return;
        double pressure = inventory.GetPressureKPa(config.VolumeLiters, config.MinimumGasSpaceLiters);
        double ambient = AtmosphereService.TryGetForWorld(worldKey, out AtmosphereState atmosphere)
            ? AtmosphereService.PressureKPa(atmosphere) : 0d;
        double gasSpace = Math.Max(config.MinimumGasSpaceLiters, config.VolumeLiters - inventory.GetLiquidLiters());
        double intensity = Math.Max(0d, pressure - ambient) * gasSpace;
        Vector2 center = WorldTopologyRuntime.NormalizePosition(logicalCenter);
        string sourceId = "tank:" + item.Guid;
        var gas = new List<FluidBatch>(); var liquid = new List<FluidBatch>();
        FluidInventoryState before = inventory.State.Clone();
        foreach (FluidBatch batch in inventory.Drain())
        {
            var definition = FluidCatalog.Default.Find(batch.FluidId);
            double temp = (batch.InternalEnergyJoules - (double)batch.GasMoles * definition.GasEnergyOffsetJPerMol) /
                ((double)batch.GasMoles * definition.GasHeatCapacityJPerMolKelvin + (double)batch.LiquidMoles * definition.LiquidHeatCapacityJPerMolKelvin);
            if (batch.GasMoles > 0m) gas.Add(FluidInventory.CreateBatch(definition, batch.GasMoles, 0m, temp));
            if (batch.LiquidMoles > 0m) liquid.Add(FluidInventory.CreateBatch(definition, 0m, batch.LiquidMoles, temp));
        }
        state.Ruptured = true;
        Flush(item);
        if (remove == null || !remove())
        {
            state.Ruptured = false; inventory.Restore(before); Flush(item);
            throw new InvalidOperationException("气罐归零时未能删除对应实例，破裂未提交。");
        }
        PressureExplosionQueue.Enqueue(worldKey, center, intensity, sourceId, gas, liquid);
    }
    private static Ex_ModData FindHealth(ItemData item)
    {
        foreach (ModuleData data in item.ModuleDataDic.Values)
            if (data.ModuleId == ModText.Hp) return data as Ex_ModData;
        return null;
    }
    public static void NotifyOwner(Item owner, ItemData item, Inventory_Data inventoryData = null)
    {
        if (owner == null) return;
        if (inventoryData != null) inventoryData.NotifyItemStateChanged(item);
        else if (InventoryContextResolver.TryResolveContainingInventory(owner, item, out Inventory inventory)) inventory.Data.NotifyItemStateChanged(item);
        owner.OnUIRefresh?.Invoke();
        ItemNetworkStateSerialization.NotifyRuntimeStateChanged(owner);
    }
    #endregion
}

public sealed class FluidTankInventoryRule : IModuleDataRule
{
    #region 冷载荷规则
    public FluidTankInventoryRule(RuntimeItemModuleDefinition definition) { }
    public bool CanStep(ModuleDataTickContext context) => GameNetwork.HasStateAuthority &&
        (context.Owner != null || context.MachineOwner != null) && context.ItemData != null;
    public void Step(ModuleDataTickContext context)
    {
        if (ItemMgr.Instance != null && ItemMgr.Instance.WorldRunTimeItems.TryGetValue(context.ItemData.Guid, out Item live) && live != null &&
            live.itemMods.GetMod_ByID<Mod_FluidTank>(Mod_FluidTank.ModuleId) is Mod_FluidTank loaded && loaded.IsRuntimeLoaded) return;
        Func<bool> remove = () => ReferenceEquals(context.Slot.itemData, context.ItemData) &&
            context.InventoryData.TryConsumeFromSlot(context.Slot, 1, out _);
        bool changed = context.Owner != null
            ? FluidTankStorage.StepOverpressure(context.ItemData, context.Owner, context.DeltaTime, remove, notifyOwner: false)
            : FluidTankStorage.StepOverpressure(context.ItemData, context.MachineOwner, context.DeltaTime, remove);
        // 稳定气罐不刷新槽位，实际相变或损伤只向真实所属库存发一次通知。
        if (changed && ReferenceEquals(context.Slot.itemData, context.ItemData))
        {
            if (context.Owner != null) FluidTankStorage.NotifyOwner(context.Owner, context.ItemData, context.InventoryData);
            else context.InventoryData.NotifyItemStateChanged(context.ItemData);
        }
    }
    #endregion
}
