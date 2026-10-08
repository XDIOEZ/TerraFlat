using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

public static class FluidIds
{
    #region 本体稳定物质身份
    public const string Oxygen = "core:oxygen";
    public const string Nitrogen = "core:nitrogen";
    public const string Hydrogen = "core:hydrogen";
    public const string CarbonDioxide = "core:carbon_dioxide";
    public const string PureWater = "core:pure_water";
    public const string FilteredWater = "core:filtered_water";
    #endregion
}

/// <summary>本体与 MOD 共用严格物性定义，气液相共享同一物质身份。</summary>
public sealed class FluidDefinition
{
    #region 不可变物性
    public FluidDefinition(FluidDefinitionDto source)
    {
        if (source == null) throw new InvalidDataException("流体定义不能为 null");
        Id = ValidateId(source.Id); DisplayName = source.DisplayName?.Trim() ?? Id;
        LiquidId = string.IsNullOrWhiteSpace(source.LiquidId) ? null : ValidateId(source.LiquidId);
        Positive(source.MolarMassKgPerMol, "molarMassKgPerMol");
        Positive(source.LiquidDensityKgPerLiter, "liquidDensityKgPerLiter");
        Positive(source.GasHeatCapacityJPerMolKelvin, "gasHeatCapacityJPerMolKelvin");
        Positive(source.LiquidHeatCapacityJPerMolKelvin, "liquidHeatCapacityJPerMolKelvin");
        if (source.MolarMassKgPerMol < 0.000001d || source.MolarMassKgPerMol > 1000d ||
            source.LiquidDensityKgPerLiter < 0.000001d || source.LiquidDensityKgPerLiter > 1000d ||
            source.MolarMassKgPerMol / source.LiquidDensityKgPerLiter > 1000d)
            throw new InvalidDataException($"流体 {Id} 的摩尔质量或密度超出首轮单位换算范围");
        if (source.LitersPerServing < 0.000001m || source.LitersPerServing > 1000000m)
            throw new InvalidDataException($"流体 {Id} 的 litersPerServing 必须为有限正数");
        MolarMassKgPerMol = source.MolarMassKgPerMol;
        LiquidDensityKgPerLiter = source.LiquidDensityKgPerLiter;
        GasHeatCapacityJPerMolKelvin = source.GasHeatCapacityJPerMolKelvin;
        LiquidHeatCapacityJPerMolKelvin = source.LiquidHeatCapacityJPerMolKelvin;
        LitersPerServing = source.LitersPerServing;
        AllowIndustrialTransport = source.AllowIndustrialTransport;
        PhaseChange = source.PhaseChange == null ? null : new FluidPhaseChangeDefinition(source.PhaseChange);
        GasEnergyOffsetJPerMol = PhaseChange == null ? 0d : PhaseChange.LatentHeatJPerMol -
            (GasHeatCapacityJPerMolKelvin - LiquidHeatCapacityJPerMolKelvin) * PhaseChange.ReferenceBoilingKelvin;
        if (!FluidUnits.IsFinite(GasEnergyOffsetJPerMol) || GasEnergyOffsetJPerMol < 0d)
            throw new InvalidDataException($"流体 {Id} 的两相内能基准无效");
    }
    public string Id { get; }
    public string DisplayName { get; }
    public string LiquidId { get; }
    public double MolarMassKgPerMol { get; }
    public double LiquidDensityKgPerLiter { get; }
    public double GasHeatCapacityJPerMolKelvin { get; }
    public double LiquidHeatCapacityJPerMolKelvin { get; }
    public decimal LitersPerServing { get; }
    public bool AllowIndustrialTransport { get; }
    public FluidPhaseChangeDefinition PhaseChange { get; }
    public double GasEnergyOffsetJPerMol { get; }
    public double EnergyAt(decimal gasMoles, decimal liquidMoles, double temperatureKelvin) =>
        (double)gasMoles * (GasHeatCapacityJPerMolKelvin * temperatureKelvin + GasEnergyOffsetJPerMol) +
        (double)liquidMoles * LiquidHeatCapacityJPerMolKelvin * temperatureKelvin;
    internal static string ValidateId(string value)
    {
        string id = value?.Trim();
        if (string.IsNullOrEmpty(id) || id.IndexOf(':') <= 0 || id.EndsWith(":", StringComparison.Ordinal))
            throw new InvalidDataException($"流体身份必须包含稳定命名空间：{value}");
        foreach (char c in id) if (char.IsWhiteSpace(c) || char.IsControl(c))
            throw new InvalidDataException($"流体身份不允许空白字符：{value}");
        return id;
    }
    internal static void Positive(double value, string field)
    {
        if (!FluidUnits.IsFinite(value) || value < 1e-12d || value > 1e20d)
            throw new InvalidDataException($"流体物性 {field} 必须在首轮支持范围 1e-12～1e20 内");
    }
    #endregion
}

