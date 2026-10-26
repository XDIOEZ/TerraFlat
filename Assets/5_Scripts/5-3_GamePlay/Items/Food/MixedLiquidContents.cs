using System;
using System.Collections.Generic;
using MemoryPack;
using UnityEngine;

[Serializable, MemoryPackable]
public sealed partial class LiquidPendingBatch
{
    public string LiquidId;
    public float Servings = 1f;
}

public partial class LiquidContainerState
{
    #region 混合液体持久状态
    public Dictionary<string, float> Composition = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, float> ComponentProcessingSeconds = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, LiquidPendingBatch> PendingOutputs = new(StringComparer.Ordinal);
    public uint RandomState;
    public long Revision;
    [field: NonSerialized, MemoryPackIgnore, Newtonsoft.Json.JsonIgnore, FastCloner.Code.FastClonerIgnore]
    public event Action<LiquidContainerState> MassChanged;
    [MemoryPackIgnore, Newtonsoft.Json.JsonIgnore] public ulong MassRevision { get; private set; }
    // 真实组分提交后通知计重，端口预留和加热不改变库存质量。
    public void NotifyMassChanged() { unchecked { MassRevision++; } MassChanged?.Invoke(this); }
    #endregion
}

/// <summary>真实组分份数和端口预留共用库存，比例只用于选择而不代替数量。</summary>
public static class MixedLiquidContents
{
    #region 组分存储
    public static void Ensure(LiquidContainerState state)
    {
        state.Composition ??= new(StringComparer.OrdinalIgnoreCase);
        state.ComponentProcessingSeconds ??= new(StringComparer.OrdinalIgnoreCase);
        state.PendingOutputs ??= new(StringComparer.Ordinal);
        if (state.Composition.Count == 0 && state.Amount > Mod_WaterVessel.AmountEpsilon && !string.IsNullOrWhiteSpace(state.LiquidId))
        { state.Composition[state.LiquidId] = state.Amount; state.NotifyMassChanged(); }
        if (state.RandomState == 0) state.RandomState = 0x9e3779b9;
    }
    public static void SynchronizeAmounts(LiquidContainerState state)
    {
        Ensure(state);
        float total = 0f, largest = 0f;
        string dominant = null;
        var remove = new List<string>();
        foreach (var pair in state.Composition)
        {
            if (pair.Value <= 0f) { remove.Add(pair.Key); continue; }
            total += pair.Value;
            if (pair.Value > largest) { largest = pair.Value; dominant = pair.Key; }
        }
        foreach (string id in remove) { state.Composition.Remove(id); state.ComponentProcessingSeconds.Remove(id); }
        state.Amount = total;
        state.LiquidId = dominant;
        if (total <= 0f)
        { state.Amount = 0f; state.LiquidId = null; state.Temperature = 0f; state.ProcessingSeconds = 0f; state.PendingOutputs.Clear(); }
    }
    public static float GetAmount(LiquidContainerState state, string id)
    { Ensure(state); return id != null && state.Composition.TryGetValue(id, out float amount) ? amount : 0f; }
    public static float GetVolumeLiters(LiquidContainerState state)
    {
        Ensure(state); double liters = 0d;
        foreach (var component in state.Composition)
            liters += component.Value * (double)(GameRes.ExistingInstance?.GetLiquidDefinition(component.Key)?.LitersPerServing
                ?? throw new InvalidOperationException($"液体定义不存在：{component.Key}"));
        return (float)liters;
    }
    public static float GetReceivableServings(LiquidContainerState state, int capacity, string liquidId, float requested)
    {
        LiquidDefinition liquid = GameRes.ExistingInstance?.GetLiquidDefinition(liquidId);
        return liquid == null || !(requested > 0f) ? 0f : Mathf.Min(requested,
            Mathf.Max(0f, WorldLiquidVolumeConversion.GetContainerCapacityLiters(capacity) - GetVolumeLiters(state)) / liquid.LitersPerServing);
    }
    public static float GetVolumeFraction(LiquidContainerState state, int capacity)
        => capacity > 0 ? GetVolumeLiters(state) / WorldLiquidVolumeConversion.GetContainerCapacityLiters(capacity) : 0f;
    public static float Reserved(LiquidContainerState state, string id, string exceptPort = null)
    {
        Ensure(state); float amount = 0f;
        foreach (var pair in state.PendingOutputs)
            if (pair.Key != exceptPort && string.Equals(pair.Value.LiquidId, id, StringComparison.OrdinalIgnoreCase)) amount += pair.Value.Servings;
        return amount;
    }
    public static void Add(LiquidContainerState state, string id, float servings, float temperature)
    {
        Ensure(state);
        if (!(servings > 0f) || GameRes.ExistingInstance?.GetLiquidDefinition(id) == null) throw new InvalidOperationException($"未知或无效液体：{id}");
        float previous = state.Amount;
        double previousCapacity = 0d;
        foreach (var pair in state.Composition) previousCapacity += HeatCapacity(pair.Key, pair.Value);
        double incomingCapacity = HeatCapacity(id, servings);
        state.Composition[id] = GetAmount(state, id) + servings;
        state.Temperature = previous <= 0f ? temperature :
            (float)((state.Temperature * previousCapacity + temperature * incomingCapacity) / (previousCapacity + incomingCapacity));
        state.ComponentProcessingSeconds[id] = 0f;
        state.ProcessingSeconds = 0f; state.Revision++;
        SynchronizeAmounts(state); state.NotifyMassChanged();
    }
    private static double HeatCapacity(string id, float servings)
    {
        // 工业物性已声明时按摩尔热容混合，其余普通液体按每份体积使用水的热容。
        if (FluidCatalog.Default.TryFromLiquidId(id, out var fluid))
            return (double)FluidUnits.ServingsToMol(fluid, (decimal)servings) * fluid.LiquidHeatCapacityJPerMolKelvin;
        return GameRes.ExistingInstance.GetLiquidDefinition(id).LitersPerServing * servings * 4184d;
    }
    public static bool RemoveSpecies(LiquidContainerState state, string id, float servings, string reservedPort = null)
    {
        Ensure(state);
        float amount = GetAmount(state, id);
        if (!(servings > 0f) || amount - Reserved(state, id, reservedPort) < servings) return false;
        state.Composition[id] = Mathf.Max(0f, amount - servings);
        if (reservedPort != null && state.PendingOutputs.TryGetValue(reservedPort, out var pending))
        { pending.Servings -= servings; if (pending.Servings <= 0f) state.PendingOutputs.Remove(reservedPort); }
        state.ComponentProcessingSeconds[id] = 0f; state.ProcessingSeconds = 0f; state.Revision++;
        SynchronizeAmounts(state); state.NotifyMassChanged(); return true;
    }
    public static bool TransformSpecies(LiquidContainerState state, string fromId, string toId)
    {
        Ensure(state);
        float amount = GetAmount(state, fromId) - Reserved(state, fromId);
        if (amount <= 0f) return false;
        state.Composition[fromId] = GetAmount(state, fromId) - amount;
        state.Composition[toId] = GetAmount(state, toId) + amount;
        state.ComponentProcessingSeconds[fromId] = 0f;
        state.ComponentProcessingSeconds[toId] = 0f;
        state.Revision++; SynchronizeAmounts(state); state.NotifyMassChanged(); return true;
    }
    public static void Clear(LiquidContainerState state)
    {
        Ensure(state); state.Composition.Clear(); state.ComponentProcessingSeconds.Clear(); state.PendingOutputs.Clear();
        state.Amount = 0f; state.LiquidId = null; state.Temperature = 0f; state.ProcessingSeconds = 0f; state.Revision++;
        state.NotifyMassChanged();
    }
    #endregion

