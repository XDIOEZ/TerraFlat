using System;
using System.Collections;
using System.Collections.Generic;
using FlatWorld.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;
using RuntimeChunk = FlatWorld.WorldModel.ChunkRuntime;
using TerrainCell = FlatWorld.WorldModel.TerrainCell;
using TerrainCellFlags = FlatWorld.WorldModel.TerrainCellFlags;

public sealed partial class DimensionManager : SingletonAutoMono<DimensionManager>
{
    private const string DefaultCatalogResourcePath = "Config/DimensionCatalog_Default";
    private const string CaveExitPrefabId = "CaveExit";

    [SerializeField] private DimensionCatalogSO catalog;

    private bool isTransitioning;
    private bool networkTransitionPending;

    private WorldAddress localAddress;
    private DimensionDefinition localDefinition;
    public WorldAddress ActiveAddress
    {
        get => GameplayWorldRegistry.HasExecutionContext ? WorldAddress.FromWorldKey(GameplayWorldRegistry.ExecutionWorldKey) : localAddress;
        private set => localAddress = value;
    }
    public DimensionDefinition ActiveDefinition
    {
        get => GameplayWorldRegistry.HasExecutionContext && TryGetDefinition(ActiveAddress.DimensionId, out DimensionDefinition definition) ? definition : localDefinition;
        private set => localDefinition = value;
    }
    public bool IsTransitioning => isTransitioning || networkTransitionPending;
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
        CancelLocalDimensionPresentation("维度管理器已停止。");
    }

    public void ActivateWorldFromScene(string worldKey)
    {
        if (!SpaceMgr.IsSpaceScene(worldKey)) ActivateWorld(WorldAddress.FromWorldKey(worldKey));
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
        definition = catalog?.FindForWorldKey(worldKey);
        return definition != null;
    }

    public bool TryGetDefinition(string dimensionId, out DimensionDefinition definition)
    {
        LoadCatalog();
        definition = catalog?.Find(dimensionId);
        return definition != null;
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
        if (IsTransitioning || localDimensionPresentation != null || player == null ||
            player != ItemMgr.Instance?.User_Player || preparePlayerForSave == null) return false;
        return NetworkDimensionBridge.RequestRespawn(player, targetAddress, targetPosition, preparePlayerForSave);
    }

    #region 星球落地事务
    public bool TryBeginPlanetLanding(Player player, WorldAddress targetAddress, Vector2 position)
    {
        if (IsTransitioning || localDimensionPresentation != null || player == null || player != ItemMgr.Instance?.User_Player ||
            !targetAddress.IsValid || !IsFinitePosition(position)) return false;
        LoadCatalog();
        bool toSpace = SpaceMgr.IsSpaceScene(targetAddress.WorldKey);
        DimensionDefinition definition = catalog.Find(targetAddress.DimensionId);
        if (definition == null) return false;
        if (GameNetwork.IsOnline)
        {
            // 联机恢复只迁移本机权威角色，禁止落入旧的全世界退出和重建事务。
            if (toSpace || !GameNetwork.HasStateAuthority) return false;
            if (!TryBeginLocalDimensionPresentation(player, targetAddress, definition)) return false;
            LocalDimensionPresentation presentation = localDimensionPresentation;
            networkTransitionPending = true;
            bool replied = false, acceptedReply = false;
            bool sent;
            try
            {
                sent = NetworkDimensionBridge.RequestExactLocal(player, targetAddress, position, (accepted, reason) =>
                {
                    replied = true; acceptedReply = accepted;
                    CompleteLocalDimensionRequest(presentation, accepted, reason);
                    if (!accepted && !string.IsNullOrWhiteSpace(reason)) Debug.LogWarning("[维度恢复] " + reason, this);
                });
            }
            catch
            {
                CompleteLocalDimensionRequest(presentation, false, "维度恢复请求未能发出。");
                throw;
            }
            if (!sent) CompleteLocalDimensionRequest(presentation, false, "维度恢复请求未能发出。");
            return sent && (!replied || acceptedReply);
        }
        if (!targetAddress.IsSurface ||
            !(toSpace ? GameManager.Instance.BeginSpaceTransitionLoading() : GameManager.Instance.BeginDimensionTransitionLoading(definition))) return false;
        StartCoroutine(TransitionCoroutine(player, WorldAddress.FromWorldKey(SceneManager.GetActiveScene().name),
            targetAddress, position));
        return true;
    }
    #endregion

    private bool TryBeginTransitionInternal(Player player, string targetDimensionId,
        Item sourcePortalItem, bool generatedWorldPortal)
    {
        // 已提交但呈现失败的租约只供快照重试，不能被当成新的入口请求重复使用。
        if (IsTransitioning || localDimensionPresentation != null || player == null || player != ItemMgr.Instance?.User_Player || sourcePortalItem?.itemData == null ||
            !TryGetDefinition(targetDimensionId, out DimensionDefinition definition)) return false;
        WorldAddress source = WorldAddress.FromWorldKey(GameplayWorldRegistry.GetItemWorldKey(player));
        if (!source.IsValid || source.DimensionId == targetDimensionId) return false;
        Mod_DimensionPortal portal = sourcePortalItem.GetComponentInChildren<Mod_DimensionPortal>(true);
        if (portal == null || !portal.ValidateNetworkTransition(player, source, targetDimensionId, generatedWorldPortal)) return false;
        if (!TryBeginLocalDimensionPresentation(player, source.WithDimension(targetDimensionId), definition)) return false;
        LocalDimensionPresentation presentation = localDimensionPresentation;

        // 请求等待也是迁移的一部分，不能在服务器准备目标期间再启动第二次请求。
        networkTransitionPending = true;
        bool replied = false, acceptedReply = false;
        bool sent;
        try
        {
            sent = NetworkDimensionBridge.RequestTransition(player, targetDimensionId,
                sourcePortalItem.itemData.Guid, generatedWorldPortal,
                (accepted, reason) =>
                {
                    replied = true; acceptedReply = accepted;
                    // 旧入口可能已随场景卸载销毁，必须使用 Unity 的存活判定而非 C# 空条件调用。
                    if (portal != null) portal.CancelNetworkTransitionRequest();
                    CompleteLocalDimensionRequest(presentation, accepted, reason);
                    if (!accepted && !string.IsNullOrWhiteSpace(reason)) Debug.LogWarning("[维度切换] " + reason, this);
                });
        }
        catch
        {
            CompleteLocalDimensionRequest(presentation, false, "维度切换请求未能发出。");
            throw;
        }
        if (!sent) CompleteLocalDimensionRequest(presentation, false, "维度切换请求未能发出。");
        return sent && (!replied || acceptedReply);
    }

    public void EnsureWorldData(WorldAddress address)
    {
        SaveDataMgr saveManager = SaveDataMgr.Instance;
        GameSaveData saveData = saveManager?.SaveData;
        if (saveData?.PlanetData_Dict == null || !address.IsValid)
            return;

        string worldKey = address.WorldKey;
        saveData.GetOrCreateWorldInstanceData(address, GameManager.Instance?.ReadyPlanetData);

        if (!address.IsSurface)
            EnsureWorldTimeData(saveData, address.WithDimension(WorldAddress.SurfaceDimensionId));
        EnsureWorldTimeData(saveData, address);

        // 联机切维度不触发 GameWorldEnter，须将新维度的存档时间同步到正在显示的昼夜系统。
        if (GameManager.Instance?.IsInGameWorld == true && DayTimeSystem.Instance is { } dayTime)
        {
            if (!address.IsSurface)
                dayTime.EnsureRuntimeSceneTimeData(address.RootWorldKey, saveData.DayTimeData);
            dayTime.EnsureRuntimeSceneTimeData(worldKey, saveData.DayTimeData);
        }
    }

    private IEnumerator TransitionCoroutine(
        Player sourcePlayer,
        WorldAddress sourceAddress,
        WorldAddress targetAddress,
        Vector3 targetPosition)
    {
        isTransitioning = true;
        Data_Player playerData = sourcePlayer.Data;
        string playerProfileName = sourcePlayer.ProfileName;
        if (string.IsNullOrWhiteSpace(playerProfileName))
            throw new InvalidOperationException("维度切换缺少稳定玩家档案名。");

        Vector3 sourcePosition = WorldTopologyRuntime.NormalizePosition(sourcePlayer.transform.position);
        Mod_GameController sourceController = sourcePlayer.GetComponentInChildren<Mod_GameController>(true);
        Mod_Mover sourceMover = sourcePlayer.itemMods?.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover);
        TransitionState state = new TransitionState
        {
            PreserveRunState = sourceMover != null && sourceMover.RunPreference
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
            targetPosition,
            state);
        Exception failure = null;

        yield return GameManager.RunWorldTransitionSteps(routine, exception => failure = exception);

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
            yield return GameManager.RunWorldTransitionSteps(recoveryRoutine, exception => recoveryFailure = exception);

            GameManager.Instance.FailDimensionTransitionLoading(
                recoveryFailure == null
                    ? "维度切换失败，已恢复到原世界。"
                    : "维度切换失败，原世界恢复也发生异常。",
                recoveryFailure == null ? failure : new AggregateException("维度切换与原世界恢复均失败。", failure, recoveryFailure));
        }

        isTransitioning = false;
    }

    private IEnumerator ExecuteTransitionCoroutine(
        Player sourcePlayer,
        WorldAddress sourceAddress,
        WorldAddress targetAddress,
        Vector3 targetPosition,
        TransitionState state)
    {
        Data_Player playerData = sourcePlayer.Data;
        string playerName = sourcePlayer.ProfileName;
        if (string.IsNullOrWhiteSpace(playerName))
            throw new InvalidOperationException("跨维度切换缺少稳定玩家档案名。");
        DimensionTravelProgressStore.SetLastPosition(playerData, sourceAddress, sourcePlayer.transform.position);
        sourcePlayer.GetComponentInChildren<Mod_TileEffectReceiver>(true)?.PrepareForWorldTransition();
        ItemMgr.Instance.SavePlayer();

        GameManager.Instance.SetDimensionTransitionLoading("正在保存当前维度…", 0.22f);
        // 先在原世界上下文解绑区块，再为单机太空往返保留可复用的表现壳。
        using (GameplayWorldRegistry.PreserveLocalChunkManagerForSpaceTransition(
                   SpaceMgr.IsSpaceScene(sourceAddress.WorldKey) || SpaceMgr.IsSpaceScene(targetAddress.WorldKey)))
            GameManager.Instance.NotifyDimensionWorldExiting();
        state.ExitNotified = true;
        SaveDataMgr.Instance.Save_And_WriteToDisk();

        ItemMgr.Instance.ReleasePlayerForWorldTransition(sourcePlayer);
        ChunkMgr.ExistingInstance?.OnSceneChange(
            SpaceMgr.IsSpaceScene(sourceAddress.WorldKey) || SpaceMgr.IsSpaceScene(targetAddress.WorldKey));
        yield return null;

        GameManager.Instance.SetDimensionTransitionLoading("正在创建目标维度…", 0.48f);
        bool toSpace = SpaceMgr.IsSpaceScene(targetAddress.WorldKey);
        if (!toSpace) EnsureWorldData(targetAddress);
        Scene oldScene = SceneManager.GetActiveScene();
        Scene targetScene = SceneManager.GetSceneByName(targetAddress.WorldKey);
        if (toSpace)
        {
            yield return GameManager.Instance.LoadSceneSingleAndInvokeWhenReady("SpaceScene");
            yield return SpaceMgr.Instance.LoadPresentation();
            targetScene = SceneManager.GetActiveScene();
        }
        else if (!targetScene.IsValid() || !targetScene.isLoaded)
            targetScene = WorldPhysicsScene2D.CreateIsolatedScene(targetAddress.WorldKey);

        SceneManager.SetActiveScene(targetScene);
        if (!toSpace) ActivateWorld(targetAddress);

        if (oldScene.IsValid() && oldScene.isLoaded && oldScene != targetScene)
        {
            AsyncOperation unloadOperation = SceneManager.UnloadSceneAsync(oldScene);
            while (unloadOperation != null && !unloadOperation.isDone)
                yield return null;
        }

        playerData.CurrentSceneName = targetAddress.WorldKey;
        playerData.transform.position = targetPosition;
        playerData.transform.rotation = Quaternion.identity;
        if (playerData.transform.scale == Vector3.zero)
            playerData.transform.scale = Vector3.one;
        SaveDataMgr.Instance.SaveData.PlayerData_Dict[playerName] = playerData;

        GameManager.Instance.SetDimensionTransitionLoading("正在加载目标维度…", 0.68f);
        GameManager.Instance.NotifyDimensionWorldEntered();
        state.EnterNotified = true;
        // 离线切换也要重新注册已加载地表，让落地准备继续使用目标世界的运行时。
        if (!toSpace) GameplayWorldRegistry.SelectLocalWorld(targetAddress.WorldKey);

        Player targetPlayer = ItemMgr.Instance.LoadPlayer(playerName);
        targetPlayer.transform.position = WorldLocalPresentation.ProjectPosition(targetPosition);
        targetPlayer.Data.transform.position = targetPosition;
        FlatWorld.Spaceflight.SpaceSession.Current?.RestorePassengerPose(targetPlayer);
        targetPlayer.GetComponentInChildren<Mod_GameController>(true)?.SetGameplayInputLocked(true);
        GameManager.Instance.NotifyDimensionPlayerEntered(targetPlayer);

        GameManager.Instance.SetDimensionTransitionLoading("正在生成目标区块…", 0.78f);
        if (toSpace) yield return SpaceMgr.Instance.PreparePresentationForCamera();
        else yield return WaitForRuntimeChunkPresentation(targetAddress, targetPosition, targetPlayer);

        yield return null;
        targetPlayer.GetComponentInChildren<Mod_TileEffectReceiver>(true)?.RefreshCurrentTileEffects();

        GameManager.Instance.SetDimensionTransitionLoading("正在完成目标维度加载…", 0.96f);
        Physics2D.SyncTransforms();
        yield return new WaitForFixedUpdate();
        GameManager.Instance.CompleteDimensionTransitionLoading();

        targetPlayer.GetComponentInChildren<Mod_GameController>(true)?.SetGameplayInputLocked(false);
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
            currentPlayer?.GetComponentInChildren<Mod_GameController>(true)?.SetGameplayInputLocked(false);
            RestorePlayerRunState(currentPlayer, preserveRunState);
            yield break;
        }

        if (enterNotified)
        {
            using (GameplayWorldRegistry.PreserveLocalChunkManagerForSpaceTransition(
                       SpaceMgr.IsSpaceScene(sourceAddress.WorldKey) || SpaceMgr.IsSpaceScene(SceneManager.GetActiveScene().name)))
                GameManager.Instance.NotifyDimensionWorldExiting();
        }

        if (currentPlayer != null)
            ItemMgr.Instance.ReleasePlayerForWorldTransition(currentPlayer);
        ChunkMgr.ExistingInstance?.OnSceneChange(
            SpaceMgr.IsSpaceScene(sourceAddress.WorldKey) || SpaceMgr.IsSpaceScene(SceneManager.GetActiveScene().name));

        Scene previousScene = SceneManager.GetActiveScene();
        Scene sourceScene = SceneManager.GetSceneByName(sourceAddress.WorldKey);
        bool fromSpace = SpaceMgr.IsSpaceScene(sourceAddress.WorldKey);
        if (fromSpace)
        {
            yield return GameManager.Instance.LoadSceneSingleAndInvokeWhenReady("SpaceScene");
            yield return SpaceMgr.Instance.LoadPresentation();
            sourceScene = SceneManager.GetActiveScene();
        }
        else if (!sourceScene.IsValid() || !sourceScene.isLoaded)
            sourceScene = WorldPhysicsScene2D.CreateIsolatedScene(sourceAddress.WorldKey);
        SceneManager.SetActiveScene(sourceScene);
        if (!fromSpace) ActivateWorld(sourceAddress);

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
        if (!fromSpace) GameplayWorldRegistry.SelectLocalWorld(sourceAddress.WorldKey);

        Player recoveredPlayer = ItemMgr.Instance.LoadPlayer(playerProfileName);
        recoveredPlayer.transform.position = WorldLocalPresentation.ProjectPosition(sourcePosition);
        recoveredPlayer.Data.transform.position = sourcePosition;
        recoveredPlayer.GetComponentInChildren<Mod_GameController>(true)?.SetGameplayInputLocked(true);
        GameManager.Instance.NotifyDimensionPlayerEntered(recoveredPlayer);
        if (fromSpace) yield return SpaceMgr.Instance.PreparePresentationForCamera();
        else yield return WaitForRuntimeChunkPresentation(sourceAddress, sourcePosition, recoveredPlayer);

        recoveredPlayer.GetComponentInChildren<Mod_TileEffectReceiver>(true)?.RefreshCurrentTileEffects();
        recoveredPlayer.GetComponentInChildren<Mod_GameController>(true)?.SetGameplayInputLocked(false);
        RestorePlayerRunState(recoveredPlayer, preserveRunState);
        ItemMgr.Instance.SavePlayer();
        SaveDataMgr.Instance.Save_And_WriteToDisk();
    }

    /// <summary>维度切换完成后恢复玩家的奔跑开关；恢复必须在输入解锁后执行。</summary>
    private static void RestorePlayerRunState(Player player, bool shouldRun)
    {
        Mod_Mover mover = player?.itemMods?.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover);
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
        WorldAddress sourceAddress,
        WorldAddress targetAddress,
        DimensionDefinition targetDefinition,
        Item sourcePortalItem,
        bool commitAnchor = true)
    {
        if (sourceAddress.IsSurface && targetAddress.DimensionId == WorldAddress.CaveDimensionId)
        {
            DimensionPortalAnchor anchor = DimensionTravelProgressStore.GetOrCreateCaveAnchor(
                SaveDataMgr.Instance.SaveData,
                sourceAddress,
                sourcePortalItem,
                targetAddress,
                targetDefinition, commitAnchor);
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
                    SaveDataMgr.Instance.SaveData,
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
        Vector3 position = WorldLocalPresentation.ToLogical(sourcePortalItem.transform.position);
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
        return portalContext?.TargetPortalPosition ??
            (DimensionTravelProgressStore.TryGetLastPosition(playerData, targetAddress, out Vector3 savedPosition)
                ? savedPosition : targetDefinition.DefaultSpawnPosition);
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
            chunkLoader.RefreshConfiguredChunkWindow();
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

    // 出口归目标世界所有，准备出口不会移动任何玩家或改变主机活动场景。
    private static void EnsureCaveExitForWorld(PortalTransitionContext context, Scene targetScene)
    {
        if (context?.Anchor == null || !targetScene.IsValid() || !targetScene.isLoaded ||
            GameplayWorldRegistry.ExecutionWorldKey != context.Anchor.CaveWorldKey)
            throw new InvalidOperationException("矿洞出口的目标世界不匹配。");
        Vector3 desiredPosition = context.TargetPortalPosition;
        EnsureCaveExitArea(desiredPosition);
        ItemMgr manager = ItemMgr.Instance;
        Item exit = manager.GetItemByGuid(context.Anchor.CaveExitGuid);
        if (exit != null && (exit.itemData?.IDName != CaveExitPrefabId || exit.gameObject.scene != targetScene))
            throw new InvalidOperationException($"矿洞出口 GUID 冲突：{context.Anchor.CaveExitGuid}。");
        if (exit == null)
        {
            exit = manager.InstantiateItemDeterministic(CaveExitPrefabId, context.Anchor.CaveExitGuid,
                WorldLocalPresentation.ProjectPosition(desiredPosition), Quaternion.identity, Vector3.one);
            if (exit == null) throw new InvalidOperationException("无法在目标维度创建 CaveExit。");
        }
        exit.transform.SetParent(null, true);
        if (exit.gameObject.scene != targetScene) SceneManager.MoveGameObjectToScene(exit.gameObject, targetScene);
        exit.transform.SetPositionAndRotation(WorldLocalPresentation.ProjectPosition(desiredPosition), Quaternion.identity);
        exit.transform.localScale = Vector3.one;
        exit.itemData.transform.position = desiredPosition;
        exit.itemData.transform.rotation = Quaternion.identity;
        exit.itemData.transform.scale = Vector3.one;
        exit.itemData.Stack.CanBePickedUp = false;
        if (!exit.IsInitialized) exit.Load();
        GameplayWorldRegistry.AssignItemToWorld(exit, context.Anchor.CaveWorldKey);
        manager.NotifyRuntimeItemMoved(exit);
        context.TargetPortalPosition = desiredPosition;
        DimensionTravelProgressStore.UpdateCaveExit(SaveDataMgr.Instance.SaveData, context.Anchor, exit);
    }

    /// <summary>
    /// 为手工矿洞入口对应的出口保证 3x3 干燥可行走区域。
    /// 只移除程序化洞壁/地下水，不覆盖玩家建筑占用或其它运行时阻挡。
    /// </summary>
    private static void EnsureCaveExitArea(Vector3 desiredPosition)
    {
        ChunkMgr chunkManager = ChunkMgr.Instance;
        if (chunkManager == null || chunkManager.ActiveGenerationProfile?.Settings == null)
            throw new InvalidOperationException("矿洞出口落地区域缺少 WorldModel 生成配置。");

        FlatWorld.WorldModel.ChunkGenerationSettingsSnapshot settings =
            chunkManager.ActiveGenerationProfile.Settings;
        if (settings.Mode != FlatWorld.WorldModel.ChunkGenerationMode.Cave)
            throw new InvalidOperationException("只能在洞穴 WorldModel 中整理矿洞出口区域。");

        Vector2Int centerCell = new(
            Mathf.FloorToInt(desiredPosition.x),
            Mathf.FloorToInt(desiredPosition.y));

        for (int offsetY = -1; offsetY <= 1; offsetY++)
        {
            for (int offsetX = -1; offsetX <= 1; offsetX++)
            {
                Vector2Int worldCell = new(centerCell.x + offsetX, centerCell.y + offsetY);
                Vector2 samplePosition = new(worldCell.x + 0.5f, worldCell.y + 0.5f);
                if (!chunkManager.TryGetRuntimeTerrainTile(samplePosition,
                        out RuntimeTerrainTileSample sample))
                {
                    throw new InvalidOperationException(
                        $"矿洞出口 3x3 区域尚未加载：{worldCell}。");
                }

                TerrainCell current = sample.Terrain.GetCell(sample.LocalCell.x, sample.LocalCell.y);
                bool isNaturalCaveWall = current.BlockingTileId == settings.CaveWallTileId;
                bool isGroundwater = sample.Terrain.GetLiquidDepth(sample.LocalCell.x, sample.LocalCell.y) > 0f;
                bool isPlayerOccupied = (current.Flags & TerrainCellFlags.Occupied) != 0;
                if (isPlayerOccupied || (!isNaturalCaveWall && !isGroundwater))
                    continue;

                TerrainCell carved = new(
                    settings.CaveFloorTileId,
                    0,
                    0,
                    current.BiomeId,
                    settings.DefaultNavigationCost,
                    TerrainCellFlags.Walkable);
                sample.Terrain.SetCell(sample.LocalCell.x, sample.LocalCell.y, carved);
                if (isGroundwater) WorldLiquidSystem.TrySet(sample, string.Empty, 0);

                if (!chunkManager.TryGetChunkRuntime(sample.Address, out RuntimeChunk runtimeChunk))
                    throw new InvalidOperationException($"矿洞出口区域所在 Chunk 已失效：{worldCell}。");

                SaveDataMgr.Instance?.RecordRuntimeTerrainChange(new TileBuildingCell(
                    runtimeChunk,
                    sample.WorldCell,
                    sample.LocalCell,
                    0,
                    null));
            }
        }

        Physics2D.SyncTransforms();
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

    private static void EnsureWorldTimeData(GameSaveData saveData, WorldAddress address)
    {
        saveData.DayTimeData ??= new DayTimeSaveData();
        saveData.DayTimeData.WorldTimeDict ??= new Dictionary<string, SerializableTimeData>();
        saveData.DayTimeData.SceneLightingRateDict ??= new Dictionary<string, float>();

        string worldKey = address.WorldKey;
        if (!saveData.DayTimeData.WorldTimeDict.ContainsKey(worldKey))
        {
            if (!address.IsSurface && saveData.DayTimeData.WorldTimeDict.TryGetValue(address.RootWorldKey, out SerializableTimeData surfaceTime))
            {
                SerializableTimeData dimensionTime = new SerializableTimeData(surfaceTime.ToTimeData())
                {
                    ReferenceScene = address.RootWorldKey
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
