using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

[Serializable]
public enum NoiseType
{
    Height = 0,
    Precipitation = 2,
    Temperature = 3
}

public static class TerrainGenerationSignature
{
    public const int CurrentVersion = 2;

    public static uint CalculateDefault()
    {
        GameObject prefab = GameRes.Instance?.GetPrefab("MapCore", false);
        return Calculate(prefab != null ? prefab.GetComponent<Map>() : null);
    }

    public static uint Calculate(Map map)
    {
        uint hash = StructureHashUtility.Begin();
        hash = StructureHashUtility.Add(hash, CurrentVersion);
        if (map?.mapGenerators == null)
            return StructureHashUtility.Add(hash, "missing-map-core");

        var entries = new List<(ChunkGeneratorBase Generator, int Index)>(map.mapGenerators.Count);
        for (int i = 0; i < map.mapGenerators.Count; i++)
        {
            ChunkGeneratorBase generator = map.mapGenerators[i];
            if (generator != null)
                entries.Add((generator, i));
        }

        entries.Sort((left, right) =>
        {
            int stage = left.Generator.Stage.CompareTo(right.Generator.Stage);
            return stage != 0 ? stage : left.Index.CompareTo(right.Index);
        });

        hash = StructureHashUtility.Add(hash, entries.Count);
        for (int i = 0; i < entries.Count; i++)
        {
            ChunkGeneratorBase generator = entries[i].Generator;
            hash = StructureHashUtility.Add(hash, (int)generator.Stage);
            hash = StructureHashUtility.Add(hash, generator.GetType().FullName);
            switch (generator)
            {
                case ChunkGenerator_Land land:
                    hash = AddLand(hash, land);
                    break;
                case ChunkGenerator_River river:
                    hash = AddRiver(hash, river);
                    break;
                case ChunkGenerator_Structures structures:
                    StructureCatalogSO catalog = structures.Catalog != null
                        ? structures.Catalog
                        : StructureCatalogSO.LoadDefault();
                    hash = StructureHashUtility.Add(hash, catalog?.CalculateContentHash() ?? 0u);
                    break;
                case ChunkGenerator_SpawnItems ecology:
                    hash = StructureHashUtility.Add(hash, ecology.globalSpawnMultiplier);
                    break;
            }
        }

        return hash;
    }

    private static uint AddLand(uint hash, ChunkGenerator_Land land)
    {
        hash = StructureHashUtility.Add(hash, land.TemperatureCelsiusRange.x);
        hash = StructureHashUtility.Add(hash, land.TemperatureCelsiusRange.y);
        hash = StructureHashUtility.Add(hash, land.enableHeightSecondaryBoost);
        hash = StructureHashUtility.Add(hash, land.heightSecondaryBoostStrength);

        IReadOnlyList<TerrainNoiseConfig> configs = land.NoiseConfigs;
        hash = StructureHashUtility.Add(hash, configs?.Count ?? 0);
        for (int i = 0; i < (configs?.Count ?? 0); i++)
        {
            TerrainNoiseConfig config = configs[i];
            hash = StructureHashUtility.Add(hash, (int)config.noiseType);
            hash = StructureHashUtility.Add(hash, config.coordScale);
            hash = StructureHashUtility.Add(hash, config.frequency);
            hash = StructureHashUtility.Add(hash, config.octaves);
            hash = StructureHashUtility.Add(hash, config.lacunarity);
            hash = StructureHashUtility.Add(hash, config.persistence);
            hash = StructureHashUtility.Add(hash, config.coordOffset.x);
            hash = StructureHashUtility.Add(hash, config.coordOffset.y);
        }

        IReadOnlyList<BiomeData> biomes = land.biomes;
        hash = StructureHashUtility.Add(hash, biomes?.Count ?? 0);
        for (int i = 0; i < (biomes?.Count ?? 0); i++)
        {
            BiomeData biome = biomes[i];
            hash = StructureHashUtility.Add(hash, biome?.BiomeId);
            EnvironmentConditionRange range = biome?.Condition;
            if (range == null)
            {
                hash = StructureHashUtility.Add(hash, 0);
            }
            else
            {
                hash = StructureHashUtility.Add(hash, range.TemperatureRange.x);
                hash = StructureHashUtility.Add(hash, range.TemperatureRange.y);
                hash = StructureHashUtility.Add(hash, range.PrecipitationRange.x);
                hash = StructureHashUtility.Add(hash, range.PrecipitationRange.y);
                hash = StructureHashUtility.Add(hash, range.HeightRange.x);
                hash = StructureHashUtility.Add(hash, range.HeightRange.y);
            }

            Tile_Block tile = biome?.TerrainConfig?.TileSpawns_NoSO != null &&
                              biome.TerrainConfig.TileSpawns_NoSO.Count > 0
                ? biome.TerrainConfig.TileSpawns_NoSO[0]?.TileBlock
                : null;
            hash = StructureHashUtility.Add(
                hash,
                tile == null ? string.Empty : string.IsNullOrEmpty(tile.tileItemName) ? tile.name : tile.tileItemName);
        }

        return hash;
    }

