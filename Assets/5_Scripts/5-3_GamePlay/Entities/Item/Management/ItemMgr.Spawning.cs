using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using UnityEngine;

public partial class ItemMgr
{
    #region Runtime Registry

    private readonly ItemRuntimeRegistry _runtimeRegistry = new();
    private readonly HashSet<Item> _runtimeDespawning = new(ItemReferenceComparer.Instance);

    private IReadOnlyList<Item> RuntimeItems => _runtimeRegistry.Items;

    internal bool IsRuntimeItemRegistered(Item item) => _runtimeRegistry.Contains(item);

    #endregion

    #region Item对象池

    [Header("Item对象池")]
    [SerializeField, Min(1)] private int maxPoolSizePerItem = 24;
    [SerializeField, Min(1)] private int maxTotalPooledItems = 256;

    [ShowInInspector]
    private Dictionary<string, Queue<Item>> ItemPools => _itemObjectPool.Pools;

    private readonly ItemObjectPool _itemObjectPool = new();

    public int TotalPooledItemCount => _itemObjectPool.TotalCount;

    /// <summary>由主菜单资源重载入口调用，释放所有依赖旧目录的缓存实体。</summary>
    internal void ClearResourcePools() => _itemObjectPool.Clear();

    #endregion

    #region Instantiate

    public Item InstantiateItem(ItemData itemData, Vector3 position = default, Quaternion rotation = default, Vector3 scale = default, GameObject parent = null)
        => InstantiateItemInternal(itemData, position, rotation, scale, parent, null);

