using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.SceneManagement;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

/// <summary>
/// AI 实体识别工具。运行时识别 IAIActor、生态物种及旧 Prefab 的兼容标记模块，
/// 存档数据识别 AI 模块 ID 与怪物管理器物种目录，用于让实体生命周期脱离旧 Chunk/Map 对象。
/// </summary>
internal static class RuntimeAiEntityUtility
{
    #region AI 实体身份识别

    private const string GenericAiModuleId = "AI";
    private const string AiModulePrefix = "AI_";
    private const string LegacyAiChunkMarkerId = "ChunkAssigner";

    /// <summary>判断当前 Item 是否承载可运行的实体 AI。</summary>
    public static bool IsAiEntity(Item item)
    {
        if (item == null || item is Player || item is Map)
            return false;

        if (MonsterManager.IsRegisteredSpeciesId(item.itemData?.IDName) ||
            item.GetComponentInChildren<Mod_ItemChunkAssigner>(true) != null)
        {
            return true;
        }

        // 每次实时读取能力，临时列表归还池，避免移动检查反复创建整份组件数组。
        using var scope = ListPool<MonoBehaviour>.Get(out var behaviours);
        item.GetComponentsInChildren(true, behaviours);
        for (int i = 0; i < behaviours.Count; i++)
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

        // 存档采集可能发生在生态目录注销之后；Actor 定义仍能识别休眠快照。
        return MonsterManager.IsRegisteredSpeciesId(data.IDName) ||
               (GameRes.Instance != null &&
                GameRes.Instance.TryGetItemDefinition(data.IDName, out RuntimeItemDefinition definition) &&
                definition.IsActor);
    }

    private static bool IsAiModuleId(string value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               (string.Equals(value, GenericAiModuleId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, LegacyAiChunkMarkerId, StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith(AiModulePrefix, StringComparison.OrdinalIgnoreCase));
    }

    #endregion
}

#region 运行时 AI 持久化契约
/// <summary>由宿主模块声明运行时 AI 是否拥有独立存档，供临时巢群等所有权模型使用。</summary>
public interface IRuntimeAiPersistencePolicy
{
    bool PersistRuntimeAi { get; }
}
#endregion

/// <summary>
/// ItemMgr 的 AI 实体区块索引。索引键为纯 WorldModel.WorldAddress，
/// 实体统一挂在场景级 RuntimeEntities 根节点，不再成为 ChunkView 或旧 Chunk 的子对象。
/// </summary>
public partial class ItemMgr
{
    private const string RuntimeEntityRootName = "RuntimeEntities";

    private sealed class RuntimeAiWorldState
    {
        public readonly Dictionary<RuntimeWorldAddress, HashSet<Item>> ByAddress = new();
        public readonly Dictionary<Item, RuntimeWorldAddress> ByItem = new(ItemReferenceComparer.Instance);
        public readonly Dictionary<int, RuntimeWorldAddress> ByGuid = new();
        public readonly HashSet<Item> Entities = new(ItemReferenceComparer.Instance);
        public readonly HashSet<RuntimeWorldAddress> Dirty = new();
        public readonly List<Item> Cleanup = new();
        public Transform Root;
        public WorldRuntime World;
        public long Epoch = long.MinValue;
    }
    private sealed class RuntimeAiOwnership
    {
        public string WorldKey;
        public int Guid;
    }
    private readonly Dictionary<string, RuntimeAiWorldState> runtimeAiWorldStates = new(StringComparer.Ordinal);
    private readonly Dictionary<Item, RuntimeAiOwnership> runtimeAiOwnership = new(ItemReferenceComparer.Instance);
    private readonly RuntimeAiWorldState runtimeAiUnboundWorld = new();
    private RuntimeAiWorldState GetRuntimeAiWorldState(string key)
    {
        if (!runtimeAiWorldStates.TryGetValue(key, out RuntimeAiWorldState value))
            runtimeAiWorldStates.Add(key, value = new RuntimeAiWorldState());
        return value;
    }
    private RuntimeAiWorldState AiWorld
    {
        get
        {
            string key = GameplayWorldRegistry.ExecutionWorldKey;
            return string.IsNullOrEmpty(key) ? runtimeAiUnboundWorld : GetRuntimeAiWorldState(key);
        }
    }
    private Dictionary<RuntimeWorldAddress, HashSet<Item>> _runtimeAiItemsByAddress => AiWorld.ByAddress;
    private Dictionary<Item, RuntimeWorldAddress> _runtimeAiAddressByItem => AiWorld.ByItem;
    private Dictionary<int, RuntimeWorldAddress> _runtimeAiAddressByGuid => AiWorld.ByGuid;
    private HashSet<Item> _runtimeAiEntities => AiWorld.Entities;
    private HashSet<RuntimeWorldAddress> _runtimeAiDirtyAddresses => AiWorld.Dirty;
    private List<Item> _runtimeAiCleanupBuffer => AiWorld.Cleanup;
    private Transform _runtimeEntityRoot { get => AiWorld.Root; set => AiWorld.Root = value; }
    private WorldRuntime _runtimeAiWorld { get => AiWorld.World; set => AiWorld.World = value; }
    private long _runtimeAiWorldEpoch { get => AiWorld.Epoch; set => AiWorld.Epoch = value; }

