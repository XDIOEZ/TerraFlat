using System;
using System.Collections.Generic;
using MemoryPack;

/// <summary>大气用 decimal mol 精确记账，巨大储量中的普通气包仍能被真实扣除。</summary>
[Serializable, MemoryPackable]
public partial class AtmosphereGasAmount
{
    public string FluidId;
    public decimal Moles;
}

[Serializable, MemoryPackable]
public partial class AtmosphereState
{
    #region 星球唯一大气库存
    public string DefinitionId;
    public decimal CapacityMoles;
    public decimal ReferenceMoles;
    public double ReferencePressureKPa;
    public double TemperatureKelvin;
    public List<AtmosphereGasAmount> Gases = new();
    public decimal TotalEscapedMoles;
    [MemoryPackIgnore] public decimal CurrentMoles
    { get { decimal result = 0m; foreach (var gas in Gases) result += gas.Moles; return result; } }
    [MemoryPackIgnore] public decimal CurrentStandardLiters => FluidUnits.MolToStandardLiters(CurrentMoles);
    [MemoryPackIgnore] public decimal CapacityStandardLiters => FluidUnits.MolToStandardLiters(CapacityMoles);
    public AtmosphereState Clone()
    {
        var result = new AtmosphereState
        { DefinitionId = DefinitionId, CapacityMoles = CapacityMoles, ReferenceMoles = ReferenceMoles,
            ReferencePressureKPa = ReferencePressureKPa, TemperatureKelvin = TemperatureKelvin, TotalEscapedMoles = TotalEscapedMoles };
        foreach (var gas in Gases) result.Gases.Add(new AtmosphereGasAmount { FluidId = gas.FluidId, Moles = gas.Moles });
        return result;
    }
    #endregion
}

public readonly struct AtmosphereEmissionResult
{
    public AtmosphereEmissionResult(decimal releasedMoles, decimal retainedMoles)
    { ReleasedMoles = releasedMoles; RetainedMoles = retainedMoles; }
    public decimal ReleasedMoles { get; }
    public decimal RetainedMoles { get; }
    public decimal EscapedMoles => ReleasedMoles - RetainedMoles;
    public decimal ReleasedStandardLiters => FluidUnits.MolToStandardLiters(ReleasedMoles);
    public decimal RetainedStandardLiters => FluidUnits.MolToStandardLiters(RetainedMoles);
    public decimal EscapedStandardLiters => FluidUnits.MolToStandardLiters(EscapedMoles);
}

public readonly struct AtmosphereGasSnapshot
{
    public AtmosphereGasSnapshot(string id, decimal moles, decimal fraction, double densityKgPerLiter)
    { FluidId = id; Moles = moles; Fraction = fraction; DensityKgPerLiter = densityKgPerLiter; }
    public string FluidId { get; }
    public decimal Moles { get; }
    public decimal Fraction { get; }
    public decimal StandardLiters => FluidUnits.MolToStandardLiters(Moles);
    public double DensityKgPerLiter { get; }
}
