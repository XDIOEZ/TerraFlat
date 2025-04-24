using Force.DeepCloner;
using MemoryPack;
using NaughtyAttributes;
using System;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;

// 库存类，继承自 MonoBehaviour
[System.Serializable]
public class Inventory : MonoBehaviour
{
    #region 字段
    [Tooltip("物品所属对象")]
    [ShowNonSerializedField]
    public IInventoryData belong_Item;
    [Tooltip("对应 UI 管理器")]
    public Inventory_UI _ui;
    [Tooltip("序列化保存的容器数据")]
    public Inventory_Data Data = new Inventory_Data();
    [Tooltip("最小堆叠容量数量")]
    public int MinStackVolume = 2;
    [Tooltip("当物品槽发生变化时触发的事件")]
    public UltEvent<int, ItemSlot> onSlotChanged = new UltEvent<int, ItemSlot>();
    [Tooltip("当背包发生变化时触发的事件")]
    public UltEvent<int> onUIChanged = new UltEvent<int>();
    #endregion

    #region 属性
    // 标识插槽是否已经全部满了
    [ShowNativeProperty]
    public bool Inventory_Slots_All_IsFull
    {
        get
        {
            foreach (var slot in Data.itemSlots)
            {
                if (!slot.IsFull)
                {
                    return false;
                }
            }
            return true;
        }
    }
    public IInventoryData Belong_Item
    {
        get
        {
            return belong_Item;
        }
        set
        {
            belong_Item = value;
        }
    }
    public Inventory_UI UI
    {
        get
        {
            if (_ui == null)
            {
                _ui = GetComponent<Inventory_UI>();
            }
            return _ui;
        }
    }
    #endregion

    #region 生命周期

    public void Awake()
    {
        onSlotChanged += ChangeItemData_Default;//注册默认交互事件
        onSlotChanged += (int index, ItemSlot itemSlot) => { onUIChanged.Invoke(index); };//注册UI变化事件                                                                          // 订阅库存UI变化事件，当库存UI变化时调用刷新UI方法
        onUIChanged += UI.RefreshSlotUI;
    }


    #endregion

    #region 增删改
    public bool AddItem(ItemData inputItemData, int index = -1)
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
        if (inputItemData.Stack.Volume < MinStackVolume)
        {
            for (int i = 0; i < Data.itemSlots.Count; i++)
            {
                var slot = Data.itemSlots[i];
                if (!slot.IsFull && slot._ItemData != null &&
                    slot._ItemData.ItemSpecialData == inputItemData.ItemSpecialData &&
                    slot._ItemData.PrefabPath == inputItemData.PrefabPath &&
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
            for (int i = 0; i < Data.itemSlots.Count; i++)
            {
                if (Data.itemSlots[i]._ItemData == null)
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
            Debug.Log("堆叠物品到槽位: " + stackIndex);
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
        UI.RefreshAllInventoryUI();
        inputItemData.Stack.CanBePickedUp = false;
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
        if (inputItemData.Stack.Volume >= MinStackVolume)
        {
            foreach (var slot in Data.itemSlots)
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
        foreach (var slot in Data.itemSlots)
        {
            if (!slot.IsFull && slot._ItemData != null &&
                slot._ItemData.ItemSpecialData == inputItemData.ItemSpecialData &&
                slot._ItemData.Name == inputItemData.Name &&
                slot._ItemData.Stack.CurrentVolume + inputItemData.Stack.CurrentVolume <= slot.SlotMaxVolume)
            {
                Debug.Log("找到可堆叠的槽位");
                return true;
            }
        }

        // 如果不能堆叠，则寻找空槽位
        foreach (var slot in Data.itemSlots)
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
    // 移除物品
    public void RemoveItemAll(ItemSlot itemSlot, int index = 0)
    {
        itemSlot._ItemData = null;
        onUIChanged.Invoke(index);
    }

    //添加一个新方法 用于减少特定数量的物品
    public bool RemoveItemAmount(ItemSlot itemSlot, int amount)
    {
        if (itemSlot._ItemData == null)
        {
            Debug.Log("物品槽位为空，无法减少");
            return false;
        }

        if (itemSlot._ItemData.Stack.Amount < amount)
        {
            Debug.Log("物品槽位数量不足，无法减少");
            return false;
        }

        itemSlot._ItemData.Stack.Amount -= amount;
        onUIChanged.Invoke(itemSlot.Index);
        return true;
    }
    // 默认的物品数据交换方法
    public void ChangeItemData_Default(int index, ItemSlot inputSlotHand)
    {
        float ChangeReate = 1;
        if (inputSlotHand.Belong_Inventory.GetComponent<Inventory_Hand>() != null)
        {
            ChangeReate = inputSlotHand.Belong_Inventory.GetComponent<Inventory_Hand>().GetItemAmountRate;
        }
        ItemData localData = Data.itemSlots[index]._ItemData;

        ItemData inputDataHand = inputSlotHand._ItemData;

        ItemSlot localSlot = Data.itemSlots[index];





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
            onUIChanged.Invoke(index);
            return;
        }

        // 本地有物体，手上没物体
        if (localData != null && inputDataHand == null)
        {
            int changeAmount = (int)Mathf.Ceil(localData.Stack.Amount * ChangeReate);
            ChangeItemAmount(localSlot, inputSlotHand, changeAmount);
            onUIChanged.Invoke(index);
            return;
        }

        // 特殊交换
        if (localSlot._ItemData.Stack.Volume > MinStackVolume || localSlot._ItemData.ItemSpecialData != inputSlotHand._ItemData.ItemSpecialData)
        {
            Debug.Log("特殊交换");
            localSlot.Change(inputSlotHand);
            onUIChanged.Invoke(index);
            return;
        }

        // 物品相同
        if (inputSlotHand._ItemData.Name == localSlot._ItemData.Name)
        {
            int changeAmount = (int)Mathf.Ceil(localSlot._ItemData.Stack.Amount * ChangeReate);
            ChangeItemAmount(localSlot, inputSlotHand, changeAmount);
            onUIChanged.Invoke(index);
            return;
        }

        // 两者不为空且物品不相同
        if (inputDataHand != null && localData != null && localData.PrefabPath != inputDataHand.PrefabPath)
        {
            localSlot.Change(inputSlotHand);
            onUIChanged.Invoke(index);
            Debug.Log("(物品不同)交换物品槽位:" + index + " 物品:" + inputSlotHand._ItemData.PrefabPath);
            return;
        }
    }
    // 修改物品数量
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
                    localSlot.ResetData();
                }
                return true;
            }
        }
    }

    #endregion

    #region 插槽物品查询 和 设置
    // 设置单个物品
    public void SetOne_ItemData(int index, ItemData inputItemData)
    {
        Data.itemSlots[index]._ItemData = inputItemData;
    }
    //修改对应插槽的数量
    public void ChangeItemDataAmount(int index, float amount)
    {
        Data.itemSlots[index]._ItemData.Stack.Amount += amount;
    }
    // 获取指定索引的物品槽
    public ItemSlot GetItemSlot(int index)
    {
        if (index < 0 || index >= Data.itemSlots.Count)
        {
            return Data.itemSlots[0];
        }
        return Data.itemSlots[index];
    }

    public void SyncSlotBelongInventory()
    {
        foreach (var slot in Data.itemSlots)
        {
            slot.Belong_Inventory = this;
        }
    }

    // 获取指定索引的物品数据
    public ItemData GetItemData(int index)
    {
        return GetItemSlot(index)._ItemData;
    }
}
    #endregion