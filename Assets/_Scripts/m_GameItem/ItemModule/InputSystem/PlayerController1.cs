using UnityEngine;
using UnityEngine.InputSystem;
using NaughtyAttributes;
using InputSystem;
using Sirenix.OdinInspector;
using static UnityEngine.InputSystem.Layouts.InputControlLayout;

[RequireComponent(typeof(Item))]
public class PlayerController : Module
{
    #region 输入系统
    public @PlayerInputActions _inputActions;
    public Camera _mainCamera;

    public bool CtrlIsDown;
    #endregion

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
        _inputActions.Disable();
    }
    public void OnEnable()
    {
        _inputActions.Enable();
    }

    #region 获取鼠标世界坐标
    public Vector3 GetMouseWorldPosition()
    {
        return _mainCamera.ScreenToWorldPoint(Input.mousePosition);
    }
    #endregion

    #endregion

/*    #region 初始化输入系统的监听
    private void Set_InputSystem()
    {
        //初始化输入系统
     

        _inputActions.Win10.Enable();

        var win10 = _inputActions.Win10;
        //监听快捷栏
        //win10.SwitchHotBar_Player.started += SwitchHotbar;
        //监听物品丢弃
        //win10.F.performed += PlayerDropItem;
        //监听背包
        win10.B.performed += SwitchBag;
        win10.B.performed += SwitchEquip;
        win10.B.performed += SwitchCraft;
        //监听设置面板
        win10.ESC.performed += SwitchSetting;
        //监听视角切换
        //win10.CtrlMouse.performed += PovValueChanged;
        //监听鼠标
       // win10.Mouse.performed += PlayerTakeItem_FaceMouse;
        //监听鼠标滚轮
  
        //监听Ctrl键
        win10.Ctrl.started += (InputAction.CallbackContext context) => { CtrlIsDown = true; };
        win10.Ctrl.canceled += (InputAction.CallbackContext context) => { CtrlIsDown = false; };
*//*        //对E键进行监听
        win10.E.performed += Interact;*//*
        //监听右键
        //win10.RightClick.performed += UseItem;
    }
    #endregion*/

    #region 物品操作

/*    #region 使用玩家手上的物品
    public void UseItem(InputAction.CallbackContext context = default)
    {
        if (Hotbar.currentObject == null) return;
        Hotbar.currentObject.GetComponent<Item>().Act();
    }
    #endregion*/
/*    #region 和工作方块交互
*//*    //和工作方块交互
    public void Interact(InputAction.CallbackContext context = default)
    {
        _handForInteract.Interact_Start();
    }*//*
    #endregion*/

/*    #region 丢弃物品
    public void PlayerDropItem(InputAction.CallbackContext context = default)
    {
        if(Hotbar.currentObject != null)
        {
            ItemDroper.DropItemBySlot(Hotbar.CurrentSelectItemSlot);
            Hotbar.DestroyCurrentObject();
        }
        else
        {
            ItemDroper.FastDropItem();

            Hotbar.RefreshUI(Hotbar.CurrentIndex);
        }
        

    }
*//*    #endregion*//*
    #region 修改玩家视角

    public void PovValueChanged(InputAction.CallbackContext context = default)
    {
        //获取鼠标滚轮数值
        Vector2 scrollValue = (Vector2)context.ReadValueAsObject();
        //Debug.Log(scrollValue.y);
        if (scrollValue.y > 0)
        {
            //TODO视野减少
            VirtualCameraManager.ChangeCameraView(-1);
        }
        else if (scrollValue.y < 0)
        {
            //TODO视野增加
            VirtualCameraManager.ChangeCameraView(1);
        }
    }
    #endregion*/
    #region 使手持物品始终朝向鼠标

/*    public void PlayerTakeItem_FaceMouse(InputAction.CallbackContext context = default)
    {
        if(Facing == null)
            return;
        context.ReadValue<Vector2>();
        //获取鼠标
        Facing.FaceToMouse(GetMouseWorldPosition());
    }*/
    #endregion
/*    #region 切换快捷栏的使用

    private void SwitchHotbar(InputAction.CallbackContext context)
    {
        if (context.control.device is Keyboard keyboard)
        {
            if (int.TryParse(context.control.displayName, out int keyNumber))
            {
                int targetIndex = keyNumber - 1;
                if (targetIndex != Hotbar.CurrentIndex)
                {
                    Hotbar.ChangeSelectBoxPosition(targetIndex);
                    return;
                }
            }
        }
    }
    #endregion*/
    #region 通过滚轮切换快捷栏

    //private void SwitchHotbarByScroll(InputAction.CallbackContext context)
    //{
    //    if (Hotbar == null) return;
    //    if (CtrlIsDown)
    //    {
    //        return;
    //    }
    //    Vector2 scrollValue = context.ReadValue<Vector2>();
    //    //Debug.Log(scrollValue);
    //    if (scrollValue.y > 0)
    //    {
    //        //Debug.Log(Hotbar.CurrentIndex);
    //        Hotbar.ChangeSelectBoxPosition(Hotbar.CurrentIndex - 1);
    //    }
    //    else if (scrollValue.y < 0)
    //    {
    //        Hotbar.ChangeSelectBoxPosition(Hotbar.CurrentIndex + 1);
    //    }
    //}
    #endregion
    #region 开关背包

    //开关背包
    public void SwitchBag(InputAction.CallbackContext context = default)
    {
 
    }
    #endregion
    #region 开关设置面板

    //开关设置面板
    public void SwitchSetting(InputAction.CallbackContext context = default)
    {
        //_playerUI.Setting.enabled = !_playerUI.Setting.enabled;
    }
    #endregion
    #region 开关装备栏位

    //开关装备栏
    public void SwitchEquip(InputAction.CallbackContext context = default)
    {
       // _playerUI.Equip.enabled = !_playerUI.Equip.enabled;
    }
    #endregion
    #region 开关手工合成面板

    //开关制作栏
    public void SwitchCraft(InputAction.CallbackContext context = default)
    {
        //_playerUI.Craft.enabled = !_playerUI.Craft.enabled;
    }
    #endregion
    #region 松开停止奔跑


    public override void Load()
    {
       // throw new System.NotImplementedException();
    }

    public override void Save()
    {
       // throw new System.NotImplementedException();
    }
    #endregion
    #endregion
}