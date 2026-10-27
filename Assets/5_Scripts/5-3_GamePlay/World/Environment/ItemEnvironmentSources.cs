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
    bool TrySampleCombustion(string worldKey, UnityEngine.Vector2 position, out bool canBurn);
    float SupplyAmbientOxygen(Item item, float maximumAmount);
}
public interface IWorldPositionEnvironmentSource
{
    bool TrySample(string worldKey, UnityEngine.Vector2 position, out ItemEnvironmentSample sample);
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
    public static bool TryGet(string worldKey, UnityEngine.Vector2 position, out ItemEnvironmentSample sample)
    {
        for (int i = sources.Count - 1; i >= 0; i--)
            if (sources[i] is IWorldPositionEnvironmentSource points && points.TrySample(worldKey, position, out sample)) return true;
        sample = default; return false;
    }
    public static float SupplyAmbientOxygen(Item item, float maximumAmount)
    {
        for (int i = sources.Count - 1; i >= 0; i--)
            if (sources[i].TrySample(item, out var sample)) return sample.CanBreathe ? sources[i].SupplyAmbientOxygen(item, maximumAmount) : 0f;
        return 0f;
    }
    // 燃烧方只查询所在位置的环境，由环境提供方决定采用舱气还是星球大气。
    public static bool CanSustainCombustion(string worldKey, UnityEngine.Vector2 position)
    {
        for (int i = sources.Count - 1; i >= 0; i--)
            if (sources[i].TrySampleCombustion(worldKey, position, out bool canBurn)) return canBurn;
        return AtmosphereService.TryGetForWorld(worldKey, out AtmosphereState atmosphere) &&
            AtmosphereService.CanSustainCombustion(atmosphere);
    }
    public static bool CanSustainCombustion(Item item)
    {
        if (item == null) return false;
        while (item.Owner != null) item = item.Owner;
        return CanSustainCombustion(GameplayWorldRegistry.GetItemWorldKey(item) ?? item.gameObject.scene.name,
            item.transform.position);
    }
}
#endregion
