using System;
using System.Collections.Generic;
using UnityEngine;

public enum GenerationStage
{
    BaseTerrain = 100,
    Hydrology = 200,
    Structures = 300,
    Ecology = 400
}

public enum MapGenerationState
{
    Created,
    Running,
    Succeeded,
    Failed,
    Cancelled
}

/// <summary>
/// 单个区块生成任务的共享状态。环境、群系和结构占用均在这条管线中传递。
/// </summary>
public sealed class MapGenerationContext
{
    public Map Map { get; }
    public PlanetData PlanetData { get; }
    public int WorldSeed { get; }
    public WorldAddress WorldAddress { get; }
    public DimensionDefinition DimensionDefinition { get; }
    public IWorldGenerationDomain WorldDomain { get; }
    public ChunkGenerator_Land ClimateService { get; }
    public ChunkGenerator_River HydrologyService { get; }
    public StructureGenerationMask StructureMask { get; }

    public MapGenerationState State { get; private set; } = MapGenerationState.Created;
    public GenerationStage? CurrentStage { get; private set; }
    public string FailureReason { get; private set; }
    public Exception FailureException { get; private set; }
    public bool IsCancellationRequested => State == MapGenerationState.Cancelled;
    public bool HasFailed => State == MapGenerationState.Failed;
    public IReadOnlyList<GenerationStage> CompletedStages => _completedStages;

    public BiomeResolver BiomeResolver { get; private set; }
    public byte[] BiomeIndices { get; private set; }
    public int BiomeWidth { get; private set; }
    public int BiomeHeight { get; private set; }

    private readonly List<GenerationStage> _completedStages = new(4);

    public MapGenerationContext(
        Map map,
        PlanetData planetData,
        int worldSeed,
        WorldAddress worldAddress,
        DimensionDefinition dimensionDefinition,
        IWorldGenerationDomain worldDomain = null)
    {
        Map = map ?? throw new ArgumentNullException(nameof(map));
        PlanetData = planetData;
        WorldSeed = worldSeed == 0 ? 1 : worldSeed;
        WorldAddress = worldAddress;
        DimensionDefinition = dimensionDefinition;
        WorldDomain = worldDomain ?? UnboundedWorldGenerationDomain.Instance;
        ClimateService = map.LandGenerator;
        HydrologyService = map.GetGenerator<ChunkGenerator_River>();
        int width = map.Data?.Width ?? 0;
        int height = map.Data?.Height ?? 0;
        StructureMask = new StructureGenerationMask(width, height);
    }

    public void BeginStage(GenerationStage stage)
    {
        if (State is MapGenerationState.Failed or MapGenerationState.Cancelled)
            throw new InvalidOperationException("失败或取消的生成上下文不能继续执行。");

        State = MapGenerationState.Running;
        CurrentStage = stage;
    }

    public void CompleteStage(GenerationStage stage)
    {
        if (CurrentStage != stage)
            throw new InvalidOperationException($"生成阶段完成顺序错误：current={CurrentStage}, completed={stage}");

        _completedStages.Add(stage);
        CurrentStage = null;
    }

    public void MarkSucceeded()
    {
        if (State is MapGenerationState.Failed or MapGenerationState.Cancelled)
            return;

        CurrentStage = null;
        State = MapGenerationState.Succeeded;
    }

    public void Fail(string reason, Exception exception = null)
    {
        if (State == MapGenerationState.Cancelled)
            return;

        FailureReason = string.IsNullOrWhiteSpace(reason) ? "未知地图生成错误" : reason;
        FailureException = exception;
        CurrentStage = null;
        State = MapGenerationState.Failed;
    }

    public void Cancel(string reason = null)
    {
        if (State is MapGenerationState.Succeeded or MapGenerationState.Failed)
            return;

        FailureReason = reason ?? "地图生成已取消";
        CurrentStage = null;
        State = MapGenerationState.Cancelled;
    }

    public void SetBiomeCache(BiomeResolver resolver, byte[] indices, int width, int height)
    {
        if (resolver == null)
            throw new ArgumentNullException(nameof(resolver));
        if (indices == null || width <= 0 || height <= 0 || indices.Length != width * height)
            throw new ArgumentException("Biome 缓存尺寸与数据不一致。", nameof(indices));

        BiomeResolver = resolver;
        BiomeIndices = indices;
        BiomeWidth = width;
        BiomeHeight = height;
    }

    public bool TryGetResolvedBiome(int localX, int localY, out BiomeData biome)
    {
        biome = null;
        if (BiomeResolver == null || BiomeIndices == null ||
            (uint)localX >= (uint)BiomeWidth || (uint)localY >= (uint)BiomeHeight)
        {
            return false;
        }

        biome = BiomeResolver.GetBiome(BiomeIndices[localY * BiomeWidth + localX]);
        return biome != null;
    }

    public bool TryGetResolvedBiome(Vector2Int localPosition, out BiomeData biome)
    {
        return TryGetResolvedBiome(localPosition.x, localPosition.y, out biome);
    }
}
