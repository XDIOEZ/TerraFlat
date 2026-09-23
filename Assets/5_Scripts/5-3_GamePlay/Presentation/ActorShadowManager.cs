using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 统一管理玩家、生物与落地植物根部的接触阴影。
/// 阴影实例全部放在场景级 ActorShadows 根节点下，以 Default/0、Queue 2991
/// 在 BRG 地形之后、草与实体之前绘制；漏注册的实体每 0.5 秒补扫一次。
/// 不依附于 Item 或 RuntimeEntities 层级；阴影透明度随 DayTimeSystem 的有效光照强度变化，
/// 进入液体后由独立液体接触系统主动关闭；树木与作物复用同一预制体，
/// 根部阴影宽度取碰撞宽度与可见贴图宽度的 0.6 倍中较小者，再外扩 1.5 倍。
/// </summary>
public sealed class ActorShadowManager : SingletonMono<ActorShadowManager>
{
    #region 阴影配置与运行状态

    private const byte VisibleAlphaThreshold = 8;
    private const string ShadowRootName = "ActorShadows";
    private const string ShadowSortingLayer = "Default"; // 与正式 BRG 地形使用同一排序域。
    private const int ShadowSortingOrder = 0;
    private const int ShadowRenderQueue = 2991; // 太阳长投影之后，Blocking 与草之前。
    private const float RegistrationScanInterval = 0.5f;
    private const float RootedPlantBodyWidthRatio = 1.5f; // 根部碰撞宽度略外扩，盖住树干与地面的接缝。
    private const float RootedPlantVisualWidthLimit = 0.6f; // 小植株不能沿用宽于本体的通用碰撞体。

    private static readonly Dictionary<Sprite, SpritePixelBounds> visibleSpriteBoundsCache =
        new Dictionary<Sprite, SpritePixelBounds>();

    [Header("阴影资源")]
    [Tooltip("实体阴影预制体；预制体只应包含 SpriteRenderer，不应包含 Item 或玩法模块。")]
    [SerializeField] private GameObject actorShadowPrefab;

    [Header("阴影外观")]
    [Tooltip("阴影在正午光照下的最大透明度。")]
    [Range(0f, 1f)] [SerializeField] private float maxShadowAlpha = 0.55f;
    [Tooltip("低于该光照强度时不显示阴影。")]
    [Range(0f, 1f)] [SerializeField] private float minSunlightToShow = 0.02f;
    [Tooltip("光照对透明度的响应曲线指数；数值越大，阴影在低光照时消失越快。")]
    [Min(0.01f)] [SerializeField] private float sunlightResponsePower = 1f;
    [Tooltip("阴影宽度相对于角色可见贴图占地尺寸的倍率。")]
    [Min(0.05f)] [SerializeField] private float shadowWidthRatio = 0.9f;
    [Tooltip("角色可见贴图高度折算为阴影宽度的倍率；避免高而窄的玩家阴影过小。")]
    [Range(0.1f, 2f)] [SerializeField] private float shadowHeightToWidthRatio = 0.9f;
    [Tooltip("阴影的最小世界宽度。")]
    [Min(0.01f)] [SerializeField] private float minShadowWidth = 0.18f;
    [Tooltip("阴影的最大世界宽度，避免异常大贴图撑满场景。")]
    [Min(0.05f)] [SerializeField] private float maxShadowWidth = 2.5f;
    [Tooltip("阴影相对生物 Sprite 底边的微小抬升，用于避免与地面产生深度冲突。")]
    [SerializeField] private float shadowGroundOffset = 0.01f;

    private readonly Dictionary<Item, ShadowBinding> bindings = new Dictionary<Item, ShadowBinding>();
    private readonly List<KeyValuePair<Item, ShadowBinding>> cleanupBindings =
        new List<KeyValuePair<Item, ShadowBinding>>();
    private Transform shadowRoot;
    private Material shadowMaterial; // 共享预制体材质的排序队列副本。
    private bool prefabWarningShown;
    private int lightingFrame = -1;
    private string lightingSceneName;
    private float cachedSunlight = 1f;
    private float nextRegistrationScanTime;

    #endregion

    #region Unity 生命周期