    /// <summary>手持物直接绑定真实槽位数据，注册前建立持有者与手持标记，禁止先生成世界物再换 GUID。</summary>
    public Item InstantiateHeldItem(ItemData data, Item owner, Transform attachment)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (owner == null) throw new ArgumentNullException(nameof(owner));
        if (attachment == null) throw new ArgumentNullException(nameof(attachment));
        bool previousInHand = data.inHand;
        data.inHand = true;
        try
        {
            return InstantiateItemInternal(data, attachment.position, Quaternion.identity,
                Vector3.one, attachment.gameObject, owner);
        }
        catch
        {
            data.inHand = previousInHand;
            throw;
        }
    }

    // 所有实例化入口共用外壳池、定义装配和完整注册链。
    private Item InstantiateItemInternal(ItemData itemData, Vector3 position, Quaternion rotation,
        Vector3 scale, GameObject parent, Item owner)
    {
        if (itemData == null)
        {
            throw new ArgumentNullException(nameof(itemData));
        }
        if (string.IsNullOrWhiteSpace(itemData.IDName))
        {
            throw new ArgumentException("ItemData.IDName 不能为空", nameof(itemData));
        }
        if (itemData.SharedConfiguration == null)
            itemData = ItemDefinitionRuntime.RebasePersistedData(GameRes.Instance, itemData);
        if (itemData.Guid != 0 && _runtimeItemReplacements.ContainsKey(itemData.Guid))
            throw new InvalidOperationException($"Item GUID {itemData.Guid} 正在交接运行时实例，不能同时生成。");
        _runtimeRegistry.ValidateIdentityAvailable(itemData);
        // 落地设施只能由机器世界持有，禁止调用旧 Item 入口形成第二个权威实例。
        if (!itemData.inHand && MachineWorld.OwnsWorldItem(itemData))
            throw new InvalidOperationException($"落地设施 {itemData.IDName} 请使用 MachineWorld.Place/SpawnGenerated/RestoreMachine。");
        // 只有显式归属 ECS 的物种才禁止创建 GameObject，普通 Actor 沿用完整 Item 生命周期。
        if (AiRuntimeBackendService.UsesEntities(itemData.IDName))
            throw new InvalidOperationException($"生物 {itemData.IDName} 请使用 ECS 生成入口。");

        if (rotation == default) rotation = Quaternion.identity;
        if (scale == default || scale == Vector3.zero) scale = Vector3.one;

        GameObject itemObj = AcquireItemObject(itemData.IDName);
        Item item = itemObj.GetComponent<Item>();
        ulong registrationToken = 0;
        try
        {
            item.PrepareForPoolReuse();
            item.BindData(itemData);
            item.Owner = owner;
            if (GameRes.Instance.TryGetItemDefinition(itemData.IDName, out RuntimeItemDefinition definition))
                ItemDefinitionRuntime.ConfigureInstance(GameRes.Instance, definition, item, itemData);
            // JSON 模块装配完成后才固定池内层级；首次外壳结构并不是可复用的最终结构。
            item.PoolMarker.CaptureBaseline();
            itemObj.name = itemData.IDName;
            Vector3 logicalPosition = itemData.inHand
                ? position
                : WorldLocalPresentation.ToLogical(position);
            itemObj.transform.position = itemData.inHand
                ? position
                : WorldLocalPresentation.ProjectPosition(logicalPosition);
            itemObj.transform.rotation = rotation;
            itemObj.transform.localScale = scale;
            itemObj.SetActive(true);

            RegisterRuntimeItem(item, itemData.IDName, out registrationToken);
            ItemWorldPlacement.Attach(item, itemObj, logicalPosition, parent);
            EnsureSpawnRegistrationCurrent(item, itemData, registrationToken);
            RuntimeItemInstantiated?.Invoke(item);
            EnsureSpawnRegistrationCurrent(item, itemData, registrationToken);
            WorldItemWaterSystem.ScheduleSpawnCheck(item);
            return item;
        }
        catch
        {
            CleanupFailedItemSpawn(item, itemObj, itemData, registrationToken);
            throw;
        }
    }

    private void EnsureSpawnRegistrationCurrent(Item item, ItemData data, ulong token)
    {
        if (item == null || item.DestructionHandled || item.IsInPool ||
            !ReferenceEquals(item.itemData, data) || !_runtimeRegistry.IsRegistrationCurrent(item, token) ||
            !_runtimeRegistry.TryGetRegisteredIdentity(item, out int guid, out string definitionId) ||
            data.Guid != guid || !string.Equals(data.IDName, definitionId, StringComparison.Ordinal))
            throw new InvalidOperationException($"Item {data.IDName}/{data.Guid} 在生成回调中已结束或切换运行实例。");
    }

    private void RestoreRegisteredItemIdentityForCleanup(Item item, ulong token)
    {
        if (item != null && item.itemData != null && _runtimeRegistry.IsRegistrationCurrent(item, token) &&
            _runtimeRegistry.TryGetRegisteredIdentity(item, out int guid, out string definitionId))
        {
            item.itemData.Guid = guid;
            item.itemData.IDName = definitionId;
        }
    }

    private void CleanupFailedItemSpawn(Item item, GameObject itemObject, ItemData data, ulong token,
        uint? runtimeGeneration = null)
    {
        if (item == null)
        {
            if (itemObject != null) Destroy(itemObject);
            return;
        }
        // 回调可能已将旧实例回池并复用，失败清理不能销毁后来接管该外壳的新注册。
        if (item.IsInPool || (token != 0 && _runtimeRegistry.Contains(item) &&
            !_runtimeRegistry.IsRegistrationCurrent(item, token))) return;
        if (token != 0 && !ReferenceEquals(item.itemData, data)) return;
        if (runtimeGeneration.HasValue && item.RuntimeGeneration != runtimeGeneration.Value) return;
        RestoreRegisteredItemIdentityForCleanup(item, token);
        if (!_runtimeDespawning.Add(item)) return;
        try
        {
            try
            {
                if (_runtimeRegistry.Contains(item))
                    RuntimeItemDespawning?.Invoke(item);
            }
            catch (Exception exception) { Debug.LogException(exception, item); }
            try { WorldItemWaterSystem.CancelSpawnCheck(item); }
            catch (Exception exception) { Debug.LogException(exception, item); }
            try { WorldItemWaterSystem.ClearRuntimeState(item); }
            catch (Exception exception) { Debug.LogException(exception, item); }
            try { item.GetComponentInParent<Chunk>()?.RemoveItem(item); }
            catch (Exception exception) { Debug.LogException(exception, item); }
            try { UnregisterRuntimeItem(item); }
            catch (Exception exception) { Debug.LogException(exception, item); }
            try { item.PrepareForDespawn(saveData: false); }
            catch (Exception exception) { Debug.LogException(exception, item); }
            if (itemObject != null) Destroy(itemObject);
        }
        finally { _runtimeDespawning.Remove(item); }
    }

    public void DespawnItem(Item item, bool saveData = true, bool detachFromChunk = true)
    {
        if (item == null)
        {
            throw new ArgumentNullException(nameof(item));
        }

        if (item.DestructionHandled || !_runtimeDespawning.Add(item))
            return;

        Exception failure = null;
        try
        {
            try { WorldItemWaterSystem.CancelSpawnCheck(item); }
            catch (Exception exception) { CaptureDespawnFailure(ref failure, exception); }
            try { MachineWorld.BeforeDespawn(item); }
            catch (Exception exception) { CaptureDespawnFailure(ref failure, exception); }
            try { RuntimeItemDespawning?.Invoke(item); }
            catch (Exception exception) { CaptureDespawnFailure(ref failure, exception); }
            try { WorldItemWaterSystem.ClearRuntimeState(item); }
            catch (Exception exception) { CaptureDespawnFailure(ref failure, exception); }
            try
            {
                if (detachFromChunk)
                    item.GetComponentInParent<Chunk>()?.RemoveItem(item);
            }
            catch (Exception exception) { CaptureDespawnFailure(ref failure, exception); }
            try { UnregisterRuntimeItem(item); }
            catch (Exception exception) { CaptureDespawnFailure(ref failure, exception); }
            try { item.PrepareForDespawn(saveData); }
            catch (Exception exception) { CaptureDespawnFailure(ref failure, exception); }

            bool returnedToPool = false;
            try
            {
                if (failure == null && item != null)
                    returnedToPool = TryReturnItemToPool(item);
            }
            catch (Exception exception) { CaptureDespawnFailure(ref failure, exception); }
            if (!returnedToPool && item != null)
                Destroy(item.gameObject);
        }
        finally
        {
            _runtimeDespawning.Remove(item);
        }
        if (failure != null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void CaptureDespawnFailure(ref Exception failure, Exception exception) =>
        failure = failure == null ? exception : new AggregateException("Item 回收阶段出现多个异常。", failure, exception);

    public void DestroyItem(Item item)
    {
        DespawnItem(item);
    }

    // 通过名称实例化：只保留一个（用可选参数覆盖绝大多数用法）
    public Item InstantiateItem(string itemName, Vector3 position = default, Quaternion rotation = default, Vector3 scale = default, GameObject parent = null)
    {
        ItemData templateData = GameRes.Instance.CreateItemData(itemName);
        return InstantiateItem(templateData, position, rotation, scale, parent);
    }

    /// <summary>
    /// 噪声地图使用的确定性实例化入口。相同世界种子与格子会在所有联机端得到相同 Guid。
    /// </summary>
    public Item InstantiateItemDeterministic(
        string itemName,
        int deterministicGuid,
        Vector3 position = default,
        Quaternion rotation = default,
        Vector3 scale = default,
        GameObject parent = null)
    {
        ItemData templateData = GameRes.Instance.CreateItemData(itemName);
        templateData.Guid = deterministicGuid == 0 ? 1 : deterministicGuid;
        return InstantiateItem(templateData, position, rotation, scale, parent);
    }

    /// <summary>
    /// AI-Context: 仅供权威网络快照补建世界 Item；调用方必须先校验 ID、GUID 与位置。
    /// </summary>
    public Item InstantiateNetworkItem(
        string itemName,
        int authoritativeGuid,
        Vector3 position,
        Quaternion rotation,
        Vector3 scale)
    {
        if (authoritativeGuid == 0)
            throw new ArgumentOutOfRangeException(nameof(authoritativeGuid), "网络 Item GUID 不能为 0");

        ItemData data = GameRes.Instance.CreateItemData(itemName);
        data.Guid = authoritativeGuid;
        return InstantiateItem(data, position, rotation, scale);
    }

    // 通过ItemData的transform信息实例化（保留此重载：项目内多处在用）
    public Item InstantiateItem(ItemData itemData, GameObject parent)
        => InstantiateItem(itemData, itemData.transform.position, itemData.transform.rotation, itemData.transform.scale, parent);

    // 生成GUID的辅助方法
    public int GenerateGuid()
    {
        int guid;
        do { guid = Guid.NewGuid().GetHashCode(); }
        while (guid == 0 || WorldRunTimeItems.ContainsKey(guid) || _runtimeItemReplacements.ContainsKey(guid));
        return guid;
    }

    private ulong RegisterRuntimeItem(Item item, string context)
        => RegisterRuntimeItem(item, context, out _);

    private ulong RegisterRuntimeItem(Item item, string context, out ulong registrationToken,
        RuntimeItemReplacement authorizedTransfer = null)
    {
        registrationToken = 0;
        if (item == null)
        {
            throw new ArgumentNullException(nameof(item), $"运行时注册缺少 Item，context={context}。");
        }

        if (item.itemData == null)
        {
            throw new InvalidOperationException($"物品缺少 ItemData：{item.name}，context={context}。");
        }

        if (!_runtimeRegistry.Contains(item) && _runtimeItemReplacements.TryGetValue(item.itemData.Guid, out var reserved) &&
            (!ReferenceEquals(reserved, authorizedTransfer) ||
             (!ReferenceEquals(item, reserved.Original) && !ReferenceEquals(item, reserved.Candidate))))
            throw new InvalidOperationException($"Item GUID {item.itemData.Guid} 已被运行时替换事务保留。");

        if (!_runtimeRegistry.Register(item, GenerateGuid))
        {
            _tickScheduler.NotifyChanged(item);
            registrationToken = _runtimeRegistry.GetRegistrationToken(item);
            return registrationToken;
        }

        registrationToken = _runtimeRegistry.GetRegistrationToken(item);
        ItemData registeredData = item.itemData;
        try
        {
            RefreshItemSpatialIndex(item);
            RefreshPerceptionTarget(item);
            _tickScheduler.Register(item);
            if (TryRegisterRuntimeAiEntity(item))
                ItemWorldPlacement.AttachRuntimeAi(item, item.gameObject);

            // 实体注册完成后创建独立阴影；阴影不进入 Item 或 RuntimeEntities 层级。
            ActorShadowManager.GetInstance()?.RegisterActor(item);
            if (item is Map mapItem)
                _cachedMap = mapItem;
            RuntimeItemRegistered?.Invoke(item);
            EnsureSpawnRegistrationCurrent(item, registeredData, registrationToken);
            return registrationToken;
        }
        catch
        {
            if (_runtimeRegistry.IsRegistrationCurrent(item, registrationToken))
            {
                RestoreRegisteredItemIdentityForCleanup(item, registrationToken);
                UnregisterRuntimeItem(item);
            }
            throw;
        }
    }

    public void InjectRuntimeItem(Item item, string context = null)
    {
        if (string.IsNullOrEmpty(context))
        {
            context = item?.itemData != null ? item.itemData.IDName : item?.name;
        }
        RegisterRuntimeItem(item, context);
    }

    public void NotifyRuntimeItemMoved(Item item)
    {
        if (item == null || item.itemData == null ||
            !WorldRunTimeItems.TryGetValue(item.itemData.Guid, out Item registered) || registered != item)
        {
            return;
        }

        RefreshRuntimeItemIndexes(item);
        _tickScheduler.NotifyMoved(item);
        RuntimeItemMoved?.Invoke(item);
    }

    /// <summary>
    /// 将网络远程手持物从本地权威 Item 循环中移除，保留 GameObject 仅作视觉展示。
    /// </summary>
    public void MarkAsRemoteVisualOnly(Item item)
    {
        if (!_runtimeRegistry.Contains(item) || !_runtimeDespawning.Add(item)) return;
        try
        {
            try { RuntimeItemDespawning?.Invoke(item); }
            finally { UnregisterRuntimeItem(item); }
        }
        finally { _runtimeDespawning.Remove(item); }
    }

    private void UnregisterRuntimeItem(Item item)
    {
        if (!_runtimeRegistry.TryGetRegisteredGuid(item, out int registeredGuid) || !_runtimeRegistry.Remove(item))
            return;

        _tickScheduler.Remove(item);
        try
        {
            RemoveRuntimeAiEntity(item, registeredGuid: registeredGuid);
        }
        finally
        {
            if (item is Map)
                _cachedMap = null;
            RemoveItemFromSpatialIndex(item);
            _perceptionTargets.Remove(item);
            RuntimeItemUnregistered?.Invoke(item);
        }
    }

    private GameObject SpawnItemObject(string itemId)
    {
        GameObject obj = GameRes.Instance.InstantiatePrefab(itemId);
        if (obj == null) throw new InvalidOperationException($"InstantiatePrefab 失败: {itemId}");
        return obj;
    }

    private GameObject AcquireItemObject(string itemId)
    {
        return _itemObjectPool.Acquire(itemId, SpawnItemObject);
    }

    private bool TryReturnItemToPool(Item item)
    {
        return _itemObjectPool.TryReturn(
            item,
            transform,
            maxPoolSizePerItem,
            maxTotalPooledItems);
    }

    #endregion

    #region Runtime Registry API

    // 分组是完整注册的派生索引，禁止只写入其中一个集合。
    public void AddToGroup(Item item)
    {
        if (item == null)
        {
            Debug.LogError("AddToGroup: item为空");
            return;
        }

        if (item.itemData == null)
        {
            Debug.LogError($"AddToGroup: itemData为空, item={item.name}", item);
            return;
        }

        string key = item.itemData.IDName;
        if (string.IsNullOrWhiteSpace(key))
        {
            Debug.LogError($"AddToGroup: IDName为空, item={item.name}", item);
            return;
        }

        RegisterRuntimeItem(item, key);
    }

    // ✅ 获取同类物品列表
    public IReadOnlyList<Item> GetItemsByNameID(string nameId)
    {
        if (RuntimeItemsGroup.TryGetValue(nameId, out var list))
        {
            return list;
        }
        return Array.Empty<Item>();
    }

    // 查找运行时物品
    [Button]
    public Item GetItemByGuid(int guid)
    {
        if (WorldRunTimeItems.TryGetValue(guid, out var item))
            return item;
        return null;
    }

    #endregion
}
