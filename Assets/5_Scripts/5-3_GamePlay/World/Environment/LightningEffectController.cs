using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>只消费单次雷电事件，把逻辑落点投影为分叉闪电、电弧和屏幕闪光。</summary>
[DisallowMultipleComponent]
public sealed class LightningEffectController : MonoBehaviour
{
    #region 表现配置与状态

    private const string LightningShaderResourcePath = "Weather/Shaders/LightningBolt";
    private static readonly int ProgressId = Shader.PropertyToID("_Progress");
    private static readonly int StrengthId = Shader.PropertyToID("_Strength");
    private static readonly int SeedId = Shader.PropertyToID("_Seed");
    private static readonly int BoltSizeId = Shader.PropertyToID("_BoltSize");
    private static readonly int ModeId = Shader.PropertyToID("_Mode");
    [SerializeField, Min(0.01f)] private float flashDurationSeconds = 0.16f;
    [SerializeField, Range(0f, 1f)] private float maximumFlashOpacity = 0.3f;
    [SerializeField, Min(1f)] private float visibleRadius = 128f;
    [SerializeField, Min(0.05f)] private float boltDurationSeconds = 0.28f;
    [SerializeField, Min(1f)] private float boltHeight = 12f;
    [SerializeField, Min(0.5f)] private float boltWidth = 5.5f;
    [SerializeField, Min(0.1f)] private float impactRadius = 2.4f;
    [SerializeField, Range(1, 24)] private int maximumSimultaneousBolts = 12;
    private WeatherMgr weather;
    private GameObject effectsRoot;
    private Mesh sharedQuad;
    private Material ownedMaterial;
    private bool shaderLoadFailed;
    private string presentationWorldKey;
    private readonly List<LightningVisual> visuals = new(12);

    private sealed class LightningVisual
    {
        public GameObject Root;
        public Transform Bolt;
        public Transform Impact;
        public MeshRenderer BoltRenderer;
        public MeshRenderer ImpactRenderer;
        public readonly MaterialPropertyBlock Properties = new();
        public Vector2 LogicalPosition;
        public float StartedTime;
        public float Duration;
        public float Strength;
        public float Seed;
        public float Width;
        public float Height;
        public bool Active;
    }

    #endregion

    #region 订阅与释放

    public void Initialize(WeatherMgr owner)
    {
        if (weather == owner)
            return;
        Shutdown();
        weather = owner;
        if (weather != null)
            weather.LightningOccurred += HandleLightning;
    }

    public void Shutdown()
    {
        if (weather != null)
            weather.LightningOccurred -= HandleLightning;
        weather = null;
        ReleaseOwnedResources();
        shaderLoadFailed = false;
    }

    private void OnEnable() => Initialize(WeatherMgr.ExistingInstance);
    private void OnDisable() => Shutdown();
    private void OnDestroy() => Shutdown();

    private void ReleaseOwnedResources()
    {
        if (effectsRoot != null)
        {
            effectsRoot.SetActive(false);
            DestroyOwned(effectsRoot);
        }
        effectsRoot = null;
        visuals.Clear();
        DestroyOwned(ownedMaterial);
        DestroyOwned(sharedQuad);
        ownedMaterial = null;
        sharedQuad = null;
        presentationWorldKey = null;
    }

    private static void DestroyOwned(UnityEngine.Object owned)
    {
        if (owned == null) return;
        if (Application.isPlaying) Destroy(owned);
        else DestroyImmediate(owned);
    }

    #endregion

    #region 单次雷电表现

    private bool TryGetPresentationPlayer(out Player player)
    {
        player = null;
        if (weather == null || !weather.isActiveAndEnabled ||
            WeatherMgr.IsWeatherSuppressedInDimension(DimensionManager.ExistingInstance?.ActiveDefinition))
            return false;

        player = ItemMgr.GetInstance()?.User_Player;
        return player != null && player.IsLocalProfile && player.gameObject.activeInHierarchy &&
               player.gameObject.scene.IsValid() && player.gameObject.scene.isLoaded;
    }