    /// <summary>初始化单例并准备独立的阴影根节点。</summary>
    protected override void Awake()
    {
        base.Awake();

        // 场景回到主菜单再进入时，重复的 WorldManager 会在基类中被销毁，不能继续初始化它的阴影根节点。
        if (GetInstance() != this)
            return;

        EnsureShadowRoot();
    }

    /// <summary>订阅 Item 生命周期事件；场景中已有实体和植物由 Start 补注册。</summary>
    private void OnEnable()
    {
        ItemMgr.RuntimeItemInstantiated -= RegisterActor;
        ItemMgr.RuntimeItemInstantiated += RegisterActor;
        ItemMgr.RuntimeItemDespawning -= UnregisterActor;
        ItemMgr.RuntimeItemDespawning += UnregisterActor;

        if (shadowRoot != null)
            shadowRoot.gameObject.SetActive(true);

        nextRegistrationScanTime = 0f;
    }

    /// <summary>补注册管理器启动前已经存在的玩家、生物与植物。</summary>
    private void Start()
    {
        RegisterExistingActors();
    }

    /// <summary>每帧末尾同步阴影位置、尺寸和光照透明度。</summary>
    private void LateUpdate()
    {
        if (Time.unscaledTime >= nextRegistrationScanTime)
        {
            nextRegistrationScanTime = Time.unscaledTime + RegistrationScanInterval;
            RegisterRuntimeActors();
        }

        if (bindings.Count == 0)
            return;

        cleanupBindings.Clear();
        foreach (KeyValuePair<Item, ShadowBinding> pair in bindings)
        {
            Item item = pair.Key;
            ShadowBinding binding = pair.Value;
            if (item == null || binding == null || binding.Renderer == null ||
                !item.gameObject.activeInHierarchy || IsItemInPool(item))
            {
                cleanupBindings.Add(pair);
                continue;
            }

            UpdateBinding(item, binding);
        }

        for (int i = 0; i < cleanupBindings.Count; i++)
        {
            KeyValuePair<Item, ShadowBinding> pair = cleanupBindings[i];
            DestroyBinding(pair.Key, pair.Value);
        }
    }

    /// <summary>解除事件并清理独立根节点，避免场景切换留下孤立阴影。</summary>
    protected override void OnDestroy()
    {
        ItemMgr.RuntimeItemInstantiated -= RegisterActor;
        ItemMgr.RuntimeItemDespawning -= UnregisterActor;

        ClearBindings();
        if (shadowMaterial != null)
            Destroy(shadowMaterial);
        if (shadowRoot != null)
            Destroy(shadowRoot.gameObject);
        shadowRoot = null;

        base.OnDestroy();
    }

    /// <summary>组件停用时暂停独立根节点的渲染；重新启用后会自动恢复。</summary>
    private void OnDisable()
    {
        ItemMgr.RuntimeItemInstantiated -= RegisterActor;
        ItemMgr.RuntimeItemDespawning -= UnregisterActor;

        if (shadowRoot != null)
            shadowRoot.gameObject.SetActive(false);
    }

    #endregion

    #region 对外生命周期接口

    /// <summary>为符合条件的角色或落地植物创建独立阴影。</summary>
    public void RegisterActor(Item item)
    {
        if (this == null)
            return;

        if (item == null || bindings.ContainsKey(item) ||
            !TryClassifyShadowCaster(item, out bool isRootedPlant))
            return;

        if (actorShadowPrefab == null)
        {
            if (!prefabWarningShown)
            {
                Debug.LogWarning("[实体阴影] 未配置 ActorShadow 预制体，已跳过阴影创建。", this);
                prefabWarningShown = true;
            }
            return;
        }

        Transform root = EnsureShadowRoot();
        if (root == null)
            return;

        GameObject shadowObject = Instantiate(actorShadowPrefab, root, false);
        shadowObject.name = "ActorShadow";

        SpriteRenderer shadowRenderer = shadowObject.GetComponentInChildren<SpriteRenderer>(true);
        if (shadowRenderer == null)
        {
            Debug.LogWarning("[实体阴影] ActorShadow 预制体缺少 SpriteRenderer，已跳过该实体。", actorShadowPrefab);
            Destroy(shadowObject);
            return;
        }

        shadowRenderer.sortingLayerName = ShadowSortingLayer;
        shadowRenderer.sortingOrder = ShadowSortingOrder;
        if (shadowMaterial == null)
        {
            shadowMaterial = new Material(shadowRenderer.sharedMaterial)
            {
                name = "ActorContactShadow",
                hideFlags = HideFlags.DontSave,
                renderQueue = ShadowRenderQueue
            };
        }
        shadowRenderer.sharedMaterial = shadowMaterial;
        shadowRenderer.enabled = false;
        bindings.Add(item, new ShadowBinding(shadowObject, shadowRenderer,
            isRootedPlant ? null : VisualGroundOffsetResolver.FindProvider(item), isRootedPlant));
    }

