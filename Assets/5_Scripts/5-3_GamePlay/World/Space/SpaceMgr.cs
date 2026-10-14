using System;
using System.Collections;
using System.Collections.Generic;
using FlatWorld.Spaceflight;
using FlatWorld.Networking;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.SceneManagement;

public partial class SpaceMgr : SingletonAutoMono<SpaceMgr>
{
    #region 星区场景资源租约
    private sealed class SectorView
    {
        public SpaceSectorId Sector;
        public Scene Scene;
        public Transform Root;
        public bool Wanted, Unloading, PresentationReady;
        public Coroutine Loading;
        public readonly List<Planet> Planets = new();
        public SpaceSessionState SessionState;
        public UniverseState Data;
        public GameRes ResourceOwner;
        public ulong ResourceRevision;
        public long LastCached;
    }
    public List<Planet> RuntimePlanets = new();
    private readonly Dictionary<string, Planet> runtimePlanetDict = new(StringComparer.Ordinal);
    private readonly Dictionary<SpaceSectorId, SectorView> sectors = new();
    private readonly List<SectorView> releaseScratch = new();
    private SpaceStarfield starfield;
    private PlanetData debugPlanetData = new();
    private bool presentationLoaded;
    private GameManager eventOwner;
    public static SpaceMgr ExistingInstance => instance;
    public static bool LocalSpacePresentationActive { get; private set; }
    public static bool IsSpaceScene(string name) => name == "SpaceScene" || SpaceSectorCoordinates.TryParseSceneKey(name, out _);

    protected override void Awake()
    {
        base.Awake();
        if (instance != this) return;
        if (!GameNetwork.IsOnline) DontDestroyOnLoad(transform.root.gameObject);
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
    }
    private void Start()
    {
        eventOwner = GameManager.Instance;
        if (eventOwner != null)
        {
            eventOwner.Event_GameWorldEnter += UpdateVisibility;
            eventOwner.Event_GameWorldExit += OnWorldExit;
        }
        UpdateVisibility();
    }
    protected override void OnDestroy()
    {
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        if (eventOwner != null)
        {
            eventOwner.Event_GameWorldEnter -= UpdateVisibility;
            eventOwner.Event_GameWorldExit -= OnWorldExit;
        }
        LocalSpacePresentationActive = false;
        ClearRetainedSectors();
        base.OnDestroy();
    }
    private void OnActiveSceneChanged(Scene before, Scene after) => UpdateVisibility();
    private void OnWorldExit()
    {
        bool retain = SectorPresentationCachingEnabled && !GameNetwork.IsOnline && GameManager.Instance?.IsInGameWorld == true;
        if (!retain) ClearRetainedSectors();
        releaseScratch.Clear(); releaseScratch.AddRange(sectors.Values);
        foreach (SectorView view in releaseScratch)
            if (!view.Unloading) StartCoroutine(ReleaseSector(view, retain));
        presentationLoaded = false;
        UpdateVisibility();
    }
    private void UpdateVisibility()
    {
        string sceneName = SceneManager.GetActiveScene().name;
        bool visible = IsSpaceScene(sceneName);
        if (visible) EnsureSpaceSceneTime(sceneName);
        LocalSpacePresentationActive = visible;
        foreach (SectorView view in sectors.Values)
            if (view.Root != null)
                view.Root.gameObject.SetActive(visible && (GameNetwork.IsOnline || view.Wanted && !view.Unloading));
        if (starfield != null) starfield.gameObject.SetActive(visible);
    }
    private void EnsureBackground()
    {
        if (starfield != null) return;
        GameObject background = new("黑色星空");
        background.transform.SetParent(transform, false);
        starfield = background.AddComponent<SpaceStarfield>();
    }
    private SectorView EnsureSectorScene(SpaceSession session, SpaceSectorId sector)
    {
        if (sectors.TryGetValue(sector, out SectorView existing))
        {
            existing.Wanted = true;
            if (existing.Unloading) return existing;
            if (existing.Scene.IsValid() && existing.Scene.isLoaded && existing.Root != null) return existing;
            foreach (Planet planet in existing.Planets) if (planet != null) { RuntimePlanets.Remove(planet); runtimePlanetDict.Remove(planet.planetData.BodyId); }
            sectors.Remove(sector);
        }
        UniverseSimulation data = session.GetUniverse(sector);
        string key = SpaceSectorCoordinates.SceneKey(sector);
        Scene scene = SceneManager.GetSceneByName(key);
        if (!scene.IsValid() || !scene.isLoaded) scene = SceneManager.CreateScene(key);
        if (TryRestoreRetainedSector(session, sector, scene, out SectorView restored))
        {
            sectors.Add(sector, restored);
            return restored;
        }
        GameObject root = new("星区资源_" + key);
        SceneManager.MoveGameObjectToScene(root, scene);
        var view = new SectorView { Sector = sector, Scene = scene, Root = root.transform, Wanted = true,
            SessionState = session.State, Data = data.State, ResourceOwner = GameRes.ExistingInstance,
            ResourceRevision = GameRes.ExistingInstance?.ItemDefinitionRevision ?? 0 };
        sectors.Add(sector, view);
        SectorPresentationCreatedCount++;
        view.Loading = StartCoroutine(LoadSectorPresentation(session, view, data.State));
        if (view.PresentationReady) view.Loading = null;
        return view;
    }

