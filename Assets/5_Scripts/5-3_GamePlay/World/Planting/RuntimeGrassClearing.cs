using FlatWorld.WorldModel;

/// <summary>锄地、剪草、建筑共同使用的草层移除入口；不删除花朵或其它独立生态物。</summary>
public static class RuntimeGrassClearing
{
    #region 草层事务

    /// <summary>建筑在材料事务成功后清除其正式占地格；失败候选不调用此入口。</summary>
    public static bool ClearAt(UnityEngine.Vector2 position) => ChunkMgr.ExistingInstance != null &&
        ChunkMgr.ExistingInstance.TryGetRuntimeTerrainTile(position, out var sample) && Clear(sample);

    public static bool Clear(RuntimeTerrainTileSample sample)
    {
        if (!FlatWorld.Networking.GameNetwork.HasStateAuthority || sample.Terrain == null || sample.Terrain.IsDisposed)
            return false;
        int x = sample.LocalCell.x, y = sample.LocalCell.y;
        bool existed = sample.Terrain.GetGrass(x, y) != 0;
        sample.Terrain.SetGrass(x, y, 0);
        SaveDataMgr.Instance.RecordRuntimeGrassRemoved(sample);
        return existed;
    }

    #endregion
}