    private static uint AddRiver(uint hash, ChunkGenerator_River river)
    {
        hash = StructureHashUtility.Add(hash, river.seed);
        hash = StructureHashUtility.Add(hash, river.channelSpacing);
        hash = StructureHashUtility.Add(hash, river.channelHalfWidth);
        hash = StructureHashUtility.Add(hash, river.bendAmplitude);
        hash = StructureHashUtility.Add(hash, river.bendFrequency);
        hash = StructureHashUtility.Add(hash, river.widthVariation);
        hash = StructureHashUtility.Add(hash, river.flowDirection.x);
        hash = StructureHashUtility.Add(hash, river.flowDirection.y);
        hash = StructureHashUtility.Add(hash, (int)river.writeMode);
        hash = StructureHashUtility.Add(hash, river.riverDepthMin);
        hash = StructureHashUtility.Add(hash, river.riverDepthMax);
        Tile_Block tile = river.riverTileBlock;
        return StructureHashUtility.Add(
            hash,
            tile == null ? string.Empty : string.IsNullOrEmpty(tile.tileItemName) ? tile.name : tile.tileItemName);
    }
}

[Serializable]
public struct TerrainNoiseConfig
{
    [LabelText("环境通道")]
    public NoiseType noiseType;

    [MinValue(0.000001f), LabelText("通道坐标倍率")]
    public float coordScale;

    [MinValue(0.000001f), LabelText("基础频率")]
    public float frequency;

    [Range(1, 12), LabelText("八度")]
    public int octaves;

    [MinValue(0.000001f), LabelText("频率倍率")]
    public float lacunarity;

    [Range(0f, 1f), LabelText("振幅衰减")]
    public float persistence;

    [LabelText("坐标偏移")]
    public Vector2 coordOffset;

    public static TerrainNoiseConfig Default(NoiseType type)
    {
        return new TerrainNoiseConfig
        {
            noiseType = type,
            coordScale = 10f,
            frequency = 0.02f,
            octaves = 4,
            lacunarity = 2f,
            persistence = 0.5f,
            coordOffset = Vector2.zero
        };
    }
}

public static class TerrainNoiseKernel
{
    public const float DefaultChannelValue = 0.5f;

    public static float Sample(in TerrainNoiseConfig config, Vector2 worldScaledPosition, int worldSeed)
    {
        return Sample(config, new float2(worldScaledPosition.x, worldScaledPosition.y), worldSeed);
    }

