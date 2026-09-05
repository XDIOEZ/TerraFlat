using System;
using System.Collections;
using System.Collections.Generic;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

/// <summary>
/// ChunkView 的自然生态与临时掉落物表现适配器。
/// 后台只返回 NaturalItemPlacement；本组件在主线程通过 ItemMgr 创建现有 Item，显式挂在
/// NaturalItems 子节点，并在区块卸载前把权威状态写回 PlanetData 的生态差量存档。
/// 应用退出不保存生态差量；已有管理器仍存活时正常注销物品，依赖引用仅在绑定和登记时获取。
/// </summary>
public sealed class ChunkNaturalItemRenderer : MonoBehaviour, IChunkViewRenderer
{
    #region 字段

    private const float AutoSaveFrameBudgetSeconds = 0.0025f;

    private readonly Dictionary<int, Item> spawnedItems = new();
    private readonly HashSet<Item> transientItems = new();
    private readonly HashSet<int> generatedPortalGuids = new();
    private ChunkRuntime boundChunk;
    private EnvironmentLayers environmentLayers;
    // 绑定时记录依赖，销毁阶段不再通过单例搜索场景。
    private ItemMgr itemManager;
    private ChunkMgr chunkManager;
    private bool applicationQuitting;
    private bool unbinding;

    public int SpawnedItemCount => spawnedItems.Count;

    /// <summary>按稳定 GUID 查询当前区块已经实例化的自然物。</summary>
    public bool TryGetSpawnedItem(int guid, out Item item)
    {
        return spawnedItems.TryGetValue(guid, out item) && item != null;
    }

    /// <summary>登记区块临时掉落物，使解绑时回收它而不写入生态存档。</summary>
    public void RegisterTransientItem(Item item)
    {
        if (applicationQuitting || unbinding || item == null || item.DestructionHandled ||
            !transientItems.Add(item))
            return;

        if (itemManager == null)
            itemManager = ItemMgr.Instance;
        item.OnItemDestroy -= HandleTransientItemDestroy;
        item.OnItemDestroy += HandleTransientItemDestroy;
    }

    /// <summary>临时掉落物被拾取或销毁后解除登记。</summary>
    private void HandleTransientItemDestroy(Item item)
    {
        if (item == null)
            return;

        item.OnItemDestroy -= HandleTransientItemDestroy;
        transientItems.Remove(item);
    }

    #endregion

    #region 绑定生命周期

    /// <summary>按纯生成结果实例化当前区块的自然物品。</summary>
    public void Bind(ChunkRuntime chunk)
    {
        if (chunk == null)
            throw new ArgumentNullException(nameof(chunk));
        if (chunk.Terrain == null)
            throw new InvalidOperationException("Cannot bind natural items before terrain is ready.");
        if (ReferenceEquals(boundChunk, chunk))
            return;

        Unbind();
        itemManager = ItemMgr.GetInstance();
        chunkManager = ChunkMgr.ExistingInstance;
        boundChunk = chunk;
        environmentLayers = BuildEnvironmentLayers(chunk.Terrain);

        IReadOnlyList<NaturalItemPlacement> placements = chunk.Ecology?.Placements;
        if (placements == null || placements.Count == 0)
            return;
        if (itemManager == null || chunkManager == null)
            throw new InvalidOperationException("绑定自然物需要有效的 ItemMgr 和 ChunkMgr。");

        for (int i = 0; i < placements.Count; i++)
            SpawnPlacement(placements[i]);

        // 传送门可能和玩家在同一帧生成；立即同步一次物理变换，避免首个交互帧拿到旧碰撞位置。
        if (generatedPortalGuids.Count > 0)
            Physics2D.SyncTransforms();
    }

    /// <summary>正常解绑时保存并注销物品；管理器已销毁时只解除登记，由场景销毁子对象。</summary>
    public void Unbind()
    {
        if (unbinding || (boundChunk == null && spawnedItems.Count == 0 && transientItems.Count == 0))
            return;

        unbinding = true;
        try
        {
            CaptureState();
            var items = new List<Item>(spawnedItems.Values);
            foreach (Item transientItem in transientItems)
            {
                if (transientItem != null && !items.Contains(transientItem))
                    items.Add(transientItem);
            }

            for (int i = 0; i < items.Count; i++)
            {
                Item item = items[i];
                if (item == null)
                    continue;
                item.OnItemDestroy -= HandleNaturalItemDestroy;
                item.OnItemDestroy -= HandleTransientItemDestroy;
                if (itemManager != null && !item.DestructionHandled)
                    itemManager.DespawnItem(item, saveData: false, detachFromChunk: false);
            }
        }
        finally
        {
            spawnedItems.Clear();
            transientItems.Clear();
            generatedPortalGuids.Clear();
            environmentLayers = null;
            boundChunk = null;
            itemManager = null;
            chunkManager = null;
            unbinding = false;
        }
    }

