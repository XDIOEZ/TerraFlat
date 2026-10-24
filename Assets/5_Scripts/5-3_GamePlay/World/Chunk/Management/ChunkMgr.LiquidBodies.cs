using UnityEngine;

public partial class ChunkMgr
{
    #region 连通静水水域
    private WorldLiquidBodySystem liquidBodies;

    /// <summary>查询已加载天然静水的整体水量与平均水位，不会唤醒远处区块。</summary>
    public bool TryGetStaticWaterBody(Vector2 position, out WorldLiquidBodySnapshot snapshot)
    {
        snapshot = default;
        return liquidBodies != null && liquidBodies.TryGetBody(position, out snapshot);
    }

    internal bool WakeStaticWaterBody(RuntimeTerrainTileSample sample) =>
        liquidBodies?.TryWake(sample) == true;

    private void StopLiquidBodies()
    {
        WorldLiquidBodySystem previous = liquidBodies;
        liquidBodies = null;
        previous?.Dispose();
    }
    #endregion
}
