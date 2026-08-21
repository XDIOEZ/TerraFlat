using FlatWorld.WorldModel;
using UnityEngine;

/// <summary>
/// 雨滴落地水花的纯表现控制器。
/// 它由 WeatherMgr 的独立 Prefab 启停，优先在已加载的非水地形上复用同一个粒子系统发射环形水花；
/// 地形刚加载、尚无法采样时会在可视范围内降级发射，避免雨天完全没有落地反馈。
/// 不修改原雨层、天气数据或地形数据。默认小雨约每秒 12 个、暴雨约每秒 48 个，最多同时保留 80 个粒子。
/// </summary>
[DisallowMultipleComponent]
public sealed class RainGroundSplashController : MonoBehaviour
{
    #region 配置

    [Header("材质与排序")]
    [SerializeField] private Material _splashMaterial; // 环形水花材质
    [SerializeField] private string _sortingLayerName = "Default"; // 位于地面之上、角色之下
    [SerializeField] private int _sortingOrder = 40; // 同层排序
    [SerializeField] private float _splashZ; // 世界空间深度

    [Header("密度")]
    [SerializeField, Min(1)] private int _maxParticles = 80; // 粒子上限
    [SerializeField, Min(0f)] private float _lightRainSplashesPerSecond = 12f; // 小雨频率
    [SerializeField, Min(0f)] private float _heavyRainSplashesPerSecond = 48f; // 大雨频率
    [SerializeField, Min(1)] private int _groundSampleAttempts = 10; // 单次水花的最大采样次数
    [SerializeField] private Vector2 _cameraPadding = new(0.15f, 0.15f); // 可视范围外扩

    [Header("外观")]
    [SerializeField] private Vector2 _lifetimeRange = new(0.32f, 0.5f); // 存活时间范围
    [SerializeField] private Vector2 _sizeRange = new(0.36f, 0.58f); // 环形尺寸范围
    [SerializeField] private Color _splashColor = new(0.72f, 0.94f, 1f, 0.78f); // 水花颜色

    #endregion

    #region 运行时状态

    private const int MaxEmitsPerFrame = 5;
    private const float MaxSpawnAccumulator = 1f;
    private const uint InitialRandomState = 0xB5297A4Du;

    private ParticleSystem _splashParticles;
    private ParticleSystemRenderer _splashRenderer;
    private Camera _cachedCamera;
    private float _rainIntensity;
    private float _spawnAccumulator;
    private uint _randomState = InitialRandomState;

    #endregion

    #region 生命周期

    private void Awake()
    {
        EnsureParticleSystem();
    }

    private void OnEnable()
    {
        EnsureParticleSystem();
    }

    private void Update()
    {
        if (_rainIntensity <= 0.001f || _splashParticles == null)
            return;

        Camera targetCamera = GetMainCamera();
        ChunkMgr chunkMgr = ChunkMgr.Instance;
        if (targetCamera == null || chunkMgr == null)
            return;

        if (!_splashParticles.isPlaying)
            _splashParticles.Play(true);

        float splashRate = Mathf.Lerp(
            _lightRainSplashesPerSecond,
            _heavyRainSplashesPerSecond,
            _rainIntensity);
        _spawnAccumulator = Mathf.Min(
            MaxSpawnAccumulator,
            _spawnAccumulator + splashRate * Mathf.Max(0f, Time.deltaTime));

        int emitCount = Mathf.Min(Mathf.FloorToInt(_spawnAccumulator), MaxEmitsPerFrame);
        if (emitCount <= 0)
            return;

        _spawnAccumulator -= emitCount;
        for (int i = 0; i < emitCount; i++)
        {
            if (TryFindVisibleGround(targetCamera, chunkMgr, out Vector3 splashPosition))
                EmitSplash(splashPosition);
        }
    }

    private void OnDisable()
    {
        _rainIntensity = 0f;
        _spawnAccumulator = 0f;
        ClearParticles();
    }

    private void OnValidate()
    {
        _maxParticles = Mathf.Max(1, _maxParticles);
        _lightRainSplashesPerSecond = Mathf.Max(0f, _lightRainSplashesPerSecond);
        _heavyRainSplashesPerSecond = Mathf.Max(_lightRainSplashesPerSecond, _heavyRainSplashesPerSecond);
        _groundSampleAttempts = Mathf.Max(1, _groundSampleAttempts);
        _cameraPadding.x = Mathf.Max(0f, _cameraPadding.x);
        _cameraPadding.y = Mathf.Max(0f, _cameraPadding.y);
        _lifetimeRange = NormalizeRange(_lifetimeRange, 0.01f);
        _sizeRange = NormalizeRange(_sizeRange, 0.01f);
    }

    #endregion

    #region 公共接口

    /// <summary>由天气管理器同步当前雨势，数值仅控制此表现层的发射频率。</summary>
    public void ApplySettings(float intensity)
    {
        _rainIntensity = Mathf.Clamp01(intensity);
        EnsureParticleSystem();

        if (_rainIntensity <= 0.001f)
        {
            _spawnAccumulator = 0f;
            ClearParticles();
        }
    }

    #endregion

    #region 粒子初始化

    /// <summary>只创建一次粒子系统，后续所有水花均通过 Emit 复用。</summary>
    private void EnsureParticleSystem()
    {
        if (_splashParticles != null)
            return;

        Transform child = transform.Find("RainGroundSplashes");
        GameObject splashObject = child != null ? child.gameObject : new GameObject("RainGroundSplashes");
        if (child == null)
        {
            splashObject.layer = gameObject.layer;
            splashObject.transform.SetParent(transform, false);
        }

        _splashParticles = splashObject.GetComponent<ParticleSystem>();
        if (_splashParticles == null)
            _splashParticles = splashObject.AddComponent<ParticleSystem>();

        _splashRenderer = _splashParticles.GetComponent<ParticleSystemRenderer>();
        ConfigureParticleSystem();
    }

