using System.Runtime.CompilerServices;

public static partial class AtmosphereService
{
    #region 星球温室偏移缓存
    private sealed class GreenhouseOffsetCache
    {
        public AtmosphericGreenhouseConfiguration Configuration;
        public long Revision = -1;
        public decimal ReferenceMoles;
        public double ReferencePressureKPa;
        public double InitialPartialPressureKPa;
        public float TemperatureOffset;
    }

    private static readonly ConditionalWeakTable<AtmosphereState, GreenhouseOffsetCache> greenhouseOffsets = new();

    // CO₂ 真实气柱决定增温，抽走其他气体导致的比例升高不会凭空制造额外热量。
    public static double CarbonDioxidePartialPressureKPa(AtmosphereState atmosphere)
        => atmosphere == null || atmosphere.CapacityMoles <= 0m || atmosphere.ReferenceMoles <= 0m
            ? 0d : atmosphere.ReferencePressureKPa *
                (double)(GetMoles(atmosphere, FluidIds.CarbonDioxide) / atmosphere.ReferenceMoles);

    public static float GetAtmosphericGreenhouseTemperatureOffset(AtmosphereState atmosphere)
    {
        if (atmosphere == null || atmosphere.CapacityMoles <= 0m || atmosphere.ReferenceMoles <= 0m)
            return 0f;
        AtmosphericGreenhouseConfiguration configuration = AtmosphericGreenhouseConfigurationService.Current;
        GreenhouseOffsetCache cache = greenhouseOffsets.GetValue(atmosphere, _ => new GreenhouseOffsetCache());
        if (cache.Revision != atmosphere.Revision ||
            !ReferenceEquals(cache.Configuration, configuration) || cache.ReferenceMoles != atmosphere.ReferenceMoles ||
            cache.ReferencePressureKPa != atmosphere.ReferencePressureKPa ||
            cache.InitialPartialPressureKPa != atmosphere.InitialCarbonDioxidePartialPressureKPa)
        {
            cache.TemperatureOffset = configuration.ResolveTemperatureOffset(CarbonDioxidePartialPressureKPa(atmosphere),
                atmosphere.InitialCarbonDioxidePartialPressureKPa);
            cache.Configuration = configuration;
            cache.Revision = atmosphere.Revision;
            cache.ReferenceMoles = atmosphere.ReferenceMoles;
            cache.ReferencePressureKPa = atmosphere.ReferencePressureKPa;
            cache.InitialPartialPressureKPa = atmosphere.InitialCarbonDioxidePartialPressureKPa;
        }
        return cache.TemperatureOffset;
    }
    #endregion
}
