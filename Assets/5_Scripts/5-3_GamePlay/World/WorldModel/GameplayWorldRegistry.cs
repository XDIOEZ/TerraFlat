using System;
using System.Collections;
using System.Collections.Generic;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed partial class GameplayWorldRuntime
{
    #region 世界资源所有权
    public string WorldKey { get; internal set; }
    public Scene Scene { get; internal set; }
    public ChunkMgr Chunks { get; internal set; }
    public WorldNavigationManager Navigation { get; internal set; }
    public bool UsesLocalCoordinates { get; internal set; }
    public bool SimulateWithoutObservers { get; internal set; }
    internal bool OwnsPhysics;
    public int PreparationCount { get; internal set; }
    internal readonly Dictionary<Renderer, bool> Renderers = new();
    #endregion
}

public static partial class GameplayWorldRegistry
{
    #region 世界注册与同步上下文
    private static readonly Dictionary<string, GameplayWorldRuntime> worlds = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, WorldRuntime> ownedWorldModels = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, ChunkMgr> ownedWorldModelManagers = new(StringComparer.Ordinal);
    private static readonly WorldSceneBindingIndex worldSceneBindings = new();
    private static readonly RuntimeWorldIdentityIndex worldIdentities = new();
    private static readonly WorldItemOwnershipIndex<Item> worldItems = new(worldIdentities);
    private static GameplayWorldRuntime execution;
    internal static readonly Dictionary<Renderer, GameplayWorldPresentationOwner> PresentationOwners = new();
    private static readonly Dictionary<string, GameplayWorldRuntime> spaceContexts = new(StringComparer.Ordinal);
    private static GameplayWorldRegistryHost host;
    private static bool releasing;
    private static bool preserveLocalChunkManagerForSpaceTransition;
    private static readonly List<WorldPhysicsService2D> physicsDispatch = new();
    private static readonly List<GameplayWorldRuntime> releaseDispatch = new();
    private static readonly HashSet<PhysicsScene2D> physicsScenes = new();
    internal static bool IsPhysicsDispatchInProgress { get; private set; }
    private static bool sceneCacheBound;
    private static Scene localScene;
    private static string localSceneKey;
    private static string selectedLocalWorldKey;
    private static void BindSceneCache()
    {
        if (sceneCacheBound) return;
        localScene = SceneManager.GetActiveScene(); localSceneKey = ResolveLocalWorldKey(localScene);
        SceneManager.activeSceneChanged += OnLocalSceneChanged;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
        sceneCacheBound = true;
    }
    private static void OnLocalSceneChanged(Scene before, Scene after)
    { localScene = after; localSceneKey = ResolveLocalWorldKey(after); RefreshVisibility(); WeatherMgr.SelectLocalPresentation(localSceneKey); }

    // 卸载只解除仍属于该场景的真实拥有者，旧上下文不能撤销已替换的绑定。
    private static void OnSceneUnloaded(Scene scene)
    {
        ReleasePhysicsForScene(scene);
        DroppedItemService.DetachScenePresentation(scene);
        foreach (GameplayWorldRuntime world in worlds.Values)
            if (world.Scene == scene) worldSceneBindings.UnbindWorldFromScene(scene.handle, world.WorldKey, world);
        foreach (GameplayWorldRuntime world in spaceContexts.Values)
            if (world.Scene == scene) worldSceneBindings.UnbindWorldFromScene(scene.handle, world.WorldKey, world);
        RefreshLocalWorldAfterSceneBindingChange(scene);
    }

