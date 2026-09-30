using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// 场景级太阳长投影：监听完整 Item 注册链，独立代理复用带透明外沿的 Sprite 网格，不进入实体 SortingGroup。
/// 共用一个材质；视口外每 0.25 秒复查，最多缓存 128 个代理，投影外观从世界渲染 JSON 读取。
/// 太阳参数在动画后的 LateUpdate 发布并同步主体；关闭设置会停用本组件并释放全部绑定。
/// 物品视觉定义可提供与椭圆底座阴影共用的局部落地点，Prefab 的 SunShadowCaster 仍可叠加高度和脚点修正。
/// </summary>
[DefaultExecutionOrder(-100)]
public sealed class WorldShadowProjectionManager : MonoBehaviour
{
    #region 配置与运行状态

    public const string MaterialResource = "SunShadows/SunShadowProjection";
    private const int MaxPooledRenderers = 128;
    private const float OffscreenInterval = 0.25f;
    private static readonly int CasterId = Shader.PropertyToID("_SunShadowCaster");
    private static readonly int MainTextureId = Shader.PropertyToID("_MainTex");
    private static readonly int BatchedId = Shader.PropertyToID("_SunShadowBatched");
    private static readonly int TexelSizeId = Shader.PropertyToID("_SunShadowTexelSize");
    private static readonly int UvBoundsId = Shader.PropertyToID("_SunShadowUvBounds");
    private static readonly int AlphaTextureId = Shader.PropertyToID("_AlphaTex");
    private static readonly int ExternalAlphaId = Shader.PropertyToID("_EnableExternalAlpha");

    private static WorldRenderingConfig.RenderingShadows Defaults => WorldRenderingConfigCatalog.Default.shadows;

