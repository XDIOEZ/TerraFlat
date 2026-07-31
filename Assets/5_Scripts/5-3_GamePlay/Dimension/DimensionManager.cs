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

    [SerializeField] private DimensionCatalogSO catalog;

    private GameObject runtimePortal;
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
        unchecked
        {
            uint hash = (uint)(baseSeed == 0 ? 1 : baseSeed);
            string key = ActiveAddress.WorldKey;
            for (int i = 0; i < key.Length; i++)
                hash = (hash ^ key[i]) * 16777619u;
            hash = (hash ^ (uint)(ActiveDefinition?.SeedSalt ?? 0)) * 16777619u;
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

        StartCoroutine(TransitionCoroutine(player, sourceAddress, targetAddress, targetDefinition));
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
        DimensionDefinition targetDefinition)
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
            GameManager.Instance.FailDimensionTransitionLoading("维度切换失败，已尝试恢复玩家。", failure);
        }

        isTransitioning = false;
    }

    private IEnumerator ExecuteTransitionCoroutine(
        Player sourcePlayer,
        WorldAddress sourceAddress,
        WorldAddress targetAddress,
        DimensionDefinition targetDefinition,
        TransitionState state)
    {
        Data_Player playerData = sourcePlayer.Data;
        string playerName = playerData.Name_User;

        DimensionTravelProgressStore.SetLastPosition(playerData, sourceAddress, sourcePlayer.transform.position);
        ItemMgr.Instance.SavePlayer();

        GameManager.Instance.SetDimensionTransitionLoading("正在保存当前维度…", 0.22f);
        GameManager.Instance.NotifyDimensionWorldExiting();
        state.ExitNotified = true;
        SaveDataMgr.Instance.Save_And_WriteToDisk();

        ItemMgr.Instance.ReleasePlayerForWorldTransition(sourcePlayer);
        ClearRuntimePortal();
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

        Vector3 targetPosition = DimensionTravelProgressStore.TryGetLastPosition(playerData, targetAddress, out Vector3 savedPosition)
            ? savedPosition
            : targetDefinition.DefaultSpawnPosition;
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

        yield return null;
        targetPlayer.GetComponentInChildren<GameController>(true)?.SetGameplayInputLocked(false);
        ItemMgr.Instance.SavePlayer();
        SaveDataMgr.Instance.Save_And_WriteToDisk();
    }

    private sealed class TransitionState
    {
        public bool ExitNotified;
        public bool EnterNotified;
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
        StartCoroutine(SpawnPortalNextFrame(player));
    }

    private IEnumerator SpawnPortalNextFrame(Player player)
    {
        yield return null;
        if (player == null || ActiveDefinition == null || string.IsNullOrWhiteSpace(ActiveDefinition.PortalTargetDimensionId))
            yield break;

        ClearRuntimePortal();
        runtimePortal = new GameObject($"DimensionPortal_{ActiveDefinition.PortalTargetDimensionId}");
        runtimePortal.transform.position = player.transform.position + ActiveDefinition.PortalOffset;
        SceneManager.MoveGameObjectToScene(runtimePortal, SceneManager.GetActiveScene());

        SpriteRenderer renderer = runtimePortal.AddComponent<SpriteRenderer>();
        GameObject stonePrefab = GameRes.Instance?.GetPrefab("Mine_Stone", false);
        renderer.sprite = stonePrefab != null
            ? stonePrefab.GetComponentInChildren<SpriteRenderer>(true)?.sprite
            : null;
        renderer.color = ActiveDefinition.GenerationMode == DimensionGenerationMode.Cave
            ? new Color(0.35f, 0.85f, 1f, 1f)
            : new Color(0.72f, 0.35f, 1f, 1f);
        renderer.sortingOrder = 50;
        runtimePortal.transform.localScale = new Vector3(1.35f, 1.35f, 1f);

        CircleCollider2D collider = runtimePortal.AddComponent<CircleCollider2D>();
        collider.isTrigger = true;
        collider.radius = 0.75f;

        DimensionPortal portal = runtimePortal.AddComponent<DimensionPortal>();
        portal.Initialize(ActiveDefinition.PortalTargetDimensionId);
    }

    private void OnActiveSceneChanged(Scene oldScene, Scene newScene)
    {
        if (GameManager.Instance != null && GameManager.Instance.IsInGameWorld && newScene.IsValid())
            ActivateWorldFromScene(newScene.name);
    }

    private void ClearRuntimePortal()
    {
        if (runtimePortal != null)
            Destroy(runtimePortal);
        runtimePortal = null;
    }

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
