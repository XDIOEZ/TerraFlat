using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// 场景级太阳长投影：监听完整 Item 注册链，独立代理只复制主体 Sprite，不进入实体 SortingGroup。
/// 共用一个材质；视口外每 0.25 秒复查，最多缓存 128 个代理，最大长度 8 个世界单位。
/// 太阳参数在动画后的 LateUpdate 发布并同步主体；关闭设置会停用本组件并释放全部绑定。
/// </summary>
[DefaultExecutionOrder(-100)]
public sealed class WorldShadowProjectionManager : MonoBehaviour
{
    #region 配置与运行状态

    public const string MaterialResource = "SunShadows/SunShadowProjection";
    private const int SortingOrder = 3; // Tilemap 的地面覆雪同序号，材质队列稍后；Blocking/4 在投影上方。
    private const int MaxPooledRenderers = 128;
    private const float OffscreenInterval = 0.25f;
    private static readonly int CasterId = Shader.PropertyToID("_SunShadowCaster");
    private static readonly int MainTextureId = Shader.PropertyToID("_MainTex");
    private static readonly int BatchedId = Shader.PropertyToID("_SunShadowBatched");
    private static readonly int AlphaTextureId = Shader.PropertyToID("_AlphaTex");
    private static readonly int ExternalAlphaId = Shader.PropertyToID("_EnableExternalAlpha");

    [Min(0.05f), SerializeField] private float minimumLength = 0.35f; // 正午长度倍率。
    [Min(0.1f), SerializeField] private float maximumLength = 2.8f; // 早晚长度倍率。
    [Min(0.1f), SerializeField] private float maximumDistance = 8f; // 世界长度硬上限。
    [Range(0f, 1f), SerializeField] private float opacity = 0.34f; // 正午最大不透明度。
    [SerializeField] private Color shadowColor = new Color(0.12f, 0.14f, 0.20f, 1f);
    [Min(1f), SerializeField] private float maximumVisibleDistance = 80f; // 源与相机的最大距离。

    private readonly Dictionary<Item, Binding> bindings = new();
    private readonly Dictionary<int, Transform> roots = new();
    private readonly Dictionary<Sprite, Bounds> spriteBounds = new();
    private readonly List<Vector2> shapePoints = new();
    private readonly List<Item> staleItems = new();
    private readonly Stack<SpriteRenderer> pool = new();
    private MaterialPropertyBlock properties; // 原生资源必须在主线程 Awake 创建。
    private readonly List<Plane[]> cameraFrustums = new();
    private Camera[] cameras = new Camera[8];
    private int cameraCount;
    private GameManager gameManager;
    private Material material;
    private bool hidden = true;
    public SunShadowParameters CurrentParameters { get; private set; }
    public int RegisteredCount => bindings.Count;
    public int VisibleCount { get; private set; }

    /// <summary>只缓存冷路径组件与视口外检查时钟，代理按需租用。</summary>
    private sealed class Binding
    {
        public Item Owner;
        public SpriteRenderer Source;
        public SpriteRenderer Proxy;
        public SunShadowCaster Authoring;
        public Mod_Building Building;
        public float NextCheck;
    }

    #endregion

    #region 生命周期与设置

    /// <summary>设置订阅保留到销毁，使停用的管理器仍可由开关重新启用。</summary>
    private void Awake()
    {
        properties = new MaterialPropertyBlock();
        gameManager = GetComponentInChildren<GameManager>(true);
        if (gameManager == null) gameManager = GameManager.Instance;
        if (gameManager != null)
        {
            gameManager.Event_GameWorldEnter += RegisterExistingItems;
            gameManager.Event_GameWorldExit += ClearWorld;
        }
    }

    /// <summary>只在功能开启时监听实体；重启从权威注册表补齐，不扫描场景。</summary>
    private void OnEnable()
    {
        // 停用期间仍保留偏好监听；重复进入播放模式时重新接入已复位的静态事件。
        SunShadowSettings.Changed -= ApplyPreference;
        SunShadowSettings.Changed += ApplyPreference;
        if (!SunShadowSettings.Enabled) { enabled = false; return; }
        ItemMgr.RuntimeItemRegistered += RegisterCaster;
        ItemMgr.RuntimeItemUnregistered += UnregisterCaster;
        Item.RuntimeStructureChanged += RefreshCaster;
        RegisterExistingItems();
    }

    /// <summary>在各管理器 Awake 完成后补齐启动前已注册的对象。</summary>
    private void Start() => RegisterExistingItems();

