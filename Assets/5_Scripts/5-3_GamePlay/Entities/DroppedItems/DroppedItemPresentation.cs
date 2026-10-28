using System;
using System.Collections.Generic;
using FlatWorld.DroppedItems;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>轻量掉落物共享视觉定义；只读取 Sprite 与外观参数，不实例化完整 Item。</summary>
internal sealed class DroppedItemVisual
{
    public Sprite Sprite;
    public Material SpriteMaterial;
    public Color Color;
    public int Layer;
    public int Order;
    public Vector3 LocalPosition;
    public Quaternion LocalRotation;
    public Vector3 LocalScale;

    public static Sprite ResolveSprite(ItemData data)
    {
        GameRes resources = GameRes.ExistingInstance;
        if (resources == null || !resources.TryGetItemPresentation(data, out _, out Sprite sprite) || sprite == null)
            throw new InvalidOperationException($"掉落物缺少显示贴图：{data.IDName}");
        return sprite;
    }

    public static DroppedItemVisual Resolve(ItemData data, Sprite sprite)
    {
        GameRes resources = GameRes.ExistingInstance;
        resources.TryGetItemDefinition(data.IDName, out RuntimeItemDefinition definition);
        ItemVisualDefinitionDto visual = definition?.Visual;
        Transform shellRenderer = definition?.ShellPrefab != null && !string.IsNullOrEmpty(visual?.RendererPath)
            ? definition.ShellPrefab.transform.Find(visual.RendererPath)
            : null;
        SpriteRenderer source = shellRenderer != null ? shellRenderer.GetComponent<SpriteRenderer>() : null;
        Vector3 position = visual?.RendererLocalPosition ?? (shellRenderer != null ? shellRenderer.localPosition : Vector3.zero);
        Quaternion rotation = Quaternion.Euler(visual?.RendererLocalEulerAngles ??
            (shellRenderer != null ? shellRenderer.localEulerAngles : Vector3.zero));
        Vector3 scale = visual?.RendererLocalScale ?? (shellRenderer != null ? shellRenderer.localScale : Vector3.one);
        if (visual?.FlipX ?? (source != null && source.flipX)) scale.x *= -1f;
        if (visual?.FlipY ?? (source != null && source.flipY)) scale.y *= -1f;

        WorldRenderingManager sorting = WorldRenderingManager.Instance;
        if (sorting == null)
            throw new InvalidOperationException("掉落物缺少世界排序管理器。");
        sorting.GetSortingKey(WorldSortingManager.WorldItemCategory, out int sortingLayerId, out int sortingOrder);
        return new DroppedItemVisual
        {
            Sprite = sprite,
            SpriteMaterial = definition?.Material ?? source?.sharedMaterial ??
                Resources.Load<Material>("DroppedItems/DroppedItemLit"),
            Color = visual?.Color ?? (source != null ? source.color : Color.white),
            Layer = sortingLayerId,
            Order = sortingOrder,
            LocalPosition = position,
            LocalRotation = rotation,
            LocalScale = scale
        };
    }
}

/// <summary>
/// 轻量 GameObject 表现池。每个可见掉落物只占一个 SpriteRenderer GameObject，
/// 不挂 Update、Collider、Rigidbody2D、Item 或 Module；离开镜头后立即回池。
/// </summary>
internal sealed class DroppedItemPresentation : IDisposable
{
    private sealed class PooledView
    {
        public GameObject Root;
        public SpriteRenderer Renderer;
        public readonly MaterialPropertyBlock Properties = new();
        public DroppedItemVisual Visual;
        public LightweightDroppedBody LastBody;
        public Vector2 LastNearestPosition;
        public Matrix4x4 LastProjection;
        public WorldItemWaterRules.DriftInterpolation WaterDrift;
        public ShaderFlameEffect CombustionFlame;
        public bool LastBurning;
        public bool HasState;
    }

    private const float CullingMargin = 4f;
    private const int MaxRetainedViews = 512;
    private static readonly int MainTextureId = Shader.PropertyToID("_MainTex");
    private static readonly int UsePerRendererWaterId = Shader.PropertyToID("_UsePerRendererDroppedWater");
    private static readonly int DroppedWaterParamsId = Shader.PropertyToID("_DroppedWaterParams");

    private readonly Scene scene;
    private readonly LightweightDroppedItemSimulation simulation;
    private readonly Dictionary<int, DroppedItemVisual> visuals;
    private readonly Dictionary<int, ItemData> payloads;
    private readonly Dictionary<int, PooledView> active = new();
    private readonly Stack<PooledView> pool = new();
    private readonly HashSet<int> candidates = new();
    private readonly HashSet<int> visible = new();
    private readonly List<int> releaseScratch = new();
    private readonly Material sharedMaterial;
    private readonly GameObject poolRoot;
    private bool disposed;

