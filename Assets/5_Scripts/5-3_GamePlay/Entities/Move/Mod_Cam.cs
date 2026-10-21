using Cinemachine;
using FlatWorld.Audio;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 相机跟随管理器
/// </summary>
public class Mod_Cam : Module
{
    private const float DefaultPovValue = 10f;

    [System.Serializable]
    private class CameraFollowSaveData
    {
        public float PovValue = DefaultPovValue;
        public bool UnlimitedViewEnabled;
        public float AdminFiniteViewMaxOverride;
    }

    #region 字段声明
    public enum FollowMode { PlayerSettings, PresentationLocked }

    // 模板读取早于 Awake，能力 ID 必须在装配前确定。
    public override string CanonicalModuleId => ModText.Camera;

    [Header("模块数据")]
    public Ex_ModData ModData;
    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = (Ex_ModData)value;
    }

    [Header("相机组件")]
    public CinemachineVirtualCamera vcam;
    public Camera ControllerCamera;

    [Header("跟随目标")]
    public Item CameraFollowItem;
    public Player Player;
    public Mod_GameController GameController;
    private WrappedWorldCameraRenderer _wrappedWorldRenderer;
    private CinemachineFramingTransposer _framingTransposer;
    private CinemachineBrain _cameraBrain;
    private CameraFollowRotation _rotationFollower;
    private float _baseXDamping;
    private float _baseYDamping;
    private bool _cameraFollowDefaultsCaptured;
    private Transform _followOverride;
    private float _followOverrideMaxPov;
    private float _followOverrideZoomMultiplier = 1f;
    private FollowMode _followOverrideMode;
    private CinemachineBrain.UpdateMethod _savedCameraUpdateMethod;
    private CinemachineBrain.BrainUpdateMethod _savedCameraBlendUpdateMethod;
    private bool _cameraUpdateOverrideApplied;

    public GameObject CamPrefab;
    private GameObject instantiatedCamera;
    [SerializeField]
    private float povValue = DefaultPovValue;

    /// <summary>
    /// 当前目标视野尺寸；观察模式保留逻辑尺寸，其余模式读取实际镜头。
    /// </summary>
    public float CurrentOrthographicSize => GetZoomProfile().MaximumRenderSize > 0f
        ? povValue
        : (vcam != null ? vcam.m_Lens.OrthographicSize : (ControllerCamera != null ? ControllerCamera.orthographicSize : 0f));
    
    [Header("视野限制")]
    public float MaxPovValue = 40f; // 视野最大拉伸值
    public float MinPovValue = 1f;  // 视野最小缩放值，保持正交相机尺寸大于零
    private bool _unlimitedViewEnabled; // 管理员无限视野只放开运行时上限，不污染普通视野配置。
    private float _adminFiniteViewMaxOverride; // GM 可临时扩展有限视野，不改普通玩法上限。

    public bool HasFollowOverride => _followOverride != null;
    private bool UsesPresentationLockedFollow => HasFollowOverride && _followOverrideMode == FollowMode.PresentationLocked;
    public float EffectiveMaxPovValue => Mathf.Max(
        Mathf.Max(MaxPovValue, HasFollowOverride ? _followOverrideMaxPov : 0f),
        GetZoomProfile().MaximumSize);
    public bool UsesProportionalZoom
    {
        get
        {
            if (HasFollowOverride) return _followOverrideZoomMultiplier > 1f;
            CameraZoomProfile profile = GetZoomProfile();
            return profile.Enabled && profile.ZoomMultiplier > 1f &&
                CurrentOrthographicSize >= profile.ProportionalStartSize;
        }
    }

    /// <summary>当前是否启用了管理员无限视野。</summary>
    public bool IsUnlimitedViewEnabled => _unlimitedViewEnabled;

    /// <summary>相机模块是否已完成虚拟相机初始化，供外部系统判断可用状态而不暴露 Cinemachine 类型依赖。</summary>
    public bool IsCameraReady => vcam != null;

    public bool FollowTargetRotation { get; private set; } = true;

    public void SetFollowTargetRotation(bool enabled)
    {
        FollowTargetRotation = enabled;
        if (_rotationFollower != null) _rotationFollower.FollowTargetRotation = enabled;
    }

    // 星区浮点重定位同时移动镜头缓存，保持屏幕上的位置和跟随惯性。
    public void ShiftSpacePresentation(Vector2 shift)
    {
        if (vcam != null && vcam.Follow != null) vcam.OnTargetObjectWarped(vcam.Follow, shift);
        if (ControllerCamera != null) ControllerCamera.transform.position += (Vector3)shift;
    }

    /// <summary>
    /// 获取虚拟相机组件
    /// </summary>
    public CinemachineVirtualCamera Vcam
    {
        get
        {
            return vcam;
        }
        set => vcam = value;
    }
    #endregion

    #region 生命周期方法
    private void OnEnable()
    {
        CameraUserSettings.Changed -= HandleCameraSettingsChanged;
        CameraUserSettings.Changed += HandleCameraSettingsChanged;
        BindWorldAudioListener();
        // 停用期间可能错过设置通知，恢复时只刷新现有镜头，不重建驾驶跟随租约。
        if (IsRuntimeLoaded && Vcam != null) ApplyCameraFollowSettings();
    }

    private void OnDisable()
    {
        CameraUserSettings.Changed -= HandleCameraSettingsChanged;
        ClearWorldAudioListener();
    }

    private void LateUpdate()
    {
        // 临时目标被外部销毁时释放租约，避免镜头继续保留驾驶更新方式。
        if (!ReferenceEquals(_followOverride, null) && _followOverride == null)
            ClearFollowOverride(_followOverride);
    }

    // 在Load方法中实例化相机逻辑
    protected override void OnLoad()
    {
        CameraUserSettings.Changed -= HandleCameraSettingsChanged;
        CameraUserSettings.Changed += HandleCameraSettingsChanged;
        // 获取GameController并绑定输入事件
        GameController = GetComponentInParent<Mod_GameController>();
        if (GameController != null && GameController._inputActions != null)
        {
            // 注意：Win10Actions是结构体，不能与null比较，直接绑定事件
            GameController._inputActions.Win10.CtrlMouse.performed += PovValueChanged;
        }
    
        // 获取跟随对象
        CameraFollowItem = GetComponentInParent<Item>();
        Player = CameraFollowItem as Player;
    
        // 直接在当前位置实例化相机预制体
        if (CamPrefab != null)
        {
            instantiatedCamera = Instantiate(CamPrefab);
            // 相机由玩家模块释放，跨星区卸载源场景时保留同一实例。
            DontDestroyOnLoad(instantiatedCamera);
            Debug.Log("相机预制体已实例化");
        }
        else
        {
            Debug.LogError("CamPrefab未设置，请在Inspector中指定相机预制体");
        }
    
        // 从实例化的预制体中获取相机组件
        if (instantiatedCamera != null)
        {
            ControllerCamera = instantiatedCamera.GetComponentInChildren<Camera>();
            vcam = instantiatedCamera.GetComponentInChildren<CinemachineVirtualCamera>();
            if (vcam != null)
            {
                _rotationFollower = vcam.GetComponent<CameraFollowRotation>();
                if (_rotationFollower == null) _rotationFollower = vcam.gameObject.AddComponent<CameraFollowRotation>();
                _rotationFollower.FollowTargetRotation = FollowTargetRotation;
            }
        }
    
        // 初始化虚拟相机跟随目标
        if (Vcam != null && CameraFollowItem != null)
        {
            Vcam.Follow = CameraFollowItem.transform;
        }

        if (ControllerCamera != null)
        {
            _wrappedWorldRenderer = ControllerCamera.GetComponent<WrappedWorldCameraRenderer>();
            if (_wrappedWorldRenderer == null)
                _wrappedWorldRenderer = ControllerCamera.gameObject.AddComponent<WrappedWorldCameraRenderer>();
            _wrappedWorldRenderer.Configure(ControllerCamera, Vcam, CameraFollowItem?.transform);
        }
        ApplyFollowTarget();
    
        // 初始化相机视野（正交大小）
        if (Vcam != null)
        {
            CacheCameraFollowDefaults();
            LoadPovValue();
            SetOrthographicSize(povValue);
        }
        GameController._mainCamera = ControllerCamera;
        BindWorldAudioListener();
    
        // 重置旋转
        transform.rotation = Quaternion.identity;
    }

    protected override void OnSave()
    {
        SavePovValue();
    }
    
    protected override void OnUnload() => ReleaseCameraRuntime();

    private void OnDestroy() => ReleaseCameraRuntime();

    // 模块卸载与销毁共用清理，停用后重载不遗留持久相机或输入订阅。
    private void ReleaseCameraRuntime()
    {
        CameraUserSettings.Changed -= HandleCameraSettingsChanged;
        ClearWorldAudioListener();
        RestoreCameraUpdateMode();

        // 注销事件
        if (GameController != null && GameController._inputActions != null)
        {
            GameController._inputActions.Win10.CtrlMouse.performed -= PovValueChanged;
        }
        if (GameController != null && GameController._mainCamera == ControllerCamera)
            GameController._mainCamera = null;
        
        // 销毁实例化的相机预制体
        if (instantiatedCamera != null)
        {
            instantiatedCamera.SetActive(false);
            Destroy(instantiatedCamera);
        }
        instantiatedCamera = null;
        ControllerCamera = null;
        vcam = null;
        _wrappedWorldRenderer = null;
        _framingTransposer = null;
        _cameraBrain = null;
        _rotationFollower = null;
        _cameraFollowDefaultsCaptured = false;
        _followOverride = null;
        _followOverrideMaxPov = 0f;
        _followOverrideZoomMultiplier = 1f;
        _followOverrideMode = FollowMode.PlayerSettings;
    }
    #endregion

    #region 玩家听音位置

    // 本机玩家拥有世界听音中心，相机只提供屏幕左右方向和音频输出监听器。
    private void BindWorldAudioListener()
    {
        if (Player == null || !Player.IsLocalProfile || ControllerCamera == null)
            return;

        AudioListener listener = ControllerCamera.GetComponent<AudioListener>();
        if (listener != null)
            AudioService.Instance.SetWorldListener(Player.transform, listener);
    }

    private void ClearWorldAudioListener()
    {
        if (Player != null && AudioService.HasInstance)
            AudioService.Instance.ClearWorldListener(Player.transform);
    }

    #endregion

    #region 镜头预判设置

    /// <summary>缓存相机 Prefab 的基础阻尼，避免切换正负前探值时丢失原始跟随手感。</summary>
    private void CacheCameraFollowDefaults()
    {
        if (Vcam == null)
            return;

        _framingTransposer ??= Vcam.GetCinemachineComponent<CinemachineFramingTransposer>();
        if (_framingTransposer == null || _cameraFollowDefaultsCaptured)
            return;

        _baseXDamping = _framingTransposer.m_XDamping;
        _baseYDamping = _framingTransposer.m_YDamping;
        _cameraFollowDefaultsCaptured = true;
    }

    /// <summary>即时应用玩家选择的正值前探、负值惯性和预判平滑。</summary>
    public void ApplyCameraFollowSettings()
    {
        if (Vcam == null)
            return;

        ApplyCameraUpdateMode();
        CacheCameraFollowDefaults();
        if (_framingTransposer == null || !_cameraFollowDefaultsCaptured)
            return;

        bool locked = UsesPresentationLockedFollow;
        float lookahead = locked ? 0f : GetZoomAdjustedLookahead();
        float inertia = Mathf.Max(0f, -lookahead);
        _framingTransposer.m_LookaheadTime = Mathf.Max(0f, lookahead);
        _framingTransposer.m_LookaheadSmoothing = locked ? 0f : CameraUserSettings.LookaheadSmoothing;
        _framingTransposer.m_XDamping = locked ? 0f : Mathf.Clamp(_baseXDamping + inertia, 0f, 20f);
        _framingTransposer.m_YDamping = locked ? 0f : Mathf.Clamp(_baseYDamping + inertia, 0f, 20f);
    }

    private void ApplyCameraUpdateMode()
    {
        if (_cameraBrain == null && ControllerCamera != null)
            _cameraBrain = ControllerCamera.GetComponent<CinemachineBrain>();
        if (_cameraBrain == null) return;
        if (!UsesPresentationLockedFollow)
        {
            RestoreCameraUpdateMode();
            return;
        }
        if (!_cameraUpdateOverrideApplied)
        {
            _savedCameraUpdateMethod = _cameraBrain.m_UpdateMethod;
            _savedCameraBlendUpdateMethod = _cameraBrain.m_BlendUpdateMethod;
            _cameraUpdateOverrideApplied = true;
        }
        // 已插值目标只在表现完成后的 LateUpdate 跟随，不再让镜头选择物理时钟。
        _cameraBrain.m_UpdateMethod = CinemachineBrain.UpdateMethod.LateUpdate;
        _cameraBrain.m_BlendUpdateMethod = CinemachineBrain.BrainUpdateMethod.LateUpdate;
    }

    private void RestoreCameraUpdateMode()
    {
        if (_cameraUpdateOverrideApplied && _cameraBrain != null)
        {
            _cameraBrain.m_UpdateMethod = _savedCameraUpdateMethod;
            _cameraBrain.m_BlendUpdateMethod = _savedCameraBlendUpdateMethod;
        }
        _cameraUpdateOverrideApplied = false;
    }

    /// <summary>按当前拉远进度应用缩放影响系数，并保证负系数不会反转前探方向。</summary>
    private float GetZoomAdjustedLookahead()
    {
        // 镜头预判的缩放影响始终以正常玩法视野范围计算；无限视野只是管理员运行时权限。
        float maximumPov = Mathf.Max(DefaultPovValue + 0.001f, MaxPovValue);
        float zoomOutProgress = Mathf.InverseLerp(
            DefaultPovValue,
            maximumPov,
            CurrentOrthographicSize);
        float multiplier = Mathf.Max(
            0f,
            1f + CameraUserSettings.LookaheadZoomInfluence * zoomOutProgress);
        return CameraUserSettings.Lookahead * multiplier;
    }

    private void HandleCameraSettingsChanged()
    {
        ApplyCameraFollowSettings();
    }

    #endregion

    #region 相机控制
    // 临时驾驶等模式借用跟随目标，结束后回到玩家且不改变听音主体。
    public void SetFollowOverride(Transform target, float maximumPov = 0f, float zoomMultiplier = 1f,
        FollowMode followMode = FollowMode.PlayerSettings)
    {
        float maximum = target != null ? Mathf.Max(0f, maximumPov) : 0f;
        float multiplier = target != null ? Mathf.Max(1f, zoomMultiplier) : 1f;
        FollowMode mode = target != null ? followMode : FollowMode.PlayerSettings;
        if (_followOverride == target && _followOverrideMaxPov == maximum &&
            _followOverrideZoomMultiplier == multiplier && _followOverrideMode == mode) return;
        bool targetChanged = _followOverride != target;
        _followOverride = target;
        _followOverrideMaxPov = maximum;
        _followOverrideZoomMultiplier = multiplier;
        _followOverrideMode = mode;
        if (targetChanged) ApplyFollowTarget();
        if (Vcam != null) SetOrthographicSize(povValue);
    }

    public void ClearFollowOverride(Transform target)
    {
        if (_followOverride != target) return;
        _followOverride = null;
        _followOverrideMaxPov = 0f;
        _followOverrideZoomMultiplier = 1f;
        _followOverrideMode = FollowMode.PlayerSettings;
        ApplyFollowTarget();
        if (Vcam != null) SetOrthographicSize(povValue);
    }

    private void ApplyFollowTarget()
    {
        Transform target = _followOverride != null ? _followOverride : CameraFollowItem?.transform;
        if (Vcam != null)
        {
            Vcam.Follow = target;
        }
        if (_rotationFollower != null) _rotationFollower.FollowTarget = target;
        if (_wrappedWorldRenderer != null)
            _wrappedWorldRenderer.Configure(ControllerCamera, Vcam, target);
    }

    /// <summary>
    /// 响应滚轮值改变视野
    /// </summary>
    /// <param name="context"></param>
    public void PovValueChanged(InputAction.CallbackContext context)
    {
        if (GameController != null &&
            (GameController.IsGameplayInputLocked || GameController.IsDropShortcutHeld))
        {
            return;
        }

        Vector2 scrollValue = context.ReadValue<Vector2>();
        if (scrollValue.y > 0)
            ChangeCameraView(-1); // 缩小视野
        else if (scrollValue.y < 0)
            ChangeCameraView(1);  // 放大视野
    }

    /// <summary>
    /// 修改相机视野范围
    /// </summary>
    /// <param name="delta">视野变化值</param>
    public void ChangeCameraView(float delta)
    {
        if (Vcam == null || float.IsNaN(delta) || float.IsInfinity(delta)) return;
        if (HasFollowOverride)
        {
            SetOrthographicSize(_followOverrideZoomMultiplier > 1f
                ? CurrentOrthographicSize * Mathf.Pow(_followOverrideZoomMultiplier, delta)
                : CurrentOrthographicSize + delta);
            return;
        }
        CameraZoomProfile profile = GetZoomProfile();
        SetInputOrthographicSize(profile.Enabled
            ? profile.Step(CurrentOrthographicSize, delta)
            : CurrentOrthographicSize + delta);
    }

    // 滚轮、双指与滑条只提交本次允许的尺寸，过渡期间多余的输入不积攒。
    private void SetInputOrthographicSize(float value)
        => SetOrthographicSize(CameraZoomExtensions.ClampInputSize(this, CurrentOrthographicSize, value));

    /// <summary>设置相机正交视野；区块加载窗口由区块配置独立控制。</summary>
    public void SetOrthographicSize(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            throw new System.ArgumentOutOfRangeException(nameof(value), "镜头视野必须是有限数值。");
        if (Vcam == null)
            throw new System.InvalidOperationException("Camera module has no Cinemachine virtual camera.");
        povValue = ClampPovValue(value);
        CameraZoomProfile profile = GetZoomProfile();
        float renderSize = profile.MaximumRenderSize > 0f
            ? Mathf.Min(povValue, profile.MaximumRenderSize)
            : povValue;
        Vcam.m_Lens.OrthographicSize = renderSize;
        if (ControllerCamera != null)
            ControllerCamera.orthographicSize = renderSize;
        ApplyCameraFollowSettings();
    }

    // 手机滑条固定使用同一归一化坐标，近景到宇宙视野不切换滑条范围。
    public float GetViewSliderValue()
    {
        float minimum = Mathf.Max(.001f, MinPovValue);
        float maximum = Mathf.Max(minimum + .001f, EffectiveMaxPovValue);
        float size = Mathf.Clamp(CurrentOrthographicSize, minimum, maximum);
        CameraZoomProfile profile = GetZoomProfile();
        if (profile.Enabled) return profile.ToSliderValue(size, minimum, maximum);
        return HasFollowOverride && _followOverrideZoomMultiplier > 1f
            ? Mathf.Log(size / minimum) / Mathf.Log(maximum / minimum)
            : Mathf.InverseLerp(minimum, maximum, size);
    }

    public void SetViewSliderValue(float normalized)
    {
        if (float.IsNaN(normalized) || float.IsInfinity(normalized))
            throw new System.ArgumentOutOfRangeException(nameof(normalized), "镜头滑条值必须是有限数值。");
        float minimum = Mathf.Max(.001f, MinPovValue);
        float maximum = Mathf.Max(minimum + .001f, EffectiveMaxPovValue);
        float value = Mathf.Clamp01(normalized);
        CameraZoomProfile profile = GetZoomProfile();
        SetInputOrthographicSize(profile.Enabled
            ? profile.FromSliderValue(value, minimum, maximum)
            : (HasFollowOverride && _followOverrideZoomMultiplier > 1f
                ? minimum * Mathf.Pow(maximum / minimum, value)
                : Mathf.Lerp(minimum, maximum, value)));
    }

    /// <summary>按双指间距变化调整正交视野；两指分开时镜头拉近，合拢时镜头拉远。</summary>
    public void ApplyPinchZoom(float screenDistanceDelta, float sensitivity)
    {
        if (Vcam == null || GameController?.IsGameplayInputLocked == true)
            return;

        float safeSensitivity = Mathf.Max(0f, sensitivity);
        if (safeSensitivity <= 0f || Mathf.Abs(screenDistanceDelta) <= 0.001f)
            return;

        ChangeCameraView(-screenDistanceDelta * safeSensitivity);
    }

    /// <summary>原子设置 GM 视野上限模式和正交尺寸，有限档可独立扩展普通玩法上限。</summary>
    public void SetViewLimitAndSize(float value, bool unlimited, float finiteMaxOverride)
    {
        _unlimitedViewEnabled = unlimited;
        _adminFiniteViewMaxOverride = unlimited
            ? 0f
            : Mathf.Max(MaxPovValue, finiteMaxOverride);
        SetOrthographicSize(value);
    }

    /// <summary>按普通玩法上限或管理员无限权限约束镜头正交尺寸。</summary>
    private float ClampPovValue(float value)
    {
        float finiteMax = _adminFiniteViewMaxOverride > 0f
            ? Mathf.Max(_adminFiniteViewMaxOverride, EffectiveMaxPovValue)
            : EffectiveMaxPovValue;
        return Mathf.Max(MinPovValue, _unlimitedViewEnabled ? value : Mathf.Min(value, finiteMax));
    }

    private CameraZoomProfile GetZoomProfile()
        => HasFollowOverride ? default : CameraZoomExtensions.GetProfile(this);
    #endregion

    private void LoadPovValue()
    {
        if (ModData != null)
        {
            var saved = ModData.GetData<CameraFollowSaveData>();
            if (saved != null)
            {
                povValue = saved.PovValue;
                _unlimitedViewEnabled = saved.UnlimitedViewEnabled;
                _adminFiniteViewMaxOverride = saved.AdminFiniteViewMaxOverride;
                return;
            }
        }

        if (Player != null && Player.Data != null)
        {
            povValue = Player.Data.PlayerPov;
        }
    }

    private void SavePovValue()
    {
        if (ModData == null) return;
        ModData.WriteData(new CameraFollowSaveData
        {
            PovValue = povValue,
            UnlimitedViewEnabled = _unlimitedViewEnabled,
            AdminFiniteViewMaxOverride = _adminFiniteViewMaxOverride
        });
    }
}
