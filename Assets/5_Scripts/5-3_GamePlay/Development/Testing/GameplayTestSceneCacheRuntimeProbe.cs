using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using FlatWorld.Networking;
using FlatWorld.Spaceflight;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>在同一隔离档案内采样地表和星区表现对象的真实往返复用。</summary>
public static class GameplayTestSceneCacheRuntimeProbe
{
    #region 白名单入口
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RegisterRuntime() => Register();
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterEditor() => Register();
#endif

    private static void Register() => GameplayTestRuntimeProbe.RegisterAsync(
        "space", "presentation_cache_roundtrip", CaptureRoundtrip);

    private static IEnumerator CaptureRoundtrip(Player player, JObject args, Action<JObject> completed)
    {
        if (args == null || args.Count != 1 ||
            !args.TryGetValue("cacheEnabled", out JToken enabled) || enabled.Type != JTokenType.Boolean)
            throw new ArgumentException("场景表现缓存往返只接受布尔参数 cacheEnabled。");
        if (GameNetwork.IsOnline || !GameNetwork.HasStateAuthority || Time.timeScale <= 0f)
            throw new InvalidOperationException("场景表现缓存往返需要未暂停的单机权威测试世界。");
        var fixture = new RoundtripFixture(player, enabled.Value<bool>());
        // 先登记恢复流程，再让真实维度迁移改变玩家与场景。
        GameplayTestRuntimeProbe.RegisterCleanup(GameplayTestSpaceScenario.BeginExpectedMigration(player));
        GameplayTestRuntimeProbe.RegisterCleanup(fixture);
        GameplayTestRuntimeProbe.RegisterAsyncCleanup(fixture.Cleanup);
        yield return fixture.Execute(completed);
    }
    #endregion

    #region 同档案真实维度往返
    private sealed class RoundtripFixture : IDisposable
    {
        private readonly string profile;
        private readonly Data_Player playerData;
        private readonly GameSaveData saveData;
        private readonly global::WorldAddress home;
        private readonly Vector2 homePosition;
        private readonly SpaceSession session;
        private readonly SpaceSessionState sessionState;
        private readonly SpaceSectorId sector = default;
        private readonly string bodyId;
        private readonly SpaceVector2 emptyPoint;
        private readonly bool initialInvincibility;
        private readonly bool cacheEnabled;
        private ChunkMgr cacheChunks;
        private SpaceMgr cacheSpace;
        private bool previousChunkCaching;
        private bool previousSectorCaching;
        private bool cacheModeApplied;
        private object previousWorldRuntime;
        private bool changedWorld;
        private bool protectionChanged;

        public RoundtripFixture(Player player, bool cacheEnabled)
        {
            GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
            if (SpaceMgr.IsSpaceScene(SceneManager.GetActiveScene().name) ||
                GameManager.Instance?.IsGameplayReady != true ||
                DimensionManager.ExistingInstance?.IsTransitioning == true)
                throw new InvalidOperationException("缓存往返必须从已经就绪的隔离地表开始。");
            profile = player.ProfileName;
            playerData = player.Data;
            saveData = SaveDataMgr.Instance.SaveData;
            initialInvincibility = player.GetComponentInChildren<Mod_PlayerAdminController>(true)?
                .IsAdminInvincibilityEnabled == true;
            this.cacheEnabled = cacheEnabled;
            home = global::WorldAddress.FromWorldKey(SceneManager.GetActiveScene().name);
            homePosition = WorldLocalPresentation.ToLogical((Vector2)player.transform.position);
            session = SpaceSession.EnsureLoaded();
            sessionState = session?.State ?? throw new InvalidOperationException("太空会话尚未载入。");
            UniverseSimulation universe = session.GetUniverse(sector);
            if (universe?.State?.Bodies == null || universe.State.Bodies.Count == 0)
                throw new InvalidOperationException("主星区没有可观察的星体表现。");
            bodyId = universe.State.Bodies[0].BodyId;
            emptyPoint = FindEmptyPoint(universe);
        }