    private void HandleLightning(RegionalLightningEvent lightning)
    {
        if (!float.IsFinite(lightning.Position.x) || !float.IsFinite(lightning.Position.y) ||
            !float.IsFinite(lightning.Strength) || !TryGetPresentationPlayer(out Player player) ||
            !string.Equals(lightning.WorldKey, weather.GetActivePlanetData()?.Name, StringComparison.Ordinal))
            return;

        float strength = Mathf.Clamp01(lightning.Strength) * GetDistanceAttenuation(player, lightning.Position);
        if (strength <= 0.001f || !EnsureResources(player, lightning.WorldKey))
            return;

        LightningVisual visual = AcquireVisual(player);
        if (visual == null) return;
        visual.LogicalPosition = lightning.Position;
        visual.StartedTime = Time.time;
        visual.Duration = Mathf.Max(Mathf.Max(0.05f, boltDurationSeconds), flashDurationSeconds);
        visual.Strength = Mathf.Clamp01(lightning.Strength);
        uint hash = unchecked((uint)(lightning.Region.X * 73856093 ^ lightning.Region.Y * 19349663 ^ lightning.Sequence * 83492791));
        visual.Seed = (hash & 0x00ffffffu) / 4096f;
        visual.Width = Mathf.Max(0.5f, boltWidth);
        visual.Height = Mathf.Max(1f, boltHeight);
        visual.Bolt.localScale = new Vector3(visual.Width, visual.Height, 1f);
        float radius = Mathf.Max(0.1f, impactRadius);
        visual.Impact.localScale = new Vector3(radius * 2f, radius * 0.85f, 1f);
        visual.Impact.localPosition = new Vector3(0f, -radius * 0.425f, 0f);
        visual.Active = true;
        visual.Root.SetActive(true);
        UpdateVisual(visual, player, Time.time);
    }

    private void LateUpdate()
    {
        if (!TryGetPresentationPlayer(out Player player) ||
            (effectsRoot != null && (effectsRoot.scene != player.gameObject.scene ||
             !string.Equals(presentationWorldKey, weather.GetActivePlanetData()?.Name, StringComparison.Ordinal))))
        {
            ReleaseOwnedResources();
            return;
        }

        float now = Time.time;
        foreach (LightningVisual visual in visuals)
        {
            if (!visual.Active) continue;
            if (visual.Root == null || now - visual.StartedTime >= visual.Duration)
            {
                visual.Active = false;
                if (visual.Root != null) visual.Root.SetActive(false);
                continue;
            }
            UpdateVisual(visual, player, now);
        }
    }

    private float GetDistanceAttenuation(Player player, Vector2 logicalPosition)
    {
        Vector2 logicalPlayer = WorldLocalPresentation.ToLogical((Vector2)player.transform.position);
        float distance = WorldTopologyRuntime.ShortestDelta(logicalPlayer, logicalPosition).magnitude;
        return Mathf.Clamp01(1f - distance / Mathf.Max(1f, visibleRadius));
    }

    private void UpdateVisual(LightningVisual visual, Player player, float now)
    {
        // 落点保持权威逻辑坐标，每帧选本地玩家附近的镜像，跨循环边界不会闪到另一边。
        visual.Root.transform.position = WorldLocalPresentation.ProjectPosition(visual.LogicalPosition,
            (Vector2)player.transform.position);
        MaterialPropertyBlock properties = visual.Properties;
        properties.SetFloat(ProgressId, Mathf.Clamp01((now - visual.StartedTime) / Mathf.Max(0.05f, boltDurationSeconds)));
        properties.SetFloat(StrengthId, visual.Strength * GetDistanceAttenuation(player, visual.LogicalPosition));
        properties.SetFloat(SeedId, visual.Seed);
        properties.SetVector(BoltSizeId, new Vector4(visual.Width, visual.Height, 0.035f, 0.16f));
        properties.SetFloat(ModeId, 0f);
        visual.BoltRenderer.SetPropertyBlock(properties);
        properties.SetFloat(ModeId, 1f);
        visual.ImpactRenderer.SetPropertyBlock(properties);
    }