    internal static float Sample(in TerrainNoiseConfig config, float2 worldScaledPosition, int worldSeed)
    {
        float coordScale = FinitePositiveOr(config.coordScale, 1f);
        float baseFrequency = FinitePositiveOr(config.frequency, 0.01f);
        float lacunarity = FinitePositiveOr(config.lacunarity, 2f);
        float persistence = math.clamp(FiniteOr(config.persistence, 0.5f), 0f, 1f);
        int octaves = math.clamp(config.octaves, 1, 12);
        float2 offset = new float2(
            FiniteOr(config.coordOffset.x, 0f),
            FiniteOr(config.coordOffset.y, 0f));
        float2 samplePosition = worldScaledPosition * coordScale + offset + GetSeedOffset(worldSeed, config.noiseType);

        float sum = 0f;
        float amplitudeSum = 0f;
        float amplitude = 1f;
        float octaveFrequency = baseFrequency;
        for (int octave = 0; octave < octaves; octave++)
        {
            float value = noise.cnoise(samplePosition * octaveFrequency) * 0.5f + 0.5f;
            sum += math.saturate(value) * amplitude;
            amplitudeSum += amplitude;
            amplitude *= persistence;
            octaveFrequency *= lacunarity;
        }

        if (!math.isfinite(sum) || !math.isfinite(amplitudeSum) || amplitudeSum <= 0.000001f)
            return DefaultChannelValue;

        return math.saturate(sum / amplitudeSum);
    }

    internal static float SampleCNoise01(float2 position)
    {
        float value = noise.cnoise(position) * 0.5f + 0.5f;
        return math.isfinite(value) ? math.saturate(value) : DefaultChannelValue;
    }

    public static float ApplyHeightBoost(float height, bool enabled, float strength)
    {
        float h = math.saturate(math.isfinite(height) ? height : DefaultChannelValue);
        if (!enabled)
            return h;

        float delta = h - 0.5f;
        float safeStrength = math.max(0f, math.isfinite(strength) ? strength : 0f);
        return math.saturate(h + math.sign(delta) * delta * delta * 4f * safeStrength);
    }

    internal static float2 GetSeedOffset(int worldSeed, NoiseType type)
    {
        uint state = unchecked((uint)(worldSeed == 0 ? 1 : worldSeed));
        state ^= unchecked((uint)((int)type + 1) * 0x9E3779B9u);
        uint x = Hash(state ^ 0xA341316Cu);
        uint y = Hash(state ^ 0xC8013EA4u);
        return new float2(
            ((x & 0xFFFFu) / 65535f - 0.5f) * 8192f,
            ((y & 0xFFFFu) / 65535f - 0.5f) * 8192f);
    }

    public static bool IsValid(in TerrainNoiseConfig config, out string reason)
    {
        if (!Enum.IsDefined(typeof(NoiseType), config.noiseType))
        {
            reason = $"未知噪声通道值 {(int)config.noiseType}";
            return false;
        }

        if (!IsFinitePositive(config.coordScale) || !IsFinitePositive(config.frequency))
        {
            reason = "坐标倍率和基础频率必须是有限正数";
            return false;
        }

        if (config.octaves < 1 || config.octaves > 12)
        {
            reason = "八度必须位于 1~12";
            return false;
        }

        if (!IsFinitePositive(config.lacunarity) || !math.isfinite(config.persistence) || config.persistence < 0f || config.persistence > 1f)
        {
            reason = "频率倍率必须为有限正数，振幅衰减必须位于 0~1";
            return false;
        }

        if (!math.isfinite(config.coordOffset.x) || !math.isfinite(config.coordOffset.y))
        {
            reason = "坐标偏移必须是有限值";
            return false;
        }

        reason = null;
        return true;
    }

    private static uint Hash(uint value)
    {
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        value ^= value >> 16;
        return value;
    }

    private static bool IsFinitePositive(float value) => math.isfinite(value) && value > 0f;
    private static float FinitePositiveOr(float value, float fallback) => IsFinitePositive(value) ? value : fallback;
    private static float FiniteOr(float value, float fallback) => math.isfinite(value) ? value : fallback;
}
