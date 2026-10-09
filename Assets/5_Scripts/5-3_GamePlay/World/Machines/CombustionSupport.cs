using System;
using System.Collections.Generic;
using FlatWorld.Networking;

public interface ICombustionSupportReceiver
{
    #region 助燃接收
    MachineEntity Entity { get; }
    void AcceptCombustionSupport(float temperatureBonus);
    #endregion
}

public interface ICombustionSupportSource
{
    #region 助燃供给
    bool TrySupply(MachineEntity source, CombustionSupportRequest request);
    #endregion
}

/// <summary>一次燃烧只接收一次实际供给，来源先扣自己的库存再提交支持的燃料量。</summary>
public sealed class CombustionSupportRequest
{
    #region 本次燃烧预算
    public ICombustionSupportReceiver Receiver { get; }
    public float FuelUnits { get; }
    public float Seconds { get; }
    public bool IsSupplied { get; private set; }
    internal CombustionSupportRequest(ICombustionSupportReceiver receiver, float fuelUnits, float seconds)
    { Receiver = receiver; FuelUnits = fuelUnits; Seconds = seconds; }
    public bool TrySupply(float supportedFuelUnits, float maximumTemperatureBonus)
    {
        if (IsSupplied || !MachineDefinition.Positive(supportedFuelUnits) || !MachineDefinition.NonNegative(maximumTemperatureBonus)) return false;
        IsSupplied = true;
        Receiver.AcceptCombustionSupport(maximumTemperatureBonus * Math.Clamp(supportedFuelUnits / FuelUnits, 0, 1));
        return true;
    }
    #endregion
}

internal sealed class FluidOutletCombustionSupport : ICombustionSupportSource
{
    #region 氧气出口助燃
    public static readonly FluidOutletCombustionSupport Instance = new();
    public bool TrySupply(MachineEntity source, CombustionSupportRequest request)
        => MachineWorld.TrySupplyOutletOxygen(source, request);
    #endregion
}

public static partial class MachineWorld
{
    #region 通用助燃来源登记
    private sealed class CombustionSupportLease : IDisposable
    {
        public MachineEntity Source;
        public Func<ICombustionSupportSource> Resolve;
        public void Dispose()
        {
            if (Source != null && combustionSources.TryGetValue(Source.Id, out var current) && ReferenceEquals(current, this))
            {
                combustionSources.Remove(Source.Id);
                combustionSourcesDirty = true;
            }
            Source = null; Resolve = null;
        }
    }
    private static readonly Dictionary<int, CombustionSupportLease> combustionSources = new();
    private static CombustionSupportLease[] combustionSourceSnapshot = Array.Empty<CombustionSupportLease>();
    private static bool combustionSourcesDirty = true;

    public static void InvalidateCombustionSupportSources() => combustionSourcesDirty = true;

    private static CombustionSupportLease[] GetCombustionSupportSources()
    {
        if (!combustionSourcesDirty) return combustionSourceSnapshot;
        combustionSourcesDirty = false;
        var candidates = new List<CombustionSupportLease>(combustionSources.Values);
        var providers = new List<CombustionSupportLease>();
        foreach (CombustionSupportLease entry in candidates)
            if (entry.Source != null && entry.Resolve() != null) providers.Add(entry);
        // 只有登记变化才筛选供给能力，燃烧热路径使用稳定快照且允许回调注销来源。
        return combustionSourceSnapshot = providers.ToArray();
    }
    public static IDisposable RegisterCombustionSupportSource(MachineEntity source, Func<ICombustionSupportSource> resolver)
    {
        if (source == null || resolver == null) throw new ArgumentException("助燃来源登记无效。");
        if (combustionSources.ContainsKey(source.Id)) throw new InvalidOperationException("助燃来源重复登记：" + source.Id);
        var lease = new CombustionSupportLease { Source = source, Resolve = resolver };
        combustionSources.Add(source.Id, lease);
        combustionSourcesDirty = true;
        return lease;
    }
    public static void SupplyCombustionSupport(ICombustionSupportReceiver receiver, float fuelUnits, float seconds)
    {
        if (!GameNetwork.HasStateAuthority || receiver?.Entity == null || !Contains(receiver.Entity) ||
            !MachineDefinition.Positive(fuelUnits) || !MachineDefinition.Positive(seconds)) return;
        var request = new CombustionSupportRequest(receiver, fuelUnits, seconds);
        foreach (CombustionSupportLease entry in GetCombustionSupportSources())
        {
            if (entry.Source == null || !Contains(entry.Source) || !entry.Source.Active) continue;
            entry.Resolve()?.TrySupply(entry.Source, request);
            if (request.IsSupplied) break;
        }
    }
    #endregion
}
