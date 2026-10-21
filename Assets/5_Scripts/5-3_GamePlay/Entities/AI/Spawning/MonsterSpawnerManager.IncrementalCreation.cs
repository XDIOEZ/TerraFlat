using System;
using System.Collections.Generic;
using System.Diagnostics;
using FlatWorld.Networking;
using UnityEngine;
using Debug = UnityEngine.Debug;

public partial class MonsterSpawnerManager
{
    #region 分帧生物出生

    private IEnumerator<int> _naturalCreatureBirth;
    private const double CreatureCreationSliceMilliseconds = 1.5d;
    private const int CreatureCreationStepsPerFrame = 8;

    // 每帧只推进一个出生任务，阶段边界必须让帧，单个模块仍保持原子加载。
    private void AdvanceNaturalCreatureBirth()
    {
        if (!IsOwnedWorldReady || !GameNetwork.HasStateAuthority)
        {
            CancelNaturalCreatureBirth();
            return;
        }
        if (SaveDataMgr.Instance?.IsEcologySnapshotInProgress == true) return;
        long started = Stopwatch.GetTimestamp();
        try
        {
            for (int step = 0; step < CreatureCreationStepsPerFrame; step++)
            {
                bool pending;
                using (CreationMarker.Auto())
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    bool sampling = _samplingPerformance;
                    long bytes = sampling ? GC.GetAllocatedBytesForCurrentThread() : 0;
                    long ticks = sampling ? Stopwatch.GetTimestamp() : 0;
                    try { pending = _naturalCreatureBirth.MoveNext(); }
                    finally
                    {
                        if (sampling) _creationSamples.Add(Stopwatch.GetTimestamp() - ticks,
                            GC.GetAllocatedBytesForCurrentThread() - bytes);
                    }
#else
                    pending = _naturalCreatureBirth.MoveNext();
#endif
                }
                if (!pending)
                {
                    CancelNaturalCreatureBirth();
                    return;
                }
                if (_naturalCreatureBirth.Current == 0 ||
                    (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency >= CreatureCreationSliceMilliseconds)
                    return;
            }
        }
        catch (Exception error)
        {
            Debug.LogError($"[MonsterSpawnerManager] 分帧出生失败：{error}", this);
            CancelNaturalCreatureBirth();
        }
    }

    private void CancelNaturalCreatureBirth()
    {
        IEnumerator<int> pending = _naturalCreatureBirth;
        _naturalCreatureBirth = null;
        pending?.Dispose();
    }

    /// <summary>新生物在停用区完成准备，成功回调才交付真实身份；释放枚举器会取消并回收。</summary>
    public IEnumerator<int> SpawnCreatureInSteps(string speciesId, Vector3 position, Action<Item> completed,
        SpawnerConfig.SpawnEntry initialization = null, Func<bool> canCommit = null)
    {
        IEnumerator<int> creation = SpawnCreatureInOwnedWorldSteps(speciesId, position, completed, initialization, canCommit);
        try
        {
            while (true)
            {
                int step;
                // 同步世界作用域不能跨让帧，每一次推进和取消分别进入所属世界。
                using (EnterOwnedWorld())
                {
                    if (!creation.MoveNext()) yield break;
                    step = creation.Current;
                }
                yield return step;
            }
        }
        finally { using (EnterOwnedWorld()) creation.Dispose(); }
    }

