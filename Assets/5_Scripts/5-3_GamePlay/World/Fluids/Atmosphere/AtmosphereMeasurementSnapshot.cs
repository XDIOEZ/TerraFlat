using System;
using System.Collections.Generic;

public enum AtmosphereMeasurementStatus
{
    Unavailable,
    Vacuum,
    Measured
}

public readonly struct AtmosphereMeasurementComponent
{
    #region 大气成分读数
    public AtmosphereMeasurementComponent(string fluidId, decimal moles, decimal fraction)
    { FluidId = fluidId; Moles = moles; Fraction = fraction; }
    public string FluidId { get; }
    public decimal Moles { get; }
    public decimal Fraction { get; }
    #endregion
}

/// <summary>仪器显示的不可变快照，气体总量沿用星球有限库存的标准体积。</summary>
public sealed class AtmosphereMeasurementSnapshot
{
    #region 星球大气只读快照
    public static readonly AtmosphereMeasurementSnapshot Unavailable = new(
        AtmosphereMeasurementStatus.Unavailable, null, null, 0, 0m, 0d,
        Array.Empty<AtmosphereMeasurementComponent>());

    internal AtmosphereMeasurementSnapshot(AtmosphereMeasurementStatus status, string worldKey,
        string planetName, long revision, decimal totalMoles, double pressureKPa,
        IReadOnlyList<AtmosphereMeasurementComponent> components)
    {
        Status = status; WorldKey = worldKey; PlanetName = planetName; Revision = revision;
        TotalMoles = totalMoles; PressureKPa = pressureKPa; Components = components;
        TotalStandardCubicMeters = FluidUnits.MolToStandardLiters(totalMoles) / 1000m;
    }
    public AtmosphereMeasurementStatus Status { get; }
    public string WorldKey { get; }
    public string PlanetName { get; }
    public long Revision { get; }
    public decimal TotalMoles { get; }
    public decimal TotalStandardCubicMeters { get; }
    public double PressureKPa { get; }
    public IReadOnlyList<AtmosphereMeasurementComponent> Components { get; }
    #endregion
}
