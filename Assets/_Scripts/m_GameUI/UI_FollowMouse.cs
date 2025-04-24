using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UI_FollowMouse : MonoBehaviour
{
    [Tooltip("�Ƿ����ø�����깦��")]
    public bool followMouse = true;

    [Tooltip("���λ���� UI Ԫ�ص�ƫ����")]
    public Vector3 offset = Vector3.zero;

    private RectTransform rectTransform;

    void Start()
    {
        // ȷ������ RectTransform ���
        rectTransform = GetComponent<RectTransform>();
        if (rectTransform == null)
        {
            Debug.LogError("UI_FollowMouse ��Ҫ������һ��ӵ�� RectTransform �Ķ����ϣ�");
        }
    }

    void Update()
    {
        if (followMouse && rectTransform != null)
        {
            FollowMousePosition();
        }
    }

    private void FollowMousePosition()
    {
        // ��ȡ�������Ļ�е�λ��
        Vector3 mousePosition = Input.mousePosition;

        // �����λ��ת��Ϊ�������꣬������ƫ��
        rectTransform.position = mousePosition + offset;
    }

    public void EnableFollowMouse(bool enable)
    {
        followMouse = enable;
    }
}