    /// <summary>移除实体对应的阴影实例。</summary>
    public void UnregisterActor(Item item)
    {
        if (item == null || !bindings.TryGetValue(item, out ShadowBinding binding))
            return;

        DestroyBinding(item, binding);
    }

    /// <summary>由水体地块切换实体阴影的显示状态。</summary>
    public void SetActorInWater(Item item, bool inWater)
    {
        if (item == null || !bindings.TryGetValue(item, out ShadowBinding binding))
            return;

        binding.InWater = inWater;
        if (inWater && binding.Renderer != null)
            binding.Renderer.enabled = false;
    }

    #endregion

    #region 阴影同步

    /// <summary>同步单个实体的脚底位置、贴图占地尺寸和当前光照透明度。</summary>
    private void UpdateBinding(Item item, ShadowBinding binding)
    {
        SpriteRenderer sourceRenderer = binding.SourceRenderer;
        if (sourceRenderer == null || sourceRenderer.sprite == null)
        {
            sourceRenderer = FindSourceRenderer(item);
            binding.SourceRenderer = sourceRenderer;
        }

        if (sourceRenderer == null || sourceRenderer.sprite == null ||
            !sourceRenderer.enabled || !sourceRenderer.gameObject.activeInHierarchy)
        {
            binding.Renderer.enabled = false;
            return;
        }

        if (binding.IsRootedPlant && !IsGroundedStaticPlant(item))
        {
            binding.Renderer.enabled = false;
            return;
        }

        float alpha = GetShadowOpacity(item.gameObject.scene);

        if (binding.InWater || alpha <= 0.001f)
        {
            binding.Renderer.enabled = false;
            return;
        }

        Bounds sourceBounds = sourceRenderer.bounds;
        ResolveSourceFootprint(sourceRenderer, sourceBounds, out Vector3 footprintAnchor,
            out float footprintWidth, out float footprintHeight);
        if (binding.IsRootedPlant)
        {
            ResolveRootedPlantFootprint(item, footprintAnchor, footprintWidth,
                out Vector3 rootAnchor, out float rootedPlantWidth);
            footprintAnchor = rootAnchor;
            binding.ShadowWidth = Mathf.Clamp(rootedPlantWidth,
                minShadowWidth, Mathf.Max(minShadowWidth, maxShadowWidth));
        }
        else
        {
            footprintAnchor -= VisualGroundOffsetResolver.Resolve(binding.GroundOffsetProvider,
                sourceRenderer.transform);
        }

        Transform shadowTransform = binding.Renderer.transform;
        shadowTransform.position = new Vector3(
            footprintAnchor.x,
            footprintAnchor.y + shadowGroundOffset,
            item.transform.position.z);

        // 角色锁定首次宽度避免动画抖动；植物则跟随成长尺寸更新。
        if (!binding.IsRootedPlant && binding.ShadowWidth <= 0f)
        {
            float visualFootprintWidth = Mathf.Max(
                footprintWidth,
                footprintHeight * Mathf.Max(0.1f, shadowHeightToWidthRatio));
            binding.ShadowWidth = Mathf.Clamp(
                visualFootprintWidth * shadowWidthRatio,
                minShadowWidth,
                Mathf.Max(minShadowWidth, maxShadowWidth));
        }

        float desiredWidth = binding.ShadowWidth;
        float sourceShadowWidth = binding.Renderer.sprite != null
            ? binding.Renderer.sprite.bounds.size.x
            : 0f;
        if (sourceShadowWidth <= 0.0001f)
        {
            binding.Renderer.enabled = false;
            return;
        }

        float scale = desiredWidth / sourceShadowWidth;
        shadowTransform.localScale = new Vector3(scale, scale, 1f);

        Color shadowColor = binding.BaseColor;
        shadowColor.a = binding.BaseColor.a * alpha;
        binding.Renderer.color = shadowColor;
        binding.Renderer.enabled = true;
    }