    public int VisibleViewCount => active.Count;

    public DroppedItemPresentation(
        Scene scene,
        LightweightDroppedItemSimulation simulation,
        Dictionary<int, DroppedItemVisual> visuals,
        Dictionary<int, ItemData> payloads)
    {
        this.scene = scene;
        this.simulation = simulation;
        this.visuals = visuals;
        this.payloads = payloads;
        sharedMaterial = Resources.Load<Material>("DroppedItems/DroppedItemLit");
        if (sharedMaterial == null)
            throw new InvalidOperationException("缺少轻量掉落物共享材质 DroppedItems/DroppedItemLit。");

        poolRoot = new GameObject("轻量掉落物_对象池") { hideFlags = HideFlags.DontSave };
        poolRoot.transform.SetParent(RuntimeSceneObjectHierarchy.GetOrCreateCategoryRoot(
            scene, RuntimeSceneObjectCategory.DroppedItems), false);
    }

    public void Changed(int id)
    {
        // 表现由统一 Present 刷新；这里不产生逐物品回调或组件更新。
    }

    #region 水流显示插值

    public void RecordWaterDrift(int id, Vector2 movement, float seconds)
    {
        if (active.TryGetValue(id, out PooledView view))
            view.WaterDrift.RecordMovement(movement, seconds, Time.timeAsDouble);
    }

    #endregion

    public void Remove(int id)
    {
        if (!active.TryGetValue(id, out PooledView view)) return;
        active.Remove(id);
        Release(view);
    }

    public void Present(
        Camera camera,
        WorldTopologyDomain domain,
        Dictionary<Vector2Int, HashSet<int>> spatial,
        float spatialCellSize,
        Matrix4x4 worldToDisplay)
    {
        if (disposed) return;
        if (camera == null)
        {
            ReleaseAllActive();
            return;
        }

        Matrix4x4 displayToWorld = worldToDisplay.inverse;
        Vector2 cameraCenter = displayToWorld.MultiplyPoint3x4(camera.transform.position);
        float halfHeight = camera.orthographic ? camera.orthographicSize : 50f;
        float halfWidth = halfHeight * Mathf.Max(0.1f, camera.aspect);
        Vector3 horizontal = displayToWorld.MultiplyVector(new Vector3(halfWidth, 0f));
        Vector3 vertical = displayToWorld.MultiplyVector(new Vector3(0f, halfHeight));
        float xRadius = Mathf.Abs(horizontal.x) + Mathf.Abs(vertical.x) + CullingMargin;
        float yRadius = Mathf.Abs(horizontal.y) + Mathf.Abs(vertical.y) + CullingMargin;

        CollectCandidates(cameraCenter, xRadius, yRadius, domain, spatial, spatialCellSize, candidates);
        visible.Clear();

        foreach (int id in candidates)
        {
            if (!simulation.Contains(id) || !visuals.TryGetValue(id, out DroppedItemVisual visual))
                continue;
            LightweightDroppedBody body = simulation.Get(id);
            if (body.IsOnBottom) continue;
            active.TryGetValue(id, out PooledView view);
            float2 displayPosition = body.Position;
            if (view != null)
            {
                if (body.WaterKind == 0 || simulation.TryGetFlight(id, out _))
                    view.WaterDrift = default;
                Vector2 offset = view.WaterDrift.GetOffset(Time.timeAsDouble);
                displayPosition += new float2(offset.x, offset.y);
            }
            float2 nearest = domain.NearestImagePosition(cameraCenter, displayPosition);
            if (Mathf.Abs(nearest.x - cameraCenter.x) > xRadius ||
                Mathf.Abs(nearest.y - cameraCenter.y) > yRadius)
                continue;

            visible.Add(id);
            if (view == null)
            {
                view = Acquire();
                active.Add(id, view);
            }
            Apply(view, id, visual, body, nearest, worldToDisplay);
        }

        releaseScratch.Clear();
        foreach (KeyValuePair<int, PooledView> pair in active)
            if (!visible.Contains(pair.Key))
                releaseScratch.Add(pair.Key);
        for (int i = 0; i < releaseScratch.Count; i++)
            Remove(releaseScratch[i]);
        releaseScratch.Clear();
    }