    private static string ResolveLocalWorldKey(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded) return null;
        if (selectedLocalWorldKey != null && worldSceneBindings.TryResolveWorldKeyForScene(
                scene.handle, selectedLocalWorldKey, out string selected)) return selected;
        if (worldSceneBindings.TryResolveWorldKeyForScene(scene.handle, null, out string unique)) return unique;
        return worldSceneBindings.HasWorldsForScene(scene.handle) ? null : scene.name;
    }

    private static void RefreshLocalWorldAfterSceneBindingChange(Scene scene)
    {
        if (!sceneCacheBound || localScene != scene) return;
        string current = ResolveLocalWorldKey(scene);
        if (string.Equals(localSceneKey, current, StringComparison.Ordinal)) return;
        localSceneKey = current;
        RefreshVisibility();
        WeatherMgr.SelectLocalPresentation(localSceneKey);
    }

    // 不同逻辑世界可以共用同一个 Scene，有多个候选时必须提供真实的世界上下文。
    public static bool TryResolveWorldKeyForScene(Scene scene, out string worldKey)
    {
        worldKey = null;
        if (!scene.IsValid() || !scene.isLoaded) return false;
        if (execution != null && execution.Scene == scene)
            return worldSceneBindings.TryResolveWorldKeyForOwner(scene.handle, execution.WorldKey, execution, out worldKey);
        return TryResolveUnambiguousWorldKeyForScene(scene, out worldKey);
    }

    // 未显式归属的物品只接受场景唯一绑定，不借用其他对象当前的执行上下文。
    public static bool TryResolveUnambiguousWorldKeyForScene(Scene scene, out string worldKey)
    {
        worldKey = null;
        if (!scene.IsValid() || !scene.isLoaded) return false;
        if (worldSceneBindings.TryResolveWorldKeyForScene(scene.handle, null, out worldKey)) return true;
        if (worldSceneBindings.HasWorldsForScene(scene.handle)) return false;
        worldKey = scene.name;
        return !string.IsNullOrWhiteSpace(worldKey);
    }

    public static int GetOrCreateRuntimeWorldIdentity(string worldKey)
        => worldIdentities.GetOrCreateRuntimeWorldIdentity(worldKey);

    // 纯世界数据随游戏会话保存，场景和表现管理器可以独立重建。
    internal static WorldRuntime GetOrCreateOwnedWorldModel(string worldKey, long epoch)
    {
        if (string.IsNullOrWhiteSpace(worldKey)) throw new ArgumentException("世界身份为空。", nameof(worldKey));
        if (!ownedWorldModels.TryGetValue(worldKey, out WorldRuntime world))
            ownedWorldModels.Add(worldKey, world = new WorldRuntime(worldKey, epoch));
        return world;
    }

    internal static bool TryGetOwnedWorldModel(string worldKey, out WorldRuntime world)
        => ownedWorldModels.TryGetValue(worldKey ?? string.Empty, out world);

    // 一个世界同时只能交给一个区块管理器推进，旧管理器完成解绑前不能抢占。
    internal static void ClaimOwnedWorldModel(string worldKey, WorldRuntime expectedWorld, ChunkMgr manager)
    {
        if (ReferenceEquals(manager, null) || expectedWorld == null ||
            !TryGetOwnedWorldModel(worldKey, out WorldRuntime current) || !ReferenceEquals(current, expectedWorld))
            throw new InvalidOperationException("不能附着未登记或已替换的世界模型。");
        if (ownedWorldModelManagers.TryGetValue(worldKey, out ChunkMgr owner))
        {
            if (!ReferenceEquals(owner, manager)) throw new InvalidOperationException("世界模型已有活动区块管理器。");
            return;
        }
        ownedWorldModelManagers.Add(worldKey, manager);
    }

    internal static bool DetachOwnedWorldModel(string worldKey, WorldRuntime expectedWorld, ChunkMgr manager)
    {
        if (ReferenceEquals(manager, null) || expectedWorld == null ||
            !TryGetOwnedWorldModel(worldKey, out WorldRuntime current) || !ReferenceEquals(current, expectedWorld) ||
            !ownedWorldModelManagers.TryGetValue(worldKey, out ChunkMgr owner) || !ReferenceEquals(owner, manager))
            return false;
        return ownedWorldModelManagers.Remove(worldKey);
    }

    internal static bool ReleaseOwnedWorldModel(string worldKey, WorldRuntime expectedWorld)
    {
        if (worldKey == null || expectedWorld == null ||
            !ownedWorldModels.TryGetValue(worldKey, out WorldRuntime current) || !ReferenceEquals(current, expectedWorld) ||
            ownedWorldModelManagers.ContainsKey(worldKey))
            return false;
        ownedWorldModels.Remove(worldKey);
        current.Dispose();
        return true;
    }

    internal static void ReleaseAllOwnedWorldModels()
    {
        if (ownedWorldModelManagers.Count != 0)
            throw new InvalidOperationException("仍有区块管理器附着，不能释放会话世界模型。");
        foreach (WorldRuntime world in ownedWorldModels.Values) world.Dispose();
        ownedWorldModels.Clear();
    }

    // 会话最后一次保存和物品注销完成后才清理归属，场景退出通知本身不能提前丢失账本。
    internal static void ClearSessionWorldOwnership()
    {
        worldItems.Clear();
        worldIdentities.Clear();
    }

    public static string GetItemWorldKey(Item item)
        => worldItems.TryGetOwnership(item, out var owner) ? owner.WorldKey : null;
    public static int GetItemWorldIdentity(Item item)
        => worldItems.TryGetOwnership(item, out var owner) ? owner.WorldIdentity : 0;
    public static ulong GetItemWorldRevision(Item item)
        => worldItems.TryGetOwnership(item, out var owner) ? owner.Revision : 0;
    internal static bool TryGetItemWorldOwnership(Item item, out WorldItemOwnershipIndex<Item>.Ownership owner)
        => worldItems.TryGetOwnership(item, out owner);
    public static int GetWorldItemCount(string worldKey) => worldItems.GetWorldItemCount(worldKey);
    public static ulong GetWorldItemOwnershipRevision(string worldKey) => worldItems.GetWorldOwnershipRevision(worldKey);

    // 世界账本负责统一交接，其他运行索引只消费归属变化通知。
    public static void AssignItemToWorld(Item item, string worldKey)
    {
        if (ReferenceEquals(item, null)) return;
        ItemMgr manager = ItemMgr.GetExistingInstance();
        bool registered = item != null && manager != null && manager.IsRuntimeItemRegistered(item);
        string sourceWorld = GetItemWorldKey(item);
        if (string.Equals(sourceWorld, worldKey, StringComparison.Ordinal)) return;
        if (registered && !string.IsNullOrEmpty(sourceWorld) &&
            (IsPhysicsDispatchInProgress ||
             TryGetPhysics(sourceWorld, out var simulatingSource) && simulatingSource.IsSimulating ||
             TryGetPhysics(worldKey, out var simulatingTarget) && simulatingTarget.IsSimulating))
            throw new InvalidOperationException("物理模拟分发期间不能迁移物品，请在本轮结束后重试。");
        if (!registered) { worldItems.AssignItemToWorld(item, worldKey); return; }
        if (!string.IsNullOrEmpty(worldKey)) RequireAssignedItemPhysicsWorld(worldKey);

        Scene sourceScene = item.gameObject.scene;
        Transform sourceParent = item.transform.parent;
        Vector3 sourceLocalPosition = item.transform.localPosition;
        Quaternion sourceLocalRotation = item.transform.localRotation;
        Vector3 sourceLocalScale = item.transform.localScale;
        Vector3 sourceWorldPosition = item.transform.position;
        Quaternion sourceWorldRotation = item.transform.rotation;
        ItemData originalData = item.itemData;
        Rigidbody2D sourceBody = item.GetComponent<Rigidbody2D>();
        WorldPhysicsItemBodyRepresentation2D sourceRepresentation = sourceBody != null
            ? WorldPhysicsService2D.CaptureItemBodyRepresentation(sourceBody) : default;
        Vector2 sourceVelocity = sourceBody != null ? sourceBody.velocity : Vector2.zero;
        float sourceAngularVelocity = sourceBody != null ? sourceBody.angularVelocity : 0f;
        bool hadSourceProxy = TryGetPhysics(sourceWorld, out var sourcePhysics) &&
            sourcePhysics.TryGetItemProxy(item, out _);
        Vector3 snapshotPosition = originalData?.transform?.position ?? sourceWorldPosition;
        Quaternion snapshotRotation = originalData?.transform?.rotation ?? sourceWorldRotation;
        Vector3 snapshotScale = originalData?.transform?.scale ?? sourceLocalScale;
        bool hadPhysicsState = originalData?.PhysicsState != null;
        Vector2 snapshotVelocity = originalData?.PhysicsState?.Velocity ?? Vector2.zero;
        float snapshotAngularVelocity = originalData?.PhysicsState?.AngularVelocity ?? 0f;
        GameplayWorldPresentationOwner presentation = item.GetComponent<GameplayWorldPresentationOwner>();
        try
        {
            using (TryGet(sourceWorld, out _) ? UseWorld(sourceWorld) : null)
                WorldPhysicsService2D.ReleaseItem(item, preserveNativeMotion: true);
            if (item == null || !manager.IsRuntimeItemRegistered(item) || !ReferenceEquals(item.itemData, originalData)) return;
            snapshotPosition = originalData?.transform?.position ?? sourceWorldPosition;
            snapshotRotation = originalData?.transform?.rotation ?? sourceWorldRotation;
            snapshotScale = originalData?.transform?.scale ?? sourceLocalScale;
            hadPhysicsState = originalData?.PhysicsState != null;
            snapshotVelocity = originalData?.PhysicsState?.Velocity ?? Vector2.zero;
            snapshotAngularVelocity = originalData?.PhysicsState?.AngularVelocity ?? 0f;
            if (!worldItems.AssignItemToWorld(item, worldKey)) return;
            if (presentation != null) presentation.Release();
            RegisterItemPhysics(item);
            manager.NotifyRuntimeItemMoved(item);
            RegisterItemPresentation(item);
        }
        catch (Exception failure)
        {
            string failedWorld = GetItemWorldKey(item);
            if (item == null || !manager.IsRuntimeItemRegistered(item) || !ReferenceEquals(item.itemData, originalData) ||
                !string.Equals(failedWorld, worldKey, StringComparison.Ordinal) &&
                !string.Equals(failedWorld, sourceWorld, StringComparison.Ordinal)) throw;
            try
            {
                WorldPhysicsService2D.ReleaseItem(item, preserveNativeMotion: true);
                worldItems.AssignItemToWorld(item, sourceWorld);
                item.transform.SetParent(null, true);
                if (sourceScene.IsValid() && sourceScene.isLoaded && item.gameObject.scene != sourceScene)
                    SceneManager.MoveGameObjectToScene(item.gameObject, sourceScene);
                if (sourceParent != null && sourceParent.gameObject.scene == sourceScene)
                {
                    item.transform.SetParent(sourceParent, false);
                    item.transform.SetLocalPositionAndRotation(sourceLocalPosition, sourceLocalRotation);
                    item.transform.localScale = sourceLocalScale;
                }
                else
                {
                    item.transform.SetPositionAndRotation(sourceWorldPosition, sourceWorldRotation);
                    item.transform.localScale = sourceLocalScale;
                }
                if (originalData?.transform != null)
                {
                    originalData.transform.position = snapshotPosition;
                    originalData.transform.rotation = snapshotRotation;
                    originalData.transform.scale = snapshotScale;
                }
                if (hadPhysicsState)
                {
                    originalData.PhysicsState ??= new ItemPhysicsRuntimeState();
                    originalData.PhysicsState.Velocity = snapshotVelocity;
                    originalData.PhysicsState.AngularVelocity = snapshotAngularVelocity;
                }
                else if (originalData != null) originalData.PhysicsState = null;
                using (TryGet(sourceWorld, out _) ? UseWorld(sourceWorld) : null)
                {
                    if (hadSourceProxy) RegisterItemPhysics(item);
                    if (sourceBody != null)
                    {
                        WorldPhysicsService2D.RestoreItemBodyRepresentation(item, sourceBody, sourceRepresentation);
                        if (sourceBody.bodyType != RigidbodyType2D.Static)
                        { sourceBody.velocity = sourceVelocity; sourceBody.angularVelocity = sourceAngularVelocity; }
                    }
                    manager.NotifyRuntimeItemMoved(item);
                    RegisterItemPresentation(item);
                }
            }
            catch (Exception rollback)
            {
                throw new AggregateException("物品世界迁移失败，恢复源世界也失败。", failure, rollback);
            }
            throw;
        }
    }

    // 旧入口仅在注册时根据明确上下文或唯一场景绑定确定一次归属。
    internal static void RegisterItemToInitialWorld(Item item)
    {
        if (worldItems.TryGetOwnership(item, out _)) return;
        string ownerWorld = item.Owner != null ? GetItemWorldKey(item.Owner) : null;
        if (ownerWorld != null) worldItems.AssignItemToWorld(item, ownerWorld);
        else if (TryResolveWorldKeyForScene(item.gameObject.scene, out string worldKey))
            worldItems.AssignItemToWorld(item, worldKey);
        else throw new InvalidOperationException($"物品 {item.name} 没有明确的所属世界。");
    }

    internal static void DetachItemFromWorld(Item item, ulong expectedRevision)
    {
        if (worldItems.TryGetOwnership(item, out var owner) && owner.Revision == expectedRevision)
            worldItems.DetachItem(item);
    }
    public static string ExecutionWorldKey { get { BindSceneCache(); return execution?.WorldKey ?? localSceneKey; } }
    public static int ExecutionWorldIdentity
    {
        get
        {
            string key = ExecutionWorldKey;
            return string.IsNullOrWhiteSpace(key) ? 0 : GetOrCreateRuntimeWorldIdentity(key);
        }
    }
    public static Scene CurrentScene { get { BindSceneCache(); return execution?.Scene ?? localScene; } }
    public static ChunkMgr ExecutionChunks => execution?.Chunks;
    public static bool HasExecutionContext => execution != null;
    public static string LocalPresentationWorldKey { get { BindSceneCache(); return localSceneKey; } }
    public static IReadOnlyDictionary<string, GameplayWorldRuntime> Worlds => worlds;

    public static bool TryGet(string worldKey, out GameplayWorldRuntime world)
        => worlds.TryGetValue(worldKey ?? string.Empty, out world) && world.Scene.IsValid() && world.Scene.isLoaded &&
           worldSceneBindings.IsCurrentWorldOwner(world.Scene.handle, world.WorldKey, world);

    public static GameplayWorldRuntime GetOrCreate(string worldKey)
    {
        if (TryGet(worldKey, out GameplayWorldRuntime existing)) return existing;
        if (releasing || string.IsNullOrWhiteSpace(worldKey) || SpaceMgr.IsSpaceScene(worldKey))
            throw new InvalidOperationException($"地表运行时必须使用有效的星球维度地址：世界={worldKey}，释放中={releasing}。");
        BindSceneCache();
        WorldAddress address = WorldAddress.FromWorldKey(worldKey);
        if (!address.IsValid) throw new ArgumentException("世界地址无效。", nameof(worldKey));
        DimensionManager.Instance.EnsureWorldData(address);
        if (existing != null && (!existing.Scene.IsValid() || !existing.Scene.isLoaded))
        {
            ChunkMgr previousManager = existing.Chunks;
            string previousOwnerKey = previousManager != null && !string.IsNullOrEmpty(previousManager.OwnedWorldKey)
                ? previousManager.OwnedWorldKey : previousManager != null ? previousManager.WorldRuntime?.WorldId : null;
            if (previousManager != null && string.Equals(previousOwnerKey, worldKey, StringComparison.Ordinal))
            {
                // 旧场景失效后先结束原调度器，旧管理器不能与重建的管理器同时持有世界。
                previousManager.DetachAfterPresentationSceneUnloaded();
                if (!worlds.TryGetValue(worldKey, out GameplayWorldRuntime current) || !ReferenceEquals(current, existing))
                {
                    if (TryGet(worldKey, out GameplayWorldRuntime replacement)) return replacement;
                    throw new InvalidOperationException("旧世界解绑期间世界注册已变化。");
                }
                if (!ReferenceEquals(current.Chunks, previousManager) || previousManager.WorldRuntime != null ||
                    !string.IsNullOrEmpty(previousManager.OwnedWorldKey) &&
                    !string.Equals(previousManager.OwnedWorldKey, worldKey, StringComparison.Ordinal))
                    throw new InvalidOperationException("旧世界解绑期间区块管理器已变化。");
                if (ReferenceEquals(ChunkMgr.LocalInstance, previousManager)) ChunkMgr.ClearLocalNetworkWorld();
                current.Chunks = null;
                UnityEngine.Object.Destroy(previousManager.gameObject);
            }
        }
        if (host == null)
        {
            var root = new GameObject("GameplayWorldRegistry");
            UnityEngine.Object.DontDestroyOnLoad(root);
            host = root.AddComponent<GameplayWorldRegistryHost>();
        }
        Scene scene = SceneManager.GetSceneByName(worldKey);
        bool created = !scene.IsValid() || !scene.isLoaded;
        if (created) scene = WorldPhysicsScene2D.CreateIsolatedScene(worldKey);
        var world = new GameplayWorldRuntime { WorldKey = worldKey, Scene = scene, OwnsPhysics = created };
        if (existing != null)
            worldSceneBindings.UnbindWorldFromScene(existing.Scene.handle, existing.WorldKey, existing);
        worlds[worldKey] = world;
        worldSceneBindings.BindWorldToScene(scene.handle, worldKey, world);
        worldIdentities.GetOrCreateRuntimeWorldIdentity(worldKey);
        try
        {
            _ = world.Physics;
            ChunkMgr local = ChunkMgr.LocalInstance;
            if (local != null && (local.WorldRuntime?.WorldId == worldKey ||
                local.WorldRuntime == null && SceneManager.GetActiveScene() == scene &&
                ResolveLocalWorldKey(scene) == worldKey))
            {
                world.Chunks = local;
                local.BindNetworkWorld(worldKey);
            }
        else world.Chunks = ChunkMgr.CreateNetworkWorldManager(worldKey);
        world.Navigation = WorldNavigationManager.GetOrCreateForWorld(worldKey);
        _ = MonsterSpawnerManager.GetForWorld(worldKey);
        _ = WeatherMgr.GetForWorld(worldKey);
            RefreshLocalWorldAfterSceneBindingChange(scene);
            return world;
        }
        catch
        {
            ReleaseWorldPhysics(worldKey);
            worldSceneBindings.UnbindWorldFromScene(scene.handle, worldKey, world);
            worlds.Remove(worldKey);
            if (world.Chunks != null && world.Chunks != ChunkMgr.LocalInstance) UnityEngine.Object.Destroy(world.Chunks.gameObject);
            if (created && scene.IsValid() && scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
            RefreshLocalWorldAfterSceneBindingChange(scene);
            throw;
        }
    }

    // 同步事务只切查询上下文，不切活动场景，不向其它玩家广播世界退出。
    public static IDisposable UseWorld(string worldKey)
    {
        GameplayWorldRuntime next = GetOrCreate(worldKey);
        GameplayWorldRuntime previous = execution;
        execution = next;
        try { return new WorldScope(previous, MachineWorld.UseScope(worldKey)); }
        catch { execution = previous; throw; }
    }

    // 销毁只借用仍属于调用方的注册记录，场景卸载中也绝不补建世界。
    internal static bool TryUseExistingWorldForOwnerRelease(string worldKey, object expectedOwner, out IDisposable scope)
    {
        scope = null;
        if (string.IsNullOrEmpty(worldKey) || expectedOwner == null ||
            !worlds.TryGetValue(worldKey, out GameplayWorldRuntime existing) ||
            !ReferenceEquals(existing.Chunks, expectedOwner) && !ReferenceEquals(existing.Navigation, expectedOwner))
            return false;
        GameplayWorldRuntime previous = execution;
        execution = existing;
        try { scope = new WorldScope(previous, MachineWorld.UseExistingScopeForOwnerRelease(worldKey)); }
        catch { execution = previous; throw; }
        return true;
    }

    public static IDisposable UseSpaceScene(string worldKey)
    {
        if (!SpaceMgr.IsSpaceScene(worldKey)) throw new ArgumentException("必须指定已存在的真实星区场景。", nameof(worldKey));
        BindSceneCache();
        Scene scene = SceneManager.GetSceneByName(worldKey);
        if (!scene.IsValid() || !scene.isLoaded) return null;
        GameplayWorldRuntime previous = execution;
        if (!spaceContexts.TryGetValue(worldKey, out GameplayWorldRuntime context) || context.Scene != scene ||
            !worldSceneBindings.IsCurrentWorldOwner(scene.handle, worldKey, context))
        {
            if (context != null)
                worldSceneBindings.UnbindWorldFromScene(context.Scene.handle, context.WorldKey, context);
            spaceContexts[worldKey] = context = new GameplayWorldRuntime { WorldKey = worldKey, Scene = scene, SimulateWithoutObservers = true };
            _ = context.Physics;
            worldSceneBindings.BindWorldToScene(scene.handle, worldKey, context);
            worldIdentities.GetOrCreateRuntimeWorldIdentity(worldKey);
            RefreshLocalWorldAfterSceneBindingChange(scene);
        }
        execution = context;
        return new WorldScope(previous, null);
    }

    public static void ReleaseSpaceContext(string worldKey)
    {
        if (!spaceContexts.Remove(worldKey, out GameplayWorldRuntime context)) return;
        ReleaseWorldPhysics(worldKey);
        worldSceneBindings.UnbindWorldFromScene(context.Scene.handle, context.WorldKey, context);
        RefreshLocalWorldAfterSceneBindingChange(context.Scene);
    }

    // 单机太空迁移只保留本机管理器与已解绑的表现壳，世界注册仍按原规则释放。
    internal static IDisposable PreserveLocalChunkManagerForSpaceTransition(bool preserve)
    {
        return new LocalChunkManagerTransitionScope(preserve && !GameNetwork.IsOnline);
    }

    private sealed class LocalChunkManagerTransitionScope : IDisposable
    {
        private readonly bool previous = preserveLocalChunkManagerForSpaceTransition;
        private bool disposed;

        public LocalChunkManagerTransitionScope(bool preserve)
        {
            preserveLocalChunkManagerForSpaceTransition = preserve;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            preserveLocalChunkManagerForSpaceTransition = previous;
        }
    }

    private sealed class WorldScope : IDisposable
    {
        private readonly GameplayWorldRuntime previous;
        private IDisposable machines;
        private bool disposed;
        public WorldScope(GameplayWorldRuntime previous, IDisposable machines)
        { this.previous = previous; this.machines = machines; }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            execution = previous;
            try { machines?.Dispose(); }
            finally { machines = null; }
        }
    }

    public static void SelectLocalWorld(string worldKey)
    {
        GameplayWorldRuntime world = GetOrCreate(worldKey);
        BindSceneCache();
        selectedLocalWorldKey = worldKey;
        if (world.Scene == localScene) localSceneKey = ResolveLocalWorldKey(localScene);
        ChunkMgr.SelectLocalNetworkWorld(world.Chunks);
        RefreshVisibility(worldKey);
        WeatherMgr.SelectLocalPresentation(worldKey);
    }

    public static IEnumerator PrepareWorld(string worldKey, Vector3 point)
    {
        GameplayWorldRuntime world = GetOrCreate(worldKey);
        world.PreparationCount++;
        try
        {
        using (UseWorld(worldKey)) world.Chunks.RefreshChunksAroundObservers(new[] { point }, point, 2, 3, 4);
        float timeout = Time.realtimeSinceStartup + 90f;
        while (true)
        {
            bool ready;
            using (UseWorld(worldKey))
            {
                var address = world.Chunks.ResolveWorldAddress(point);
                ready = true;
                var profile = world.Chunks.ActiveGenerationProfile;
                if (profile == null) throw new InvalidOperationException("目标世界没有有效生成配置：" + worldKey);
                for (int y = -1; y <= 1 && ready; y++)
                for (int x = -1; x <= 1 && ready; x++)
                {
                    var neighbor = world.Chunks.ResolveWorldAddress(new Vector2(
                        address.ChunkOrigin.X + x * profile.Width, address.ChunkOrigin.Y + y * profile.Height));
                    ready = world.Chunks.TryGetChunkRuntime(neighbor, out var chunk) && chunk?.DataStatus == FlatWorld.WorldModel.ChunkDataStatus.Ready && chunk.Terrain != null && !chunk.Terrain.IsDisposed;
                }
            }
            if (ready) yield break;
            if (Time.realtimeSinceStartup > timeout) throw new TimeoutException("目标维度的真实区块准备超时：" + worldKey);
            yield return null;
        }
        }
        finally { world.PreparationCount = Math.Max(0, world.PreparationCount - 1); }
    }

    public static void ReleaseAll()
    {
        if (releasing) return;
        releasing = true;
        try
        {
            execution = null;
            ChunkMgr preservedLocal = preserveLocalChunkManagerForSpaceTransition && !GameNetwork.IsOnline
                ? ChunkMgr.LocalInstance : null;
            releaseDispatch.Clear();
            bool preserveLocalWorlds = false;
            foreach (GameplayWorldRuntime world in worlds.Values)
                if (world.UsesLocalCoordinates) preserveLocalWorlds = true;
                else releaseDispatch.Add(world);
            WeatherMgr.ReleaseAll();
            MonsterSpawnerManager.ReleaseAll();
            var releasedChunkManagers = new HashSet<ChunkMgr>();
            // 管理器在注册表仍可查询时按自己的世界同步释放，延迟OnDestroy只执行已关闭分支。
            foreach (GameplayWorldRuntime world in releaseDispatch)
            {
                ChunkMgr manager = world.Chunks;
                if (manager != null && releasedChunkManagers.Contains(manager)) continue;
                if (!world.Scene.IsValid() || !world.Scene.isLoaded ||
                    !worldSceneBindings.IsCurrentWorldOwner(world.Scene.handle, world.WorldKey, world))
                {
                    string managerWorldKey = manager != null && !string.IsNullOrEmpty(manager.OwnedWorldKey)
                        ? manager.OwnedWorldKey : manager != null ? manager.WorldRuntime?.WorldId : null;
                    if (manager != null && string.Equals(managerWorldKey, world.WorldKey, StringComparison.Ordinal))
                    {
                        manager.DetachAfterPresentationSceneUnloaded();
                        if (ReferenceEquals(world.Chunks, manager))
                        {
                            releasedChunkManagers.Add(manager);
                            if (ReferenceEquals(preservedLocal, manager))
                            {
                                if (ReferenceEquals(ChunkMgr.LocalInstance, manager)) ChunkMgr.ClearLocalNetworkWorld();
                                preservedLocal = null;
                            }
                        }
                    }
                    continue;
                }
                using (UseWorld(world.WorldKey))
                {
                    if (manager != null && manager == preservedLocal)
                        manager.OnSceneChange(retainPresentationPool: true);
                    else
                        manager?.ReleaseRegisteredWorld();
                }
                if (manager != null && ReferenceEquals(world.Chunks, manager)) releasedChunkManagers.Add(manager);
            }
            if (!preserveLocalWorlds)
            {
                WorldEntityRuntime.ReleaseAll();
                FlatWorld.NaturalEntities.NaturalEntityEcsService.ReleaseAll();
                BuildingOccupancyRegistry.ReleaseAll();
                WorldLiquidFlowObstacles.ReleaseAll();
            }
            foreach (GameplayWorldRuntime world in releaseDispatch)
            {
                if (preserveLocalWorlds)
                {
                    WorldEntityRuntime.ReleaseWorld(world.WorldKey);
                    BuildingOccupancyRegistry.ReleaseWorld(world.WorldKey);
                }
                AiRuntimeBackendService.ReleaseWorldEcology(world.WorldKey);
                WorldNavigationManager.ReleaseWorld(world.WorldKey);
                foreach (var pair in world.Renderers)
                {
                    if (pair.Key != null) pair.Key.forceRenderingOff = pair.Value;
                    PresentationOwners.Remove(pair.Key);
                }
                if (world.Chunks != null && world.Chunks != preservedLocal && releasedChunkManagers.Remove(world.Chunks))
                    UnityEngine.Object.Destroy(world.Chunks.gameObject);
                ReleaseWorldPhysics(world.WorldKey);
                worldSceneBindings.UnbindWorldFromScene(world.Scene.handle, world.WorldKey, world);
                worlds.Remove(world.WorldKey);
                if (world.OwnsPhysics && world.Scene.IsValid() && world.Scene.isLoaded)
                    SceneManager.UnloadSceneAsync(world.Scene);
            }
            if (preservedLocal != null) preservedLocal.BindNetworkWorld(null);
            else ChunkMgr.ClearLocalNetworkWorld();
            foreach (GameplayWorldRuntime context in spaceContexts.Values)
            {
                ReleaseWorldPhysics(context.WorldKey);
                worldSceneBindings.UnbindWorldFromScene(context.Scene.handle, context.WorldKey, context);
            }
            spaceContexts.Clear(); releaseDispatch.Clear(); selectedLocalWorldKey = null;
            if (!preserveLocalWorlds)
            {
                ReleaseAllWorldPhysics();
                worlds.Clear(); PresentationOwners.Clear(); worldSceneBindings.Clear();
            }
            if (sceneCacheBound) localSceneKey = ResolveLocalWorldKey(localScene);
            if (!preserveLocalWorlds)
            {
                if (host != null) UnityEngine.Object.Destroy(host.gameObject);
                host = null;
            }
        }
        finally { releasing = false; }
    }
    #endregion

    #region 独立场景物理与本机显示
    internal static void SimulatePhysics()
    {
        if (Time.timeScale <= 0f) return;
        if (IsPhysicsDispatchInProgress)
            throw new InvalidOperationException("不能重入世界物理模拟分发。");
        IsPhysicsDispatchInProgress = true;
        try
        {
            // 整轮分发期间禁止跨世界迁移，避免实体在两个场景各推进一次。
            physicsDispatch.Clear(); physicsDispatch.AddRange(worldPhysics.Values); physicsScenes.Clear();
            foreach (WorldPhysicsService2D physics in physicsDispatch)
            {
                if (!physics.IsValid || !physicsScenes.Add(physics.PrimarySpace.PhysicsScene)) continue;
                GameplayWorldRuntime next = worlds.TryGetValue(physics.WorldKey, out var world) ? world :
                    spaceContexts.TryGetValue(physics.WorldKey, out var space) ? space :
                    (physics.ExecutionContext ??= new GameplayWorldRuntime { WorldKey = physics.WorldKey, Scene = physics.PrimarySpace.Scene });
                GameplayWorldRuntime previous = execution;
                execution = next;
                try
                {
                    using var machines = next.Chunks != null ? MachineWorld.UseScope(physics.WorldKey) : default;
                    physics.Simulate(Time.fixedDeltaTime);
                }
                finally { execution = previous; }
            }
        }
        finally { IsPhysicsDispatchInProgress = false; }
    }

    public static void RegisterPresentation(GameObject root)
    {
        if (root == null || root.GetComponentInParent<Player>() != null) return;
        Item item = root.GetComponent<Item>();
        string worldKey;
        if (item != null) worldKey = GameplayWorldRegistry.GetItemWorldKey(item);
        else if (!TryResolveWorldKeyForScene(root.scene, out worldKey)) return;
        if (string.IsNullOrEmpty(worldKey) || !TryGet(worldKey, out GameplayWorldRuntime world) ||
            world.Scene != root.scene) return;
        bool visible = world.WorldKey == (ItemMgr.Instance?.User_Player?.Data?.CurrentSceneName ?? localSceneKey);
        GameplayWorldPresentationOwner owner = root.GetComponent<GameplayWorldPresentationOwner>();
        if (owner == null) owner = root.AddComponent<GameplayWorldPresentationOwner>();
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        owner.Bind(world, renderers);
        foreach (Renderer renderer in renderers)
        {
            if (PresentationOwners.TryGetValue(renderer, out GameplayWorldPresentationOwner previous) && previous != owner &&
                previous != null && previous.transform.IsChildOf(root.transform)) continue;
            PresentationOwners[renderer] = owner;
            if (!world.Renderers.ContainsKey(renderer)) world.Renderers.Add(renderer, renderer.forceRenderingOff);
            if (!visible) renderer.forceRenderingOff = true;
        }
    }

    internal static void RegisterItemPresentation(Item item)
    { if (item != null) RegisterPresentation(item.gameObject); }

    public static bool GetPresentationOriginalForceRenderingOff(Renderer renderer)
        => renderer != null && PresentationOwners.TryGetValue(renderer, out var owner) && owner != null
            ? owner.GetOriginalForceRenderingOff(renderer) : renderer != null && renderer.forceRenderingOff;

    internal static void RefreshVisibility() => RefreshVisibility(LocalPresentationWorldKey);
    private static void RefreshVisibility(string localWorld)
    {
        foreach (GameplayWorldRuntime world in worlds.Values)
        {
            bool visible = world.WorldKey == localWorld;
            foreach (var pair in world.Renderers)
                if (pair.Key != null) pair.Key.forceRenderingOff = visible ? pair.Value : true;
        }
    }
    #endregion
}

