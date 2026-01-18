using Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 管理摄像机跟随逻辑
/// </summary>
public class CameraFollowManager : Module
{
    #region 字段与属性
    [Header("模块数据")]
    public Ex_ModData ModData;
    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = (Ex_ModData)value;
    }

    [Header("摄像机配置")]
    public CinemachineVirtualCamera vcam;
    public Camera ControllerCamera;

    [Header("跟随目标")]
    public Item CameraFollowItem;
    public Player Player;
    public GameController GameController;

    public GameObject CamPrefab;
    private GameObject instantiatedCamera;

    /// <summary>
    /// 获取或设置虚拟摄像机
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

    // 在Load方法中修改实例化逻辑，直接在世界空间中创建
    public override void Load()
    {
        // 获取GameController并绑定鼠标滚轮事件
        GameController = GetComponentInParent<GameController>();
        if (GameController != null && GameController._inputActions != null)
        {
            // 注意：Win10Actions是结构体，不能与null比较，直接绑定事件
            GameController._inputActions.Win10.CtrlMouse.performed += PovValueChanged;
        }
    
        // 获取跟随物体
        CameraFollowItem = GetComponentInParent<Item>();
        Player = CameraFollowItem as Player;
    
        // 直接在世界空间中实例化摄像机预制体
        if (CamPrefab != null)
        {
            instantiatedCamera = Instantiate(CamPrefab);
            Debug.Log("摄像机预制体已在世界空间中实例化");
        }
        else
        {
            Debug.LogError("CamPrefab未设置，请在Inspector中指定摄像机预制体");
        }
    
        // 从实例化的预制体中获取摄像机组件
        if (instantiatedCamera != null)
        {
            ControllerCamera = instantiatedCamera.GetComponentInChildren<Camera>();
            vcam = instantiatedCamera.GetComponentInChildren<CinemachineVirtualCamera>();
        }
    
        // 初始化虚拟摄像机跟随目标
        if (Vcam != null && CameraFollowItem != null)
        {
            Vcam.Follow = CameraFollowItem.transform;
        }
    
        // 移除设置父对象为null的代码，因为已经在世界空间中实例化了
        
        // 初始化摄像机视野（添加空值检查）
        if (Vcam != null && Player != null)
        {
            Vcam.m_Lens.OrthographicSize = Player.PovValue;
        }
        GameController._mainCamera = ControllerCamera;
    
        // 重置旋转
        transform.rotation = Quaternion.identity;
    }

    public override void Save()
    {
        // TODO: 实现保存逻辑
    }
    
    // 销毁时调用，解除事件绑定
    private void OnDestroy()
    {
        // 解除事件绑定
        if (GameController != null && GameController._inputActions != null)
        {
            GameController._inputActions.Win10.CtrlMouse.performed -= PovValueChanged;
        }
        
        // 销毁实例化的摄像机预制体
        if (instantiatedCamera != null)
        {
            Destroy(instantiatedCamera);
        }
    }
    #endregion

    #region 摄像机操作方法
    /// <summary>
    /// 鼠标滚轮调整视野
    /// </summary>
    /// <param name="context"></param>
    public void PovValueChanged(InputAction.CallbackContext context)
    {
        Vector2 scrollValue = context.ReadValue<Vector2>();
        if (scrollValue.y > 0)
            ChangeCameraView(-1); // 缩小视野
        else if (scrollValue.y < 0)
            ChangeCameraView(1);  // 放大视野
    }

    /// <summary>
    /// 修改摄像机视野范围
    /// </summary>
    /// <param name="delta">视野变化值</param>
    public void ChangeCameraView(float delta)
    {
        if (Player == null || Vcam == null) return;

        Player.PovValue += delta;
        Vcam.m_Lens.OrthographicSize += delta;

        // Debug.Log($"视野范围修改为：{Vcam.m_Lens.OrthographicSize}");
    }
    #endregion
}