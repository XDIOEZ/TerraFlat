using UnityEngine;
using UnityEngine.InputSystem;
using UltEvents;
using InputSystem;

[RequireComponent(typeof(Item))]
public class GameController : Module
{
    #region 输入系统
    public PlayerInputActions _inputActions;
    public Camera _mainCamera;
    public bool CtrlIsDown;
    #endregion

    public UltEvent LeftClick = new UltEvent();
    public UltEvent LeftClickUp = new UltEvent();
    public UltEvent RightClick = new UltEvent();
    public UltEvent RightClickUp = new UltEvent();

    public Ex_ModData _modData;
    public override ModuleData _Data { get => _modData; set => _modData = value as Ex_ModData; }

    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.Controller;
        }
        _inputActions = new PlayerInputActions();
    }

    #region Unity生命周期
    public void OnDisable()
    {
        // 取消左键/右键监听
        if (_inputActions != null && _inputActions.Win10.LeftClick != null)
        {
            _inputActions.Win10.LeftClick.performed -= LeftClickAction;
            _inputActions.Win10.LeftClick.canceled -= LeftClickUpAction;
        }

        if (_inputActions != null && _inputActions.Win10.RightClick != null)
        {
            _inputActions.Win10.RightClick.performed -= RightClickAction;
            _inputActions.Win10.RightClick.canceled -= RightClickUpAction;
        }
        _inputActions.Disable();
    }
    
    public void OnEnable()
    {
        _inputActions.Enable();
        _inputActions.Win10.LeftClick.performed += LeftClickAction;
        _inputActions.Win10.LeftClick.canceled += LeftClickUpAction;
        // 添加右键点击监听
        _inputActions.Win10.RightClick.performed += RightClickAction;
        _inputActions.Win10.RightClick.canceled += RightClickUpAction;
    }
    
    public void LeftClickAction(InputAction.CallbackContext obj)
    {
        LeftClick.Invoke();
    }

    // 左键抬起处理方法
    public void LeftClickUpAction(InputAction.CallbackContext obj)
    {
        LeftClickUp.Invoke();
    }
    
    // 右键点击处理方法
    public void RightClickAction(InputAction.CallbackContext obj)
    {
        RightClick.Invoke();
    }

    // 右键抬起处理方法
    public void RightClickUpAction(InputAction.CallbackContext obj)
    {
        RightClickUp.Invoke();
    }
    
    
    public void OnDestroy()
    {
        // 清理事件
        LeftClick.Clear();
        LeftClickUp.Clear();
        RightClick.Clear();
        RightClickUp.Clear();
    }

    #region 获取鼠标世界坐标
    public Vector3 GetMouseWorldPosition()
    {
        return _mainCamera.ScreenToWorldPoint(Input.mousePosition);
    }
    #endregion

    #endregion


    #region 数据存取
    public override void Load()
    {
        // TODO: 实现加载逻辑 可以是玩家修改的键位
    }

    public override void Save()
    {
        // TODO: 实现保存逻辑
    }
    #endregion
}