/// <summary>使用配置的饱和点插值，不在未知或超临界温度上强行液化。</summary>
public sealed class FluidPhaseChangeDefinition
{
    #region 有限相变曲线
    private readonly double[] temperatures;
    private readonly double[] pressures;
    public FluidPhaseChangeDefinition(FluidPhaseChangeDto dto)
    {
        FluidDefinition.Positive(dto.LatentHeatJPerMol, "latentHeatJPerMol");
        FluidDefinition.Positive(dto.ReferenceBoilingKelvin, "referenceBoilingKelvin");
        FluidDefinition.Positive(dto.CriticalTemperatureKelvin, "criticalTemperatureKelvin");
        if (dto.Saturation == null || dto.Saturation.Count < 2)
            throw new InvalidDataException("相变饱和曲线至少需要两个点");
        LatentHeatJPerMol = dto.LatentHeatJPerMol; ReferenceBoilingKelvin = dto.ReferenceBoilingKelvin;
        CriticalTemperatureKelvin = dto.CriticalTemperatureKelvin;
        temperatures = new double[dto.Saturation.Count]; pressures = new double[temperatures.Length];
        for (int i = 0; i < temperatures.Length; i++)
        {
            FluidSaturationPointDto point = dto.Saturation[i] ?? throw new InvalidDataException("相变曲线含空项");
            FluidDefinition.Positive(point.TemperatureKelvin, "saturation.temperatureKelvin");
            FluidDefinition.Positive(point.PressureKPa, "saturation.pressureKPa");
            if (point.TemperatureKelvin > CriticalTemperatureKelvin ||
                (i > 0 && (point.TemperatureKelvin <= temperatures[i - 1] || point.PressureKPa <= pressures[i - 1])))
                throw new InvalidDataException("相变曲线须按温度和蒸气压严格递增且不超过临界温度");
            temperatures[i] = point.TemperatureKelvin; pressures[i] = point.PressureKPa;
        }
        if (ReferenceBoilingKelvin >= CriticalTemperatureKelvin)
            throw new InvalidDataException("相变内能参考沸点必须低于临界温度");
    }
    public double LatentHeatJPerMol { get; }
    public double ReferenceBoilingKelvin { get; }
    public double CriticalTemperatureKelvin { get; }
    public double MinimumTemperatureKelvin => temperatures[0];
    public double MaximumTemperatureKelvin => temperatures[temperatures.Length - 1];
    public bool TrySaturationPressure(double temperatureKelvin, out double pressureKPa)
    {
        pressureKPa = 0d;
        if (!FluidUnits.IsFinite(temperatureKelvin) || temperatureKelvin < MinimumTemperatureKelvin ||
            temperatureKelvin >= CriticalTemperatureKelvin || temperatureKelvin > MaximumTemperatureKelvin) return false;
        for (int i = 1; i < temperatures.Length; i++)
        {
            if (temperatureKelvin > temperatures[i]) continue;
            double fraction = (temperatureKelvin - temperatures[i - 1]) / (temperatures[i] - temperatures[i - 1]);
            pressureKPa = Math.Exp(Math.Log(pressures[i - 1]) + fraction * Math.Log(pressures[i] / pressures[i - 1]));
            return true;
        }
        return false;
    }
    #endregion
}

[Serializable]
public sealed class FluidDefinitionDto
{
    [JsonProperty("id", Required = Required.Always)] public string Id;
    [JsonProperty("displayName", Required = Required.Always)] public string DisplayName;
    [JsonProperty("liquidId")] public string LiquidId;
    [JsonProperty("molarMassKgPerMol", Required = Required.Always)] public double MolarMassKgPerMol;
    [JsonProperty("liquidDensityKgPerLiter", Required = Required.Always)] public double LiquidDensityKgPerLiter;
    [JsonProperty("gasHeatCapacityJPerMolKelvin", Required = Required.Always)] public double GasHeatCapacityJPerMolKelvin;
    [JsonProperty("liquidHeatCapacityJPerMolKelvin", Required = Required.Always)] public double LiquidHeatCapacityJPerMolKelvin;
    [JsonProperty("litersPerServing")] public decimal LitersPerServing = 0.25m;
    [JsonProperty("allowIndustrialTransport")] public bool AllowIndustrialTransport = true;
    [JsonProperty("phaseChange")] public FluidPhaseChangeDto PhaseChange;
    [JsonProperty("modelNotes")] public string ModelNotes;
    [JsonProperty("sourceReferences")] public List<string> SourceReferences;
}

[Serializable]
public sealed class FluidPhaseChangeDto
{
    [JsonProperty("latentHeatJPerMol", Required = Required.Always)] public double LatentHeatJPerMol;
    [JsonProperty("referenceBoilingKelvin", Required = Required.Always)] public double ReferenceBoilingKelvin;
    [JsonProperty("criticalTemperatureKelvin", Required = Required.Always)] public double CriticalTemperatureKelvin;
    [JsonProperty("saturation", Required = Required.Always)] public List<FluidSaturationPointDto> Saturation;
}

[Serializable]
public sealed class FluidSaturationPointDto
{
    [JsonProperty("temperatureKelvin", Required = Required.Always)] public double TemperatureKelvin;
    [JsonProperty("pressureKPa", Required = Required.Always)] public double PressureKPa;
}
