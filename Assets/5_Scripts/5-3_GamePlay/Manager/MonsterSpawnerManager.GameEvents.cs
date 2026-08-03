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
        if (_playerPositions.Count == 0 ||
            (request.RequireGlobalDarkness && !IsGlobalDark(activeWorldKey)))
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
                !TryGetLoadedMap(candidate, out Map map) ||
                !IsWalkableSpawnPosition(candidate) ||
                !IsEventBiomeAllowed(request.AllowedBiomes, map, candidate) ||
                !IsEventLightAllowed(request, candidate))
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

    private static bool IsEventBiomeAllowed(List<string> allowedBiomes, Map map, Vector3 worldPosition)
    {
        if (allowedBiomes == null || allowedBiomes.Count == 0)
            return true;

        ChunkGenerator_Land landGenerator = map?.LandGenerator;
        Vector2Int worldCell = new(Mathf.FloorToInt(worldPosition.x), Mathf.FloorToInt(worldPosition.y));
        if (landGenerator == null || !landGenerator.TryGetBiomeAtWorld(worldCell, out BiomeData biome))
            return false;

        for (int i = 0; i < allowedBiomes.Count; i++)
        {
            string allowed = allowedBiomes[i];
            if (string.Equals(allowed, biome.BiomeName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(allowed, biome.name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
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
            spawnedItem.GetComponentInChildren<Mod_ItemDetector>(true)?.Update_Detector();
            return true;
        }
        catch (Exception exception)
        {
            spawnedItem = null;
            Debug.LogError($"[GameEvent] Failed to spawn '{prefabId}': {exception.Message}");
            return false;
        }
    }
}
