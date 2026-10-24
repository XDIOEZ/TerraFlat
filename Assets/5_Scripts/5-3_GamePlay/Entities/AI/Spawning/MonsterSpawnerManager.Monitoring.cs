using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using UnityEngine;

public partial class MonsterSpawnerManager
{
    #region 世界生物只读监控

    public sealed class WorldCreatureMonitor
    {
        public string World, Status, SelectedSpecies;
        public bool CountdownInitialized;
        public double RemainingGameSeconds, IntervalGameSeconds;
        public int PendingNaturalSpawns;
        public bool Authority, Initialized;
        public int Active, Hidden, Parked, Entities, LimitedPopulation, GlobalLimit;
        public int Births, Restores, Rollbacks, Creating;
        public float FrequencyMultiplier, NextTickSeconds, TickOverdueSeconds, TickInterval, GameTimeScale;
        public readonly List<CreatureSpawnMonitorEntry> Entries = new();
        internal readonly Dictionary<string, int> LoadedSpecies = new(StringComparer.Ordinal);
        public int Total => Active + Hidden + Parked + Entities;
    }

    public struct CreatureSpawnMonitorEntry
    {
        public string RuleId, SpeciesId, LastAttempt;
        public bool NaturalSpawnEnabled;
        public bool CountdownInitialized;
        public double RemainingGameSeconds, IntervalGameSeconds;
        public int Pending, Population, SpeciesLimit, Budget, LifetimeBirths;
        public float RetrySeconds, LastAttemptAge, MinDistance, MaxDistance;
    }

    private readonly struct SpawnObservation
    {
        public readonly string Result;
        public readonly float Time;
        public SpawnObservation(string result) { Result = result; Time = UnityEngine.Time.unscaledTime; }
    }

    private readonly Dictionary<SpawnerConfig, SpawnObservation> _spawnObservations = new();

    // 记录真实尝试经过的分支，不重新运行选点、概率或生成逻辑。
    private bool ObserveSpawn(SpawnerConfig config, string result)
    {
        _spawnObservations[config] = new SpawnObservation(result);
        return false;
    }

    internal static CreatureSpawnMonitorEntry ReadCountdownMonitor(SpawnerConfig config,
        SpawnerConfig.SpawnEntry entry, SpawnerCountdownProgress countdown, SpawnerRuntimeSettings settings,
        float frequencyMultiplier)
    {
        bool initialized = countdown != null && countdown.RemainingGameSeconds >= 0d;
        return new CreatureSpawnMonitorEntry
        {
            SpeciesId = entry.PrefabName,
            NaturalSpawnEnabled = config.SpawnChance > 0f,
            CountdownInitialized = initialized,
            RemainingGameSeconds = initialized ? countdown.RemainingGameSeconds : -1d,
            IntervalGameSeconds = countdown?.IntervalGameSeconds > 0d ? countdown.IntervalGameSeconds
                : settings != null ? GetCountdownInterval(settings, frequencyMultiplier) : -1d,
            Pending = IsCountdownBatchFor(countdown, config.PersistentId, entry.PrefabName) ? countdown.PendingCount : 0
        };
    }

