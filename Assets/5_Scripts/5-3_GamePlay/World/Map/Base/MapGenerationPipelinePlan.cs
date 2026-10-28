using System;
using System.Collections.Generic;

// 管线计划只验证与排序声明的生成阶段，不持有地图或推进生成。
internal static class MapGenerationPipelinePlan
{
    #region 生成阶段计划
    public static bool TryCreateOrderedGenerators(
        IReadOnlyList<ChunkGeneratorBase> generators,
        out List<ChunkGeneratorBase> orderedGenerators,
        out string failureReason)
    {
        orderedGenerators = new List<ChunkGeneratorBase>();
        failureReason = null;
        if (generators == null || generators.Count == 0)
        {
            failureReason = "mapGenerators 为空。";
            return false;
        }

        var entries = new List<(ChunkGeneratorBase Generator, int SerializedIndex)>(generators.Count);
        int baseTerrainCount = 0;
        for (int i = 0; i < generators.Count; i++)
        {
            ChunkGeneratorBase generator = generators[i];
            if (generator == null)
            {
                failureReason = $"mapGenerators[{i}] 为空。";
                return false;
            }
            if (!Enum.IsDefined(typeof(GenerationStage), generator.Stage))
            {
                failureReason = $"mapGenerators[{i}] 使用了非法生成阶段 {(int)generator.Stage}。";
                return false;
            }
            if (generator.Stage == GenerationStage.BaseTerrain)
                baseTerrainCount++;
            entries.Add((generator, i));
        }

        if (baseTerrainCount != 1)
        {
            failureReason = $"生成管线必须恰好包含一个 BaseTerrain，当前为 {baseTerrainCount}。";
            return false;
        }
        entries.Sort((left, right) =>
        {
            int stageOrder = left.Generator.Stage.CompareTo(right.Generator.Stage);
            return stageOrder != 0 ? stageOrder : left.SerializedIndex.CompareTo(right.SerializedIndex);
        });
        for (int i = 0; i < entries.Count; i++)
            orderedGenerators.Add(entries[i].Generator);
        return true;
    }
    #endregion
}
