using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Item GameObject 的复用策略。ItemMgr 只提供容量和 Prefab 工厂。
/// </summary>
internal sealed class ItemObjectPool
{
    public Dictionary<string, Queue<Item>> Pools { get; } = new();
    public int TotalCount { get; private set; }

    private Transform poolRoot;

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

                PooledItemMarker marker = pooledItem.GetComponent<PooledItemMarker>();
                if (marker != null)
                {
                    marker.InPool = false;
                    marker.RestoreBaseline();
                }

                pooledItem.transform.SetParent(null, false);
                return pooledItem.gameObject;
            }
        }

        GameObject itemObject = spawn(itemId);
        PooledItemMarker newMarker = itemObject.GetComponent<PooledItemMarker>();
        if (newMarker == null)
        {
            newMarker = itemObject.AddComponent<PooledItemMarker>();
        }

        newMarker.PoolKey = itemId;
        newMarker.InPool = false;
        newMarker.PoolingDisabled = !CanPool(itemObject.GetComponent<Item>());
        newMarker.CaptureBaseline();
        return itemObject;
    }

    public bool TryReturn(Item item, Transform owner, int maxPerItem, int maxTotal)
    {
        PooledItemMarker marker = item.GetComponent<PooledItemMarker>();
        if (marker == null || marker.InPool || marker.PoolingDisabled ||
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

    private static bool CanPool(Item item)
    {
        if (item == null || item is Player || item is Map)
        {
            return false;
        }

        MonoBehaviour[] behaviours = item.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null || behaviour == item || behaviour is PooledItemMarker ||
                behaviour is IItemPoolLifecycle)
            {
                continue;
            }

            Type type = behaviour.GetType();
            const BindingFlags flags = BindingFlags.Instance |
                                       BindingFlags.Public |
                                       BindingFlags.NonPublic |
                                       BindingFlags.DeclaredOnly;

            if (type.GetMethod("OnDestroy", flags) != null && type.GetMethod("OnDisable", flags) == null)
            {
                return false;
            }
        }

        return true;
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
}
