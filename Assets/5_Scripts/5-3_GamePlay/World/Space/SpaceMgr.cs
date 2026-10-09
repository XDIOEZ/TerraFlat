using System;
using System.Collections.Generic;
using FlatWorld.Spaceflight;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SpaceMgr : SingletonAutoMono<SpaceMgr>
{
    #region 表现与生命周期
    [ShowInInspector, ReadOnly] public List<Planet> RuntimePlanets = new();
    [ShowInInspector, ReadOnly] public Dictionary<string, Planet> RuntimePlanetDict = new(StringComparer.Ordinal);
    [SerializeField] private Transform runtimePlanetRoot;
    [SerializeField] private bool autoUpdateRuntimePlanets = true;
    [SerializeField] private PlanetData debugPlanetData = new();
    private SpaceStarfield starfield;
    private GameManager eventManager;
    public static SpaceMgr ExistingInstance => instance;

    protected override void Awake()
    {
        base.Awake();
        if (instance != this) return;
        if (runtimePlanetRoot == null)
        {
            var root = new GameObject("RuntimePlanets");
            root.transform.SetParent(transform, false); runtimePlanetRoot = root.transform;
        }
        SceneManager.activeSceneChanged += OnSceneChanged;
        UpdateVisibility();
    }
    public void Start()
    {
        eventManager = GameManager.Instance;
        if (eventManager != null)
        {
            eventManager.Event_GameWorldEnter += OnGameWorldEnter;
            eventManager.Event_GameWorldExit += OnGameWorldExit;
        }
        UpdateVisibility();
    }
    private void OnGameWorldEnter() => UpdateVisibility();
    private void OnGameWorldExit() { Debug_ClearRuntimePlanets(); UpdateVisibility(); }
    private void OnSceneChanged(Scene previous, Scene current) => UpdateVisibility();
    private void UpdateVisibility()
    {
        bool visible = SceneManager.GetActiveScene().name == "SpaceScene";
        if (runtimePlanetRoot != null) runtimePlanetRoot.gameObject.SetActive(visible);
        if (starfield != null) starfield.gameObject.SetActive(visible);
    }
    protected override void OnDestroy()
    {
        SceneManager.activeSceneChanged -= OnSceneChanged;
        if (eventManager != null)
        {
            eventManager.Event_GameWorldEnter -= OnGameWorldEnter;
            eventManager.Event_GameWorldExit -= OnGameWorldExit;
        }
        base.OnDestroy();
    }
    #endregion

    #region 星系加载与存档
    public void Load()
    {
        SpaceSession session = SpaceSession.EnsureLoaded();
        if (session.Universe == null) throw new InvalidOperationException("太空会话尚未准备好");
        Debug_ClearRuntimePlanets();
        SpaceCatalog catalog = SpaceCatalog.LoadDefault();
        foreach (BodyState body in session.Universe.State.Bodies)
        {
            PlanetData data;
            var saves = SaveDataMgr.Instance?.SaveData?.PlanetData_Dict;
            if (saves == null || !saves.TryGetValue(body.PlanetId, out data))
                data = catalog.CreatePlanetData(body.BodyId);
            AddPlanet(data);
        }
        foreach (Planet planet in RuntimePlanets) BindOrbitCenterByData(planet);
        if (starfield == null)
        {
            var background = new GameObject("黑色星空"); background.transform.SetParent(transform, false);
            starfield = background.AddComponent<SpaceStarfield>();
        }
        TickRuntimePlanets(0f); UpdateVisibility();
    }
    // 保存权威数据由会话负责，表现不能把第一个星体覆盖成当前星球。
    public void Save() => SpaceSession.Capture(SaveDataMgr.Instance?.SaveData);
    #endregion

    #region 星体与物品表现
    public void AddPlanet(PlanetData planet)
    {
        if (planet == null) throw new ArgumentNullException(nameof(planet));
        string key = string.IsNullOrWhiteSpace(planet.BodyId) ? planet.RuntimePlanetName : planet.BodyId;
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("星体身份不能为空", nameof(planet));
        if (RuntimePlanetDict.ContainsKey(key)) return;
        string prefabName = string.IsNullOrWhiteSpace(planet.PrefabName) ? planet.RuntimePlanetName : planet.PrefabName;
        GameObject instanceObject = GameRes.Instance.InstantiatePrefab(prefabName, parent: runtimePlanetRoot);
        if (instanceObject == null) throw new InvalidOperationException($"星体外观未加载：{prefabName}");
        Planet runtimePlanet = instanceObject.GetComponent<Planet>();
        if (runtimePlanet == null)
        {
            Destroy(instanceObject);
            throw new InvalidOperationException($"星体外观缺少 Planet：{prefabName}");
        }
        runtimePlanet.planetData = planet; runtimePlanet.name = planet.RuntimePlanetName;
        RuntimePlanets.Add(runtimePlanet); RuntimePlanetDict.Add(key, runtimePlanet);
    }
    private void BindOrbitCenterByData(Planet planet)
    {
        planet.OrbitCenter = FindPlanetByBodyId(planet.planetData?.OrbitCenterBodyId)?.transform ?? planet.transform;
    }
    public Planet FindPlanetByBodyId(string bodyId) =>
        !string.IsNullOrEmpty(bodyId) && RuntimePlanetDict.TryGetValue(bodyId, out Planet planet) ? planet : null;
    public void RemovePlanet(PlanetData planet)
    {
        if (planet == null) throw new ArgumentNullException(nameof(planet));
        string key = string.IsNullOrWhiteSpace(planet.BodyId) ? planet.RuntimePlanetName : planet.BodyId;
        if (!RuntimePlanetDict.TryGetValue(key, out Planet runtimePlanet)) return;
        RuntimePlanetDict.Remove(key); RuntimePlanets.Remove(runtimePlanet);
        if (runtimePlanet != null) Destroy(runtimePlanet.gameObject);
    }
    public Item InstantiateItemNearPlanet(string itemId, string planetBodyId, Vector3 localOffset = default)
    {
        Item item = ItemMgr.Instance.InstantiateItem(itemId, GetSpawnPositionNearPlanet(planetBodyId, localOffset),
            Quaternion.identity, Vector3.one, runtimePlanetRoot.gameObject);
        ActivateRuntimeItem(item); return item;
    }
    public Item InstantiateItemNearPlanet(ItemData data, string planetBodyId, Vector3 localOffset = default)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        Item item = ItemMgr.Instance.InstantiateItem(data, GetSpawnPositionNearPlanet(planetBodyId, localOffset),
            Quaternion.identity, Vector3.one, runtimePlanetRoot.gameObject);
        ActivateRuntimeItem(item); return item;
    }
    private static void ActivateRuntimeItem(Item item)
    {
        if (item == null) throw new InvalidOperationException("太空物品实例化失败");
        item.InjectToItemMgr(); item.Load();
    }
    private Vector3 GetSpawnPositionNearPlanet(string bodyId, Vector3 offset)
    {
        SpaceSession session = SpaceSession.EnsureLoaded();
        BodyState body = session.Universe.GetBody(bodyId);
        SpaceVector2 radial = offset == default ? new SpaceVector2(1d, 0d) :
            SpaceVector2.FromVector2(new Vector2(offset.x, offset.y)).Normalized;
        SpaceVector2 position = body.PositionMeters + radial * (body.RadiusMeters + Math.Max(100d, offset.magnitude));
        Vector2 projected = session.ProjectPosition(position); return new Vector3(projected.x, projected.y, offset.z);
    }
    #endregion

    #region 同一权威状态的渲染
    public void Update()
    {
        if (autoUpdateRuntimePlanets) TickRuntimePlanets(Time.deltaTime);
    }
    public void TickRuntimePlanets(float ignoredDeltaTime)
    {
        SpaceSession session = SpaceSession.Current;
        if (session?.Universe == null) return;
        for (int i = RuntimePlanets.Count - 1; i >= 0; i--)
        {
            Planet planet = RuntimePlanets[i];
            if (planet == null) { RuntimePlanets.RemoveAt(i); continue; }
            if (session.Universe.TryGetBody(planet.planetData?.BodyId, out BodyState body))
                planet.ApplyUniverse(session, body);
        }
    }
    #endregion

    #region 调试入口
    [FoldoutGroup("调试"), ShowInInspector, ReadOnly] public int RuntimePlanetCount => RuntimePlanets.Count;
    [FoldoutGroup("调试"), Button("添加调试星球")] public void Debug_AddPlanet() => AddPlanet(debugPlanetData);
    [FoldoutGroup("调试"), Button("删除调试星球")] public void Debug_RemovePlanet() => RemovePlanet(debugPlanetData);
    [FoldoutGroup("调试"), Button("刷新星体表现")] public void Debug_TickOneFrame() => TickRuntimePlanets(0f);
    [FoldoutGroup("调试"), Button("清空运行时星球")]
    public void Debug_ClearRuntimePlanets()
    {
        foreach (Planet planet in RuntimePlanets) if (planet != null) Destroy(planet.gameObject);
        RuntimePlanets.Clear(); RuntimePlanetDict.Clear();
    }
    [FoldoutGroup("调试"), Button("读取整个太阳系")] public void Debug_CreateSolarSystem() => Load();
    #endregion
}
