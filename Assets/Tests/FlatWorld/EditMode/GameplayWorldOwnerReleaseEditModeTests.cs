using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using FlatWorld.WorldModel;
using NUnit.Framework;
using UnityEngine;
using ModelChunkMgr = FlatWorld.WorldModel.ChunkMgr;
using ModelWorldAddress = FlatWorld.WorldModel.WorldAddress;

namespace FlatWorld.Tests.EditMode
{
    public sealed class GameplayWorldOwnerReleaseEditModeTests
    {
        #region 退出时的所有权与资源释放回归
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
        private readonly List<GameObject> roots = new();
        private readonly List<string> worldKeys = new();
        private readonly List<WorldRuntime> unownedWorlds = new();
        private object previousExecution;
        private object previousMachineScope;
        private object previousMachineOverride;
        private object previousSaveDataManager;
        private IDictionary Registry => (IDictionary)typeof(GameplayWorldRegistry).GetField("worlds", PrivateStatic).GetValue(null);
        private IDictionary MachineScopes => (IDictionary)typeof(MachineWorld).GetField("scopes", PrivateStatic).GetValue(null);

        [SetUp]
        public void RememberExecutionContext()
        {
            previousExecution = typeof(GameplayWorldRegistry).GetField("execution", PrivateStatic).GetValue(null);
            previousMachineScope = typeof(MachineWorld).GetField("scope", PrivateStatic).GetValue(null);
            previousMachineOverride = typeof(MachineWorld).GetField("scopeOverride", PrivateStatic).GetValue(null);
            // 编辑态夹具显式注入未启动的自有存档管理器，避免只读查询自动创建常驻对象。
            FieldInfo saveSingleton = typeof(SingletonAutoMono<SaveDataMgr>).GetField("instance", PrivateStatic);
            previousSaveDataManager = saveSingleton.GetValue(null);
            saveSingleton.SetValue(null, NewInactiveComponent<SaveDataMgr>());
        }

