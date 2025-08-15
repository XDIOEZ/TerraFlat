using Cinemachine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class CameraFollowManager : Module
{
    public Ex_ModData ModData;
    public override ModuleData _Data { get => ModData; set => ModData = (Ex_ModData)value; }

    public CinemachineVirtualCamera vcam;

    public Item CameraFollowItem;

    public PlayerController PlayerController;

    public CinemachineVirtualCamera Vcam
    {
        get
        {
            if (vcam == null)
            {
                vcam = GetComponentInChildren<CinemachineVirtualCamera>();
            }
            return vcam;
        }

        set
        {
            vcam = value;
        }
    }

    public Camera ControllerCamera;

    public Player Player;

    public new void Awake()
    {
        if (_Data.ID == "")
        _Data.ID = ModText.Camera;
    }

    public void PovValueChanged(InputAction.CallbackContext context = default)
    {
        //获取鼠标滚轮数值
        Vector2 scrollValue = (Vector2)context.ReadValueAsObject();
        //Debug.Log(scrollValue.y);
        if (scrollValue.y > 0)
        {
            //TODO视野减少
            ChangeCameraView(-1);
        }
        else if (scrollValue.y < 0)
        {
            //TODO视野增加
            ChangeCameraView(1);
        }
    }

    // Start is called before the first frame update
    public override void Load()
    {
        item.GetComponent<PlayerController>()._inputActions.Win10.CtrlMouse.performed += PovValueChanged;

        ControllerCamera = GetComponentInChildren<Camera>();

        transform.rotation = Quaternion.identity;
      
        PlayerController = GetComponentInParent<PlayerController>();

        CameraFollowItem = GetComponentInParent<Item>();


        // 设置跟随目标
        Vcam.Follow = CameraFollowItem.transform;


        // 重命名物体名字，本体+父对象
        transform.name = $"{CameraFollowItem.name} 的 Camera";

        transform.SetParent(null);

        Player = CameraFollowItem as Player;
        Vcam.m_Lens.OrthographicSize = Player.PovValue;
    }


    //修改视野范围方法
    public void ChangeCameraView(float view)
    {
        Player.PovValue += view;
        Vcam.m_Lens.OrthographicSize += view;
       // Debug.Log("视野范围修改为：" + Vcam.m_Lens.FieldOfView);
    }


    public override void Save()
    {
        //throw new System.NotImplementedException();
    }
}
