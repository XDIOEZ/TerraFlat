using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;

[Serializable]
public enum NoiseType
{
    Height = 0,
    Precipitation = 2,
    Temperature = 3
}

public readonly struct WindSample
{
    public Vector2 Direction { get; }

    public WindSample(Vector2 direction)
    {
        Direction = direction.sqrMagnitude > 0.000001f
            ? direction.normalized
            : Vector2.right;
    }
}

public readonly struct ClimateSample
{
    public EnvironmentSample Environment { get; }
    public float BasePrecipitation { get; }
    public WindSample Wind { get; }

    public ClimateSample(
        EnvironmentSample environment,
        float basePrecipitation,
        WindSample wind)
    {
        Environment = environment;
        BasePrecipitation = Mathf.Clamp01(basePrecipitation);
        Wind = wind;
    }
}

[Serializable]
public struct WindFieldConfig
{
    [MinValue(8f), LabelText("风区尺寸")]
    public float RegionSize;

    [LabelText("风场种子盐")]
    public int SeedSalt;

    public static WindFieldConfig Default => new()
    {
        RegionSize = 256f,
        SeedSalt = unchecked((int)0x6A09E667)
    };
}

public interface IWindFieldProvider
{
    uint GenerationSignature { get; }

    WindSample Sample(Vector2Int worldPosition, int worldSeed, in WindFieldConfig config);
}

public interface IWorldGenerationDomain
{
    uint GenerationSignature { get; }

    bool Contains(Vector2Int worldPosition);

    bool TryResolveOutflow(
        Vector2Int fromWorldPosition,
        Vector2Int outsideCandidate,
        out Vector2Int outflowPosition);
}

public sealed class UnboundedWorldGenerationDomain : IWorldGenerationDomain
{
    public static readonly UnboundedWorldGenerationDomain Instance = new();

    public uint GenerationSignature => 0x554E4244u;

    private UnboundedWorldGenerationDomain()
    {
    }

    public bool Contains(Vector2Int worldPosition)
    {
        return true;
    }

    public bool TryResolveOutflow(
        Vector2Int fromWorldPosition,
        Vector2Int outsideCandidate,
        out Vector2Int outflowPosition)
    {
        outflowPosition = default;
        return false;
    }
}

public sealed class RegionalRandomWindFieldProvider : IWindFieldProvider
{
    public static readonly RegionalRandomWindFieldProvider Instance = new();

    public uint GenerationSignature => 0x52574E44u;

    private RegionalRandomWindFieldProvider()
    {
    }

    public WindSample Sample(Vector2Int worldPosition, int worldSeed, in WindFieldConfig config)
    {
        float2 direction = WindFieldKernel.Sample(
            new float2(worldPosition.x, worldPosition.y),
            worldSeed,
            config);
        return new WindSample(new Vector2(direction.x, direction.y));
    }

    public WindSample Sample(
        Vector2Int worldPosition,
        int worldSeed,
        in WindFieldConfig config,
        in WorldTopologyBounds bounds)
    {
        float2 direction = WindFieldKernel.Sample(
            new float2(worldPosition.x, worldPosition.y),
            worldSeed,
            config,
            bounds.ToDomain());
        return new WindSample(new Vector2(direction.x, direction.y));
    }
}

internal static class WindFieldKernel
{
    internal static float2 Sample(float2 worldPosition, int worldSeed, in WindFieldConfig config)
    {
        return Sample(worldPosition, worldSeed, config, default);
    }

    internal static float2 Sample(
        float2 worldPosition,
        int worldSeed,
        in WindFieldConfig config,
        in WorldTopologyDomain domain)
    {
        float regionSize = math.max(8f, math.isfinite(config.RegionSize) ? config.RegionSize : 256f);
        float2 gridPosition;
        int2 repeat = default;
        if (domain.IsWrapped)
        {
            repeat = math.max(new int2(1), (int2)math.round(new float2(domain.Span.x, domain.Span.y) / regionSize));
            gridPosition = (worldPosition - domain.Min) /
                           new float2(domain.Span.x, domain.Span.y) * repeat;
        }
        else
        {
            gridPosition = worldPosition / regionSize;
        }

        int2 cell = (int2)math.floor(gridPosition);
        float2 t = math.frac(gridPosition);
        t = t * t * (3f - 2f * t);

        int2 c00 = CanonicalCell(cell, repeat, domain.IsWrapped);
        int2 c10 = CanonicalCell(cell + new int2(1, 0), repeat, domain.IsWrapped);
        int2 c01 = CanonicalCell(cell + new int2(0, 1), repeat, domain.IsWrapped);
        int2 c11 = CanonicalCell(cell + new int2(1, 1), repeat, domain.IsWrapped);

        float2 bottom = math.lerp(
            DirectionAt(c00.x, c00.y, worldSeed, config.SeedSalt),
            DirectionAt(c10.x, c10.y, worldSeed, config.SeedSalt),
            t.x);
        float2 top = math.lerp(
            DirectionAt(c01.x, c01.y, worldSeed, config.SeedSalt),
            DirectionAt(c11.x, c11.y, worldSeed, config.SeedSalt),
            t.x);
        float2 blended = math.lerp(bottom, top, t.y);
        if (!math.all(math.isfinite(blended)) || math.lengthsq(blended) <= 0.000001f)
            return DirectionAt(c00.x, c00.y, worldSeed, config.SeedSalt);
        return math.normalize(blended);
    }