    #region 星区时间与采光归属
    private static void EnsureSpaceSceneTime(string sceneName)
    {
        GameManager manager = GameManager.Instance;
        DayTimeSystem time = DayTimeSystem.GetInstance();
        if (manager?.IsInGameWorld != true || time == null) return;
        const string clockScene = "SpaceScene";
        // 所有星区引用同一个太空时钟，跨区只换表现，不重置昼夜或丢失采光登记。
        if (!time.WorldTimeDict.TryGetValue(clockScene, out TimeData shared) || shared == null)
        {
            TimeData seed = time.TryGetResolvedTimeData(sceneName, out _, out TimeData current)
                ? current : manager.ReadyTimeData;
            time.EnsureSceneTimeData(clockScene, seed);
            time.SetReferenceScene(clockScene, string.Empty);
            shared = time.WorldTimeDict[clockScene];
        }
        if (sceneName == clockScene) return;
        if (time.WorldTimeDict.TryGetValue(sceneName, out TimeData existing) && existing != null &&
            existing.ReferenceScene == clockScene && time.SceneLightingRateDict.ContainsKey(sceneName)) return;
        time.EnsureSceneTimeData(sceneName, shared);
        time.SetReferenceScene(sceneName, clockScene);
    }
    #endregion

    private IEnumerator LoadSectorPresentation(SpaceSession session, SectorView view, UniverseState data)
    {
        var failures = new HashSet<string>(StringComparer.Ordinal);
        while (view.Wanted && !view.Unloading && view.Root != null)
        {
            bool pending = false;
            foreach (BodyState body in data.Bodies)
            {
                if (!view.Wanted || view.Unloading || view.Root == null) yield break;
                if (runtimePlanetDict.TryGetValue(body.BodyId, out Planet found) && found != null) continue;
                pending = true;
                try
                {
                    if (!SaveDataMgr.Instance.SaveData.PlanetData_Dict.TryGetValue(body.PlanetId, out PlanetData planet))
                        throw new InvalidOperationException("星体数据尚未登记：" + body.PlanetId);
                    AddPlanet(planet, view);
                    FindPlanetByBodyId(body.BodyId)?.ApplyUniverse(session, body);
                    failures.Remove(body.BodyId);
                }
                catch (Exception error)
                {
                    // 外观故障可重试，权威运动和碰撞不等待这段协程。
                    if (failures.Add(body.BodyId)) Debug.LogException(error, this);
                }
                yield return null;
            }
            if (!pending || !GameNetwork.IsOnline && IsSectorPresentationComplete(view))
            {
                view.PresentationReady = IsSectorPresentationComplete(view);
                break;
            }
            for (int frame = 0; frame < 120 && view.Wanted && !view.Unloading; frame++) yield return null;
        }
        view.Loading = null;
    }
    public void UpdateSectorLeases(SpaceSession session, IReadOnlyCollection<SpaceSectorId> required)
    {
        PruneRetainedSectors(session);
        foreach (SectorView view in sectors.Values) view.Wanted = false;
        foreach (SpaceSectorId sector in required) EnsureSectorScene(session, sector);
        Player player = ItemMgr.Instance?.User_Player;
        if (player != null && session.TryGetNetworkObserver(player.ProfileName, out SpaceNetworkObserver observer) && observer.InSpace)
        {
            SectorView current = EnsureSectorScene(session, observer.Position.Sector);
            if (!current.Unloading && current.Scene.IsValid() && current.Scene.isLoaded)
            {
                EnsureSpaceSceneTime(current.Scene.name);
                if (SceneManager.GetActiveScene() != current.Scene) SceneManager.SetActiveScene(current.Scene);
                MovePresentationToSector(player.gameObject, observer.Position.Sector);
            }
            EnsureBackground();
        }
        releaseScratch.Clear();
        foreach (SectorView view in sectors.Values) if (!view.Wanted && !view.Unloading) releaseScratch.Add(view);
        foreach (SectorView view in releaseScratch) StartCoroutine(ReleaseSector(view));
        UpdateVisibility();
    }
    private IEnumerator ReleaseSector(SectorView view, bool allowRetention = true)
    {
        view.Unloading = true;
        // 高速跳过多个区时先保护真实玩家，资源卸载不能顺带销毁角色。
        if (ItemMgr.Instance != null)
            foreach (Player player in ItemMgr.Instance.Player_DIC.Values)
                if (player != null && player.gameObject.scene == view.Scene)
                { player.transform.SetParent(null, true); DontDestroyOnLoad(player.gameObject); }
        if (view.Loading != null) StopCoroutine(view.Loading);
        bool retain = allowRetention && TryRetainSector(view);
        view.Loading = null;
        foreach (Planet planet in view.Planets)
            if (planet != null) { RuntimePlanets.Remove(planet); runtimePlanetDict.Remove(planet.planetData.BodyId); }
        if (!retain)
        {
            view.Planets.Clear();
            if (view.Root != null) Destroy(view.Root.gameObject);
        }
        if (view.Scene.IsValid() && view.Scene.isLoaded)
        {
            AsyncOperation unload = SceneManager.UnloadSceneAsync(view.Scene);
            if (unload != null) yield return unload;
        }
        if (sectors.TryGetValue(view.Sector, out SectorView current) && ReferenceEquals(current, view))
            sectors.Remove(view.Sector);
        view.Unloading = false;
        view.Scene = default;
    }
    public void MovePresentationToSector(GameObject item, SpaceSectorId sector)
    {
        if (item == null || !sectors.TryGetValue(sector, out SectorView view) || view.Unloading || !view.Scene.IsValid() || !view.Scene.isLoaded) return;
        if (item.scene == view.Scene) return;
        item.transform.SetParent(null, true);
        SceneManager.MoveGameObjectToScene(item, view.Scene);
        item.transform.SetParent(view.Root, true);
    }
    #endregion

