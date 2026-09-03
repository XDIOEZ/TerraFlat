using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// 怪物运行时生命周期注册表。
/// 由生成器注入当前物种目录，并通过 ItemMgr 的统一创建/销毁事件维护怪物、分组、物种数量与死亡订阅；
/// 只负责“当前有哪些怪物”，不承载生成时间、生态预算、位置选择或回收策略。
/// </summary>
[DisallowMultipleComponent]
public sealed class MonsterManager : SingletonMono<MonsterManager>
{
    #region 类型

    /// <summary>怪物及其所属生态配置的只读快照项。</summary>
    public readonly struct Registration
    {
        internal Registration(Item item, SpawnerConfig config, string speciesId)
        {
            Item = item;
            Config = config;
            SpeciesId = speciesId;
        }

        public Item Item { get; }
        public SpawnerConfig Config { get; }
        public string SpeciesId { get; }
    }

    /// <summary>确保生态回收保护按引用计数成对释放。</summary>
    private sealed class EcologyRecycleProtectionLease : IDisposable
    {
        private MonsterManager _owner;
        private Item _item;

        internal EcologyRecycleProtectionLease(MonsterManager owner, Item item)
        {
            _owner = owner;
            _item = item;
        }

        public void Dispose()
        {
            MonsterManager owner = _owner;
            Item item = _item;
            _owner = null;
            _item = null;
            owner?.ReleaseEcologyRecycleProtection(item);
        }
    }

    #endregion

    #region 运行时状态

    private static readonly HashSet<string> RegisteredSpeciesIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SpawnerConfig> _configBySpecies = new(StringComparer.Ordinal);
    private readonly Dictionary<Item, Registration> _registrations = new();
    private readonly Dictionary<SpawnerConfig, int> _activeGroupCounts = new();
    private readonly Dictionary<string, int> _activeSpeciesCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<DamageReceiver, Item> _itemByDeathReceiver = new();
    private readonly Dictionary<Item, DamageReceiver> _deathReceiverByItem = new();
    private readonly Dictionary<Item, int> _ecologyRecycleProtectionCounts = new();
    private readonly List<Item> _cleanupItems = new(64);
    private int _activePopulationLimitedCount;
    private int _activeCountFrame = -1;
    private bool _activeCountsDirty = true;
    private bool _ownsSingleton;

    [ShowInInspector, ReadOnly]
    public int Count => _registrations.Count;

    /// <summary>仅统计当前激活且仍受全局上限约束的怪物。</summary>
    public int PopulationLimitedCount
    {
        get
        {
            EnsureActiveCountsCurrent();
            return _activePopulationLimitedCount;
        }
    }

    /// <summary>怪物完成注册时通知需要建立附属状态的系统。</summary>
    public event Action<Item, SpawnerConfig> MonsterRegistered;

    /// <summary>怪物完成注销时通知需要释放附属状态的系统。</summary>
    public event Action<Item, SpawnerConfig> MonsterUnregistered;

    /// <summary>怪物开始死亡时通知生态生成器补位。</summary>
    public event Action<Item, SpawnerConfig> MonsterDeathStarted;

    #endregion

    #region Unity 生命周期

    protected override void Awake()
    {
        base.Awake();
        _ownsSingleton = GetInstance() == this;
        if (!_ownsSingleton)
            return;

        ItemMgr.RuntimeItemInstantiated += Register;
        ItemMgr.RuntimeItemDespawning += Unregister;
    }

    protected override void OnDestroy()
    {
        if (_ownsSingleton)
        {
            ItemMgr.RuntimeItemInstantiated -= Register;
            ItemMgr.RuntimeItemDespawning -= Unregister;
            ResetWorld();
            MonsterRegistered = null;
            MonsterUnregistered = null;
            MonsterDeathStarted = null;
        }

        base.OnDestroy();
    }

    #endregion

    #region 物种目录

    /// <summary>注入当前世界物种目录，并从 ItemMgr 现有注册表一次性重建怪物索引。</summary>
    public void Configure(
        IReadOnlyList<SpawnerConfig> configs,
        IEnumerable<Item> existingItems)
    {
        ResetWorld();
        if (configs == null)
            throw new ArgumentNullException(nameof(configs));

        for (int configIndex = 0; configIndex < configs.Count; configIndex++)
        {
            SpawnerConfig config = configs[configIndex];
            if (config?.SpawnEntries == null)
                continue;

            for (int entryIndex = 0; entryIndex < config.SpawnEntries.Count; entryIndex++)
            {
                SpawnerConfig.SpawnEntry entry = config.SpawnEntries[entryIndex];
                string speciesId = entry?.PrefabName;
                if (string.IsNullOrWhiteSpace(speciesId))
                    continue;

                if (_configBySpecies.ContainsKey(speciesId))
                {
                    Debug.LogError(
                        $"[MonsterManager] 物种 {speciesId} 同时存在于多个生成配置，已忽略后续配置 {config.name}。",
                        config);
                    continue;
                }

                _configBySpecies.Add(speciesId, config);
                RegisteredSpeciesIds.Add(speciesId);
            }
        }

        if (existingItems == null)
            return;

        foreach (Item item in existingItems)
            Register(item);
    }

