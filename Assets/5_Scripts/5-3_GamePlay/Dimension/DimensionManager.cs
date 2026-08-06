using System;
using System.Collections;
using System.Collections.Generic;
using FastCloner;
using FlatWorld.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class DimensionManager : SingletonAutoMono<DimensionManager>
{
    private const string DefaultCatalogResourcePath = "Config/DimensionCatalog_Default";
    private const string CaveExitPrefabId = "CaveExit";

    [SerializeField] private DimensionCatalogSO catalog;

    // Retained for backward compatibility with worlds created by the temporary
    // runtime-portal implementation. Formal mine entrances no longer bind this path.
    private readonly Dictionary<Vector2Int, GameObject> runtimePortals = new();
    private readonly List<Vector2Int> stalePortalCells = new();
    private ChunkMgr boundChunkManager;
    private bool isTransitioning;

    public WorldAddress ActiveAddress { get; private set; }
    public DimensionDefinition ActiveDefinition { get; private set; }
    public bool IsTransitioning => isTransitioning;

    protected override void Awake()
    {
        base.Awake();
        DontDestroyOnLoad(gameObject);
        LoadCatalog();
    }

    private void OnEnable()
    {
        GameManager.Event_PlayerEnterWorld += OnPlayerEnteredWorld;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
    }

    private void OnDisable()
    {
        GameManager.Event_PlayerEnterWorld -= OnPlayerEnteredWorld;
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
    }

    public void ActivateWorldFromScene(string worldKey)
    {
        ActivateWorld(WorldAddress.FromWorldKey(worldKey));
    }

    public void ActivateWorld(WorldAddress address)
    {
        LoadCatalog();
        EnsureWorldData(address);
        ActiveAddress = address;
        ActiveDefinition = catalog.Find(address.DimensionId) ?? catalog.Find(WorldAddress.SurfaceDimensionId);
    }

    public bool TryGetDefinitionForWorldKey(string worldKey, out DimensionDefinition definition)
    {
        LoadCatalog();
        definition = catalog.Find(WorldAddress.FromWorldKey(worldKey).DimensionId);
        return definition != null;
    }

    public string GetActiveMapCorePrefabId()
    {
        return string.IsNullOrWhiteSpace(ActiveDefinition?.MapCorePrefabId)
            ? "MapCore"
            : ActiveDefinition.MapCorePrefabId;
    }

    public int GetActiveGenerationSeed(int baseSeed)
    {
        return GetGenerationSeed(baseSeed, ActiveAddress, ActiveDefinition);
    }

    public int GetGenerationSeed(int baseSeed, WorldAddress address, DimensionDefinition definition = null)
    {
        LoadCatalog();
        definition ??= catalog.Find(address.DimensionId) ?? catalog.Find(WorldAddress.SurfaceDimensionId);
        unchecked
        {
            uint hash = (uint)(baseSeed == 0 ? 1 : baseSeed);
            string key = address.WorldKey;
            for (int i = 0; i < key.Length; i++)
                hash = (hash ^ key[i]) * 16777619u;
            hash = (hash ^ (uint)(definition?.SeedSalt ?? 0)) * 16777619u;
            int result = (int)hash;
            return result == 0 ? 1 : result;
        }
    }

    public void ConfigureMap(Map map)
    {
        if (map == null || ActiveDefinition == null)
            return;

        if (ActiveDefinition.GenerationMode == DimensionGenerationMode.Cave)
        {
            map.mapGenerators = new List<ChunkGeneratorBase>
            {
                new ChunkGenerator_Cave()
            };
        }
    }

    public bool TryBeginTransition(Player player, string targetDimensionId)
    {
        return TryBeginTransition(player, targetDimensionId, null);
    }

    public bool TryBeginTransition(Player player, string targetDimensionId, Item sourcePortalItem)
    {
        if (isTransitioning || player == null || player != ItemMgr.Instance?.User_Player)
            return false;

        if (GameNetwork.IsOnline)
        {
            Debug.LogWarning("[DimensionManager] 当前联机版本暂不支持维度切换。");
            return false;
        }

        WorldAddress sourceAddress = ActiveAddress.IsValid
            ? ActiveAddress
            : WorldAddress.FromWorldKey(SceneManager.GetActiveScene().name);
        WorldAddress targetAddress = sourceAddress.WithDimension(targetDimensionId);
        if (!targetAddress.IsValid || targetAddress == sourceAddress)
            return false;

        LoadCatalog();
        DimensionDefinition targetDefinition = catalog.Find(targetAddress.DimensionId);
        if (targetDefinition == null)
        {
            Debug.LogError($"[DimensionManager] 未注册目标维度：{targetAddress.DimensionId}");
            return false;
        }

        if (!GameManager.Instance.BeginDimensionTransitionLoading(targetDefinition.DisplayName))
            return false;

        PortalTransitionContext portalContext = ResolvePortalTransition(
            player.Data,
            sourceAddress,
            targetAddress,
            targetDefinition,
            sourcePortalItem);
        if (portalContext == null && IsMinePortalTransition(sourceAddress, targetAddress))
        {
            GameManager.Instance.FailDimensionTransitionLoading("矿坑入口锚点无效，无法切换维度。", null);
            return false;
        }

        StartCoroutine(TransitionCoroutine(player, sourceAddress, targetAddress, targetDefinition, portalContext));
        return true;
    }

    public void EnsureWorldData(WorldAddress address)
    {
        SaveDataMgr saveManager = SaveDataMgr.Instance;
        GameSaveData saveData = saveManager?.SaveData;
        if (saveData?.PlanetData_Dict == null || !address.IsValid)
            return;

        string worldKey = address.WorldKey;
        if (!saveData.PlanetData_Dict.TryGetValue(worldKey, out PlanetData worldData) || worldData == null)
        {
            PlanetData source = ResolveSurfaceData(saveData, address.PlanetId);
            worldData = source != null
                ? FastCloner.FastCloner.DeepClone(source)
                : new PlanetData { Name = address.PlanetId };
            worldData.Name = worldKey;
            worldData.MapData_Dict = new Dictionary<string, MapSave>();
            worldData.CurrentWeather = WeatherType.Clear;
            worldData.WeatherIntensity = 0f;
            saveData.PlanetData_Dict[worldKey] = worldData;
        }

        EnsureWorldTimeData(saveData, address);
    }

    private IEnumerator TransitionCoroutine(
        Player sourcePlayer,
        WorldAddress sourceAddress,
        WorldAddress targetAddress,
        DimensionDefinition targetDefinition,
        PortalTransitionContext portalContext)
    {
        isTransitioning = true;
        Data_Player playerData = sourcePlayer.Data;
        Vector3 sourcePosition = sourcePlayer.transform.position;
        GameController sourceController = sourcePlayer.GetComponentInChildren<GameController>(true);
        sourceController?.SetGameplayInputLocked(true);
        TransitionState state = new TransitionState();
        IEnumerator routine = ExecuteTransitionCoroutine(
            sourcePlayer,
            sourceAddress,
            targetAddress,
            targetDefinition,
            portalContext,
            state);
        Exception failure = null;

        while (true)
        {
            bool hasNext;
            object current = null;
            try
            {
                hasNext = routine.MoveNext();
                if (hasNext)
                    current = routine.Current;
            }
            catch (Exception exception)
            {
                failure = exception;
                break;
            }

            if (!hasNext)
                break;

            yield return current;
        }

        if (failure != null)
        {
            RecoverAfterTransitionFailure(playerData, sourceAddress, sourcePosition, state.ExitNotified, state.EnterNotified);
            ItemMgr.Instance?.User_Player
                ?.GetComponentInChildren<TileEffectReceiver>(true)
                ?.RefreshCurrentTileEffects();
            GameManager.Instance.FailDimensionTransitionLoading("维度切换失败，已尝试恢复玩家。", failure);
        }

        isTransitioning = false;
    }

    private IEnumerator ExecuteTransitionCoroutine(
        Player sourcePlayer,
        WorldAddress sourceAddress,
        WorldAddress targetAddress,
        DimensionDefinition targetDefinition,
        PortalTransitionContext portalContext,
        TransitionState state)
    {
        Data_Player playerData = sourcePlayer.Data;
        string playerName = playerData.Name_User;
        DimensionTravelProgressStore.SetLastPosition(playerData, sourceAddress, sourcePlayer.transform.position);
        sourcePlayer.GetComponentInChildren<TileEffectReceiver>(true)?.PrepareForWorldTransition();
        ItemMgr.Instance.SavePlayer();

        GameManager.Instance.SetDimensionTransitionLoading("正在保存当前维度…", 0.22f);
        GameManager.Instance.NotifyDimensionWorldExiting();
        state.ExitNotified = true;
        SaveDataMgr.Instance.Save_And_WriteToDisk();

        ItemMgr.Instance.ReleasePlayerForWorldTransition(sourcePlayer);
        ChunkMgr.Instance.OnSceneChange();
        yield return null;

        GameManager.Instance.SetDimensionTransitionLoading("正在创建目标维度…", 0.48f);
        EnsureWorldData(targetAddress);
        Scene oldScene = SceneManager.GetActiveScene();
        Scene targetScene = SceneManager.GetSceneByName(targetAddress.WorldKey);
        if (!targetScene.IsValid() || !targetScene.isLoaded)
            targetScene = SceneManager.CreateScene(targetAddress.WorldKey);

        SceneManager.SetActiveScene(targetScene);
        ActivateWorld(targetAddress);

        if (oldScene.IsValid() && oldScene.isLoaded && oldScene != targetScene)
        {
            AsyncOperation unloadOperation = SceneManager.UnloadSceneAsync(oldScene);
            while (unloadOperation != null && !unloadOperation.isDone)
                yield return null;
        }

        Vector3 targetPosition = ResolveTargetPosition(playerData, targetAddress, targetDefinition, portalContext);
        playerData.CurrentSceneName = targetAddress.WorldKey;
        playerData.transform.position = targetPosition;
        playerData.transform.rotation = Quaternion.identity;
        if (playerData.transform.scale == Vector3.zero)
            playerData.transform.scale = Vector3.one;
        SaveDataMgr.Instance.SaveData.PlayerData_Dict[playerName] = playerData;

        GameManager.Instance.SetDimensionTransitionLoading("正在加载目标维度…", 0.68f);
        GameManager.Instance.NotifyDimensionWorldEntered();
        state.EnterNotified = true;

        Player targetPlayer = ItemMgr.Instance.LoadPlayer(playerName);
        targetPlayer.transform.position = targetPosition;
        targetPlayer.Data.transform.position = targetPosition;
        targetPlayer.GetComponentInChildren<GameController>(true)?.SetGameplayInputLocked(true);
        GameManager.Instance.NotifyDimensionPlayerEntered(targetPlayer);

        while (ChunkMgr.Instance.HasPendingChunkLoads)
            yield return null;

        if (portalContext?.EnsureCaveExit == true)
        {
            GameManager.Instance.SetDimensionTransitionLoading("正在固定矿洞出口…", 0.88f);
            yield return EnsureCaveExitCoroutine(playerData, targetPlayer, targetDefinition, portalContext);
        }

        yield return null;
        targetPlayer.GetComponentInChildren<TileEffectReceiver>(true)?.RefreshCurrentTileEffects();
        targetPlayer.GetComponentInChildren<GameController>(true)?.SetGameplayInputLocked(false);
        ItemMgr.Instance.SavePlayer();
        SaveDataMgr.Instance.Save_And_WriteToDisk();
    }

    private sealed class TransitionState
    {
        public bool ExitNotified;
        public bool EnterNotified;
    }

    private sealed class PortalTransitionContext
    {
        public DimensionPortalAnchor Anchor;
        public Vector3 TargetPortalPosition;
        public bool EnsureCaveExit;
    }

    private void RecoverAfterTransitionFailure(
        Data_Player playerData,
        WorldAddress sourceAddress,
        Vector3 sourcePosition,
        bool exitNotified,
        bool enterNotified)
    {
        if (playerData == null)
            return;

        if (ItemMgr.Instance?.User_Player == null)
        {
            Scene scene = SceneManager.GetSceneByName(sourceAddress.WorldKey);
            if (!scene.IsValid() || !scene.isLoaded)
                scene = SceneManager.CreateScene(sourceAddress.WorldKey);
            SceneManager.SetActiveScene(scene);
            ActivateWorld(sourceAddress);

            playerData.CurrentSceneName = sourceAddress.WorldKey;
            playerData.transform.position = sourcePosition;
            SaveDataMgr.Instance.SaveData.PlayerData_Dict[playerData.Name_User] = playerData;

            if (exitNotified && !enterNotified)
                GameManager.Instance.NotifyDimensionWorldEntered();

            Player recoveredPlayer = ItemMgr.Instance.LoadPlayer(playerData.Name_User);
            recoveredPlayer.transform.position = sourcePosition;
            recoveredPlayer.Data.transform.position = sourcePosition;
            GameManager.Instance.NotifyDimensionPlayerEntered(recoveredPlayer);
        }
        else
        {
            ItemMgr.Instance.User_Player.GetComponentInChildren<GameController>(true)?.SetGameplayInputLocked(false);
            if (exitNotified && !enterNotified)
                GameManager.Instance.NotifyDimensionWorldEntered();
        }
    }

    private void OnPlayerEnteredWorld(Player player)
    {
        if (player == null || player != ItemMgr.Instance?.User_Player)
            return;

        ActivateWorldFromScene(SceneManager.GetActiveScene().name);
    }

    private IEnumerator RefreshPortalsNextFrame(Player player)
    {
        yield return null;
        if (player == null ||
            ActiveDefinition == null ||
            string.IsNullOrWhiteSpace(ActiveDefinition.PortalTargetDimensionId))
        {
            yield break;
        }

        ClearRuntimePortals();
        BindChunkManager();
        if (boundChunkManager == null)
            yield break;

        List<Chunk> loadedChunks = new(boundChunkManager.Chunk_Dic_Active_ByPos.Values);
        for (int i = 0; i < loadedChunks.Count; i++)
            TrySpawnPortalsForChunk(loadedChunks[i]);
    }

    private void BindChunkManager()
    {
        ChunkMgr current = ChunkMgr.Instance;
        if (boundChunkManager == current)
            return;

        UnbindChunkManager();
        boundChunkManager = current;
        if (boundChunkManager != null)
            boundChunkManager.OnChunkLoadFinish.DynamicCalls += TrySpawnPortalsForChunk;
    }

    private void UnbindChunkManager()
    {
        if (boundChunkManager != null)
            boundChunkManager.OnChunkLoadFinish.DynamicCalls -= TrySpawnPortalsForChunk;
        boundChunkManager = null;
    }

    private void TrySpawnPortalsForChunk(Chunk chunk)
    {
        PruneStaleRuntimePortals();
        if (chunk == null ||
            !chunk.IsReady ||
            chunk.Map == null ||
            ActiveDefinition == null ||
            string.IsNullOrWhiteSpace(ActiveDefinition.PortalTargetDimensionId))
        {
            return;
        }

        if (ActiveAddress.IsSurface)
            TrySpawnSurfaceEntrance(chunk);
        else if (ActiveDefinition.GenerationMode == DimensionGenerationMode.Cave)
            SpawnKnownCaveEntrances(chunk);
    }

    private void TrySpawnSurfaceEntrance(Chunk chunk)
    {
        LoadCatalog();
        DimensionDefinition caveDefinition = catalog.Find(ActiveDefinition.PortalTargetDimensionId);
        if (caveDefinition == null || caveDefinition.GenerationMode != DimensionGenerationMode.Cave)
            return;

        Vector2 rawChunkSize = ChunkMgr.GetChunkSize();
        Vector2Int chunkSize = new Vector2Int(
            Mathf.Max(1, Mathf.RoundToInt(rawChunkSize.x)),
            Mathf.Max(1, Mathf.RoundToInt(rawChunkSize.y)));
        Vector2Int chunkOrigin = chunk.MapSave?.MapPosition
            ?? Chunk.GetChunkPosition(chunk.transform.position, chunkSize);
        int baseSeed = SaveDataMgr.Instance?.SaveData?.Seed ?? 1;
        int caveSeed = GetGenerationSeed(
            baseSeed,
            ActiveAddress.WithDimension(caveDefinition.DimensionId),
            caveDefinition);
        if (!DimensionPortalLayout.ShouldGenerateEntrance(
                chunkOrigin,
                caveSeed,
                caveDefinition.CaveEntranceChunkChance))
        {
            return;
        }

        for (int candidateIndex = 0;
             candidateIndex < DimensionPortalLayout.CandidateCount;
             candidateIndex++)
        {
            Vector2Int cell = DimensionPortalLayout.GetCandidateCell(
                chunkOrigin,
                chunkSize,
                caveSeed,
                candidateIndex);
            TileData topTile = chunk.Map.GetTopTile(cell);
            if (topTile == null || topTile is TileData_Water || !topTile.IsWalkable)
                continue;

            Vector3 portalPosition = new Vector3(cell.x + 0.5f, cell.y + 0.5f, 0f);
            if (chunk.TryGetItemsByPosition(portalPosition, out List<Item> items) &&
                items.Exists(existingItem => existingItem != null && existingItem != chunk.Map))
            {
                continue;
            }

            SpawnRuntimePortal(chunk, portalPosition, caveDefinition.DimensionId);
            DimensionTravelProgressStore.AddPortalAnchor(
                ItemMgr.Instance?.User_Player?.Data,
                ActiveAddress.PlanetId,
                portalPosition);
            return;
        }
    }

    private void SpawnKnownCaveEntrances(Chunk chunk)
    {
        Data_Player playerData = ItemMgr.Instance?.User_Player?.Data;
        List<Vector3> anchors = DimensionTravelProgressStore.GetPortalAnchors(
            playerData,
            ActiveAddress.PlanetId);
        if (anchors.Count == 0)
            return;

        Vector2 rawChunkSize = ChunkMgr.GetChunkSize();
        Vector2Int chunkSize = new Vector2Int(
            Mathf.Max(1, Mathf.RoundToInt(rawChunkSize.x)),
            Mathf.Max(1, Mathf.RoundToInt(rawChunkSize.y)));
        Vector2Int chunkOrigin = chunk.MapSave?.MapPosition
            ?? Chunk.GetChunkPosition(chunk.transform.position, chunkSize);
        for (int i = 0; i < anchors.Count; i++)
        {
            if (Chunk.GetChunkPosition(anchors[i], chunkSize) == chunkOrigin)
            {
                SpawnRuntimePortal(
                    chunk,
                    anchors[i],
                    ActiveDefinition.PortalTargetDimensionId);
            }
        }
    }

    private void SpawnRuntimePortal(Chunk ownerChunk, Vector3 position, string targetDimensionId)
    {
        Vector2Int portalCell = new Vector2Int(
            Mathf.FloorToInt(position.x),
            Mathf.FloorToInt(position.y));
        if (runtimePortals.TryGetValue(portalCell, out GameObject existingPortal))
        {
            if (existingPortal != null)
                return;
            runtimePortals.Remove(portalCell);
        }

        GameObject portalObject = new GameObject(
            $"DimensionPortal_{targetDimensionId}_{portalCell.x}_{portalCell.y}");
        Scene ownerScene = ownerChunk.gameObject.scene;
        if (ownerScene.IsValid())
            SceneManager.MoveGameObjectToScene(portalObject, ownerScene);
        portalObject.transform.SetParent(ownerChunk.transform, true);
        portalObject.transform.position = position;

        SpriteRenderer renderer = portalObject.AddComponent<SpriteRenderer>();
        GameObject stonePrefab = GameRes.Instance?.GetPrefab("Mine_Stone", false);
        renderer.sprite = stonePrefab != null
            ? stonePrefab.GetComponentInChildren<SpriteRenderer>(true)?.sprite
            : null;
        renderer.color = ActiveDefinition.GenerationMode == DimensionGenerationMode.Cave
            ? new Color(0.35f, 0.85f, 1f, 1f)
            : new Color(0.72f, 0.35f, 1f, 1f);
        renderer.sortingOrder = 50;
        portalObject.transform.localScale = new Vector3(1.35f, 1.35f, 1f);

        CircleCollider2D collider = portalObject.AddComponent<CircleCollider2D>();
        collider.isTrigger = true;
        collider.radius = 0.75f;

        DimensionPortal portal = portalObject.AddComponent<DimensionPortal>();
        portal.Initialize(targetDimensionId);
        runtimePortals[portalCell] = portalObject;
    }

    private void PruneStaleRuntimePortals()
    {
        stalePortalCells.Clear();
        foreach (KeyValuePair<Vector2Int, GameObject> pair in runtimePortals)
        {
            GameObject portal = pair.Value;
            if (portal == null)
            {
                stalePortalCells.Add(pair.Key);
                continue;
            }

            Vector2Int currentCell = new Vector2Int(
                Mathf.FloorToInt(portal.transform.position.x),
                Mathf.FloorToInt(portal.transform.position.y));
            if (currentCell != pair.Key)
            {
                Destroy(portal);
                stalePortalCells.Add(pair.Key);
            }
        }

        for (int i = 0; i < stalePortalCells.Count; i++)
            runtimePortals.Remove(stalePortalCells[i]);
        stalePortalCells.Clear();
    }

    private void OnActiveSceneChanged(Scene oldScene, Scene newScene)
    {
        if (GameManager.Instance != null && GameManager.Instance.IsInGameWorld && newScene.IsValid())
            ActivateWorldFromScene(newScene.name);
    }

    #region 正式矿坑入口

    private static bool IsMinePortalTransition(WorldAddress sourceAddress, WorldAddress targetAddress)
    {
        return sourceAddress.IsSurface && targetAddress.DimensionId == WorldAddress.CaveDimensionId ||
               sourceAddress.DimensionId == WorldAddress.CaveDimensionId && targetAddress.IsSurface;
    }

    // Compatibility helper for saves/tests produced while portals used 1:1 world coordinates.
    public static Vector3 GetCorrespondingPosition(Vector3 sourcePosition)
    {
        return sourcePosition;
    }

    private void ClearRuntimePortals()
    {
        foreach (GameObject portal in runtimePortals.Values)
        {
            if (portal != null)
                Destroy(portal);
        }
        runtimePortals.Clear();
        stalePortalCells.Clear();
    }

    private static PortalTransitionContext ResolvePortalTransition(
        Data_Player playerData,
        WorldAddress sourceAddress,
        WorldAddress targetAddress,
        DimensionDefinition targetDefinition,
        Item sourcePortalItem)
    {
        if (sourceAddress.IsSurface && targetAddress.DimensionId == WorldAddress.CaveDimensionId)
        {
            DimensionPortalAnchor anchor = DimensionTravelProgressStore.GetOrCreateCaveAnchor(
                playerData,
                sourceAddress,
                sourcePortalItem,
                targetAddress,
                targetDefinition);
            return anchor == null
                ? null
                : new PortalTransitionContext
                {
                    Anchor = anchor,
                    TargetPortalPosition = anchor.CaveExitPosition,
                    EnsureCaveExit = true
                };
        }

        if (sourceAddress.DimensionId == WorldAddress.CaveDimensionId && targetAddress.IsSurface)
        {
            if (!DimensionTravelProgressStore.TryGetAnchorByCaveExit(
                    playerData,
                    sourceAddress,
                    sourcePortalItem,
                    out DimensionPortalAnchor anchor))
            {
                return null;
            }

            return new PortalTransitionContext
            {
                Anchor = anchor,
                TargetPortalPosition = anchor.SurfaceEntrancePosition
            };
        }

        return null;
    }

    private static Vector3 ResolveTargetPosition(
        Data_Player playerData,
        WorldAddress targetAddress,
        DimensionDefinition targetDefinition,
        PortalTransitionContext portalContext)
    {
        if (portalContext != null)
            return portalContext.TargetPortalPosition + targetDefinition.PortalOffset;

        return DimensionTravelProgressStore.TryGetLastPosition(playerData, targetAddress, out Vector3 savedPosition)
            ? savedPosition
            : targetDefinition.DefaultSpawnPosition;
    }

    private static IEnumerator EnsureCaveExitCoroutine(
        Data_Player playerData,
        Player targetPlayer,
        DimensionDefinition targetDefinition,
        PortalTransitionContext portalContext)
    {
        Vector3 desiredPosition = portalContext.TargetPortalPosition;
        Vector2Int chunkPosition = Chunk.GetChunkPosition(desiredPosition);
        Chunk targetChunk = null;
        bool requestCompleted = false;
        ChunkMgr.Instance.RequestLoadChunk_By_Position(chunkPosition, loadedChunk =>
        {
            targetChunk = loadedChunk;
            requestCompleted = true;
        });

        while (!requestCompleted || targetChunk == null || !targetChunk.IsReady)
            yield return null;

        Item caveExit = FindCaveExit(targetChunk, portalContext.Anchor, desiredPosition);
        if (caveExit == null)
        {
            caveExit = targetChunk.InstantiateItemInChunkDeterministic(
                CaveExitPrefabId,
                portalContext.Anchor.CaveExitGuid,
                desiredPosition,
                Quaternion.identity,
                Vector3.one);
            if (caveExit == null)
                throw new InvalidOperationException("无法在目标 Chunk 创建 CaveExit。");

            caveExit.Load();
        }
        else if (!caveExit.IsInitialized)
        {
            caveExit.Load();
        }

        caveExit.transform.SetPositionAndRotation(desiredPosition, Quaternion.identity);
        caveExit.transform.localScale = Vector3.one;
        caveExit.itemData.transform.position = desiredPosition;
        caveExit.itemData.transform.rotation = Quaternion.identity;
        caveExit.itemData.transform.scale = Vector3.one;
        caveExit.itemData.Stack.CanBePickedUp = false;
        targetChunk.AddItem(caveExit);

        portalContext.TargetPortalPosition = caveExit.transform.position;
        DimensionTravelProgressStore.UpdateCaveExit(playerData, portalContext.Anchor, caveExit);
        Vector3 safePosition = portalContext.TargetPortalPosition + targetDefinition.PortalOffset;
        targetPlayer.transform.position = safePosition;
        targetPlayer.Data.transform.position = safePosition;
        playerData.transform.position = safePosition;
        SaveDataMgr.Instance.SaveData.PlayerData_Dict[playerData.Name_User] = playerData;
    }

    private static Item FindCaveExit(Chunk chunk, DimensionPortalAnchor anchor, Vector3 desiredPosition)
    {
        if (chunk.RunTimeItems.TryGetValue(anchor.CaveExitGuid, out Item anchoredExit) &&
            anchoredExit?.itemData?.IDName == CaveExitPrefabId)
        {
            return anchoredExit;
        }

        if (!chunk.RuntimeItemsGroup.TryGetValue(CaveExitPrefabId, out HashSet<Item> exits))
            return null;

        foreach (Item candidate in exits)
        {
            if (candidate != null && WorldTopologyRuntime.SqrDistance(candidate.transform.position, desiredPosition) <= 0.01f)
                return candidate;
        }

        return null;
    }

    #endregion

    private void LoadCatalog()
    {
        if (catalog != null && catalog.Dimensions != null && catalog.Dimensions.Count > 0)
            return;

        catalog = Resources.Load<DimensionCatalogSO>(DefaultCatalogResourcePath);
        if (catalog == null)
            catalog = DimensionCatalogSO.CreateRuntimeDefault();
        else if (catalog.Dimensions == null || catalog.Dimensions.Count == 0)
            catalog.ResetToDefaults();
    }

    private static PlanetData ResolveSurfaceData(GameSaveData saveData, string planetId)
    {
        if (saveData.PlanetData_Dict.TryGetValue(planetId, out PlanetData surfaceData) && surfaceData != null)
            return surfaceData;

        PlanetData readyPlanet = GameManager.Instance?.ReadyPlanetData;
        return readyPlanet != null && string.Equals(readyPlanet.Name, planetId, StringComparison.Ordinal)
            ? readyPlanet
            : null;
    }

    private static void EnsureWorldTimeData(GameSaveData saveData, WorldAddress address)
    {
        saveData.DayTimeData ??= new DayTimeSaveData();
        saveData.DayTimeData.WorldTimeDict ??= new Dictionary<string, SerializableTimeData>();
        saveData.DayTimeData.SceneLightingRateDict ??= new Dictionary<string, float>();

        string worldKey = address.WorldKey;
        if (!saveData.DayTimeData.WorldTimeDict.ContainsKey(worldKey))
        {
            if (!address.IsSurface && saveData.DayTimeData.WorldTimeDict.TryGetValue(address.PlanetId, out SerializableTimeData surfaceTime))
            {
                SerializableTimeData dimensionTime = new SerializableTimeData(surfaceTime.ToTimeData())
                {
                    ReferenceScene = address.PlanetId
                };
                saveData.DayTimeData.WorldTimeDict[worldKey] = dimensionTime;
            }
            else
            {
                saveData.DayTimeData.WorldTimeDict[worldKey] = new SerializableTimeData(GameManager.Instance.ReadyTimeData ?? new TimeData());
            }
        }

        if (!saveData.DayTimeData.SceneLightingRateDict.ContainsKey(worldKey))
            saveData.DayTimeData.SceneLightingRateDict[worldKey] = 1f;
    }
}
