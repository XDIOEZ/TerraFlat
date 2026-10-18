using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using Unity.Mathematics;
using UnityEngine;

public partial class MonsterSpawnerManager
{
    #region 同批出生落点

    internal static int GetCountdownBatchRequiredRoom(SpawnerCountdownProgress batch) =>
        batch != null && !batch.HasBatchAnchor ? Mathf.Max(1, batch.PendingCount) : 1;

    private SpawnerCountdownProgress GetSpawnBatch(SpawnerConfig config, SpawnerProgressSaveData state,
        SpawnerConfig.SpawnEntry entry)
    {
        if (config.ScheduleMode != SpawnerScheduleMode.Countdown || config.BatchSpawnRadius <= 0f || entry == null ||
            !state.SpeciesCountdowns.TryGetValue(entry.PrefabName, out SpawnerCountdownProgress batch))
            return null;
        if (batch.HasBatchAnchor && (!IsCountdownBatchAnchorUsable(batch, _itemManager.PlayerInSceneName,
                _playerPositions, config.MaxSpawnDistance + config.BatchSpawnRadius, WorldTopologyRuntime.GetActiveDomain()) ||
            !_chunkManager.IsRuntimeEntityPresentationReady(new Vector3(batch.BatchAnchorX, batch.BatchAnchorY))))
            batch.ClearBatchAnchor();
        return batch;
    }

    // 仅在旧场景、非法坐标或全部玩家离开后重选锚点，普通选点失败仍留在同一批附近。
    internal static bool IsCountdownBatchAnchorUsable(SpawnerCountdownProgress batch, string sceneName,
        IReadOnlyList<Vector3> players, float maxDistance, in WorldTopologyDomain topology)
    {
        if (batch == null || !batch.HasBatchAnchor || !IsFiniteBatchCoordinate(batch.BatchAnchorX) ||
            !IsFiniteBatchCoordinate(batch.BatchAnchorY) || string.IsNullOrWhiteSpace(sceneName) ||
            !string.Equals(batch.BatchAnchorScene, sceneName, StringComparison.Ordinal) ||
            !IsFiniteBatchCoordinate(maxDistance) || maxDistance < 0f)
            return false;
        float maximumSqr = maxDistance * maxDistance;
        var anchor = new float2(batch.BatchAnchorX, batch.BatchAnchorY);
        for (int i = 0; i < players.Count; i++)
        {
            Vector3 player = players[i];
            if (IsFiniteBatchCoordinate(player.x) && IsFiniteBatchCoordinate(player.y) &&
                topology.SqrDistance(new float2(player.x, player.y), anchor) <= maximumSqr)
                return true;
        }
        return false;
    }

    internal static bool IsCountdownBatchCandidate(SpawnerCountdownProgress batch, Vector3 candidate,
        float radius, in WorldTopologyDomain topology)
    {
        if (!IsFiniteBatchCoordinate(candidate.x) || !IsFiniteBatchCoordinate(candidate.y))
            return false;
        if (batch == null || !batch.HasBatchAnchor)
            return true;
        if (!IsFiniteBatchCoordinate(batch.BatchAnchorX) || !IsFiniteBatchCoordinate(batch.BatchAnchorY) ||
            !IsFiniteBatchCoordinate(radius) || radius <= 0f)
            return false;
        if (topology.SqrDistance(new float2(batch.BatchAnchorX, batch.BatchAnchorY),
                new float2(candidate.x, candidate.y)) > radius * radius)
            return false;
        return batch.BatchSpawnCells == null ||
               !batch.BatchSpawnCells.Contains(SpawnerCountdownProgress.GetBatchSpawnCell(candidate.x, candidate.y));
    }

    private static bool IsFiniteBatchCoordinate(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    #endregion
}