        public IEnumerator Execute(Action<JObject> completed)
        {
            ChunkMgr chunks = ChunkMgr.ExistingInstance ?? throw new InvalidOperationException("区块管理器缺失。");
            // 地表首次入场时星区管理器按需创建，测试也走正式单例入口。
            SpaceMgr space = SpaceMgr.Instance ?? throw new InvalidOperationException("星区表现管理器无法创建。");
            ApplyCacheMode(chunks, space);
            try
            {
                ProtectCurrentPlayer();
                JObject initial = SurfaceSnapshot(chunks, space);
                var rounds = new JArray();
                for (int index = 0; index < (cacheEnabled ? 2 : 1); index++)
                {
                    JObject before = index == 0 ? initial : (JObject)rounds[index - 1]["surface"];
                    var wall = Stopwatch.StartNew();
                    yield return EnterSpace();
                    yield return WaitSimulation(.2d);
                    yield return WaitUntil(() => space.FindPlanetByBodyId(bodyId) != null &&
                        space.TryGetSectorPresentationDebugState(sector, out _, out bool retained, out bool active) &&
                        !retained && active, 30d, "星体和星区表现未完成入场。");
                    JObject inSpace = SpaceSnapshot(chunks, space);
                    double enterWallSeconds = wall.Elapsed.TotalSeconds;

                    wall.Restart();
                    yield return ReturnHome();
                    yield return WaitSimulation(.2d);
                    yield return WaitUntil(() => chunks.AreRuntimeWindowPresentationsReady &&
                        (cacheEnabled
                            ? space.TryGetSectorPresentationDebugState(sector, out _, out bool retained, out bool active) &&
                              retained && !active
                            : !space.TryGetSectorPresentationDebugState(sector, out _, out _, out _) &&
                              space.FindPlanetByBodyId(bodyId) == null),
                        30d, "地表窗口或星区表现未完成回收。");
                    JObject onSurface = SurfaceSnapshot(chunks, space);
                    double returnWallSeconds = wall.Elapsed.TotalSeconds;
                    rounds.Add(new JObject
                    {
                        ["before"] = before.DeepClone(), ["space"] = inSpace, ["surface"] = onSurface,
                        ["enterWallSeconds"] = enterWallSeconds,
                        ["returnWallSeconds"] = returnWallSeconds,
                        ["wallSeconds"] = enterWallSeconds + returnWallSeconds
                    });
                }
                completed(new JObject
                {
                    ["initial"] = initial, ["rounds"] = rounds,
                    ["cacheEnabled"] = cacheEnabled,
                    ["chunkCapacity"] = ChunkMgr.SceneTransitionChunkViewCapacity,
                    ["sectorCapacity"] = SpaceMgr.SectorPresentationCacheCapacity,
                    ["profile"] = profile, ["bodyId"] = bodyId
                });
            }
            finally
            {
                RestoreCacheMode();
            }
        }

        public IEnumerator Cleanup()
        {
            if (!changedWorld || !Application.isPlaying) yield break;
            yield return ReturnHome();
        }

        public void Dispose()
        {
            RestoreCacheMode();
            if (!protectionChanged || ItemMgr.Instance?.User_Player?.ProfileName != profile) return;
            Mod_PlayerAdminController admin = ItemMgr.Instance.User_Player
                .GetComponentInChildren<Mod_PlayerAdminController>(true);
            if (admin?.IsAdministrator == true) admin.TrySetAdminInvincibilityEnabled(initialInvincibility);
        }

        private void ApplyCacheMode(ChunkMgr chunks, SpaceMgr space)
        {
            cacheChunks = chunks;
            cacheSpace = space;
            previousChunkCaching = chunks.SceneTransitionChunkViewCachingEnabled;
            previousSectorCaching = space.SectorPresentationCachingEnabled;
            cacheModeApplied = true;
            chunks.SceneTransitionChunkViewCachingEnabled = cacheEnabled;
            space.SectorPresentationCachingEnabled = cacheEnabled;
        }

        private void RestoreCacheMode()
        {
            if (!cacheModeApplied) return;
            cacheModeApplied = false;
            // 每条采样命令结束即恢复开关，后续命令不会受前一轮清理租约覆盖。
            if (cacheChunks != null) cacheChunks.SceneTransitionChunkViewCachingEnabled = previousChunkCaching;
            if (cacheSpace != null) cacheSpace.SectorPresentationCachingEnabled = previousSectorCaching;
        }

