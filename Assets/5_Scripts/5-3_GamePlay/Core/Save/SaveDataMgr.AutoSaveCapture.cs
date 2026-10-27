using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.SceneManagement;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

public partial class SaveDataMgr
{
    #region 自动保存的世界分段采集

    internal readonly struct AutoSaveWorldScope
    {
        private readonly SaveDataMgr manager;
        private readonly GameSaveData save;
        private readonly int sceneHandle;
        private readonly WorldRuntime world;
        private readonly long worldEpoch;

        internal AutoSaveWorldScope(SaveDataMgr manager)
        {
            this.manager = manager;
            save = manager.SaveData;
            sceneHandle = GameplayWorldRegistry.CurrentScene.handle;
            world = ChunkMgr.ExistingInstance?.WorldRuntime;
            worldEpoch = world?.Epoch ?? long.MinValue;
        }

        internal bool IsCurrent => manager != null &&
            ReferenceEquals(manager.SaveData, save) &&
            GameplayWorldRegistry.CurrentScene.handle == sceneHandle &&
            ReferenceEquals(ChunkMgr.ExistingInstance?.WorldRuntime, world) &&
            (world?.Epoch ?? long.MinValue) == worldEpoch;
    }

    // 维度切换沿用同一存档对象，因此同时冻结场景和世界运行体身份。
    internal AutoSaveWorldScope CaptureAutoSaveWorldScope() => new(this);

    private IEnumerator CaptureRuntimeTerrainStatesCoroutine(AutoSaveWorkBudget budget)
    {
        ChunkMgr manager = ChunkMgr.ExistingInstance;
        if (!GameNetwork.HasStateAuthority || SaveData == null ||
            runtimeTerrainDirtyChunks.Count == 0 || manager?.Chunks == null)
            yield break;

        var chunks = new List<KeyValuePair<RuntimeWorldAddress, ChunkRuntime>>(manager.Chunks);
        string planetName = ResolveRuntimePlanetName();
        foreach (var pair in chunks)
        {
            ChunkRuntime chunk = pair.Value;
            string key = BuildChunkKey(planetName, ToChunkName(pair.Key));
            if (chunk != null && chunk.DataStatus == ChunkDataStatus.Ready &&
                chunk.Terrain != null && !chunk.Terrain.IsDisposed &&
                runtimeTerrainDirtyChunks.Contains(key) &&
                TryEnsureRuntimeChunkBaseline(key, chunk, out RuntimeChunkBaseline baseline))
            {
                // 每区块原子采集，随后发生的新地形修改继续保留脏标记。
                runtimeTerrainDirtyChunks.Remove(key);
                try { RefreshRuntimeChunkDelta(key, planetName, pair.Key, chunk, baseline); }
                catch { runtimeTerrainDirtyChunks.Add(key); throw; }
            }
            if (budget.ShouldYield())
                yield return budget.WaitForNextSlice();
        }
    }

    private IEnumerator CaptureRuntimeBuildingStatesCoroutine(AutoSaveWorkBudget budget)
    {
        ItemMgr items = ItemMgr.GetInstance();
        ChunkMgr chunks = ChunkMgr.ExistingInstance;
        if (!GameNetwork.HasStateAuthority || SaveData == null || items == null ||
            chunks == null || !chunks.IsWorldModelRuntimeActive)
            yield break;

        string planetName = ResolveRuntimePlanetName();
        var addresses = new Dictionary<string, RuntimeWorldAddress>(StringComparer.Ordinal);
        string worldPrefix = planetName + "\u001f";
        foreach (var pair in runtimeBuildingDirtyAddresses)
            if (pair.Key.StartsWith(worldPrefix, StringComparison.Ordinal)) addresses.Add(pair.Key, pair.Value);
        var snapshot = new List<Item>(items.WorldRunTimeItems.Values);
        foreach (Item item in snapshot)
        {
            if (IsRuntimeBuildingItemInWorld(item, planetName))
            {
                RuntimeWorldAddress address = chunks.ResolveWorldAddress(item.transform.position);
                addresses[BuildChunkKey(planetName, ToChunkName(address))] = address;
            }
            if (budget.ShouldYield())
                yield return budget.WaitForNextSlice();
        }

        var savedItems = new List<ItemData>();
        foreach (var pair in addresses)
        {
            snapshot.Clear();
            snapshot.AddRange(items.WorldRunTimeItems.Values);
            savedItems.Clear();
            bool succeeded = true;
            // 同区块建筑的捕获与合并不跨帧，避免拆除后把旧建筑快照重新写回来。
            foreach (Item item in snapshot)
            {
                if (!IsRuntimeBuildingItemInWorld(item, planetName)) continue;
                RuntimeWorldAddress address = chunks.ResolveWorldAddress(item.transform.position);
                if (!string.Equals(BuildChunkKey(planetName, ToChunkName(address)), pair.Key, StringComparison.Ordinal)) continue;
                try
                {
                    item.Save();
                    if (item.itemData == null) throw new InvalidDataException($"建筑 {item.name} 的 ItemData 为空");
                    savedItems.Add(CloneItemData(item.itemData));
                }
                catch (Exception exception)
                {
                    succeeded = false;
                    Debug.LogError($"[SaveDataMgr] 分帧保存运行时建筑失败：{item.name}", item);
                    Debug.LogException(exception);
                }
            }
            if (succeeded)
            {
                MergeRuntimeBuildingItems(planetName, pair.Value, savedItems);
                runtimeBuildingDirtyChunks.Remove(pair.Key);
                runtimeBuildingDirtyAddresses.Remove(pair.Key);
            }
            yield return budget.WaitForNextSlice();
        }
    }

    #endregion
}