    #region 稳定随机批次
    public static bool Peek(LiquidContainerState state, string portId, out LiquidTransferBatch batch, float minimumServings = 0f,
        float minimumLiters = 0f)
    {
        Ensure(state); batch = default;
        if (!float.IsFinite(minimumServings) || minimumServings < 0f || !float.IsFinite(minimumLiters) || minimumLiters < 0f) return false;
        float Minimum(string id) => minimumLiters <= 0f ? minimumServings : Mathf.Max(minimumServings,
            minimumLiters / GameRes.ExistingInstance.GetLiquidDefinition(id).LitersPerServing);
        if (state.PendingOutputs.TryGetValue(portId, out LiquidPendingBatch pending) && pending.Servings >= Minimum(pending.LiquidId))
        { batch = new(pending.LiquidId, pending.Servings, state.Temperature); return true; }
        double total = 0d;
        foreach (var pair in state.Composition)
        {
            double available = Math.Max(0d, pair.Value - Reserved(state, pair.Key, portId));
            if (available >= Minimum(pair.Key)) total += available;
        }
        if (!(total > 0d)) return false;
        uint random = Next(state.RandomState);
        double pick = random / ((double)uint.MaxValue + 1d) * total;
        // 固定 ID 排序使存档恢复后的词典顺序不影响抽取。
        var ids = new List<string>(state.Composition.Keys); ids.Sort(StringComparer.Ordinal);
        foreach (string id in ids)
        {
            double available = Math.Max(0d, GetAmount(state, id) - Reserved(state, id, portId));
            float minimum = Minimum(id);
            if (available < minimum) continue;
            pick -= available;
            if (pick < 0d) { batch = new(id, (float)Math.Min(Math.Max(1d, minimum), available), state.Temperature); return true; }
        }
        return false;
    }
    public static bool Reserve(LiquidContainerState state, string portId, out LiquidTransferBatch batch, float minimumServings = 0f,
        float minimumLiters = 0f)
    {
        if (!Peek(state, portId, out batch, minimumServings, minimumLiters)) return false;
        if (state.PendingOutputs.TryGetValue(portId, out var pending) && LiquidVesselOperations.SameLiquid(pending.LiquidId, batch.LiquidId)
            && pending.Servings >= batch.Servings) return true;
        // 整份出口跳过不足一份的组分，只替换本口预约，不删减任何真实尾量。
        state.RandomState = Next(state.RandomState);
        state.PendingOutputs[portId] = new LiquidPendingBatch { LiquidId = batch.LiquidId, Servings = batch.Servings };
        state.Revision++; return true;
    }
    public static bool ConsumeBatch(LiquidContainerState state, string portId, LiquidTransferBatch batch, float servings)
    {
        if (!state.PendingOutputs.TryGetValue(portId, out var pending) ||
            !LiquidVesselOperations.SameLiquid(pending.LiquidId, batch.LiquidId) || pending.Servings < servings) return false;
        return RemoveSpecies(state, batch.LiquidId, servings, portId);
    }
    private static uint Next(uint seed)
    { if (seed == 0) seed = 0x9e3779b9; seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5; return seed == 0 ? 0x9e3779b9 : seed; }
    public static LiquidContainerState Clone(LiquidContainerState state) => MemoryPackSerializer.Deserialize<LiquidContainerState>(MemoryPackSerializer.Serialize(state));
    public static void Restore(LiquidContainerState target, LiquidContainerState saved)
    {
        target.LiquidId = saved.LiquidId; target.Amount = saved.Amount; target.Temperature = saved.Temperature;
        target.ProcessingSeconds = saved.ProcessingSeconds; target.Composition = saved.Composition;
        target.RainwaterProgress = saved.RainwaterProgress;
        target.ComponentProcessingSeconds = saved.ComponentProcessingSeconds; target.PendingOutputs = saved.PendingOutputs;
        target.RandomState = saved.RandomState; target.Revision = saved.Revision;
        target.NotifyMassChanged();
    }
    #endregion

    #region 混合显示
    public static LiquidDefinition DisplayLiquid(LiquidContainerState state)
    {
        Ensure(state);
        if (state.Amount <= 0f) return null;
        if (state.Composition.Count <= 1) return GameRes.ExistingInstance?.GetLiquidDefinition(state.LiquidId);
        Color color = Color.clear; float viscosity = 0f;
        foreach (var pair in state.Composition)
        {
            LiquidDefinition liquid = GameRes.ExistingInstance?.GetLiquidDefinition(pair.Key);
            if (liquid == null) throw new InvalidOperationException($"液体定义不存在：{pair.Key}");
            float ratio = pair.Value / state.Amount; color += liquid.PrimaryColor * ratio; viscosity += liquid.Viscosity * ratio;
        }
        return new LiquidDefinition("display:mixture", "混合液体", string.Empty, "mixture", "filled", color, viscosity,
            false, 0f, null, null);
    }
    #endregion
}
