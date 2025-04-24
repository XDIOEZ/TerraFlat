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
        //���游����
        Vcam.Follow = CameraFollowItem.transform;
        //��������������,����+������
        transform.name = $"{transform.parent.name} �� Camera";

        //���Դ�CameraFollowItem�ϻ�ȡPlayerController
        if (CameraFollowItem.GetComponent<PlayerController>()!= null&&PlayerController == null)
        {
            PlayerController = CameraFollowItem.GetComponent<PlayerController>();
        }

        PlayerController.VirtualCameraManager = this;
    }

    //�޸���Ұ��Χ����
    public void ChangeCameraView(float view)
    {
        Vcam.m_Lens.OrthographicSize += view;
       // Debug.Log("��Ұ��Χ�޸�Ϊ��" + Vcam.m_Lens.FieldOfView);
    }
}
