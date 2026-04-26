using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

public class SpaceMgr : SingletonAutoMono<SpaceMgr>
{
#region 字段

    [ShowInInspector, ReadOnly]
    public List<Planet> RuntimePlanets = new List<Planet>(); // 运行时星球列表

    [ShowInInspector, ReadOnly]
    public Dictionary<string, Planet> RuntimePlanetDict = new(); // 运行时星球字典（Key=Name）

    [SerializeField]
    private Transform runtimePlanetRoot; // 运行时星球父节点

    [SerializeField]
    private bool autoUpdateRuntimePlanets = true; // 是否自动更新星球运行

    [SerializeField, MinValue(0f)]
    private float runtimeTimeScale = 1f; // 运行时速度倍率

    [FoldoutGroup("调试"), SerializeField]
    private PlanetData debugPlanetData = new PlanetData(); // 调试用星球数据

    [FoldoutGroup("调试"), SerializeField, MinValue(0.1f)]
    private float solarSystemOrbitSpacingScale = 1f; // 太阳系轨道间隔系数

#endregion

#region 生命周期

    protected override void Awake()
    {
        base.Awake();

        if (runtimePlanetRoot == null)
        {
            GameObject root = new GameObject("RuntimePlanets");
            root.transform.SetParent(transform);
            runtimePlanetRoot = root.transform;
        }
    }

    public void Start()
    {
        // 初始状态禁用 Update，等玩家进入游戏世界后由事件激活
        enabled = false;
        GameManager.Event_GameWorldEnter += OnGameWorldEnter;
        GameManager.Event_GameWorldExit += OnGameWorldExit;
    }

    private void OnGameWorldEnter()
    {
        enabled = true;
    }

    private void OnGameWorldExit()
    {
        enabled = false;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        GameManager.Event_GameWorldEnter -= OnGameWorldEnter;
        GameManager.Event_GameWorldExit -= OnGameWorldExit;
    }

#endregion

#region 存档接口

    public void Load()
    {
        RuntimePlanets.RemoveAll(p => p == null);

        // 当前项目 GameManager 里维护的是单个 ReadyPlanetData，这里直接接入生命周期
        PlanetData readyData = GameManager.Instance.ReadyPlanetData;
        if (!string.IsNullOrEmpty(readyData.RuntimePlanetName))
        {
            AddPlanet(readyData);
        }
    }

    public void Save()
    {
        // 当前项目先保存第一个运行时星球到 ReadyPlanetData，后续可扩展为列表存档
        if (RuntimePlanets.Count <= 0)
        {
            return;
        }

        Planet planet = RuntimePlanets[0];
        if (planet != null)
        {
            GameManager.Instance.ReadyPlanetData = planet.planetData;
        }
    }

#endregion

#region 星球管理

    public void AddPlanet(PlanetData planet)
    {
        if (planet == null)
        {
            throw new System.ArgumentNullException(nameof(planet), "planet 不能为空");
        }

        string planetName = planet.RuntimePlanetName;
        if (string.IsNullOrEmpty(planetName))
        {
            throw new System.ArgumentException("planet.Name / planet.name / planet.PrefabName 至少要有一个", nameof(planet));
        }

        if (RuntimePlanetDict.ContainsKey(planetName))
        {
            Debug.LogWarning($"[SpaceMgr] 已存在同名运行时星球，跳过添加: {planetName}");
            return;
        }

        string prefabName = string.IsNullOrEmpty(planet.PrefabName) ? planetName : planet.PrefabName;
        GameObject planetObj = GameRes.Instance.InstantiatePrefab(prefabName, parent: runtimePlanetRoot);
        if (planetObj == null)
        {
            throw new System.InvalidOperationException($"[SpaceMgr] 无法实例化星球预制体: {prefabName}");
        }

        Planet runtimePlanet = planetObj.GetComponent<Planet>();
        if (runtimePlanet == null)
        {
            throw new System.InvalidOperationException($"[SpaceMgr] 预制体缺少 Planet 组件: {planetName}");
        }

        runtimePlanet.planetData = planet;
    runtimePlanet.name = planetName;
    BindOrbitCenterByData(runtimePlanet);
        planet.InitializeRuntime();
        planet.RunPlanet(runtimePlanet.transform, runtimePlanet.GetOrbitCenterPosition(), 0f);

        RuntimePlanets.Add(runtimePlanet);
        RuntimePlanetDict.Add(planetName, runtimePlanet);
    }

