using System;
using System.Collections;
using System.Collections.Generic;
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
        public readonly Data_Player Data;
        public readonly string Profile;
        public readonly Vector3 Position;
        public readonly int OriginalPlayerGuid;
        public readonly int OriginalPlayerInstance;
        public DimensionPortalAnchor Anchor;
        private readonly JToken originalNamespace;
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
            Data = player.Data; Profile = player.ProfileName;
            Position = WorldLocalPresentation.ToLogical(player.transform.position);
            OriginalPlayerGuid = player.itemData.Guid;
            OriginalPlayerInstance = player.GetInstanceID();
            originalNamespace = ItemSpecialDataJsonStore.ReadRoot(Data.ItemSpecialData)["flatworld.dimensions"]?.DeepClone();
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

        private void ReleaseActorControl()
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
            Item entrance = ItemMgr.Instance.InstantiateItem("MineEntrance", player.transform.position + Vector3.right * 2f);
            if (entrance == null) throw new InvalidOperationException("正式入口资源无法创建。");
            entranceGuid = entrance.itemData.Guid;
            entrance.Load();
            if (entrance.GetComponentInChildren<Mod_DimensionPortal>(true) == null)
                throw new InvalidOperationException("正式矿洞入口没有维度入口模块。");
            Anchor = DimensionTravelProgressStore.GetOrCreateCaveAnchor(Data, Surface, entrance, Cave, definition);
            if (Anchor == null) throw new InvalidOperationException("正式入口配对未建立。");
            return entrance;
        }

        public IEnumerator Restore()
        {
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
        }
    }
    #endregion

    #region 完整地表洞穴往返采样
    private static IEnumerator WaitForDimension(WorldAddress expected, string profile, DimensionScope controls)
    {
        double deadline = Time.realtimeSinceStartupAsDouble + 150d;
        while (true)
        {
            DimensionManager manager = DimensionManager.ExistingInstance;
            Player player = ItemMgr.Instance?.User_Player;
            if (player != null && player.ProfileName == profile && ReferenceEquals(player.Data, controls.Data))
                controls.ProtectActorControl(player);
            if (manager != null && !manager.IsTransitioning)
            {
                if (manager.ActiveAddress != expected)
                    throw new InvalidOperationException("正式维度事务结束，但实际目标地址不匹配。");
                if (player != null && player.ProfileName == profile && player.IsInitialized &&
                    GameManager.Instance?.IsGameplayReady == true &&
                    ChunkMgr.ExistingInstance?.AreRuntimeWindowPresentationsReady == true)
                {
                    GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
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
        return new JObject
        {
            ["address"] = manager.ActiveAddress.WorldKey, ["expected"] = expected.WorldKey,
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
            ["guid"] = player.itemData.Guid, ["instance"] = player.GetInstanceID()
        };
    }

    private static JObject CaptureRestoredDimensionControl(Player player, JObject args)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        return DimensionSample(player, DimensionManager.ExistingInstance.ActiveAddress);
    }

    private static IEnumerator CaptureDimension(Player player, JObject args, Action<JObject> completed)
    {
        var scope = new DimensionScope(player);
        GameplayTestRuntimeProbe.RegisterCleanup(scope);
        GameplayTestRuntimeProbe.RegisterAsyncCleanup(scope.Restore);
        var result = new JObject
        {
            ["profile"] = scope.Profile, ["originalGuid"] = scope.OriginalPlayerGuid,
            ["originalInstance"] = scope.OriginalPlayerInstance,
            ["sameDimensionRejected"] = !DimensionManager.ExistingInstance.TryBeginTransition(player, WorldAddress.SurfaceDimensionId)
        };
        Exception failure = null;
        IEnumerator Journey()
        {
            Item entrance = scope.PrepareEntrance(player);
            using (GameplayTestSpaceScenario.BeginExpectedMigration(player))
            {
                result["enterAccepted"] = DimensionManager.ExistingInstance.TryBeginTransition(player, WorldAddress.CaveDimensionId, entrance);
                result["enterMigrationExpected"] = GameplayTestSpaceScenario.IsMigrationExpected;
                if (!result.Value<bool>("enterAccepted")) throw new InvalidOperationException("正式入口拒绝进入洞穴。");
                result["concurrentRejected"] = !DimensionManager.ExistingInstance.TryBeginTransition(player, WorldAddress.CaveDimensionId, entrance);
                yield return WaitForDimension(scope.Cave, scope.Profile, scope);
            }
            result["enterMigrationReleased"] = !GameplayTestSpaceScenario.IsMigrationExpected;
            Player cavePlayer = ItemMgr.Instance.User_Player;
            result["cave"] = DimensionSample(cavePlayer, scope.Cave, scope);
            result["sameDataInCave"] = ReferenceEquals(cavePlayer.Data, scope.Data);
            Item exit = ItemMgr.Instance.GetItemByGuid(scope.Anchor.CaveExitGuid);
            result["exitCreated"] = exit != null && exit.IsInitialized && exit.itemData.IDName == "CaveExit";
            result["anchorFound"] = DimensionTravelProgressStore.TryGetAnchorByCaveExit(scope.Data, scope.Cave, exit, out var paired);
            result["anchorSurface"] = paired?.SurfaceWorldKey;
            result["exitModulePresent"] = exit?.GetComponentInChildren<Mod_DimensionPortal>(true) != null;
            using (GameplayTestSpaceScenario.BeginExpectedMigration(cavePlayer))
            {
                result["returnAccepted"] = DimensionManager.ExistingInstance.TryBeginTransition(cavePlayer, WorldAddress.SurfaceDimensionId, exit);
                result["returnMigrationExpected"] = GameplayTestSpaceScenario.IsMigrationExpected;
                if (!result.Value<bool>("returnAccepted")) throw new InvalidOperationException("正式配对出口拒绝返回地表。");
                yield return WaitForDimension(scope.Surface, scope.Profile, scope);
            }
            result["returnMigrationReleased"] = !GameplayTestSpaceScenario.IsMigrationExpected;
            Player returned = ItemMgr.Instance.User_Player;
            result["surface"] = DimensionSample(returned, scope.Surface, scope);
            result["sameDataAfterReturn"] = ReferenceEquals(returned.Data, scope.Data);
            result["returnNearEntrance"] = WorldTopologyRuntime.ShortestDelta(returned.transform.position, scope.Anchor.SurfaceEntrancePosition).magnitude;
            result["originalSurface"] = scope.Surface.WorldKey;
        }
        yield return GameManager.RunWorldTransitionSteps(Journey(), error => failure = error);
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
    #endregion
}
