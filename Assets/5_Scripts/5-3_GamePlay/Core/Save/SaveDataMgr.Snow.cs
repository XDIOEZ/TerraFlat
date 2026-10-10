using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using MemoryPack;
using UnityEngine;

public partial class SaveDataMgr
{
    #region 积雪差量
    public void RecordSnowCell(RuntimeTerrainTileSample sample)
    {
        if (!GameNetwork.HasStateAuthority || SaveData == null) return;
        var manager = ChunkMgr.ExistingInstance;
        if (manager != null && manager.TryGetChunkRuntime(sample.Address, out ChunkRuntime chunk))
            RecordWeatherSurfaceChunk(chunk, Array.Empty<int>(),
                new[] { sample.LocalCell.y * sample.Terrain.Width + sample.LocalCell.x });
    }

    private static void RestoreSnowTerrain(ChunkRuntime chunk, ChunkSaveRecord record)
    {
        if (record.SnowCells == null) return;
        var seen = new HashSet<Vector2Int>();
        foreach (var state in record.SnowCells)
        {
            if (state == null || !seen.Add(state.LocalPosition))
                throw new InvalidOperationException("积雪存档包含空值或重复格子。");
            WorldSnowInteraction.Restore(chunk.Terrain, state);
        }
    }
    #endregion
}

/// <summary>保存区域天气雪与玩家编辑，铲空仍保留标记以免天然雪重新出现。</summary>
[Serializable, MemoryPackable]
public partial class SnowCellSaveData
{
    #region 雪格快照
    public Vector2Int LocalPosition;
    public bool Edited;
    public float Depth;
    public float SeasonalDepth;
    public float WeatherDepth;
    #endregion
}
