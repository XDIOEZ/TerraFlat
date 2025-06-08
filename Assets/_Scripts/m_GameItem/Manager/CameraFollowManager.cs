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
    public void Start()
    {
        transform.rotation = Quaternion.identity;
        // ��󸸶��󣬱����ܵ������תӰ��
        transform.SetParent(null);

        // ���ø���Ŀ��
        Vcam.Follow = CameraFollowItem.transform;

        // �������������֣�����+������
        transform.name = $"{CameraFollowItem.name} �� Camera";

        // ���Դ� CameraFollowItem ��ȡ PlayerController
        if (CameraFollowItem.GetComponent<PlayerController>() != null && PlayerController == null)
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