    /// <summary>植物以根部碰撞体定位，宽度同时受实际植株贴图限制。</summary>
    private static void ResolveRootedPlantFootprint(Item item, Vector3 visualFoot,
        float visualWidth, out Vector3 rootAnchor, out float shadowWidth)
    {
        Collider2D rootCollider = item.GetComponent<Collider2D>();
        if (rootCollider != null && rootCollider.enabled)
        {
            Bounds bodyBounds = rootCollider.bounds;
            rootAnchor = bodyBounds.center;
            shadowWidth = Mathf.Min(bodyBounds.size.x,
                visualWidth * RootedPlantVisualWidthLimit) * RootedPlantBodyWidthRatio;
            return;
        }

        rootAnchor = visualFoot;
        shadowWidth = visualWidth * RootedPlantVisualWidthLimit;
    }

    /// <summary>按 Sprite 的可见像素范围解析角色脚底锚点和视觉占地尺寸。</summary>
    private static void ResolveSourceFootprint(SpriteRenderer sourceRenderer, Bounds sourceBounds,
        out Vector3 footprintAnchor, out float footprintWidth, out float footprintHeight)
    {
        Sprite sprite = sourceRenderer.sprite;
        if (!TryGetVisibleSpriteBounds(sprite, out SpritePixelBounds visibleBounds))
        {
            footprintAnchor = new Vector3(sourceBounds.center.x, sourceBounds.min.y, sourceBounds.center.z);
            footprintWidth = sourceBounds.size.x;
            footprintHeight = sourceBounds.size.y;
            return;
        }

        float pixelsPerUnit = Mathf.Max(0.0001f, sprite.pixelsPerUnit);
        Rect spriteRect = sprite.rect;
        float visibleCenterX = ((visibleBounds.MinX + visibleBounds.MaxX + 1) * 0.5f - sprite.pivot.x) /
                               pixelsPerUnit;
        float visibleBottomY = (visibleBounds.MinY - sprite.pivot.y) / pixelsPerUnit;
        if (sourceRenderer.flipX)
            visibleCenterX = -visibleCenterX;
        if (sourceRenderer.flipY)
            visibleBottomY = -((visibleBounds.MaxY + 1) - sprite.pivot.y) / pixelsPerUnit;

        footprintAnchor = sourceRenderer.transform.TransformPoint(new Vector3(visibleCenterX, visibleBottomY, 0f));
        Vector3 sourceScale = sourceRenderer.transform.lossyScale;
        footprintWidth = visibleBounds.Width / pixelsPerUnit * Mathf.Abs(sourceScale.x);
        footprintHeight = visibleBounds.Height / pixelsPerUnit * Mathf.Abs(sourceScale.y);

        // 贴图数据异常时仍保持和 Renderer bounds 一致，避免阴影被计算成零尺寸。
        if (footprintWidth <= 0.0001f || footprintHeight <= 0.0001f ||
            spriteRect.width <= 0f || spriteRect.height <= 0f)
        {
            footprintAnchor = new Vector3(sourceBounds.center.x, sourceBounds.min.y, sourceBounds.center.z);
            footprintWidth = sourceBounds.size.x;
            footprintHeight = sourceBounds.size.y;
        }
    }

    /// <summary>读取并缓存当前 Sprite 的透明像素边界；不可读贴图回退到整帧边界。</summary>
    private static bool TryGetVisibleSpriteBounds(Sprite sprite, out SpritePixelBounds bounds)
    {
        bounds = default(SpritePixelBounds);
        if (sprite == null)
            return false;

        if (visibleSpriteBoundsCache.TryGetValue(sprite, out bounds))
            return bounds.IsValid;

        bounds = CalculateVisibleSpriteBounds(sprite);
        visibleSpriteBoundsCache[sprite] = bounds;
        return bounds.IsValid;
    }

