using Sirenix.OdinInspector;
using System;
using System.Collections;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

[Serializable]
public sealed class ChunkGenerator_River : ChunkGeneratorBase
{
    private const float SeaSalt = 80f;

    public override GenerationStage Stage => GenerationStage.Hydrology;

    [Header("水体 Tile")]
    public Tile_Block riverTileBlock;

    [Header("河道形状")]
    public int seed = 12345;
    [Min(8f)] public float channelSpacing = 128f;
    [Range(0.5f, 6f)] public float channelHalfWidth = 1.4f;
    [Range(0f, 48f)] public float bendAmplitude = 16f;
    [Range(0.0005f, 0.05f)] public float bendFrequency = 0.006f;
    [Range(0f, 1f)] public float widthVariation = 0.3f;
    public Vector2 flowDirection = new Vector2(0.35f, 0.94f);

    [Header("写入方式")]
    public RiverWriteMode writeMode = RiverWriteMode.ReplaceTop;
    [Range(0f, 1f)] public float riverDepthMin = 0.2f;
    [Range(0f, 1f)] public float riverDepthMax = 0.9f;

    [NonSerialized] private int _activeWorldSeed = 1;
    [NonSerialized] private bool _jobActive;
    [NonSerialized] private JobHandle _activeHandle;
    [NonSerialized] private NativeArray<float> _activeDepths;

    public enum RiverWriteMode
    {
        ReplaceTop = 0,
        AddLayer = 1,
        ReplaceAll = 2
    }

