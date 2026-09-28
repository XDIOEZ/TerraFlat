using System;
using System.Collections.Generic;
using FlatWorld.Gameplay.Events;
using FlatWorld.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;

public partial class MonsterSpawnerManager
{
    #region 事件生成与位置

    /// <summary>事件候选重试共用相机缓冲；相机数量增加时完整扩容，不截断视野查询。</summary>
    private Camera[] _eventCameras = Array.Empty<Camera>();
    private float _nextEventSearchTime;

    public int GetEventSpawnPlayerCount(string worldKey)
    {
        string targetWorld = string.IsNullOrWhiteSpace(worldKey)
            ? SceneManager.GetActiveScene().name
            : worldKey;
        RefreshPlayerPositions(targetWorld);
        return _playerPositions.Count;
    }

    /// <summary>
    /// 事件出生不消耗自然生态预算，但与自然出生共用创建节流；未成功的数量由事件状态保留。
    /// </summary>
    public int SpawnEventCreatures(GameEventCreatureSpawnRequest request)
    {
        return SpawnEventCreatures(request, null);
    }

    public int SpawnEventCreatures(
        GameEventCreatureSpawnRequest request,
        List<int> spawnedActorGuids)
    {
        if (!GameNetwork.HasStateAuthority ||
            request == null ||
            request.Count <= 0 ||
            string.IsNullOrWhiteSpace(request.PrefabId) ||
            _gameManager == null || !_gameManager.IsGameplayReady ||
            _itemManager == null ||
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

        float now = Time.unscaledTime;
        if (now < _nextEventSearchTime || now < _nextCreatureBirthTime)
            return 0;
        _nextEventSearchTime = now + Mathf.Max(0.1f, _settings.EcologyTickInterval);
        if (!TryGetEventSpawnPosition(request, out Vector3 position))
            return 0;

        _nextCreatureBirthTime = now + Mathf.Max(0.1f, _settings.EcologyTickInterval);
        if (!TrySpawnEventCreature(request.PrefabId, position, out int actorGuid))
            return 0;
        spawnedActorGuids?.Add(actorGuid);
        return 1;
    }

    private bool TryGetEventSpawnPosition(
        GameEventCreatureSpawnRequest request,
        out Vector3 spawnPosition)
    {
        spawnPosition = default;
        int retries = Mathf.Clamp(request.SearchAttemptsPerCreature, 1, 8);
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
            candidate = WorldTopologyRuntime.NormalizePosition(candidate);
            candidate.x = Mathf.Floor(candidate.x) + 0.5f;
            candidate.y = Mathf.Floor(candidate.y) + 0.5f;

            if (IsNearAnyPlayer(candidate, exclusionDistance) ||
                IsVisibleByAnyActiveCamera(candidate, 0.15f) ||
                !_chunkManager.IsRuntimeEntityPresentationReady(candidate) ||
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

    /// <summary>按所有活动相机与循环世界最近镜像判断视野，padding 为额外视口边距。</summary>
    private bool IsVisibleByAnyActiveCamera(Vector3 worldPosition, float padding = 0.05f)
    {
        int required = Camera.allCamerasCount;
        if (_eventCameras.Length < required)
            Array.Resize(ref _eventCameras, Mathf.NextPowerOfTwo(Mathf.Max(4, required)));
        int count = Camera.GetAllCameras(_eventCameras);
        for (int i = 0; i < count; i++)
        {
            Camera camera = _eventCameras[i];
            if (camera == null || !camera.isActiveAndEnabled)
                continue;

            Vector2 nearest = WorldTopologyRuntime.NearestImagePosition(
                camera.transform.position, worldPosition);
            Vector3 viewport = camera.WorldToViewportPoint(new Vector3(nearest.x, nearest.y, worldPosition.z));
            if (viewport.z > 0f &&
                viewport.x >= -padding && viewport.x <= 1f + padding &&
                viewport.y >= -padding && viewport.y <= 1f + padding)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsEventBiomeAllowed(List<string> allowedBiomes, Vector3 worldPosition)
    {
        if (allowedBiomes == null || allowedBiomes.Count == 0)
            return true;

        Vector2Int worldCell = new(Mathf.FloorToInt(worldPosition.x), Mathf.FloorToInt(worldPosition.y));
        if (_chunkManager != null && _chunkManager.TryGetRuntimeBiomeName(
                worldCell + new Vector2(0.5f, 0.5f), out string runtimeBiomeName))
        {
            return IsAllowedBiomeName(allowedBiomes, runtimeBiomeName, runtimeBiomeName);
        }

        return false;
    }

    private bool IsEventLightAllowed(
        GameEventCreatureSpawnRequest request,
        Vector3 worldPosition)
    {
        bool needsCheck = request.RequireCompletelyDarkTile || request.MaxAllowedTileLight < 0.9999f;
        if (!needsCheck)
            return true;

        if (_lightLayerManager == null ||
            !_lightLayerManager.TryGetLightLevel(worldPosition, out float lightLevel))
        {
            return false;
        }

        float maximum = request.RequireCompletelyDarkTile
            ? LightLayerMgr.CompletelyDarkValue
            : Mathf.Clamp01(request.MaxAllowedTileLight);
        return lightLevel <= maximum + 0.0001f;
    }

    #endregion

    #region 事件生物 ECS 创建

    private bool TrySpawnEventCreature(
        string prefabId,
        Vector3 position,
        out int actorGuid)
    {
        actorGuid = 0;
        if (!AiRuntimeBackendService.UsesEntities(prefabId))
        {
            Debug.LogWarning($"[GameEvent] 物种 '{prefabId}' 未注册为 ECS Actor。", this);
            return false;
        }

        IAiEcologyBackend backend = AiRuntimeBackendService.Ecology;
        if (backend == null || !backend.SupportsSpecies(prefabId))
        {
            Debug.LogWarning($"[GameEvent] ECS 后端尚不支持物种 '{prefabId}'。", this);
            return false;
        }
        if (backend.ResidentCount >= Mathf.Max(1, _settings.MaxLoadedEntityActors))
            return false;
        return backend.TrySpawnEvent(prefabId, position, out actorGuid);
    }

    #endregion
}
