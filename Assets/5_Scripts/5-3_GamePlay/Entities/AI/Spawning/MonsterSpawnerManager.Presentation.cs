using System;
using System.Diagnostics;
using System.Collections.Generic;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

public partial class MonsterSpawnerManager
{
    #region 区块表现事件

    private readonly Queue<RuntimeWorldAddress> _presentationChanges = new(16);
    private bool _processingPresentationChanges;

    /// <summary>先解绑再销毁实体/区块，退出和维度切换不得因表现通知唤醒旧世界对象。</summary>
    private void UnsubscribePresentationEvents()
    {
        if (_chunkManager != null)
            _chunkManager.RuntimeEntityPresentationChanged -= OnChunkPresentationChanged;
        if (_itemManager != null)
            _itemManager.RuntimeAiAddressChanged -= OnRuntimeAiAddressChanged;
        _initialDormancyPending = false;
        _presentationItems.Clear();
        _presentationChanges.Clear();
    }

    /// <summary>复用 ItemMgr 的正式区块索引，只处理变化地址内的实体。</summary>
    private void OnChunkPresentationChanged(RuntimeWorldAddress address)
    {
        if (!enabled || _gameManager == null || !_gameManager.IsGameplayReady || _itemManager == null)
            return;
        _presentationChanges.Enqueue(address);
        if (_processingPresentationChanges) return;
        _processingPresentationChanges = true;
        using (DormancyMarker.Auto())
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            bool sampling = _samplingPerformance;
            long bytes = sampling ? GC.GetAllocatedBytesForCurrentThread() : 0;
            long ticks = sampling ? Stopwatch.GetTimestamp() : 0;
#endif
            try
            {
                // SetActive 可同步触发其它生命周期；队列避免嵌套通知改写正在遍历的快照。
                while (_presentationChanges.Count > 0 && enabled && _itemManager != null)
                {
                    _itemManager.CopyRuntimeAiItems(_presentationChanges.Dequeue(), _presentationItems);
                    for (int i = 0; i < _presentationItems.Count; i++)
                        if (_monsterManager != null && _monsterManager.Contains(_presentationItems[i]))
                            RefreshTrackedItemChunkDormancy(_presentationItems[i]);
                }
            }
            finally
            {
                _presentationItems.Clear();
                _processingPresentationChanges = false;
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