    private void OnGUI()
    {
        if (Event.current.type != EventType.Repaint || !TryGetPresentationPlayer(out Player player))
            return;

        float flashStrength = 0f;
        foreach (LightningVisual visual in visuals)
        {
            if (!visual.Active) continue;
            float progress = Mathf.Clamp01((Time.time - visual.StartedTime) / Mathf.Max(0.01f, flashDurationSeconds));
            if (progress >= 1f) continue;
            float fading = (1f - progress) * (1f - progress);
            float secondaryPulse = Mathf.Exp(-Mathf.Pow((progress - 0.55f) * 12f, 2f)) * 0.18f;
            flashStrength += visual.Strength * GetDistanceAttenuation(player, visual.LogicalPosition) * (fading + secondaryPulse);
        }
        if (flashStrength <= 0.001f) return;

        Color previousColor = GUI.color;
        int previousDepth = GUI.depth;
        GUI.color = new Color(0.9f, 0.95f, 1f, maximumFlashOpacity * Mathf.Clamp01(flashStrength));
        GUI.depth = -1000;
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = previousColor;
        GUI.depth = previousDepth;
    }

    #endregion

    #region 共享资源与有限对象池

    private bool EnsureResources(Player player, string worldKey)
    {
        if (effectsRoot != null && (effectsRoot.scene != player.gameObject.scene ||
            !string.Equals(presentationWorldKey, worldKey, StringComparison.Ordinal)))
            ReleaseOwnedResources();
        if (effectsRoot != null) return true;
        if (shaderLoadFailed) return false;

        Shader shader = Resources.Load<Shader>(LightningShaderResourcePath);
        if (shader == null)
        {
            shaderLoadFailed = true;
            Debug.LogError($"[LightningEffect] 缺少闪电 Shader，路径={LightningShaderResourcePath}。", this);
            return false;
        }
        ownedMaterial = new Material(shader) { name = "LightningBolt (Runtime)", hideFlags = HideFlags.DontSave };
        sharedQuad = new Mesh { name = "LightningQuad (Runtime)", hideFlags = HideFlags.DontSave };
        sharedQuad.vertices = new[]
        {
            new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f),
            new Vector3(-0.5f, 1f, 0f), new Vector3(0.5f, 1f, 0f)
        };
        sharedQuad.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
        sharedQuad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        sharedQuad.RecalculateBounds();
        sharedQuad.UploadMeshData(true);
        effectsRoot = new GameObject("WeatherLightningEffects");
        SceneManager.MoveGameObjectToScene(effectsRoot, player.gameObject.scene);
        presentationWorldKey = worldKey;
        return true;
    }

    private LightningVisual AcquireVisual(Player player)
    {
        foreach (LightningVisual visual in visuals)
            if (!visual.Active && visual.Root != null) return visual;
        // 同时落下的闪电各自保留到寿命结束，池满时只舍弃新表现，伤害仍由权威层独立结算。
        if (visuals.Count >= Mathf.Clamp(maximumSimultaneousBolts, 1, 24)) return null;

        LightningVisual created = new() { Root = new GameObject("LightningStrike") };
        created.Root.transform.SetParent(effectsRoot.transform, false);
        created.Root.layer = player.gameObject.layer;
        created.BoltRenderer = CreateRenderer("Bolt", created.Root.transform, player.gameObject.layer,
            WorldSortingManager.WorldEffectCategory);
        created.ImpactRenderer = CreateRenderer("ImpactArcs", created.Root.transform, player.gameObject.layer,
            WorldSortingManager.GroundSplashCategory);
        created.Bolt = created.BoltRenderer.transform;
        created.Impact = created.ImpactRenderer.transform;
        created.Root.SetActive(false);
        visuals.Add(created);
        return created;
    }

    private MeshRenderer CreateRenderer(string objectName, Transform parent, int layer, string sortingCategory)
    {
        GameObject child = new(objectName) { layer = layer };
        child.transform.SetParent(parent, false);
        child.AddComponent<MeshFilter>().sharedMesh = sharedQuad;
        MeshRenderer renderer = child.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = ownedMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        WorldSortingManager sorting = WorldSortingManager.GetInstance();
        if (sorting != null) sorting.ApplyRenderer(renderer, sortingCategory);
        else
        {
            WorldSortingManager.GetResourceSortingKey(sortingCategory, out int layerId, out int order);
            renderer.sortingLayerID = layerId;
            renderer.sortingOrder = order;
        }
        return renderer;
    }

    #endregion
}
