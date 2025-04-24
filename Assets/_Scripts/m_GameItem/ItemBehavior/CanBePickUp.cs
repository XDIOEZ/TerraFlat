using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// �ɱ�ʰȡ�����ʵ�� ICanBePickUp �ӿ�
/// </summary>
public class CanBePickUp : MonoBehaviour, ICanBePickUp
{
    // ��ǰ���ص���Ʒ���
    public Item item;

    /// <summary>
    /// ʵ�ֽӿ����ԣ��ɱ�ʰȡ����Ʒ����
    /// </summary>
    public ItemData PickUp_ItemData
    {
        get => item.Item_Data;
        set => item.Item_Data = value;
    }

    /// <summary>
    /// ��ʼ������ȡ��Ʒ�������
    /// </summary>
    private void Start()
    {
        item = GetComponent<Item>();
    }

    /// <summary>
    /// ʰȡ�߼���������Ʒ����
    /// </summary>
    public virtual ItemData Pickup()
    {
        // �����Ʒ����Ϊ����ʰȡ���򷵻� null
        if (!item.Item_Data.Stack.CanBePickedUp)
        {
            // Debug.Log("���ܱ�ʰȡ: " + item.Item_Data.Name);
            return null;
        }

        // ����Ϊ�����ٴ�ʰȡ
        item.Item_Data.Stack.CanBePickedUp = false;

        // ������Ʒ GameObject
        Destroy(item.gameObject);

        // ���ظ���Ʒ����
        return item.Item_Data;
    }

    /*
    // �����߼����ο�����ʹ�� Item.GetData().CanBePickedUp �ж�
    if (!item.GetData().CanBePickedUp)
    {
        Debug.Log("Can't be picked up: " + item.GetData().Name);
        return null;
    }
    */
}
