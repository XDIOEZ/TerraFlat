using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement; // ���볡�����������ռ�

[RequireComponent(typeof(BoxCollider2D))]
public class WorldEdge : Item
{
    public Com_ItemData Data;
    public override ItemData Item_Data { get { return Data; } set { Data = value as Com_ItemData; } }

    // ��ײ���͵�ʲô����
    public string TPTOSceneName { get => Data.code; set => Data.code = value; }

    public override void Act()
    {
        throw new System.NotImplementedException();
    }

    // ������봫���ŷ�����ײʱ
    public void OnTriggerEnter2D(Collider2D collision)
    {
        // �����ײ�����ǲ������
        if (collision.gameObject.name =="Player") // ���������Ҷ�����һ�� "Player" ��ǩ
        {
            // ���ش��͵��ĳ���
            if (!string.IsNullOrEmpty(TPTOSceneName))
            {
                SceneManager.LoadScene(TPTOSceneName);  // ���͵�ָ������
            }
            else
            {
                Debug.LogWarning("���ͳ�������Ϊ�գ�");
            }
        }
    }
}