    private readonly Dictionary<Item, Binding> bindings = new();
    private readonly Dictionary<MechanicalShadowRegistry.Part, MechanicalBinding> mechanicalBindings = new(); // 数据机械子层代理。
    private readonly List<MechanicalShadowRegistry.Part> staleMechanicalParts = new(); // 区块卸载后的清理缓存。
    private readonly Dictionary<int, Transform> roots = new();
    private readonly Dictionary<Sprite, Vector4> spriteUvBounds = new();
    private readonly List<Item> staleItems = new();
    private readonly Stack<ShadowProxy> pool = new();
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
        public ShadowProxy Proxy;
        public SunShadowCaster Authoring;
        public ItemShadowVisualDefinitionDto ShadowVisual;
        public Mod_Building Building;
        public IVisualGroundOffset GroundOffsetProvider;
        public float NextCheck;
        public bool InvalidGeometryReported;
    }

    /// <summary>机械子层只保存视觉代理，不创建 Item 或独立玩法对象。</summary>
    private sealed class MechanicalBinding
    {
        public ShadowProxy Proxy;
        public float NextCheck;
        public bool Seen;
        public int Revision = -1; // 未变化的静态 Sprite 不重复设置 MPB。
        public bool InvalidGeometryReported;
    }

    /// <summary>普通 Sprite 用共享四边形柔化，九宫格或平铺 Sprite 保留原绘制模式。</summary>
    private sealed class ShadowProxy
    {
        private static readonly int RendererColorId = Shader.PropertyToID("_RendererColor");
        internal readonly SpriteRenderer SpriteRenderer;
        internal readonly MeshRenderer MeshRenderer;
        private readonly MeshFilter meshFilter;
        private readonly GameObject root;
        internal GameObject gameObject => root;
        internal Transform transform => root.transform;
        internal bool enabled
        {
            get => SpriteRenderer.enabled || MeshRenderer.enabled;
            set
            {
                bool simple = SpriteRenderer.drawMode == SpriteDrawMode.Simple;
                SpriteRenderer.enabled = value && !simple;
                MeshRenderer.enabled = value && simple;
            }
        }
        internal Bounds bounds
        {
            set { SpriteRenderer.bounds = value; MeshRenderer.bounds = value; }
        }
        internal Material sharedMaterial
        {
            set { SpriteRenderer.sharedMaterial = value; MeshRenderer.sharedMaterial = value; }
        }

        internal ShadowProxy()
        {
            root = new GameObject("SunShadow");
            try
            {
                meshFilter = root.AddComponent<MeshFilter>();
                MeshRenderer = root.AddComponent<MeshRenderer>();
                // SpriteRenderer 与网格组件互斥，兼容绘制放到独立子节点。
                GameObject spriteObject = new GameObject("SpriteShadow");
                spriteObject.transform.SetParent(root.transform, false);
                SpriteRenderer = spriteObject.AddComponent<SpriteRenderer>();
                foreach (Renderer renderer in new Renderer[] { SpriteRenderer, MeshRenderer })
                {
                    renderer.enabled = false;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                }
            }
            catch
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(root);
                else UnityEngine.Object.DestroyImmediate(root);
                throw;
            }
        }

        /// <summary>两种绘制节点同步相机层，代理归池与换场景仍由根节点统一处理。</summary>
        internal void SetLayer(int layer)
        {
            root.layer = layer;
            SpriteRenderer.gameObject.layer = layer;
        }

        /// <summary>代理只读取主体帧和姿态，翻转作用于阴影网格而不改动本体。</summary>
        internal void SetSource(Sprite sprite, SpriteDrawMode drawMode, Vector2 size,
            bool flipX, bool flipY, float alpha)
        {
            SpriteRenderer.sprite = sprite;
            SpriteRenderer.drawMode = drawMode;
            SpriteRenderer.size = size;
            SpriteRenderer.flipX = flipX;
            SpriteRenderer.flipY = flipY;
            SpriteRenderer.color = new Color(1f, 1f, 1f, alpha);
            meshFilter.sharedMesh = drawMode == SpriteDrawMode.Simple
                ? SharedSpriteMeshCache.GetSunShadowGeometry(sprite).Mesh : null;
            if (drawMode == SpriteDrawMode.Simple)
                transform.localScale = Vector3.Scale(transform.localScale,
                    new Vector3(flipX ? -1f : 1f, flipY ? -1f : 1f, 1f));
        }

        internal void ResetSource() { SpriteRenderer.sprite = null; meshFilter.sharedMesh = null; }
        internal void ResetBounds() { SpriteRenderer.ResetBounds(); MeshRenderer.ResetBounds(); }
        internal void SetPropertyBlock(MaterialPropertyBlock block)
        {
            if (block != null)
            {
                bool simple = SpriteRenderer.drawMode == SpriteDrawMode.Simple;
                // 网格代理已在 Transform 翻转，不再读取 SpriteRenderer 的实例翻转与颜色。
                block.SetFloat(BatchedId, simple ? 2f : 0f);
                if (simple) block.SetColor(RendererColorId, SpriteRenderer.color);
            }
            SpriteRenderer.SetPropertyBlock(block);
            MeshRenderer.SetPropertyBlock(block);
        }
    }

    #endregion

    #region 生命周期与设置

    /// <summary>设置订阅保留到销毁，使停用的管理器仍可由开关重新启用。</summary>
    private void Awake()
    {
        properties = new MaterialPropertyBlock();
        SharedSpriteMeshCache.Clearing += ReleaseSharedGeometry;
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
                Defaults.minimumLength, Defaults.maximumLength, Defaults.maximumDistance)
            : default;
        SunShadowParametersProvider.Publish(CurrentParameters, Defaults.color);
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
            if (binding.Proxy != null && binding.Proxy.gameObject != null && binding.Proxy.enabled) VisibleCount++;
        }
        foreach (Item item in staleItems) UnregisterCaster(item);
        UpdateMechanicalCasters();
    }

    /// <summary>禁用时释放注册、GPU 状态与代理；不会改动脚底或局部灯光阴影。</summary>
    private void OnDisable()
    {
        ItemMgr.RuntimeItemRegistered -= RegisterCaster;
        ItemMgr.RuntimeItemUnregistered -= UnregisterCaster;
        Item.RuntimeStructureChanged -= RefreshCaster;
        // OnDisable 可能发生在场景/父节点停用过程中，此时不能把子代理重挂到其它父节点。
        ClearWorld(false);
    }

    /// <summary>解除常驻偏好与游戏事件，销毁归池资源。</summary>
    private void OnDestroy()
    {
        SunShadowSettings.Changed -= ApplyPreference;
        SharedSpriteMeshCache.Clearing -= ReleaseSharedGeometry;
        if (gameManager != null)
        {
            gameManager.Event_GameWorldEnter -= RegisterExistingItems;
            gameManager.Event_GameWorldExit -= ClearWorld;
        }
        while (pool.Count > 0)
        {
            ShadowProxy proxy = pool.Pop();
            if (proxy != null && proxy.gameObject != null) Destroy(proxy.gameObject);
        }
    }

    #endregion

    #region 注册与回收

    /// <summary>通用 MOD 注册入口；玩家、生物、落地建筑和不可拾取世界物体自动接入。</summary>
    public void RegisterCaster(Item item)
    {
        if (!isActiveAndEnabled || item == null || item is Map || item.itemData == null || bindings.ContainsKey(item)) return;
        SunShadowCaster authoring = item.GetComponent<SunShadowCaster>();
        ItemShadowVisualDefinitionDto shadowVisual = null;
        GameRes resources = GameRes.Instance;
        if (resources != null &&
            resources.TryGetItemDefinition(item.itemData.IDName, out RuntimeItemDefinition definition))
            shadowVisual = definition.Visual?.Shadows;
        Mod_Building building = item.GetComponentInChildren<Mod_Building>(true);
        bool actor = item is Player || RuntimeAiEntityUtility.IsAiEntity(item);
        if (authoring == null && !actor && building == null &&
            (item.itemData.Stack == null || item.itemData.Stack.CanBePickedUp)) return;
        if (building != null && building.IsSummoner) return;
        SpriteRenderer source = authoring != null && authoring.Source != null ? authoring.Source : item.Sprite;
        if (source == null) source = item.GetComponentInChildren<SpriteRenderer>(true);
        if (source == null) return;
        bindings.Add(item, new Binding
        {
            Owner = item,
            Source = source,
            Authoring = authoring,
            ShadowVisual = shadowVisual,
            Building = building,
            GroundOffsetProvider = VisualGroundOffsetResolver.FindProvider(item)
        });
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
    private void ClearWorld() => ClearWorld(true);

    /// <summary>清空当前世界；组件停用期间禁用归池重挂，避免 Unity 层级激活/停用时序冲突。</summary>
    private void ClearWorld(bool allowPooling)
    {
        foreach (Binding binding in bindings.Values) ReleaseProxy(binding, allowPooling);
        bindings.Clear();
        foreach (MechanicalBinding binding in mechanicalBindings.Values)
            ReleaseMechanicalProxy(binding, allowPooling);
        mechanicalBindings.Clear();
        staleMechanicalParts.Clear();
        foreach (Transform root in roots.Values) if (root != null) Destroy(root.gameObject);
        roots.Clear();
        spriteUvBounds.Clear();
        CurrentParameters = default;
        VisibleCount = 0;
        hidden = true;
        SunShadowParametersProvider.ClearGlobals();
    }

    /// <summary>代理停用后脱离世界根节点归池，不对正在回池的 Item 层级做重挂操作。</summary>
    private void ReleaseProxy(Binding binding) => ReleaseProxy(binding, true);

    /// <summary>释放单个代理；停用链中由世界根节点统一销毁，禁止 SetParent。</summary>
    private void ReleaseProxy(Binding binding, bool allowPooling)
    {
        ShadowProxy proxy = binding.Proxy;
        binding.Proxy = null;
        if (proxy == null || proxy.gameObject == null) return;
        proxy.enabled = false;
        proxy.gameObject.SetActive(false);
        proxy.ResetSource();
        proxy.SetPropertyBlock(null);
        proxy.ResetBounds();
        if (!allowPooling) return;
        if (pool.Count < MaxPooledRenderers)
        {
            proxy.transform.SetParent(transform, false);
            pool.Push(proxy);
        }
        else Destroy(proxy.gameObject);
    }

    /// <summary>按源世界创建独立根节点；共享 Resources 材质保证构建不会剥离 Shader。</summary>
    private ShadowProxy AcquireProxy(Binding binding)
        => AcquireProxy(binding.Owner.gameObject.scene, binding.Source.gameObject.layer);

    /// <summary>物品和机械复用同一共享材质、场景根节点与有上限的代理池。</summary>
    private ShadowProxy AcquireProxy(Scene scene, int layer)
    {
        if (material == null) material = Resources.Load<Material>(MaterialResource);
        if (material == null) throw new MissingReferenceException("缺少太阳长投影共享材质：" + MaterialResource);
        if (!roots.TryGetValue(scene.handle, out Transform root) || root == null)
        {
            GameObject rootObject = new GameObject("WorldSunShadows");
            SceneManager.MoveGameObjectToScene(rootObject, scene);
            root = rootObject.transform;
            roots[scene.handle] = root;
        }
        ShadowProxy proxy = null;
        while (pool.Count > 0 && proxy == null)
        {
            ShadowProxy candidate = pool.Pop();
            if (candidate != null && candidate.gameObject != null) proxy = candidate;
        }
        if (proxy == null) proxy = new ShadowProxy();
        proxy.transform.SetParent(root, false);
        proxy.SetLayer(layer);
        proxy.sharedMaterial = material;
        WorldSortingManager.GetInstance().ApplyRenderer(proxy.SpriteRenderer, WorldSortingManager.GroundShadowCategory);
        WorldSortingManager.GetInstance().ApplyRenderer(proxy.MeshRenderer, WorldSortingManager.GroundShadowCategory);
        proxy.gameObject.SetActive(true);
        return proxy;
    }

    #endregion

    #region 可见性与 Sprite 同步

    /// <summary>只遍历已显示区块的机械子层，夜间入口已在上层直接跳过。</summary>
    private void UpdateMechanicalCasters()
    {
        foreach (MechanicalBinding binding in mechanicalBindings.Values) binding.Seen = false;
        foreach (MechanicalShadowRegistry.Entry entry in MechanicalShadowRegistry.Entries)
        {
            if (!entry.Active || !entry.Scene.IsValid() || !entry.Scene.isLoaded) continue;
            foreach (MechanicalShadowRegistry.Part part in entry.Parts)
            {
                if (part == null || !part.Active || part.Sprite == null) continue;
                if (!mechanicalBindings.TryGetValue(part, out MechanicalBinding binding))
                {
                    binding = new MechanicalBinding();
                    mechanicalBindings.Add(part, binding);
                }
                binding.Seen = true;
                if (Time.unscaledTime >= binding.NextCheck)
                    SynchronizeMechanicalCaster(part, binding);
                if (binding.Proxy != null && binding.Proxy.gameObject != null && binding.Proxy.enabled) VisibleCount++;
            }
        }
        staleMechanicalParts.Clear();
        foreach (KeyValuePair<MechanicalShadowRegistry.Part, MechanicalBinding> pair in mechanicalBindings)
            if (!pair.Value.Seen) staleMechanicalParts.Add(pair.Key);
        foreach (MechanicalShadowRegistry.Part part in staleMechanicalParts)
        {
            ReleaseMechanicalProxy(mechanicalBindings[part], true);
            mechanicalBindings.Remove(part);
        }
    }

    /// <summary>同一 Sprite 投影 Shader 处理机械静态主体和旋转部件，视口外不租用代理。</summary>
    private void SynchronizeMechanicalCaster(MechanicalShadowRegistry.Part part, MechanicalBinding binding)
    {
        part.ResolveTransform(out Vector3 position, out Quaternion rotation);
        Matrix4x4 matrix = Matrix4x4.TRS(position, rotation, part.Scale);
        Bounds sourceBounds = SharedSpriteMeshCache.GetSunShadowWorldBounds(part.Sprite, matrix);
        float footY = part.Owner.Foot.y;
        if (!TryCalculateProjectedBounds(sourceBounds, footY, 1f, out float height, out Bounds projected))
        {
            ReportInvalidGeometry(ref binding.InvalidGeometryReported, part.Sprite, sourceBounds, footY, 1f);
            ReleaseMechanicalProxy(binding, true);
            binding.NextCheck = Time.unscaledTime + OffscreenInterval;
            return;
        }
        if (!IsInView(projected, part.Owner.Layer))
        {
            ReleaseMechanicalProxy(binding, true);
            binding.NextCheck = Time.unscaledTime + OffscreenInterval;
            return;
        }

        if (binding.Proxy == null || binding.Proxy.gameObject == null)
            binding.Proxy = AcquireProxy(part.Owner.Scene, part.Owner.Layer);
        ShadowProxy proxy = binding.Proxy;
        if (!part.Animated && binding.Revision == part.Revision)
        {
            proxy.bounds = projected;
            proxy.enabled = true;
            binding.NextCheck = 0f;
            return;
        }
        proxy.transform.SetPositionAndRotation(position, rotation);
        proxy.transform.localScale = part.Scale;
        proxy.SetSource(part.Sprite, SpriteDrawMode.Simple, Vector2.one, false, false, 1f);
        properties.Clear();
        Texture2D texture = part.Sprite.texture;
        properties.SetTexture(MainTextureId, texture);
        properties.SetVector(TexelSizeId, new Vector4(1f / texture.width, 1f / texture.height, 0f, 0f));
        properties.SetVector(UvBoundsId, GetSpriteUvBounds(part.Sprite));
        properties.SetVector(CasterId, new Vector4(footY, 1f, height, 1f));
        properties.SetFloat(BatchedId, 0f);
        Texture2D alphaTexture = part.Sprite.associatedAlphaSplitTexture;
        properties.SetFloat(ExternalAlphaId, alphaTexture != null ? 1f : 0f);
        if (alphaTexture != null) properties.SetTexture(AlphaTextureId, alphaTexture);
        proxy.SetPropertyBlock(properties);
        proxy.bounds = projected;
        proxy.enabled = true;
        binding.Revision = part.Revision;
        binding.NextCheck = 0f;
    }

    /// <summary>回收单个机械代理，卸载世界时遵守父节点停用时序。</summary>
    private void ReleaseMechanicalProxy(MechanicalBinding binding, bool allowPooling)
    {
        ShadowProxy proxy = binding.Proxy;
        binding.Proxy = null;
        binding.Revision = -1;
        if (proxy == null || proxy.gameObject == null) return;
        proxy.enabled = false;
        proxy.gameObject.SetActive(false);
        proxy.ResetSource();
        proxy.SetPropertyBlock(null);
        proxy.ResetBounds();
        if (!allowPooling) return;
        if (pool.Count < MaxPooledRenderers)
        {
            proxy.transform.SetParent(transform, false);
            pool.Push(proxy);
        }
        else Destroy(proxy.gameObject);
    }

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
            !source.enabled || (source.forceRenderingOff && !BuildingDepthMeshBridge.IsProjected(source)) || !source.gameObject.activeInHierarchy ||
            (authoring != null && !authoring.CastShadow) || heightScale <= 0f ||
            (binding.Building != null && (!binding.Building.IsInstalled() || binding.Building.IsGroundFacility || !binding.Building.CastSunShadow)))
        {
            ReleaseProxy(binding);
            binding.NextCheck = Time.unscaledTime + OffscreenInterval;
            return;
        }

        Vector3 visualOffset = VisualGroundOffsetResolver.Resolve(binding.GroundOffsetProvider, source.transform);
        Bounds footprint = ShadowFootprintResolver.MeasureVisibleWorldBounds(source);
        footprint.center -= visualOffset;
        float footY = ResolveFootY(owner, footprint, authoring, binding.ShadowVisual);
        Bounds sourceBounds = source.drawMode == SpriteDrawMode.Simple
            ? SharedSpriteMeshCache.GetSunShadowWorldBounds(source.sprite, source.transform.localToWorldMatrix,
                source.flipX, source.flipY) : source.bounds;
        sourceBounds.center -= visualOffset;
        if (!TryCalculateProjectedBounds(sourceBounds, footY, heightScale, out float height, out Bounds projected))
        {
            ReportInvalidGeometry(ref binding.InvalidGeometryReported, source, sourceBounds, footY, heightScale);
            ReleaseProxy(binding);
            binding.NextCheck = Time.unscaledTime + OffscreenInterval;
            return;
        }
        if (!IsInView(projected, source.gameObject.layer))
        {
            ReleaseProxy(binding);
            binding.NextCheck = Time.unscaledTime + OffscreenInterval;
            return;
        }

        if (binding.Proxy == null || binding.Proxy.gameObject == null) binding.Proxy = AcquireProxy(binding);
        ShadowProxy proxy = binding.Proxy;
        proxy.transform.SetPositionAndRotation(source.transform.position - visualOffset, source.transform.rotation);
        proxy.transform.localScale = source.transform.lossyScale;
        proxy.SetSource(source.sprite, source.drawMode, source.size, source.flipX, source.flipY, source.color.a);
        properties.Clear();
        Texture2D texture = source.sprite.texture;
        float texelScale = source.drawMode == SpriteDrawMode.Simple
            ? SharedSpriteMeshCache.GetSunShadowGeometry(source.sprite).TexelScale : 1f;
        properties.SetTexture(MainTextureId, texture);
        properties.SetVector(TexelSizeId, new Vector4(1f / texture.width,
            1f / texture.height, texelScale, 0f));
        properties.SetVector(UvBoundsId, GetSpriteUvBounds(source.sprite));
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
        if (!IsValidBounds(bounds)) return false;
        for (int i = 0; i < cameraCount; i++)
        {
            Camera camera = cameras[i];
            if (camera == null || !camera.isActiveAndEnabled || camera.cameraType != CameraType.Game ||
                (camera.cullingMask & (1 << layer)) == 0) continue;
            Vector2 delta = (Vector2)bounds.center - (Vector2)camera.transform.position;
            if (delta.sqrMagnitude > Defaults.maximumVisibleDistance * Defaults.maximumVisibleDistance) continue;
            if (GeometryUtility.TestPlanesAABB(cameraFrustums[i], bounds)) return true;
        }
        return false;
    }

    /// <summary>物品和机械共用投影计算，非法输入与溢出不得进入剔除、排序或 Renderer.bounds。</summary>
    private bool TryCalculateProjectedBounds(Bounds source, float footY, float heightScale,
        out float height, out Bounds projected)
    {
        height = 0f;
        projected = default;
        if (!CurrentParameters.IsValid || !IsValidBounds(source) || !IsFinite(footY) ||
            !IsFinite(heightScale) || heightScale <= 0f)
            return false;
        height = Mathf.Max(0.01f, source.max.y - footY);
        float length = height * heightScale * CurrentParameters.LengthMultiplier;
        if (!IsFinite(height) || !IsFinite(length)) return false;
        Vector2 displacement = CurrentParameters.ShadowDirection * (Mathf.Min(CurrentParameters.MaximumDistance, length) / height);
        Vector2 bottom = displacement * (source.min.y - footY);
        Vector2 top = displacement * (source.max.y - footY);
        projected.SetMinMax(new Vector3(source.min.x + Mathf.Min(bottom.x, top.x),
                footY + Mathf.Min(bottom.y, top.y), source.min.z - 0.1f),
            new Vector3(source.max.x + Mathf.Max(bottom.x, top.x),
                footY + Mathf.Max(bottom.y, top.y) + 0.02f, source.max.z + 0.1f));
        return IsValidBounds(projected);
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

    private static bool IsValidBounds(Bounds bounds)
    {
        Vector3 extents = bounds.extents;
        return IsFinite(bounds.center) && IsFinite(extents) &&
            extents.x >= 0f && extents.y >= 0f && extents.z >= 0f &&
            IsFinite(bounds.min) && IsFinite(bounds.max) &&
            IsFinite(bounds.center.sqrMagnitude) && IsFinite(extents.sqrMagnitude);
    }

    /// <summary>每个绑定只诊断一次异常源，后续低频重试不刷屏，也不改写实体位置或存档。</summary>
    private void ReportInvalidGeometry(ref bool reported, Object source, Bounds bounds, float footY, float heightScale)
    {
        if (reported) return;
        reported = true;
        Debug.LogWarning($"[SunShadow] 跳过无效投影：source={source.name}, bounds={bounds}, " +
            $"footY={footY}, heightScale={heightScale}, sun={CurrentParameters.ShaderVector}", source);
    }

    /// <summary>所有物品都从可见轮廓求脚点；显式偏移不能把阴影推离贴图底边。</summary>
    private static float ResolveFootY(Item owner, Bounds footprint,
        SunShadowCaster authoring, ItemShadowVisualDefinitionDto shadowVisual)
    {
        return ShadowFootprintResolver.ResolveFoot(owner, footprint,
            shadowVisual?.FootLocalPosition, authoring != null ? authoring.FootOffset : 0f,
            shadowVisual?.FootOverlap).y;
    }

    /// <summary>每张 Sprite 只读取一次图集 UV，模糊采样限定在本帧贴图区域内。</summary>
    private Vector4 GetSpriteUvBounds(Sprite sprite)
    {
        if (spriteUvBounds.TryGetValue(sprite, out Vector4 bounds)) return bounds;
        Vector2[] uv = sprite.uv;
        bounds = new Vector4(float.PositiveInfinity, float.PositiveInfinity,
            float.NegativeInfinity, float.NegativeInfinity);
        for (int i = 0; i < uv.Length; i++)
        {
            bounds.x = Mathf.Min(bounds.x, uv[i].x);
            bounds.y = Mathf.Min(bounds.y, uv[i].y);
            bounds.z = Mathf.Max(bounds.z, uv[i].x);
            bounds.w = Mathf.Max(bounds.w, uv[i].y);
        }
        spriteUvBounds.Add(sprite, bounds);
        return bounds;
    }

    /// <summary>资源重载前解绑自有代理，保留实体登记让下一帧按新资源重新取网格。</summary>
    private void ReleaseSharedGeometry()
    {
        foreach (Binding binding in bindings.Values) { ReleaseProxy(binding); binding.NextCheck = 0f; }
        foreach (MechanicalBinding binding in mechanicalBindings.Values)
        {
            ReleaseMechanicalProxy(binding, true);
            binding.NextCheck = 0f;
        }
        spriteUvBounds.Clear();
    }

    /// <summary>夜间和相机不可见时不保留上一帧可见状态。</summary>
    private void HideAll()
    {
        PruneInactiveMechanicalCasters();
        if (hidden) return;
        foreach (Binding binding in bindings.Values)
        {
            if (binding.Proxy != null && binding.Proxy.gameObject != null) binding.Proxy.enabled = false;
            binding.NextCheck = 0f;
        }
        foreach (MechanicalBinding binding in mechanicalBindings.Values)
        {
            if (binding.Proxy != null && binding.Proxy.gameObject != null) binding.Proxy.enabled = false;
            binding.NextCheck = 0f;
        }
        VisibleCount = 0;
        hidden = true;
    }

    /// <summary>夜间区块仍会卸载，及时回收它们的隐藏代理和 Sprite 引用。</summary>
    private void PruneInactiveMechanicalCasters()
    {
        if (mechanicalBindings.Count == 0) return;
        staleMechanicalParts.Clear();
        foreach (KeyValuePair<MechanicalShadowRegistry.Part, MechanicalBinding> pair in mechanicalBindings)
            if (!pair.Key.Active || !pair.Key.Owner.Active ||
                !pair.Key.Owner.Scene.IsValid() || !pair.Key.Owner.Scene.isLoaded)
                staleMechanicalParts.Add(pair.Key);
        foreach (MechanicalShadowRegistry.Part part in staleMechanicalParts)
        {
            ReleaseMechanicalProxy(mechanicalBindings[part], true);
            mechanicalBindings.Remove(part);
        }
    }

    #endregion
}