        [TearDown]
        public void RestoreOnlyTestOwnedObjectsAndWorldRecords()
        {
            SetExecution(previousExecution);
            foreach (string key in worldKeys) Registry.Remove(key);
            foreach (GameObject root in roots)
            {
                ChunkMgr manager = root != null ? root.GetComponent<ChunkMgr>() : null;
                if (manager != null && manager.RuntimeChunks != null)
                    Invoke(manager, "DetachAfterPresentationSceneUnloaded");
            }
            foreach (string key in worldKeys)
            {
                var args = new object[] { key, null };
                if ((bool)InvokeRegistry("TryGetOwnedWorldModel", args))
                    InvokeRegistry("ReleaseOwnedWorldModel", key, args[1]);
            }
            foreach (WorldRuntime world in unownedWorlds) world.Dispose();
            foreach (GameObject root in roots) if (root != null) UnityEngine.Object.DestroyImmediate(root);
            foreach (string key in worldKeys) MachineScopes.Remove(key);
            typeof(MachineWorld).GetField("scope", PrivateStatic).SetValue(null, previousMachineScope);
            typeof(MachineWorld).GetField("scopeOverride", PrivateStatic).SetValue(null, previousMachineOverride);
            typeof(SingletonAutoMono<SaveDataMgr>).GetField("instance", PrivateStatic).SetValue(null, previousSaveDataManager);
            roots.Clear();
            worldKeys.Clear();
            unownedWorlds.Clear();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ItemUnregister_MissingWorldNeverCreatesSceneOrWorld(bool registeredItem)
        {
            string key = NewWorldKey();
            WorldNavigationManager manager = NewInactiveComponent<WorldNavigationManager>();
            SetField(manager, "ownedWorldKey", key);
            Item item = NewInactiveComponent<GameItem>();
            object bridge = NewFootprintBridge(manager, item, registeredItem);
            if (registeredItem) manager.RegisterObstacle(item.GetInstanceID(), new[] { Vector2Int.zero });
            int revision = manager.GridRevision;
            int count = Registry.Count;

            Assert.DoesNotThrow(() => Invoke(bridge, "Unregister", item));
            Assert.That(Registry.Contains(key), Is.False);
            Assert.That(Registry.Count, Is.EqualTo(count));
            Assert.That(((HashSet<int>)GetField(bridge, "registeredItemIds")).Contains(item.GetInstanceID()), Is.False);
            Assert.That(manager.GridRevision, registeredItem ? Is.GreaterThan(revision) : Is.EqualTo(revision));
        }

        [Test]
        public void ItemUnregister_UnloadedSceneUsesOnlyExistingNavigationOwnerAndRestoresContext()
        {
            string key = NewWorldKey();
            WorldNavigationManager manager = NewInactiveComponent<WorldNavigationManager>();
            SetField(manager, "ownedWorldKey", key);
            GameplayWorldRuntime record = RegisterWorld(key, null, manager);
            Item item = NewInactiveComponent<GameItem>();
            object bridge = NewFootprintBridge(manager, item, true);
            manager.RegisterObstacle(item.GetInstanceID(), new[] { Vector2Int.zero });
            int revision = manager.GridRevision;

            Assert.That(GameplayWorldRegistry.TryGet(key, out _), Is.False, "正常查询仍拒绝卸载场景");
            Assert.DoesNotThrow(() => Invoke(bridge, "Unregister", item));
            Assert.That(manager.GridRevision, Is.GreaterThan(revision));
            Assert.That(Registry[key], Is.SameAs(record));
            Assert.That(typeof(GameplayWorldRegistry).GetField("execution", PrivateStatic).GetValue(null), Is.SameAs(previousExecution));
        }

        [Test]
        public void ChunkOwnerDestroy_UnloadedSceneDetachesButSessionRetainsPureWorld()
        {
            string key = NewWorldKey();
            ChunkMgr manager = NewInactiveComponent<ChunkMgr>();
            manager.BindNetworkWorld(key);
            var world = AttachPureWorld(manager, key);
            GameplayWorldRuntime record = RegisterWorld(key, manager, null);

            Assert.That(GameplayWorldRegistry.TryGet(key, out _), Is.False);
            Assert.DoesNotThrow(() => Invoke(manager, "OnDestroy"));
            Assert.That(manager.RuntimeChunks, Is.Null);
            Assert.DoesNotThrow(() => world.Tick(0));
            Assert.That(Registry[key], Is.SameAs(record));
            Assert.That((bool)GetField(manager, "<IsWorldRuntimeShuttingDown>k__BackingField"), Is.True);
            Assert.That((bool)InvokeRegistry("ReleaseOwnedWorldModel", key, world), Is.True);
            Assert.Throws<ObjectDisposedException>(() => world.Tick(0));
        }

        [Test]
        public void ChunkOwnerSceneDetach_SameWorldIsReattachedUntilSessionExplicitlyReleasesIt()
        {
            string key = NewWorldKey();
            var world = (WorldRuntime)InvokeRegistry("GetOrCreateOwnedWorldModel", key, 7L);
            var address = new ModelWorldAddress("surface", new Int2(0, 0));
            var profile = new ChunkGenerationProfileSnapshot("owner-scene-test", 1, 2, 2);
            ChunkGenerationRequest request = world.BeginChunkGeneration(address, 1, profile);
            using var buffer = new ChunkTerrainBuffer(2, 2);
            buffer.SetCell(0, 0, new TerrainCell(23, 0, 0, 1, 1, TerrainCellFlags.Walkable));
            using var result = new ChunkGenerationResult(request, buffer);
            Assert.That(world.TryCommit(result, out _), Is.True);
            world.TryGetChunk(address, out ChunkRuntime originalChunk);
            ChunkLease retention = world.AcquireChunkLease(address, ChunkLeaseKind.DataRetention);
            try
            {
                ChunkMgr first = NewInactiveComponent<ChunkMgr>();
                ChunkMgr second = NewInactiveComponent<ChunkMgr>();
                first.BindNetworkWorld(key);
                second.BindNetworkWorld(key);
                Assert.That(AttachPureWorld(first, key), Is.SameAs(world));
                ChunkView view = AttachOwnViewWithLease(first, world);
                Assert.Throws<InvalidOperationException>(() => AttachPureWorld(second, key));
                Assert.That((bool)InvokeRegistry("ReleaseOwnedWorldModel", key, world), Is.False);

                Invoke(first, "DetachAfterPresentationSceneUnloaded");
                Assert.That(first.RuntimeChunks, Is.Null);
                Assert.That(view == null, Is.True);
                Assert.That(originalChunk.DataRetentionLeaseCount, Is.EqualTo(1));
                Assert.That(AttachPureWorld(second, key), Is.SameAs(world));
                Assert.That(world.Epoch, Is.EqualTo(7));
                Assert.That(world.TryGetChunk(address, out ChunkRuntime restored), Is.True);
                Assert.That(restored, Is.SameAs(originalChunk));
                Assert.That(restored.Terrain.GetCell(0, 0).GroundTileId, Is.EqualTo(23));
                Invoke(first, "DetachAfterPresentationSceneUnloaded");
                Assert.That(second.WorldRuntime, Is.SameAs(world));
                Invoke(second, "DetachAfterPresentationSceneUnloaded");
                retention.Dispose();
                Assert.That((bool)InvokeRegistry("ReleaseOwnedWorldModel", key, world), Is.True);
                Assert.Throws<ObjectDisposedException>(() => world.Tick(0));
                var replacement = (WorldRuntime)InvokeRegistry("GetOrCreateOwnedWorldModel", key, 8L);
                Assert.That((bool)InvokeRegistry("ReleaseOwnedWorldModel", key, world), Is.False);
                Assert.DoesNotThrow(() => replacement.Tick(0));
                Assert.That((bool)InvokeRegistry("ReleaseOwnedWorldModel", key, replacement), Is.True);
            }
            finally { retention.Dispose(); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ChunkOwnerDestroy_MissingOrReplacedRegistrationReleasesOwnViewAndPreservesCurrentWorld(bool replaceOwner)
        {
            string key = NewWorldKey();
            string currentKey = replaceOwner ? key : NewWorldKey();
            ChunkMgr oldOwner = NewInactiveComponent<ChunkMgr>();
            oldOwner.BindNetworkWorld(key);
            var oldWorld = AttachUnownedPureWorld(oldOwner, key);
            ChunkMgr currentOwner = NewInactiveComponent<ChunkMgr>();
            currentOwner.BindNetworkWorld(currentKey);
            var currentWorld = AttachPureWorld(currentOwner, currentKey);
            GameplayWorldRuntime currentRecord = RegisterWorld(currentKey, currentOwner, null);
            SetExecution(currentRecord);
            object blocker = new object();
            WorldLiquidFlowObstacles.Register(blocker, new[] { Vector2Int.zero });
            ChunkView ownView = AttachOwnViewWithLease(oldOwner, oldWorld);
            try
            {
                Assert.DoesNotThrow(() => Invoke(oldOwner, "OnDestroy"));
                Assert.That(ownView == null, Is.True, "旧管理器自有 View 必须销毁");
                Assert.That(((IDictionary)GetField(oldOwner, "activeRuntimeBindings")).Count, Is.Zero);
                Assert.That(oldOwner.RuntimeChunks, Is.Null);
                Assert.DoesNotThrow(() => oldWorld.Tick(0));
                Assert.DoesNotThrow(() => currentWorld.Tick(0));
                Assert.That(WorldLiquidFlowObstacles.IsBlocked(Vector2Int.zero), Is.True, "不得清理当前世界的阻挡");
                Assert.That(Registry[currentKey], Is.SameAs(currentRecord));
                Assert.That(typeof(GameplayWorldRegistry).GetField("execution", PrivateStatic).GetValue(null), Is.SameAs(currentRecord));
            }
            finally
            {
                WorldLiquidFlowObstacles.Unregister(blocker);
                Invoke(currentOwner, "OnDestroy");
            }
        }

        [Test]
        public void NormalWorldRelease_ReplacedOwnerStillRejectsForeignContext()
        {
            string key = NewWorldKey();
            ChunkMgr oldOwner = NewInactiveComponent<ChunkMgr>();
            oldOwner.BindNetworkWorld(key);
            WorldRuntime world = AttachPureWorld(oldOwner, key);
            ChunkMgr replacement = NewInactiveComponent<ChunkMgr>();
            RegisterWorld(key, replacement, null);
            Assert.Throws<InvalidOperationException>(() => oldOwner.ReleaseRegisteredWorld());
            Assert.That(oldOwner.RuntimeChunks, Is.Not.Null);
            Invoke(oldOwner, "OnDestroy");
            Assert.DoesNotThrow(() => world.Tick(0));
            Assert.That((bool)InvokeRegistry("ReleaseOwnedWorldModel", key, world), Is.True);
            Assert.Throws<ObjectDisposedException>(() => world.Tick(0));
        }

        [Test]
        public void ChunkOwnerDestroy_SubscriptionCleanupFailureStillReturnsWorldToSession()
        {
            string key = NewWorldKey();
            ChunkMgr owner = NewInactiveComponent<ChunkMgr>();
            owner.BindNetworkWorld(key);
            WorldRuntime world = AttachPureWorld(owner, key);
            SetField(owner, "sightBlockingChunkCommittedSubscription", new FailingCleanupSubscription());
            Assert.Throws<InvalidOperationException>(() => Invoke(owner, "OnDestroy"));
            Assert.That(owner.RuntimeChunks, Is.Null);
            Assert.DoesNotThrow(() => world.Tick(0));
            Assert.That((bool)InvokeRegistry("ReleaseOwnedWorldModel", key, world), Is.True);
            Assert.Throws<ObjectDisposedException>(() => world.Tick(0));
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void AgricultureRendererRelease_OnlyCurrentOwnerMayClearTillingProgress(bool destroyCallback, bool replaceOwner)
        {
            string key = NewWorldKey();
            ChunkMgr oldOwner = NewInactiveComponent<ChunkMgr>();
            oldOwner.BindNetworkWorld(key);
            WorldRuntime oldWorld = replaceOwner ? AttachUnownedPureWorld(oldOwner, key) : AttachPureWorld(oldOwner, key);
            ChunkMgr newOwner = NewInactiveComponent<ChunkMgr>();
            newOwner.BindNetworkWorld(key);
            WorldRuntime newWorld = replaceOwner ? AttachPureWorld(newOwner, key) : AttachUnownedPureWorld(newOwner, key);
            RegisterWorld(key, replaceOwner ? newOwner : oldOwner, null);
            var address = new ModelWorldAddress("cleanup-tilling-" + Guid.NewGuid().ToString("N"), new Int2(0, 0));
            using ChunkLease oldLease = oldWorld.AcquireChunkLease(address, ChunkLeaseKind.Presentation);
            using ChunkLease newLease = newWorld.AcquireChunkLease(address, ChunkLeaseKind.Presentation);
            oldWorld.TryGetChunk(address, out ChunkRuntime oldChunk);
            newWorld.TryGetChunk(address, out _);
            using var buffer = new ChunkTerrainBuffer(1, 1);
            typeof(ChunkRuntime).GetProperty("Terrain").SetValue(oldChunk, buffer.Seal());
            ChunkAgricultureRenderer renderer = NewInactiveComponent<ChunkAgricultureRenderer>();
            SetField(renderer, "chunkManager", oldOwner);
            SetField(renderer, "chunk", oldChunk);
            Type keyType = typeof(FarmlandSystem).GetNestedType("TillingKey", BindingFlags.NonPublic);
            Type stateType = typeof(FarmlandSystem).GetNestedType("TillingRuntimeState", BindingFlags.NonPublic);
            object tillingKey = Activator.CreateInstance(keyType, new object[] { address, Vector2Int.zero });
            object tillingState = Activator.CreateInstance(stateType, true);
            IDictionary tillingStates = (IDictionary)typeof(FarmlandSystem).GetField("tillingStates", PrivateStatic).GetValue(null);
            tillingStates.Add(tillingKey, tillingState);
            try
            {
                if (destroyCallback) Assert.DoesNotThrow(() => Invoke(renderer, "OnDestroy"));
                else Assert.DoesNotThrow(renderer.Unbind);
                Assert.That(tillingStates.Contains(tillingKey), Is.EqualTo(replaceOwner), "仅当前管理器自己的农业表现可以清理同地址进度");
                if (replaceOwner) Assert.That(tillingStates[tillingKey], Is.SameAs(tillingState));
            }
            finally
            {
                tillingStates.Remove(tillingKey);
                Invoke(oldOwner, "OnDestroy");
                Invoke(newOwner, "OnDestroy");
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MachineOwnerReleaseScope_BorrowsOnlyExistingStateWithoutBuildingGraphs(bool existingScope)
        {
            string key = NewWorldKey();
            Type stateType = typeof(MachineWorld).GetNestedType("ScopeState", BindingFlags.NonPublic);
            object existing = null;
            if (existingScope)
            {
                existing = Activator.CreateInstance(stateType, true);
                stateType.GetProperty("Key").SetValue(existing, key);
                MachineScopes.Add(key, existing);
            }
            int count = MachineScopes.Count;
            var lease = (IDisposable)typeof(MachineWorld).GetMethod("UseExistingScopeForOwnerRelease", PrivateStatic)
                .Invoke(null, new object[] { key });
            try
            {
                Assert.That(MachineScopes.Count, Is.EqualTo(count));
                Assert.That(lease != null, Is.EqualTo(existingScope));
                Assert.That(typeof(MachineWorld).GetField("scope", PrivateStatic).GetValue(null),
                    Is.SameAs(existingScope ? existing : previousMachineScope));
                if (existingScope)
                {
                    Assert.That(stateType.GetField("Graph").GetValue(existing), Is.Null);
                    Assert.That(stateType.GetField("Electrical").GetValue(existing), Is.Null);
                    Assert.That(stateType.GetField("Fluid").GetValue(existing), Is.Null);
                }
            }
            finally { lease?.Dispose(); }
            Assert.That(typeof(MachineWorld).GetField("scope", PrivateStatic).GetValue(null), Is.SameAs(previousMachineScope));
            Assert.That(typeof(MachineWorld).GetField("scopeOverride", PrivateStatic).GetValue(null), Is.EqualTo(previousMachineOverride));
        }
        #endregion

        #region 最小自有资源夹具
        private sealed class FailingCleanupSubscription : IDisposable
        {
            public void Dispose() => throw new InvalidOperationException("退出回归：模拟事件解绑失败");
        }

        private string NewWorldKey()
        {
            string key = "owner-release-regression-" + Guid.NewGuid().ToString("N");
            worldKeys.Add(key);
            return key;
        }

        private T NewInactiveComponent<T>() where T : Component
        {
            var root = new GameObject(typeof(T).Name + " owner release regression");
            root.SetActive(false);
            roots.Add(root);
            return root.AddComponent<T>();
        }

        private GameplayWorldRuntime RegisterWorld(string key, ChunkMgr chunks, WorldNavigationManager navigation)
        {
            var world = new GameplayWorldRuntime();
            typeof(GameplayWorldRuntime).GetProperty("WorldKey").SetValue(world, key);
            typeof(GameplayWorldRuntime).GetProperty("Chunks").SetValue(world, chunks);
            typeof(GameplayWorldRuntime).GetProperty("Navigation").SetValue(world, navigation);
            Registry.Add(key, world);
            return world;
        }

        private static WorldRuntime AttachPureWorld(ChunkMgr manager, string key)
        {
            var world = (WorldRuntime)InvokeRegistry("GetOrCreateOwnedWorldModel", key, 1L);
            Invoke(manager, "AttachOwnedWorldRuntime", world, new DeterministicChunkGenerator());
            return world;
        }

        private WorldRuntime AttachUnownedPureWorld(ChunkMgr manager, string key)
        {
            var world = new WorldRuntime(key, 1);
            unownedWorlds.Add(world);
            SetField(manager, "runtimeChunkManager", new ModelChunkMgr(world, new DeterministicChunkGenerator()));
            return world;
        }

        private ChunkView AttachOwnViewWithLease(ChunkMgr manager, WorldRuntime world)
        {
            var address = new ModelWorldAddress("surface", new Int2(0, 0));
            ChunkLease lease = world.AcquireChunkLease(address, ChunkLeaseKind.Presentation);
            world.TryGetChunk(address, out ChunkRuntime chunk);
            ChunkView view = NewInactiveComponent<ChunkView>();
            SetField(view, "world", world);
            SetField(view, "chunk", chunk);
            SetField(view, "presentationLease", lease);
            Type bindingType = typeof(ChunkMgr).GetNestedType("RuntimeChunkBinding", BindingFlags.NonPublic);
            object binding = Activator.CreateInstance(bindingType, true);
            bindingType.GetField("View").SetValue(binding, view);
            ((IDictionary)GetField(manager, "activeRuntimeBindings")).Add(address, binding);
            return view;
        }

        private static object NewFootprintBridge(WorldNavigationManager manager, Item item, bool registered)
        {
            Type type = typeof(WorldNavigationManager).Assembly.GetType("WorldNavigationItemFootprintBridge", true);
            object bridge = Activator.CreateInstance(type, true);
            SetField(bridge, "navigationManager", manager);
            if (registered) ((HashSet<int>)GetField(bridge, "registeredItemIds")).Add(item.GetInstanceID());
            return bridge;
        }

        private static void SetExecution(object world)
            => typeof(GameplayWorldRegistry).GetField("execution", PrivateStatic).SetValue(null, world);
        private static object GetField(object target, string name)
            => target.GetType().GetField(name, PrivateInstance).GetValue(target);
        private static void SetField(object target, string name, object value)
            => target.GetType().GetField(name, PrivateInstance).SetValue(target, value);
        private static object InvokeRegistry(string method, params object[] args)
        {
            try { return typeof(GameplayWorldRegistry).GetMethod(method, PrivateStatic).Invoke(null, args); }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw(); throw; }
        }
        private static void Invoke(object target, string method, params object[] args)
        {
            try { target.GetType().GetMethod(method, PrivateInstance).Invoke(target, args); }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw(); }
        }
        #endregion
    }
}
