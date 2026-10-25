using UnityEngine;

public partial class ChunkMgr
{
    #region 连通液体水域
    private WorldLiquidBodySystem liquidBodies;

    /// <summary>查询已加载天然水域的整体水量与平均水位，不会唤醒远处区块。</summary>
    public bool TryGetStaticWaterBody(Vector2 position, out WorldLiquidBodySnapshot snapshot)
    {
        snapshot = default;
        return liquidBodies != null && liquidBodies.TryGetBody(position, out snapshot);
    }

    internal bool WakeStaticWaterBody(RuntimeTerrainTileSample sample) =>
        liquidBodies?.TryWake(sample) == true;

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
