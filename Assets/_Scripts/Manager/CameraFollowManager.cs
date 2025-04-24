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

    // Start is called before the first frame update
    void Start()
    {
        //跟随父对象
        Vcam.Follow = CameraFollowItem.transform;
        //重命名物体名字,本体+父对象
        transform.name = $"{transform.parent.name} 的 Camera";

        //尝试从CameraFollowItem上获取PlayerController
        if (CameraFollowItem.GetComponent<PlayerController>()!= null&&PlayerController == null)
        {
            PlayerController = CameraFollowItem.GetComponent<PlayerController>();
        }

        PlayerController.VirtualCameraManager = this;
    }

    //修改视野范围方法
    public void ChangeCameraView(float view)
    {
        Vcam.m_Lens.OrthographicSize += view;
       // Debug.Log("视野范围修改为：" + Vcam.m_Lens.FieldOfView);
    }
}