    /// <summary>纯空间桶候选选择，可供规则测试直接核验视野与环绕接缝。</summary>
    internal static void CollectCandidates(
        Vector2 center,
        float xRadius,
        float yRadius,
        WorldTopologyDomain domain,
        Dictionary<Vector2Int, HashSet<int>> spatial,
        float cellSize,
        HashSet<int> candidates)
    {
        candidates.Clear();
        if (spatial.Count == 0 || cellSize <= 0f) return;

        int minX = Mathf.FloorToInt((center.x - xRadius) / cellSize);
        int maxX = Mathf.FloorToInt((center.x + xRadius) / cellSize);
        int minY = Mathf.FloorToInt((center.y - yRadius) / cellSize);
        int maxY = Mathf.FloorToInt((center.y + yRadius) / cellSize);

        // 掉落物稀疏时只检查有物品的桶；拉远镜头无需逐格探测大片空地。
        // 外扩一格保证边缘桶完整包含，最终仍由物品的精确坐标决定是否可见。
        long columns = (long)maxX - minX + 1;
        long rows = (long)maxY - minY + 1;
        if (columns <= 0 || rows <= 0) return;
        if (spatial.Count < (double)columns * rows)
        {
            float2 cameraPoint = new(center.x, center.y);
            float paddingX = xRadius + cellSize;
            float paddingY = yRadius + cellSize;
            foreach (KeyValuePair<Vector2Int, HashSet<int>> pair in spatial)
            {
                Vector2Int cell = pair.Key;
                float2 cellCenter = new((cell.x + 0.5f) * cellSize, (cell.y + 0.5f) * cellSize);
                float2 delta = domain.ShortestDelta(cameraPoint, cellCenter);
                if (math.abs(delta.x) > paddingX || math.abs(delta.y) > paddingY)
                    continue;
                foreach (int id in pair.Value)
                    candidates.Add(id);
            }
            return;
        }

        // 密集掉落物仍按视野网格查询，复杂度不随世界上全部物品数量增长。
        if (!domain.IsWrapped)
        {
            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                if (!spatial.TryGetValue(new Vector2Int(x, y), out HashSet<int> bucket)) continue;
                foreach (int id in bucket) candidates.Add(id);
            }
            return;
        }

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float2 logicalPoint = domain.Normalize(new float2(
                    (x + 0.5f) * cellSize,
                    (y + 0.5f) * cellSize));
                Vector2Int cell = new(
                    Mathf.FloorToInt(logicalPoint.x / cellSize),
                    Mathf.FloorToInt(logicalPoint.y / cellSize));
                if (!spatial.TryGetValue(cell, out HashSet<int> bucket)) continue;
                foreach (int id in bucket)
                    candidates.Add(id);
            }
        }
    }

    private PooledView Acquire()
    {
        PooledView view;
        if (pool.Count != 0)
        {
            view = pool.Pop();
            view.Root.SetActive(true);
            return view;
        }

        GameObject root = new("轻量掉落物");
        root.hideFlags = HideFlags.DontSave;
        root.transform.SetParent(poolRoot.transform, false);
        SpriteRenderer renderer = root.AddComponent<SpriteRenderer>();
        renderer.spriteSortPoint = SpriteSortPoint.Pivot;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return new PooledView { Root = root, Renderer = renderer };
    }

    #region 每实例贴图与水线

    private void Apply(
        PooledView view,
        int id,
        DroppedItemVisual visual,
        LightweightDroppedBody body,
        float2 nearestPosition,
        Matrix4x4 worldToDisplay)
    {
        if (!ReferenceEquals(view.Visual, visual))
        {
            view.Visual = visual;
            view.HasState = false;
            view.Renderer.sprite = visual.Sprite;
            view.Renderer.sharedMaterial = sharedMaterial;
            view.Renderer.color = visual.Color;
            view.Renderer.sortingLayerID = visual.Layer;
            view.Renderer.sortingOrder = visual.Order;
            view.Renderer.spriteSortPoint = SpriteSortPoint.Pivot;
        }

        Vector2 nearest = new(nearestPosition.x, nearestPosition.y);
        bool burning = payloads.TryGetValue(id, out ItemData data) && data.MatterState?.IsBurning == true;
        if (view.HasState && SamePresentationState(view.LastBody, body) &&
            view.LastNearestPosition == nearest && view.LastBurning == burning && view.LastProjection == worldToDisplay)
            return;

        float submergedScale = WorldItemWaterRules.ResolveSubmergedScale(body.SubmergedProgress);
        Vector3 bodyScale = new(body.Scale.x * submergedScale, body.Scale.y * submergedScale, 1f);
        Quaternion bodyRotation = Quaternion.Euler(0f, 0f, body.Rotation);
        Vector3 basePosition = new(nearestPosition.x, nearestPosition.y + body.VisualHeight, 0f);
        view.Root.transform.SetPositionAndRotation(
            worldToDisplay.MultiplyPoint3x4(basePosition + bodyRotation * Vector3.Scale(visual.LocalPosition, bodyScale)),
            worldToDisplay.rotation * bodyRotation * visual.LocalRotation);
        view.Root.transform.localScale = Vector3.Scale(worldToDisplay.lossyScale, Vector3.Scale(bodyScale, visual.LocalScale));

        Bounds bounds = view.Renderer.bounds;
        float height = Mathf.Max(0.0001f, bounds.size.y);
        float waterLine = bounds.min.y + height * Mathf.Clamp01(body.LiquidDepth);
        view.Properties.Clear();
        // 自定义水线参数会覆盖 SpriteRenderer 的属性块，必须同时保留当前精灵贴图。
        view.Properties.SetTexture(MainTextureId, visual.Sprite.texture);
        view.Properties.SetFloat(UsePerRendererWaterId, 1f);
        view.Properties.SetVector(DroppedWaterParamsId, new Vector4(
            body.WaterKind != 0 ? 1f : 0f,
            waterLine,
            height,
            (id & 1023) * 0.017f));
        view.Renderer.SetPropertyBlock(view.Properties);
        SetCombustionPlaying(view, burning, visual.Layer, visual.Order + 2);
        view.LastBody = body;
        view.LastNearestPosition = nearest;
        view.LastProjection = worldToDisplay;
        view.LastBurning = burning;
        view.HasState = true;
    }

    private static bool SamePresentationState(LightweightDroppedBody left, LightweightDroppedBody right)
    {
        return left.Position.x == right.Position.x && left.Position.y == right.Position.y &&
               left.Scale.x == right.Scale.x && left.Scale.y == right.Scale.y &&
               left.Rotation == right.Rotation && left.VisualHeight == right.VisualHeight &&
               left.LiquidDepth == right.LiquidDepth && left.SubmergedProgress == right.SubmergedProgress &&
               left.WaterKind == right.WaterKind;
    }

    #endregion

    #region 燃烧表现

    /// <summary>轻量掉落物按需创建共享网格火焰，动画不增加逐物品 Update。</summary>
    private static void SetCombustionPlaying(PooledView view, bool burning, int sortingLayerId, int sortingOrder)
    {
        if (!burning)
        {
            if (view.CombustionFlame != null)
                view.CombustionFlame.SetBurning(false);
            return;
        }

        // 独立根保持世界向上与固定尺寸，掉落物翻滚或缩放时不把火焰一起倒置。
        ShaderFlameEffect flame = view.CombustionFlame ??= WorldItemCombustionVisual.Create(view.Root.transform.parent);
        if (flame.transform.position != view.Root.transform.position)
            flame.transform.position = view.Root.transform.position;
        flame.SetSorting(sortingLayerId, sortingOrder);
        flame.SetBurning(true);
    }


    #endregion

    private void Release(PooledView view)
    {
        if (view?.Root == null) return;
        view.Visual = null;
        view.HasState = false;
        view.WaterDrift = default;
        view.LastBurning = false;
        if (view.CombustionFlame != null)
            view.CombustionFlame.SetBurning(false);
        view.Renderer.sprite = null;
        view.Renderer.SetPropertyBlock(null);
        view.Root.SetActive(false);
        if (pool.Count < MaxRetainedViews)
            pool.Push(view);
        else
        {
            if (view.CombustionFlame != null) DestroyObject(view.CombustionFlame.gameObject);
            DestroyObject(view.Root);
        }
    }

    private void ReleaseAllActive()
    {
        releaseScratch.Clear();
        releaseScratch.AddRange(active.Keys);
        for (int i = 0; i < releaseScratch.Count; i++)
            Remove(releaseScratch[i]);
        releaseScratch.Clear();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        ReleaseAllActive();
        while (pool.Count != 0)
            DestroyObject(pool.Pop().Root);
        DestroyObject(poolRoot);
        active.Clear();
        candidates.Clear();
        visible.Clear();
    }

    private static void DestroyObject(UnityEngine.Object target)
    {
        if (target == null) return;
        if (Application.isPlaying) UnityEngine.Object.Destroy(target);
        else UnityEngine.Object.DestroyImmediate(target);
    }
}
