using System;

/// <summary>按真实二氧化碳气柱相对初始大气计算温室温差。</summary>
public static class AtmosphericGreenhouseTemperatureRules
{
    #region 二氧化碳温室规则

    // 对有效光学量 1+p/scale 取对数，使零二氧化碳大气也有连续、有限的温度偏移。
    public static float ResolveTemperatureOffset(double currentPartialPressureKPa,
        double baselinePartialPressureKPa, double pressureScaleKPa = .04d,
        double celsiusPerDoubling = 3d, double minimumOffsetCelsius = -30d,
        double maximumOffsetCelsius = 60d)
    {
        if (!double.IsFinite(currentPartialPressureKPa) || currentPartialPressureKPa < 0d ||
            !double.IsFinite(baselinePartialPressureKPa) || baselinePartialPressureKPa < 0d ||
            !double.IsFinite(pressureScaleKPa) || pressureScaleKPa <= 0d ||
            !double.IsFinite(celsiusPerDoubling) || celsiusPerDoubling < 0d ||
            !double.IsFinite(minimumOffsetCelsius) || minimumOffsetCelsius > 0d ||
            !double.IsFinite(maximumOffsetCelsius) || maximumOffsetCelsius < 0d ||
            minimumOffsetCelsius < -273.15d || maximumOffsetCelsius > 10000d)
            throw new ArgumentOutOfRangeException(nameof(currentPartialPressureKPa), "温室气柱和配置必须是有效有限数值。");

        double current = LogPositiveSum(pressureScaleKPa, currentPartialPressureKPa);
        double baseline = LogPositiveSum(pressureScaleKPa, baselinePartialPressureKPa);
        double offset = (current - baseline) / Math.Log(2d) * celsiusPerDoubling;
        return (float)Math.Max(minimumOffsetCelsius, Math.Min(maximumOffsetCelsius, offset));
    }

    private static double LogPositiveSum(double first, double second)
    {
        double maximum = Math.Max(first, second), minimum = Math.Min(first, second);
        return Math.Log(maximum) + Math.Log(1d + minimum / maximum);
    }

    #endregion
}