        private void ProtectCurrentPlayer()
        {
            if (!GameplayTestGm.TryExecute(CurrentPlayer(), "enable_invincibility", null, out string message))
                throw new InvalidOperationException(message);
            protectionChanged = true;
        }

        private IEnumerator EnterSpace()
        {
            Player player = CurrentPlayer();
            bool finished = false, succeeded = false;
            changedWorld = true;
            var motion = new SpaceMotionState
            {
                Sector = sector, PositionMeters = emptyPoint,
                VelocityMetersPerSecond = new SpaceVector2(0d, 0d), RadiusMeters = .3d
            };
            if (!session.TryTeleportPlayerToSpace(player, motion, result =>
                { succeeded = result; finished = true; }))
                throw new InvalidOperationException("正式太空传送入口拒绝缓存往返测试。");
            yield return WaitUntil(() => finished && Ready() &&
                SceneManager.GetActiveScene().name == SpaceSectorCoordinates.SceneKey(sector),
                120d, "太空入场或隔离玩家重绑超时。");
            if (!succeeded || !session.IsSpaceView)
                throw new InvalidOperationException("正式太空迁移没有成功。");
            CurrentPlayer();
            ProtectCurrentPlayer();
        }

        private IEnumerator ReturnHome()
        {
            yield return WaitUntil(Ready, 120d, "恢复地表前隔离玩家未就绪。");
            if (SceneManager.GetActiveScene().name != home.WorldKey)
            {
                // 活着的测试玩家通过正式落地事务回程，不能借用死亡重生入口。
                Player traveller = CurrentPlayer();
                SpacePassengerState passenger = session.GetPassenger(traveller);
                bool wasInSpace = passenger.IsInSpace;
                passenger.IsInSpace = false;
                if (!DimensionManager.Instance.TryBeginPlanetLanding(traveller, home, homePosition))
                {
                    passenger.IsInSpace = wasInSpace;
                    throw new InvalidOperationException("正式回地表迁移被拒绝。");
                }
                yield return WaitUntil(() => Ready() && SceneManager.GetActiveScene().name == home.WorldKey,
                    120d, "正式回地表或隔离玩家重绑超时。");
                session.ResetPassengerForRespawn(CurrentPlayer());
            }
            else
            {
                session.ResetPassengerForRespawn(CurrentPlayer());
                Camera camera = Camera.main;
                Mod_PlayerTraits traits = CurrentPlayer().itemMods.GetMod_ByID<Mod_PlayerTraits>(Mod_PlayerTraits.ModuleId);
                if (camera == null || traits == null || !traits.TryTeleportToScreenPosition(
                    camera.WorldToScreenPoint(WorldLocalPresentation.ProjectPosition(homePosition))))
                    throw new InvalidOperationException("缓存往返清理无法恢复地表玩家位置。");
            }
            CurrentPlayer();
            changedWorld = false;
        }

        private Player CurrentPlayer()
        {
            Player player = ItemMgr.Instance?.User_Player;
            if (player == null || player.ProfileName != profile || !ReferenceEquals(player.Data, playerData) ||
                !ReferenceEquals(SaveDataMgr.Instance?.SaveData, saveData))
                throw new InvalidOperationException("迁移没有重绑同一隔离档案与 Data_Player。");
            GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
            return player;
        }

        private bool Ready() => GameManager.Instance?.IsGameplayReady == true &&
            DimensionManager.ExistingInstance?.IsTransitioning != true &&
            ItemMgr.Instance?.User_Player?.ProfileName == profile;

        private IEnumerator WaitSimulation(double seconds)
        {
            double target = session.State.SimulationSeconds + seconds;
            yield return WaitUntil(() => session.State.SimulationSeconds >= target, 20d,
                "往返期间宇宙固定时钟未推进。");
        }

        private static IEnumerator WaitUntil(Func<bool> condition, double seconds, string message)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + seconds;
            while (!condition())
            {
                if (!Application.isPlaying) throw new OperationCanceledException(message);
                if (Time.realtimeSinceStartupAsDouble >= deadline) throw new TimeoutException(message);
                yield return null;
            }
        }