    /// <summary>开关关闭时停止 Unity 更新回调，代理解绑由 OnDisable 完成。</summary>
    private void ApplyPreference() => enabled = SunShadowSettings.Enabled;

    /// <summary>所有 Update 完成后采样时间，早于默认顺序的 ECS LateUpdate 发布。</summary>
    private void UpdateSunParameters()
    {
        CurrentParameters = gameManager != null && gameManager.IsInGameWorld
            ? SunShadowParametersProvider.Evaluate(SceneManager.GetActiveScene().name,
                minimumLength, maximumLength, maximumDistance, opacity)
            : default;
        SunShadowParametersProvider.Publish(CurrentParameters, shadowColor);
    }

    /// <summary>动画和移动结束后更新可见代理；夜晚仅在切入隐藏状态时处理一次。</summary>
    private void LateUpdate()
    {
        UpdateSunParameters();
        if (!CurrentParameters.IsVisible) { HideAll(); return; }
        hidden = false;
        if (Camera.allCamerasCount > cameras.Length) cameras = new Camera[Camera.allCamerasCount];
        cameraCount = Camera.GetAllCameras(cameras);
        while (cameraFrustums.Count < cameraCount) cameraFrustums.Add(new Plane[6]);
        for (int i = 0; i < cameraCount; i++)
            GeometryUtility.CalculateFrustumPlanes(cameras[i], cameraFrustums[i]);
        VisibleCount = 0;
        staleItems.Clear();
        foreach (KeyValuePair<Item, Binding> entry in bindings)
        {
            Binding binding = entry.Value;
            if (binding.Owner == null) { staleItems.Add(entry.Key); continue; }
            if (Time.unscaledTime < binding.NextCheck) continue;
            SynchronizeCaster(binding);
            if (binding.Proxy != null && binding.Proxy.enabled) VisibleCount++;
        }
        foreach (Item item in staleItems) UnregisterCaster(item);
    }

    /// <summary>禁用时释放注册、GPU 状态与代理；不会改动脚底或局部灯光阴影。</summary>
    private void OnDisable()
    {
        ItemMgr.RuntimeItemRegistered -= RegisterCaster;
        ItemMgr.RuntimeItemUnregistered -= UnregisterCaster;
        Item.RuntimeStructureChanged -= RefreshCaster;
        ClearWorld();
    }

    /// <summary>解除常驻偏好与游戏事件，销毁归池资源。</summary>
    private void OnDestroy()
    {
        SunShadowSettings.Changed -= ApplyPreference;
        if (gameManager != null)
        {
            gameManager.Event_GameWorldEnter -= RegisterExistingItems;
            gameManager.Event_GameWorldExit -= ClearWorld;
        }
        while (pool.Count > 0)
        {
            SpriteRenderer proxy = pool.Pop();
            if (proxy != null) Destroy(proxy.gameObject);
        }
    }

    #endregion

    #region 注册与回收

    /// <summary>通用 MOD 注册入口；玩家、生物、落地建筑和不可拾取世界物体自动接入。</summary>
    public void RegisterCaster(Item item)
    {
        if (!isActiveAndEnabled || item == null || item is Map || item.itemData == null || bindings.ContainsKey(item)) return;
        SunShadowCaster authoring = item.GetComponent<SunShadowCaster>();
        Mod_Building building = item.GetComponentInChildren<Mod_Building>(true);
        bool actor = item is Player || RuntimeAiEntityUtility.IsAiEntity(item);
        if (authoring == null && !actor && building == null &&
            (item.itemData.Stack == null || item.itemData.Stack.CanBePickedUp)) return;
        if (building != null && building.IsSummoner) return;
        SpriteRenderer source = authoring != null && authoring.Source != null ? authoring.Source : item.Sprite;
        if (source == null) source = item.GetComponentInChildren<SpriteRenderer>(true);
        if (source == null) return;
        bindings.Add(item, new Binding { Owner = item, Source = source, Authoring = authoring, Building = building });
    }

    /// <summary>回池、死亡和远程纯表现注销共用同一解绑入口。</summary>
    public void UnregisterCaster(Item item)
    {
        if (ReferenceEquals(item, null) || !bindings.TryGetValue(item, out Binding binding)) return;
        ReleaseProxy(binding);
        bindings.Remove(item);
    }

    /// <summary>模块/视觉结构重建后重新解析，只处理已经进入权威运行表的实例。</summary>
    private void RefreshCaster(Item item)
    {
        ItemMgr items = ItemMgr.GetInstance();
        if (item == null || item.itemData == null || items == null ||
            !items.WorldRunTimeItems.TryGetValue(item.itemData.Guid, out Item registered) || registered != item) return;
        UnregisterCaster(item);
        RegisterCaster(item);
    }

