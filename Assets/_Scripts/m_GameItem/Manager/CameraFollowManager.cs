using Cinemachine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraFollowManager : MonoBehaviour
{
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

    // Start is called before the first frame update
    public void Start()
    {
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
}
