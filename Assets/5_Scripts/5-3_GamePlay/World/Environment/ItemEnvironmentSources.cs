using System;
using System.Collections.Generic;

#region 可组合局部环境
public readonly struct ItemEnvironmentSample
{
    public readonly double PressureKPa, TemperatureCelsius;
    public readonly bool CanBreathe, BlocksTerrainContact, EarthGravity;
    public ItemEnvironmentSample(double pressure, double temperature, bool breathe, bool floor, bool gravity)
    { PressureKPa = pressure; TemperatureCelsius = temperature; CanBreathe = breathe; BlocksTerrainContact = floor; EarthGravity = gravity; }
}
public interface IItemEnvironmentSource
{
    bool TrySample(Item item, out ItemEnvironmentSample sample);
    float SupplyAmbientOxygen(Item item, float maximumAmount);
}

// 环境提供方主动声明覆盖范围，生物模块无需认识船舱、装备或具体供气设施。
public static class ItemEnvironmentSources
{
    private static readonly List<IItemEnvironmentSource> sources = new();
    public static void Register(IItemEnvironmentSource source) { if (source != null && !sources.Contains(source)) sources.Add(source); }
    public static void Unregister(IItemEnvironmentSource source) => sources.Remove(source);
    public static bool TryGet(Item item, out ItemEnvironmentSample sample)
    {
        for (int i = sources.Count - 1; i >= 0; i--) if (sources[i].TrySample(item, out sample)) return true;
        sample = default; return false;
    }
    public static float SupplyAmbientOxygen(Item item, float maximumAmount)
    {
        for (int i = sources.Count - 1; i >= 0; i--)
            if (sources[i].TrySample(item, out var sample)) return sample.CanBreathe ? sources[i].SupplyAmbientOxygen(item, maximumAmount) : 0f;
        return 0f;
    }
}
#endregion
