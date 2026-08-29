using System;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.InputSystem;

public partial class WeatherMgr : SingletonAutoMono<WeatherMgr>
{
    private bool _weatherRuntimeAllowed;

#region 字段

    public const float DefaultWeatherTemperatureOffset = 0f; // 默认天气温度修正
    private const string RainEffectResourcePath = "Weather/RainEffect"; // 雨效资源路径
    private const string RainGroundSplashResourcePath = "Weather/RainGroundSplash"; // 地面水花资源路径
    private const int DebugWindowId = 981237; // 调试窗口ID
    private const float TitleBarHeight = 28f; // 标题栏高度
    private const float ResizeHandleSize = 18f; // 右下角缩放手柄尺寸

    public bool EnableDebugLog = false; // 是否输出天气处理调试日志
    [SerializeField] private Key _toggleDebugPanelInputKey = Key.F12; // 天气调试面板快捷键
    [SerializeField] private bool _debugPanelVisible = false; // 天气调试面板显示状态
    [SerializeField] private Rect _debugWindowRect = new Rect(20f, 220f, 380f, 280f); // 调试面板位置和尺寸
    [SerializeField] private Vector2 _debugWindowMinSize = new Vector2(320f, 240f); // 调试面板最小尺寸
    [SerializeField] private Vector2 _debugWindowMaxSize = new Vector2(760f, 640f); // 调试面板最大尺寸
    [SerializeField] private bool _lockWindowInsideScreen = true; // 是否限制窗口不出屏

    private GUIStyle _windowStyle; // 窗口样式
    private GUIStyle _titleStyle; // 标题样式
    private GUIStyle _labelStyle; // 文本样式
    private GUIStyle _valueStyle; // 数值样式
    private GUIStyle _buttonStyle; // 按钮样式
    private GUIStyle _footerStyle; // 底部提示样式
    private bool _isResizing; // 是否正在缩放
    private Vector2 _resizeStartMousePosition; // 缩放起始鼠标位置
    private Rect _resizeStartRect; // 缩放起始窗口矩形
    private Vector2 _scrollPosition; // 内容滚动位置
    private GameObject _rainEffectInstance; // 雨效实例
    private RainEffectController _rainEffectController; // 雨效控制器
    private bool _rainEffectLoadFailed; // 雨效加载失败标记
    private GameObject _rainGroundSplashInstance; // 独立地面水花实例
    private RainGroundSplashController _rainGroundSplashController; // 地面水花控制器
    private bool _rainGroundSplashLoadFailed; // 地面水花加载失败标记

#endregion

#region 属性

    [ShowInInspector, ReadOnly, LabelText("当前天气")]
    public WeatherType CurrentWeather => GetCurrentWeather(); // 当前天气类型

    [ShowInInspector, ReadOnly, LabelText("天气强度")]
    public float CurrentWeatherIntensity => GetCurrentWeatherIntensity(); // 当前天气强度

    [ShowInInspector, ReadOnly, LabelText("天气阶段")]
    public WeatherPhase CurrentWeatherPhase => GetCurrentWeatherPhase(); // 当前天气事件阶段

    [ShowInInspector, ReadOnly, LabelText("阶段剩余时间")]
    public float CurrentWeatherRemainingTime => GetCurrentWeatherRemainingTime(); // 当前阶段或晴朗间隔剩余时间

    [ShowInInspector, ReadOnly, LabelText("天气修正(℃)")]
    public float CurrentWeatherTemperatureOffset => GetWeatherTemperatureOffset(); // 当前天气对环境温度的修正

#endregion

#region 公共方法

    [Button("切换为晴天")]
    public void SetClearWeatherDebug()
    {
        ClearWeather();
    }

    [Button("切换为雨天")]
    public void SetRainWeatherDebug()
    {
        SetRain();
    }

    public void NormalizeData(PlanetData planetData)
    {
        if (planetData == null)
        {
            throw new ArgumentNullException(nameof(planetData));
        }

        planetData.WeatherIntensity = Mathf.Clamp01(planetData.WeatherIntensity);
        planetData.WindStrength = Mathf.Clamp01(planetData.WindStrength);
        planetData.RainTemperatureOffset = Mathf.Min(0f, planetData.RainTemperatureOffset);
        planetData.CloudyTemperatureOffset = Mathf.Min(0f, planetData.CloudyTemperatureOffset);
        planetData.StormTemperatureOffset = Mathf.Min(planetData.RainTemperatureOffset, planetData.StormTemperatureOffset);
    }

