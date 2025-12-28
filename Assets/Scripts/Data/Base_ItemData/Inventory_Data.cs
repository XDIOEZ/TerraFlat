using FastCloner.Code;
using Force.DeepCloner;
using MemoryPack;
using System;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;
using Sirenix.OdinInspector;
using Random = UnityEngine.Random;
using System.Linq; // 添加Odin引用

[Serializable]
[MemoryPackable]
public partial class Inventory_Data
{
    //TODO 设置Event - 已完成：Event_RefreshUI就是用于UI刷新的事件
    public string Name = string.Empty;                      // 背包名称
    public List<ItemSlot> itemSlots = new List<ItemSlot>(); // 物品槽列表
    public int Index = 0;                      // 当前选中槽位索引
    public bool IsInjected = false;            // 是否注入
    public Vector3 PanelPosition = Vector3.zero;            // 面板位置（用于持久化）
    public bool PanelIsOpen = true;              // 面板是否打开（用于持久化）

    [MemoryPackIgnore]
    [FastClonerIgnore]
    public UltEvent<int> Event_RefreshUI = new UltEvent<int>(); // UI刷新事件

    [MemoryPackIgnore]
    [FastClonerIgnore]
    public UltEvent OnDataChanged = new UltEvent(); // 数据变更事件

    [FastClonerIgnore]
    public bool IsFull => itemSlots.TrueForAll(slot => slot.itemData != null);

    // 构造函数
    [MemoryPackConstructor]
    public Inventory_Data(List<ItemSlot> itemSlots, string Name)
    {
        this.itemSlots = itemSlots;
        this.Name = Name;
        this.PanelPosition = Vector3.zero; // 初始化面板位置
    }

    #region 插槽操作逻辑

    public void RemoveItemAll(ItemSlot itemSlot, int index = 0)
    {
        itemSlot.itemData = null;
        Event_RefreshUI.Invoke(index);
        OnDataChanged.Invoke();
    }

    public void SetOne_ItemData(int index, ItemData inputItemData)
    {
        itemSlots[index].itemData = inputItemData;
        OnDataChanged.Invoke();
    }

    public ItemSlot GetItemSlot(int index)
    {
        if (index < 0 || index >= itemSlots.Count)
            return itemSlots[0];
        return itemSlots[index];
    }

    public void ChangeItemDataAmount(int index, float amount)
    {
        itemSlots[index].itemData.Stack.Amount += amount;
        OnDataChanged.Invoke();
    }

    #endregion

    #region 基础交互逻辑

    public void ChangeItemData_Default(int index, ItemSlot inputSlotHand)
    {
        float rate = 1f;
        // 移除对 Belong_Inventory 的依赖，改为通过事件参数传递或其它方式获取
        // 如果需要获取来源背包信息，应该通过其他方式传入

        var localSlot = itemSlots[index];
        var localData = localSlot.itemData;
        var inputData = inputSlotHand.itemData;

        // 情况1：两个都为空
        if (localData == null && inputData == null) return;

        // 情况2：手有物体，本地空
        if (inputData != null && localData == null)
        {
            int changeAmount = Mathf.CeilToInt(inputData.Stack.Amount * rate);
            ChangeItemAmount(inputSlotHand, localSlot, changeAmount);
            Event_RefreshUI.Invoke(index);
            OnDataChanged.Invoke();
            return;
        }

        // 情况3：手空，本地有
        if (inputData == null && localData != null)
        {
            int changeAmount = Mathf.CeilToInt(localData.Stack.Amount * rate);
            ChangeItemAmount(localSlot, inputSlotHand, changeAmount);
            Event_RefreshUI.Invoke(index);
            OnDataChanged.Invoke();
            return;
        }

        // 情况4：特殊交换（特殊数据不一致）
        if (inputData.Stack.Volume >= 2 || localData.Stack.Volume >= 2)
        {
            localSlot.Change(inputSlotHand);
            Event_RefreshUI.Invoke(index);
            OnDataChanged.Invoke();
            return;
        }

        // 情况4：特殊交换（特殊数据不一致）
        if (inputData.ItemSpecialData != localData.ItemSpecialData)
        {
            localSlot.Change(inputSlotHand);
            Event_RefreshUI.Invoke(index);
            OnDataChanged.Invoke();
            Debug.Log("特殊交换");
            return;
        }

        // 情况5：物品相同，堆叠交换
        if (inputData.IDName == localData.IDName)
        {
            int changeAmount = Mathf.CeilToInt(localData.Stack.Amount * rate);
            ChangeItemAmount(localSlot, inputSlotHand, changeAmount);
            Event_RefreshUI.Invoke(index);
            OnDataChanged.Invoke();
            return;
        }

        // 情况6：物品不同，直接交换
        localSlot.Change(inputSlotHand);
        Event_RefreshUI.Invoke(index);
        OnDataChanged.Invoke();
        Debug.Log($"(物品不同)交换物品槽位:{index} 物品:{inputSlotHand.itemData.IDName}");
    }

