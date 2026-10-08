using System.Collections.Generic;
using MemoryPack;

public partial class PlanetData
{
    #region 星球大气持久化与派生读数
    public string AtmosphereProfileId = AtmosphereCatalog.DefaultProfileId;
    public AtmosphereState Atmosphere;
    [MemoryPackIgnore] public decimal AtmosphereGasVolume => (Atmosphere?.CurrentStandardLiters ?? 0m) / 1000m;
    [MemoryPackIgnore] public decimal AtmosphereGasCapacity => (Atmosphere?.CapacityStandardLiters ?? 0m) / 1000m;
    [MemoryPackIgnore] public decimal AtmosphereReferenceGasVolume => FluidUnits.MolToStandardLiters(Atmosphere?.ReferenceMoles ?? 0m) / 1000m;
    [MemoryPackIgnore] public double AtmosphereReferencePressureKPa => Atmosphere?.ReferencePressureKPa ?? 0d;
    [MemoryPackIgnore] public double AtmospherePressureKPa => AtmosphereService.PressureKPa(Atmosphere);
    [MemoryPackIgnore] public double AtmosphereMaxPressureKPa => AtmosphereService.MaximumPressureKPa(Atmosphere);
    [MemoryPackIgnore] public IReadOnlyDictionary<string, decimal> AtmosphereGasVolumeByGasId
    {
        get
        {
            var values = new Dictionary<string, decimal>(System.StringComparer.OrdinalIgnoreCase);
            if (Atmosphere != null) foreach (var gas in Atmosphere.Gases)
                values.Add(gas.FluidId, FluidUnits.MolToStandardLiters(gas.Moles) / 1000m);
            return values;
        }
    }
    #endregion
}