    public WeatherType GetCurrentWeather()
    {
        if (!_weatherRuntimeAllowed)
            return WeatherType.Clear;

        PlanetData planetData = GetActivePlanetData();
        return planetData != null ? planetData.CurrentWeather : WeatherType.Clear;
    }

    public float GetCurrentWeatherIntensity()
    {
        if (!_weatherRuntimeAllowed)
            return 0f;

        PlanetData planetData = GetActivePlanetData();
        return planetData != null ? Mathf.Clamp01(planetData.WeatherIntensity) : 0f;
    }

    public float GetWeatherTemperatureOffset()
    {
        if (!_weatherRuntimeAllowed)
            return DefaultWeatherTemperatureOffset;

        return CalculateWeatherTemperatureOffset(GetActivePlanetData());
    }

    public static bool IsWeatherSuppressedInDimension(DimensionDefinition definition)
    {
        return definition?.SuppressWeather == true;
    }

    public void SetWeather(WeatherType weatherType, float intensity = 1f)
    {
        SetAuthoritativeWeather(weatherType, intensity);
    }

    public void SetRain(float intensity = 1f)
    {
        SetWeather(WeatherType.Rain, intensity);
    }

    public void ClearWeather()
    {
        SetWeather(WeatherType.Clear, 0f);
    }

    public bool IsRaining()
    {
        WeatherType weather = GetCurrentWeather();
        return (weather == WeatherType.Rain || weather == WeatherType.Storm) &&
               GetCurrentWeatherIntensity() > 0f;
    }

    public void RefreshRainEffect()
    {
        if (!IsRaining())
        {
            if (_rainEffectInstance != null)
            {
                _rainEffectInstance.SetActive(false);
            }

            SetRainGroundSplashActive(false, 0f);

            return;
        }

        GameObject rainEffectInstance = EnsureRainEffectInstance();
        if (rainEffectInstance == null)
        {
            return;
        }

        rainEffectInstance.SetActive(true);
        SyncRainEffectTransform(rainEffectInstance.transform);

        if (_rainEffectController != null)
        {
            _rainEffectController.ApplySettings(GetCurrentWeatherIntensity());
        }

        SetRainGroundSplashActive(true, GetCurrentWeatherIntensity());
    }

    public void ToggleDebugPanel()
    {
        _debugPanelVisible = !_debugPanelVisible;

        if (EnableDebugLog)
        {
            Debug.Log($"[WeatherMgr] 天气调试面板已{(_debugPanelVisible ? "打开" : "关闭")}");
        }
    }

    public PlanetData GetActivePlanetData()
    {
        return SaveDataMgr.Instance != null ? SaveDataMgr.Instance.Active_PlanetData : null;
    }

    private GameObject EnsureRainEffectInstance()
    {
        if (_rainEffectInstance != null)
        {
            return _rainEffectInstance;
        }

        if (_rainEffectLoadFailed)
        {
            return null;
        }

        GameObject rainEffectPrefab = Resources.Load<GameObject>(RainEffectResourcePath);
        if (rainEffectPrefab == null)
        {
            _rainEffectLoadFailed = true;
            Debug.LogError($"[WeatherMgr] 未找到雨效 prefab，路径={RainEffectResourcePath}");
            return null;
        }

        _rainEffectInstance = Instantiate(rainEffectPrefab, transform);
        _rainEffectInstance.name = rainEffectPrefab.name;
        _rainEffectController = _rainEffectInstance.GetComponent<RainEffectController>();

        if (_rainEffectController == null)
        {
            _rainEffectLoadFailed = true;
            Debug.LogError($"[WeatherMgr] 雨效 prefab 缺少 RainEffectController，路径={RainEffectResourcePath}");
            Destroy(_rainEffectInstance);
            _rainEffectInstance = null;
            return null;
        }

        return _rainEffectInstance;
    }

    /// <summary>启停独立地面水花层，不修改原雨层 Prefab 与粒子参数。</summary>
    private void SetRainGroundSplashActive(bool active, float intensity)
    {
        if (!active)
        {
            if (_rainGroundSplashInstance != null)
                _rainGroundSplashInstance.SetActive(false);
            return;
        }

        GameObject splashInstance = EnsureRainGroundSplashInstance();
        if (splashInstance == null)
            return;

        splashInstance.SetActive(true);
        _rainGroundSplashController?.ApplySettings(intensity);
    }