    /// <summary>清空当前世界目录和全部生命周期引用。</summary>
    public void ResetWorld()
    {
        ClearRegistrations();
        _configBySpecies.Clear();
        RegisteredSpeciesIds.Clear();
    }

    /// <summary>无需创建单例即可判断当前目录是否包含该生态物种。</summary>
    public static bool IsRegisteredSpeciesId(string itemId)
    {
        return !string.IsNullOrWhiteSpace(itemId) && RegisteredSpeciesIds.Contains(itemId);
    }

    /// <summary>判断物品 ID 是否属于当前生态物种目录。</summary>
    public bool IsManagedSpeciesId(string itemId)
    {
        return !string.IsNullOrWhiteSpace(itemId) && _configBySpecies.ContainsKey(itemId);
    }

    #endregion

    #region 注册接口

    /// <summary>注册一个已进入 ItemMgr 生命周期的怪物；重复注册不产生副作用。</summary>
    public void Register(Item item)
    {
        if (item == null || item.DestructionHandled || _registrations.ContainsKey(item))
            return;

        string speciesId = item.itemData?.IDName;
        if (string.IsNullOrWhiteSpace(speciesId) ||
            !_configBySpecies.TryGetValue(speciesId, out SpawnerConfig config))
        {
            return;
        }

        var registration = new Registration(item, config, speciesId);
        _registrations.Add(item, registration);
        InvalidateActiveCounts();

        DamageReceiver receiver = item.GetComponentInChildren<DamageReceiver>(true);
        if (receiver != null && !_itemByDeathReceiver.ContainsKey(receiver))
        {
            _itemByDeathReceiver.Add(receiver, item);
            _deathReceiverByItem.Add(item, receiver);
            receiver.DeathStarted += OnMonsterDeathStarted;
        }

        MonsterRegistered?.Invoke(item, config);
    }

    /// <summary>注销一个离开 ItemMgr 生命周期的怪物；重复注销不产生副作用。</summary>
    public void Unregister(Item item)
    {
        if (ReferenceEquals(item, null) || !_registrations.TryGetValue(item, out Registration registration))
            return;

        _registrations.Remove(item);
        InvalidateActiveCounts();

        _ecologyRecycleProtectionCounts.Remove(item);
        if (!_deathReceiverByItem.TryGetValue(item, out DamageReceiver receiver))
        {
            MonsterUnregistered?.Invoke(item, registration.Config);
            return;
        }

        _deathReceiverByItem.Remove(item);
        _itemByDeathReceiver.Remove(receiver);
        if (receiver != null)
            receiver.DeathStarted -= OnMonsterDeathStarted;
        MonsterUnregistered?.Invoke(item, registration.Config);
    }

    /// <summary>清理场景卸载留下的 Unity 伪空引用。</summary>
    public void PruneInvalidRegistrations()
    {
        _cleanupItems.Clear();
        foreach (KeyValuePair<Item, Registration> pair in _registrations)
        {
            Item item = pair.Key;
            if (item == null || item.itemData == null || item.DestructionHandled)
                _cleanupItems.Add(item);
        }

        for (int i = 0; i < _cleanupItems.Count; i++)
            Unregister(_cleanupItems[i]);
        _cleanupItems.Clear();
    }

    private void ClearRegistrations()
    {
        foreach (DamageReceiver receiver in _itemByDeathReceiver.Keys)
        {
            if (receiver != null)
                receiver.DeathStarted -= OnMonsterDeathStarted;
        }

        _registrations.Clear();
        _activeGroupCounts.Clear();
        _activeSpeciesCounts.Clear();
        _itemByDeathReceiver.Clear();
        _deathReceiverByItem.Clear();
        _ecologyRecycleProtectionCounts.Clear();
        _cleanupItems.Clear();
        _activePopulationLimitedCount = 0;
        _activeCountFrame = -1;
        _activeCountsDirty = true;
    }

    private void OnMonsterDeathStarted(DamageReceiver receiver)
    {
        if (receiver == null ||
            !_itemByDeathReceiver.TryGetValue(receiver, out Item item) ||
            item == null ||
            !_registrations.TryGetValue(item, out Registration registration))
        {
            return;
        }

        MonsterDeathStarted?.Invoke(item, registration.Config);
    }

    #endregion

    #region 查询接口

    /// <summary>只有激活且尚未进入销毁流程的怪物才占用生态生成上限。</summary>
    public static bool IsActiveForPopulationLimits(Item item)
    {
        return item != null &&
               !item.DestructionHandled &&
               item.gameObject.activeInHierarchy;
    }

