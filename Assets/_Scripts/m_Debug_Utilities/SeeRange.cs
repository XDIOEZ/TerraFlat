using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SeeRange : MonoBehaviour
{
    [Header("�ɼ���Χ����")]
    public float[] radii = new float[1]; // ��ʼ��Ϊһ��Ĭ��ֵ����
    public Vector2 Pianyi = new Vector2(0, 0);

   [Header("Gizmos ��ɫ����")]
    public Color gizmoColor = Color.red; // ��¶ Gizmos ��ɫ����

    // �ڳ�����ͼ�л��ƿɼ�Բ
    private void OnDrawGizmos()
    {
        Vector2 center = transform.position;
         center += Pianyi;

        // ��� radii �����Ƿ��ѳ�ʼ��������Ԫ��
        if (radii != null && radii.Length > 0)
        {
            // ����ÿ��Բ
            for (int i = 0; i < radii.Length; i++)
            {
                Gizmos.color = gizmoColor; // ʹ�ñ�¶����ɫ
                Gizmos.DrawWireSphere(center, radii[i]); // ����Բ�ı߽�
            }
        }
        else
        {
            Debug.LogWarning("radii ����δ��ʼ����Ϊ�ա�");
        }
    }
}