    /// <summary>按需加载一次地面水花 Prefab；失败不会影响原雨层的显示。</summary>
    private GameObject EnsureRainGroundSplashInstance()
    {
        if (_rainGroundSplashInstance != null)
            return _rainGroundSplashInstance;

        if (_rainGroundSplashLoadFailed)
            return null;

        GameObject splashPrefab = Resources.Load<GameObject>(RainGroundSplashResourcePath);
        if (splashPrefab == null)
        {
            _rainGroundSplashLoadFailed = true;
            Debug.LogWarning($"[WeatherMgr] 未找到地面水花 prefab，路径={RainGroundSplashResourcePath}");
            return null;
        }

        _rainGroundSplashInstance = Instantiate(splashPrefab, transform);
        _rainGroundSplashInstance.name = splashPrefab.name;
        _rainGroundSplashController = _rainGroundSplashInstance.GetComponent<RainGroundSplashController>();
        if (_rainGroundSplashController != null)
            return _rainGroundSplashInstance;

        _rainGroundSplashLoadFailed = true;
        Debug.LogError($"[WeatherMgr] 地面水花 prefab 缺少 RainGroundSplashController，路径={RainGroundSplashResourcePath}");
        Destroy(_rainGroundSplashInstance);
        _rainGroundSplashInstance = null;
        return null;
    }

    private void SyncRainEffectTransform(Transform rainEffectTransform)
    {
        if (rainEffectTransform == null)
        {
            return;
        }

        // RainEffectController 会在 LateUpdate 中依据相机顶部完成精确定位；
        // 这里再写一次中心点会与其相互覆盖，造成发射区域在场景中漂移。
        if (_rainEffectController != null && rainEffectTransform == _rainEffectController.transform)
        {
            return;
        }

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            rainEffectTransform.position = new Vector3(mainCamera.transform.position.x, mainCamera.transform.position.y, 0f);
            rainEffectTransform.rotation = Quaternion.identity;
            return;
        }

        rainEffectTransform.position = transform.position;
        rainEffectTransform.rotation = Quaternion.identity;
    }

#endregion

