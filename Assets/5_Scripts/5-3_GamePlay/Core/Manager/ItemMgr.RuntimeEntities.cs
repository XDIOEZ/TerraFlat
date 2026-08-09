using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.SceneManagement;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

/// <summary>
/// AI 实体识别工具。运行时识别 IAIActor、生态物种及旧 Prefab 的兼容标记模块，
/// 存档数据识别 AI 模块 ID 与生成器物种目录，用于让实体生命周期脱离旧 Chunk/Map 对象。
/// </summary>
internal static class RuntimeAiEntityUtility
{
    private const string GenericAiModuleId = "AI";
    private const string AiModulePrefix = "AI_";
    private const string LegacyAiChunkMarkerId = "ChunkAssigner";

    /// <summary>判断当前 Item 是否承载可运行的实体 AI。</summary>
    public static bool IsAiEntity(Item item)
    {
        if (item == null || item is Player || item is Map)
            return false;

        if (MonsterSpawnerManager.IsRegisteredSpeciesId(item.itemData?.IDName) ||
            item.GetComponentInChildren<Mod_ItemChunkAssigner>(true) != null)
        {
            return true;
        }

        MonoBehaviour[] behaviours = item.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is IAIActor)
                return true;
        }

        return false;
    }

    /// <summary>判断序列化 ItemData 是否属于实体 AI，供数据层存取时使用。</summary>
    public static bool IsAiData(ItemData data)
    {
        if (data == null || string.IsNullOrWhiteSpace(data.IDName))
            return false;

        if (data.ModuleDataDic != null)
        {
            foreach (KeyValuePair<string, ModuleData> pair in data.ModuleDataDic)
            {
                if (IsAiModuleId(pair.Key) ||
                    IsAiModuleId(pair.Value?.ID) ||
                    IsAiModuleId(pair.Value?.Name))
                {
                    return true;
                }
            }
        }

        return MonsterSpawnerManager.IsRegisteredSpeciesId(data.IDName);
    }

    private static bool IsAiModuleId(string value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               (string.Equals(value, GenericAiModuleId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, LegacyAiChunkMarkerId, StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith(AiModulePrefix, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// ItemMgr 的 AI 实体区块索引。索引键为纯 WorldModel.WorldAddress，
/// 实体统一挂在场景级 RuntimeEntities 根节点，不再成为 ChunkView 或旧 Chunk 的子对象。
/// </summary>
public partial class ItemMgr
{
    private const string RuntimeEntityRootName = "RuntimeEntities";

    private readonly Dictionary<RuntimeWorldAddress, HashSet<Item>> _runtimeAiItemsByAddress = new();
    private readonly Dictionary<Item, RuntimeWorldAddress> _runtimeAiAddressByItem = new();
    private readonly Dictionary<int, RuntimeWorldAddress> _runtimeAiAddressByGuid = new();
    private readonly HashSet<Item> _runtimeAiEntities = new();
    private readonly HashSet<RuntimeWorldAddress> _runtimeAiDirtyAddresses = new();
    private readonly List<Item> _runtimeAiCleanupBuffer = new();
    private Transform _runtimeEntityRoot;
    private WorldRuntime _runtimeAiWorld;
    private long _runtimeAiWorldEpoch = long.MinValue;

    #region 实体识别与根节点

    /// <summary>判断 Item 是否属于新运行时实体 AI。</summary>
    public bool IsRuntimeAiEntity(Item item) => RuntimeAiEntityUtility.IsAiEntity(item);

    /// <summary>取得当前动态世界场景内的中性实体根节点。</summary>
    internal Transform GetRuntimeEntityRoot(Scene preferredScene)
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
            scene = preferredScene;
        if (!scene.IsValid() || !scene.isLoaded)
            return null;

        if (_runtimeEntityRoot != null && _runtimeEntityRoot.gameObject.scene == scene)
            return _runtimeEntityRoot;

        var rootObject = new GameObject(RuntimeEntityRootName);
        SceneManager.MoveGameObjectToScene(rootObject, scene);
        _runtimeEntityRoot = rootObject.transform;
        return _runtimeEntityRoot;
    }

    #endregion

    #region WorldAddress 索引

    /// <summary>在 Item 注册时完成一次 AI 分类，并缓存结果供高频 Tick 使用。</summary>
    private bool TryRegisterRuntimeAiEntity(Item item)
    {
        if (item == null || !EnsureRuntimeAiWorldScope() ||
            !RuntimeAiEntityUtility.IsAiEntity(item))
        {
            return false;
        }

        _runtimeAiEntities.Add(item);
        return true;
    }

    /// <summary>读取实体当前所属的新版区块地址。</summary>
    public bool TryGetRuntimeEntityAddress(Item item, out RuntimeWorldAddress address)
    {
        address = default;
        EnsureRuntimeAiWorldScope();
        return item != null && _runtimeAiAddressByItem.TryGetValue(item, out address);
    }

    /// <summary>按稳定 GUID 读取实体当前所属的新版区块地址。</summary>
    public bool TryGetRuntimeEntityAddress(int guid, out RuntimeWorldAddress address)
    {
        EnsureRuntimeAiWorldScope();
        return _runtimeAiAddressByGuid.TryGetValue(guid, out address);
    }

    /// <summary>复制指定地址的当前 AI 实体，调用方可安全遍历快照。</summary>
    public void CopyRuntimeAiItems(RuntimeWorldAddress address, List<Item> output)
    {
        if (output == null)
            throw new ArgumentNullException(nameof(output));

        output.Clear();
        EnsureRuntimeAiWorldScope();
        if (!_runtimeAiItemsByAddress.TryGetValue(address, out HashSet<Item> items))
            return;

        foreach (Item item in items)
        {
            if (item != null && item.itemData != null)
                output.Add(item);
        }
    }

    /// <summary>复制本次存档需要重写的地址，并包含所有仍有活体的地址。</summary>
    public void CopyRuntimeAiSaveAddresses(HashSet<RuntimeWorldAddress> output)
    {
        if (output == null)
            throw new ArgumentNullException(nameof(output));

        output.Clear();
        EnsureRuntimeAiWorldScope();
        output.UnionWith(_runtimeAiDirtyAddresses);
        foreach (RuntimeWorldAddress address in _runtimeAiItemsByAddress.Keys)
            output.Add(address);
    }

    /// <summary>确认这些地址已写入内存存档快照。</summary>
    public void AcknowledgeRuntimeAiSave(HashSet<RuntimeWorldAddress> addresses)
    {
        if (addresses == null)
            return;

        foreach (RuntimeWorldAddress address in addresses)
            _runtimeAiDirtyAddresses.Remove(address);
    }

    /// <summary>注册或刷新 AI 的 WorldAddress；即使目标区块尚未加载也会记录地址。</summary>
    private void RefreshRuntimeAiAddress(Item item, bool markDirty = true)
    {
        ChunkMgr chunkManager = ChunkMgr.ExistingInstance;
        if (item == null || item.itemData == null ||
            !EnsureRuntimeAiWorldScope() || chunkManager == null)
        {
            return;
        }

        // 分类只在 Item 注册/维护时执行；常规 Tick 不扫描非 AI 的子组件树。
        if (!_runtimeAiEntities.Contains(item))
            return;

        RuntimeWorldAddress next = chunkManager.ResolveWorldAddress(item.transform.position);
        if (_runtimeAiAddressByItem.TryGetValue(item, out RuntimeWorldAddress previous))
        {
            if (previous == next)
            {
                _runtimeAiAddressByGuid[item.itemData.Guid] = next;
                return;
            }

            RemoveRuntimeAiFromAddress(item, previous);
            if (markDirty)
                _runtimeAiDirtyAddresses.Add(previous);
        }

        if (!_runtimeAiItemsByAddress.TryGetValue(next, out HashSet<Item> items))
        {
            items = new HashSet<Item>();
            _runtimeAiItemsByAddress[next] = items;
        }

        items.Add(item);
        _runtimeAiAddressByItem[item] = next;
        _runtimeAiAddressByGuid[item.itemData.Guid] = next;
        if (markDirty)
            _runtimeAiDirtyAddresses.Add(next);
    }

    /// <summary>从新版实体索引移除 Item，并把原地址标记为需要保存。</summary>
    private void RemoveRuntimeAiEntity(Item item, bool markDirty = true)
    {
        if (ReferenceEquals(item, null))
            return;

        EnsureRuntimeAiWorldScope();
        _runtimeAiEntities.Remove(item);
        if (!_runtimeAiAddressByItem.TryGetValue(item, out RuntimeWorldAddress previous))
            return;

        RemoveRuntimeAiFromAddress(item, previous);
        if (item != null && item.itemData != null)
            _runtimeAiAddressByGuid.Remove(item.itemData.Guid);
        if (markDirty)
            _runtimeAiDirtyAddresses.Add(previous);
    }

    private void RemoveRuntimeAiFromAddress(Item item, RuntimeWorldAddress address)
    {
        _runtimeAiAddressByItem.Remove(item);
        if (!_runtimeAiItemsByAddress.TryGetValue(address, out HashSet<Item> items))
            return;

        items.Remove(item);
        if (items.Count == 0)
            _runtimeAiItemsByAddress.Remove(address);
    }

    /// <summary>清理场景卸载产生的伪空引用，并补齐仍有效 AI 的地址。</summary>
    private void CleanupRuntimeAiIndex()
    {
        EnsureRuntimeAiWorldScope();
        _runtimeAiCleanupBuffer.Clear();
        foreach (Item indexed in _runtimeAiAddressByItem.Keys)
        {
            if (indexed == null || indexed.itemData == null ||
                !WorldRunTimeItems.TryGetValue(indexed.itemData.Guid, out Item registered) ||
                registered != indexed)
            {
                _runtimeAiCleanupBuffer.Add(indexed);
            }
        }

        for (int i = 0; i < _runtimeAiCleanupBuffer.Count; i++)
            RemoveRuntimeAiEntity(_runtimeAiCleanupBuffer[i], markDirty: false);

        _runtimeAiAddressByGuid.Clear();

        foreach (Item item in RuntimeItems)
        {
            if (item != null && RuntimeAiEntityUtility.IsAiEntity(item))
            {
                _runtimeAiEntities.Add(item);
                RefreshRuntimeAiAddress(item, markDirty: false);
            }
        }
        _runtimeAiCleanupBuffer.Clear();
    }

    /// <summary>世界对象或纪元变化时丢弃旧场景索引，禁止跨世界误用同一地址。</summary>
    private bool EnsureRuntimeAiWorldScope()
    {
        WorldRuntime world = ChunkMgr.ExistingInstance?.WorldRuntime;
        long epoch = world?.Epoch ?? long.MinValue;
        if (ReferenceEquals(world, _runtimeAiWorld) && epoch == _runtimeAiWorldEpoch)
            return world != null;

        _runtimeAiItemsByAddress.Clear();
        _runtimeAiAddressByItem.Clear();
        _runtimeAiAddressByGuid.Clear();
        _runtimeAiEntities.Clear();
        _runtimeAiDirtyAddresses.Clear();
        _runtimeEntityRoot = null;
        _runtimeAiWorld = world;
        _runtimeAiWorldEpoch = epoch;
        return world != null;
    }

    #endregion

    #region ItemMgr 生命周期桥接

    /// <summary>同时刷新感知空间哈希与新版 AI 地址索引。</summary>
    private void RefreshRuntimeItemIndexes(Item item)
    {
        RefreshItemSpatialIndex(item);
        RefreshRuntimeAiAddress(item);
    }

    /// <summary>Item 被 Unity 场景卸载直接销毁时，从运行时注册表注销。</summary>
    internal void NotifyRuntimeItemDestroyed(Item item)
    {
        if (item?.itemData == null ||
            !WorldRunTimeItems.TryGetValue(item.itemData.Guid, out Item registered) ||
            registered != item)
        {
            return;
        }

        RuntimeItemDespawning?.Invoke(item);
        UnregisterRuntimeItem(item);
    }

    #endregion
}
