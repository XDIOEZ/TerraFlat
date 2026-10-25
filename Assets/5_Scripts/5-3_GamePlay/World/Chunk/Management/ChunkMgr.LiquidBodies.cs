using UnityEngine;

public partial class ChunkMgr
{
    #region 连通液体水域
    private WorldLiquidBodySystem liquidBodies;
    public int SharedLiquidPumpSearchGroupCount => liquidBodies?.SharedPumpSearchGroupCount ?? 0;
    public long SharedLiquidPumpTopologyBuildCount => liquidBodies?.PumpSearchTopologyBuildCount ?? 0;

    /// <summary>查询已加载天然水域的整体水量与平均水位，不会唤醒远处区块。</summary>
    public bool TryGetStaticWaterBody(Vector2 position, out WorldLiquidBodySnapshot snapshot)
    {
        snapshot = default;
        return liquidBodies != null && liquidBodies.TryGetBody(position, out snapshot);
    }

    internal bool WakeStaticWaterBody(RuntimeTerrainTileSample sample) =>
        liquidBodies?.TryWake(sample) == true;

    /// <summary>多台泵共用所属水域的候选格和搜索进度，搜索中不准提前抽入口残水。</summary>
    public WorldLiquidPumpSourceSelection SelectConnectedLiquidPumpSource(Vector2 position, out RuntimeTerrainTileSample source)
    {
        source = default;
        return liquidBodies == null ? WorldLiquidPumpSourceSelection.Searching : liquidBodies.SelectConnectedPumpSource(position, out source);
    }

    /// <summary>冷地表写入变化后使相关水域摘要失效，不能继续沿旧干湿连接取水。</summary>
    public void InvalidateConnectedLiquidPumpSearch(Vector2Int cell) => liquidBodies?.InvalidatePumpSearchAtCell(cell);

    /// <summary>液体后台缓存释放后只回收窗口外且没有其它使用者的数据。</summary>
    internal bool TryEvictUnleasedDiffusionChunkOutsideRuntimeWindows(FlatWorld.WorldModel.WorldAddress address) =>
        runtimeChunkManager != null && runtimeChunkManager.TryEvictUnleasedChunkOutsideRuntimeWindows(address);

    private void StopLiquidBodies()
    {
        WorldLiquidBodySystem previous = liquidBodies;
        liquidBodies = null;
        previous?.Dispose();
    }
    #endregion
}