        private static SpaceVector2 FindEmptyPoint(UniverseSimulation universe)
        {
            foreach (SpaceVector2 point in new[]
            {
                new SpaceVector2(100000d, 100000d), new SpaceVector2(-100000d, 100000d),
                new SpaceVector2(100000d, -100000d), new SpaceVector2(-100000d, -100000d)
            })
            {
                bool clear = true;
                foreach (BodyState body in universe.State.Bodies)
                    if ((point - body.PositionMeters).Magnitude <= body.RadiusMeters + 100d)
                    { clear = false; break; }
                if (clear) return point;
            }
            throw new InvalidOperationException("主星区没有安全的测试入场点。");
        }
    #endregion

    #region 只读表现采样
        private JObject SurfaceSnapshot(ChunkMgr chunks, SpaceMgr space)
        {
            Player player = CurrentPlayer();
            var world = chunks.WorldRuntime;
            if (world == null) throw new InvalidOperationException("回地表后没有当前 WorldRuntime。");
            var views = new JArray();
            int bound = 0, currentModels = 0;
            foreach (ChunkView view in UnityEngine.Object.FindObjectsOfType<ChunkView>())
            {
                if (view == null || view.Model == null ||
                    view.gameObject.scene != SceneManager.GetActiveScene()) continue;
                bound++;
                bool current = chunks.TryGetChunkRuntime(view.Model.Address, out ChunkRuntime model) &&
                    ReferenceEquals(model, view.Model);
                if (current) currentModels++;
                views.Add(view.GetInstanceID());
            }
            bool footFound = chunks.TryGetRuntimeChunkView((Vector2)player.transform.position, out ChunkView foot);
            bool sectorFound = space.TryGetSectorPresentationDebugState(sector,
                out int sectorRootId, out bool retained, out bool active);
            Planet retainedPlanet = null;
            int activeSectorColliders = 0;
            if (sectorFound && retained)
            {
                foreach (Transform candidate in space.GetComponentsInChildren<Transform>(true))
                    if (candidate.GetInstanceID() == sectorRootId)
                    {
                        foreach (Planet planet in candidate.GetComponentsInChildren<Planet>(true))
                            if (planet.planetData?.BodyId == bodyId) retainedPlanet = planet;
                        activeSectorColliders = CountActiveColliders(candidate);
                        break;
                    }
            }
            bool priorRuntimeReused = ReferenceEquals(previousWorldRuntime, world);
            previousWorldRuntime = world;
            bool registeredCurrentManager = GameplayWorldRegistry.TryGet(home.WorldKey, out GameplayWorldRuntime registered) &&
                ReferenceEquals(registered.Chunks, chunks);
            bool observerFound = session.TryGetNetworkObserver(player.ProfileName, out SpaceNetworkObserver observer);
            Vector2 logicalPosition = WorldLocalPresentation.ToLogical((Vector2)player.transform.position);
            return new JObject
            {
                ["scene"] = SceneManager.GetActiveScene().name,
                ["profile"] = player.ProfileName,
                ["samePlayerData"] = ReferenceEquals(player.Data, playerData),
                ["sameSaveData"] = ReferenceEquals(SaveDataMgr.Instance.SaveData, saveData),
                ["sameSessionState"] = ReferenceEquals(session.State, sessionState),
                ["gameplayReady"] = GameManager.Instance.IsGameplayReady,
                ["worldActive"] = chunks.IsWorldModelRuntimeActive,
                ["chunkManagerId"] = chunks.GetInstanceID(),
                ["registeredCurrentManager"] = registeredCurrentManager,
                ["observerFound"] = observerFound, ["observerInSpace"] = observer.InSpace,
                ["observerWorld"] = observer.WorldKey,
                ["observerPositionError"] = Vector2.Distance(logicalPosition, observer.Position.LocalMeters.ToVector2()),
                ["worldId"] = world.WorldId, ["worldEpoch"] = world.Epoch,
                ["priorRuntimeReused"] = priorRuntimeReused,
                ["windowReady"] = chunks.AreRuntimeWindowPresentationsReady,
                ["boundViewCount"] = bound, ["currentModelCount"] = currentModels,
                ["viewIds"] = views, ["footViewId"] = footFound ? foot.GetInstanceID() : 0,
                ["chunkHits"] = chunks.RuntimeChunkViewTransitionHits,
                ["chunkCreated"] = chunks.RuntimeChunkViewCreatedCount,
                ["chunkRetained"] = chunks.RetainedSceneTransitionChunkViewCount,
                ["sectorFound"] = sectorFound, ["sectorRetained"] = retained,
                ["sectorActive"] = active, ["sectorRootId"] = sectorRootId,
                ["planetInstanceId"] = retainedPlanet == null ? 0 : retainedPlanet.GetInstanceID(),
                ["sectorActiveColliders"] = activeSectorColliders,
                ["sectorRetainedCount"] = space.RetainedSectorPresentationCount,
                ["sectorHits"] = space.SectorPresentationCacheHits,
                ["sectorCreated"] = space.SectorPresentationCreatedCount,
                ["simulationSeconds"] = session.State.SimulationSeconds
            };
        }

