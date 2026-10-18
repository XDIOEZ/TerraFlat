using System;
using System.Collections.Generic;
using System.Reflection;
using FlatWorld.Diagnostics;
using UnityEngine;

/// <summary>
/// Item GameObject 的复用策略。ItemMgr 只提供容量和 Prefab 工厂。
/// </summary>
internal sealed class ItemObjectPool
{
    #region 对象池性能诊断
    // 拒收原因只记录计数，不改变回收资格和生命周期顺序。
    private static readonly PerformanceDebugPoint AcquireDebug = new("FlatWorld.ItemPool.Acquire");
    private static readonly PerformanceDebugPoint ReturnDebug = new("FlatWorld.ItemPool.Return");
    private static readonly PerformanceDebugPoint HitDebug = new("FlatWorld.ItemPool.Hits");
    private static readonly PerformanceDebugPoint MissDebug = new("FlatWorld.ItemPool.Misses");
    private static readonly PerformanceDebugPoint DeadEntryDebug = new("FlatWorld.ItemPool.DeadEntries");
    private static readonly PerformanceDebugPoint AvailableDebug = new("FlatWorld.ItemPool.AvailableInstances");
    private static readonly PerformanceDebugPoint AcceptedReturnDebug = new("FlatWorld.ItemPool.AcceptedReturns");
    private static readonly PerformanceDebugPoint AlreadyPooledDebug = new("FlatWorld.ItemPool.Rejected.AlreadyPooled");
    private static readonly PerformanceDebugPoint DisabledReturnDebug = new("FlatWorld.ItemPool.Rejected.PoolingDisabled");
    private static readonly PerformanceDebugPoint HierarchyReturnDebug = new("FlatWorld.ItemPool.Rejected.HierarchyChanged");
    private static readonly PerformanceDebugPoint IdentityReturnDebug = new("FlatWorld.ItemPool.Rejected.MissingKey");
    private static readonly PerformanceDebugPoint TotalCapacityReturnDebug = new("FlatWorld.ItemPool.Rejected.TotalCapacity");
    private static readonly PerformanceDebugPoint TypeCapacityReturnDebug = new("FlatWorld.ItemPool.Rejected.TypeCapacity");
    private static readonly PerformanceDebugPoint RuntimeReturnDebug = new("FlatWorld.ItemPool.Rejected.RuntimeOrRootType");
    private static readonly PerformanceDebugPoint LoadedModuleReturnDebug = new("FlatWorld.ItemPool.Rejected.LoadedModule");
    private static readonly PerformanceDebugPoint UnsafeTypeReturnDebug = new("FlatWorld.ItemPool.Rejected.UnsafeComponent");
    private static readonly PerformanceDebugPoint ValidateComponentsDebug = new("FlatWorld.ItemPool.ValidateComponents");
    #endregion

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
        AvailableDebug.SetGauge(TotalCount);
        if (poolRoot != null) UnityEngine.Object.Destroy(poolRoot.gameObject);
        poolRoot = null;
    }

    public GameObject Acquire(string itemId, Func<string, GameObject> spawn)
    {
        return TryAcquire(itemId, out GameObject itemObject)
            ? itemObject : Adopt(itemId, spawn(itemId));
    }

    /// <summary>只尝试复用；未命中时由调用方决定同步或异步创建外壳。</summary>
    public bool TryAcquire(string itemId, out GameObject itemObject)
    {
        itemObject = null;
        using var acquireDebug = AcquireDebug.Measure(1, itemId);
        if (Pools.TryGetValue(itemId, out Queue<Item> pool))
        {
            while (pool.Count > 0)
            {
                Item pooledItem = pool.Dequeue();
                TotalCount = Mathf.Max(0, TotalCount - 1);
                AvailableDebug.SetGauge(TotalCount);
                if (pooledItem == null)
                {
                    DeadEntryDebug.AddWork(1);
                    continue;
                }

                try
                {
                    PooledItemMarker pooledMarker = pooledItem.PoolMarker;
                    pooledMarker.InPool = false;
                    pooledMarker.RestoreBaseline();
                    pooledItem.transform.SetParent(null, false);
                    HitDebug.AddWork(1);
                    itemObject = pooledItem.gameObject;
                    return true;
                }
                catch
                {
                    UnityEngine.Object.Destroy(pooledItem.gameObject);
                    throw;
                }
            }
        }

        MissDebug.AddWork(1);
        return false;
    }

    public GameObject Adopt(string itemId, GameObject itemObject)
    {
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
        using var returnDebug = ReturnDebug.Measure(1, item.itemData?.IDName);
        PooledItemMarker marker = item.PoolMarker;
        if (marker.InPool)
        {
            AlreadyPooledDebug.AddWork(1);
            return false;
        }
        if (marker.PoolingDisabled)
        {
            DisabledReturnDebug.AddWork(1);
            return false;
        }
        if (!marker.HasOriginalHierarchy())
        {
            HierarchyReturnDebug.AddWork(1);
            return false;
        }
        if (!CanPool(item)) return false;

        string poolKey = string.IsNullOrEmpty(marker.PoolKey) ? item.itemData?.IDName : marker.PoolKey;
        if (string.IsNullOrEmpty(poolKey))
        {
            IdentityReturnDebug.AddWork(1);
            return false;
        }
        if (TotalCount >= Mathf.Max(1, maxTotal))
        {
            TotalCapacityReturnDebug.AddWork(1);
            return false;
        }

        if (!Pools.TryGetValue(poolKey, out Queue<Item> pool))
        {
            pool = new Queue<Item>();
            Pools[poolKey] = pool;
        }

        if (pool.Count >= Mathf.Max(1, maxPerItem))
        {
            TypeCapacityReturnDebug.AddWork(1);
            return false;
        }

        item.NotifyReturnedToPool();
        marker.InPool = true;
        item.gameObject.SetActive(false);
        item.transform.SetParent(GetPoolRoot(owner), false);
        pool.Enqueue(item);
        TotalCount++;
        AcceptedReturnDebug.AddWork(1);
        AvailableDebug.SetGauge(TotalCount);
        return true;
    }

    private bool CanPool(Item item)
    {
        if (item == null || item.IsRuntimeTransitioning || item is Player || item is Map)
        {
            RuntimeReturnDebug.AddWork(1);
            return false;
        }

        using var validationDebug = ValidateComponentsDebug.Measure(context: item.itemData?.IDName);
        item.GetComponentsInChildren(true, poolComponents);
        ValidateComponentsDebug.AddWork(poolComponents.Count);
        for (int i = 0; i < poolComponents.Count; i++)
        {
            MonoBehaviour behaviour = poolComponents[i];
            if (behaviour == null || behaviour == item)
            {
                continue;
            }

            if (behaviour is Module module && (module.IsRuntimeLoaded || module.IsRuntimeTransitioning))
            {
                LoadedModuleReturnDebug.AddWork(1);
                poolComponents.Clear();
                return false;
            }
            if (behaviour is IItemPoolLifecycle) continue;

            Type type = behaviour.GetType();
            if (!IsPoolSafe(type))
            {
                UnsafeTypeReturnDebug.AddWork(1);
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

        bool hasDestroy = FindLifecycleMethod(type, "OnDestroy") != null;
        bool hasDisable = FindLifecycleMethod(type, "OnDisable") != null;
        safe = !hasDestroy || hasDisable;
        if (!safe && typeof(Module).IsAssignableFrom(type))
        {
            MethodInfo unload = FindLifecycleMethod(type, "OnUnload");
            safe = unload != null && unload.DeclaringType != typeof(Module);
        }

        PoolSafeTypes.Add(type, safe);
        return safe;
    }

    // 私有 Unity 回调和受保护卸载钩子都沿继承链查找，派生模块不能绕开基础清理约束。
    private static MethodInfo FindLifecycleMethod(Type type, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public |
                                   BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (Type current = type; current != null && current != typeof(MonoBehaviour); current = current.BaseType)
        {
            MethodInfo method = current.GetMethod(name, flags);
            if (method != null) return method;
        }
        return null;
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