    /// <summary>只扫描 Sprite 对应的像素区域，排除动画帧周围的透明留白。</summary>
    private static SpritePixelBounds CalculateVisibleSpriteBounds(Sprite sprite)
    {
        Texture2D texture = sprite.texture;
        Rect spriteRect = sprite.rect;
        if (texture == null || !texture.isReadable || spriteRect.width <= 0f || spriteRect.height <= 0f)
            return default(SpritePixelBounds);

        int rectX = Mathf.RoundToInt(spriteRect.x);
        int rectY = Mathf.RoundToInt(spriteRect.y);
        int rectWidth = Mathf.RoundToInt(spriteRect.width);
        int rectHeight = Mathf.RoundToInt(spriteRect.height);
        if (rectX < 0 || rectY < 0 || rectWidth <= 0 || rectHeight <= 0 ||
            rectX + rectWidth > texture.width || rectY + rectHeight > texture.height)
        {
            return default(SpritePixelBounds);
        }

        try
        {
            int minX = rectWidth;
            int minY = rectHeight;
            int maxX = -1;
            int maxY = -1;
            for (int y = 0; y < rectHeight; y++)
            {
                for (int x = 0; x < rectWidth; x++)
                {
                    if (texture.GetPixel(rectX + x, rectY + y).a * 255f < VisibleAlphaThreshold)
                        continue;

                    minX = Mathf.Min(minX, x);
                    minY = Mathf.Min(minY, y);
                    maxX = Mathf.Max(maxX, x);
                    maxY = Mathf.Max(maxY, y);
                }
            }

            return maxX >= minX && maxY >= minY
                ? new SpritePixelBounds(minX, minY, maxX, maxY)
                : default(SpritePixelBounds);
        }
        catch (UnityException)
        {
            return default(SpritePixelBounds);
        }
    }