    public bool ChangeItemAmount(ItemSlot localSlot, ItemSlot inputSlotHand, int count)
    {
        if (inputSlotHand.itemData == null)
        {
            var tempData = FastCloner.FastCloner.DeepClone(localSlot.itemData);
            tempData.Stack.Amount = 0;
            inputSlotHand.itemData = tempData;
        }

        // 确保两个物品的特殊数据一致
        if (localSlot.itemData != null &&
            localSlot.itemData.ItemSpecialData != inputSlotHand.itemData.ItemSpecialData)
            return false;

        int changed = 0;

        while (changed < count &&
               localSlot.itemData != null &&
               localSlot.itemData.Stack.Amount > 0 &&
               inputSlotHand.itemData.Stack.Amount < inputSlotHand.SlotMaxVolume)
        {
            localSlot.itemData.Stack.Amount--;
            inputSlotHand.itemData.Stack.Amount++;
            changed++;
        }

        if (localSlot.itemData != null && localSlot.itemData.Stack.Amount <= 0)
            localSlot.ClearData();

        if (changed > 0)
            OnDataChanged.Invoke();

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
                if (itemSlots[i].itemData == null)
                {
                    if (doAdd)
                    {
                        SetOne_ItemData(i, inputItemData);
                        Event_RefreshUI.Invoke(i);
                        OnDataChanged.Invoke();
                        inputItemData.Stack.CanBePickedUp = false;
                    }
                    return true;
                }
            }
            return false;
        }

        // 堆叠物品（体积为1）
        // 优先填充已有的同类堆叠槽位，其次才占用新的空槽位

        // 第一轮：只尝试向已有的同类物品堆叠
        for (int i = 0; i < itemSlots.Count && remainingAmount > 0; i++)
        {
            var slot = itemSlots[i];
            bool hasItem = slot.itemData != null;
            bool sameItem = hasItem &&
                            slot.itemData.IDName == inputItemData.IDName &&
                            slot.itemData.ItemSpecialData == inputItemData.ItemSpecialData;

            // 仅处理已有、同类、且未满的堆叠
            if (!hasItem || !sameItem || slot.IsFull)
                continue;

            float currentVol = slot.itemData.Stack.CurrentVolume;
            float canAdd = slot.SlotMaxVolume - currentVol;
            float toAdd = Mathf.Min(remainingAmount, canAdd);
            if (toAdd <= 0f) continue;

            if (doAdd)
            {
                ChangeItemDataAmount(i, toAdd);
                Event_RefreshUI.Invoke(i);
                OnDataChanged.Invoke();
            }

            remainingAmount -= toAdd;
            addedAny = true;
        }

        // 第二轮：若还有剩余数量，再找空槽位放入（创建新堆叠）
        for (int i = 0; i < itemSlots.Count && remainingAmount > 0; i++)
        {
            var slot = itemSlots[i];
            bool hasItem = slot.itemData != null;

            // 只处理空槽位，且槽位本身未被标记为满
            if (hasItem || slot.IsFull)
                continue;

            float currentVol = 0f;
            float canAdd = slot.SlotMaxVolume - currentVol;
            float toAdd = Mathf.Min(remainingAmount, canAdd);
            if (toAdd <= 0f) continue;

            if (doAdd)
            {
                var newItem = FastCloner.FastCloner.DeepClone(inputItemData);
                newItem.Stack.Amount = toAdd;
                SetOne_ItemData(i, newItem);
                Event_RefreshUI.Invoke(i);
                OnDataChanged.Invoke();
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

        var dataFrom = slotFrom.itemData;
        if (dataFrom == null || dataFrom.Stack.Amount <= 0)
            return false;

        var dataTo = slotTo.itemData;

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
                slotTo.itemData = dataFrom;
                slotFrom.ClearData();
            }
            else
            {
                // 从原数据中复制出一个新对象
                var newData = dataFrom.DeepClone();
                newData.Stack.Amount = 1;
                dataFrom.Stack.Amount -= 1;
                slotTo.itemData = newData;
            }

            slotFrom.RefreshUI();
            slotTo.RefreshUI();
            OnDataChanged.Invoke();
            return true;
        }

        // 堆叠逻辑处理
        int transferCount = Mathf.Min(upToCount, (int)dataFrom.Stack.Amount);

        // 检查目标槽位是否能容纳转移的物品数量
        if (dataTo != null)
        {
            // 计算转移后目标槽位的总数量
            float targetTotalAmount = dataTo.Stack.Amount + transferCount;
            if (targetTotalAmount > slotTo.SlotMaxVolume)
            {
                // 如果会超出上限，则计算实际可转移的数量
                transferCount = Mathf.FloorToInt(slotTo.SlotMaxVolume - dataTo.Stack.Amount);
                if (transferCount <= 0)
                    return false; // 目标槽位已满，无法转移
            }
        }
        else
        {
            // 目标槽位为空，检查要转移的数量是否超出槽位上限
            if (transferCount > slotTo.SlotMaxVolume)
            {
                transferCount = Mathf.FloorToInt(slotTo.SlotMaxVolume);
                if (transferCount <= 0)
                    return false; // 槽位上限为0或负数
            }
        }

        // 克隆一个转移对象，设置转移数量
        var transferData = dataFrom.DeepClone();
        transferData.Stack.Amount = transferCount;

        // 扣除来源物品数量
        dataFrom.Stack.Amount -= transferCount;
        if (dataFrom.Stack.Amount <= 0)
            slotFrom.ClearData();

        // 如果目标为空，直接赋值，否则叠加数量
        if (dataTo == null)
            slotTo.itemData = transferData;
        else
            dataTo.Stack.Amount += transferCount;

        slotFrom.RefreshUI();
        slotTo.RefreshUI();

        OnDataChanged.Invoke();

        return true;
    }


    #endregion

    public ItemData FindItemByTagTypeAndTag(string tagType, string tag)
    {
        foreach (var slot in itemSlots)
        {
            if (slot.itemData != null && slot.itemData.Tags.HasTypeTag(tagType, tag))
            {
                return slot.itemData;
            }
        }
        return null;
    }

    public ModuleData GetModuleByID(string ID)
    {
        foreach (var slot in itemSlots)
        {
            if (slot.itemData != null)
            {
                var moduledata = slot.itemData.GetModuleData_Frist(ID);
                if (moduledata != null)
                {
                    return moduledata;
                }
            }
        }
        return null;
    }
    //TODO 增加根据ID获取物品的方法 - 已完成
    public ItemSlot GetItemSlotByModuleID(string moduleID)
    {
        foreach (var slot in itemSlots)
        {
            if (slot.itemData != null)
            {
                var module = slot.itemData.GetModuleData_Frist(moduleID); // 你已有的方法
                if (module != null)
                {
                    return slot;
                }
            }
        }
        return null;
    }


}