        private JObject SpaceSnapshot(ChunkMgr chunks, SpaceMgr space)
        {
            Player player = CurrentPlayer();
            Planet planet = space.FindPlanetByBodyId(bodyId);
            bool sectorFound = space.TryGetSectorPresentationDebugState(sector,
                out int sectorRootId, out bool retained, out bool active);
            Transform pool = chunks.transform.Find("SceneTransitionChunkViewPool");
            var cachedViews = new JArray();
            if (pool != null)
                foreach (ChunkView view in pool.GetComponentsInChildren<ChunkView>(true))
                {
                    view.TryGetTerrainBatchDebugState(out bool terrainRegistered, out int terrainVisuals);
                    cachedViews.Add(new JObject
                    {
                        ["id"] = view.GetInstanceID(), ["bound"] = view.IsBound || view.IsBinding,
                        ["modelPresent"] = view.Model != null,
                        ["active"] = view.gameObject.activeInHierarchy,
                        ["activeColliders"] = CountActiveColliders(view.transform),
                        ["terrainRegistered"] = terrainRegistered, ["terrainVisuals"] = terrainVisuals
                    });
                }
            return new JObject
            {
                ["scene"] = SceneManager.GetActiveScene().name,
                ["profile"] = player.ProfileName,
                ["samePlayerData"] = ReferenceEquals(player.Data, playerData),
                ["sameSaveData"] = ReferenceEquals(SaveDataMgr.Instance.SaveData, saveData),
                ["sameSessionState"] = ReferenceEquals(session.State, sessionState),
                ["gameplayReady"] = GameManager.Instance.IsGameplayReady,
                ["spaceView"] = session.IsSpaceView,
                ["chunkManagerId"] = chunks.GetInstanceID(),
                ["chunkManagerHasWorldRuntime"] = chunks.WorldRuntime != null,
                ["registeredSurfaceWorldCount"] = GameplayWorldRegistry.Worlds.Count,
                ["sectorFound"] = sectorFound, ["sectorRetained"] = retained,
                ["sectorActive"] = active, ["sectorRootId"] = sectorRootId,
                ["planetInstanceId"] = planet == null ? 0 : planet.GetInstanceID(),
                ["planetBodyId"] = planet?.planetData?.BodyId,
                ["sectorHits"] = space.SectorPresentationCacheHits,
                ["sectorCreated"] = space.SectorPresentationCreatedCount,
                ["sectorRetainedCount"] = space.RetainedSectorPresentationCount,
                ["chunkCreated"] = chunks.RuntimeChunkViewCreatedCount,
                ["chunkHits"] = chunks.RuntimeChunkViewTransitionHits,
                ["chunkRetained"] = chunks.RetainedSceneTransitionChunkViewCount,
                ["cachedViews"] = cachedViews,
                ["simulationSeconds"] = session.State.SimulationSeconds
            };
        }

        private static int CountActiveColliders(Transform root)
        {
            int count = 0;
            foreach (Collider2D collider in root.GetComponentsInChildren<Collider2D>(true))
                if (collider != null && collider.enabled && collider.gameObject.activeInHierarchy) count++;
            return count;
        }
    }
    #endregion
}
