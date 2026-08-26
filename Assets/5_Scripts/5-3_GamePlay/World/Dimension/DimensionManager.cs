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

    private bool isTransitioning;

    public WorldAddress ActiveAddress { get; private set; }
    public DimensionDefinition ActiveDefinition { get; private set; }
    public bool IsTransitioning => isTransitioning;
    public static DimensionManager ExistingInstance => instance;

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

    public bool TryGetDefinition(string dimensionId, out DimensionDefinition definition)
    {
        LoadCatalog();
        definition = catalog?.Find(dimensionId);
        return definition != null;
    }

    public string GetActiveMapCorePrefabId()
    {
#pragma warning disable 0618 // 旧 ChunkMgr 兼容路径仍需读取旧 MapCore 配置；新版 WorldModel 不使用该入口。
        return string.IsNullOrWhiteSpace(ActiveDefinition?.MapCorePrefabId)
            ? "MapCore"
            : ActiveDefinition.MapCorePrefabId;
#pragma warning restore 0618
    }

    public ChunkGenerationProfileSO GetActiveGenerationProfile()
    {
        return ActiveDefinition?.GenerationProfile;
    }

    /// <summary>读取指定维度的生成 Profile；矿洞复核地表入口时使用，不返回 Unity 资源给后台线程。</summary>
    public ChunkGenerationProfileSO GetGenerationProfile(string dimensionId)
    {
        LoadCatalog();
        return catalog?.Find(dimensionId)?.GenerationProfile;
    }

    public ChunkView GetActiveChunkViewPrefab()
    {
        return ActiveDefinition?.ChunkViewPrefab;
    }

    public int GetActiveGenerationSeed(int baseSeed)
    {
        return GetGenerationSeed(baseSeed, ActiveAddress, ActiveDefinition);
    }

    /// <summary>按当前星球根计算另一维度的稳定生成种子，确保矿洞可复算地表实际地形。</summary>
    public int GetGenerationSeedForDimension(int baseSeed, string dimensionId)
    {
        LoadCatalog();
        WorldAddress source = ActiveAddress.IsValid
            ? ActiveAddress
            : WorldAddress.FromWorldKey(SceneManager.GetActiveScene().name);
        WorldAddress target = source.WithDimension(dimensionId);
        DimensionDefinition definition = catalog?.Find(target.DimensionId);
        return GetGenerationSeed(baseSeed, target, definition);
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

    public bool TryBeginTransition(Player player, string targetDimensionId)
    {
        return TryBeginTransition(player, targetDimensionId, null);
    }

    public bool TryBeginTransition(Player player, string targetDimensionId, Item sourcePortalItem)
    {
        return TryBeginTransitionInternal(player, targetDimensionId, sourcePortalItem,
            generatedWorldPortal: false);
    }

    /// <summary>从新版 ChunkView 生成的天然入口切换；目标维度使用同一世界格的确定性出口。</summary>
    public bool TryBeginGeneratedPortalTransition(Player player, string targetDimensionId,
        Item sourcePortalItem)
    {
        return TryBeginTransitionInternal(player, targetDimensionId, sourcePortalItem,
            generatedWorldPortal: true);
    }

    /// <summary>
    /// 玩家在非地表维度死亡时，复用完整世界切换事务返回指定地表出生点。
    /// 该入口使用精确坐标，不读取维度最后位置，也不套用传送门偏移。
    /// </summary>
    public bool TryBeginRespawnTransition(
        Player player,
        WorldAddress targetAddress,
        Vector3 targetPosition,
        Action preparePlayerForSave)
    {
        if (isTransitioning || player == null || player != ItemMgr.Instance?.User_Player ||
            preparePlayerForSave == null)
        {
            return false;
        }

        if (GameNetwork.IsOnline)
        {
            Debug.LogWarning("[DimensionManager] 当前联机版本暂不支持跨维度重生。");
            return false;
        }

        WorldAddress sourceAddress = ActiveAddress.IsValid
            ? ActiveAddress
            : WorldAddress.FromWorldKey(SceneManager.GetActiveScene().name);
        if (!targetAddress.IsValid || !targetAddress.IsSurface || targetAddress == sourceAddress ||
            !IsFinitePosition(targetPosition))
        {
            return false;
        }

        LoadCatalog();
        DimensionDefinition targetDefinition = catalog.Find(targetAddress.DimensionId);
        if (targetDefinition == null)
        {
            Debug.LogError($"[DimensionManager] 未注册重生目标维度：{targetAddress.DimensionId}");
            return false;
        }

        if (!GameManager.Instance.BeginDimensionTransitionLoading(targetDefinition))
            return false;

        try
        {
            // StartCoroutine 会同步运行到首个 yield；必须先恢复生命等权威数据，
            // 否则 ExecuteTransitionCoroutine 的首次 SavePlayer 会把死亡状态写回存档。
            preparePlayerForSave();
        }
        catch (Exception exception)
        {
            GameManager.Instance.FailDimensionTransitionLoading(
                "跨维度重生前恢复玩家状态失败。",
                exception);
            return false;
        }

        PortalTransitionContext respawnContext = new PortalTransitionContext
        {
            TargetPortalPosition = new Vector3(targetPosition.x, targetPosition.y, 0f),
            UseExactTargetPosition = true
        };
        StartCoroutine(TransitionCoroutine(
            player,
            sourceAddress,
            targetAddress,
            targetDefinition,
            respawnContext));
        return true;
    }

    private bool TryBeginTransitionInternal(Player player, string targetDimensionId,
        Item sourcePortalItem, bool generatedWorldPortal)
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

        if (!GameManager.Instance.BeginDimensionTransitionLoading(targetDefinition))
            return false;

        PortalTransitionContext portalContext = generatedWorldPortal
            ? ResolveGeneratedPortalTransition(sourcePortalItem)
            : ResolvePortalTransition(
                player.Data,
                sourceAddress,
                targetAddress,
                targetDefinition,
                sourcePortalItem);
        if (portalContext == null &&
            (generatedWorldPortal || IsMinePortalTransition(sourceAddress, targetAddress)))
        {
            GameManager.Instance.FailDimensionTransitionLoading(
                generatedWorldPortal ? "天然矿洞入口位置无效，无法切换维度。" :
                "矿坑入口锚点无效，无法切换维度。", null);
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
        string playerProfileName = sourcePlayer.ProfileName;
        if (string.IsNullOrWhiteSpace(playerProfileName))
            throw new InvalidOperationException("维度切换缺少稳定玩家档案名。");

        Vector3 sourcePosition = sourcePlayer.transform.position;
        GameController sourceController = sourcePlayer.GetComponentInChildren<GameController>(true);
        Mover sourceMover = sourcePlayer.itemMods?.GetMod_ByID<Mover>(ModText.Mover);
        TransitionState state = new TransitionState
        {
            PreserveRunState = sourceMover != null && sourceMover.IsRunning
        };
        sourceController?.SetGameplayInputLocked(true);

        // 必须等专属 Canvas 实际提交一帧后再保存、卸载场景，避免首帧穿帮。
        bool presentationWarningLogged = false;
        float presentationWarningAt = Time.realtimeSinceStartup + 12f;
        while (!GameManager.Instance.IsDimensionLoadingPresentationVisible)
        {
            if (!presentationWarningLogged && Time.realtimeSinceStartup >= presentationWarningAt)
            {
                presentationWarningLogged = true;
                Debug.LogWarning("[DimensionManager] 维度加载页尚未完成实例化，继续保持输入锁并等待。", this);
            }

            yield return null;
        }
        yield return new WaitForEndOfFrame();

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
            GameManager.Instance.SetDimensionTransitionLoading("维度切换失败，正在恢复原世界…", 0.1f);
            IEnumerator recoveryRoutine = RecoverAfterTransitionFailureCoroutine(
                playerData,
                playerProfileName,
                sourceAddress,
                sourcePosition,
                state.ExitNotified,
                state.EnterNotified,
                state.PreserveRunState);
            Exception recoveryFailure = null;
            while (true)
            {
                bool hasNext;
                object current = null;
                try
                {
                    hasNext = recoveryRoutine.MoveNext();
                    if (hasNext)
                        current = recoveryRoutine.Current;
                }
                catch (Exception exception)
                {
                    recoveryFailure = exception;
                    break;
                }

                if (!hasNext)
                    break;

                yield return current;
            }

            GameManager.Instance.FailDimensionTransitionLoading(
                recoveryFailure == null
                    ? "维度切换失败，已恢复到原世界。"
                    : "维度切换失败，原世界恢复也发生异常。",
                recoveryFailure ?? failure);
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
        string playerName = sourcePlayer.ProfileName;
        if (string.IsNullOrWhiteSpace(playerName))
            throw new InvalidOperationException("跨维度切换缺少稳定玩家档案名。");
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

        GameManager.Instance.SetDimensionTransitionLoading("正在生成目标区块…", 0.78f);
        yield return WaitForRuntimeChunkPresentation(targetAddress, targetPosition, targetPlayer);

        if (portalContext?.EnsureCaveExit == true)
        {
            GameManager.Instance.SetDimensionTransitionLoading("正在固定矿洞出口…", 0.88f);
            yield return EnsureCaveExitCoroutine(playerData, targetPlayer, targetDefinition, portalContext);
        }

        yield return null;
        targetPlayer.GetComponentInChildren<TileEffectReceiver>(true)?.RefreshCurrentTileEffects();

        GameManager.Instance.SetDimensionTransitionLoading("正在完成目标维度加载…", 0.96f);
        Physics2D.SyncTransforms();
        yield return new WaitForFixedUpdate();
        GameManager.Instance.CompleteDimensionTransitionLoading();

        targetPlayer.GetComponentInChildren<GameController>(true)?.SetGameplayInputLocked(false);
        RestorePlayerRunState(targetPlayer, state.PreserveRunState);
        ItemMgr.Instance.SavePlayer();
        SaveDataMgr.Instance.Save_And_WriteToDisk();
    }

    private sealed class TransitionState
    {
        public bool ExitNotified;
        public bool EnterNotified;
        public bool PreserveRunState;
    }

    private sealed class PortalTransitionContext
    {
        public DimensionPortalAnchor Anchor;
        public Vector3 TargetPortalPosition;
        public bool EnsureCaveExit;
        public bool UseExactTargetPosition;
    }

    private IEnumerator RecoverAfterTransitionFailureCoroutine(
        Data_Player playerData,
        string playerProfileName,
        WorldAddress sourceAddress,
        Vector3 sourcePosition,
        bool exitNotified,
        bool enterNotified,
        bool preserveRunState)
    {
        if (playerData == null)
            yield break;

        Player currentPlayer = ItemMgr.Instance?.User_Player;
        if (!exitNotified)
        {
            currentPlayer?.GetComponentInChildren<GameController>(true)?.SetGameplayInputLocked(false);
            RestorePlayerRunState(currentPlayer, preserveRunState);
            yield break;
        }

        if (enterNotified)
            GameManager.Instance.NotifyDimensionWorldExiting();

        if (currentPlayer != null)
            ItemMgr.Instance.ReleasePlayerForWorldTransition(currentPlayer);
        ChunkMgr.Instance?.OnSceneChange();

        Scene previousScene = SceneManager.GetActiveScene();
        Scene sourceScene = SceneManager.GetSceneByName(sourceAddress.WorldKey);
        if (!sourceScene.IsValid() || !sourceScene.isLoaded)
            sourceScene = SceneManager.CreateScene(sourceAddress.WorldKey);
        SceneManager.SetActiveScene(sourceScene);
        ActivateWorld(sourceAddress);

        if (previousScene.IsValid() && previousScene.isLoaded && previousScene != sourceScene)
        {
            AsyncOperation unloadOperation = SceneManager.UnloadSceneAsync(previousScene);
            while (unloadOperation != null && !unloadOperation.isDone)
                yield return null;
        }

        playerData.CurrentSceneName = sourceAddress.WorldKey;
        playerData.transform.position = sourcePosition;
        SaveDataMgr.Instance.SaveData.PlayerData_Dict[playerProfileName] = playerData;
        GameManager.Instance.NotifyDimensionWorldEntered();

        Player recoveredPlayer = ItemMgr.Instance.LoadPlayer(playerProfileName);
        recoveredPlayer.transform.position = sourcePosition;
        recoveredPlayer.Data.transform.position = sourcePosition;
        recoveredPlayer.GetComponentInChildren<GameController>(true)?.SetGameplayInputLocked(true);
        GameManager.Instance.NotifyDimensionPlayerEntered(recoveredPlayer);
        yield return WaitForRuntimeChunkPresentation(sourceAddress, sourcePosition, recoveredPlayer);

        recoveredPlayer.GetComponentInChildren<TileEffectReceiver>(true)?.RefreshCurrentTileEffects();
        recoveredPlayer.GetComponentInChildren<GameController>(true)?.SetGameplayInputLocked(false);
        RestorePlayerRunState(recoveredPlayer, preserveRunState);
        ItemMgr.Instance.SavePlayer();
        SaveDataMgr.Instance.Save_And_WriteToDisk();
    }

    /// <summary>维度切换完成后恢复玩家的奔跑开关；恢复必须在输入解锁后执行。</summary>
    private static void RestorePlayerRunState(Player player, bool shouldRun)
    {
        Mover mover = player?.itemMods?.GetMod_ByID<Mover>(ModText.Mover);
        mover?.SetRunState(shouldRun);
    }

    private void OnPlayerEnteredWorld(Player player)
    {
        if (player == null || player != ItemMgr.Instance?.User_Player)
            return;

        ActivateWorldFromScene(SceneManager.GetActiveScene().name);
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

    /// <summary>新版自然入口不写旧版玩家锚点；入口/出口由同格确定性生成保证配对。</summary>
    private static PortalTransitionContext ResolveGeneratedPortalTransition(Item sourcePortalItem)
    {
        if (sourcePortalItem == null)
            return null;
        Vector3 position = sourcePortalItem.transform.position;
        if (float.IsNaN(position.x) || float.IsInfinity(position.x) ||
            float.IsNaN(position.y) || float.IsInfinity(position.y) ||
            float.IsNaN(position.z) || float.IsInfinity(position.z))
        {
            return null;
        }

        return new PortalTransitionContext
        {
            TargetPortalPosition = position,
            EnsureCaveExit = false
        };
    }

    private static Vector3 ResolveTargetPosition(
        Data_Player playerData,
        WorldAddress targetAddress,
        DimensionDefinition targetDefinition,
        PortalTransitionContext portalContext)
    {
        if (portalContext != null)
        {
            return portalContext.UseExactTargetPosition
                ? portalContext.TargetPortalPosition
                : portalContext.TargetPortalPosition + targetDefinition.PortalOffset;
        }

        return DimensionTravelProgressStore.TryGetLastPosition(playerData, targetAddress, out Vector3 savedPosition)
            ? savedPosition
            : targetDefinition.DefaultSpawnPosition;
    }

    /// <summary>检查跨世界目标坐标是否可安全写入存档。</summary>
    private static bool IsFinitePosition(Vector3 position)
    {
        return !float.IsNaN(position.x) && !float.IsInfinity(position.x) &&
               !float.IsNaN(position.y) && !float.IsInfinity(position.y) &&
               !float.IsNaN(position.z) && !float.IsInfinity(position.z);
    }

    /// <summary>
    /// 维度场景切换后按玩家实际视距主动驱动新版 ChunkRuntime，并等待活动视野绑定完成。
    /// 不能用 1x1 的保底窗口覆盖 Mod_ChunkLoader 已经计算好的视野窗口。
    /// </summary>
    private static IEnumerator WaitForRuntimeChunkPresentation(WorldAddress targetAddress,
        Vector3 targetPosition, Player targetPlayer)
    {
        ChunkMgr chunkManager = ChunkMgr.Instance;
        if (chunkManager == null)
            throw new InvalidOperationException("维度切换后找不到 ChunkMgr。");

        Mod_ChunkLoader chunkLoader = targetPlayer?.GetComponentInChildren<Mod_ChunkLoader>(true);
        if (chunkLoader != null)
        {
            // 复用玩家自己的动态视距、预取距离和性能配置，避免切维度时只保留中心区块。
            chunkLoader.RefreshChunksForCameraView();
        }
        else
        {
            // 没有标准玩家区块加载模块时保留一个兼容兜底窗口，至少不影响传送目标落地。
            Debug.LogWarning("[DimensionManager] 目标玩家缺少 Mod_ChunkLoader，使用默认 3x3 维度窗口。", targetPlayer);
            chunkManager.RefreshRuntimeWindow(targetPosition, 2, 3,
                includeLocalPresentation: true, prefetchDistance: 3);
        }

        // 玩家脚下区块决定碰撞与实体能否安全落地；超时只诊断，绝不带病放行。
        bool centerWarningLogged = false;
        float centerWarningAt = Time.realtimeSinceStartup + 12f;
        while (!chunkManager.IsRuntimeEntityPresentationReady(targetPosition))
        {
            if (!centerWarningLogged && Time.realtimeSinceStartup >= centerWarningAt)
            {
                centerWarningLogged = true;
                Debug.LogWarning(
                    $"[DimensionManager] 目标维度玩家落地区块表现超过 12 秒，继续等待：{targetAddress.WorldKey}",
                    chunkManager);
            }

            yield return null;
        }

        // 只等待相机活动窗口；更远的低优先级预取不属于该判定。
        bool windowWarningLogged = false;
        float windowWarningAt = Time.realtimeSinceStartup + 12f;
        while (!chunkManager.AreRuntimeWindowPresentationsReady)
        {
            if (!windowWarningLogged && Time.realtimeSinceStartup >= windowWarningAt)
            {
                windowWarningLogged = true;
                Debug.LogWarning(
                    $"[DimensionManager] 目标维度可见窗口表现超过 12 秒，继续保持加载页：" +
                    $"{targetAddress.WorldKey}，待表现 {chunkManager.PendingRuntimeChunkPresentationCount}，" +
                    $"仍有后台生成 {chunkManager.HasPendingChunkDataLoads}。",
                    chunkManager);
            }

            yield return null;
        }

        Physics2D.SyncTransforms();
        yield return new WaitForFixedUpdate();
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
        SaveDataMgr.Instance.SaveData.PlayerData_Dict[targetPlayer.ProfileName] = playerData;
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

        // 兼容已经创建过的旧矿洞存档：旧数据可能因固定光照从未写入地表时间引用。
        if (!address.IsSurface &&
            saveData.DayTimeData.WorldTimeDict.TryGetValue(worldKey, out SerializableTimeData existingDimensionTime) &&
            existingDimensionTime != null &&
            string.IsNullOrEmpty(existingDimensionTime.ReferenceScene))
        {
            existingDimensionTime.ReferenceScene = address.PlanetId;
        }

        if (!saveData.DayTimeData.SceneLightingRateDict.ContainsKey(worldKey))
            saveData.DayTimeData.SceneLightingRateDict[worldKey] = 1f;
    }
}
