using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FlatWorld.Networking;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Pool;

/// <summary>蒸汽提供方只认识引擎数据与资源库存，水和燃料齐全后才原子提交。</summary>
public sealed class SteamEnginePowerProvider : IEnginePowerProvider
{
    #region 引擎状态与配置缓存
    public string ModuleId => Mod_SteamEngine.ModuleId;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void Register() => EnginePowerService.Register(new SteamEnginePowerProvider());
    private sealed class StateCache { public string Bytes; public SteamEngineState State; }
    private sealed class ConfigurationCache
    {
        public string Parameters;
        public float Power = 1f, Water = .02f, FuelSeconds = 60f;
    }
    private sealed class VesselCache
    {
        public byte[] Bytes;
        public Ex_ModData_MemoryPackable Storage;
        public LiquidContainerState State;
    }
    private sealed class ResourceCache
    {
        public uint Revision = uint.MaxValue;
        public readonly List<ItemSlot> Water = new(), Fuel = new();
    }
    private static readonly ConditionalWeakTable<Ex_ModData, StateCache> states = new();
    private static readonly ConditionalWeakTable<ItemData, ConfigurationCache> configurations = new();
    private static readonly ConditionalWeakTable<ItemData, VesselCache> vessels = new();
    private static readonly ConditionalWeakTable<Inventory_Data, ResourceCache> resources = new();
    public static SteamEngineState ReadState(Ex_ModData storage)
    {
        if (storage == null) throw new ArgumentNullException(nameof(storage));
        StateCache cached = states.GetOrCreateValue(storage);
        if (cached.State == null || !ReferenceEquals(cached.Bytes, storage.BitData))
        {
            cached.State = storage.GetData<SteamEngineState>() ?? new SteamEngineState();
            cached.Bytes = storage.BitData;
            if (cached.State.Version != 1 || cached.State.OutputLevel < 1 || cached.State.OutputLevel > 3 ||
                !float.IsFinite(cached.State.StoredFuelSeconds) || cached.State.StoredFuelSeconds < 0f)
                throw new InvalidOperationException("蒸汽机开关、档位或燃料储备状态无效。");
        }
        return cached.State;
    }
    public static void WriteState(Ex_ModData storage, SteamEngineState state)
    {
        storage.WriteData(state);
        StateCache cached = states.GetOrCreateValue(storage);
        cached.State = state; cached.Bytes = storage.BitData;
    }
    private static ConfigurationCache Configuration(ItemData engine, ModuleData storage)
    {
        ConfigurationCache cached = configurations.GetOrCreateValue(engine);
        string json = null;
        if (GameRes.ExistingInstance?.TryGetItemDefinition(engine.IDName, out RuntimeItemDefinition definition) == true)
            definition.TryGetModuleParameters(storage.Name, out json);
        if (!ReferenceEquals(cached.Parameters, json))
        {
            JObject parameters = string.IsNullOrWhiteSpace(json) ? null : JObject.Parse(json);
            cached.Parameters = json;
            cached.Power = parameters?.Value<float?>(nameof(Mod_SteamEngine.MaximumPower)) ?? 1f;
            cached.Water = parameters?.Value<float?>(nameof(Mod_SteamEngine.WaterPerSecond)) ?? .02f;
            cached.FuelSeconds = parameters?.Value<float?>(nameof(Mod_SteamEngine.FuelSecondsPerUnit)) ?? 60f;
            if (!SteamEngineRules.Positive(cached.Power) || !SteamEngineRules.Positive(cached.Water) || !SteamEngineRules.Positive(cached.FuelSeconds))
                throw new InvalidOperationException("蒸汽机功率、水耗和燃料换算必须为有限正数。");
        }
        return cached;
    }
    private static ResourceCache Resources(Inventory_Data inventory)
    {
        ResourceCache cached = resources.GetOrCreateValue(inventory);
        uint revision = inventory.ContentStructureRevision;
        if (cached.Revision == revision) return cached;
        cached.Revision = revision; cached.Water.Clear(); cached.Fuel.Clear();
        if (inventory.itemSlots == null) return cached;
        foreach (ItemSlot slot in inventory.itemSlots)
        {
            ItemData data = slot?.itemData;
            if (data?.ModuleDataDic == null) continue;
            if (data.Tags?.ContainsTag(Tag.CombustionFuel) == true) cached.Fuel.Add(slot);
            foreach (ModuleData module in data.ModuleDataDic.Values)
                if (module?.ID == Mod_WaterVessel.ModuleId && module.Enabled) { cached.Water.Add(slot); break; }
        }
        return cached;
    }
    private static bool TryVessel(ItemData data, out VesselCache vessel)
    {
        vessel = null;
        if (data?.ModuleDataDic == null || !(data.Stack?.Amount == 1f)) return false;
        foreach (ModuleData module in data.ModuleDataDic.Values)
        {
            if (module?.ID != Mod_WaterVessel.ModuleId || !module.Enabled || module is not Ex_ModData_MemoryPackable storage) continue;
            VesselCache cached = vessels.GetOrCreateValue(data);
            if (cached.State == null || !ReferenceEquals(cached.Bytes, storage.BitData))
            {
                if (!Mod_WaterVessel.TryRead(data, out cached.Storage, out cached.State)) return false;
                cached.Bytes = cached.Storage.BitData;
            }
            vessel = cached; return true;
        }
        return false;
    }
    private static bool IsWater(string id)
        => string.Equals(GameRes.ExistingInstance?.GetLiquidDefinition(id)?.Category, "water", StringComparison.OrdinalIgnoreCase);
    private static float WaterAvailable(ResourceCache resource)
    {
        float amount = 0f;
        foreach (ItemSlot slot in resource.Water)
            if (TryVessel(slot.itemData, out VesselCache vessel))
                foreach (var component in vessel.State.Composition)
                    if (IsWater(component.Key)) amount += Mathf.Max(0f, component.Value - MixedLiquidContents.Reserved(vessel.State, component.Key));
        return amount;
    }
    private static bool TryFuel(ItemSlot slot, out float value)
    {
        value = 0f;
        ItemData data = slot?.itemData;
        if (!(data?.Stack?.Amount >= 1f) || data.Tags?.ContainsTag(Tag.CombustionFuel) != true || !Mod_Fuel.TryResolveItemData(data, out FuelData fuel)) return false;
        value = fuel.Fuel.x;
        return SteamEngineRules.Positive(value);
    }
    private static float FuelAvailable(ResourceCache resource, ConfigurationCache configuration, SteamEngineState state)
    {
        float amount = state.StoredFuelSeconds;
        foreach (ItemSlot slot in resource.Fuel)
            if (TryFuel(slot, out float value)) amount += value * configuration.FuelSeconds * Mathf.Floor(slot.itemData.Stack.Amount);
        return amount;
    }
    #endregion

