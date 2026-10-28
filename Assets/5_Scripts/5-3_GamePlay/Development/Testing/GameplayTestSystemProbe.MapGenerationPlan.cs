using System;
using System.Collections;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 地图生成阶段计划采样
    #if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    #endif
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void RegisterMapGenerationPlanRules() => Register(
        "world_streaming", "map_generation_plan", CaptureMapGenerationPlanRules);

    // 夹具只声明阶段，排序和拒绝结果均由正式生成计划返回。
    private static JObject CaptureMapGenerationPlanRules(JObject arguments)
    {
        var terrain = new MapPlanStageFixture(GenerationStage.BaseTerrain);
        var water = new MapPlanStageFixture(GenerationStage.Hydrology);
        var structure = new MapPlanStageFixture(GenerationStage.Structures);
        var ecologyFirst = new MapPlanStageFixture(GenerationStage.Ecology);
        var ecologySecond = new MapPlanStageFixture(GenerationStage.Ecology);
        var declared = new ChunkGeneratorBase[] { ecologyFirst, water, terrain, ecologySecond, structure };
        bool created = MapGenerationPipelinePlan.TryCreateOrderedGenerators(declared, out var ordered, out var reason);
        bool order = created && ordered.Count == 5 && ReferenceEquals(ordered[0], terrain) &&
            ReferenceEquals(ordered[1], water) && ReferenceEquals(ordered[2], structure) &&
            ReferenceEquals(ordered[3], ecologyFirst) && ReferenceEquals(ordered[4], ecologySecond);
        bool repeated = MapGenerationPipelinePlan.TryCreateOrderedGenerators(declared, out var next, out _) &&
            !ReferenceEquals(ordered, next) && ReferenceEquals(next[3], ecologyFirst);

        bool Reject(ChunkGeneratorBase[] input, string fragment) =>
            !MapGenerationPipelinePlan.TryCreateOrderedGenerators(input, out var result, out var failure) &&
            result.Count == 0 && !string.IsNullOrEmpty(failure) && failure.Contains(fragment);

        return new JObject
        {
            ["ordered"] = order,
            ["originalUnchanged"] = ReferenceEquals(declared[0], ecologyFirst) && ReferenceEquals(declared[2], terrain),
            ["separatePlans"] = repeated,
            ["successReasonEmpty"] = reason == null,
            ["rejectEmpty"] = Reject(null, "为空") && Reject(Array.Empty<ChunkGeneratorBase>(), "为空"),
            ["rejectNullStage"] = Reject(new ChunkGeneratorBase[] { terrain, null }, "mapGenerators[1]"),
            ["rejectMissingTerrain"] = Reject(new ChunkGeneratorBase[] { water, ecologyFirst }, "BaseTerrain"),
            ["rejectDuplicateTerrain"] = Reject(new ChunkGeneratorBase[] { terrain, terrain }, "当前为 2"),
            ["rejectUndefinedStage"] = Reject(new ChunkGeneratorBase[] { terrain, new MapPlanStageFixture((GenerationStage)999) }, "非法生成阶段")
        };
    }

    private sealed class MapPlanStageFixture : ChunkGeneratorBase
    {
        private readonly GenerationStage stage;
        public MapPlanStageFixture(GenerationStage stage) => this.stage = stage;
        public override GenerationStage Stage => stage;
        public override IEnumerator GenerateAsync(MapGenerationContext context, int workBatchSize) { yield break; }
    }
    #endregion
}