    #region 太空进入和本机镜头
    public void Load()
    {
        SpaceSession.EnsureLoaded();
        if (IsSpaceScene(SceneManager.GetActiveScene().name)) StartCoroutine(LoadPresentation());
    }
    public IEnumerator ActivateLocalSector(SpaceSectorId sector)
    {
        SpaceSession session = SpaceSession.EnsureLoaded();
        SectorView view = EnsureSectorScene(session, sector);
        while (view.Unloading) { yield return null; view = EnsureSectorScene(session, sector); }
        SceneManager.SetActiveScene(view.Scene);
        EnsureBackground(); UpdateVisibility();
        presentationLoaded = true;
    }
    public IEnumerator LoadPresentation()
    {
        SpaceSession session = SpaceSession.EnsureLoaded();
        Player player = ItemMgr.Instance?.User_Player;
        SpaceSectorId sector = player != null && session.TryGetNetworkObserver(player.ProfileName, out SpaceNetworkObserver observer)
            ? observer.Position.Sector : session.ViewSector;
        if (player == null && session.TryGetNetworkObserver(SaveDataMgr.Instance.CurrentContrrolPlayerName, out SpaceNetworkObserver saved))
            sector = saved.Position.Sector;
        yield return ActivateLocalSector(sector);
    }
    public IEnumerator PreparePresentationForCamera()
    {
        if (!presentationLoaded) yield return LoadPresentation();
        // 世界时间载入完成后再补一次，避免首个星区采样早于下一次租约维护。
        EnsureSpaceSceneTime(SceneManager.GetActiveScene().name);
        for (int frame = 0; frame < 300; frame++)
        {
            Camera camera = Camera.main;
            if (camera == null) { yield return null; continue; }
            TickRuntimePlanets(0f); starfield.Prepare(camera);
            yield return null;
            if (camera == Camera.main && starfield.IsPreparedFor(camera)) yield break;
        }
        throw new TimeoutException("太空场景玩家镜头未就绪");
    }
    public void Save() => SpaceSession.Capture(SaveDataMgr.Instance?.SaveData);
    #endregion