    /// <summary>停止播放和关闭程序都在对象销毁前关闭生态保存，避免把退出误记为采集。</summary>
    private void OnApplicationQuit() => applicationQuitting = true;

    /// <summary>保存当前自然物状态，不改变实例生命周期。</summary>
    public void CaptureState()
    {
        if (applicationQuitting || boundChunk == null || chunkManager == null ||
            chunkManager.IsWorldRuntimeShuttingDown || !GameNetwork.HasStateAuthority)
        {
            return;
        }

        RuntimeWorldAddress address = boundChunk.Address;
        foreach (KeyValuePair<int, Item> pair in spawnedItems)
        {
            Item item = pair.Value;
            if (item == null || item.itemData == null || item.DestructionHandled)
                continue;
            // 天然传送门完全由确定性基线恢复，不能被一次临时销毁写进删除/状态差量。
            if (generatedPortalGuids.Contains(pair.Key))
                continue;

            try
            {
                item.Save();
                ItemData snapshot = FastCloner.FastCloner.DeepClone(item.itemData);
                chunkManager.CaptureNaturalItemState(address, snapshot);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[ChunkNaturalItemRenderer] 保存自然物失败：{item.name}，{exception}",
                    item);
            }
        }
    }

    /// <summary>自动保存专用的自然物分帧快照，避免一次克隆全部表现物。</summary>
    public IEnumerator CaptureStateCoroutine()
    {
        if (applicationQuitting || boundChunk == null || chunkManager == null ||
            chunkManager.IsWorldRuntimeShuttingDown || !GameNetwork.HasStateAuthority)
        {
            yield break;
        }

        RuntimeWorldAddress address = boundChunk.Address;
        ChunkRuntime capturedChunk = boundChunk;
        List<KeyValuePair<int, Item>> items = new List<KeyValuePair<int, Item>>(spawnedItems);
        float frameStart = Time.realtimeSinceStartup;
        for (int i = 0; i < items.Count; i++)
        {
            // 分帧期间发生退出或重新绑定时，旧快照不能继续写回世界。
            if (applicationQuitting || !ReferenceEquals(boundChunk, capturedChunk) ||
                chunkManager == null || chunkManager.IsWorldRuntimeShuttingDown)
                yield break;

            KeyValuePair<int, Item> pair = items[i];
            Item item = pair.Value;
            if (item != null && item.itemData != null && !item.DestructionHandled &&
                !generatedPortalGuids.Contains(pair.Key))
            {
                try
                {
                    item.Save();
                    ItemData snapshot = FastCloner.FastCloner.DeepClone(item.itemData);
                    chunkManager.CaptureNaturalItemState(address, snapshot);
                }
                catch (Exception exception)
                {
                    Debug.LogError($"[ChunkNaturalItemRenderer] 分帧保存自然物失败：{item.name}，{exception}",
                        item);
                }
            }

            if (Time.realtimeSinceStartup - frameStart >= AutoSaveFrameBudgetSeconds)
            {
                frameStart = Time.realtimeSinceStartup;
                yield return null;
            }
        }
    }

    #endregion

    #region 物品实例化

    /// <summary>应用删除/状态覆盖后实例化一个自然物。</summary>
    private void SpawnPlacement(NaturalItemPlacement placement)
    {
        if (boundChunk == null || itemManager == null || placement.Guid == 0 ||
            string.IsNullOrWhiteSpace(placement.ItemId))
        {
            return;
        }

        RuntimeWorldAddress address = boundChunk.Address;
        if (chunkManager != null &&
            chunkManager.IsNaturalItemRemoved(address, placement.Guid))
        {
            return;
        }

        Vector3 position = new Vector3(
            address.ChunkOrigin.X + placement.LocalX + 0.5f + placement.OffsetX,
            address.ChunkOrigin.Y + placement.LocalY + 0.5f + placement.OffsetY,
            0f);
        Quaternion rotation = Quaternion.identity;
        Vector3 scale = Vector3.one;
        ItemData changedData = null;
        if (chunkManager != null)
            chunkManager.TryGetNaturalItemOverride(address, placement.Guid, out changedData);

        Item item = null;
        try
        {
            if (changedData != null && !string.IsNullOrWhiteSpace(changedData.IDName))
            {
                if (changedData.transform != null)
                {
                    position = changedData.transform.position;
                    rotation = changedData.transform.rotation;
                    scale = changedData.transform.scale;
                }
                item = itemManager.InstantiateItem(
                    changedData, position, rotation, scale, gameObject);
            }
            else
            {
                item = itemManager.InstantiateItemDeterministic(
                    placement.ItemId,
                    placement.Guid,
                    position,
                    rotation,
                    scale,
                    gameObject);
            }

            if (item == null)
                return;

            item.Load();
            if (placement.IsDimensionPortal)
            {
                DimensionPortal portal = item.GetComponentInChildren<DimensionPortal>(true);
                if (portal == null)
                    throw new InvalidOperationException(
                        $"生成传送门物品缺少 DimensionPortal：{placement.ItemId}");
                // 显式绑定拥有者，切换维度时不再依赖父层级查找的时序。
                portal.ConfigureGenerated(placement.TargetDimensionId, item);
                if (item.itemData.Stack != null)
                    item.itemData.Stack.CanBePickedUp = false;
                generatedPortalGuids.Add(placement.Guid);
            }
            INaturalResourceInitializer[] initializers =
                item.GetComponentsInChildren<INaturalResourceInitializer>(true);
            foreach (INaturalResourceInitializer initializer in initializers)
                initializer.InitializeNaturalResource(unchecked((uint)placement.Guid));
            item.Initialize_Env(environmentLayers,
                new Vector2Int(placement.LocalX, placement.LocalY));
            if (!placement.IsDimensionPortal)
                item.OnItemDestroy += HandleNaturalItemDestroy;
            spawnedItems[placement.Guid] = item;
        }
        catch (Exception exception)
        {
            if (item != null && itemManager != null && !item.DestructionHandled)
                itemManager.DespawnItem(item, saveData: false, detachFromChunk: false);
            Debug.LogWarning(
                $"[ChunkNaturalItemRenderer] 自然物实例化失败：{placement.ItemId}，规则={placement.RuleId}，{exception.Message}",
                this);
        }
    }

    /// <summary>自然物被玩家采集或其它系统销毁时写入删除列表。</summary>
    private void HandleNaturalItemDestroy(Item item)
    {
        if (applicationQuitting || unbinding || item == null || item.itemData == null)
            return;

        int guid = item.itemData.Guid;
        spawnedItems.Remove(guid);
        if (boundChunk != null && chunkManager != null && !chunkManager.IsWorldRuntimeShuttingDown)
            chunkManager.MarkNaturalItemRemoved(boundChunk.Address, guid);
    }

    #endregion

    #region 环境适配

    /// <summary>把纯地形环境数组适配成现有 Item 模块使用的 EnvironmentLayers。</summary>
    private static EnvironmentLayers BuildEnvironmentLayers(ChunkTerrainData terrain)
    {
        var layers = new EnvironmentLayers();
        layers.EnsureSize(terrain.Width, terrain.Height);
        CopyLayer(terrain, "temperature", layers.Temperature);
        CopyLayer(terrain, "temperature.celsius", layers.TemperatureCelsius);
        CopyLayer(terrain, "precipitation", layers.Precipitation);
        CopyLayer(terrain, "height", layers.Height);
        for (int y = 0; y < terrain.Height; y++)
        for (int x = 0; x < terrain.Width; x++)
        {
            layers.WindX[x, y] = 1f;
            layers.WindY[x, y] = 0f;
            layers.Light[x, y] = 1f;
        }
        return layers;
    }

    /// <summary>复制一个一维纯数据层到 EnvironmentLayers 的二维数组。</summary>
    private static void CopyLayer(ChunkTerrainData terrain, string layerId, float[,] target)
    {
        if (!terrain.TryCopyEnvironmentLayer(layerId, out float[] values))
            return;

        for (int y = 0; y < terrain.Height; y++)
        for (int x = 0; x < terrain.Width; x++)
            target[x, y] = values[y * terrain.Width + x];
    }

    #endregion
}
