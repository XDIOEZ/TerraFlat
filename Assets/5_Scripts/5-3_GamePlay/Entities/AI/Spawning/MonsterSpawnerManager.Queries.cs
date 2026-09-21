using System.Collections.Generic;
using UnityEngine;

public partial class MonsterSpawnerManager
{
    #region 配置索引

    /// <summary>资源会话内配置固定，条目所属关系和后端路由只在进入世界时建立。</summary>
    private void CacheConfigurationQueries()
    {
        foreach (SpawnerConfig config in _spawnerConfigs)
        {
            if (config == null) continue;
            GetConfigKey(config);
            if (config.SpawnEntries == null) continue;
            foreach (SpawnerConfig.SpawnEntry entry in config.SpawnEntries)
            {
                if (entry == null) continue;
                _entryOwners[entry] = config;
                if (!AiRuntimeBackendService.UsesEntities(entry)) continue;
                _entityConfigs.Add(config);
                if (!config.UnboundedDailyGrowth && !config.IgnorePopulationLimits)
                    _hasLimitedEntities = true;
            }
        }
    }

    private bool UsesEntityPopulation(SpawnerConfig config) =>
        AiRuntimeBackendService.UseEntities && _entityConfigs.Contains(config);

    #endregion

    #region 候选查询

    /// <summary>同一生成尝试的重试共用周边数量，只按玩家数查询一次，不按候选点重扫怪物。</summary>
    private void PrepareNearbyGroupCounts(SpawnerConfig config)
    {
        _nearbyGroupCounts.Clear();
        if (config.IgnorePopulationLimits || config.UnboundedDailyGrowth || config.PerPlayerAliveLimit <= 0)
            return;
        float radius = Mathf.Max(1f, config.PlayerPopulationRadius);
        for (int i = 0; i < _playerPositions.Count; i++)
        {
            int count = _monsterManager?.CountGroupWithinRadius(config, _playerPositions[i], radius * radius) ?? 0;
            if (UsesEntityPopulation(config))
                count += AiRuntimeBackendService.Ecology?.CountGroupWithinRadius(config, _playerPositions[i], radius * radius) ?? 0;
            _nearbyGroupCounts.Add(count);
        }
    }

    private sealed class HabitatCandidates
    {
        public float Until;
        public readonly List<Vector2> Positions = new(32);
    }

    /// <summary>容量计算与随机选树共用同一合格候选集；保留既有两秒生态快照周期。</summary>
    private List<Vector2> GetEligibleHabitatTrees(SpawnerConfig config)
    {
        if (!_treeCapacityCache.TryGetValue(config, out HabitatCandidates cached))
        {
            cached = new HabitatCandidates();
            _treeCapacityCache.Add(config, cached);
        }
        if (Time.unscaledTime < cached.Until)
            return cached.Positions;
        cached.Until = Time.unscaledTime + 2f;
        cached.Positions.Clear();
        if (_itemManager == null) return cached.Positions;
        IReadOnlyList<Vector2> trees = _treeHabitatIndex.GetActivePositions(_itemManager.PlayerInSceneName);
        for (int i = 0; i < trees.Count; i++)
            if (IsEligibleHabitatTree(config, trees[i]))
                cached.Positions.Add(trees[i]);
        return cached.Positions;
    }

    #endregion
}