    #region 星体表现
    public void AddPlanet(PlanetData planet)
    {
        SpaceSession session = SpaceSession.EnsureLoaded();
        BodyState body = session.GetBody(planet.BodyId);
        AddPlanet(planet, EnsureSectorScene(session, body.Sector));
    }
    private void AddPlanet(PlanetData planet, SectorView view)
    {
        if (runtimePlanetDict.TryGetValue(planet.BodyId, out Planet existing) && existing != null) return;
        GameObject instance = GameRes.Instance.InstantiatePrefab(string.IsNullOrWhiteSpace(planet.PrefabName) ? planet.RuntimePlanetName : planet.PrefabName, parent: view.Root);
        if (instance == null) throw new InvalidOperationException("星体外观未加载：" + planet.PrefabName);
        Planet runtime = instance.GetComponent<Planet>();
        if (runtime == null) { Destroy(instance); throw new InvalidOperationException("星体预制体缺少 Planet。"); }
        runtime.planetData = planet; runtime.name = planet.RuntimePlanetName;
        RuntimePlanets.Add(runtime); view.Planets.Add(runtime); runtimePlanetDict[planet.BodyId] = runtime;
    }
    public Planet FindPlanetByBodyId(string id) => id != null && runtimePlanetDict.TryGetValue(id, out Planet planet) ? planet : null;
    public void RemovePlanet(PlanetData planet)
    {
        if (planet == null || !runtimePlanetDict.Remove(planet.BodyId, out Planet runtime)) return;
        RuntimePlanets.Remove(runtime);
        foreach (SectorView view in sectors.Values) view.Planets.Remove(runtime);
        if (runtime != null) Destroy(runtime.gameObject);
    }
    public void TickRuntimePlanets(float ignored)
    {
        if (!IsSpaceScene(SceneManager.GetActiveScene().name)) return;
        SpaceSession session = SpaceSession.Current;
        if (session == null) return;
        foreach (Planet planet in RuntimePlanets)
            if (planet != null && session.TryGetBody(planet.planetData.BodyId, out BodyState body)) planet.ApplyUniverse(session, body);
    }
    public Item InstantiateItemNearPlanet(string itemId, string bodyId, Vector3 offset = default)
        => ActivateItem(ItemMgr.Instance.InstantiateItem(itemId, GetSpawnPositionNearPlanet(bodyId, offset), Quaternion.identity, Vector3.one));
    public Item InstantiateItemNearPlanet(ItemData data, string bodyId, Vector3 offset = default)
        => ActivateItem(ItemMgr.Instance.InstantiateItem(data, GetSpawnPositionNearPlanet(bodyId, offset), Quaternion.identity, Vector3.one));
    private static Item ActivateItem(Item item) { item.InjectToItemMgr(); item.Load(); return item; }
    private Vector3 GetSpawnPositionNearPlanet(string id, Vector3 offset)
    {
        SpaceSession session = SpaceSession.EnsureLoaded(); BodyState body = session.GetBody(id);
        SpaceVector2 radial = offset == default ? new SpaceVector2(1d, 0d) : SpaceVector2.FromVector2(offset).Normalized;
        Vector2 point = session.ProjectPosition(new SpaceSectorPosition(body.Sector, body.PositionMeters + radial * (body.RadiusMeters + Math.Max(100d, offset.magnitude))));
        return new Vector3(point.x, point.y, offset.z);
    }
    [FoldoutGroup("调试"), ShowInInspector, ReadOnly] public int RuntimePlanetCount => RuntimePlanets.Count;
    [FoldoutGroup("调试"), Button("添加调试星球")] public void Debug_AddPlanet() => AddPlanet(debugPlanetData);
    [FoldoutGroup("调试"), Button("删除调试星球")] public void Debug_RemovePlanet() => RemovePlanet(debugPlanetData);
    [FoldoutGroup("调试"), Button("刷新星体表现")] public void Debug_TickOneFrame() => TickRuntimePlanets(0f);
    [FoldoutGroup("调试"), Button("清空运行时星区")]
    public void Debug_ClearRuntimePlanets()
    {
        ClearRetainedSectors();
        releaseScratch.Clear(); releaseScratch.AddRange(sectors.Values);
        foreach (SectorView view in releaseScratch) if (!view.Unloading) StartCoroutine(ReleaseSector(view, false));
        presentationLoaded = false;
    }
    [FoldoutGroup("调试"), Button("读取当前星区")] public void Debug_CreateSolarSystem() => Load();
    #endregion
}