    /// <summary>正式地址索引已完成跨区块更新；只通知实际变化，不按帧发布。</summary>
    public event Action<Item> RuntimeAiAddressChanged;

    #region 实体识别与根节点

    /// <summary>判断 Item 是否属于新运行时实体 AI。</summary>
    public bool IsRuntimeAiEntity(Item item) => RuntimeAiEntityUtility.IsAiEntity(item);

    /// <summary>取得当前动态世界场景内的中性实体根节点。</summary>
    internal Transform GetRuntimeEntityRoot(Scene preferredScene)
    {
        Scene scene = preferredScene;
        if (!scene.IsValid() || !scene.isLoaded)
            scene = GameplayWorldRegistry.CurrentScene;
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
        if (item == null || item.itemData == null || !RuntimeAiEntityUtility.IsAiEntity(item)) return false;
        BindRuntimeAiLogicalOwnership(item, GameplayWorldRegistry.GetItemWorldKey(item), item.itemData.Guid, markDirty: true);
        RefreshRuntimeAiAddress(item);
        return runtimeAiOwnership.TryGetValue(item, out RuntimeAiOwnership owner) &&
               !string.IsNullOrEmpty(owner.WorldKey) &&
               runtimeAiWorldStates.TryGetValue(owner.WorldKey, out RuntimeAiWorldState state) &&
               state.ByItem.ContainsKey(item);
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
            if (item != null && item.itemData != null && ShouldPersistRuntimeAi(item))
                output.Add(item);
        }
    }

    /// <summary>具有宿主所有权的实体由宿主恢复，不再另存一份自由 AI。</summary>
    private static bool ShouldPersistRuntimeAi(Item item)
    {
        MonoBehaviour[] behaviours = item.GetComponentsInChildren<MonoBehaviour>(true);
        for (int index = 0; index < behaviours.Length; index++)
        {
            if (behaviours[index] is IRuntimeAiPersistencePolicy policy && !policy.PersistRuntimeAi)
                return false;
        }
        return true;
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
        // 仅处理注册时已识别的 AI；逻辑世界迁移先移除旧桶，未就绪的目标保留待定位。
        if (item == null || item.itemData == null || !runtimeAiOwnership.TryGetValue(item, out RuntimeAiOwnership owner)) return;
        string targetWorld = GameplayWorldRegistry.GetItemWorldKey(item);
        bool movedWorld = BindRuntimeAiLogicalOwnership(item, targetWorld, owner.Guid, markDirty: true);
        if (!TryUseRuntimeAiWorld(targetWorld, out IDisposable scope)) return;
        using (scope)
        {
            ChunkMgr chunkManager = ChunkMgr.ExistingInstance;
            if (!EnsureRuntimeAiWorldScope() || chunkManager == null) return;
            _runtimeAiEntities.Add(item);
            RuntimeWorldAddress next = chunkManager.ResolveWorldAddress(item.transform.position);
            if (_runtimeAiAddressByItem.TryGetValue(item, out RuntimeWorldAddress previous))
            {
                if (previous == next)
                {
                    _runtimeAiAddressByGuid[owner.Guid] = next;
                    return;
                }

                RemoveRuntimeAiFromAddress(AiWorld, item, previous);
                if (markDirty || movedWorld) _runtimeAiDirtyAddresses.Add(previous);
            }

            if (!_runtimeAiItemsByAddress.TryGetValue(next, out HashSet<Item> items))
            {
                items = new HashSet<Item>(ItemReferenceComparer.Instance);
                _runtimeAiItemsByAddress[next] = items;
            }

            items.Add(item);
            _runtimeAiAddressByItem[item] = next;
            _runtimeAiAddressByGuid[owner.Guid] = next;
            if (markDirty || movedWorld) _runtimeAiDirtyAddresses.Add(next);
            RuntimeAiAddressChanged?.Invoke(item);
        }
    }

