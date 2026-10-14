using System;
using FlatWorld.Networking;
using FlatWorld.WorldModel;

public partial class SaveDataMgr
{
    #region 隔离现场草差量恢复
    /// <summary>只在隔离测试存档中捕获单格原草差量，供测试完成后原样恢复。</summary>
    public GrassCellState? CaptureTestRuntimeGrassDelta(Player player, RuntimeTerrainTileSample sample)
    {
        RequireTestGrassCell(player, sample);
        return GetRuntimeChunkRecord(sample.Address, false)?.GrassDeltas
            .Find(value => value.LocalPosition == sample.LocalCell)?.State;
    }

    /// <summary>仅撤销测试格新增的 Removed 差量，已有记录按捕获值恢复，其他格保持不变。</summary>
    public bool RestoreTestRuntimeGrassDelta(Player player, RuntimeTerrainTileSample sample, GrassCellState? previous)
    {
        RequireTestGrassCell(player, sample);
        ChunkSaveRecord record = GetRuntimeChunkRecord(sample.Address, false);
        GrassCellSaveDelta current = record?.GrassDeltas.Find(value => value.LocalPosition == sample.LocalCell);
        if (current == null) return !previous.HasValue;
        if (current.State != GrassCellState.Removed && current.State != previous) return false;
        if (previous.HasValue) current.State = previous.Value;
        else record.GrassDeltas.Remove(current);
        return true;
    }

    private void RequireTestGrassCell(Player player, RuntimeTerrainTileSample sample)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        if (!ReferenceEquals(this, TestExistingInstance) || !GameNetwork.HasStateAuthority || sample.Terrain == null || sample.Terrain.IsDisposed ||
            (uint)sample.LocalCell.x >= (uint)sample.Terrain.Width || (uint)sample.LocalCell.y >= (uint)sample.Terrain.Height)
            throw new InvalidOperationException("草差量恢复必须指向本轮隔离存档中的有效自有格。");
    }
    #endregion
}