    /// <summary>启用及进入世界时进行一次权威注册表补登记。</summary>
    private void RegisterExistingItems()
    {
        if (!isActiveAndEnabled) return;
        ItemMgr items = ItemMgr.GetInstance();
        if (items == null) return;
        foreach (Item item in items.WorldRunTimeItems.Values) RegisterCaster(item);
    }

    /// <summary>退出世界立即清空绑定；只保留有上限的停用代理池。</summary>
    private void ClearWorld()
    {
        foreach (Binding binding in bindings.Values) ReleaseProxy(binding);
        bindings.Clear();
        foreach (Transform root in roots.Values) if (root != null) Destroy(root.gameObject);
        roots.Clear();
        spriteBounds.Clear();
        CurrentParameters = default;
        VisibleCount = 0;
        hidden = true;
        SunShadowParametersProvider.ClearGlobals();
    }

    /// <summary>代理停用后脱离世界根节点归池，不对正在回池的 Item 层级做重挂操作。</summary>
    private void ReleaseProxy(Binding binding)
    {
        SpriteRenderer proxy = binding.Proxy;
        binding.Proxy = null;
        if (proxy == null) return;
        proxy.enabled = false;
        proxy.gameObject.SetActive(false);
        proxy.sprite = null;
        proxy.SetPropertyBlock(null);
        proxy.ResetBounds();
        if (pool.Count < MaxPooledRenderers)
        {
            proxy.transform.SetParent(transform, false);
            pool.Push(proxy);
        }
        else Destroy(proxy.gameObject);
    }

    /// <summary>按源世界创建独立根节点；共享 Resources 材质保证构建不会剥离 Shader。</summary>
    private SpriteRenderer AcquireProxy(Binding binding)
    {
        if (material == null) material = Resources.Load<Material>(MaterialResource);
        if (material == null) throw new MissingReferenceException("缺少太阳长投影共享材质：" + MaterialResource);
        Scene scene = binding.Owner.gameObject.scene;
        if (!roots.TryGetValue(scene.handle, out Transform root) || root == null)
        {
            GameObject rootObject = new GameObject("WorldSunShadows");
            SceneManager.MoveGameObjectToScene(rootObject, scene);
            root = rootObject.transform;
            roots[scene.handle] = root;
        }
        SpriteRenderer proxy = null;
        while (pool.Count > 0 && proxy == null) proxy = pool.Pop();
        if (proxy == null) proxy = new GameObject("SunShadow").AddComponent<SpriteRenderer>();
        proxy.transform.SetParent(root, false);
        proxy.gameObject.layer = binding.Source.gameObject.layer;
        proxy.sharedMaterial = material;
        proxy.sortingLayerName = "Tilemap";
        proxy.sortingOrder = SortingOrder;
        proxy.shadowCastingMode = ShadowCastingMode.Off;
        proxy.receiveShadows = false;
        proxy.gameObject.SetActive(true);
        return proxy;
    }

    #endregion

    #region 可见性与 Sprite 同步