    public void CaptureWorldCreatureMonitor(WorldCreatureMonitor target)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        using var worldScope = EnterOwnedWorld();
        target.World = _ownedWorld;
        target.Authority = GameNetwork.HasStateAuthority;
        target.Initialized = _worldInitialized;
        target.Active = target.Hidden = target.Entities = 0;
        SaveDataMgr saveManager = SaveDataMgr.GetExistingInstance();
        target.Parked = saveManager?.GetExistingParkedRuntimeAiCount(_ownedWorld) ?? 0;
        target.LoadedSpecies.Clear();
        target.Entries.Clear();
        target.Births = SuccessfulCreatureBirths;
        target.Restores = SuccessfulCreatureRestores;
        target.Rollbacks = RolledBackCreatureCreations;
        target.Creating = _creatingCreatureGuids.Count;
        target.FrequencyMultiplier = GameRuleConfiguration.Current.World.SpawnFrequencyMultiplier;
        target.CountdownInitialized = target.Authority && _naturalCountdown?.RemainingGameSeconds >= 0d;
        target.RemainingGameSeconds = target.CountdownInitialized ? _naturalCountdown.RemainingGameSeconds : -1d;
        target.IntervalGameSeconds = target.Authority && _naturalCountdown?.IntervalGameSeconds > 0d
            ? _naturalCountdown.IntervalGameSeconds
            : _settings != null ? GetCountdownInterval(_settings, target.FrequencyMultiplier) : -1d;
        target.PendingNaturalSpawns = target.Authority ? _naturalCountdown?.PendingCount ?? 0 : 0;
        target.SelectedSpecies = target.Authority ? _naturalCountdown?.SelectedSpeciesId : null;
        target.TickInterval = _settings?.EcologyTickInterval ?? 0f;
        target.NextTickSeconds = Mathf.Max(0f, _nextEcologyTickTime - Time.unscaledTime);
        target.TickOverdueSeconds = Mathf.Max(0f, Time.unscaledTime - _nextEcologyTickTime);
        target.GlobalLimit = _settings == null ? 0 : GameRuleMath.ScaleCount(_settings.GlobalAliveLimit,
            GameRuleConfiguration.Current.World.SpawnPopulationMultiplier, 1);
        IAiEcologyBackend ecology = AiRuntimeBackendService.ExistingEcology;
        target.LimitedPopulation = (_monsterManager?.ResidentPopulationLimitedCount ?? 0) +
            (ecology?.PopulationLimitedCount ?? 0);
        bool hasTime = TryGetCurrentTimeData(out _, out TimeData timeData);
        target.GameTimeScale = hasTime ? timeData.TimeScaleModifier * Time.timeScale *
            GameRuleConfiguration.Current.World.TimeSpeedMultiplier : 0f;
        target.Status = !_worldInitialized || _settings == null ? "生成器尚未初始化"
            : !IsOwnedWorldReady ? "等待世界就绪"
            : !target.Authority ? "联机客户端：刷新由服务器调度，倒计时不可用"
            : !isActiveAndEnabled ? "自然生成调度已关闭"
            : !hasTime ? "缺少当前世界时间数据"
            : _playerPositions.Count == 0 ? "未找到当前世界的有效玩家"
            : saveManager?.IsEcologySnapshotInProgress == true ? "保存快照中，暂缓出生"
            : _naturalCreatureBirth != null ? "正在分帧创建生物"
            : target.FrequencyMultiplier <= 0f ? "刷新频率为 0，倒计时已停用"
            : target.GameTimeScale <= 0f ? "游戏时间已停止，倒计时不推进"
            : "自然生成调度运行中";

        // 只遍历 Actor 对应的物品组，包含不在自然生成规则内的蜜蜂，不扫描地形和普通物品。
        GameRes resources = GameRes.ExistingInstance;
        ItemMgr itemManager = _itemManager != null ? _itemManager : ItemMgr.GetExistingInstance();
        if (resources != null)
            foreach (string species in resources.ActorDefinitions.Keys)
            {
                int alive = 0;
                if (itemManager != null && itemManager.RuntimeItemsGroup.TryGetValue(species, out var items))
                    for (int i = 0; i < items.Count; i++)
                    {
                        Item item = items[i];
                        if (item == null || item.gameObject.scene.name != _ownedWorld ||
                            !AiRuntimeBackendService.TryGetGameObjectActor(item, out IAIActor actor) || !actor.IsAlive)
                            continue;
                        alive++;
                        if (item.gameObject.activeInHierarchy) target.Active++;
                        else target.Hidden++;
                    }
                int entities = ecology?.GetSpeciesCount(species) ?? 0;
                target.Entities += entities;
                target.LoadedSpecies[species] = alive + entities;
            }

        foreach (SpawnerConfig config in _spawnerConfigs)
        {
            if (config?.SpawnEntries == null) continue;
            string key = _configKeys.TryGetValue(config, out string cached) ? cached
                : string.IsNullOrWhiteSpace(config.PersistentId) ? config.name : config.PersistentId.Trim();
            _runtimeStates.TryGetValue(key, out SpawnerProgressSaveData state);
            _spawnObservations.TryGetValue(config, out SpawnObservation observation);
            foreach (SpawnerConfig.SpawnEntry entry in config.SpawnEntries)
            {
                if (entry == null) continue;
                CreatureSpawnMonitorEntry row = ReadCountdownMonitor(config, entry,
                    target.Authority ? _naturalCountdown : null, _settings, target.FrequencyMultiplier);
                row.RuleId = key;
                target.LoadedSpecies.TryGetValue(entry.PrefabName, out row.Population);
                row.Population += saveManager?.GetExistingParkedRuntimeAiCount(_ownedWorld, entry.PrefabName) ?? 0;
                row.SpeciesLimit = config.IgnorePopulationLimits ? 0
                    : GameRuleMath.ScaleCount(entry.SpeciesAliveLimit, GameRuleConfiguration.Current.World.SpawnPopulationMultiplier);
                row.Budget = state?.AvailableBudget ?? -1;
                row.LifetimeBirths = state?.LifetimeSpawnCount ?? 0;
                row.LastAttempt = observation.Result;
                row.LastAttemptAge = Mathf.Max(0f, Time.unscaledTime - observation.Time);
                row.RetrySeconds = _nextSpawnRetryTime.TryGetValue(key, out float retry) ? Mathf.Max(0f, retry - Time.unscaledTime) : 0f;
                row.MinDistance = config.MinSpawnDistance;
                row.MaxDistance = config.MaxSpawnDistance;
                target.Entries.Add(row);
            }
        }
    }

    #endregion
}
