using System;
using System.Collections.Generic;
using Unity.Mathematics;

public struct CompiledBiomeRange
{
    public float2 Temperature;
    public float2 Precipitation;
    public float2 Height;
    public byte Index;

    public readonly bool Matches(float temperature, float precipitation, float height)
    {
        return temperature >= Temperature.x && temperature <= Temperature.y &&
               precipitation >= Precipitation.x && precipitation <= Precipitation.y &&
               height >= Height.x && height <= Height.y;
    }
}

public sealed class BiomeResolver
{
    public const byte UnmatchedIndex = byte.MaxValue;
    public const int MaxBiomeCount = byte.MaxValue;

    private readonly IReadOnlyList<BiomeData> _biomes;
    private readonly CompiledBiomeRange[] _ranges;

    public IReadOnlyList<BiomeData> Biomes => _biomes;
    public IReadOnlyList<CompiledBiomeRange> Ranges => _ranges;
    public int Count => _biomes.Count;

    public BiomeResolver(IReadOnlyList<BiomeData> biomes)
    {
        if (biomes == null || biomes.Count == 0)
            throw new InvalidOperationException("必须至少配置一个 Biome。");
        if (biomes.Count > MaxBiomeCount)
            throw new InvalidOperationException($"Biome 数量不能超过 {MaxBiomeCount}，255 保留为未匹配标记。");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var copiedBiomes = new BiomeData[biomes.Count];
        _ranges = new CompiledBiomeRange[biomes.Count];
        for (int i = 0; i < biomes.Count; i++)
        {
            BiomeData biome = biomes[i] ?? throw new InvalidOperationException($"Biome[{i}] 为空。");
            if (string.IsNullOrWhiteSpace(biome.BiomeId))
                throw new InvalidOperationException($"Biome[{i}] {biome.name} 缺少稳定 BiomeId。");
            if (!ids.Add(biome.BiomeId))
                throw new InvalidOperationException($"BiomeId 重复：{biome.BiomeId}");
            string reason = biome.Condition == null ? "condition is null" : null;
            if (biome.Condition == null || !biome.Condition.TryValidate(out reason))
                throw new InvalidOperationException($"Biome {biome.BiomeId} 环境范围非法：{reason}");

            copiedBiomes[i] = biome;
            _ranges[i] = new CompiledBiomeRange
            {
                Temperature = new float2(biome.Condition.TemperatureRange.x, biome.Condition.TemperatureRange.y),
                Precipitation = new float2(biome.Condition.PrecipitationRange.x, biome.Condition.PrecipitationRange.y),
                Height = new float2(biome.Condition.HeightRange.x, biome.Condition.HeightRange.y),
                Index = (byte)i
            };
        }

        _biomes = copiedBiomes;
    }

    public byte ResolveIndex(EnvironmentSample sample)
    {
        return ResolveIndex(sample.Temperature, sample.Precipitation, sample.Height, _ranges);
    }

    public BiomeData Resolve(EnvironmentSample sample)
    {
        return GetBiome(ResolveIndex(sample));
    }

    public BiomeData GetBiome(byte index)
    {
        return index == UnmatchedIndex || index >= _biomes.Count ? null : _biomes[index];
    }

    public CompiledBiomeRange[] CopyCompiledRanges()
    {
        return (CompiledBiomeRange[])_ranges.Clone();
    }

    public static byte ResolveIndex(
        float temperature,
        float precipitation,
        float height,
        IReadOnlyList<CompiledBiomeRange> ranges)
    {
        for (int i = 0; i < ranges.Count; i++)
        {
            CompiledBiomeRange range = ranges[i];
            if (range.Matches(temperature, precipitation, height))
                return range.Index;
        }

        return UnmatchedIndex;
    }
}
