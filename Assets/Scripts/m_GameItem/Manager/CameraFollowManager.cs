using Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 管理摄像机跟随逻辑
/// </summary>
public class CameraFollowManager : Module
{
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
    public PlayerController PlayerController;

    /// <summary>
    /// 获取或设置虚拟摄像机
    /// </summary>
    public CinemachineVirtualCamera Vcam
    {
        get
        {
            if (vcam == null)
                vcam = GetComponentInChildren<CinemachineVirtualCamera>();
            return vcam;
        }
        set => vcam = value;
    }

    #region 生命周期方法
    public new void Awake()
    {
        // 如果ID为空，则使用默认名称
        if (string.IsNullOrEmpty(_Data.ID))
            _Data.ID = ModText.Camera;
    }

    public override void Load()
    {
        // 获取PlayerController并绑定鼠标滚轮事件
        PlayerController = GetComponentInParent<PlayerController>();
        if (PlayerController != null && PlayerController._inputActions != null)
        {
            // 注意：Win10Actions是结构体，不能与null比较，直接绑定事件
            PlayerController._inputActions.Win10.CtrlMouse.performed += PovValueChanged;
        }

        // 获取跟随物体
        CameraFollowItem = GetComponentInParent<Item>();
        Player = CameraFollowItem as Player;

        // 获取主摄像机
        ControllerCamera = GetComponentInChildren<Camera>();

        // 初始化虚拟摄像机跟随目标
        if (Vcam != null && CameraFollowItem != null)
        {
            Vcam.Follow = CameraFollowItem.transform;
        }

        // 重命名摄像机物体（添加空值检查）
        if (CameraFollowItem != null)
        {
            transform.name = $"{CameraFollowItem.name} 的 Camera";
        }

        // 将摄像机脱离父对象（添加空值检查）
        if (ControllerCamera != null)
        {
            ControllerCamera.transform.SetParent(null);
        }
        if (vcam != null)
        {
            vcam.transform.SetParent(null);
        }

        // 初始化摄像机视野（添加空值检查）
        if (Vcam != null && Player != null)
        {
            Vcam.m_Lens.OrthographicSize = Player.PovValue;
        }

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
        if (PlayerController != null && PlayerController._inputActions != null)
        {
            PlayerController._inputActions.Win10.CtrlMouse.performed -= PovValueChanged;
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