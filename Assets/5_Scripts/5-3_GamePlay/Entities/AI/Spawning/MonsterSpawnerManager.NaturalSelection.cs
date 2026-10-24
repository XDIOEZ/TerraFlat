using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;

public partial class MonsterSpawnerManager
{
    #region 到期后从已加载栖息地抽取生物

    private readonly List<NaturalSpawnCandidate> _naturalSpawnCandidates = new();
    private readonly List<ChunkRuntime> _naturalSpawnChunks = new();
    private float _nextNaturalSelectionTime;

    private bool ProcessDueSpawnWork(string sceneName, int currentDay)
    {
        float frequency = GameRuleConfiguration.Current.World.SpawnFrequencyMultiplier;
        if (frequency <= 0f || _naturalCreatureBirth != null ||
            _naturalCountdown.RemainingGameSeconds != 0d || Time.unscaledTime < _nextCreatureBirthTime)
            return false;

        if (_naturalCountdown.PendingCount > 0)
        {
            SpawnerConfig selected = _spawnerConfigs.Find(config =>
                config.PersistentId == _naturalCountdown.SelectedConfigId);
            SpawnerConfig.SpawnEntry entry = selected?.SpawnEntries.Find(candidate =>
                candidate.PrefabName == _naturalCountdown.SelectedSpeciesId);
            if (entry == null)
            {
                if (_runtimeStates.TryGetValue(_naturalCountdown.SelectedConfigId ?? string.Empty, out var stale))
                    stale.PendingSpawnCount = stale.PendingReplacementCount = 0;
                _naturalCountdown.Restart(GetCountdownInterval(_settings, frequency));
                return false;
            }
            return ProcessPendingSpawns(selected, GetOrCreateState(selected), sceneName, currentDay, entry);
        }

        if (Time.unscaledTime < _nextNaturalSelectionTime) return false;
        _nextNaturalSelectionTime = Time.unscaledTime + Mathf.Max(0.05f, _settings.SpawnRetryInterval);
        CollectNaturalSpawnCandidates(sceneName);
        if (_naturalSpawnCandidates.Count == 0) return false;
        NaturalSpawnCandidate choice = _naturalSpawnCandidates[SelectCountdownCandidate(
            _naturalSpawnCandidates, UnityEngine.Random.value)];
        SpawnerProgressSaveData state = GetOrCreateState(choice.Config);
        if (!TryBeginCountdownBatch(_naturalCountdown, choice.Config, choice.Entry, state, frequency, CountdownChanceRoll))
        {
            ObserveSpawn(choice.Config, "规则生成概率未通过，已重新开始统一倒计时");
            return true;
        }
        _remainingSearchAttempts.Remove(choice.Config);
        _nextSpawnRetryTime.Remove(GetConfigKey(choice.Config));
        return ProcessPendingSpawns(choice.Config, state, sceneName, currentDay, choice.Entry, choice.Position);
    }

    // 每轮每条规则至多检查四个真实落点，列表只保留本次通过检查的候选。
    private void CollectNaturalSpawnCandidates(string sceneName)
    {
        _naturalSpawnCandidates.Clear();
        int globalLimit = GameRuleMath.ScaleCount(_settings.GlobalAliveLimit,
            GameRuleConfiguration.Current.World.SpawnPopulationMultiplier, 1);
        foreach (SpawnerConfig config in _spawnerConfigs)
        {
            if (config == null || config.SpawnChance <= 0f ||
                (config.RequireGlobalDarkness && !IsGlobalDark(sceneName))) continue;
            if (!config.IgnorePopulationLimits &&
                (CountPopulationLimitedAlive() >= globalLimit || CountGroupAlive(config) >= GetEffectiveGroupLimit(config)))
            {
                ObserveSpawn(config, "未加入本轮候选：数量上限或栖息地容量不足");
                continue;
            }
            SpawnerProgressSaveData state = GetOrCreateState(config);
            _availableEntries.Clear();
            foreach (SpawnerConfig.SpawnEntry entry in config.SpawnEntries)
                if (CanSpawnEntry(config, entry, state)) _availableEntries.Add(entry);
            if (_availableEntries.Count == 0)
            {
                ObserveSpawn(config, "未加入本轮候选：预算、物种上限或后端条件不满足");
                continue;
            }
            for (int attempt = 0; attempt < Mathf.Min(4, config.SpawnSearchRetryCount); attempt++)
            {
                if (!TryGetValidSpawnPosition(config, out Vector3 position)) continue;
                bool found = false;
                foreach (SpawnerConfig.SpawnEntry entry in _availableEntries)
                {
                    if (!IsWithinLocalSpeciesLimit(config, entry, position)) continue;
                    _naturalSpawnCandidates.Add(new NaturalSpawnCandidate(config, entry, position));
                    found = true;
                }
                if (found) break;
            }
        }
    }

