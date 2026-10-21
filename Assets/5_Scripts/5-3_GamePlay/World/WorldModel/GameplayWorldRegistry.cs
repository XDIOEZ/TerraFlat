using System;
using System.Collections;
using System.Collections.Generic;
using FlatWorld.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class GameplayWorldRuntime
{
    #region 世界资源所有权
    public string WorldKey { get; internal set; }
    public Scene Scene { get; internal set; }
    public ChunkMgr Chunks { get; internal set; }
    public WorldNavigationManager Navigation { get; internal set; }
    internal bool OwnsPhysics;
    public int PreparationCount { get; internal set; }
    internal readonly Dictionary<Renderer, bool> Renderers = new();
    #endregion
}

public static class GameplayWorldRegistry
{
    #region 世界注册与同步上下文
    private static readonly Dictionary<string, GameplayWorldRuntime> worlds = new(StringComparer.Ordinal);
    private static GameplayWorldRuntime execution;
    internal static readonly Dictionary<Renderer, GameplayWorldPresentationOwner> PresentationOwners = new();
    private static readonly Dictionary<string, GameplayWorldRuntime> spaceContexts = new(StringComparer.Ordinal);
    private static GameplayWorldRegistryHost host;
    private static bool releasing;
    private static readonly List<GameplayWorldRuntime> physicsDispatch = new();
    private static readonly List<GameplayWorldRuntime> releaseDispatch = new();
    private static readonly HashSet<PhysicsScene2D> physicsScenes = new();
    private static bool sceneCacheBound;
    private static Scene localScene;
    private static string localSceneKey;
    private static void BindSceneCache()
    {
        if (sceneCacheBound) return;
        localScene = SceneManager.GetActiveScene(); localSceneKey = localScene.name;
        SceneManager.activeSceneChanged += OnLocalSceneChanged;
        sceneCacheBound = true;
    }
    private static void OnLocalSceneChanged(Scene before, Scene after)
    { localScene = after; localSceneKey = after.name; RefreshVisibility(); WeatherMgr.SelectLocalPresentation(localSceneKey); }
    public static string ExecutionWorldKey { get { BindSceneCache(); return execution?.WorldKey ?? localSceneKey; } }
    public static Scene CurrentScene { get { BindSceneCache(); return execution?.Scene ?? localScene; } }
    public static ChunkMgr ExecutionChunks => execution?.Chunks;
    public static bool HasExecutionContext => execution != null;
    public static string LocalPresentationWorldKey { get { BindSceneCache(); return localSceneKey; } }
    public static IReadOnlyDictionary<string, GameplayWorldRuntime> Worlds => worlds;

    public static bool TryGet(string worldKey, out GameplayWorldRuntime world)
        => worlds.TryGetValue(worldKey ?? string.Empty, out world) && world.Scene.IsValid() && world.Scene.isLoaded;

