using System;
using System.Diagnostics;
using System.Collections.Generic;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

public partial class MonsterSpawnerManager
{
    #region 区块表现事件

    private readonly Queue<RuntimeWorldAddress> _presentationChanges = new(16);
    private readonly HashSet<RuntimeWorldAddress> _queuedPresentationChanges = new();
    private readonly Queue<RuntimeWorldAddress> _wakePriorityAddresses = new(16);
    private readonly HashSet<RuntimeWorldAddress> _queuedWakePriorityAddresses = new();
    private int _presentationItemCursor;

    /// <summary>先解绑再销毁实体/区块，退出和维度切换不得因表现通知唤醒旧世界对象。</summary>
    private void UnsubscribePresentationEvents()
    {
        if (_chunkManager != null)
            _chunkManager.RuntimeEntityPresentationChanged -= OnChunkPresentationChanged;
        if (_itemManager != null)
            _itemManager.RuntimeAiAddressChanged -= OnRuntimeAiAddressChanged;
        _presentationItems.Clear();
        _presentationChanges.Clear();
        _queuedPresentationChanges.Clear();
        _wakePriorityAddresses.Clear();
        _queuedWakePriorityAddresses.Clear();
        _presentationItemCursor = 0;
    }

    /// <summary>区块事件只记录脏地址，实际显隐由低频调度器分批执行。</summary>
    private void OnChunkPresentationChanged(RuntimeWorldAddress address)
    {
        if (!enabled || _itemManager == null)
            return;
        if (_queuedPresentationChanges.Add(address))
            _presentationChanges.Enqueue(address);
        QueueWakePriorityAddress(address);
    }

    /// <summary>新区块优先唤醒本地居民，不等待全世界休眠地址轮询一圈。</summary>
    private void QueueWakePriorityAddress(RuntimeWorldAddress address)
    {
        if (_queuedWakePriorityAddresses.Add(address))
            _wakePriorityAddresses.Enqueue(address);
    }

    /// <summary>复用 ItemMgr 地址索引，单轮最多检查固定数量，遇到一次显隐变化即让帧。</summary>
    private bool ProcessPresentationChanges(int maxChecks)
    {
        using (DormancyMarker.Auto())
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            bool sampling = _samplingPerformance;
            long bytes = sampling ? GC.GetAllocatedBytesForCurrentThread() : 0;
            long ticks = sampling ? Stopwatch.GetTimestamp() : 0;
#endif
            try
            {
                int checks = 0;
                while (checks < maxChecks)
                {
                    if (_presentationItemCursor >= _presentationItems.Count)
                    {
                        _presentationItems.Clear();
                        _presentationItemCursor = 0;
                        if (_presentationChanges.Count == 0)
                            return false;
                        RuntimeWorldAddress address = _presentationChanges.Dequeue();
                        checks++;
                        _queuedPresentationChanges.Remove(address);
                        _itemManager.CopyRuntimeAiItems(address, _presentationItems);
                        continue;
                    }

                    Item item = _presentationItems[_presentationItemCursor++];
                    checks++;
                    if (_monsterManager != null && _monsterManager.Contains(item) &&
                        RefreshTrackedItemChunkDormancy(item))
                        return true;
                }
                return false;
            }
            finally
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (sampling)
                    _dormancySamples.Add(Stopwatch.GetTimestamp() - ticks,
                        GC.GetAllocatedBytesForCurrentThread() - bytes);
#endif
            }
        }
    }

    /// <summary>移动通知已完成索引更新；每次跨区块只检查对应实体一次。</summary>
    private void OnRuntimeAiAddressChanged(Item item)
    {
        if (!enabled || _gameManager == null || !_gameManager.IsGameplayReady ||
            _monsterManager == null || !_monsterManager.Contains(item))
            return;

        using (DormancyMarker.Auto())
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            bool sampling = _samplingPerformance;
            long bytes = sampling ? GC.GetAllocatedBytesForCurrentThread() : 0;
            long ticks = sampling ? Stopwatch.GetTimestamp() : 0;
            try { RefreshTrackedItemChunkDormancy(item); }
            finally
            {
                if (sampling)
                    _dormancySamples.Add(Stopwatch.GetTimestamp() - ticks,
                        GC.GetAllocatedBytesForCurrentThread() - bytes);
            }
#else
            RefreshTrackedItemChunkDormancy(item);
#endif
        }
    }

    #endregion

    #region 注册快照

    /// <summary>注册表不变时复用快照；失效时完整扩容复制，不丢弃超容量实体。</summary>
    private void RefreshMonsterSnapshot()
    {
        if (_monsterManager == null || _monsterSnapshotVersion == _monsterManager.RegistrationVersion)
            return;
        _monsterManager.CopyRegistrations(_monsterSnapshot);
        _monsterSnapshotVersion = _monsterManager.RegistrationVersion;
    }

    #endregion
}
