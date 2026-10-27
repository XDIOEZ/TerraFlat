using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using UnityEngine;
using UnityEngine.Pool;

public partial class SaveDataMgr
{
    #region 局部世界普通存档采集
    internal void CaptureRuntimeLocalWorldState(string worldKey)
    {
        if (!GameNetwork.HasStateAuthority || SaveData == null) return;
        using var scope = GameplayWorldRegistry.UseWorld(worldKey);
        if (!CaptureRuntimeTerrainStates())
            throw new InvalidOperationException("局部世界地形尚未完整捕获：" + worldKey);
        ChunkMgr.ExistingInstance?.CaptureRuntimeNaturalItemStates();
        CaptureRuntimeBuildingStates();
        if (!CaptureRuntimeAiEntityStates())
            throw new InvalidOperationException("局部世界居民尚未完整捕获：" + worldKey);
    }

    internal void CopyRuntimeLocalWorldSavedChunkOrigins(string worldKey, ICollection<Vector2Int> destination)
    {
        if (destination == null) throw new ArgumentNullException(nameof(destination));
        destination.Clear();
        foreach (ChunkSaveRecord record in chunkDeltas.Values)
            if (record?.PlanetName == worldKey && record.HasChanges) destination.Add(record.ChunkPosition);
    }
    #endregion

    #region 退役世界差量清理
    // 只有实体已移交且世界永久退役时才丢弃差量，普通场景解绑继续保留存档。
    internal void DiscardRuntimeWorldDifferences(string worldKey)
    {
        if (string.IsNullOrWhiteSpace(worldKey)) throw new ArgumentException("退役世界身份为空。", nameof(worldKey));
        if (GameplayWorldRegistry.GetWorldItemCount(worldKey) != 0)
            throw new InvalidOperationException("退役世界仍持有物品，不能删除存档差量：" + worldKey);
        string prefix = worldKey + "\u001f";
        RemoveRuntimeWorldDictionaryEntries(chunkBaselines, prefix);
        RemoveRuntimeWorldDictionaryEntries(chunkDeltas, prefix);
        RemoveRuntimeWorldDictionaryEntries(runtimeChunkBaselines, prefix);
        RemoveRuntimeWorldDictionaryEntries(runtimeBuildingDirtyAddresses, prefix);
        RemoveRuntimeWorldDictionaryEntries(unloadedRuntimeBuildingGuids, prefix);
        runtimeTerrainDirtyChunks.RemoveWhere(key => key.StartsWith(prefix, StringComparison.Ordinal));
        runtimeBuildingDirtyChunks.RemoveWhere(key => key.StartsWith(prefix, StringComparison.Ordinal));
        buildingUnloadFailures.RemoveWhere(key => key.StartsWith(prefix, StringComparison.Ordinal));
        worldRestoreStates.Remove(worldKey);
        parkedWorldStates.Remove(worldKey);
        using var scratch = ListPool<(string Planet, Vector2Int Origin)>.Get(out var keys);
        foreach (var key in chunkDeltaLookupKeys.Keys) if (key.Planet == worldKey) keys.Add(key);
        foreach (var key in keys) chunkDeltaLookupKeys.Remove(key);
    }

    private static void RemoveRuntimeWorldDictionaryEntries<T>(IDictionary<string, T> entries, string prefix)
    {
        using var scratch = ListPool<string>.Get(out var keys);
        foreach (string key in entries.Keys)
            if (key.StartsWith(prefix, StringComparison.Ordinal)) keys.Add(key);
        foreach (string key in keys) entries.Remove(key);
    }
    #endregion
}