    /// <summary>获取实体当前使用的主 SpriteRenderer，兼容玩家和 AI 动画模块。</summary>
    private static SpriteRenderer FindSourceRenderer(Item item)
    {
        if (item == null)
            return null;

        if (item.Sprite != null && item.Sprite.sprite != null)
            return item.Sprite;

        SpriteRenderer[] renderers = item.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null && renderers[i].sprite != null)
                return renderers[i];
        }

        return null;
    }

    /// <summary>向旧对象与 ECS 表现提供同一场景的阴影透明度；每场景每帧缓存光照查询。</summary>
    public float GetShadowOpacity(Scene actorScene)
    {
        if (!isActiveAndEnabled) return 0f;
        float sunlightT = Mathf.InverseLerp(minSunlightToShow, 1f, GetSunlightIntensity(actorScene));
        return maxShadowAlpha * Mathf.Pow(sunlightT, Mathf.Max(0.01f, sunlightResponsePower));
    }

    /// <summary>按当前场景读取 DayTimeSystem 的有效太阳光照强度。</summary>
    private float GetSunlightIntensity(Scene actorScene)
    {
        string sceneName = actorScene.IsValid() && actorScene.isLoaded
            ? actorScene.name
            : SceneManager.GetActiveScene().name;

        if (lightingFrame == Time.frameCount && lightingSceneName == sceneName)
            return cachedSunlight;

        lightingFrame = Time.frameCount;
        lightingSceneName = sceneName;
        cachedSunlight = 1f;

        DayTimeSystem dayTimeSystem = DayTimeSystem.GetInstance();
        if (dayTimeSystem == null ||
            !dayTimeSystem.TryGetResolvedTimeData(sceneName, out _, out _))
        {
            return cachedSunlight;
        }

        cachedSunlight = Mathf.Clamp01(dayTimeSystem.GetLighting(sceneName));
        return cachedSunlight;
    }

    #endregion

    #region 注册与清理

    /// <summary>扫描当前场景，补齐管理器启用前已经生成的实体。</summary>
    private void RegisterExistingActors()
    {
        RegisterRuntimeActors();

        Item[] items = FindObjectsOfType<Item>(true);
        for (int i = 0; i < items.Length; i++)
            RegisterActor(items[i]);
    }

    /// <summary>从 ItemMgr 的权威运行时注册表补登记漏过事件的角色与植物。</summary>
    private void RegisterRuntimeActors()
    {
        ItemMgr itemMgr = ItemMgr.GetInstance();
        if (itemMgr == null)
            return;

        foreach (Item item in itemMgr.WorldRunTimeItems.Values)
            RegisterActor(item);
    }

    /// <summary>识别角色或根植植物，并记录两种阴影的尺寸规则。</summary>
    private static bool TryClassifyShadowCaster(Item item, out bool isRootedPlant)
    {
        isRootedPlant = false;
        if (item == null || !item.gameObject.activeInHierarchy || IsItemInPool(item))
            return false;

        if (item is Player || RuntimeAiEntityUtility.IsAiEntity(item))
            return true;

        bool groundedPlant = IsGroundedStaticPlant(item);
        bool hasPlantTag = groundedPlant && item.itemData.Tags != null &&
                           (item.itemData.Tags.ContainsTag(Tag.Tree) ||
                            item.itemData.Tags.ContainsTag(Tag.Plant));
        MonoBehaviour[] behaviours = item.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is IAIActor)
                return true;
            if (groundedPlant && behaviours[i] is IPlantableCrop)
                isRootedPlant = true;
        }

        isRootedPlant |= hasPlantTag;
        return isRootedPlant;
    }

    /// <summary>只让留在地面、不可拾取的植物保有根部阴影。</summary>
    private static bool IsGroundedStaticPlant(Item item)
    {
        return item != null && item.Owner == null && item.itemData?.Stack != null &&
               !item.InHand && !item.itemData.Stack.CanBePickedUp;
    }

    /// <summary>判断对象是否已经回收到对象池。</summary>
    private static bool IsItemInPool(Item item)
    {
        return item != null && item.PoolMarker.InPool;
    }

    /// <summary>创建无父级的场景级阴影根节点，避免阴影进入 RuntimeEntities 或 Item 层级。</summary>
    private Transform EnsureShadowRoot()
    {
        if (this == null)
            return null;

        if (shadowRoot != null)
            return shadowRoot;

        GameObject rootObject = new GameObject(ShadowRootName);
        rootObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        rootObject.transform.localScale = Vector3.one;

        Scene managerScene = gameObject.scene;
        if (managerScene.IsValid() && managerScene.isLoaded)
            SceneManager.MoveGameObjectToScene(rootObject, managerScene);

        shadowRoot = rootObject.transform;
        return shadowRoot;
    }

    /// <summary>清理所有阴影绑定。</summary>
    private void ClearBindings()
    {
        foreach (ShadowBinding binding in bindings.Values)
        {
            if (binding != null && binding.ShadowObject != null)
                Destroy(binding.ShadowObject);
        }

        bindings.Clear();
    }

    /// <summary>销毁阴影实例并移除绑定；兼容 Item 已进入 Unity 销毁态的情况。</summary>
    private void DestroyBinding(Item item, ShadowBinding binding)
    {
        if (binding != null && binding.ShadowObject != null)
            Destroy(binding.ShadowObject);

        bindings.Remove(item);
    }

    #endregion

    /// <summary>实体与其独立阴影渲染器的运行时绑定。</summary>
    private sealed class ShadowBinding
    {
        public readonly GameObject ShadowObject;
        public readonly SpriteRenderer Renderer;
        public readonly Color BaseColor;
        public readonly IVisualGroundOffset GroundOffsetProvider;
        public readonly bool IsRootedPlant;
        public SpriteRenderer SourceRenderer;
        public float ShadowWidth;
        public bool InWater;

        public ShadowBinding(GameObject shadowObject, SpriteRenderer renderer,
            IVisualGroundOffset groundOffsetProvider, bool isRootedPlant)
        {
            ShadowObject = shadowObject;
            Renderer = renderer;
            BaseColor = renderer.color;
            GroundOffsetProvider = groundOffsetProvider;
            IsRootedPlant = isRootedPlant;
        }
    }

    /// <summary>Sprite 可见像素的整数边界，坐标原点与 Sprite rect 一致。</summary>
    private readonly struct SpritePixelBounds
    {
        public readonly int MinX;
        public readonly int MinY;
        public readonly int MaxX;
        public readonly int MaxY;
        public readonly bool IsValid;

        public int Width => MaxX - MinX + 1;
        public int Height => MaxY - MinY + 1;

        public SpritePixelBounds(int minX, int minY, int maxX, int maxY)
        {
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
            IsValid = true;
        }
    }
}