internal sealed class GameplayWorldPresentationOwner : MonoBehaviour
{
    private GameplayWorldRuntime world;
    private Renderer[] renderers;
    private bool initialized;
    internal bool GetOriginalForceRenderingOff(Renderer renderer)
        => world != null && world.Renderers.TryGetValue(renderer, out bool original) ? original : renderer.forceRenderingOff;
    public void Bind(GameplayWorldRuntime next, Renderer[] nextRenderers)
    { Release(); world = next; renderers = nextRenderers; initialized = true; }
    private void OnEnable() { if (initialized) GameplayWorldRegistry.RegisterPresentation(gameObject); }
    private void OnDisable() => Release();
    private void OnDestroy() => Release();
    internal void Release()
    {
        if (world == null || renderers == null) return;
        foreach (Renderer renderer in renderers)
        {
            if (ReferenceEquals(renderer, null) || !GameplayWorldRegistry.PresentationOwners.TryGetValue(renderer, out var owner) || owner != this) continue;
            GameplayWorldRegistry.PresentationOwners.Remove(renderer);
            if (world.Renderers.Remove(renderer, out bool original) && renderer != null) renderer.forceRenderingOff = original;
        }
        world = null; renderers = null;
    }
}

[DefaultExecutionOrder(10000)]
internal sealed class GameplayWorldRegistryHost : MonoBehaviour
{
    private GameManager manager;
    private SimulationMode2D previousSimulation;
    #region 世界会话驱动
    private void OnEnable()
    {
        previousSimulation = Physics2D.simulationMode;
        Physics2D.simulationMode = SimulationMode2D.Script;
        manager = GameManager.Instance;
        if (manager != null) manager.Event_GameWorldExit += GameplayWorldRegistry.ReleaseAll;
        ItemMgr.RuntimeItemRegistered += GameplayWorldRegistry.RegisterItemPresentation;
        Item.RuntimeStructureChanged += GameplayWorldRegistry.RegisterItemPresentation;
    }
    private void OnDisable()
    {
        if (manager != null) manager.Event_GameWorldExit -= GameplayWorldRegistry.ReleaseAll;
        manager = null;
        Physics2D.simulationMode = previousSimulation;
        ItemMgr.RuntimeItemRegistered -= GameplayWorldRegistry.RegisterItemPresentation;
        Item.RuntimeStructureChanged -= GameplayWorldRegistry.RegisterItemPresentation;
    }
    private void FixedUpdate() => GameplayWorldRegistry.SimulatePhysics();
    #endregion
}