    private IEnumerator<int> SpawnCreatureInOwnedWorldSteps(string speciesId, Vector3 position, Action<Item> completed,
        SpawnerConfig.SpawnEntry initialization, Func<bool> canCommit)
    {
        if (!PrepareCreatureFactory() || !IsValidCreatureRequest(speciesId, position, GameNetwork.HasStateAuthority,
                IsOwnedWorldReady) || completed == null ||
            GameRes.ExistingInstance == null || !GameRes.ExistingInstance.TryGetActorDefinition(speciesId, out _) ||
            AiRuntimeBackendService.UsesEntities(speciesId)) yield break;
        ItemData data = GameRes.ExistingInstance.CreateItemData(speciesId);
        data.Guid = GenerateCreatureGuid();
        int reservedGuid = data.Guid;
        _creatingCreatureGuids.Add(reservedGuid);
        Item created = null;
        uint generation = 0;
        bool committed = false;
        try
        {
            yield return 0;
            using var creation = _itemManager.InstantiateItemInSteps(data, position, candidate =>
            {
                created = candidate;
                generation = candidate.RuntimeGeneration;
            }, candidate =>
            {
                ApplySpawnInitialization(candidate, initialization);
            }, () => GameNetwork.HasStateAuthority && IsOwnedWorldReady && canCommit?.Invoke() != false);
            while (true)
            {
                if (!IsOwnedWorldReady || !GameNetwork.HasStateAuthority) yield break;
                if (!creation.MoveNext()) break;
                yield return creation.Current;
            }
            if (created == null) yield break;
            if (!IsValidCreatedCreature(created, reservedGuid, generation))
                throw new InvalidOperationException($"生物 {speciesId} 未交付完整 AI。");
            created.GetComponentInChildren<Mod_ItemDetector>(true)?.Update_Detector();
            completed(created);
            if (!IsValidCreatedCreature(created, reservedGuid, generation))
                throw new InvalidOperationException($"生物 {speciesId} 在出生回调中已失效。");
            SuccessfulCreatureBirths++;
            committed = true;
        }
        finally
        {
            _creatingCreatureGuids.Remove(reservedGuid);
            if (!committed)
            {
                if (created != null && created.RuntimeGeneration == generation &&
                    ReferenceEquals(created.itemData, data) && !created.DestructionHandled && !created.IsInPool)
                    _itemManager?.DespawnItem(created, saveData: false);
                RolledBackCreatureCreations++;
            }
        }
    }

    private IEnumerator<int> CreateNaturalCreatureInSteps(SpawnerConfig config, SpawnerProgressSaveData state,
        SpawnerConfig.SpawnEntry entry, Vector3 position, int currentDay, Action recordBirth)
    {
        bool born = false;
        using (var creation = SpawnCreatureInSteps(entry.PrefabName, position, _ => born = true, entry,
                   () => IsNaturalCreatureCommitAllowed(config, state, entry, position)))
            while (creation.MoveNext()) yield return creation.Current;
        if (!born) yield break;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (_samplingPerformance) _sampleSpawnSuccesses++;
#endif
        if (TryGetCurrentTimeData(out _, out TimeData timeData)) currentDay = timeData.GetCurrentDay();
        recordBirth?.Invoke();
        if (!config.UnboundedDailyGrowth)
            state.AvailableBudget = Mathf.Max(0, state.AvailableBudget - Mathf.Max(1, entry.EcologyCost));
        _remainingSearchAttempts.Remove(config);
        _nextSpawnRetryTime[GetConfigKey(config)] = Time.unscaledTime + Mathf.Max(_settings.EcologyTickInterval, config.AsyncSpawnInterval);
        if (config.ScheduleMode == SpawnerScheduleMode.Countdown)
            CompleteCountdownSpawnAttempt(state, entry, true, currentDay);
        else state.CompleteSpawnAttempt(true, currentDay);
    }

    private bool IsNaturalCreatureCommitAllowed(SpawnerConfig config, SpawnerProgressSaveData state,
        SpawnerConfig.SpawnEntry entry, Vector3 position)
    {
        // 准备期间玩家和地形仍会变化，真正交付前重新检查出生资格。
        if (config == null || _chunkManager == null || _itemManager == null) return false;
        RefreshPlayerPositions(_itemManager.PlayerInSceneName);
        PrepareNearbyGroupCounts(config);
        int globalLimit = GameDifficultyService.ScaleCount(_settings.GlobalAliveLimit,
            GameDifficultyService.Current.World.SpawnPopulationMultiplier, 1);
        return CanSpawnEntry(config, entry, state) &&
               (config.UnboundedDailyGrowth || config.IgnorePopulationLimits ||
                (CountPopulationLimitedAlive() < globalLimit && CountGroupAlive(config) < GetEffectiveGroupLimit(config))) &&
               (!config.RequireGlobalDarkness || IsGlobalDark(_itemManager.PlayerInSceneName)) &&
               _playerPositions.Count > 0 && IsWithinPlayerPopulationLimit(config, position) &&
               IsWithinLocalSpeciesLimit(config, entry, position) &&
               !IsNearAnyPlayer(position, Mathf.Max(config.MinSpawnDistance, config.PlayerVisibilityExclusionDistance)) &&
               !IsVisibleByAnyActiveCamera(position, 0.15f) &&
               _chunkManager.IsRuntimeEntityPresentationReady(position) && IsRuntimeTerrainReady(position) &&
               (config.WaterOnly ? AquaticHabitat.CanSwimAt(position) : IsWalkableSpawnPosition(position)) &&
               IsBiomeAllowed(config, position) && IsGroundTileAllowed(config, position) && IsLightAllowed(config, position);
    }

    #endregion
}