    /// <summary>同步当前主体帧、翻转、尺寸及逐 Renderer 贴图，不创建材质实例。</summary>
    private void SynchronizeCaster(Binding binding)
    {
        Item owner = binding.Owner;
        SunShadowCaster authoring = binding.Authoring;
        if (authoring != null && authoring.Source != null) binding.Source = authoring.Source;
        else if (owner.Sprite != null) binding.Source = owner.Sprite;
        SpriteRenderer source = binding.Source;
        float heightScale = authoring != null ? Mathf.Max(0f, authoring.HeightMultiplier) : 1f;
        if (!owner.gameObject.activeInHierarchy || owner.InHand || source == null || source.sprite == null ||
            !source.enabled || source.forceRenderingOff || !source.gameObject.activeInHierarchy ||
            (authoring != null && !authoring.CastShadow) || heightScale <= 0f ||
            (binding.Building != null && !binding.Building.IsInstalled()))
        {
            ReleaseProxy(binding);
            binding.NextCheck = Time.unscaledTime + OffscreenInterval;
            return;
        }

        Bounds footprint = MeasureVisibleBounds(source);
        float footY = footprint.min.y + (authoring != null ? authoring.FootOffset : 0f);
        float height = Mathf.Max(0.01f, source.bounds.max.y - footY);
        Vector2 displacement = CurrentParameters.ShadowDirection * Mathf.Min(CurrentParameters.MaximumDistance,
            height * heightScale * CurrentParameters.LengthMultiplier);
        Bounds projected = new Bounds();
        projected.SetMinMax(new Vector3(source.bounds.min.x + Mathf.Min(0f, displacement.x),
                footY + Mathf.Min(0f, displacement.y), source.bounds.min.z - 0.1f),
            new Vector3(source.bounds.max.x + Mathf.Max(0f, displacement.x),
                footY + Mathf.Max(0f, displacement.y) + 0.02f, source.bounds.max.z + 0.1f));
        if (!IsInView(projected, source.gameObject.layer))
        {
            ReleaseProxy(binding);
            binding.NextCheck = Time.unscaledTime + OffscreenInterval;
            return;
        }

        if (binding.Proxy == null) binding.Proxy = AcquireProxy(binding);
        SpriteRenderer proxy = binding.Proxy;
        proxy.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
        proxy.transform.localScale = source.transform.lossyScale;
        proxy.sprite = source.sprite;
        proxy.flipX = source.flipX;
        proxy.flipY = source.flipY;
        proxy.drawMode = source.drawMode;
        proxy.size = source.size;
        proxy.color = new Color(1f, 1f, 1f, source.color.a);
        properties.Clear();
        properties.SetTexture(MainTextureId, source.sprite.texture);
        properties.SetVector(CasterId, new Vector4(footY, heightScale, height, 1f));
        properties.SetFloat(BatchedId, 0f);
        Texture2D alphaTexture = source.sprite.associatedAlphaSplitTexture;
        properties.SetFloat(ExternalAlphaId, alphaTexture != null ? 1f : 0f);
        if (alphaTexture != null) properties.SetTexture(AlphaTextureId, alphaTexture);
        proxy.SetPropertyBlock(properties);
        // Shader 顶点投影超出原始 Sprite，显式扩大世界包围盒以免相机提前裁掉长阴影。
        proxy.bounds = projected;
        proxy.enabled = true;
        binding.NextCheck = 0f;
    }

    /// <summary>所有游戏相机共用代理，包含环绕世界镜像相机；不依赖主体 isVisible。</summary>
    private bool IsInView(Bounds bounds, int layer)
    {
        for (int i = 0; i < cameraCount; i++)
        {
            Camera camera = cameras[i];
            if (camera == null || !camera.isActiveAndEnabled || camera.cameraType != CameraType.Game ||
                (camera.cullingMask & (1 << layer)) == 0) continue;
            Vector2 delta = (Vector2)bounds.center - (Vector2)camera.transform.position;
            if (delta.sqrMagnitude > maximumVisibleDistance * maximumVisibleDistance) continue;
            if (GeometryUtility.TestPlanesAABB(cameraFrustums[i], bounds)) return true;
        }
        return false;
    }

    /// <summary>使用导入的轮廓几何排除透明留白；每张 Sprite 只在首次遇到时读取。</summary>
    private Bounds MeasureVisibleBounds(SpriteRenderer source)
    {
        Sprite sprite = source.sprite;
        if (source.drawMode != SpriteDrawMode.Simple) return source.bounds;
        if (!spriteBounds.TryGetValue(sprite, out Bounds local))
        {
            local = sprite.bounds;
            bool first = true;
            for (int shape = 0; shape < sprite.GetPhysicsShapeCount(); shape++)
            {
                sprite.GetPhysicsShape(shape, shapePoints);
                foreach (Vector2 point in shapePoints)
                {
                    if (first) { local = new Bounds(point, Vector3.zero); first = false; }
                    else local.Encapsulate(point);
                }
            }
            spriteBounds[sprite] = local;
        }
        Bounds world = new Bounds();
        for (int i = 0; i < 4; i++)
        {
            Vector3 point = new Vector3((i & 1) == 0 ? local.min.x : local.max.x,
                i < 2 ? local.min.y : local.max.y, 0f);
            if (source.flipX) point.x = -point.x;
            if (source.flipY) point.y = -point.y;
            point = source.transform.TransformPoint(point);
            if (i == 0) world = new Bounds(point, Vector3.zero); else world.Encapsulate(point);
        }
        return world;
    }

    /// <summary>夜间和相机不可见时不保留上一帧可见状态。</summary>
    private void HideAll()
    {
        if (hidden) return;
        foreach (Binding binding in bindings.Values)
        {
            if (binding.Proxy != null) binding.Proxy.enabled = false;
            binding.NextCheck = 0f;
        }
        VisibleCount = 0;
        hidden = true;
    }

    #endregion
}