    public override IEnumerator GenerateAsync(MapGenerationContext context, int workBatchSize)
    {
        if (context?.Map?.Data == null)
            throw new ArgumentNullException(nameof(context), "[ChunkGenerator_River] 缺少地图生成上下文。");

        Map = context.Map;
        _activeWorldSeed = context.WorldSeed;
        ValidateConfiguration();

        int width = Map.Data.Width;
        int height = Map.Data.Height;
        int cellCount = checked(width * height);
        _activeDepths = new NativeArray<float>(cellCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        var job = new RiverMaskJob
        {
            Origin = new int2(Map.Data.position.x, Map.Data.position.y),
            Width = width,
            WorldSeed = _activeWorldSeed,
            SeedSalt = seed,
            ChannelSpacing = channelSpacing,
            ChannelHalfWidth = channelHalfWidth,
            BendAmplitude = bendAmplitude,
            BendFrequency = bendFrequency,
            WidthVariation = widthVariation,
            FlowDirection = new float2(flowDirection.x, flowDirection.y),
            RiverDepthRange = new float2(riverDepthMin, riverDepthMax),
            Depths = _activeDepths
        };

        _activeHandle = job.Schedule(cellCount, 64);
        _jobActive = true;
        try
        {
            while (!_activeHandle.IsCompleted)
            {
                if (context.IsCancellationRequested)
                    yield break;
                yield return null;
            }

            _activeHandle.Complete();
            var budget = new ChunkGenerationWorkBudget(Map, Mathf.Max(1, workBatchSize));
            for (int index = 0; index < cellCount; index++)
            {
                float depth = _activeDepths[index];
                if (depth >= 0f)
                {
                    int localX = index % width;
                    int localY = index / width;
                    WriteFreshWaterAt(
                        Map.Data.position + new Vector2Int(localX, localY),
                        depth);
                }

                if (!budget.ShouldYield())
                    continue;
                yield return null;
                budget.BeginNextFrame();
            }
        }
        finally
        {
            CompleteAndDisposeActiveJob();
        }
    }

    public override void CancelPendingWork()
    {
        CompleteAndDisposeActiveJob();
    }

    public bool TryEvaluateRiverCell(Vector2Int worldPosition, out float depth)
    {
        return TryEvaluateRiverCell(worldPosition, _activeWorldSeed, out depth);
    }

    public bool TryEvaluateRiverCell(Vector2Int worldPosition, int worldSeed, out float depth)
    {
        depth = EvaluateRiverDepth(
            new float2(worldPosition.x, worldPosition.y),
            worldSeed,
            seed,
            channelSpacing,
            channelHalfWidth,
            bendAmplitude,
            bendFrequency,
            widthVariation,
            new float2(flowDirection.x, flowDirection.y),
            new float2(riverDepthMin, riverDepthMax));
        return depth >= 0f;
    }

    internal bool TryEvaluateAppliedRiverCell(
        Vector2Int worldPosition,
        int worldSeed,
        TileData baseTerrain,
        out float depth)
    {
        if (!TryEvaluateRiverCell(worldPosition, worldSeed, out depth) || IsSeaWater(baseTerrain))
        {
            depth = 0f;
            return false;
        }

        return true;
    }

    public void ValidateConfiguration()
    {
        if (riverTileBlock?.tileDataTemplate is not TileData_Water)
            throw new InvalidOperationException("riverTileBlock 必须提供 TileData_Water 模板。");
        if (!IsFinitePositive(channelSpacing) || !IsFinitePositive(channelHalfWidth) ||
            !IsFinitePositive(bendFrequency) || !IsFinite(bendAmplitude) ||
            !IsFinite(widthVariation) || !IsFinite(flowDirection.x) || !IsFinite(flowDirection.y) ||
            flowDirection.sqrMagnitude <= 0.0001f)
        {
            throw new InvalidOperationException("河道形状参数非法。");
        }
        if (!IsFinite(riverDepthMin) || !IsFinite(riverDepthMax) || riverDepthMin < 0f || riverDepthMax > 1f || riverDepthMin > riverDepthMax)
            throw new InvalidOperationException("河流深度范围非法。");
    }

    private void WriteFreshWaterAt(Vector2Int worldPosition, float depth)
    {
        int layerCount = Map.Data.GetLayerCount(worldPosition);
        TileData top = Map.Data.GetTopTile(worldPosition);
        if (IsSeaWater(top))
            return;

        TileData riverTile = riverTileBlock.tileDataTemplate.Clone();
        if (riverTile is not TileData_Water waterTile)
            throw new InvalidOperationException("河流 Tile 模板克隆后不是 TileData_Water。");

        riverTile.position = new Vector3Int(worldPosition.x, worldPosition.y, 0);
        Vector2Int localPosition = worldPosition - Map.Data.position;
        Map.Data.SetPrecipitationAtLocal(localPosition.x, localPosition.y, 1f);
        riverTile.Initialize_Env(Map.Data.EnvironmentLayers, localPosition.x, localPosition.y);
        waterTile.salt = 0f;
        waterTile.deepValue = math.saturate(depth);

        if (layerCount == 0)
        {
            Map.Data.SetBaseTile(worldPosition, riverTile);
            return;
        }

        switch (writeMode)
        {
            case RiverWriteMode.AddLayer when top is not TileData_Water:
                Map.Data.PushTile(worldPosition, riverTile);
                break;
            case RiverWriteMode.ReplaceAll:
                Map.Data.ReplaceStack(worldPosition, riverTile);
                break;
            default:
                Map.Data.ReplaceTop(worldPosition, riverTile);
                break;
        }
    }

    internal static bool IsSeaWater(TileData tile)
    {
        return tile is TileData_Water water && math.abs(water.salt - SeaSalt) <= 0.01f;
    }

    private void CompleteAndDisposeActiveJob()
    {
        if (_jobActive)
            _activeHandle.Complete();
        if (_activeDepths.IsCreated)
            _activeDepths.Dispose();
        _jobActive = false;
    }

    private static float EvaluateRiverDepth(
        float2 worldPosition,
        int worldSeed,
        int seedSalt,
        float spacingValue,
        float halfWidthValue,
        float bendAmplitudeValue,
        float bendFrequencyValue,
        float widthVariationValue,
        float2 directionValue,
        float2 depthRange)
    {
        float2 direction = math.normalizesafe(directionValue, new float2(0f, 1f));
        float2 normal = new float2(-direction.y, direction.x);
        float along = math.dot(worldPosition, direction);
        float across = math.dot(worldPosition, normal);
        int generationSeed = unchecked(worldSeed * 486187739 ^ seedSalt * 16777619);
        float2 seedOffset = TerrainNoiseKernel.GetSeedOffset(generationSeed, NoiseType.Precipitation);
        float frequency = math.max(0.0005f, bendFrequencyValue);

        float bendNoise = TerrainNoiseKernel.SampleCNoise01(new float2(
            along * frequency + seedOffset.x,
            seedOffset.y * 0.731f));
        float bend = (bendNoise - 0.5f) * 2f * math.max(0f, bendAmplitudeValue);
        float spacing = math.max(8f, spacingValue);
        float wrappedInput = across + bend + spacing * 0.5f;
        float wrapped = wrappedInput - math.floor(wrappedInput / spacing) * spacing - spacing * 0.5f;
        float distance = math.abs(wrapped);

        float widthNoise = TerrainNoiseKernel.SampleCNoise01(new float2(
            along * frequency * 1.9f + seedOffset.x * 0.37f,
            seedOffset.y * 1.117f));
        float variation = math.saturate(widthVariationValue);
        float halfWidth = math.max(0.5f, halfWidthValue) * math.lerp(1f - variation, 1f + variation, widthNoise);
        if (distance > halfWidth)
            return -1f;

        float centerStrength = 1f - math.saturate(distance / math.max(0.001f, halfWidth));
        return math.lerp(math.saturate(depthRange.x), math.saturate(depthRange.y), centerStrength * centerStrength);
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool IsFinitePositive(float value) => IsFinite(value) && value > 0f;

    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard)]
    private struct RiverMaskJob : IJobParallelFor
    {
        public int2 Origin;
        public int Width;
        public int WorldSeed;
        public int SeedSalt;
        public float ChannelSpacing;
        public float ChannelHalfWidth;
        public float BendAmplitude;
        public float BendFrequency;
        public float WidthVariation;
        public float2 FlowDirection;
        public float2 RiverDepthRange;
        [WriteOnly] public NativeArray<float> Depths;

        public void Execute(int index)
        {
            int localX = index % Width;
            int localY = index / Width;
            Depths[index] = EvaluateRiverDepth(
                new float2(Origin.x + localX, Origin.y + localY),
                WorldSeed,
                SeedSalt,
                ChannelSpacing,
                ChannelHalfWidth,
                BendAmplitude,
                BendFrequency,
                WidthVariation,
                FlowDirection,
                RiverDepthRange);
        }
    }
}