    /// <summary>从新版实体索引移除 Item，并把原地址标记为需要保存。</summary>
    private void RemoveRuntimeAiEntity(Item item, bool markDirty = true, int? registeredGuid = null)
    {
        if (ReferenceEquals(item, null) || !runtimeAiOwnership.Remove(item, out RuntimeAiOwnership owner)) return;
        RemoveRuntimeAiFromOwnedWorld(item, owner.WorldKey, registeredGuid ?? owner.Guid, markDirty);
    }

    private static void RemoveRuntimeAiFromAddress(RuntimeAiWorldState world, Item item, RuntimeWorldAddress address)
    {
        world.ByItem.Remove(item);
        if (!world.ByAddress.TryGetValue(address, out HashSet<Item> items))
            return;

        items.Remove(item);
        if (items.Count == 0)
            world.ByAddress.Remove(address);
    }

    // 旧索引直接按登记时的所有者移除，不创建已卸载世界或借用当前场景。
    private void RemoveRuntimeAiFromOwnedWorld(Item item, string worldKey, int guid, bool markDirty)
    {
        if (string.IsNullOrEmpty(worldKey) || !runtimeAiWorldStates.TryGetValue(worldKey, out RuntimeAiWorldState world)) return;
        world.Entities.Remove(item);
        world.ByGuid.Remove(guid);
        if (!world.ByItem.TryGetValue(item, out RuntimeWorldAddress previous)) return;
        RemoveRuntimeAiFromAddress(world, item, previous);
        if (markDirty) world.Dirty.Add(previous);
    }

    private bool BindRuntimeAiLogicalOwnership(Item item, string worldKey, int guid, bool markDirty)
    {
        if (string.IsNullOrWhiteSpace(worldKey)) worldKey = null;
        if (!runtimeAiOwnership.TryGetValue(item, out RuntimeAiOwnership owner))
            runtimeAiOwnership.Add(item, owner = new RuntimeAiOwnership { WorldKey = worldKey, Guid = guid });
        else if (!string.Equals(owner.WorldKey, worldKey, StringComparison.Ordinal) || owner.Guid != guid)
        {
            RemoveRuntimeAiFromOwnedWorld(item, owner.WorldKey, owner.Guid, markDirty);
            owner.WorldKey = worldKey;
            owner.Guid = guid;
            if (worldKey != null) GetRuntimeAiWorldState(worldKey).Entities.Add(item);
            return true;
        }
        if (worldKey != null) GetRuntimeAiWorldState(worldKey).Entities.Add(item);
        return false;
    }

    private static bool TryUseRuntimeAiWorld(string worldKey, out IDisposable scope)
    {
        scope = null;
        if (string.IsNullOrEmpty(worldKey)) return false;
        if (string.Equals(worldKey, GameplayWorldRegistry.ExecutionWorldKey, StringComparison.Ordinal))
            return !GameplayWorldRegistry.HasExecutionContext || GameplayWorldRegistry.ExecutionChunks != null;
        if (!GameplayWorldRegistry.TryGet(worldKey, out GameplayWorldRuntime world) || world.Chunks == null) return false;
        scope = GameplayWorldRegistry.UseWorld(worldKey);
        return true;
    }

