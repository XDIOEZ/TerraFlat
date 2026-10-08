using System;

/// <summary>工业与既有液体容器使用同一显式换算，份数不默认等于升数。</summary>
public static class FluidUnits
{
    #region 参考单位与换算
    public const double GasConstant = 8.31446261815324; // kPa·L/(mol·K)。
    public const double ReferencePressureKPa = 100d;
    public const double ReferenceTemperatureKelvin = 293.15d;
    public const decimal StandardLitersPerMol = 24.3738471641152m;
    public static decimal MolToStandardLiters(decimal moles) => moles * StandardLitersPerMol;
    public static decimal StandardLitersToMol(decimal liters) => liters / StandardLitersPerMol;
    public static decimal MolToLiquidLiters(FluidDefinition definition, decimal moles) =>
        moles * (decimal)definition.MolarMassKgPerMol / (decimal)definition.LiquidDensityKgPerLiter;
    public static decimal LiquidLitersToMol(FluidDefinition definition, decimal liters) =>
        liters * (decimal)definition.LiquidDensityKgPerLiter / (decimal)definition.MolarMassKgPerMol;
    public static decimal ServingsToMol(FluidDefinition definition, decimal servings) =>
        LiquidLitersToMol(definition, servings * definition.LitersPerServing);
    public static decimal MolToServings(FluidDefinition definition, decimal moles) =>
        MolToLiquidLiters(definition, moles) / definition.LitersPerServing;
    public static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    public static double CelsiusToKelvin(double value) => value + 273.15d;
    #endregion
}