    private void BindOrbitCenterByData(Planet runtimePlanet)
    {
        PlanetData data = runtimePlanet.planetData;

        if (string.IsNullOrEmpty(data.OrbitCenterBodyId))
        {
            runtimePlanet.OrbitCenter = runtimePlanet.transform;
            return;
        }

        Planet centerPlanet = FindPlanetByBodyId(data.OrbitCenterBodyId);
        if (centerPlanet == null)
        {
            throw new System.InvalidOperationException($"[SpaceMgr] 未找到公转中心星体，OrbitCenterBodyId={data.OrbitCenterBodyId}, BodyId={data.BodyId}");
        }

        runtimePlanet.OrbitCenter = centerPlanet.transform;
    }

    private Planet FindPlanetByBodyId(string bodyId)
    {
        if (string.IsNullOrEmpty(bodyId))
        {
            return null;
        }

        for (int i = 0; i < RuntimePlanets.Count; i++)
        {
            Planet p = RuntimePlanets[i];
            if (p == null || p.planetData == null)
            {
                continue;
            }

            if (p.planetData.BodyId == bodyId)
            {
                return p;
            }
        }

        return null;
    }

    public void RemovePlanet(PlanetData planet)
    {
        if (planet == null)
        {
            throw new System.ArgumentNullException(nameof(planet), "planet 不能为空");
        }

        string planetName = planet.RuntimePlanetName;
        if (string.IsNullOrEmpty(planetName))
        {
            throw new System.ArgumentException("planet.Name / planet.name / planet.PrefabName 至少要有一个", nameof(planet));
        }

        if (!RuntimePlanetDict.TryGetValue(planetName, out Planet runtimePlanet))
        {
            Debug.LogWarning($"[SpaceMgr] 未找到要移除的星球: {planetName}");
            return;
        }

        RuntimePlanets.Remove(runtimePlanet);
        RuntimePlanetDict.Remove(planetName);

        if (runtimePlanet != null)
        {
            Destroy(runtimePlanet.gameObject);
        }
    }

    public Item InstantiateItemNearPlanet(string itemId, string planetBodyId, Vector3 localOffset = default)
    {
        if (string.IsNullOrEmpty(itemId))
        {
            throw new System.ArgumentException("itemId 不能为空", nameof(itemId));
        }

        Vector3 spawnPosition = GetSpawnPositionNearPlanet(planetBodyId, localOffset);
        Item runtimeItem = ItemMgr.Instance.InstantiateItem(itemId, spawnPosition, Quaternion.identity, Vector3.one, runtimePlanetRoot != null ? runtimePlanetRoot.gameObject : gameObject);
        ActivateRuntimeItem(runtimeItem, itemId);

        Debug.Log($"[SpaceMgr] 太空场景实例化Item成功，itemId={itemId}, planetBodyId={planetBodyId}, pos={spawnPosition}");
        return runtimeItem;
    }

    public Item InstantiateItemNearPlanet(ItemData itemData, string planetBodyId, Vector3 localOffset = default)
    {
        if (itemData == null)
        {
            throw new System.ArgumentNullException(nameof(itemData), "itemData 不能为空");
        }

        Vector3 spawnPosition = GetSpawnPositionNearPlanet(planetBodyId, localOffset);
        Item runtimeItem = ItemMgr.Instance.InstantiateItem(itemData, spawnPosition, Quaternion.identity, Vector3.one, runtimePlanetRoot != null ? runtimePlanetRoot.gameObject : gameObject);
        ActivateRuntimeItem(runtimeItem, itemData.IDName);

        Debug.Log($"[SpaceMgr] 太空场景实例化Item成功，itemId={itemData.IDName}, planetBodyId={planetBodyId}, pos={spawnPosition}");
        return runtimeItem;
    }

