using System;
using System.Collections.Generic;
using FlatWorld.Gameplay.Events;
using FlatWorld.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;

public partial class MonsterSpawnerManager
{
    public int GetEventSpawnPlayerCount(string worldKey)
    {
        string targetWorld = string.IsNullOrWhiteSpace(worldKey)
            ? SceneManager.GetActiveScene().name
            : worldKey;
        RefreshPlayerPositions(targetWorld);
        return _playerPositions.Count;
    }

    /// <summary>
    /// Plain-data spawn bridge for configured game events. Event spawns intentionally do not
    /// consume the natural ecology budget; their own JSON count is the controlling limit.
    /// </summary>
    public int SpawnEventCreatures(GameEventCreatureSpawnRequest request)
    {
        return SpawnEventCreatures(request, null);
    }

    public int SpawnEventCreatures(
        GameEventCreatureSpawnRequest request,
        List<Item> spawnedItems)
    {
        if (!GameNetwork.HasStateAuthority ||
            request == null ||
            request.Count <= 0 ||
            string.IsNullOrWhiteSpace(request.PrefabId) ||
            ItemMgr.Instance == null ||
            DimensionManager.Instance?.ActiveDefinition?.EnableMonsterSpawning == false)
        {
            return 0;
        }

        string activeWorldKey = SceneManager.GetActiveScene().name;
        if (!string.IsNullOrWhiteSpace(request.WorldKey) &&
            !string.Equals(request.WorldKey, activeWorldKey, StringComparison.Ordinal))
        {
            return 0;
        }

        _dayTimeSystem ??= DayTimeSystem.Instance;
        RefreshPlayerPositions(activeWorldKey);
        bool ignoreEnvironmentalRestrictions = request.IgnoreEnvironmentalRestrictions;
        if (_playerPositions.Count == 0 ||
            (!ignoreEnvironmentalRestrictions &&
             request.RequireGlobalDarkness &&
             !IsGlobalDark(activeWorldKey)))
        {
            return 0;
        }

        int spawned = 0;
        for (int i = 0; i < request.Count; i++)
        {
            if (!TryGetEventSpawnPosition(request, out Vector3 position))
                continue;

            if (TrySpawnEventCreature(request.PrefabId, position, out Item spawnedItem))
            {
                spawnedItems?.Add(spawnedItem);
                spawned++;
            }
        }

        return spawned;
    }

    private bool TryGetEventSpawnPosition(
        GameEventCreatureSpawnRequest request,
        out Vector3 spawnPosition)
    {
        spawnPosition = default;
        int retries = Mathf.Max(1, request.SearchAttemptsPerCreature);
        float minDistance = Mathf.Max(0f, request.MinDistance);
        float maxDistance = Mathf.Max(minDistance, request.MaxDistance);
        float exclusionDistance = Mathf.Max(minDistance, request.PlayerVisibilityExclusionDistance);

        for (int i = 0; i < retries; i++)
        {
            Vector3 anchor = request.UseSpawnAnchor
                ? request.SpawnAnchor
                : _playerPositions[UnityEngine.Random.Range(0, _playerPositions.Count)];
            float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            float distance = UnityEngine.Random.Range(minDistance, maxDistance);
            Vector3 candidate = anchor + new Vector3(
                Mathf.Cos(angle) * distance,
                Mathf.Sin(angle) * distance,
                0f);
            candidate.x = Mathf.Floor(candidate.x) + 0.5f;
            candidate.y = Mathf.Floor(candidate.y) + 0.5f;

            if (IsNearAnyPlayer(candidate, exclusionDistance) ||
                (request.RequireOutsidePlayerView && IsVisibleByAnyActiveCamera(candidate)) ||
                !IsRuntimeTerrainReady(candidate) ||
                !IsWalkableSpawnPosition(candidate) ||
                (!request.IgnoreEnvironmentalRestrictions &&
                 !IsEventBiomeAllowed(request.AllowedBiomes, candidate)) ||
                (!request.IgnoreEnvironmentalRestrictions &&
                 !IsEventLightAllowed(request, candidate)))
            {
                continue;
            }

            spawnPosition = candidate;
            return true;
        }

        return false;
    }

