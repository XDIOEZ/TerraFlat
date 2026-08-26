using Cinemachine;
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
    }

    #region 字段声明
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
    public GameController GameController;
    private Mod_ChunkLoader _chunkLoader;
    private WrappedWorldCameraRenderer _wrappedWorldRenderer;
    private CinemachineFramingTransposer _framingTransposer;
    private float _baseXDamping;
    private float _baseYDamping;
    private bool _cameraFollowDefaultsCaptured;

    public GameObject CamPrefab;
    private GameObject instantiatedCamera;
    [SerializeField]
    private float povValue = DefaultPovValue;

    /// <summary>
    /// 当前目标正交尺寸（直接读取 vcam lens，避免 Cinemachine 同步延迟）
    /// </summary>
    public float CurrentOrthographicSize => vcam != null ? vcam.m_Lens.OrthographicSize : (ControllerCamera != null ? ControllerCamera.orthographicSize : 0f);
    
    [Header("视野限制")]
    public float MaxPovValue = 20f; // 视野最大拉伸值
    public float MinPovValue = 5f;  // 视野最小缩放值

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
    public new void Awake()
    {
         _Data.ID = ModText.Camera;
    }

    private void OnEnable()
    {
        CameraUserSettings.Changed -= HandleCameraSettingsChanged;
        CameraUserSettings.Changed += HandleCameraSettingsChanged;
    }

    private void OnDisable()
    {
        CameraUserSettings.Changed -= HandleCameraSettingsChanged;
    }

    // 在Load方法中实例化相机逻辑
    public override void Load()
    {
        // 获取GameController并绑定输入事件
        GameController = GetComponentInParent<GameController>();
        if (GameController != null && GameController._inputActions != null)
        {
            // 注意：Win10Actions是结构体，不能与null比较，直接绑定事件
            GameController._inputActions.Win10.CtrlMouse.performed += PovValueChanged;
        }
    
        // 获取跟随对象
        CameraFollowItem = GetComponentInParent<Item>();
        Player = CameraFollowItem as Player;
        _chunkLoader = GetComponentInParent<Mod_ChunkLoader>();
    
        // 直接在当前位置实例化相机预制体
        if (CamPrefab != null)
        {
            instantiatedCamera = Instantiate(CamPrefab);
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
    
        // 初始化相机视野（正交大小）
        if (Vcam != null)
        {
            CacheCameraFollowDefaults();
            LoadPovValue();
            Vcam.m_Lens.OrthographicSize = povValue;
            if (ControllerCamera != null)
                ControllerCamera.orthographicSize = povValue;
            ApplyCameraFollowSettings();
        }
        GameController._mainCamera = ControllerCamera;
    
        // 重置旋转
        transform.rotation = Quaternion.identity;
    }

    public override void Save()
    {
        SavePovValue();
    }
    
    // 销毁时调用，注销事件
    private void OnDestroy()
    {
        CameraUserSettings.Changed -= HandleCameraSettingsChanged;

        // 注销事件
        if (GameController != null && GameController._inputActions != null)
        {
            GameController._inputActions.Win10.CtrlMouse.performed -= PovValueChanged;
        }
        
        // 销毁实例化的相机预制体
        if (instantiatedCamera != null)
        {
            Destroy(instantiatedCamera);
        }
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

        CacheCameraFollowDefaults();
        if (_framingTransposer == null || !_cameraFollowDefaultsCaptured)
            return;

        float lookahead = GetZoomAdjustedLookahead();
        float inertia = Mathf.Max(0f, -lookahead);
        _framingTransposer.m_LookaheadTime = Mathf.Max(0f, lookahead);
        _framingTransposer.m_LookaheadSmoothing = CameraUserSettings.LookaheadSmoothing;
        _framingTransposer.m_XDamping = Mathf.Clamp(_baseXDamping + inertia, 0f, 20f);
        _framingTransposer.m_YDamping = Mathf.Clamp(_baseYDamping + inertia, 0f, 20f);
    }

    /// <summary>按当前拉远进度应用缩放影响系数，并保证负系数不会反转前探方向。</summary>
    private float GetZoomAdjustedLookahead()
    {
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
    /// <summary>
    /// 响应滚轮值改变视野
    /// </summary>
    /// <param name="context"></param>
    public void PovValueChanged(InputAction.CallbackContext context)
    {
        if (GameController != null && GameController.IsGameplayInputLocked)
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
        if (Vcam == null) return;

        povValue += delta;
        povValue = Mathf.Clamp(povValue, MinPovValue, MaxPovValue); // 限制视野范围
        Vcam.m_Lens.OrthographicSize = povValue;
        if (ControllerCamera != null)
            ControllerCamera.orthographicSize = povValue;
        ApplyCameraFollowSettings();

        if (_chunkLoader == null)
        {
            _chunkLoader = GetComponentInParent<Mod_ChunkLoader>();
        }

        if (_chunkLoader != null)
            _chunkLoader.RefreshChunksForCameraView();

        // Debug.Log($"视野范围修改为：{Vcam.m_Lens.OrthographicSize}");
    }

    /// <summary>Sets an absolute gameplay view size and refreshes the streamed Chunk window.</summary>
    public void SetOrthographicSize(float value)
    {
        if (Vcam == null)
            throw new System.InvalidOperationException("Camera module has no Cinemachine virtual camera.");
        povValue = Mathf.Clamp(value, MinPovValue, MaxPovValue);
        Vcam.m_Lens.OrthographicSize = povValue;
        if (ControllerCamera != null)
            ControllerCamera.orthographicSize = povValue;
        ApplyCameraFollowSettings();
        _chunkLoader ??= GetComponentInParent<Mod_ChunkLoader>();
        _chunkLoader?.RefreshChunksForCameraView();
    }

    /// <summary>按双指间距变化调整正交视野；两指分开时镜头拉近，合拢时镜头拉远。</summary>
    public void ApplyPinchZoom(float screenDistanceDelta, float sensitivity)
    {
        if (Vcam == null || GameController?.IsGameplayInputLocked == true)
            return;

        float safeSensitivity = Mathf.Max(0f, sensitivity);
        if (safeSensitivity <= 0f || Mathf.Abs(screenDistanceDelta) <= 0.001f)
            return;

        SetOrthographicSize(CurrentOrthographicSize - screenDistanceDelta * safeSensitivity);
    }

    public void EnableUnlimitedView()
    {
        MaxPovValue = float.MaxValue;
    }
    #endregion

    private void LoadPovValue()
    {
        if (ModData != null)
        {
            var saved = ModData.GetData<CameraFollowSaveData>();
            if (saved != null)
            {
                povValue = saved.PovValue;
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
        ModData.WriteData(new CameraFollowSaveData { PovValue = povValue });
    }
}
