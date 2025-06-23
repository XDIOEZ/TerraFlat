using Force.DeepCloner;
using MemoryPack;
using System;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;

[Serializable]
[MemoryPackable]
public partial class Inventory_Data
{
    // 背包名称
    public string inventoryName = string.Empty;
    // 保存物品的列表
    public List<ItemSlot> itemSlots = new List<ItemSlot>();
    // 当前选择的插槽索引
    public int selectedSlotIndex = 0;

    public bool IsFull
    {
        get
        {
            foreach (var slot in itemSlots)
            {
                if (slot._ItemData == null) 
                {
                    return false;
                }
            }
            return true;
        }
    }

    // 当物品槽发生变化时触发的事件
    [MemoryPackIgnore]
    public UltEvent<int, ItemSlot> onSlotChanged = new UltEvent<int, ItemSlot>();
    // 当背包 UI 发生变化时触发的事件
    [MemoryPackIgnore]
    public UltEvent<int> Event_RefreshUI = new UltEvent<int>();
    // 构造函数
    [MemoryPackConstructor]
    public Inventory_Data(List<ItemSlot> itemSlots, string inventoryName)
    {
        this.itemSlots = itemSlots;
        this.inventoryName = inventoryName;
    }

    // 空白构造函数
    public Inventory_Data() { }

    #region 操作方法
    public void RemoveItemAll(ItemSlot itemSlot, int index = 0)
    {
        Event_RefreshUI.Invoke(index);
        itemSlot._ItemData = null;
    }

    public void ChangeItemData_Default(int index, ItemSlot inputSlotHand)
    {
        float ChangeReate = 1;
        if (inputSlotHand.Belong_Inventory.GetComponent<Inventory_Hand>() != null)
        {
            ChangeReate = inputSlotHand.Belong_Inventory.GetComponent<Inventory_Hand>().GetItemAmountRate;
        }
        ItemData localData = itemSlots[index]._ItemData;

        ItemData inputDataHand = inputSlotHand._ItemData;

        ItemSlot localSlot = itemSlots[index];

        // 两者为空
        if (inputDataHand == null && localData == null)
        {
            return;
        }

        // 手上有物体，本地无物体
        if (inputDataHand != null && localData == null)
        {
            int changeAmount = (int)Mathf.Ceil(inputDataHand.Stack.Amount * ChangeReate);
            ChangeItemAmount(inputSlotHand, localSlot, changeAmount);
            Event_RefreshUI.Invoke(index);
            return;
        }

        // 本地有物体，手上没物体
        if (localData != null && inputDataHand == null)
        {
            int changeAmount = (int)Mathf.Ceil(localData.Stack.Amount * ChangeReate);
            ChangeItemAmount(localSlot, inputSlotHand, changeAmount);
            Event_RefreshUI.Invoke(index);
            return;
        }

        // 特殊交换
        if (localSlot._ItemData.Stack.Volume ==1 || localSlot._ItemData.ItemSpecialData != inputSlotHand._ItemData.ItemSpecialData)
        {
            Debug.Log("特殊交换");
            localSlot.Change(inputSlotHand);
            Event_RefreshUI.Invoke(index);
            return;
        }

        // 物品相同
        if (inputSlotHand._ItemData.IDName == localSlot._ItemData.IDName)
        {
            int changeAmount = (int)Mathf.Ceil(localSlot._ItemData.Stack.Amount * ChangeReate);
            ChangeItemAmount(localSlot, inputSlotHand, changeAmount);
            Event_RefreshUI.Invoke(index);
            return;
        }

        // 两者不为空且物品不相同
        if (inputDataHand != null && localData != null && localData.IDName != inputDataHand.IDName)
        {
            localSlot.Change(inputSlotHand);
            Event_RefreshUI.Invoke(index);
            Debug.Log("(物品不同)交换物品槽位:" + index + " 物品:" + inputSlotHand._ItemData.IDName);
            return;
        }
    }

    //修改对应插槽的数量
    public void ChangeItemDataAmount(int index, float amount)
    {
        itemSlots[index]._ItemData.Stack.Amount += amount;
    }

    public bool ChangeItemAmount(ItemSlot localSlot, ItemSlot inputSlotHand, int count)
    {
        int changeCount = 0;

        if (inputSlotHand._ItemData == null)
        {
            ItemData tempItemData = localSlot._ItemData.DeepClone();
            tempItemData.Stack.Amount = 1;
            inputSlotHand._ItemData = tempItemData;
            inputSlotHand._ItemData.Stack.Amount = 0;
        }

        if (localSlot._ItemData.ItemSpecialData != inputSlotHand._ItemData.ItemSpecialData)
        {
            return false;
        }

        while (true)
        {
            localSlot._ItemData.Stack.Amount--;
            changeCount++;
            inputSlotHand._ItemData.Stack.Amount++;

            // 如果本地槽位容量已经满了，跳出循环
            if (changeCount >= count || localSlot._ItemData.Stack.Amount <= 0 || inputSlotHand._ItemData.Stack.Amount >= inputSlotHand.SlotMaxVolume)
            {
                if (localSlot._ItemData.Stack.Amount <= 0)
                {
                    localSlot.ClearData();
                }
                return true;
            }
        }
    }