    public static GameplayWorldRuntime GetOrCreate(string worldKey)
    {
        if (TryGet(worldKey, out GameplayWorldRuntime existing)) return existing;
        if (releasing || string.IsNullOrWhiteSpace(worldKey) || SpaceMgr.IsSpaceScene(worldKey))
            throw new InvalidOperationException("地表运行时必须使用有效的星球维度地址。");
        WorldAddress address = WorldAddress.FromWorldKey(worldKey);
        if (!address.IsValid) throw new ArgumentException("世界地址无效。", nameof(worldKey));
        DimensionManager.Instance.EnsureWorldData(address);
        if (host == null)
        {
            var root = new GameObject("GameplayWorldRegistry");
            UnityEngine.Object.DontDestroyOnLoad(root);
            host = root.AddComponent<GameplayWorldRegistryHost>();
        }
        Scene scene = SceneManager.GetSceneByName(worldKey);
        bool created = !scene.IsValid() || !scene.isLoaded;
        if (created) scene = SceneManager.CreateScene(worldKey, new CreateSceneParameters(LocalPhysicsMode.Physics2D));
        var world = new GameplayWorldRuntime { WorldKey = worldKey, Scene = scene, OwnsPhysics = created };
        worlds[worldKey] = world;
        try
        {
            ChunkMgr local = ChunkMgr.LocalInstance;
            if (local != null && (local.WorldRuntime?.WorldId == worldKey ||
                local.WorldRuntime == null && SceneManager.GetActiveScene().name == worldKey))
            {
                world.Chunks = local;
                local.BindNetworkWorld(worldKey);
            }
        else world.Chunks = ChunkMgr.CreateNetworkWorldManager(worldKey);
        world.Navigation = WorldNavigationManager.GetOrCreateForWorld(worldKey);
        _ = MonsterSpawnerManager.GetForWorld(worldKey);
        _ = WeatherMgr.GetForWorld(worldKey);
            return world;
        }
        catch
        {
            worlds.Remove(worldKey);
            if (world.Chunks != null && world.Chunks != ChunkMgr.LocalInstance) UnityEngine.Object.Destroy(world.Chunks.gameObject);
            if (created && scene.IsValid() && scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
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

    public static IDisposable UseSpaceScene(string worldKey)
    {
        if (!SpaceMgr.IsSpaceScene(worldKey)) throw new ArgumentException("必须指定已存在的真实星区场景。", nameof(worldKey));
        Scene scene = SceneManager.GetSceneByName(worldKey);
        if (!scene.IsValid() || !scene.isLoaded) return null;
        GameplayWorldRuntime previous = execution;
        if (!spaceContexts.TryGetValue(worldKey, out GameplayWorldRuntime context) || context.Scene != scene)
            spaceContexts[worldKey] = context = new GameplayWorldRuntime { WorldKey = worldKey, Scene = scene };
        execution = context;
        return new WorldScope(previous, null);
    }

    public static void ReleaseSpaceContext(string worldKey) => spaceContexts.Remove(worldKey);

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
        ChunkMgr.SelectLocalNetworkWorld(GetOrCreate(worldKey).Chunks);
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
            releaseDispatch.Clear(); releaseDispatch.AddRange(worlds.Values);
            WeatherMgr.ReleaseAll();
            MonsterSpawnerManager.ReleaseAll();
            // 管理器在注册表仍可查询时按自己的世界同步释放，延迟OnDestroy只执行已关闭分支。
            foreach (GameplayWorldRuntime world in releaseDispatch)
            {
                using (UseWorld(world.WorldKey)) world.Chunks?.ReleaseRegisteredWorld();
            }
            WorldEntityRuntime.ReleaseAll();
            FlatWorld.NaturalEntities.NaturalEntityEcsService.ReleaseAll();
            BuildingOccupancyRegistry.ReleaseAll();
            WorldLiquidFlowObstacles.ReleaseAll();
            foreach (GameplayWorldRuntime world in releaseDispatch)
            {
                AiRuntimeBackendService.ReleaseWorldEcology(world.WorldKey);
                WorldNavigationManager.ReleaseWorld(world.WorldKey);
                foreach (var pair in world.Renderers) if (pair.Key != null) pair.Key.forceRenderingOff = pair.Value;
                if (world.Chunks != null) UnityEngine.Object.Destroy(world.Chunks.gameObject);
                if (world.OwnsPhysics && world.Scene.IsValid() && world.Scene.isLoaded)
                    SceneManager.UnloadSceneAsync(world.Scene);
            }
            ChunkMgr.ClearLocalNetworkWorld();
            worlds.Clear(); releaseDispatch.Clear(); spaceContexts.Clear(); PresentationOwners.Clear();
            if (host != null) UnityEngine.Object.Destroy(host.gameObject);
            host = null;
        }
        finally { releasing = false; }
    }
    #endregion

    #region 独立场景物理与本机显示
    internal static void SimulatePhysics()
    {
        if (Time.timeScale <= 0f) return;
        physicsDispatch.Clear(); physicsDispatch.AddRange(worlds.Values); physicsScenes.Clear();
        foreach (GameplayWorldRuntime world in physicsDispatch)
        {
            if (!world.Scene.IsValid() || !world.Scene.isLoaded || !TryGet(world.WorldKey, out GameplayWorldRuntime current) || current != world) continue;
            using (UseWorld(world.WorldKey))
            {
                PhysicsScene2D physics = world.Scene.GetPhysicsScene2D();
                if (physics.IsValid() && physicsScenes.Add(physics)) physics.Simulate(Time.fixedDeltaTime);
            }
        }
    }

    public static void RegisterPresentation(GameObject root)
    {
        if (root == null || root.GetComponentInParent<Player>() != null ||
            !TryGet(root.scene.name, out GameplayWorldRuntime world)) return;
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

    internal static void RefreshVisibility() => RefreshVisibility(ItemMgr.Instance?.User_Player?.Data?.CurrentSceneName ?? localSceneKey);
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
    public void Bind(GameplayWorldRuntime next, Renderer[] nextRenderers)
    { Release(); world = next; renderers = nextRenderers; initialized = true; }
    private void OnEnable() { if (initialized) GameplayWorldRegistry.RegisterPresentation(gameObject); }
    private void OnDisable() => Release();
    private void OnDestroy() => Release();
    private void Release()
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
