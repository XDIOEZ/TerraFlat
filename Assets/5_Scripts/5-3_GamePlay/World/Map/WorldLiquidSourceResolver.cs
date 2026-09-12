using UnityEngine;

/// <summary>
/// 一次准心命中的世界液体来源。当前地形液体没有有限份数状态，因此 Tile 来源保持无限源语义。
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
/// 不依赖碰撞体、不识别具体 Water ID，任何实现 IWorldLiquidSourceData 的地块都可接入同一取液链路。
/// </summary>
public static class WorldLiquidSourceResolver
{
    public static bool TryResolve(Vector2 worldPosition, out WorldLiquidSourceTarget target)
    {
        target = default;
        ChunkMgr chunkManager = ChunkMgr.ExistingInstance;
        if (chunkManager == null ||
            !chunkManager.TryGetRuntimeTileEffect(
                worldPosition,
                out RuntimeTerrainTileSample sample,
                out TileData tileData,
                out _) ||
            tileData is not IWorldLiquidSourceData liquidSource ||
            string.IsNullOrWhiteSpace(liquidSource.LiquidId))
        {
            return false;
        }

        GameRes gameRes = GameRes.ExistingInstance;
        if (gameRes == null ||
            !gameRes.TryGetLiquidDefinition(liquidSource.LiquidId, out LiquidDefinition liquid))
        {
            return false;
        }

        target = new WorldLiquidSourceTarget(sample, liquid);
        return true;
    }
}
