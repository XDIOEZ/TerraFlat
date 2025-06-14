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

        var pickAble = other.GetComponentInParent<ICanBePickUp>();
        if (pickAble != null)
        {
            ItemData itemData = pickAble.PickUp_ItemData;

            // �������б������ҵ���һ��������ӵ�
            foreach (var inventory in AddTargetInventories)
            {
                if (inventory != null && inventory.Data.CanAddTheItem(itemData))
                {
                    inventory.Data.AddItem(pickAble.Pickup());
                    return; // ��ӳɹ�����������
                }
            }

            Debug.Log("����Ŀ�걳�������ˣ��޷�ʰȡ��Ʒ��");
        }
    }
}