    /// <summary>清理场景卸载产生的伪空引用，并补齐仍有效 AI 的地址。</summary>
    private void CleanupRuntimeAiIndex()
    {
        // 场景卸载可能先清理注册表，反向索引也要跨世界清理伪空对象。
        using (ListPool<Item>.Get(out var staleOwners))
        {
            foreach (Item indexed in runtimeAiOwnership.Keys)
                if (indexed == null || indexed.itemData == null ||
                    !WorldRunTimeItems.TryGetValue(indexed.itemData.Guid, out Item registered) ||
                    !ReferenceEquals(registered, indexed))
                    staleOwners.Add(indexed);
            foreach (Item indexed in staleOwners)
                RemoveRuntimeAiEntity(indexed, markDirty: false);
        }

        EnsureRuntimeAiWorldScope();
        _runtimeAiCleanupBuffer.Clear();
        foreach (Item indexed in _runtimeAiAddressByItem.Keys)
        {
            if (indexed == null || indexed.itemData == null ||
                !WorldRunTimeItems.TryGetValue(indexed.itemData.Guid, out Item registered) ||
                registered != indexed || GameplayWorldRegistry.GetItemWorldKey(indexed) != GameplayWorldRegistry.ExecutionWorldKey)
            {
                _runtimeAiCleanupBuffer.Add(indexed);
            }
        }

        for (int i = 0; i < _runtimeAiCleanupBuffer.Count; i++)
        {
            Item item = _runtimeAiCleanupBuffer[i];
            if (item != null && item.itemData != null &&
                WorldRunTimeItems.TryGetValue(item.itemData.Guid, out Item registered) && registered == item)
                RefreshRuntimeAiAddress(item, markDirty: false);
            else RemoveRuntimeAiEntity(item, markDirty: false);
        }

        _runtimeAiAddressByGuid.Clear();

        string currentWorld = GameplayWorldRegistry.ExecutionWorldKey;
        foreach (Item item in RuntimeItems)
        {
            if (!string.IsNullOrEmpty(currentWorld) && item != null && item.itemData != null &&
                GameplayWorldRegistry.GetItemWorldKey(item) == currentWorld && RuntimeAiEntityUtility.IsAiEntity(item))
            {
                BindRuntimeAiLogicalOwnership(item, GameplayWorldRegistry.GetItemWorldKey(item), item.itemData.Guid, markDirty: true);
                RefreshRuntimeAiAddress(item, markDirty: false);
            }
        }
        _runtimeAiCleanupBuffer.Clear();
    }

    /// <summary>世界对象或纪元变化时丢弃旧场景索引，禁止跨世界误用同一地址。</summary>
    private bool EnsureRuntimeAiWorldScope()
    {
        string worldKey = GameplayWorldRegistry.ExecutionWorldKey;
        WorldRuntime world = ChunkMgr.ExistingInstance?.WorldRuntime;
        if (world != null && !string.Equals(world.WorldId, worldKey, StringComparison.Ordinal)) world = null;
        long epoch = world?.Epoch ?? long.MinValue;
        if (ReferenceEquals(world, _runtimeAiWorld) && epoch == _runtimeAiWorldEpoch)
            return world != null;

        _runtimeAiItemsByAddress.Clear();
        _runtimeAiAddressByItem.Clear();
        _runtimeAiAddressByGuid.Clear();
        _runtimeAiEntities.Clear();
        foreach (var entry in runtimeAiOwnership)
            if (entry.Key != null && string.Equals(entry.Value.WorldKey, worldKey, StringComparison.Ordinal))
                _runtimeAiEntities.Add(entry.Key);
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
        if (!_runtimeRegistry.Contains(item) || !_runtimeDespawning.Add(item))
        {
            return;
        }

        try
        {
            try { RuntimeItemDespawning?.Invoke(item); }
            finally { UnregisterRuntimeItem(item); }
        }
        finally { _runtimeDespawning.Remove(item); }
    }

    #endregion
}