    /// <summary>配置世界空间、无物理碰撞、短生命周期的环形粒子。</summary>
    private void ConfigureParticleSystem()
    {
        ParticleSystem.MainModule main = _splashParticles.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        main.maxParticles = _maxParticles;
        main.startLifetime = new ParticleSystem.MinMaxCurve(_lifetimeRange.x, _lifetimeRange.y);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(_sizeRange.x, _sizeRange.y);
        main.startColor = Color.white;

        ParticleSystem.EmissionModule emission = _splashParticles.emission;
        emission.enabled = false;

        ParticleSystem.ShapeModule shape = _splashParticles.shape;
        shape.enabled = false;

        ParticleSystem.VelocityOverLifetimeModule velocity = _splashParticles.velocityOverLifetime;
        velocity.enabled = false;

        ParticleSystem.SizeOverLifetimeModule size = _splashParticles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(
            1f,
            new AnimationCurve(
                new Keyframe(0f, 0.35f),
                new Keyframe(0.32f, 0.82f),
                new Keyframe(1f, 1f)));

        ParticleSystem.ColorOverLifetimeModule color = _splashParticles.colorOverLifetime;
        color.enabled = true;
        Gradient alphaGradient = new();
        alphaGradient.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f)
            },
            new[]
            {
                new GradientAlphaKey(0.7f, 0f),
                new GradientAlphaKey(0.45f, 0.45f),
                new GradientAlphaKey(0f, 1f)
            });
        color.color = alphaGradient;

        if (_splashRenderer != null)
        {
            _splashRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            _splashRenderer.sharedMaterial = _splashMaterial;
            _splashRenderer.sortingLayerName = _sortingLayerName;
            _splashRenderer.sortingOrder = _sortingOrder;
            _splashRenderer.enableGPUInstancing = true;
        }

        _splashParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    #endregion

    #region 地面采样与发射

    /// <summary>优先在相机可见范围的已加载地形中寻找落点，海面也保留雨滴水花。</summary>
    private bool TryFindVisibleGround(Camera targetCamera, ChunkMgr chunkMgr, out Vector3 splashPosition)
    {
        splashPosition = default;
        float viewportHeight = targetCamera.orthographic ? targetCamera.orthographicSize * 2f : 10f;
        float viewportWidth = viewportHeight * Mathf.Max(0.1f, targetCamera.aspect);
        Vector3 cameraPosition = targetCamera.transform.position;
        float minX = cameraPosition.x - viewportWidth * 0.5f - _cameraPadding.x;
        float maxX = cameraPosition.x + viewportWidth * 0.5f + _cameraPadding.x;
        float minY = cameraPosition.y - viewportHeight * 0.5f - _cameraPadding.y;
        float maxY = cameraPosition.y + viewportHeight * 0.5f + _cameraPadding.y;
        bool hasRuntimeSample = false;
        Vector3 fallbackPosition = default;

        for (int attempt = 0; attempt < _groundSampleAttempts; attempt++)
        {
            Vector2 candidate = new(
                Mathf.Lerp(minX, maxX, Next01(ref _randomState)),
                Mathf.Lerp(minY, maxY, Next01(ref _randomState)));
            fallbackPosition = new Vector3(candidate.x, candidate.y, _splashZ);

            if (!chunkMgr.TryGetRuntimeTerrainTile(candidate, out RuntimeTerrainTileSample sample))
                continue;

            hasRuntimeSample = true;
            if (sample.TopTileId == 0 || (sample.Cell.Flags & TerrainCellFlags.Blocking) != 0)
                continue;

            splashPosition = fallbackPosition;
            return true;
        }

        // 运行时区块在刚进入世界时可能尚未 Ready；此时保留可视反馈，下一帧会自动恢复严格地形采样。
        if (!hasRuntimeSample)
        {
            splashPosition = fallbackPosition;
            return true;
        }

        return false;
    }

    /// <summary>按随机大小与生命周期发射一颗环形水花。</summary>
    private void EmitSplash(Vector3 splashPosition)
    {
        ParticleSystem.EmitParams emitParams = new()
        {
            position = splashPosition,
            startColor = _splashColor,
            startLifetime = NextRange(ref _randomState, _lifetimeRange),
            startSize = NextRange(ref _randomState, _sizeRange)
        };
        _splashParticles.Emit(emitParams, 1);
    }

    private Camera GetMainCamera()
    {
        if (_cachedCamera == null || !_cachedCamera.isActiveAndEnabled)
            _cachedCamera = Camera.main;
        return _cachedCamera;
    }

    #endregion

    #region 工具

    /// <summary>停雨或禁用时立即清理遗留水花。</summary>
    private void ClearParticles()
    {
        if (_splashParticles != null)
            _splashParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private static Vector2 NormalizeRange(Vector2 range, float minimum)
    {
        float min = Mathf.Max(minimum, Mathf.Min(range.x, range.y));
        float max = Mathf.Max(min, Mathf.Max(range.x, range.y));
        return new Vector2(min, max);
    }

    private static float NextRange(ref uint state, Vector2 range)
    {
        return Mathf.Lerp(range.x, range.y, Next01(ref state));
    }

    private static float Next01(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return (state & 0xFFFFFFu) / (float)0x1000000;
    }

    #endregion
}
