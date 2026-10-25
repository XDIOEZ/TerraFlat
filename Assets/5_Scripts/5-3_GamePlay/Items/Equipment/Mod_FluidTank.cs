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
    public double GasCapacityStandardLiters = 20d;
    public double NaturalFillFraction = .2d;
    public bool SupportsSpacesuit = true;
    public bool AllowManualAirFill; // 手动充空气由气罐定义声明，不按材质或物品名判断。
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(MaterialId) || !FluidUnits.IsFinite(VolumeLiters) || VolumeLiters <= 0d ||
            !FluidUnits.IsFinite(MinimumGasSpaceLiters) || MinimumGasSpaceLiters <= 0d || MinimumGasSpaceLiters >= VolumeLiters ||
            !FluidUnits.IsFinite(GasCapacityStandardLiters) || GasCapacityStandardLiters <= 0d ||
            !FluidUnits.IsFinite(NaturalFillFraction) || NaturalFillFraction < 0d || NaturalFillFraction > 1d)
            throw new InvalidOperationException("独立气罐的材质、容量或自然补气占比配置无效。");
    }
    #endregion
}

[Serializable, MemoryPackable]
public partial class FluidTankState
{
    #region 实例状态
    public FluidInventoryState Contents = new();
    #endregion
}

/// <summary>世界独立气罐与库存中的冷载荷读取同一份持久状态。</summary>
public sealed class Mod_FluidTank : Module, IItemModuleDependencyBinder, IModuleJsonParameterValidator, IInteractable
{
    #region 气罐模块
    public const string ModuleId = "流体气罐模块";
    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;
    public Ex_ModData_MemoryPackable modData = new() { ID = ModuleId };
    public override ModuleData _Data { get => modData; set => modData = (Ex_ModData_MemoryPackable)value; }
    public FluidTankConfiguration Configuration = new();
    private Mod_DamageReceiver health;
    public void BindModuleDependencies(ItemMods modules) => health = modules.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
    protected override void OnLoad()
    {
        Configuration.Validate();
        if (health != null) health.DeathStarted += OnTankDeath;
        FluidTankStorage.Ensure(item.itemData);
        item.OnAct += OpenHeld;
    }
    protected override void OnSave() => FluidTankStorage.Flush(item.itemData);
    protected override void OnUnload()
    {
        if (health != null) health.DeathStarted -= OnTankDeath;
        if (item != null) item.OnAct -= OpenHeld;
        PortableFluidPanel.Close(item);
    }
    private void OnTankDeath(Mod_DamageReceiver receiver)
    {
        // 普通受击销毁只释放实际气体，死亡和掉落仍由生命模块负责。
        if (GameNetwork.HasStateAuthority) PortableGasTankAirFilling.ReleaseContents(item);
    }
    private void OpenHeld()
    {
        if (item.Owner != null &&
            item.itemMods.GetMod_ByID<Mod_Building>(ModText.Building)?.IsPlacementModeActive != true)
            OnInteractStart(item.Owner);
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
        public bool HasSnapshot;
    }
    private static readonly ConditionalWeakTable<Ex_ModData_MemoryPackable, Cache> states = new();
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
            MinimumGasSpaceLiters = shared.MinimumGasSpaceLiters, GasCapacityStandardLiters = shared.GasCapacityStandardLiters,
            NaturalFillFraction = shared.NaturalFillFraction, SupportsSpacesuit = shared.SupportsSpacesuit,
            AllowManualAirFill = shared.AllowManualAirFill
        };
        return true;
    }
    public static bool AllowsManualAirFill(ItemData item) =>
        FindBinary(item, Mod_FluidTank.ModuleId) != null && GetTankConfiguration(item).AllowManualAirFill;

    private static bool TryGetStorage(ItemData item, out FluidInventory inventory, out FluidTankConfiguration config)
    {
        inventory = null; config = null;
        if (FindBinary(item, Mod_FluidTank.ModuleId) is not Ex_ModData_MemoryPackable binary) return false;
        config = GetTankConfiguration(item);
        FluidTankState state = GetState(binary);
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
            cache.SavedRevision == state.Contents.StateRevision) return;
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

    #region 所属库存通知
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
