using System;
using System.Collections.Generic;

/// <summary>只读测量实际星球库存，不创建大气、不消耗样本，也不借用镜头所在星球。</summary>
public static class AtmosphereMeasurementService
{
    #region 实际世界来源与快照
    public static bool TryResolvePlanetForWorld(string worldKey,
        IReadOnlyDictionary<string, PlanetData> planets, out PlanetData planet)
    {
        planet = null;
        return !string.IsNullOrWhiteSpace(worldKey) && planets != null &&
            planets.TryGetValue(WorldAddress.FromWorldKey(worldKey).RootWorldKey, out planet) && planet != null;
    }

    public static AtmosphereMeasurementSnapshot CaptureForWorld(string worldKey,
        IReadOnlyDictionary<string, PlanetData> planets)
        => TryResolvePlanetForWorld(worldKey, planets, out PlanetData planet)
            ? Capture(planet.Atmosphere, worldKey, planet.RuntimePlanetName)
            : AtmosphereMeasurementSnapshot.Unavailable;

    public static AtmosphereMeasurementSnapshot Capture(AtmosphereState atmosphere,
        string worldKey = null, string planetName = null)
    {
        if (atmosphere == null) return AtmosphereMeasurementSnapshot.Unavailable;
        decimal total = atmosphere.CurrentMoles;
        if (total <= 0m)
            return new AtmosphereMeasurementSnapshot(AtmosphereMeasurementStatus.Vacuum, worldKey, planetName,
                atmosphere.Revision, 0m, 0d, Array.Empty<AtmosphereMeasurementComponent>());

        var components = new List<AtmosphereMeasurementComponent>(atmosphere.Gases.Count);
        foreach (AtmosphereGasAmount gas in atmosphere.Gases)
            if (gas.Moles > 0m)
                components.Add(new AtmosphereMeasurementComponent(gas.FluidId, gas.Moles, gas.Moles / total));
        components.Sort(CompareComponentsByAmountThenIdentity);
        return new AtmosphereMeasurementSnapshot(AtmosphereMeasurementStatus.Measured, worldKey, planetName,
            atmosphere.Revision, total, AtmosphereService.PressureKPa(atmosphere), components.AsReadOnly());
    }

    private static int CompareComponentsByAmountThenIdentity(
        AtmosphereMeasurementComponent left, AtmosphereMeasurementComponent right)
    {
        int amountOrder = right.Moles.CompareTo(left.Moles);
        return amountOrder != 0 ? amountOrder : StringComparer.Ordinal.Compare(left.FluidId, right.FluidId);
    }
    #endregion
}