#region 生命周期

    private void Start()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.Event_GameWorldEnter += OnGameWorldEnter;
            GameManager.Instance.Event_GameWorldExit += OnGameWorldExit;
        }

        bool isInGameWorld = GameManager.Instance != null && GameManager.Instance.IsInGameWorld;
        ApplyGameWorldLifecycleState(isInGameWorld);
    }

    protected override void OnDestroy()
    {
        ShutdownWeatherEventSystem();
        if (GameManager.Instance != null)
        {
            GameManager.Instance.Event_GameWorldEnter -= OnGameWorldEnter;
            GameManager.Instance.Event_GameWorldExit -= OnGameWorldExit;
        }
    }

    private void OnGameWorldEnter()
    {
        ApplyGameWorldLifecycleState(true);
    }

    private void OnGameWorldExit()
    {
        ApplyGameWorldLifecycleState(false);
    }

    private void ApplyGameWorldLifecycleState(bool isActive)
    {
        _weatherRuntimeAllowed = isActive &&
                                 !IsWeatherSuppressedInDimension(DimensionManager.Instance.ActiveDefinition);
        enabled = _weatherRuntimeAllowed;

        if (!_weatherRuntimeAllowed)
        {
            ShutdownWeatherEventSystem();
            _debugPanelVisible = false;
            return;
        }

        ActivateWeatherEventSystem();
        RefreshWeatherFeedback();
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard[_toggleDebugPanelInputKey].wasPressedThisFrame)
        {
            ToggleDebugPanel();
        }

        MaintainWeatherEventSystem();
        if (_rainEffectInstance != null && _rainEffectInstance.activeSelf)
            SyncRainEffectTransform(_rainEffectInstance.transform);
    }

    private void OnGUI()
    {
        if (!_debugPanelVisible)
        {
            return;
        }

        EnsureGuiStyles();
        _debugWindowRect = GUI.Window(DebugWindowId, _debugWindowRect, DrawDebugWindow, GUIContent.none, _windowStyle);

        if (_lockWindowInsideScreen)
        {
            _debugWindowRect.x = Mathf.Clamp(_debugWindowRect.x, 0f, Mathf.Max(0f, Screen.width - _debugWindowRect.width));
            _debugWindowRect.y = Mathf.Clamp(_debugWindowRect.y, 0f, Mathf.Max(0f, Screen.height - _debugWindowRect.height));
        }
    }

    private void DrawDebugWindow(int windowId)
    {
        Rect titleBarRect = new Rect(0f, 0f, _debugWindowRect.width, TitleBarHeight);
        GUI.Label(titleBarRect, $"天气调试面板  [{_toggleDebugPanelInputKey}] 切换", _titleStyle);

        Rect closeButtonRect = new Rect(_debugWindowRect.width - 26f, 4f, 22f, 20f);
        if (GUI.Button(closeButtonRect, "×", _buttonStyle))
        {
            _debugPanelVisible = false;
        }

        Rect contentRect = new Rect(12f, TitleBarHeight + 8f, _debugWindowRect.width - 24f, _debugWindowRect.height - TitleBarHeight - 36f);
        GUILayout.BeginArea(contentRect);
        _scrollPosition = GUILayout.BeginScrollView(_scrollPosition);

        GUILayout.Label($"当前天气: {CurrentWeather}", _labelStyle);
        GUILayout.Label($"天气阶段: {CurrentWeatherPhase}", _valueStyle);
        GUILayout.Label($"天气强度: {CurrentWeatherIntensity:F2}", _valueStyle);
        GUILayout.Label($"全局风力: {CurrentWindStrength:F2}", _valueStyle);
        GUILayout.Label($"阶段剩余: {CurrentWeatherRemainingTime:F1} 秒", _valueStyle);
        GUILayout.Label($"天气修正: {CurrentWeatherTemperatureOffset:F2} ℃", _valueStyle);

        PlanetData planetData = GetActivePlanetData();
        if (planetData != null)
        {
            GUILayout.Label($"基础温度: {planetData.GlobalTemperature:F2} ℃", _valueStyle);
            GUILayout.Label($"有效环境温度: {GetWeatherTemperatureOffset() + planetData.GlobalTemperature:F2} ℃", _valueStyle);
        }
        else
        {
            GUILayout.Label("当前没有可用的星球数据", _valueStyle);
        }

        GUILayout.Space(8f);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("晴天"))
        {
            ClearWeather();
        }

        if (GUILayout.Button("雨天"))
        {
            SetRain();
        }
        GUILayout.EndHorizontal();

        if (planetData != null)
        {
            GUILayout.Space(8f);
            GUILayout.Label("风力", _labelStyle);
            float nextWindStrength = GUILayout.HorizontalSlider(planetData.WindStrength, 0f, 1f);
            if (!Mathf.Approximately(nextWindStrength, planetData.WindStrength))
                SetWindStrength(nextWindStrength);

            GUILayout.Space(8f);
            GUILayout.Label("雨强度", _labelStyle);
            float nextIntensity = GUILayout.HorizontalSlider(planetData.WeatherIntensity, 0f, 1f);
            if (!Mathf.Approximately(nextIntensity, planetData.WeatherIntensity))
            {
                SetWeather(planetData.CurrentWeather, nextIntensity);
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("小雨 0.35", _buttonStyle))
            {
                SetWeather(WeatherType.Rain, 0.35f);
            }

            if (GUILayout.Button("中雨 0.65", _buttonStyle))
            {
                SetWeather(WeatherType.Rain, 0.65f);
            }

            if (GUILayout.Button("大雨 1.0", _buttonStyle))
            {
                SetWeather(WeatherType.Rain, 1f);
            }
            GUILayout.EndHorizontal();
        }

        GUILayout.Label("拖动标题栏移动，拖动右下角缩放", _footerStyle);
        GUILayout.EndScrollView();
        GUILayout.EndArea();

        HandleResize(titleBarRect);
        GUI.DragWindow(titleBarRect);
    }

    private void HandleResize(Rect titleBarRect)
    {
        Event currentEvent = Event.current;
        Rect resizeRect = new Rect(_debugWindowRect.width - ResizeHandleSize, _debugWindowRect.height - ResizeHandleSize, ResizeHandleSize, ResizeHandleSize);

        GUI.Box(resizeRect, GUIContent.none, _buttonStyle);

        if (currentEvent.type == EventType.MouseDown && resizeRect.Contains(currentEvent.mousePosition) && currentEvent.button == 0)
        {
            _isResizing = true;
            _resizeStartMousePosition = currentEvent.mousePosition;
            _resizeStartRect = _debugWindowRect;
            currentEvent.Use();
        }

        if (_isResizing && currentEvent.type == EventType.MouseDrag)
        {
            Vector2 delta = currentEvent.mousePosition - _resizeStartMousePosition;
            float nextWidth = Mathf.Clamp(_resizeStartRect.width + delta.x, _debugWindowMinSize.x, _debugWindowMaxSize.x);
            float nextHeight = Mathf.Clamp(_resizeStartRect.height + delta.y, _debugWindowMinSize.y, _debugWindowMaxSize.y);
            _debugWindowRect.width = nextWidth;
            _debugWindowRect.height = nextHeight;
            currentEvent.Use();
        }

        if (_isResizing && (currentEvent.type == EventType.MouseUp || currentEvent.type == EventType.Ignore))
        {
            _isResizing = false;
        }

        EditorLikeCursorHint(resizeRect);
    }

    private void EditorLikeCursorHint(Rect resizeRect)
    {
        if (resizeRect.Contains(Event.current.mousePosition))
        {
            GUI.Label(resizeRect, "↘", _footerStyle);
        }
    }

    private void EnsureGuiStyles()
    {
        if (_windowStyle != null)
        {
            return;
        }

        Texture2D windowBackgroundTexture = CreateSolidTexture(new Color(0.18f, 0.18f, 0.18f, 0.96f));
        Texture2D buttonTexture = CreateSolidTexture(new Color(0.28f, 0.28f, 0.28f, 1f));
        Texture2D buttonHoverTexture = CreateSolidTexture(new Color(0.34f, 0.34f, 0.34f, 1f));
        Texture2D buttonActiveTexture = CreateSolidTexture(new Color(0.24f, 0.24f, 0.24f, 1f));

        GUIStyleState windowBackground = new GUIStyleState
        {
            background = windowBackgroundTexture,
            textColor = new Color(0.92f, 0.94f, 0.96f, 1f)
        };

        _windowStyle = new GUIStyle(GUI.skin.window)
        {
            padding = new RectOffset(12, 12, 12, 12),
            border = new RectOffset(6, 6, 24, 6),
            normal = windowBackground,
            hover = windowBackground,
            active = windowBackground,
            onNormal = windowBackground,
            onHover = windowBackground,
            onActive = windowBackground
        };

        Color titleColor = new Color(0.95f, 0.97f, 0.99f, 1f);
        Color textColor = new Color(0.86f, 0.90f, 0.94f, 1f);
        Color softTextColor = new Color(0.70f, 0.75f, 0.80f, 1f);

        _titleStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleLeft,
            fontStyle = FontStyle.Bold,
            fontSize = 15,
            normal = new GUIStyleState { textColor = titleColor },
            margin = new RectOffset(4, 4, 0, 0)
        };

        _labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            normal = new GUIStyleState { textColor = textColor },
            margin = new RectOffset(0, 0, 2, 2)
        };

        _valueStyle = new GUIStyle(_labelStyle)
        {
            normal = new GUIStyleState { textColor = softTextColor }
        };

        _buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 12,
            fixedHeight = 28f,
            margin = new RectOffset(2, 2, 2, 2),
            padding = new RectOffset(8, 8, 4, 4),
            normal = new GUIStyleState { textColor = textColor, background = buttonTexture },
            hover = new GUIStyleState { textColor = titleColor, background = buttonHoverTexture },
            active = new GUIStyleState { textColor = titleColor, background = buttonActiveTexture },
            focused = new GUIStyleState { textColor = textColor, background = buttonTexture }
        };

        _footerStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 11,
            normal = new GUIStyleState { textColor = softTextColor },
            margin = new RectOffset(0, 0, 4, 0)
        };
    }

    private static Texture2D CreateSolidTexture(Color color)
    {
        Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.HideAndDontSave
        };

        texture.SetPixel(0, 0, color);
        texture.Apply();
        return texture;
    }

#endregion
}