    // 获取指定索引的物品槽
    public ItemSlot GetItemSlot(int index)
    {
        if (index < 0 || index >= itemSlots.Count)
        {
            return itemSlots[0];
        }
        return itemSlots[index];
    }

    // 获取指定索引的物品数据
    public ItemData GetItemData(int index)
    {
        return GetItemSlot(index)._ItemData;
    }

    public bool AddItem(ItemData inputItemData)
    {
        // 物品为空或无法添加时直接返回
        if (!CanAddTheItem(inputItemData))
        {
            // Debug.Log("无法添加物品：物品为空或背包已满");
            return false;
        }

        int stackIndex = -1; // 可堆叠的槽位索引
        int emptyIndex = -1; // 空槽位索引

        // 检查是否可以进行堆叠（仅当物品体积小于最小可堆叠体积时允许）
        if (inputItemData.Stack.Volume == 1)
        {
            for (int i = 0; i < itemSlots.Count; i++)
            {
                var slot = itemSlots[i];
                if (!slot.IsFull && slot._ItemData != null &&
                    slot._ItemData.ItemSpecialData == inputItemData.ItemSpecialData &&
                    slot._ItemData.IDName == inputItemData.IDName &&
                    slot._ItemData.Stack.CurrentVolume + inputItemData.Stack.CurrentVolume <= slot.SlotMaxVolume)
                {
                    stackIndex = i;
                    break;
                }
            }
        }


        // 如果无法堆叠，则寻找空槽位
        if (stackIndex == -1)
        {
            for (int i = 0; i < itemSlots.Count; i++)
            {
                if (itemSlots[i]._ItemData == null)
                {
                    emptyIndex = i;
                    break;
                }
            }
        }

        // 进行物品添加操作
        if (stackIndex != -1)
        {
            // 堆叠物品
            //  Debug.Log("堆叠物品到槽位: " + stackIndex);
            ChangeItemDataAmount(stackIndex, inputItemData.Stack.Amount);
        }
        else if (emptyIndex != -1)
        {
            // 放入新物品
            // Debug.Log("放入物品到空槽位: " + emptyIndex);
            SetOne_ItemData(emptyIndex, inputItemData);
        }
        else
        {
            Debug.LogError("背包已满，无法添加物品");
            return false;
        }

        inputItemData.Stack.CanBePickedUp = false;
        // 进行物品添加操作
        int updatedIndex = -1; // 记录实际更新的槽位索引

        if (stackIndex != -1)
        {
            // 堆叠物品
            // Debug.Log("堆叠物品到槽位: " + stackIndex);
            ChangeItemDataAmount(stackIndex, inputItemData.Stack.Amount);
            updatedIndex = stackIndex;
        }
        else if (emptyIndex != -1)
        {
            // 放入新物品
            // Debug.Log("放入物品到空槽位: " + emptyIndex);
            SetOne_ItemData(emptyIndex, inputItemData);
            updatedIndex = emptyIndex;
        }
        else
        {
            Debug.LogError("背包已满，无法添加物品");
            return false;
        }

        inputItemData.Stack.CanBePickedUp = false;
        Event_RefreshUI.Invoke(updatedIndex); // 使用实际更新的槽位索引
        return true;
    }

    public bool CanAddTheItem(ItemData inputItemData)
    {
        if (inputItemData == null)
        {
            // Debug.Log("物品为空，无法添加");
            return false;
        }

        // 如果物品体积大于等于最小可堆叠体积，则只能放入空槽位
        if (inputItemData.Stack.Volume > 1)
        {
            foreach (var slot in itemSlots)
            {
                if (slot._ItemData == null)
                {
                    return true;
                }
            }
            Debug.Log("背包已满，无法添加大体积物品");
            return false;
        }

        // 查找可堆叠的位置
        foreach (var slot in itemSlots)
        {
            if (!slot.IsFull && slot._ItemData != null &&
                slot._ItemData.ItemSpecialData == inputItemData.ItemSpecialData &&
                slot._ItemData.IDName == inputItemData.IDName &&
                slot._ItemData.Stack.CurrentVolume + inputItemData.Stack.CurrentVolume <= slot.SlotMaxVolume)
            {
                //  Debug.Log("找到可堆叠的槽位");
                return true;
            }
        }

        // 如果不能堆叠，则寻找空槽位
        foreach (var slot in itemSlots)
        {
            if (slot._ItemData == null)
            {
                // Debug.Log("找到空槽位");
                return true;
            }
        }

        Debug.LogError("背包已满，无法添加物品");
        return false;
    }

    public void SetOne_ItemData(int index, ItemData inputItemData)
    {
        itemSlots[index]._ItemData = inputItemData;
    }
    #endregion
}
