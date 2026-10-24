using System;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using UnityEngine;

public partial class SaveDataMgr
{
    #region 权威地形变更通知
    public static event Action<string, ChunkRuntime, Vector2Int> AuthoritativeTerrainCellChanged;

    // 网络层只记脏格，事务及其回滚结束后再捕获最终状态，避免广播未提交的候选。
    private static void NotifyAuthoritativeTerrainCellChanged(ChunkRuntime chunk, Vector2Int local)
    {
        if (!GameNetwork.HasStateAuthority || chunk?.Terrain == null || chunk.Terrain.IsDisposed)
            return;
        AuthoritativeTerrainCellChanged?.Invoke(GameplayWorldRegistry.ExecutionWorldKey, chunk, local);
    }
    #endregion
}
