using NUnit.Framework.Interfaces;
using System.Collections.Generic;
using UnityEngine;

public class ItemPicker : MonoBehaviour
{
    [Header("目标物品栏（按优先级排列）")]
    public List<Inventory> AddTargetInventories = new List<Inventory>(); // 改为列表

    public bool canPickUp = true;

    public bool CanPickUp
    {
        get
        {
            // 所有目标背包都满了，才不能拾取
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
        // 若列表为空则尝试自动获取当前对象上的 Inventory
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

            // 遍历所有背包，找到第一个可以添加的
            foreach (var inventory in AddTargetInventories)
            {
                if (inventory != null && inventory.Data.CanAddTheItem(itemData))
                {
                    Destroy(pickAble.gameObject); // 物品拾取后销毁
                    itemData.Stack.CanBePickedUp = false; // 物品堆栈的 CanBePickedUp 置为 false
                    inventory.Data.AddItem(itemData);
                    return; // 添加成功后立即返回
                }
            }

            Debug.Log("所有目标背包都满了，无法拾取物品。");
        }
    }
}
