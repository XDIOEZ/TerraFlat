using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using FlatWorld.Gameplay.Progress;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class GameplayTestNarrativeRuntimeProbe
{
    #region 维度回程控制诊断登记
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RegisterDimensionControlProbe() =>
        GameplayTestRuntimeProbe.Register("dimension", "restored_control", CaptureRestoredDimensionControl);
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterEditorDimensionControlProbe() => RegisterDimensionControlProbe();
#endif
    #endregion

    #region 真实矿洞迁移现场租约
    private sealed class DimensionScope : IDisposable
    {
        public readonly WorldAddress Surface;
        public readonly WorldAddress Cave;
        public readonly FlatWorld.WorldModel.WorldRuntime SurfaceRuntime;
        public readonly PlanetData SurfaceData;
        public readonly Data_Player Data;
        public readonly string Profile;
        public readonly Vector3 Position;
        public readonly int OriginalPlayerGuid;
        public readonly int OriginalPlayerInstance;
        public readonly int OriginalNetworkIdentity;
        public DimensionPortalAnchor Anchor;
        private readonly JToken originalNamespace;
        private readonly string originalWorldDimensionState;
        private readonly bool originalInvincibility;
        private int entranceGuid;
        private bool disposed;
        private readonly List<Mod_GameController> migrationControllers = new();

        public DimensionScope(Player player)
        {
            GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
            DimensionManager manager = DimensionManager.ExistingInstance ??
                throw new InvalidOperationException("正式维度管理器缺失。");
            Surface = manager.ActiveAddress;
            if (!Surface.IsSurface || manager.IsTransitioning)
                throw new InvalidOperationException("矿洞现场要求已完成入世的真实地表。");
            Cave = Surface.WithDimension(WorldAddress.CaveDimensionId);
            SurfaceRuntime = ChunkMgr.ExistingInstance.WorldRuntime;
            SurfaceData = SaveDataMgr.Instance.GetCurrentPlanetData();
            Data = player.Data; Profile = player.ProfileName;
            Position = WorldLocalPresentation.ToLogical(player.transform.position);
            OriginalPlayerGuid = player.itemData.Guid;
            OriginalPlayerInstance = player.GetInstanceID();
            OriginalNetworkIdentity = player.GetComponent("NetworkIdentity")?.GetInstanceID() ?? 0;
            originalNamespace = ItemSpecialDataJsonStore.ReadRoot(Data.ItemSpecialData)["flatworld.dimensions"]?.DeepClone();
            originalWorldDimensionState = SaveDataMgr.Instance.SaveData.DimensionStateJson;
            originalInvincibility = player.GetComponentInChildren<Mod_PlayerAdminController>(true)?.IsAdminInvincibilityEnabled == true;
        }

        public void ProtectActorControl(Player actor)
        {
            GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(actor);
            if (actor.ProfileName != Profile || !ReferenceEquals(actor.Data, Data))
                throw new InvalidOperationException("迁移控制不能接管其他档案或数据的玩家。");
            Mod_GameController controller = actor.GetComponentInChildren<Mod_GameController>(true) ??
                throw new InvalidOperationException("迁移玩家缺少正式输入控制器。");
            if (controller.IsExternalGameplayControlOwner(this)) return;
            if (controller.HasExternalGameplayControl)
            {
                if (controller.ExternalGameplayControlOwnerName == typeof(GameplayTestLuaRunner).FullName) return;
                throw new InvalidOperationException("迁移玩家被其他控制租约占用：" + controller.ExternalGameplayControlOwnerName);
            }
            // 新角色在完整窗口等待期间由本轮现场接管，避免加载锁解除后真人输入泄漏。
            if (!controller.TryAcquireExternalGameplayControl(this))
                throw new InvalidOperationException("无法取得本轮迁移玩家的临时测试控制。");
            migrationControllers.Add(controller);
        }

        public bool OwnsActorControl(Mod_GameController controller) => controller != null &&
            (controller.IsExternalGameplayControlOwner(this) ||
             controller.HasExternalGameplayControl && controller.ExternalGameplayControlOwnerName == typeof(GameplayTestLuaRunner).FullName);

        public void ReleaseActorControl()
        {
            foreach (Mod_GameController controller in migrationControllers)
                if (controller != null && controller.IsExternalGameplayControlOwner(this))
                    controller.ReleaseExternalGameplayControl(this);
            migrationControllers.Clear();
        }

        public Item PrepareEntrance(Player player)
        {
            DimensionManager manager = DimensionManager.ExistingInstance;
            if (!manager.TryGetDefinition(WorldAddress.CaveDimensionId, out DimensionDefinition definition))
                throw new InvalidOperationException("正式维度目录没有洞穴定义。");
            // 保留在正式交互半径内，避免玩家与服务器权威位置的微小偏差导致入口拒绝。
            Item entrance = ItemMgr.Instance.InstantiateItem("MineEntrance", player.transform.position + Vector3.right);
            if (entrance == null) throw new InvalidOperationException("正式入口资源无法创建。");
            entranceGuid = entrance.itemData.Guid;
            Mod_Building.SetInstalledDataState(entrance.itemData);
            entrance.Load();
            if (entrance.GetComponentInChildren<Mod_DimensionPortal>(true) == null)
                throw new InvalidOperationException("正式矿洞入口没有维度入口模块。");
            Anchor = DimensionTravelProgressStore.GetOrCreateCaveAnchor(SaveDataMgr.Instance.SaveData, Surface, entrance, Cave, definition);
            if (Anchor == null) throw new InvalidOperationException("正式入口配对未建立。");
            return entrance;
        }

        public IEnumerator Restore()
        {
            if (disposed) yield break;
            DimensionManager manager = DimensionManager.ExistingInstance;
            if (manager == null) throw new InvalidOperationException("恢复维度时正式管理器已丢失。");
            double deadline = Time.realtimeSinceStartupAsDouble + 150d;
            while (manager.IsTransitioning)
            {
                Player pendingActor = ItemMgr.Instance?.User_Player;
                if (pendingActor != null && pendingActor.ProfileName == Profile && ReferenceEquals(pendingActor.Data, Data))
                    ProtectActorControl(pendingActor);
                if (Time.realtimeSinceStartupAsDouble >= deadline)
                    throw new TimeoutException("维度事务仍在执行，无法安全启动回程。");
                yield return null;
            }
            Player current = ItemMgr.Instance?.User_Player;
            if (current == null || current.ProfileName != Profile)
                throw new InvalidOperationException("维度回程缺失本轮同档案角色。");
            if (manager.ActiveAddress != Surface)
            {
                // 仅在正式回程事务期间声明预期迁移，完成或失败都归还宿主租约。
                using (GameplayTestSpaceScenario.BeginExpectedMigration(current))
                {
                    if (!manager.TryBeginPlanetLanding(current, Surface, Position))
                        throw new InvalidOperationException("正式地表回程事务拒绝恢复。");
                    yield return WaitForDimension(Surface, Profile, this);
                }
            }
            current = ItemMgr.Instance.User_Player;
            ProtectActorControl(current);
            current.transform.position = WorldLocalPresentation.ProjectPosition(Position);
            current.Data.transform.position = Position;
            current.GetComponentInChildren<Mod_TileEffectReceiver>(true)?.RefreshCurrentTileEffects();
            Physics2D.SyncTransforms();
            Dispose();
        }

        public void Dispose()
        {
            ReleaseActorControl();
            if (disposed) return;
            DimensionManager manager = DimensionManager.ExistingInstance;
            // 强制中断时仍运行的迁移交由宿主退出世界收尾，不能覆盖正在提交的玩家数据。
            if (manager == null || manager.IsTransitioning || manager.ActiveAddress != Surface) return;
            disposed = true;
            ItemMgr items = ItemMgr.Instance;
            Item ownEntrance = entranceGuid == 0 ? null : items?.GetItemByGuid(entranceGuid);
            if (ownEntrance != null) items.DespawnItem(ownEntrance, false);
            Item ownExit = Anchor == null ? null : items?.GetItemByGuid(Anchor.CaveExitGuid);
            if (ownExit != null) items.DespawnItem(ownExit, false);
            JObject root = ItemSpecialDataJsonStore.ReadRoot(Data.ItemSpecialData);
            if (originalNamespace == null) root.Remove("flatworld.dimensions");
            else root["flatworld.dimensions"] = originalNamespace.DeepClone();
            Data.ItemSpecialData = root.ToString(Formatting.None);
            SaveDataMgr.Instance.SaveData.DimensionStateJson = originalWorldDimensionState;
            ItemMgr.Instance.User_Player?.GetComponentInChildren<Mod_PlayerAdminController>(true)
                ?.TrySetAdminInvincibilityEnabled(originalInvincibility);
        }
    }
    #endregion

    #region 完整地表洞穴往返采样
    private static IEnumerator WaitForDimension(WorldAddress expected, string profile, DimensionScope controls, JObject protection = null)
    {
        double deadline = Time.realtimeSinceStartupAsDouble + 150d;
        int frames = 0, unlockedFrames = 0, missingOwnerFrames = 0;
        while (true)
        {
            DimensionManager manager = DimensionManager.ExistingInstance;
            Player player = ItemMgr.Instance?.User_Player;
            if (player != null && player.ProfileName == profile && ReferenceEquals(player.Data, controls.Data))
                controls.ProtectActorControl(player);
            if (manager != null && manager.IsTransitioning && protection != null)
            {
                ++frames;
                Mod_GameController input = player != null ? player.GetComponentInChildren<Mod_GameController>(true) : null;
                if (input == null || !input.IsGameplayInputLocked) ++unlockedFrames;
                if (input == null || input.GameplayInputLockOwnerCount == 0) ++missingOwnerFrames;
            }
            if (manager != null && !manager.IsTransitioning)
            {
                if (manager.ActiveAddress != expected)
                {
                    var observer = default(FlatWorld.Spaceflight.SpaceNetworkObserver);
                    bool observerPresent = FlatWorld.Spaceflight.SpaceSession.Current?.TryGetNetworkObserver(profile, out observer) == true;
                    throw new InvalidOperationException($"正式维度事务结束，但实际目标地址不匹配：期望={expected.WorldKey}，" +
                        $"实际={manager.ActiveAddress.WorldKey}，玩家归属={GameplayWorldRegistry.GetItemWorldKey(player)}，" +
                        $"权威归属={(observerPresent ? observer.WorldKey : "未登记")}。");
                }
                if (player != null && player.ProfileName == profile && player.IsInitialized &&
                    GameManager.Instance?.IsGameplayReady == true &&
                    ChunkMgr.ExistingInstance?.AreRuntimeWindowPresentationsReady == true)
                {
                    GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
                    if (protection != null)
                    {
                        protection["frames"] = frames;
                        protection["unlockedFrames"] = unlockedFrames;
                        protection["missingOwnerFrames"] = missingOwnerFrames;
                    }
                    yield break;
                }
            }
            if (Time.realtimeSinceStartupAsDouble >= deadline)
                throw new TimeoutException("正式维度迁移没有在 150 秒内完成主角和完整区块窗口准备。");
            yield return null;
        }
    }

    private static JObject DimensionSample(Player player, WorldAddress expected, DimensionScope controls = null)
    {
        DimensionManager manager = DimensionManager.ExistingInstance;
        Mod_GameController controller = player.GetComponentInChildren<Mod_GameController>(true);
        PlanetData currentData = SaveDataMgr.Instance.GetCurrentPlanetData();
        FlatWorld.WorldModel.WorldRuntime currentRuntime = ChunkMgr.ExistingInstance?.WorldRuntime;
        var observer = default(FlatWorld.Spaceflight.SpaceNetworkObserver);
        bool observerPresent = FlatWorld.Spaceflight.SpaceSession.Current?.TryGetNetworkObserver(player.ProfileName, out observer) == true;
        Vector3 logicalPosition = WorldLocalPresentation.ToLogical(player.transform.position);
        double observerDistance = observerPresent && !observer.InSpace && observer.WorldKey == expected.WorldKey
            ? WorldTopologyRuntime.Distance(observer.Position.LocalMeters.ToVector2(), logicalPosition) : -1d;
        return new JObject
        {
            ["address"] = manager.ActiveAddress.WorldKey, ["expected"] = expected.WorldKey,
            ["playerWorld"] = GameplayWorldRegistry.GetItemWorldKey(player),
            ["observerPresent"] = observerPresent, ["observerWorld"] = observerPresent ? observer.WorldKey : null,
            ["observerInSpace"] = observerPresent && observer.InSpace, ["observerDistance"] = observerDistance,
            ["logicalX"] = logicalPosition.x, ["logicalY"] = logicalPosition.y,
            ["modelWorldKey"] = currentRuntime?.WorldId,
            ["generationMode"] = ChunkMgr.ExistingInstance?.ActiveGenerationProfile?.Settings.Mode.ToString(),
            ["originalRuntime"] = controls != null && ReferenceEquals(currentRuntime, controls.SurfaceRuntime),
            ["originalData"] = controls != null && ReferenceEquals(currentData, controls.SurfaceData),
            ["sourceDataRetained"] = controls != null && ReferenceEquals(controls.SurfaceData,
                SaveDataMgr.Instance.SaveData.PlanetData_Dict[controls.Surface.WorldKey]),
            ["scene"] = SceneManager.GetActiveScene().name, ["savedScene"] = player.Data.CurrentSceneName,
            ["profile"] = player.ProfileName, ["initialized"] = player.IsInitialized,
            ["windowReady"] = ChunkMgr.ExistingInstance?.AreRuntimeWindowPresentationsReady == true,
            ["transitioning"] = manager.IsTransitioning,
            ["controllerPresent"] = controller != null,
            ["inputLocked"] = controller == null || controller.IsGameplayInputLocked,
            ["inputExplicitlyLocked"] = controller == null || controller.IsGameplayInputExplicitlyLocked,
            ["inputLockOwnerCount"] = controller?.GameplayInputLockOwnerCount ?? 0,
            ["inputLockState"] = controller?.DescribeGameplayInputLockState(),
            ["loadingGateLocked"] = controller == null || controller.HasBlockingGameplayInputLock(_ => true),
            ["gameplayReady"] = GameManager.Instance?.IsGameplayReady == true,
            ["externalControlPresent"] = controller?.HasExternalGameplayControl == true,
            ["externalControlOwner"] = controller?.ExternalGameplayControlOwnerName,
            ["testControlOwned"] = controls != null ? controls.OwnsActorControl(controller) :
                controller?.HasExternalGameplayControl == true && controller.ExternalGameplayControlOwnerName == typeof(GameplayTestLuaRunner).FullName,
            ["suppressWeather"] = manager.ActiveDefinition != null && manager.ActiveDefinition.SuppressWeather,
            ["guid"] = player.itemData.Guid, ["instance"] = player.GetInstanceID(),
            ["networkIdentity"] = player.GetComponent("NetworkIdentity")?.GetInstanceID() ?? 0,
            ["sourceWorldRetained"] = controls == null || GameplayWorldRegistry.TryGet(controls.Surface.WorldKey, out _),
            ["targetRuntimePresent"] = GameplayWorldRegistry.TryGet(expected.WorldKey, out _),
            ["runtimeActive"] = GameplayWorldRegistry.TryGet(expected.WorldKey, out var runtime) &&
                runtime.Chunks != null && !runtime.Chunks.IsWorldRuntimeShuttingDown
        };
    }

    private static JObject CaptureRestoredDimensionControl(Player player, JObject args)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        return DimensionSample(player, DimensionManager.ExistingInstance.ActiveAddress);
    }

    // 在请求刚发出的同一帧采样，防止只检查到目标准备完成后的呈现锁。
    private static JObject DimensionRequestProtectionSample(Player player)
    {
        Mod_GameController controller = player.GetComponentInChildren<Mod_GameController>(true);
        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        double observerDistance = -1d;
        if (FlatWorld.Spaceflight.SpaceSession.Current?.TryGetNetworkObserver(player.ProfileName, out var observer) == true &&
            !observer.InSpace && observer.WorldKey == player.gameObject.scene.name)
            observerDistance = WorldTopologyRuntime.Distance(observer.Position.LocalMeters.ToVector2(),
                WorldLocalPresentation.ToLogical(player.transform.position));
        return new JObject
        {
            ["observerDistance"] = observerDistance,
            ["loadingVisible"] = GameManager.Instance.IsDimensionLoadingPresentationVisible,
            ["loadingTarget"] = GameManager.Instance.ActiveDimensionLoadingTargetId,
            ["inputLocked"] = controller?.IsGameplayInputLocked == true,
            ["inputOwners"] = controller?.GameplayInputLockOwnerCount ?? 0,
            ["bodyPresent"] = body != null,
            ["simulated"] = body != null && body.simulated,
            ["velocitySqr"] = body != null ? body.velocity.sqrMagnitude : 0f,
            ["running"] = player.itemMods?.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover)?.RunPreference == true
        };
    }

    private static IEnumerator CaptureDimension(Player player, JObject args, Action<JObject> completed)
    {
        var scope = new DimensionScope(player);
        GameplayTestRuntimeProbe.RegisterCleanup(scope);
        GameplayTestRuntimeProbe.RegisterAsyncCleanup(scope.Restore);
        var terrainRoundtrip = args.Value<bool?>("generationPresetAudit") == true
            ? new DimensionTerrainSaveRoundtrip(scope.Cave) : null;
        if (terrainRoundtrip != null) GameplayTestRuntimeProbe.RegisterCleanup(terrainRoundtrip);
        var result = new JObject
        {
            ["profile"] = scope.Profile, ["originalGuid"] = scope.OriginalPlayerGuid,
            ["originalInstance"] = scope.OriginalPlayerInstance,
            ["originalNetworkIdentity"] = scope.OriginalNetworkIdentity,
            ["sameDimensionRejected"] = !DimensionManager.ExistingInstance.TryBeginTransition(player, WorldAddress.SurfaceDimensionId),
            ["beforeEnter"] = DimensionRequestProtectionSample(player)
        };
        Exception failure = null;
        IEnumerator Journey()
        {
            Item entrance = scope.PrepareEntrance(player);
            result["entrance"] = DimensionPortalOwnershipSample(entrance);
            result["ecsResidentsBefore"] = GameplayTestRuntimeProbe.Capture(player, "dimension", "ecs_residents_begin", new JObject());
            using (GameplayTestSpaceScenario.BeginExpectedMigration(player))
            {
                result["enterAccepted"] = DimensionManager.ExistingInstance.TryBeginTransition(player, WorldAddress.CaveDimensionId, entrance);
                result["enterPending"] = DimensionRequestProtectionSample(player);
                result["enterMigrationExpected"] = GameplayTestSpaceScenario.IsMigrationExpected;
                if (!result.Value<bool>("enterAccepted")) throw new InvalidOperationException("正式入口拒绝进入洞穴。");
                result["concurrentRejected"] = !DimensionManager.ExistingInstance.TryBeginTransition(player, WorldAddress.CaveDimensionId, entrance);
                var protection = new JObject();
                result["enterProtection"] = protection;
                yield return WaitForDimension(scope.Cave, scope.Profile, scope, protection);
            }
            result["enterMigrationReleased"] = !GameplayTestSpaceScenario.IsMigrationExpected;
            Player cavePlayer = ItemMgr.Instance.User_Player;
            result["cave"] = DimensionSample(cavePlayer, scope.Cave, scope);
            result["afterEnter"] = DimensionRequestProtectionSample(cavePlayer);
            if (args.Value<bool?>("generationPresetAudit") == true)
            {
                result["presetAudit"] = CaptureLiveGenerationPresetOwnership(cavePlayer, scope);
                result["terrainSnapshot"] = terrainRoundtrip.CaptureSavedWorldAndChunk(cavePlayer, scope.Anchor.CaveExitPosition);
            }
            result["sameDataInCave"] = ReferenceEquals(cavePlayer.Data, scope.Data);
            result["ecsResidentsCave"] = GameplayTestRuntimeProbe.Capture(cavePlayer, "dimension", "ecs_residents_sample", new JObject());
            Item exit = ItemMgr.Instance.GetItemByGuid(scope.Anchor.CaveExitGuid);
            result["exit"] = DimensionPortalOwnershipSample(exit);
            result["exitCreated"] = exit != null && exit.IsInitialized && exit.itemData.IDName == "CaveExit";
            result["anchorFound"] = DimensionTravelProgressStore.TryGetAnchorByCaveExit(SaveDataMgr.Instance.SaveData, scope.Cave, exit, out var paired);
            result["anchorSurface"] = paired?.SurfaceWorldKey;
            result["exitModulePresent"] = exit?.GetComponentInChildren<Mod_DimensionPortal>(true) != null;
            result["exitApproach"] = PrepareDimensionPortalInteraction(cavePlayer, exit);
            using (GameplayTestSpaceScenario.BeginExpectedMigration(cavePlayer))
            {
                result["returnAccepted"] = DimensionManager.ExistingInstance.TryBeginTransition(cavePlayer, WorldAddress.SurfaceDimensionId, exit);
                result["returnPending"] = DimensionRequestProtectionSample(cavePlayer);
                result["returnMigrationExpected"] = GameplayTestSpaceScenario.IsMigrationExpected;
                if (!result.Value<bool>("returnAccepted")) throw new InvalidOperationException("正式配对出口拒绝返回地表。");
                var protection = new JObject();
                result["returnProtection"] = protection;
                yield return WaitForDimension(scope.Surface, scope.Profile, scope, protection);
            }
            result["returnMigrationReleased"] = !GameplayTestSpaceScenario.IsMigrationExpected;
            Player returned = ItemMgr.Instance.User_Player;
            result["surface"] = DimensionSample(returned, scope.Surface, scope);
            result["afterReturn"] = DimensionRequestProtectionSample(returned);
            result["sameDataAfterReturn"] = ReferenceEquals(returned.Data, scope.Data);
            result["ecsResidentsSurface"] = GameplayTestRuntimeProbe.Capture(returned, "dimension", "ecs_residents_sample", new JObject());
            result["returnNearEntrance"] = WorldTopologyRuntime.ShortestDelta(returned.transform.position, scope.Anchor.SurfaceEntrancePosition).magnitude;
            result["originalSurface"] = scope.Surface.WorldKey;
            if (terrainRoundtrip != null)
            {
                using (GameplayTestSpaceScenario.BeginExpectedMigration(returned))
                {
                    Item repeatEntrance = ItemMgr.Instance.GetItemByGuid(scope.Anchor.SurfaceEntranceGuid);
                    result["terrainReenterApproach"] = PrepareDimensionPortalInteraction(returned, repeatEntrance);
                    result["terrainReenterAccepted"] = DimensionManager.ExistingInstance.TryBeginTransition(
                        returned, WorldAddress.CaveDimensionId, repeatEntrance);
                    if (!result.Value<bool>("terrainReenterAccepted"))
                        throw new InvalidOperationException("检查地下地形时正式入口拒绝再次进入。");
                    yield return WaitForDimension(scope.Cave, scope.Profile, scope);
                }
                cavePlayer = ItemMgr.Instance.User_Player;
                result["terrainAfterReturn"] = terrainRoundtrip.CaptureCurrentWorldCell(cavePlayer);
                terrainRoundtrip.Dispose();
                result["terrainAfterCleanup"] = terrainRoundtrip.CaptureCurrentWorldCell(cavePlayer);
                using (GameplayTestSpaceScenario.BeginExpectedMigration(cavePlayer))
                {
                    Item repeatExit = ItemMgr.Instance.GetItemByGuid(scope.Anchor.CaveExitGuid);
                    result["terrainReturnApproach"] = PrepareDimensionPortalInteraction(cavePlayer, repeatExit);
                    result["terrainReturnAccepted"] = DimensionManager.ExistingInstance.TryBeginTransition(
                        cavePlayer, WorldAddress.SurfaceDimensionId, repeatExit);
                    if (!result.Value<bool>("terrainReturnAccepted"))
                        throw new InvalidOperationException("检查地下地形后正式出口拒绝返回。");
                    yield return WaitForDimension(scope.Surface, scope.Profile, scope);
                }
            }
        }
        yield return GameManager.RunWorldTransitionSteps(Journey(), error => failure = error);
        try { terrainRoundtrip?.Dispose(); }
        catch (Exception error) { failure = failure == null ? error : new AggregateException("迁移与地形恢复都失败。", failure, error); }
        Exception recoveryFailure = null;
        yield return GameManager.RunWorldTransitionSteps(scope.Restore(), error => recoveryFailure = error);
        if (recoveryFailure != null)
            throw failure == null ? recoveryFailure : new AggregateException("迁移与恢复都失败。", failure, recoveryFailure);
        if (failure != null) throw failure;
        Player restored = ItemMgr.Instance.User_Player;
        result["restored"] = DimensionManager.ExistingInstance.ActiveAddress == scope.Surface &&
            ReferenceEquals(restored.Data, scope.Data) &&
            WorldTopologyRuntime.ShortestDelta(WorldLocalPresentation.ToLogical(restored.transform.position), scope.Position).sqrMagnitude < .0001f;
        result["migrationReleased"] = !GameplayTestSpaceScenario.IsMigrationExpected;
        completed(result);
    }

    private sealed class DimensionTerrainSaveRoundtrip : IDisposable
    {
        private readonly WorldAddress world;
        private Vector2 markerPosition;
        private int markerTileId;
        private string markerBlockId;
        private bool modified;

        public DimensionTerrainSaveRoundtrip(WorldAddress world) => this.world = world;

        public JObject CaptureSavedWorldAndChunk(Player player, Vector2 exitPosition)
        {
            GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
            string playerWorld = GameplayWorldRegistry.GetItemWorldKey(player);
            if (playerWorld != world.WorldKey ||
                !GameplayWorldRegistry.TryGet(world.WorldKey, out GameplayWorldRuntime runtime) ||
                runtime.Chunks == null || runtime.Chunks.WorldRuntime?.WorldId != world.WorldKey)
                throw new InvalidOperationException($"地下地形采样世界不一致：期望={world.WorldKey}，玩家={playerWorld}，" +
                    $"本地管理器={ChunkMgr.LocalInstance?.OwnedWorldKey}，场景={SceneManager.GetActiveScene().name}。");

            using var context = GameplayWorldRegistry.UseWorld(world.WorldKey);
            ChunkMgr chunks = runtime.Chunks;
            Vector2Int center = Vector2Int.FloorToInt(WorldLocalPresentation.ToLogical(player.transform.position));
            Vector2Int exit = Vector2Int.FloorToInt(exitPosition);
            RuntimeTerrainTileSample sample = default;
            TileBuildingCell placed = default;
            int unloaded = 0, nearExit = 0, rejected = 0;
            string lastRejection = null;
            bool found = false;
            for (int radius = 2; radius <= 12 && !found; ++radius)
            for (int dy = -radius; dy <= radius && !found; ++dy)
            for (int dx = -radius; dx <= radius; ++dx)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != radius) continue;
                Vector2Int cell = WorldTopologyRuntime.NormalizeCell(center + new Vector2Int(dx, dy));
                Vector2Int fromExit = WorldTopologyRuntime.ShortestDelta(exit, cell);
                if (Math.Abs(fromExit.x) <= 1 && Math.Abs(fromExit.y) <= 1) { ++nearExit; continue; }
                Vector2 position = (Vector2)cell + Vector2.one * .5f;
                if (!chunks.TryGetRuntimeTerrainTile(position, out sample)) { ++unloaded; continue; }
                string block = sample.Terrain.GetLiquidLevel(sample.LocalCell.x, sample.LocalCell.y) > 0
                    ? "Tile_WaterPlatform" : "Tile_Floor_Wood";
                bool accepted;
                string reason;
                try { accepted = TileBuildingSystem.TryPlace(position, block, out placed, out reason); }
                finally
                {
                    if (placed.ReplacedGroundCell.HasValue)
                    {
                        markerPosition = (Vector2)placed.Position + Vector2.one * .5f;
                        markerTileId = placed.RuntimeTileId;
                        markerBlockId = block;
                        modified = true;
                    }
                }
                if (!accepted)
                {
                    ++rejected;
                    lastRejection = $"格={sample.WorldCell}，区块维度={sample.Address.DimensionId}，原因={reason}";
                    continue;
                }
                found = true;
                break;
            }
            if (!found)
                throw new InvalidOperationException($"地下地形没有可铺设的已加载格：世界={world.WorldKey}，" +
                    $"玩家={center}，出口={exit}，未加载={unloaded}，出口范围={nearExit}，铺设拒绝={rejected}，最后拒绝={lastRejection}。");
            int expectedCost = sample.Terrain.TryGetEnvironmentValue(FlatWorld.WorldModel.TerrainSupportLayer.CostLayer,
                sample.LocalCell.x, sample.LocalCell.y, out float supportCost) ? (int)supportCost : 0;
            SaveDataMgr saves = SaveDataMgr.Instance;
            saves.Save_And_WriteToDisk();
            byte[] archive = File.ReadAllBytes(saves.GetFullSavePath(saves.SaveData.saveName));
            var shell = new GameObject("[LuaDimensionSaveReadProbe]");
            shell.SetActive(false);
            shell.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                SaveDataMgr reader = shell.AddComponent<SaveDataMgr>();
                GameSaveData restored = reader.ReadTestArchiveWithoutPublishing(archive, out var records);
                PlanetData restoredWorld = restored.PlanetData_Dict[world.WorldKey];
                var origin = new Vector2Int(sample.Address.ChunkOrigin.X, sample.Address.ChunkOrigin.Y);
                ChunkSaveRecord chunk = null;
                int worldChunkCount = 0;
                foreach (ChunkSaveRecord record in records.Values)
                {
                    if (record.PlanetName != world.WorldKey) continue;
                    ++worldChunkCount;
                    if (record.ChunkPosition == origin) chunk = record;
                }
                SupportCellSaveData support = chunk?.SupportCells?.Find(value => value.LocalPosition == sample.LocalCell);
                return new JObject
                {
                    ["world"] = restoredWorld.Name, ["bodyId"] = restoredWorld.BodyId,
                    ["presetId"] = restoredWorld.GenerationPresetId,
                    ["frozenProfile"] = restoredWorld.Ecology.Generation.ProfileId,
                    ["chunkCount"] = worldChunkCount, ["chunkWorld"] = chunk?.PlanetName,
                    ["playerWorld"] = playerWorld, ["managerWorld"] = chunks.OwnedWorldKey,
                    ["modelWorld"] = chunks.WorldRuntime.WorldId, ["chunkDimension"] = sample.Address.DimensionId,
                    ["cellX"] = sample.WorldCell.x, ["cellY"] = sample.WorldCell.y,
                    ["blockId"] = markerBlockId,
                    ["savedTile"] = support?.TileId ?? 0, ["savedCost"] = support?.NavigationCost ?? 0,
                    ["expectedTile"] = markerTileId, ["expectedCost"] = expectedCost,
                    ["underlyingGround"] = sample.Terrain.GetCell(sample.LocalCell.x, sample.LocalCell.y).GroundTileId
                };
            }
            finally { UnityEngine.Object.DestroyImmediate(shell); }
        }

        public JObject CaptureCurrentWorldCell(Player player)
        {
            using var context = GameplayWorldRegistry.UseWorld(world.WorldKey);
            if (!ChunkMgr.ExistingInstance.TryGetRuntimeTerrainTile(markerPosition,
                    out RuntimeTerrainTileSample sample))
                throw new InvalidOperationException("回到地下后无法读取地形现场。");
            bool hasCost = sample.Terrain.TryGetEnvironmentValue(FlatWorld.WorldModel.TerrainSupportLayer.CostLayer,
                sample.LocalCell.x, sample.LocalCell.y, out float cost);
            return new JObject
            {
                ["world"] = GameplayWorldRegistry.GetItemWorldKey(player),
                ["chunkDimension"] = sample.Address.DimensionId,
                ["tile"] = FlatWorld.WorldModel.TerrainSupportLayer.GetTileId(sample.Terrain, sample.LocalCell.x, sample.LocalCell.y),
                ["cost"] = hasCost ? (int)cost : 0,
                ["underlyingGround"] = sample.Terrain.GetCell(sample.LocalCell.x, sample.LocalCell.y).GroundTileId
            };
        }

        public void Dispose()
        {
            if (!modified || !GameplayWorldRegistry.TryGet(world.WorldKey, out GameplayWorldRuntime runtime)) return;
            using (GameplayWorldRegistry.UseWorld(world.WorldKey))
            {
                if (!runtime.Chunks.TryGetRuntimeTerrainTile(markerPosition, out RuntimeTerrainTileSample sample) ||
                    !runtime.Chunks.TryGetChunkRuntime(sample.Address, out var chunk))
                    throw new InvalidOperationException("地下地形恢复时区块已卸载。");
                var placed = new TileBuildingCell(chunk, sample.WorldCell, sample.LocalCell, markerTileId, markerBlockId,
                    sample.Terrain.GetCell(sample.LocalCell.x, sample.LocalCell.y));
                if (!TileBuildingSystem.TryRemove(placed, false, out string reason))
                    throw new InvalidOperationException("无法恢复地下测试地形：" + reason);
                modified = false;
            }
        }
    }

    private static JObject PrepareDimensionPortalInteraction(Player player, Item portal)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        string world = GameplayWorldRegistry.GetItemWorldKey(player);
        string portalWorld = portal != null ? GameplayWorldRegistry.GetItemWorldKey(portal) : null;
        var session = FlatWorld.Spaceflight.SpaceSession.Current;
        if (world == null || world != portalWorld || session == null ||
            !session.TryGetNetworkObserver(player.ProfileName, out var observer) || observer.InSpace || observer.WorldKey != world)
            throw new InvalidOperationException("准备入口交互时玩家、观察者或入口世界不一致。");

        using var context = GameplayWorldRegistry.UseWorld(world);
        Mod_InteractSender sender = player.GetComponentInChildren<Mod_InteractSender>(true);
        float reach = sender != null ? sender.maxInteractDistance : Mod_InteractSender.DefaultMaxInteractDistance;
        Vector2 portalPosition = WorldLocalPresentation.ToLogical(portal.transform.position);
        Vector2 playerPosition = WorldLocalPresentation.ToLogical(player.transform.position);
        float actorDistance = WorldTopologyRuntime.Distance(playerPosition, portalPosition);
        float observerDistance = WorldTopologyRuntime.Distance(observer.Position.LocalMeters.ToVector2(), portalPosition);
        float before = observerDistance;
        bool teleported = false;
        if (Mathf.Max(actorDistance, observerDistance) > reach * .75f)
        {
            // 正式传送为隔离玩家准备入口附近的合法交互位置，不改变生产距离规则。
            Vector2 direction = WorldTopologyRuntime.ShortestDelta(portalPosition, playerPosition).normalized;
            if (direction == Vector2.zero) direction = Vector2.right;
            bool completed = false, succeeded = false;
            bool sent = PlayerTeleportService.TryTeleportToSurface(player, portalPosition + direction * (reach * .45f),
                value => { completed = true; succeeded = value; });
            if (!sent || !completed || !succeeded)
                throw new InvalidOperationException("正式传送无法将测试玩家带回入口附近。");
            teleported = true;
        }
        if (!session.TryGetNetworkObserver(player.ProfileName, out observer))
            throw new InvalidOperationException("准备入口交互后缺少玩家权威观察者。");
        return new JObject
        {
            ["world"] = world, ["portalWorld"] = portalWorld, ["observerWorld"] = observer.WorldKey,
            ["inSpace"] = observer.InSpace, ["reach"] = reach, ["before"] = before, ["teleported"] = teleported,
            ["actorDistance"] = WorldTopologyRuntime.Distance(WorldLocalPresentation.ToLogical(player.transform.position), portalPosition),
            ["observerDistance"] = WorldTopologyRuntime.Distance(observer.Position.LocalMeters.ToVector2(), portalPosition)
        };
    }

    private static JObject DimensionPortalOwnershipSample(Item portal)
    {
        return new JObject
        {
            ["registered"] = portal != null && portal.itemData != null &&
                ReferenceEquals(ItemMgr.Instance.GetItemByGuid(portal.itemData.Guid), portal),
            ["world"] = portal != null ? GameplayWorldRegistry.GetItemWorldKey(portal) : null,
            ["scene"] = portal != null ? portal.gameObject.scene.name : null
        };
    }
    #endregion

    #region 火星地下引用共享地表预设的真实往返
    private static IEnumerator CaptureMarsGenerationPresetRoundtrip(Player player, JObject args,
        Action<JObject> completed)
    {
        var home = new DimensionScope(player);
        GameplayTestRuntimeProbe.RegisterCleanup(home);
        GameplayTestRuntimeProbe.RegisterAsyncCleanup(home.Restore);
        if (!GameplayTestGm.TryExecute(player, "enable_invincibility", null, out string message))
            throw new InvalidOperationException("火星环境现场无法开启正式管理员保护：" + message);
        var session = FlatWorld.Spaceflight.SpaceSession.EnsureLoaded();
        var marsBody = session.Universe.GetBody("mars");
        WorldAddress marsAddress = marsBody.SurfaceAddress;
        var result = new JObject { ["homeWorld"] = home.Surface.WorldKey, ["marsWorld"] = marsAddress.WorldKey,
            ["expectedMarsBody"] = marsBody.BodyId, ["marsTemplateBody"] = marsBody.TemplateBodyId };
        session.EnsureNetworkPassenger(player);
        result["beforeLanding"] = DimensionSample(player, home.Surface, home);
        var exitSaves = new JArray();
        void CapturePlayerUnregisteredAfterWorldExitSave(Item item)
        {
            if (item is not Player leaving || leaving.ProfileName != home.Profile || !ReferenceEquals(leaving.Data, home.Data)) return;
            ChunkMgr selected = ChunkMgr.LocalInstance;
            exitSaves.Add(new JObject
            {
                ["world"] = GameplayWorldRegistry.GetItemWorldKey(leaving),
                ["selectedManager"] = selected != null,
                ["selectedWorld"] = selected != null ? selected.OwnedWorldKey : null,
                ["runtimeWorld"] = selected != null ? selected.WorldRuntime?.WorldId : null
            });
        }
        Exception failure = null;
        IEnumerator Journey()
        {
            DimensionManager manager = DimensionManager.ExistingInstance;
            using (GameplayTestSpaceScenario.BeginExpectedMigration(player))
            {
                if (!manager.TryBeginPlanetLanding(player, marsAddress, new Vector2(.5f, .5f)))
                    throw new InvalidOperationException("火星正式落地事务拒绝测试入场。");
                yield return WaitForDimension(marsAddress, home.Profile, home);
            }
            Player marsPlayer = ItemMgr.Instance.User_Player;
            result["landing"] = DimensionSample(marsPlayer, marsAddress, home);
            result["landingSavedPassenger"] = CaptureSavedSurfacePassenger(home.Profile);
            PlanetData mars = SaveDataMgr.Instance.GetCurrentPlanetData();
            result["marsBodyBefore"] = mars.BodyId;
            result["marsPresetBefore"] = mars.GenerationPresetId;
            manager.EnsureWorldData(marsAddress.WithDimension(WorldAddress.CaveDimensionId));
            PlanetData underground = SaveDataMgr.Instance.SaveData.PlanetData_Dict[
                marsAddress.WithDimension(WorldAddress.CaveDimensionId).WorldKey];
            if (underground.Ecology.HasGenerationConfiguration)
                throw new InvalidOperationException("火星地下预设现场要求尚未生成的本轮独立世界。");
            underground.GenerationPresetId = "core:surface.earth";
            underground.GenerationNumericOverrides["testing.presetNumber"] = .375d;
            underground.GenerationTextOverrides["testing.presetLabel"] = "火星地下地球地形";
            home.ReleaseActorControl();
            yield return CaptureDimension(marsPlayer, new JObject { ["generationPresetAudit"] = true },
                sample => result["journey"] = sample);
            result["marsBodyAfter"] = mars.BodyId;
            result["marsPresetAfter"] = mars.GenerationPresetId;
            result["undergroundRetained"] = ReferenceEquals(underground,
                SaveDataMgr.Instance.SaveData.PlanetData_Dict[underground.Name]);
            PlanetData loaded = MemoryPack.MemoryPackSerializer.Deserialize<PlanetData>(
                MemoryPack.MemoryPackSerializer.Serialize(underground));
            result["savedWorld"] = loaded.Name;
            result["savedBody"] = loaded.BodyId;
            result["savedPreset"] = loaded.GenerationPresetId;
            result["savedNumber"] = loaded.GenerationNumericOverrides["testing.presetNumber"];
            result["savedText"] = loaded.GenerationTextOverrides["testing.presetLabel"];
            result["savedFrozenProfile"] = loaded.Ecology.Generation.ProfileId;
            result["savedFrozenConfiguration"] = loaded.Ecology.HasGenerationConfiguration;
        }
        Exception recoveryFailure = null;
        ItemMgr.RuntimeItemUnregistered += CapturePlayerUnregisteredAfterWorldExitSave;
        try
        {
            yield return GameManager.RunWorldTransitionSteps(Journey(), error => failure = error);
            yield return GameManager.RunWorldTransitionSteps(home.Restore(), error => recoveryFailure = error);
        }
        finally { ItemMgr.RuntimeItemUnregistered -= CapturePlayerUnregisteredAfterWorldExitSave; }
        if (recoveryFailure != null)
            throw failure == null ? recoveryFailure : new AggregateException("火星现场及原世界恢复都失败。", failure, recoveryFailure);
        if (failure != null) throw failure;
        result["restoredHome"] = DimensionManager.ExistingInstance.ActiveAddress == home.Surface;
        result["home"] = DimensionSample(ItemMgr.Instance.User_Player, home.Surface, home);
        result["homeSavedPassenger"] = CaptureSavedSurfacePassenger(home.Profile);
        result["sameHomePlayerData"] = ReferenceEquals(ItemMgr.Instance.User_Player.Data, home.Data);
        result["exitSaves"] = exitSaves;
        completed(result);
    }

    private static JObject CaptureSavedSurfacePassenger(string profile)
    {
        var saved = SaveDataMgr.DeserializeSpaceState(SaveDataMgr.Instance.SaveData.SpaceStateJson)
            .Passengers.Find(passenger => passenger.ProfileId == profile);
        return new JObject
        {
            ["present"] = saved != null, ["world"] = saved?.LandingWorldKey,
            ["inSpace"] = saved?.IsInSpace == true,
            ["x"] = saved?.FreeMotion.PositionMeters.X ?? 0d,
            ["y"] = saved?.FreeMotion.PositionMeters.Y ?? 0d
        };
    }

    private static JObject CaptureLiveGenerationPresetOwnership(Player player, DimensionScope scope)
    {
        PlanetData world = SaveDataMgr.Instance.GetCurrentPlanetData();
        ChunkMgr chunks = ChunkMgr.ExistingInstance;
        FlatWorld.WorldModel.ChunkGenerationProfileSnapshot profile = chunks.ActiveGenerationProfile;
        FlatWorld.WorldModel.ChunkGenerationProfileSnapshot earth = FlatWorld.Spaceflight.SpaceCatalog.LoadDefault()
            .CreateGenerationProfile("core:surface.earth");
        if (!chunks.TryGetRuntimeTerrainTile(player.transform.position, out RuntimeTerrainTileSample sample))
            throw new InvalidOperationException("火星地下完整窗口没有真实脚下区块。");
        DayTimeSystem.Instance.WorldTimeDict.TryGetValue(scope.Cave.WorldKey, out TimeData clock);
        DimensionManager manager = DimensionManager.ExistingInstance;
        return new JObject
        {
            ["world"] = world.Name, ["bodyId"] = world.BodyId,
            ["presetId"] = world.GenerationPresetId, ["profileId"] = profile.ProfileId,
            ["atmosphereProfileId"] = world.AtmosphereProfileId,
            ["pressureKPa"] = AtmosphereService.PressureKPa(world.Atmosphere),
            ["sourcePressureKPa"] = AtmosphereService.PressureKPa(scope.SurfaceData.Atmosphere),
            ["profileTemperature"] = profile.NumericParameters["space.surfaceTemperatureCelsius"],
            ["earthTemperature"] = earth.NumericParameters["space.surfaceTemperatureCelsius"],
            ["groundTileId"] = profile.Settings.GroundTileId, ["earthGroundTileId"] = earth.Settings.GroundTileId,
            ["overrideNumber"] = profile.NumericParameters["testing.presetNumber"],
            ["overrideText"] = profile.TextParameters["testing.presetLabel"],
            ["sourceHasOverride"] = scope.SurfaceData.GenerationNumericOverrides.ContainsKey("testing.presetNumber") ||
                scope.SurfaceData.GenerationTextOverrides.ContainsKey("testing.presetLabel"),
            ["seed"] = manager.GetGenerationSeed(SaveDataMgr.Instance.SaveData.Seed, scope.Cave),
            ["sourceSeed"] = manager.GetGenerationSeed(SaveDataMgr.Instance.SaveData.Seed, scope.Surface),
            ["chunkDimension"] = sample.Address.DimensionId,
            ["terrainMode"] = sample.Terrain.GenerationMode.ToString(),
            ["caveLayerPresent"] = sample.Terrain.TryGetEnvironmentValue("caveDirtGround", sample.LocalCell.x,
                sample.LocalCell.y, out _),
            ["terrainHash"] = sample.Terrain.ComputeStableHash().ToString(),
            ["independentMaps"] = !ReferenceEquals(world.MapData_Dict, scope.SurfaceData.MapData_Dict),
            ["independentEcology"] = !ReferenceEquals(world.Ecology, scope.SurfaceData.Ecology),
            ["independentAtmosphere"] = !ReferenceEquals(world.Atmosphere, scope.SurfaceData.Atmosphere),
            ["timeReference"] = clock?.ReferenceScene,
            ["lightingFromPortals"] = manager.ActiveDefinition.FadeLightingFromPortals,
            ["fixedLighting"] = manager.ActiveDefinition.UseFixedLighting,
            ["lightingCap"] = manager.ActiveDefinition.FixedLighting,
            ["suppressWeather"] = manager.ActiveDefinition.SuppressWeather,
            ["frozenOverrides"] = CaptureFrozenWorldPresetParameterOverrides(earth)
        };
    }

    private static JObject CaptureFrozenWorldPresetParameterOverrides(
        FlatWorld.WorldModel.ChunkGenerationProfileSnapshot sharedEarth)
    {
        var data = new PlanetData { Name = "standalone:preset-overrides__dimension__cave",
            GenerationPresetId = "core:surface.earth", ChunkSize = new Vector2Int(4, 4) };
        data.GenerationNumericOverrides["river.enabled"] = 0d;
        data.GenerationNumericOverrides["lake.large.enabled"] = 0d;
        data.GenerationNumericOverrides["lake.lava.enabled"] = 0d;
        data.GenerationNumericOverrides["structure.enabled"] = 0d;
        data.GenerationNumericOverrides["cave.portal.enabled"] = 0d;
        data.GenerationNumericOverrides["climate.distance.oceanRadiusChunks"] = 1d;
        data.GenerationNumericOverrides["climate.distance.freshwaterRadiusChunks"] = 1d;
        data.GenerationNumericOverrides["airHumidity.radiusTiles"] = 1d;
        data.GenerationNumericOverrides["terrain.seaLevel"] = .31d;
        data.GenerationTextOverrides["terrain.mode"] = "surface";
        var first = ChunkMgr.CreateFrozenWorldGenerationProfile(data);
        bool frozenAfterFirst = data.Ecology.HasGenerationConfiguration;
        data.GenerationNumericOverrides["terrain.seaLevel"] = .43d;
        data.GenerationTextOverrides["terrain.mode"] = "cave";
        var changed = ChunkMgr.CreateFrozenWorldGenerationProfile(data);
        var generator = new FlatWorld.WorldModel.DeterministicChunkGenerator(GameRes.RequireWorldLiquidTypes());
        try
        {
            return new JObject
            {
                ["frozenAfterFirst"] = frozenAfterFirst,
                ["firstPreset"] = first.ProfileId, ["changedPreset"] = changed.ProfileId,
                ["firstSeaLevel"] = first.NumericParameters["terrain.seaLevel"],
                ["changedSeaLevel"] = changed.NumericParameters["terrain.seaLevel"],
                ["firstMode"] = first.Settings.Mode.ToString(), ["changedMode"] = changed.Settings.Mode.ToString(),
                ["firstTerrain"] = GameplayTestSystemProbe.CaptureGenerationPresetTerrain(generator, first, "cave-after-freeze"),
                ["changedTerrain"] = GameplayTestSystemProbe.CaptureGenerationPresetTerrain(generator, changed, "garden-after-freeze"),
                ["sharedPresetUnchanged"] = sharedEarth.GenerationFingerprint == FlatWorld.Spaceflight.SpaceCatalog.LoadDefault()
                    .CreateGenerationProfile("core:surface.earth").GenerationFingerprint
            };
        }
        finally { generator.BeginNewWorldHydrologyEpoch(1); }
    }
    #endregion
}
