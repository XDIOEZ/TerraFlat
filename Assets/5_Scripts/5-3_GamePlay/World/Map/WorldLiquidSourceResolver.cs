using UnityEngine;

/// <summary>
/// 一次准心命中的世界液体来源。液体身份与深度来自独立 Liquid 层。
/// </summary>
public readonly struct WorldLiquidSourceTarget
{
    public WorldLiquidSourceTarget(RuntimeTerrainTileSample sample, LiquidDefinition liquid)
    {
        Sample = sample;
        Liquid = liquid;
    }

    public RuntimeTerrainTileSample Sample { get; }
    public LiquidDefinition Liquid { get; }
    public Vector2Int WorldCell => Sample.WorldCell;
}

/// <summary>
/// 把权威世界地块的 LiquidId 解析成统一 LiquidDefinition。
/// 不依赖碰撞体或 Ground Behaviour；MOD 液体只需在目录声明 worldWater 配置。
/// </summary>
public static class WorldLiquidSourceResolver
{
    #region 液体来源解析
    public static bool TryResolve(Vector2 worldPosition, out WorldLiquidSourceTarget target)
    {
        target = default;
        ChunkMgr chunkManager = ChunkMgr.ExistingInstance;
        return chunkManager != null &&
            chunkManager.TryGetRuntimeTerrainTile(worldPosition, out RuntimeTerrainTileSample sample) &&
            TryResolve(sample, out target);
    }

    /// <summary>复用调用方本轮的权威地形采样，不重复进行世界寻址。</summary>
    public static bool TryResolve(RuntimeTerrainTileSample sample, out WorldLiquidSourceTarget target)
    {
        target = default;
        if (sample.LiquidDepth <= 0f || !WorldLiquidSystem.TryGetDefinition(sample, out LiquidDefinition liquid))
            return false;

        target = new WorldLiquidSourceTarget(sample, liquid);
        return true;
    }
    #endregion
}