    private void ActivateRuntimeItem(Item runtimeItem, string itemId)
    {
        if (runtimeItem == null)
        {
            throw new System.InvalidOperationException($"[SpaceMgr] 实例化失败，Item为空，itemId={itemId}");
        }

        runtimeItem.InjectToItemMgr();
        runtimeItem.Load();
    }

    private Vector3 GetSpawnPositionNearPlanet(string planetBodyId, Vector3 localOffset)
    {
        if (string.IsNullOrEmpty(planetBodyId))
        {
            throw new System.ArgumentException("planetBodyId 不能为空", nameof(planetBodyId));
        }

        Planet targetPlanet = FindPlanetByBodyId(planetBodyId);
        if (targetPlanet == null)
        {
            throw new System.InvalidOperationException($"[SpaceMgr] 未找到目标星球，planetBodyId={planetBodyId}");
        }

        if (localOffset == default)
        {
            localOffset = new Vector3(2f, 0f, 0f);
        }

        return targetPlanet.transform.position + localOffset;
    }

#endregion

#region 运行更新

    public void Update()
    {
        if (!autoUpdateRuntimePlanets)
        {
            return;
        }

        TickRuntimePlanets(Time.deltaTime * runtimeTimeScale);
    }

    public void TickRuntimePlanets(float deltaTime)
    {
        for (int i = RuntimePlanets.Count - 1; i >= 0; i--)
        {
            Planet runtimePlanet = RuntimePlanets[i];
            if (runtimePlanet == null)
            {
                RuntimePlanets.RemoveAt(i);
                continue;
            }

            PlanetData data = runtimePlanet.planetData;
            if (data == null)
            {
                throw new System.InvalidOperationException($"[SpaceMgr] PlanetData 为空，星球对象: {runtimePlanet.name}");
            }

            data.RunPlanet(runtimePlanet.transform, runtimePlanet.GetOrbitCenterPosition(), deltaTime);
        }
    }

#endregion

#region Odin调试

    [FoldoutGroup("调试"), ShowInInspector, ReadOnly]
    public int RuntimePlanetCount => RuntimePlanets.Count;

    [FoldoutGroup("调试"), Button("添加调试星球")]
    public void Debug_AddPlanet()
    {
        AddPlanet(debugPlanetData);
    }

    [FoldoutGroup("调试"), Button("删除调试星球")]
    public void Debug_RemovePlanet()
    {
        RemovePlanet(debugPlanetData);
    }

    [FoldoutGroup("调试"), Button("手动运行一帧")]
    public void Debug_TickOneFrame()
    {
        TickRuntimePlanets(Time.deltaTime > 0f ? Time.deltaTime : 0.016f);
    }

    [FoldoutGroup("调试"), Button("清空运行时星球")]
    public void Debug_ClearRuntimePlanets()
    {
        for (int i = RuntimePlanets.Count - 1; i >= 0; i--)
        {
            Planet runtimePlanet = RuntimePlanets[i];
            if (runtimePlanet != null)
            {
                Destroy(runtimePlanet.gameObject);
            }
        }

        RuntimePlanets.Clear();
        RuntimePlanetDict.Clear();
    }

