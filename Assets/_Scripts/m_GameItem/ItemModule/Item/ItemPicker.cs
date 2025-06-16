using NUnit.Framework.Interfaces;
using System.Collections.Generic;
using UnityEngine;

public class ItemPicker : MonoBehaviour
{
    [Header("Ŀ����Ʒ���������ȼ����У�")]
    public List<Inventory> AddTargetInventories = new List<Inventory>(); // ��Ϊ�б�

    public bool canPickUp = true;

    public bool CanPickUp
    {
        get
        {
            // ����Ŀ�걳�������ˣ��Ų���ʰȡ
            foreach (var inventory in AddTargetInventories)
            {
                if (inventory != null && !inventory.Data.IsFull)
                {
                    return canPickUp;
                }
            }
            return false;
        }

        set => canPickUp = value;
    }

    private void Start()
    {
        // ���б�Ϊ�������Զ���ȡ��ǰ�����ϵ� Inventory
        if (AddTargetInventories.Count == 0)
        {
            Inventory inv = GetComponent<Inventory>();
            if (inv != null)
            {
                AddTargetInventories.Add(inv);
            }
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (AddTargetInventories.Count == 0)
        {
            Debug.LogWarning("ItemPicker: AddTargetInventories is empty");
            return;
        }

        var pickAble = other.GetComponent<Item>();
 
        if (pickAble != null && pickAble.Item_Data.Stack.CanBePickedUp ==true)
        {
            ItemData itemData = pickAble.Item_Data;

            // �������б������ҵ���һ��������ӵ�
            foreach (var inventory in AddTargetInventories)
            {
                if (inventory != null && inventory.Data.CanAddTheItem(itemData))
                {
                    Destroy(pickAble.gameObject); // ��Ʒʰȡ������
                    itemData.Stack.CanBePickedUp = false; // ��Ʒ��ջ�� CanBePickedUp ��Ϊ false
                    inventory.Data.AddItem(itemData);
                    return; // ��ӳɹ�����������
                }
            }

            Debug.Log("����Ŀ�걳�������ˣ��޷�ʰȡ��Ʒ��");
        }
    }
}
