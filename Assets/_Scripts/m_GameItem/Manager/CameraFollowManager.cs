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

    // Start is called before the first frame update
    public void Start()
    {
        ControllerCamera = GetComponentInChildren<Camera>();

        transform.rotation = Quaternion.identity;
      




        PlayerController = GetComponentInParent<PlayerController>();
        CameraFollowItem = GetComponentInParent<Item>();


        // ���ø���Ŀ��
        Vcam.Follow = CameraFollowItem.transform;


        // �������������֣�����+������
        transform.name = $"{CameraFollowItem.name} �� Camera";

        transform.SetParent(null);
    }


    //�޸���Ұ��Χ����
    public void ChangeCameraView(float view)
    {
        Vcam.m_Lens.OrthographicSize += view;
       // Debug.Log("��Ұ��Χ�޸�Ϊ��" + Vcam.m_Lens.FieldOfView);
    }
}
