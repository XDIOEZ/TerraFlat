using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using UnityEngine;

public partial class ItemMgr
{
    #region Runtime Registry

    private readonly ItemRuntimeRegistry _runtimeRegistry = new();

    private List<Item> RuntimeItems => _runtimeRegistry.Items;

    #endregion

    #region Item对象池

    [Header("Item对象池")]
    [SerializeField, Min(1)] private int maxPoolSizePerItem = 24;
    [SerializeField, Min(1)] private int maxTotalPooledItems = 256;

    [ShowInInspector]
    private Dictionary<string, Queue<Item>> ItemPools => _itemObjectPool.Pools;

    private readonly ItemObjectPool _itemObjectPool = new();

    public int TotalPooledItemCount => _itemObjectPool.TotalCount;

    #endregion

    #region Instantiate

    // 核心实例化方法：统一所有重载走这里
    public Item InstantiateItem(ItemData itemData, Vector3 position = default, Quaternion rotation = default, Vector3 scale = default, GameObject parent = null)
    {
        if (itemData == null)
        {
            throw new ArgumentNullException(nameof(itemData));
        }
        if (string.IsNullOrWhiteSpace(itemData.IDName))
        {
            throw new ArgumentException("ItemData.IDName 不能为空", nameof(itemData));
        }

        if (rotation == default) rotation = Quaternion.identity;
        if (scale == default || scale == Vector3.zero) scale = Vector3.one;

        GameObject itemObj = AcquireItemObject(itemData.IDName);
        Item item = itemObj.GetComponent<Item>();
        if (item == null)
        {
            Destroy(itemObj);
            throw new InvalidOperationException($"Prefab 缺少 Item 组件: {itemData.IDName}");
        }

        item.BindData(itemData);
        item.PrepareForPoolReuse();
        if (GameRes.Instance.TryGetItemDefinition(itemData.IDName, out RuntimeItemDefinition definition))
            ItemDefinitionRuntime.ConfigureInstance(GameRes.Instance, definition, item, itemData);
        itemObj.name = itemData.IDName;
        itemObj.transform.position = position;
        itemObj.transform.rotation = rotation;
        itemObj.transform.localScale = scale;
        itemObj.SetActive(true);

        RegisterRuntimeItem(item, itemData.IDName);
        ItemWorldPlacement.Attach(item, itemObj, position, parent);
        RuntimeItemInstantiated?.Invoke(item);

        return item;
    }

    public void DespawnItem(Item item, bool saveData = true, bool detachFromChunk = true)
    {
        if (item == null)
        {
            throw new ArgumentNullException(nameof(item));
        }

        if (item.DestructionHandled)
            return;

        RuntimeItemDespawning?.Invoke(item);

        if (detachFromChunk)
            item.GetComponentInParent<Chunk>()?.RemoveItem(item);

        UnregisterRuntimeItem(item);

        item.PrepareForDespawn(saveData);
        if (saveData && TryReturnItemToPool(item))
            return;

        Destroy(item.gameObject);
    }

    public void DestroyItem(Item item)
    {
        DespawnItem(item);
    }

    // 通过名称实例化：只保留一个（用可选参数覆盖绝大多数用法）
    public Item InstantiateItem(string itemName, Vector3 position = default, Quaternion rotation = default, Vector3 scale = default, GameObject parent = null)
    {
        ItemData templateData = GameRes.Instance.CreateItemData(itemName);
        if (templateData == null)
            throw new InvalidOperationException($"找不到物品定义或有效 Prefab: {itemName}");
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
        if (templateData == null)
            throw new InvalidOperationException($"找不到物品定义或有效 Prefab: {itemName}");
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
        if (data == null)
            throw new InvalidOperationException($"找不到物品定义或有效 Prefab: {itemName}");
        data.Guid = authoritativeGuid;
        return InstantiateItem(data, position, rotation, scale);
    }

    // 通过ItemData的transform信息实例化（保留此重载：项目内多处在用）
    public Item InstantiateItem(ItemData itemData, GameObject parent)
        => InstantiateItem(itemData, itemData.transform.position, itemData.transform.rotation, itemData.transform.scale, parent);

    // 生成GUID的辅助方法
    public int GenerateGuid() => Guid.NewGuid().GetHashCode();

    private void RegisterRuntimeItem(Item item, string context)
    {
        if (item == null)
        {
            Debug.LogError($"RegisterRuntimeItem: item为空, context={context}");
            return;
        }

        if (item.itemData == null)
        {
            Debug.LogError($"物品缺少itemData: {item.name}, context={context}", item);
            return;
        }

        _runtimeRegistry.Register(item, GenerateGuid);
        RefreshItemSpatialIndex(item);
        RefreshPerceptionColliderCache(item);
        _tickScheduler.Register(item);
        WorldTopologyBody.Ensure(item);
        WorldTopologyProxySource.Ensure(item);

        if (TryRegisterRuntimeAiEntity(item))
            ItemWorldPlacement.AttachRuntimeAi(item, item.gameObject);

        if (item is Map mapItem)
        {
            _cachedMap = mapItem;
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
        RefreshPerceptionColliderCache(item);
    }

    /// <summary>
    /// 将网络远程手持物从本地权威 Item 循环中移除，保留 GameObject 仅作视觉展示。
    /// </summary>
    public void MarkAsRemoteVisualOnly(Item item)
    {
        RuntimeItemDespawning?.Invoke(item);
        UnregisterRuntimeItem(item);
    }

    private void UnregisterRuntimeItem(Item item)
    {
        if (item == null || item.itemData == null) return;

        _runtimeRegistry.Remove(item);
        RemoveRuntimeAiEntity(item);

        if (item is Map)
        {
            _cachedMap = null;
        }

        RemoveItemFromSpatialIndex(item);
        _perceptionColliderCache.Remove(item);
        _tickScheduler.Remove(item);
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

    // ✅ 添加到分组
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

        _runtimeRegistry.AddToGroup(item);
    }

    // ✅ 获取同类物品列表
    public List<Item> GetItemsByNameID(string nameId)
    {
        if (RuntimeItemsGroup.TryGetValue(nameId, out var list))
        {
            return list;
        }
        return new List<Item>();
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