    [FoldoutGroup("调试"), Button("创建整个太阳系")]
    public void Debug_CreateSolarSystem()
    {
        Debug_ClearRuntimePlanets();

        // 参考值：地球公转一周 = 1440 * 16 秒
        float earthOrbitPeriodSeconds = 1440f * 16f;
        // 参考值：地球自转一周 = 1440 秒
        float earthSelfRotatePeriodSeconds = 1440f;

        float SpeedByEarthYearRatio(float earthYearRatio)
        {
            float periodSeconds = earthOrbitPeriodSeconds * earthYearRatio;
            return 360f / periodSeconds;
        }

        float SelfRotateSpeedByEarthDayRatio(float earthDayRatio)
        {
            float periodSeconds = earthSelfRotatePeriodSeconds * Mathf.Abs(earthDayRatio);
            float direction = earthDayRatio >= 0f ? 1f : -1f;
            return 360f / periodSeconds * direction;
        }

        float OrbitRadiusByScale(float orbitRadius)
        {
            return orbitRadius * solarSystemOrbitSpacingScale;
        }

        List<PlanetData> solarSystemPlanets = new List<PlanetData>
        {
            new PlanetData { name = "太阳", BodyId = "sun", OrbitCenterBodyId = "", PrefabName = "太阳", OrbitRadius = 0f, OrbitAngularSpeed = 0f, SelfRotateSpeed = SelfRotateSpeedByEarthDayRatio(24.47f) },
            new PlanetData { name = "水星", BodyId = "mercury", OrbitCenterBodyId = "sun", PrefabName = "水星", OrbitRadius = OrbitRadiusByScale(10f), OrbitAngularSpeed = SpeedByEarthYearRatio(0.2408467f), OrbitStartAngle = 15f, SelfRotateSpeed = SelfRotateSpeedByEarthDayRatio(58.646f) },
            new PlanetData { name = "金星", BodyId = "venus", OrbitCenterBodyId = "sun", PrefabName = "金星", OrbitRadius = OrbitRadiusByScale(14f), OrbitAngularSpeed = SpeedByEarthYearRatio(0.61519726f), OrbitStartAngle = 75f, OrbitClockwise = true, SelfRotateSpeed = SelfRotateSpeedByEarthDayRatio(-243.025f) },
            new PlanetData { name = "地球", BodyId = "earth", OrbitCenterBodyId = "sun", PrefabName = "地球", OrbitRadius = OrbitRadiusByScale(18f), OrbitAngularSpeed = SpeedByEarthYearRatio(1f), OrbitStartAngle = 130f, SelfRotateSpeed = SelfRotateSpeedByEarthDayRatio(1f) },
            new PlanetData { name = "月亮", BodyId = "moon", OrbitCenterBodyId = "earth", PrefabName = "月亮", OrbitRadius = OrbitRadiusByScale(4f), OrbitAngularSpeed = SpeedByEarthYearRatio(0.074801f), OrbitStartAngle = 35f, SelfRotateSpeed = SelfRotateSpeedByEarthDayRatio(27.321661f) },
            new PlanetData { name = "火星", BodyId = "mars", OrbitCenterBodyId = "sun", PrefabName = "火星", OrbitRadius = OrbitRadiusByScale(23f), OrbitAngularSpeed = SpeedByEarthYearRatio(1.8808f), OrbitStartAngle = 220f, SelfRotateSpeed = SelfRotateSpeedByEarthDayRatio(1.025957f) },
            new PlanetData { name = "木星", BodyId = "jupiter", OrbitCenterBodyId = "sun", PrefabName = "木星", OrbitRadius = OrbitRadiusByScale(31f), OrbitAngularSpeed = SpeedByEarthYearRatio(11.862f), OrbitStartAngle = 300f, SelfRotateSpeed = SelfRotateSpeedByEarthDayRatio(0.41354f) },
            new PlanetData { name = "土星", BodyId = "saturn", OrbitCenterBodyId = "sun", PrefabName = "土星", OrbitRadius = OrbitRadiusByScale(40f), OrbitAngularSpeed = SpeedByEarthYearRatio(29.457f), OrbitStartAngle = 20f, SelfRotateSpeed = SelfRotateSpeedByEarthDayRatio(0.44401f) },
            new PlanetData { name = "天王星", BodyId = "uranus", OrbitCenterBodyId = "sun", PrefabName = "天王星", OrbitRadius = OrbitRadiusByScale(48f), OrbitAngularSpeed = SpeedByEarthYearRatio(84.016846f), OrbitStartAngle = 160f, SelfRotateSpeed = SelfRotateSpeedByEarthDayRatio(-0.71833f) },
            new PlanetData { name = "海王星", BodyId = "neptune", OrbitCenterBodyId = "sun", PrefabName = "海王星", OrbitRadius = OrbitRadiusByScale(56f), OrbitAngularSpeed = SpeedByEarthYearRatio(164.79132f), OrbitStartAngle = 260f, SelfRotateSpeed = SelfRotateSpeedByEarthDayRatio(0.67125f) }
        };

        for (int i = 0; i < solarSystemPlanets.Count; i++)
        {
            AddPlanet(solarSystemPlanets[i]);
        }

        Debug.Log($"[SpaceMgr] 太阳系创建完成，数量={RuntimePlanets.Count}");
    }


#endregion
}
