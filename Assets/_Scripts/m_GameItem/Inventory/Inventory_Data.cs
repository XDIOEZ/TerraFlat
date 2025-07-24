using FastCloner.Code;
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
    //TODO 设置Event
    public string Name = string.Empty;                      // 背包名称
    public List<ItemSlot> itemSlots = new List<ItemSlot>(); // 物品槽列表
    public int Index = 0;                      // 当前选中槽位索引

    [MemoryPackIgnore]
    [FastClonerIgnore]
    public UltEvent<int> Event_RefreshUI = new UltEvent<int>(); // UI刷新事件

    [FastClonerIgnore]
    public bool IsFull => itemSlots.TrueForAll(slot => slot._ItemData != null);

    // 构造函数
    [MemoryPackConstructor]
    public Inventory_Data(List<ItemSlot> itemSlots, string Name)
    {
        this.itemSlots = itemSlots;
        this.Name = Name;
    }

    #region 插槽操作逻辑

    public void RemoveItemAll(ItemSlot itemSlot, int index = 0)
    {
        itemSlot._ItemData = null;
        Event_RefreshUI.Invoke(index);
    }

    public void SetOne_ItemData(int index, ItemData inputItemData)
    {
        itemSlots[index]._ItemData = inputItemData;
    }

    public ItemSlot GetItemSlot(int index)
    {
        if (index < 0 || index >= itemSlots.Count)
            return itemSlots[0];
        return itemSlots[index];
    }

    public ItemData GetItemData(int index) => GetItemSlot(index)._ItemData;

    public void ChangeItemDataAmount(int index, float amount)
    {
        itemSlots[index]._ItemData.Stack.Amount += amount;
    }

    #endregion

    #region 基础交互逻辑

    public void ChangeItemData_Default(int index, ItemSlot inputSlotHand)
    {
        float rate = 1f;
        if (inputSlotHand.Belong_Inventory == null)
        {
            return;
        }
        var handInventory = inputSlotHand.Belong_Inventory.GetComponent<Inventory_Hand>();
        if (handInventory != null)
            rate = handInventory.GetItemAmountRate;

        var localSlot = itemSlots[index];
        var localData = localSlot._ItemData;
        var inputData = inputSlotHand._ItemData;

        // 情况1：两个都为空
        if (localData == null && inputData == null) return;

        // 情况2：手有物体，本地空
        if (inputData != null && localData == null)
        {
            int changeAmount = Mathf.CeilToInt(inputData.Stack.Amount * rate);
            ChangeItemAmount(inputSlotHand, localSlot, changeAmount);
            Event_RefreshUI.Invoke(index);
            return;
        }

        // 情况3：手空，本地有
        if (inputData == null && localData != null)
        {
            int changeAmount = Mathf.CeilToInt(localData.Stack.Amount * rate);
            ChangeItemAmount(localSlot, inputSlotHand, changeAmount);
            Event_RefreshUI.Invoke(index);
            return;
        }

        // 情况4：特殊交换（特殊数据不一致）
        if (inputData.ItemSpecialData != localData.ItemSpecialData)
        {
            localSlot.Change(inputSlotHand);
            Event_RefreshUI.Invoke(index);
            Debug.Log("特殊交换");
            return;
        }

        // 情况5：物品相同，堆叠交换
        if (inputData.IDName == localData.IDName)
        {
            int changeAmount = Mathf.CeilToInt(localData.Stack.Amount * rate);
            ChangeItemAmount(localSlot, inputSlotHand, changeAmount);
            Event_RefreshUI.Invoke(index);
            return;
        }

        // 情况6：物品不同，直接交换
        localSlot.Change(inputSlotHand);
        Event_RefreshUI.Invoke(index);
        Debug.Log($"(物品不同)交换物品槽位:{index} 物品:{inputSlotHand._ItemData.IDName}");
    }

    public bool ChangeItemAmount(ItemSlot localSlot, ItemSlot inputSlotHand, int count)
    {
        if (inputSlotHand._ItemData == null)
        {
            var tempData = FastCloner.FastCloner.DeepClone(localSlot._ItemData);
            tempData.Stack.Amount = 0;
            inputSlotHand._ItemData = tempData;
        }

        if (localSlot._ItemData.ItemSpecialData != inputSlotHand._ItemData.ItemSpecialData)
            return false;

        int changed = 0;

        while (changed < count &&
               localSlot._ItemData.Stack.Amount > 0 &&
               inputSlotHand._ItemData.Stack.Amount < inputSlotHand.SlotMaxVolume)
        {
            localSlot._ItemData.Stack.Amount--;
            inputSlotHand._ItemData.Stack.Amount++;
            changed++;
        }

        if (localSlot._ItemData.Stack.Amount <= 0)
            localSlot.ClearData();

        return changed > 0;
    }

    #endregion

    #region 添加与转移逻辑

    public bool TryAddItem(ItemData inputItemData, bool doAdd = true)
    {
        if (inputItemData == null) return false;

        float unitVolume = inputItemData.Stack.Volume;
        float remainingAmount = inputItemData.Stack.Amount;
        bool addedAny = false;

        // 非堆叠物品（体积大于1）
        if (unitVolume > 1)
        {
            for (int i = 0; i < itemSlots.Count; i++)
            {
                if (itemSlots[i]._ItemData == null)
                {
                    if (doAdd)
                    {
                        SetOne_ItemData(i,inputItemData);
                        Event_RefreshUI.Invoke(i);
                        inputItemData.Stack.CanBePickedUp = false;
                    }
                    return true;
                }
            }
            return false;
        }

        // 堆叠物品（体积为1）
        for (int i = 0; i < itemSlots.Count && remainingAmount > 0; i++)
        {
            var slot = itemSlots[i];
            bool hasItem = slot._ItemData != null;
            bool sameItem = hasItem &&
                            slot._ItemData.IDName == inputItemData.IDName &&
                            slot._ItemData.ItemSpecialData == inputItemData.ItemSpecialData;

            if ((!hasItem && slot.IsFull) || (hasItem && (!sameItem || slot.IsFull)))
                continue;

            float currentVol = hasItem ? slot._ItemData.Stack.CurrentVolume : 0f;
            float canAdd = slot.SlotMaxVolume - currentVol;
            float toAdd = Mathf.Min(remainingAmount, canAdd);
            if (toAdd <= 0f) continue;

            if (doAdd)
            {
                if (hasItem)
                    ChangeItemDataAmount(i, toAdd);
                else
                {
                    var newItem = FastCloner.FastCloner.DeepClone(inputItemData);
                    newItem.Stack.Amount = toAdd;
                    SetOne_ItemData(i, newItem);
                }
                Event_RefreshUI.Invoke(i);
            }

            remainingAmount -= toAdd;
            addedAny = true;
        }

        if (doAdd)
        {
            inputItemData.Stack.CanBePickedUp = false;
            if (remainingAmount > 0)
                Debug.LogWarning($"物品添加未完全完成，剩余 {remainingAmount} 个未添加。");
        }

        return addedAny;
    }

    /// <summary>
    /// 在两个物品槽之间转移指定数量（upToCount）的物品。
    /// 转移逻辑包括以下检查：
    /// - 两个槽位有效，且不相同
    /// - 来源槽位有物品，且数量充足
    /// - 如果目标槽位已有物品，则其类型与来源物品一致（包括特殊数据）
    /// - 若物品不可堆叠（Volume > 1），则不能合并，必须空槽才允许转移
    /// - 转移后自动更新 UI 和数据
    /// </summary>
    public bool TransferItemQuantity(ItemSlot slotFrom, ItemSlot slotTo, int upToCount)
    {
        if (slotFrom == null || slotTo == null || slotFrom == slotTo || upToCount <= 0)
            return false;

        var dataFrom = slotFrom._ItemData;
        if (dataFrom == null || dataFrom.Stack.Amount <= 0)
            return false;

        var dataTo = slotTo._ItemData;

        // 若目标槽位已有物品，需确保ID与特殊数据一致
        if (dataTo != null &&
            (dataTo.IDName != dataFrom.IDName || dataTo.ItemSpecialData != dataFrom.ItemSpecialData))
            return false;

        // 若物品不可堆叠（Volume > 1），则不能进行堆叠式转移，只能直接移动单件到空槽
        if (dataFrom.Stack.Volume > 1)
        {
            // 非空槽位不能接收不可堆叠物品
            if (dataTo != null)
                return false;

            // 只允许转移一个
            var singleData = dataFrom;
            if (dataFrom.Stack.Amount == 1)
            {
                // 直接搬迁引用，不用 Clone（减少 GC）
                slotTo._ItemData = dataFrom;
                slotFrom.ClearData();
            }
            else
            {
                // 从原数据中复制出一个新对象
                var newData = dataFrom.DeepClone();
                newData.Stack.Amount = 1;
                dataFrom.Stack.Amount -= 1;
                slotTo._ItemData = newData;
            }

            slotFrom.RefreshUI();
            slotTo.RefreshUI();
            return true;
        }

        // 堆叠逻辑处理
        int transferCount = Mathf.Min(upToCount, (int)dataFrom.Stack.Amount);

        // 克隆一个转移对象，设置转移数量
        var transferData = dataFrom.DeepClone();
        transferData.Stack.Amount = transferCount;

        // 扣除来源物品数量
        dataFrom.Stack.Amount -= transferCount;
        if (dataFrom.Stack.Amount <= 0)
            slotFrom.ClearData();

        // 如果目标为空，直接赋值，否则叠加数量
        if (dataTo == null)
            slotTo._ItemData = transferData;
        else
            dataTo.Stack.Amount += transferCount;

        slotFrom.RefreshUI();
        slotTo.RefreshUI();

        return true;
    }


    #endregion
}