    private static bool IsVisibleByAnyActiveCamera(Vector3 worldPosition)
    {
        Camera[] cameras = Camera.allCameras;
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera camera = cameras[i];
            if (camera == null || !camera.isActiveAndEnabled)
                continue;

            Vector3 viewport = camera.WorldToViewportPoint(worldPosition);
            if (viewport.z > 0f &&
                viewport.x >= -0.05f && viewport.x <= 1.05f &&
                viewport.y >= -0.05f && viewport.y <= 1.05f)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsEventBiomeAllowed(List<string> allowedBiomes, Vector3 worldPosition)
    {
        if (allowedBiomes == null || allowedBiomes.Count == 0)
            return true;

        Vector2Int worldCell = new(Mathf.FloorToInt(worldPosition.x), Mathf.FloorToInt(worldPosition.y));
        ChunkMgr runtimeManager = ChunkMgr.Instance;
        if (runtimeManager != null && runtimeManager.TryGetRuntimeBiomeName(
                worldCell + new Vector2(0.5f, 0.5f), out string runtimeBiomeName))
        {
            return IsAllowedBiomeName(allowedBiomes, runtimeBiomeName, runtimeBiomeName);
        }

        return false;
    }

    private static bool IsEventLightAllowed(
        GameEventCreatureSpawnRequest request,
        Vector3 worldPosition)
    {
        bool needsCheck = request.RequireCompletelyDarkTile || request.MaxAllowedTileLight < 0.9999f;
        if (!needsCheck)
            return true;

        if (LightLayerMgr.Instance == null ||
            !LightLayerMgr.Instance.TryGetLightLevel(worldPosition, out float lightLevel))
        {
            return false;
        }

        float maximum = request.RequireCompletelyDarkTile
            ? LightLayerMgr.CompletelyDarkValue
            : Mathf.Clamp01(request.MaxAllowedTileLight);
        return lightLevel <= maximum + 0.0001f;
    }

    #region 事件生物初始化校验

    private static bool TrySpawnEventCreature(
        string prefabId,
        Vector3 position,
        out Item spawnedItem)
    {
        spawnedItem = null;
        try
        {
            spawnedItem = ItemMgr.Instance.InstantiateItem(
                prefabId,
                position,
                Quaternion.identity,
                Vector3.one);
            if (spawnedItem == null)
                return false;

            spawnedItem.Load();
            if (!TryGetBoundAiActor(spawnedItem, out _))
            {
                Debug.LogError(
                    $"[GameEvent] 生成 '{prefabId}' 后没有找到已绑定的 IAIActor，已回收未初始化实体。");
                DespawnFailedEventCreature(spawnedItem);
                spawnedItem = null;
                return false;
            }

            spawnedItem.GetComponentInChildren<Mod_ItemDetector>(true)?.Update_Detector();
            return true;
        }
        catch (Exception exception)
        {
            DespawnFailedEventCreature(spawnedItem);
            spawnedItem = null;
            Debug.LogError($"[GameEvent] Failed to spawn '{prefabId}': {exception.Message}");
            return false;
        }
    }

    private static bool TryGetBoundAiActor(Item spawnedItem, out IAIActor actor)
    {
        actor = null;
        if (spawnedItem == null)
            return false;

        MonoBehaviour[] behaviours = spawnedItem.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is not IAIActor candidate || candidate.ActorItem != spawnedItem)
                continue;

            actor = candidate;
            return true;
        }

        return false;
    }

    private static void DespawnFailedEventCreature(Item spawnedItem)
    {
        if (spawnedItem == null || spawnedItem.DestructionHandled || ItemMgr.Instance == null)
            return;

        try
        {
            ItemMgr.Instance.DespawnItem(spawnedItem, saveData: false);
        }
        catch (Exception cleanupException)
        {
            Debug.LogError($"[GameEvent] 回收未初始化实体失败: {cleanupException.Message}");
        }
    }

    #endregion
}