    private static int2 CanonicalCell(int2 cell, int2 repeat, bool wrapped)
    {
        if (!wrapped)
            return cell;
        int x = cell.x % repeat.x;
        int y = cell.y % repeat.y;
        if (x < 0) x += repeat.x;
        if (y < 0) y += repeat.y;
        return new int2(x, y);
    }

    private static float2 DirectionAt(int regionX, int regionY, int worldSeed, int seedSalt)
    {
        uint hash = unchecked((uint)(worldSeed == 0 ? 1 : worldSeed));
        hash = Mix(hash ^ unchecked((uint)seedSalt));
        hash = Mix(hash ^ unchecked((uint)regionX * 0x9E3779B9u));
        hash = Mix(hash ^ unchecked((uint)regionY * 0x85EBCA6Bu));
        float angle = (hash & 0x00FFFFFFu) / 16777216f * math.PI * 2f;
        return new float2(math.cos(angle), math.sin(angle));
    }

    private static uint Mix(uint value)
    {
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        value ^= value >> 16;
        return value;
    }
}

public static class ClimateFieldKernel
{
    public static float ApplyOrographicPrecipitation(
        float basePrecipitation,
        float currentHeight,
        float meanUpwindHeight,
        float maxUpwindHeight,
        float windwardGain,
        float leewardLoss)
    {
        float safeBase = math.saturate(math.isfinite(basePrecipitation)
            ? basePrecipitation
            : TerrainNoiseKernel.DefaultChannelValue);
        float safeHeight = math.saturate(math.isfinite(currentHeight)
            ? currentHeight
            : TerrainNoiseKernel.DefaultChannelValue);
        float safeMean = math.saturate(math.isfinite(meanUpwindHeight)
            ? meanUpwindHeight
            : safeHeight);
        float safeMaximum = math.saturate(math.isfinite(maxUpwindHeight)
            ? maxUpwindHeight
            : safeHeight);
        float uplift = math.max(0f, safeHeight - safeMean);
        float rainShadow = math.max(0f, safeMaximum - safeHeight);
        return math.saturate(
            safeBase +
            uplift * math.max(0f, windwardGain) -
            rainShadow * math.max(0f, leewardLoss));
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
        // Burst jobs call SampleBurst directly. Main-thread sampling does not need a separately
        // compiled function pointer, which produced an invalid Burst entry-point exception during
        // domain reload on Unity 2022.3.
        return SampleBurst(config, worldScaledPosition, worldSeed);
    }

    public static float Sample(
        in TerrainNoiseConfig config,
        Vector2 worldPosition,
        float noiseScale,
        int worldSeed,
        in WorldTopologyDomain domain)
    {
        return SampleBurst(
            config,
            new float2(worldPosition.x, worldPosition.y),
            noiseScale,
            worldSeed,
            domain);
    }

    internal static float SampleBurst(
        in TerrainNoiseConfig config,
        float2 worldScaledPosition,
        int worldSeed)
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

    internal static float SampleBurst(
        in TerrainNoiseConfig config,
        float2 worldPosition,
        float noiseScale,
        int worldSeed,
        in WorldTopologyDomain domain)
    {
        if (!domain.IsWrapped)
            return SampleBurst(config, worldPosition * noiseScale, worldSeed);

        float safeNoiseScale = math.max(0f, math.isfinite(noiseScale) ? noiseScale : PlanetData.DefaultNoiseScale);
        float coordScale = FinitePositiveOr(config.coordScale, 1f);
        float baseFrequency = FinitePositiveOr(config.frequency, 0.01f);
        float lacunarity = FinitePositiveOr(config.lacunarity, 2f);
        float persistence = math.clamp(FiniteOr(config.persistence, 0.5f), 0f, 1f);
        int octaves = math.clamp(config.octaves, 1, 12);
        float2 offset = new float2(
            FiniteOr(config.coordOffset.x, 0f),
            FiniteOr(config.coordOffset.y, 0f)) + GetSeedOffset(worldSeed, config.noiseType);
        float2 relative = worldPosition - domain.Min;
        float2 span = new float2(domain.Span.x, domain.Span.y);

        float sum = 0f;
        float amplitudeSum = 0f;
        float amplitude = 1f;
        float octaveFrequency = baseFrequency;
        for (int octave = 0; octave < octaves; octave++)
        {
            float2 repeat = math.max(
                new float2(1f),
                math.round(span * safeNoiseScale * coordScale * octaveFrequency));
            float2 phase = offset * octaveFrequency;
            phase -= math.floor(phase / repeat) * repeat;
            float2 samplePosition = relative / span * repeat + phase;
            float value = noise.pnoise(samplePosition, repeat) * 0.5f + 0.5f;
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