    #region 供能预检与资源事务
    public float AvailablePower(ItemData engine, ModuleData module, Inventory_Data inventory)
    {
        if (module is not Ex_ModData storage || !storage.Enabled || inventory == null) return 0f;
        SteamEngineState state = ReadState(storage);
        if (!state.Enabled) return 0f;
        ConfigurationCache configuration = Configuration(engine, storage);
        ResourceCache resource = Resources(inventory);
        float fuel = GameRuleConfiguration.Current.Production.FuelConsumptionMultiplier == 0f ? 1f : FuelAvailable(resource, configuration, state);
        return SteamEngineRules.AvailablePower(configuration.Power, state.OutputLevel, state.Enabled, WaterAvailable(resource), fuel);
    }
    private readonly struct WaterDebit
    {
        public readonly ItemData Data;
        public readonly VesselCache Vessel;
        public readonly string Id;
        public readonly float Amount;
        public readonly byte[] OriginalBytes;
        public WaterDebit(ItemData data, VesselCache vessel, string id, float amount)
        { Data = data; Vessel = vessel; Id = id; Amount = amount; OriginalBytes = vessel.Storage.BitData; }
    }
    private readonly struct FuelDebit
    {
        public readonly ItemSlot Slot;
        public readonly ItemData Data;
        public readonly int Amount;
        public readonly float OriginalAmount, Durability, Seconds;
        public FuelDebit(ItemSlot slot, int amount, float seconds)
        { Slot = slot; Data = slot.itemData; Amount = amount; OriginalAmount = Data.Stack.Amount; Durability = Data.Durability; Seconds = seconds; }
    }
    public bool TrySupply(ItemData engine, ModuleData module, Inventory_Data inventory, float seconds, float requestedPower, out float suppliedPower)
    {
        suppliedPower = 0f;
        if (!GameNetwork.HasStateAuthority || module is not Ex_ModData storage || inventory == null || !SteamEngineRules.Positive(seconds) || !SteamEngineRules.Positive(requestedPower)) return false;
        SteamEngineState state = ReadState(storage);
        ConfigurationCache configuration = Configuration(engine, storage);
        float power = state.Enabled && storage.Enabled ? Mathf.Min(requestedPower, configuration.Power * (state.OutputLevel / 3f)) : 0f;
        if (!SteamEngineRules.Positive(power)) return false;
        float workSeconds = seconds * power / configuration.Power;
        float waterNeeded = workSeconds * configuration.Water;
        float fuelNeeded = workSeconds * GameRuleConfiguration.Current.Production.FuelConsumptionMultiplier;
        if (!SteamEngineRules.Positive(workSeconds) || !SteamEngineRules.Positive(waterNeeded) || !float.IsFinite(fuelNeeded) || fuelNeeded < 0f) return false;
        ResourceCache resource = Resources(inventory);
        if (!SteamEngineRules.CanSupply(waterNeeded, fuelNeeded, WaterAvailable(resource), FuelAvailable(resource, configuration, state))) return false;
        using var waterLease = ListPool<WaterDebit>.Get(out List<WaterDebit> waterPlan);
        using var fuelLease = ListPool<FuelDebit>.Get(out List<FuelDebit> fuelPlan);
        float remainingWater = waterNeeded, remainingFuel = Mathf.Max(0f, fuelNeeded - state.StoredFuelSeconds);
        foreach (ItemSlot slot in resource.Water)
        {
            if (!TryVessel(slot.itemData, out VesselCache vessel)) continue;
            foreach (var component in vessel.State.Composition)
            {
                if (!IsWater(component.Key)) continue;
                float amount = Mathf.Min(remainingWater, Mathf.Max(0f, component.Value - MixedLiquidContents.Reserved(vessel.State, component.Key)));
                if (amount > 0f) { waterPlan.Add(new WaterDebit(slot.itemData, vessel, component.Key, amount)); remainingWater -= amount; }
                if (remainingWater <= .0000001f) break;
            }
            if (remainingWater <= .0000001f) break;
        }
        foreach (ItemSlot slot in resource.Fuel)
        {
            if (remainingFuel <= .0000001f) break;
            if (!TryFuel(slot, out float value)) continue;
            float perItem = value * configuration.FuelSeconds;
            int amount = Mathf.Min(Mathf.FloorToInt(slot.itemData.Stack.Amount), Mathf.CeilToInt(remainingFuel / perItem));
            if (amount > 0) { fuelPlan.Add(new FuelDebit(slot, amount, perItem * amount)); remainingFuel -= perItem * amount; }
        }
        if (remainingWater > .0000001f || remainingFuel > .0000001f) return false;
        int consumed = 0;
        float gainedFuel = 0f;
        float originalFuel = state.StoredFuelSeconds;
        string originalEngineBytes = storage.BitData;
        try
        {
            foreach (FuelDebit debit in fuelPlan)
            {
                if (!ReferenceEquals(debit.Slot.itemData, debit.Data))
                    throw new InvalidOperationException("蒸汽机燃料库存已改变。");
                consumed++;
                if (!inventory.TryConsumeFromSlot(debit.Slot, debit.Amount, out _))
                    throw new InvalidOperationException("蒸汽机燃料库存提交失败。");
                gainedFuel += debit.Seconds;
            }
            foreach (WaterDebit debit in waterPlan)
                if (!ReferenceEquals(debit.Vessel.Bytes, debit.Vessel.Storage.BitData) || !inventory.itemSlots.Exists(slot => ReferenceEquals(slot?.itemData, debit.Data)))
                    throw new InvalidOperationException("蒸汽机水容器已改变。");
            foreach (WaterDebit debit in waterPlan)
                if (!MixedLiquidContents.RemoveSpecies(debit.Vessel.State, debit.Id, debit.Amount))
                    throw new InvalidOperationException("蒸汽机预留水量提交失败。");
            state.StoredFuelSeconds = Mathf.Max(0f, state.StoredFuelSeconds + gainedFuel - fuelNeeded);
            WriteState(storage, state);
            foreach (WaterDebit debit in waterPlan)
            {
                debit.Vessel.Storage.WriteData(debit.Vessel.State); debit.Vessel.Bytes = debit.Vessel.Storage.BitData;
            }
        }
        catch
        {
            storage.BitData = originalEngineBytes; state.StoredFuelSeconds = originalFuel;
            StateCache engineCache = states.GetOrCreateValue(storage);
            engineCache.State = state; engineCache.Bytes = originalEngineBytes;
            for (int i = consumed - 1; i >= 0; i--)
            {
                FuelDebit debit = fuelPlan[i]; debit.Data.Durability = debit.Durability;
                debit.Data.Stack.Amount = debit.OriginalAmount;
                if (debit.Slot.itemData == null || ReferenceEquals(debit.Slot.itemData, debit.Data)) debit.Slot.itemData = debit.Data;
            }
            foreach (WaterDebit debit in waterPlan)
            { debit.Vessel.Storage.BitData = debit.OriginalBytes; debit.Vessel.Bytes = debit.OriginalBytes; debit.Vessel.State = null; }
            foreach (FuelDebit debit in fuelPlan) PublishSafely(inventory, debit.Data);
            foreach (WaterDebit debit in waterPlan) PublishSafely(inventory, debit.Data);
            return false;
        }
        foreach (WaterDebit debit in waterPlan) PublishSafely(inventory, debit.Data);
        suppliedPower = power; return true;
    }
    private static void PublishSafely(Inventory_Data inventory, ItemData data)
    {
        // 库存已经提交，表现监听异常不能把成功交易回滚成半份资源。
        try { inventory.NotifyItemStateChanged(data); }
        catch (Exception exception) { Debug.LogException(exception); }
    }
    #endregion
}

public static class SteamEngineRules
{
    #region 蒸汽供能数值规则
    public static bool Positive(float value) => float.IsFinite(value) && value > 0f;
    public static float AvailablePower(float maximumPower, int level, bool enabled, float water, float fuelSeconds)
        => enabled && level >= 1 && level <= 3 && Positive(maximumPower) && Positive(water) && Positive(fuelSeconds) ? maximumPower * (level / 3f) : 0f;
    public static bool CanSupply(float waterNeeded, float fuelNeeded, float water, float fuelSeconds)
        => Positive(waterNeeded) && float.IsFinite(fuelNeeded) && fuelNeeded >= 0f && float.IsFinite(water) && float.IsFinite(fuelSeconds) &&
           water >= waterNeeded && fuelSeconds >= fuelNeeded;
    #endregion
}