    /// <summary>实体显隐变化后使本帧活动数量缓存失效。</summary>
    public void NotifyPopulationActivityChanged(Item item)
    {
        if (ReferenceEquals(item, null) || !_registrations.ContainsKey(item))
            return;

        InvalidateActiveCounts();
    }

    public bool Contains(Item item)
    {
        return item != null && _registrations.ContainsKey(item);
    }

    public bool TryGetConfig(Item item, out SpawnerConfig config)
    {
        config = null;
        if (item == null || !_registrations.TryGetValue(item, out Registration registration))
            return false;

        config = registration.Config;
        return true;
    }

    public int GetGroupCount(SpawnerConfig config)
    {
        if (config == null)
            return 0;

        EnsureActiveCountsCurrent();
        return _activeGroupCounts.TryGetValue(config, out int count) ? count : 0;
    }

    public int GetSpeciesCount(string speciesId)
    {
        if (string.IsNullOrWhiteSpace(speciesId))
            return 0;

        EnsureActiveCountsCurrent();
        return _activeSpeciesCounts.TryGetValue(speciesId, out int count) ? count : 0;
    }

    /// <summary>复制稳定快照，调用方可在回收实体时安全遍历。</summary>
    public void CopyRegistrations(List<Registration> output)
    {
        if (output == null)
            throw new ArgumentNullException(nameof(output));

        output.Clear();
        foreach (Registration registration in _registrations.Values)
            output.Add(registration);
    }

    /// <summary>统计指定生态组在圆形范围内的怪物数量。</summary>
    public int CountGroupWithinRadius(SpawnerConfig config, Vector3 center, float radiusSqr)
    {
        if (config == null || radiusSqr < 0f)
            return 0;

        int count = 0;
        foreach (Registration registration in _registrations.Values)
        {
            Item item = registration.Item;
            if (IsActiveForPopulationLimits(item) &&
                registration.Config == config &&
                WorldTopologyRuntime.SqrDistance(item.transform.position, center) <= radiusSqr)
            {
                count++;
            }
        }

        return count;
    }

    #endregion

    #region 生态回收保护

    /// <summary>为已注册怪物获取临时回收保护租约。</summary>
    public IDisposable AcquireEcologyRecycleProtection(Item item)
    {
        if (item == null)
            throw new ArgumentNullException(nameof(item));
        if (item.DestructionHandled)
            throw new InvalidOperationException("已进入销毁流程的怪物不能获取生态回收保护。");
        if (!_registrations.ContainsKey(item))
            throw new InvalidOperationException("只有已注册到 MonsterManager 的怪物才能获取生态回收保护。");

        _ecologyRecycleProtectionCounts.TryGetValue(item, out int count);
        _ecologyRecycleProtectionCounts[item] = count + 1;
        return new EcologyRecycleProtectionLease(this, item);
    }

    /// <summary>判断怪物是否仍持有至少一个生态回收保护租约。</summary>
    public bool IsEcologyRecycleProtected(Item item)
    {
        return item != null &&
               _ecologyRecycleProtectionCounts.TryGetValue(item, out int count) &&
               count > 0;
    }

    private void ReleaseEcologyRecycleProtection(Item item)
    {
        if (ReferenceEquals(item, null) ||
            !_ecologyRecycleProtectionCounts.TryGetValue(item, out int count))
        {
            return;
        }

        if (count <= 1)
            _ecologyRecycleProtectionCounts.Remove(item);
        else
            _ecologyRecycleProtectionCounts[item] = count - 1;
    }

    #endregion

    #region 计数工具

    /// <summary>每帧至多重建一次活动种群计数，兼容外部层级显隐变化。</summary>
    private void EnsureActiveCountsCurrent()
    {
        int currentFrame = Time.frameCount;
        if (!_activeCountsDirty && _activeCountFrame == currentFrame)
            return;

        _activeGroupCounts.Clear();
        _activeSpeciesCounts.Clear();
        _activePopulationLimitedCount = 0;

        foreach (Registration registration in _registrations.Values)
        {
            if (!IsActiveForPopulationLimits(registration.Item))
                continue;

            IncrementCount(_activeGroupCounts, registration.Config);
            IncrementCount(_activeSpeciesCounts, registration.SpeciesId);
            if (!registration.Config.UnboundedDailyGrowth &&
                !registration.Config.IgnorePopulationLimits)
            {
                _activePopulationLimitedCount++;
            }
        }

        _activeCountFrame = currentFrame;
        _activeCountsDirty = false;
    }

    private void InvalidateActiveCounts()
    {
        _activeCountsDirty = true;
    }

    private static void IncrementCount<TKey>(Dictionary<TKey, int> counts, TKey key)
    {
        counts.TryGetValue(key, out int count);
        counts[key] = count + 1;
    }

    #endregion
}
