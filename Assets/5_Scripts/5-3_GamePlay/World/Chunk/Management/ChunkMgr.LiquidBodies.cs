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

    /// <summary>选择真实供水格，缺水时按需准备邻区；实际扣量和回滚仍由既有地表事务负责。</summary>
    public bool TryGetConnectedLiquidPumpSource(Vector2 position, out RuntimeTerrainTileSample source)
    {
        source = default;
        return liquidBodies != null && liquidBodies.TryGetPumpSource(position, out source);
    }

    private void StopLiquidBodies()
    {
        WorldLiquidBodySystem previous = liquidBodies;
        liquidBodies = null;
        previous?.Dispose();
    }
    #endregion
}
