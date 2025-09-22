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

    [MemoryPackIgnore]
    [FastClonerIgnore]
    public UltEvent<int> Event_RefreshUI = new UltEvent<int>(); // UI刷新事件

    [FastClonerIgnore]
    public bool IsFull => itemSlots.TrueForAll(slot => slot.itemData != null);

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
        itemSlot.itemData = null;
        Event_RefreshUI.Invoke(index);
    }

    public void SetOne_ItemData(int index, ItemData inputItemData)
    {
        itemSlots[index].itemData = inputItemData;
    }

    public ItemSlot GetItemSlot(int index)
    {
        if (index < 0 || index >= itemSlots.Count)
            return itemSlots[0];
        return itemSlots[index];
    }

    public ItemData GetItemData(int index) => GetItemSlot(index).itemData;

    public void ChangeItemDataAmount(int index, float amount)
    {
        itemSlots[index].itemData.Stack.Amount += amount;
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
        if (inputData.Stack.Volume >= 2 || localData.Stack.Volume >= 2)
        {
            localSlot.Change(inputSlotHand);
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

        if (localSlot.itemData.ItemSpecialData != inputSlotHand.itemData.ItemSpecialData)
            return false;

        int changed = 0;

        while (changed < count &&
               localSlot.itemData.Stack.Amount > 0 &&
               inputSlotHand.itemData.Stack.Amount < inputSlotHand.SlotMaxVolume)
        {
            localSlot.itemData.Stack.Amount--;
            inputSlotHand.itemData.Stack.Amount++;
            changed++;
        }

        if (localSlot.itemData.Stack.Amount <= 0)
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
                if (itemSlots[i].itemData == null)
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
            bool hasItem = slot.itemData != null;
            bool sameItem = hasItem &&
                            slot.itemData.IDName == inputItemData.IDName &&
                            slot.itemData.ItemSpecialData == inputItemData.ItemSpecialData;

            if ((!hasItem && slot.IsFull) || (hasItem && (!sameItem || slot.IsFull)))
                continue;

            float currentVol = hasItem ? slot.itemData.Stack.CurrentVolume : 0f;
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
            slotTo.itemData = transferData;
        else
            dataTo.Stack.Amount += transferCount;

        slotFrom.RefreshUI();
        slotTo.RefreshUI();

        return true;
    }


    #endregion


    public ItemData FindItemByTag_First(string tag)
    {
        foreach (var slot in itemSlots)
        {
            if(slot.itemData!= null && slot.itemData.ItemTags.HasTypeTag(tag))
            {
                return slot.itemData;
            }
        }
        return null;
    }
    public List<ItemData> FindItemByTag_All(string tag)
    {
        List<ItemData> result = new List<ItemData>();
        foreach (var slot in itemSlots)
        {
            if (slot.itemData!= null && slot.itemData.ItemTags.HasTypeTag(tag))
            {
                result.Add(slot.itemData);
            }
        }

        return result;
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

    /// <summary>
    /// 根据Prefab注入ItemData到指定的Slot中
    /// </summary>
    /// <param name="prefab">物品预制体，必须包含Item组件</param>
    /// <param name="count">物品数量</param>
    /// <param name="index">要注入的槽位索引</param>
    [Button("注入物品到槽位")] // Odin特性：在Inspector中显示为按钮
    [LabelText("注入物品")] // Odin特性：自定义标签文本
    public void InjectItemData(
        [LabelText("物品预制体")] GameObject prefab, 
        [LabelText("数量")] [MinValue(1)] int count, 
        [LabelText("槽位索引")] [MinValue(0)] int index)
    {
        // 参数验证
        if (prefab == null)
        {
            Debug.LogError("注入失败：Prefab不能为空");
            return;
        }

        if (index < 0 || index >= itemSlots.Count)
        {
            Debug.LogError($"注入失败：索引 {index} 超出范围 [0, {itemSlots.Count - 1}]");
            return;
        }

        // 获取Prefab上的Item组件
        Item itemComponent = prefab.GetComponent<Item>();
        if (itemComponent == null)
        {
            Debug.LogError($"注入失败：Prefab {prefab.name} 上找不到Item组件");
            return;
        }
         // 确保ItemData已经初始化完毕
        // 克隆ItemData
        ItemData clonedItemData = itemComponent.IsPrefabInit();
        if (clonedItemData == null)
        {
            Debug.LogError($"注入失败：无法克隆 {prefab.name} 的ItemData");
            return;
        }

        // 设置数量
        clonedItemData.Stack.Amount = count;
        
        // 注入到指定槽位
        SetOne_ItemData(index, clonedItemData);
        
        // 触发UI刷新
        Event_RefreshUI.Invoke(index);
        
        Debug.Log($"成功注入物品 {prefab.name} x{count} 到槽位 {index}");
    }
    public void RandomOrderAutoInjectItemDataList(List<GameObject> prefabList, List<int> countList)
    {
        if (prefabList == null || countList == null) return;
        if (prefabList.Count != countList.Count) return;

        // --- Step1: 打乱物品顺序 ---
        List<int> itemIndices = Enumerable.Range(0, prefabList.Count).ToList();
        for (int i = itemIndices.Count - 1; i > 0; i--)
        {
            int r = Random.Range(0, i + 1);
            (itemIndices[i], itemIndices[r]) = (itemIndices[r], itemIndices[i]);
        }

        // --- Step2: 收集所有空槽位并打乱 ---
        List<int> emptySlots = new List<int>();
        for (int i = 0; i < itemSlots.Count; i++)
        {
            if (itemSlots[i].itemData == null)
                emptySlots.Add(i);
        }
        for (int i = emptySlots.Count - 1; i > 0; i--)
        {
            int r = Random.Range(0, i + 1);
            (emptySlots[i], emptySlots[r]) = (emptySlots[r], emptySlots[i]);
        }

        // --- Step3: 按随机顺序把物品塞进随机槽位 ---
        int successCount = 0;
        int failCount = 0;

        for (int i = 0; i < itemIndices.Count && i < emptySlots.Count; i++)
        {
            int randomItemIndex = itemIndices[i];
            int slotIndex = emptySlots[i];

            GameObject prefab = prefabList[randomItemIndex];
            int count = countList[randomItemIndex];

            if (prefab == null || count <= 0) { failCount++; continue; }

            var itemComp = prefab.GetComponent<Item>();
            if (itemComp == null) { failCount++; continue; }

            var itemData = itemComp.IsPrefabInit();
            if (itemData == null) { failCount++; continue; }

            itemData.Stack.Amount = count;
            itemData.Stack.CanBePickedUp = false;

            SetOne_ItemData(slotIndex, itemData);
            Event_RefreshUI.Invoke(slotIndex);

            Debug.Log($"放置 {prefab.name} x{count} 到随机槽位 {slotIndex}");
            successCount++;
        }

        Debug.Log($"随机注入完成：成功 {successCount}，失败 {failCount}");
    }


    /// <summary>
    /// 自动注入物品列表到容器中，智能查找空槽位或可堆叠槽位，避免覆盖已有物品
    /// </summary>
    /// <param name="prefabList">物品预制体列表</param>
    /// <param name="countList">对应物品数量列表</param>
    [Button("自动注入物品列表")] // Odin特性：在Inspector中显示为按钮
[LabelText("自动注入物品列表")] // Odin特性：自定义标签文本
public void AutoInjectItemDataList(
    [LabelText("物品预制体列表")] List<GameObject> prefabList, 
    [LabelText("数量列表")] List<int> countList)
{
    // 参数验证
    if (prefabList == null || countList == null)
    {
        Debug.LogError("自动注入失败：Prefab列表或数量列表不能为空");
        return;
    }

    if (prefabList.Count != countList.Count)
    {
        Debug.LogError($"自动注入失败：Prefab列表数量({prefabList.Count})与数量列表数量({countList.Count})不匹配");
        return;
    }

    if (prefabList.Count == 0)
    {
        Debug.LogWarning("自动注入失败：Prefab列表为空");
        return;
    }

    int successCount = 0;
    int failCount = 0;

    // 遍历并自动注入每个物品
    for (int i = 0; i < prefabList.Count; i++)
    {
        GameObject prefab = prefabList[i];
        int count = countList[i];

        if (prefab == null)
        {
            Debug.LogWarning($"跳过空的Prefab（索引 {i}）");
            failCount++;
            continue;
        }

        if (count <= 0)
        {
            Debug.LogWarning($"跳过无效数量 {count} 的物品 {prefab.name}（索引 {i}）");
            failCount++;
            continue;
        }

        // 获取Prefab上的Item组件
        Item itemComponent = prefab.GetComponent<Item>();
        if (itemComponent == null)
        {
            Debug.LogError($"自动注入失败：Prefab {prefab.name} 上找不到Item组件（索引 {i}）");
            failCount++;
            continue;
        }

        // 克隆ItemData
        ItemData itemData = itemComponent.IsPrefabInit();
        if (itemData == null)
        {
            Debug.LogError($"自动注入失败：无法克隆 {prefab.name} 的ItemData（索引 {i}）");
            failCount++;
            continue;
        }

        // 设置数量
        itemData.Stack.Amount = count;
        itemData.Stack.CanBePickedUp = false;

        // 尝试添加物品
        if (TryAddItem(itemData, true))
        {
            Debug.Log($"成功自动注入物品 {prefab.name} x{count}");
            successCount++;
        }
        else
        {
            Debug.LogError($"自动注入失败：容器空间不足，无法注入物品 {prefab.name} x{count}");
            failCount++;
        }
    }

    Debug.Log($"自动注入物品列表完成：成功 {successCount} 个，失败 {failCount} 个");
}

// 重载方法：支持统一数量
[Button("自动注入物品列表(统一数量)")]
[LabelText("自动注入物品列表(统一数量)")]
public void AutoInjectItemDataList(
    [LabelText("物品预制体列表")] List<GameObject> prefabList, 
    [LabelText("统一数量")] [MinValue(1)] int uniformCount = 1)
{
    if (prefabList == null)
    {
        Debug.LogError("自动注入失败：Prefab列表不能为空");
        return;
    }

    // 创建统一数量列表
    List<int> countList = new List<int>();
    for (int i = 0; i < prefabList.Count; i++)
    {
        countList.Add(uniformCount);
    }

    AutoInjectItemDataList(prefabList, countList);
}
}