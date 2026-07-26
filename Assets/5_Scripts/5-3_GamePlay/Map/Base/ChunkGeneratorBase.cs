using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Chunk 生成器基类：
/// - 通过 Map.mapGenerators 列表按顺序执行，实现“迭代式”生成（先大陆、后河流…）
/// - 生成过程中共享 MapGenerationContext，便于后续生成器基于前面结果继续加工
/// </summary>
[Serializable]
public abstract class ChunkGeneratorBase
{
    #region 运行时引用
    [NonSerialized]
    public Map Map;
    #endregion

    #region 基础生命周期
    public virtual void Init(Map map)
    {
        Map = map;
    }

    /// <summary>
    /// 生成入口：由 Map 遍历调用。
    /// 注意：不要在这里“静默 return”；遇到关键引用缺失请 Debug.LogError。
    /// </summary>
    public abstract void Generate(MapGenerationContext context);

    /// <summary>
    /// 运行时分帧生成入口。默认保持旧生成器的同步行为；
    /// 计算量大的生成器可覆写此方法，在不改变管线顺序的前提下主动让帧。
    /// </summary>
    public virtual IEnumerator GenerateAsync(MapGenerationContext context, int workBatchSize)
    {
        Generate(context);
        yield break;
    }
    #endregion

    #region 工具
    protected void LogNullContext(string generatorName)
    {
        Debug.LogError($"[{generatorName}] ❌ context 为空，无法生成。");
    }

    protected void LogNullMap(string generatorName)
    {
        Debug.LogError($"[{generatorName}] ❌ Map 为空，无法生成。");
    }
    #endregion
}
