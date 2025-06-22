# if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameTestManager : MonoBehaviour
{
    // Start is called before the first frame update
    void Update()
    {
        // ����������Ƿ���
        if (Input.GetMouseButtonDown(0))
        {
            // ��ȡ�������
            Camera mainCamera = Camera.main;
            if (mainCamera == null)
            {
                Debug.LogWarning("������û�����������");
                return;
            }

            // �������λ���������λ�÷�������
            Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;

            // �������߼��
            if (Physics.Raycast(ray, out hit))
            {
                // ��ȡ���е���Ϸ����
                GameObject hitObject = hit.collider.gameObject;
                // ����������������
                Debug.Log("���е�����: " + hitObject.name);
            }
            else
            {
                Debug.Log("����δ�����κ����塣");
            }
        }
    }
}
 #endif