    // 直接采样当前世界玩家附近已就绪的区块，不为了自然刷新加载新区域。
    private bool TryGetLoadedSpawnCandidate(SpawnerConfig config, out Vector3 position)
    {
        position = default;
        _naturalSpawnChunks.Clear();
        if (_playerPositions.Count == 0 || _chunkManager == null) return false;
        string dimension = _chunkManager.ResolveWorldAddress(_playerPositions[0]).DimensionId;
        foreach (ChunkRuntime chunk in _chunkManager.Chunks.Values)
        {
            if (!IsLoadedNaturalSpawnChunk(chunk, dimension)) continue;
            ChunkTerrainData terrain = chunk.Terrain;
            var center = new Vector3(chunk.Address.ChunkOrigin.X + terrain.Width * 0.5f,
                chunk.Address.ChunkOrigin.Y + terrain.Height * 0.5f);
            float halfDiagonal = Mathf.Sqrt((float)terrain.Width * terrain.Width + (float)terrain.Height * terrain.Height) * 0.5f;
            if (IsNearAnyPlayer(center, config.MaxSpawnDistance + halfDiagonal)) _naturalSpawnChunks.Add(chunk);
        }
        if (_naturalSpawnChunks.Count == 0) return false;
        ChunkRuntime selected = _naturalSpawnChunks[UnityEngine.Random.Range(0, _naturalSpawnChunks.Count)];
        position = new Vector3(selected.Address.ChunkOrigin.X + UnityEngine.Random.Range(0, selected.Terrain.Width) + 0.5f,
            selected.Address.ChunkOrigin.Y + UnityEngine.Random.Range(0, selected.Terrain.Height) + 0.5f);
        return true;
    }

    internal static bool IsLoadedNaturalSpawnChunk(ChunkRuntime chunk, string dimension) =>
        chunk != null && chunk.DataStatus == ChunkDataStatus.Ready && chunk.Terrain != null &&
        !chunk.Terrain.IsDisposed && string.Equals(chunk.Address.DimensionId, dimension, StringComparison.Ordinal);

    private void CompleteNaturalSpawnAttempt(SpawnerProgressSaveData state, bool spawned, int currentDay)
    {
        double now = TryGetCurrentTimeData(out _, out TimeData time)
            ? time.GetTotalGameTimeSeconds() : Math.Max(0d, _naturalCountdown.LastObservedGameSeconds);
        CompleteCountdownSpawnAttempt(_naturalCountdown, state, spawned, currentDay, now);
    }

    // 已选批次的栖息地长期失效时结束本轮，不能用一条鱼永久锁住陆地刷新。
    private void AbandonNaturalBatch(SpawnerConfig config, SpawnerProgressSaveData state)
    {
        state.PendingSpawnCount = state.PendingReplacementCount = 0;
        _naturalCountdown.Restart(GetCountdownInterval(_settings,
            GameRuleConfiguration.Current.World.SpawnFrequencyMultiplier));
        if (TryGetCurrentTimeData(out _, out TimeData time))
            _naturalCountdown.LastObservedGameSeconds = time.GetTotalGameTimeSeconds();
        ObserveSpawn(config, "本批出生条件持续失效，结束本轮并重新开始统一倒计时");
    }

    private void RecordNaturalSpawnFailure(SpawnerConfig config, SpawnerProgressSaveData state)
    {
        int remaining = _remainingSearchAttempts.TryGetValue(config, out int value)
            ? value - 1 : Mathf.Max(1, config.SpawnSearchRetryCount) - 1;
        if (remaining > 0) _remainingSearchAttempts[config] = remaining;
        else
        {
            _remainingSearchAttempts.Remove(config);
            AbandonNaturalBatch(config, state);
        }
        _nextSpawnRetryTime[GetConfigKey(config)] = Time.unscaledTime + Mathf.Max(0.05f, _settings.SpawnRetryInterval);
    }

    #endregion
}
