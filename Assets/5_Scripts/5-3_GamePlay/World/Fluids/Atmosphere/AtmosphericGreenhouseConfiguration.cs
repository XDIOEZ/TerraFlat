using System;
using System.Collections;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

[Serializable, MemoryPack.MemoryPackable]
public sealed partial class AtmosphericGreenhouseConfigurationDto
{
    #region 温室配置数据
    [JsonProperty("schemaVersion", Required = Required.Always)] public int SchemaVersion;
    [JsonProperty("pressureScaleKPa", Required = Required.Always)] public double PressureScaleKPa;
    [JsonProperty("celsiusPerDoubling", Required = Required.Always)] public double CelsiusPerDoubling;
    [JsonProperty("minimumOffsetCelsius", Required = Required.Always)] public double MinimumOffsetCelsius;
    [JsonProperty("maximumOffsetCelsius", Required = Required.Always)] public double MaximumOffsetCelsius;
    #endregion
}

public sealed class AtmosphericGreenhouseConfiguration
{
    #region 不可变温室配置
    public static readonly AtmosphericGreenhouseConfiguration Default = new(.04d, 3d, -30d, 60d);
    public double PressureScaleKPa { get; }
    public double CelsiusPerDoubling { get; }
    public double MinimumOffsetCelsius { get; }
    public double MaximumOffsetCelsius { get; }

    public AtmosphericGreenhouseConfiguration(double pressureScaleKPa, double celsiusPerDoubling,
        double minimumOffsetCelsius, double maximumOffsetCelsius)
    {
        AtmosphericGreenhouseTemperatureRules.ResolveTemperatureOffset(0d, 0d, pressureScaleKPa,
            celsiusPerDoubling, minimumOffsetCelsius, maximumOffsetCelsius);
        PressureScaleKPa = pressureScaleKPa;
        CelsiusPerDoubling = celsiusPerDoubling;
        MinimumOffsetCelsius = minimumOffsetCelsius;
        MaximumOffsetCelsius = maximumOffsetCelsius;
    }

    public float ResolveTemperatureOffset(double currentPartialPressureKPa, double baselinePartialPressureKPa)
        => AtmosphericGreenhouseTemperatureRules.ResolveTemperatureOffset(currentPartialPressureKPa,
            baselinePartialPressureKPa, PressureScaleKPa, CelsiusPerDoubling, MinimumOffsetCelsius,
            MaximumOffsetCelsius);
    #endregion
}

public static class AtmosphericGreenhouseConfigurationLoader
{
    #region 跨平台温室配置加载
    public const string RelativeConfigPath = "GameConfig/Fluids/greenhouse-effect.json";
    private static readonly JsonSerializerSettings StrictSettings = new()
    { MissingMemberHandling = MissingMemberHandling.Error, DateParseHandling = DateParseHandling.None };

    public static AtmosphericGreenhouseConfiguration Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 16384)
            throw new InvalidDataException("温室配置为空或超过大小限制。");
        AtmosphericGreenhouseConfigurationDto dto = JsonConfigurationCache.Deserialize<AtmosphericGreenhouseConfigurationDto>(json, StrictSettings);
        if (dto == null || dto.SchemaVersion != 1)
            throw new InvalidDataException("温室配置 schemaVersion 必须为 1。");
        return new AtmosphericGreenhouseConfiguration(dto.PressureScaleKPa, dto.CelsiusPerDoubling,
            dto.MinimumOffsetCelsius, dto.MaximumOffsetCelsius);
    }

    public static IEnumerator LoadBuiltInAsync(Action<AtmosphericGreenhouseConfiguration> completed,
        Action<Exception> failed)
    {
        string json = null;
        Exception error = null;
        yield return StreamingAssetsTextLoader.ReadAllTextAsync(
            StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath, RelativeConfigPath),
            value => json = value, exception => error = exception);
        if (error != null) { failed?.Invoke(error); yield break; }
        AtmosphericGreenhouseConfiguration config;
        try { config = Deserialize(json); }
        catch (Exception exception) { failed?.Invoke(exception); yield break; }
        completed?.Invoke(config);
    }
    #endregion
}

public static class AtmosphericGreenhouseConfigurationService
{
    #region 温室资源会话
    public static AtmosphericGreenhouseConfiguration Current { get; private set; } = AtmosphericGreenhouseConfiguration.Default;
    public static int Version { get; private set; }

    public static void ReplaceConfiguration(AtmosphericGreenhouseConfiguration configuration)
    { Current = configuration ?? throw new ArgumentNullException(nameof(configuration)); Version++; }

    internal static void Reset()
    { Current = AtmosphericGreenhouseConfiguration.Default; Version++; }

    // 候选配置只交换资源状态，失败回滚不触碰星球真实气体库存。
    internal static void ConfigureResourceReload(ResourceReloadContext context)
    {
        context.Add(() => Current, value => Current = value, AtmosphericGreenhouseConfiguration.Default);
        context.Add(() => Version, value => Version = value, Version);
    }
    #endregion
}
