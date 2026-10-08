using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

[Serializable]
public sealed class AtmosphereDefinitionDto
{
    [JsonProperty("id", Required = Required.Always)] public string Id;
    [JsonProperty("initialStandardCubicMeters", Required = Required.Always)] public decimal InitialStandardCubicMeters;
    [JsonProperty("capacityStandardCubicMeters", Required = Required.Always)] public decimal CapacityStandardCubicMeters;
    [JsonProperty("referenceStandardCubicMeters", Required = Required.Always)] public decimal ReferenceStandardCubicMeters;
    [JsonProperty("referencePressureKPa", Required = Required.Always)] public double ReferencePressureKPa;
    [JsonProperty("temperatureKelvin", Required = Required.Always)] public double TemperatureKelvin;
    [JsonProperty("composition", Required = Required.Always)] public List<AtmosphereCompositionDto> Composition;
}

[Serializable]
public sealed class AtmosphereCompositionDto
{
    [JsonProperty("fluidId", Required = Required.Always)] public string FluidId;
    [JsonProperty("fraction", Required = Required.Always)] public decimal Fraction;
}

public sealed class AtmosphereDefinition
{
    #region 不可变大气初值
    private readonly List<AtmosphereCompositionDto> composition = new();
    public AtmosphereDefinition(AtmosphereDefinitionDto dto, FluidCatalog fluids)
    {
        if (dto == null) throw new InvalidDataException("大气定义不能为空");
        Id = FluidDefinition.ValidateId(dto.Id);
        if (dto.InitialStandardCubicMeters < 0m || dto.CapacityStandardCubicMeters < dto.InitialStandardCubicMeters ||
            dto.CapacityStandardCubicMeters > 10000000000000000m || dto.ReferenceStandardCubicMeters < 0m ||
            dto.ReferenceStandardCubicMeters > 10000000000000000m ||
            (dto.CapacityStandardCubicMeters > 0m && dto.ReferenceStandardCubicMeters <= 0m))
            throw new InvalidDataException($"大气 {Id} 的初始量、容量和气压基准无效，容量上限为 1e16 标准 m³");
        if (!FluidUnits.IsFinite(dto.ReferencePressureKPa) || dto.ReferencePressureKPa < 0d ||
            dto.ReferencePressureKPa > 1e20d ||
            (dto.CapacityStandardCubicMeters > 0m && dto.ReferencePressureKPa < 1e-12d))
            throw new InvalidDataException($"大气 {Id} 的气压基准无效");
        FluidDefinition.Positive(dto.TemperatureKelvin, "atmosphere.temperatureKelvin");
        InitialMoles = AtmosphereService.Quantize(FluidUnits.StandardLitersToMol(dto.InitialStandardCubicMeters * 1000m));
        CapacityMoles = AtmosphereService.Quantize(FluidUnits.StandardLitersToMol(dto.CapacityStandardCubicMeters * 1000m));
        ReferenceMoles = FluidUnits.StandardLitersToMol(dto.ReferenceStandardCubicMeters * 1000m);
        ReferencePressureKPa = dto.ReferencePressureKPa; TemperatureKelvin = dto.TemperatureKelvin;
        if (CapacityMoles > FluidInventory.MaximumMoles || ReferenceMoles > FluidInventory.MaximumMoles ||
            dto.CapacityStandardCubicMeters > 0m &&
            (CapacityMoles < AtmosphereService.MinimumMoles || ReferenceMoles < AtmosphereService.MinimumMoles))
            throw new InvalidDataException($"大气 {Id} 的容量或气压基准超出首轮精确单位范围");
        decimal total = 0m; var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var component in dto.Composition ?? throw new InvalidDataException($"大气 {Id} 缺少 composition"))
        {
            if (component == null || !fluids.TryGet(component.FluidId, out _) || component.Fraction < 0m || component.Fraction > 1m ||
                !ids.Add(component.FluidId)) throw new InvalidDataException($"大气 {Id} 成分重复、未知或比例错误");
            total += component.Fraction;
            composition.Add(new AtmosphereCompositionDto { FluidId = component.FluidId, Fraction = component.Fraction });
        }
        if (CapacityMoles > 0m && total != 1m) throw new InvalidDataException($"大气 {Id} 初始比例之和必须等于 1");
        if (CapacityMoles == 0m && (total != 0m || InitialMoles != 0m)) throw new InvalidDataException($"真空大气 {Id} 不允许初始成分");
    }
    public string Id { get; }
    public decimal InitialMoles { get; }
    public decimal CapacityMoles { get; }
    public decimal ReferenceMoles { get; }
    public double ReferencePressureKPa { get; }
    public double TemperatureKelvin { get; }
    public AtmosphereState CreateState()
    {
        var state = new AtmosphereState { DefinitionId = Id, CapacityMoles = CapacityMoles, ReferenceMoles = ReferenceMoles,
            ReferencePressureKPa = ReferencePressureKPa, TemperatureKelvin = TemperatureKelvin };
        decimal left = InitialMoles;
        int lastPositive = composition.FindLastIndex(value => value.Fraction > 0m);
        for (int i = 0; i < composition.Count; i++)
        {
            decimal amount = i == lastPositive ? left : AtmosphereService.Quantize(InitialMoles * composition[i].Fraction);
            left -= amount;
            state.Gases.Add(new AtmosphereGasAmount { FluidId = composition[i].FluidId, Moles = amount });
        }
        return state;
    }
    #endregion
}

public sealed class AtmosphereCatalog
{
    #region 大气来源目录
    public const string DefaultProfileId = "core:temperate";
    public const string VacuumProfileId = "core:vacuum";
    private static AtmosphereCatalog current = new();
    private readonly Dictionary<string, AtmosphereDefinition> definitions = new(StringComparer.OrdinalIgnoreCase);
    public static AtmosphereCatalog Default => current;
    public AtmosphereDefinition Find(string id) => definitions.TryGetValue(id ?? string.Empty, out var definition) ? definition :
        throw new InvalidDataException($"大气定义不存在：{id}");
    public List<AtmosphereDefinition> Register(IEnumerable<AtmosphereDefinitionDto> values, FluidCatalog fluids, string ownerModId = null)
    {
        var candidates = new List<AtmosphereDefinition>();
        var ids = new HashSet<string>(definitions.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var value in values ?? Array.Empty<AtmosphereDefinitionDto>())
        {
            var definition = new AtmosphereDefinition(value, fluids);
            if (!ids.Add(definition.Id) || (!string.IsNullOrEmpty(ownerModId) &&
                !definition.Id.StartsWith(ownerModId + ":", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException($"MOD 大气身份冲突或命名空间错误：{definition.Id}");
            candidates.Add(definition);
        }
        foreach (var definition in candidates) definitions.Add(definition.Id, definition);
        return candidates;
    }
    public void Unregister(AtmosphereDefinition definition)
    {
        if (definition != null && definitions.TryGetValue(definition.Id, out var existing) && ReferenceEquals(definition, existing))
            definitions.Remove(definition.Id);
    }
    public static void Replace(AtmosphereCatalog catalog) => current = catalog ?? throw new ArgumentNullException(nameof(catalog));
    public static void Reset() => current = new AtmosphereCatalog();
    internal static void ConfigureResourceReload(ResourceReloadContext context) =>
        context.Add(() => current, value => current = value, new AtmosphereCatalog());
    #endregion
}
