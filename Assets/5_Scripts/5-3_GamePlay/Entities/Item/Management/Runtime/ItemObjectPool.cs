using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Item GameObject 的复用策略。ItemMgr 只提供容量和 Prefab 工厂。
/// </summary>
internal sealed class ItemObjectPool
{
    #region 池状态

    public Dictionary<string, Queue<Item>> Pools { get; } = new();
    public int TotalCount { get; private set; }

    private Transform poolRoot;
    // 回收时复用组件查询缓冲，避免每个对象生成 MonoBehaviour 数组。
    private readonly List<MonoBehaviour> poolComponents = new(32);
    // 类型的生命周期能力固定，只需反射一次。
    private static readonly Dictionary<Type, bool> PoolSafeTypes = new();

    #endregion

    #region 取还与清理

    /// <summary>重载目录前清空旧实例，防止复用旧模块结构和已释放的外观资源。</summary>
    public void Clear()
    {
        foreach (Queue<Item> pool in Pools.Values)
            while (pool.Count > 0)
            {
                Item item = pool.Dequeue();
                if (item != null) UnityEngine.Object.Destroy(item.gameObject);
            }
        Pools.Clear();
        TotalCount = 0;
        if (poolRoot != null) UnityEngine.Object.Destroy(poolRoot.gameObject);
        poolRoot = null;
    }

    public GameObject Acquire(string itemId, Func<string, GameObject> spawn)
    {
        if (Pools.TryGetValue(itemId, out Queue<Item> pool))
        {
            while (pool.Count > 0)
            {
                Item pooledItem = pool.Dequeue();
                TotalCount = Mathf.Max(0, TotalCount - 1);
                if (pooledItem == null)
                {
                    continue;
                }

                PooledItemMarker pooledMarker = pooledItem.PoolMarker;
                pooledMarker.InPool = false;
                pooledMarker.RestoreBaseline();

                pooledItem.transform.SetParent(null, false);
                return pooledItem.gameObject;
            }
        }

        GameObject itemObject = spawn(itemId);
        Item newItem = itemObject.GetComponent<Item>();
        if (newItem == null)
        {
            UnityEngine.Object.Destroy(itemObject);
            throw new InvalidOperationException($"Prefab 缺少 Item 组件: {itemId}");
        }
        PooledItemMarker marker = newItem.PoolMarker;
        marker.PoolKey = itemId;
        marker.InPool = false;
        marker.PoolingDisabled = false;
        return itemObject;
    }

    public bool TryReturn(Item item, Transform owner, int maxPerItem, int maxTotal)
    {
        PooledItemMarker marker = item.PoolMarker;
        if (marker.InPool || marker.PoolingDisabled ||
            !marker.HasOriginalHierarchy() || !CanPool(item))
        {
            return false;
        }

        string poolKey = string.IsNullOrEmpty(marker.PoolKey) ? item.itemData?.IDName : marker.PoolKey;
        if (string.IsNullOrEmpty(poolKey) || TotalCount >= Mathf.Max(1, maxTotal))
        {
            return false;
        }

        if (!Pools.TryGetValue(poolKey, out Queue<Item> pool))
        {
            pool = new Queue<Item>();
            Pools[poolKey] = pool;
        }

        if (pool.Count >= Mathf.Max(1, maxPerItem))
        {
            return false;
        }

        item.NotifyReturnedToPool();
        marker.InPool = true;
        item.gameObject.SetActive(false);
        item.transform.SetParent(GetPoolRoot(owner), false);
        pool.Enqueue(item);
        TotalCount++;
        return true;
    }

    private bool CanPool(Item item)
    {
        if (item == null || item is Player || item is Map)
        {
            return false;
        }

        item.GetComponentsInChildren(true, poolComponents);
        for (int i = 0; i < poolComponents.Count; i++)
        {
            MonoBehaviour behaviour = poolComponents[i];
            if (behaviour == null || behaviour == item || behaviour is IItemPoolLifecycle)
            {
                continue;
            }

            Type type = behaviour.GetType();
            if (!IsPoolSafe(type))
            {
                poolComponents.Clear();
                return false;
            }
        }

        poolComponents.Clear();
        return true;
    }

    /// <summary>带销毁回调的模块必须在标准 Unload 阶段完成同等清理，才允许复用。</summary>
    private static bool IsPoolSafe(Type type)
    {
        if (PoolSafeTypes.TryGetValue(type, out bool safe))
            return safe;

        const BindingFlags lifecycleFlags = BindingFlags.Instance |
                                            BindingFlags.Public |
                                            BindingFlags.NonPublic |
                                            BindingFlags.DeclaredOnly;
        bool hasDestroy = type.GetMethod("OnDestroy", lifecycleFlags) != null;
        bool hasDisable = type.GetMethod("OnDisable", lifecycleFlags) != null;
        safe = !hasDestroy || hasDisable;
        if (!safe && typeof(Module).IsAssignableFrom(type))
        {
            MethodInfo unload = type.GetMethod(nameof(Module.Unload), BindingFlags.Instance | BindingFlags.Public);
            safe = unload != null && unload.DeclaringType != typeof(Module);
        }

        PoolSafeTypes.Add(type, safe);
        return safe;
    }

    private Transform GetPoolRoot(Transform owner)
    {
        if (poolRoot != null)
        {
            return poolRoot;
        }

        GameObject root = new("ItemPool");
        root.transform.SetParent(owner, false);
        poolRoot = root.transform;
        return poolRoot;
    }

    #endregion
}
