using FastCloner.Code;
using MemoryPack;
using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;

[Serializable]
[MemoryPackable]
public partial class Inventory_Data
{
    public string Name = string.Empty;                      // 背包名称
    public List<ItemSlot> itemSlots = new List<ItemSlot>(); // 物品槽列表
    [ReadOnly]
    public int Index = 0;                                   // 当前选中槽位索引
    public bool IsInjected = false;                         // 是否注入
    [ReadOnly]
    public Vector3 PanelPosition = Vector3.zero;            // 面板位置（用于持久化）
    public bool PanelIsOpen = true;
    // UI 开关按键绑定字段，让策划可以在编辑器中设置
    [Tooltip("UI面板开关Action名称，对应InputSystem中的Action Name")]
    public string ToggleActionName = "";

    [Tooltip("UI面板开关Action名称，对应InputSystem中的Action Name")]
    public string UIPrefabName = "";

    [MemoryPackIgnore]
    [FastClonerIgnore]
    public UltEvent<int> Event_RefreshUI = new(); // UI刷新事件

    [MemoryPackIgnore]
    [FastClonerIgnore]
    public UltEvent<ItemSlot> Event_OnBeforeDataChanged = new(); // 数据变更前事件

    [MemoryPackIgnore]
    [FastClonerIgnore]
    public UltEvent<ItemSlot> Event_OnDataChanged = new(); // 数据变更事件，传入发生变化的槽位

    // 数据变更事件（双槽位版本），用于需要同时知道本地槽位和输入槽位的场景
    [MemoryPackIgnore]
    [FastClonerIgnore]
    public UltEvent<ItemSlot, ItemSlot> Event_OnDataChanged_TwoSlots = new();

    [FastClonerIgnore]
    [MemoryPackIgnore]
    public bool IsFull => itemSlots.TrueForAll(slot => slot.itemData != null);

    [MemoryPackConstructor]
    public Inventory_Data(List<ItemSlot> itemSlots, string Name)
    {
        this.itemSlots = itemSlots;
        this.Name = Name;
        this.PanelPosition = Vector3.zero; // 初始化面板位置
        EnsureRuntimeEvents();
    }

    private void EnsureRuntimeEvents()
    {
        Event_RefreshUI ??= new UltEvent<int>();
        Event_OnBeforeDataChanged ??= new UltEvent<ItemSlot>();
        Event_OnDataChanged ??= new UltEvent<ItemSlot>();
        Event_OnDataChanged_TwoSlots ??= new UltEvent<ItemSlot, ItemSlot>();
    }

    #region 插槽操作逻辑

    public void RemoveItemAll(ItemSlot itemSlot, int index = 0)
    {
        EnsureRuntimeEvents();
        Event_OnBeforeDataChanged.Invoke(itemSlot);
        itemSlot.itemData = null;
        Event_RefreshUI.Invoke(index);
        Event_OnDataChanged.Invoke(itemSlot);
    }

    public void SetOne_ItemData(int index, ItemData inputItemData)
    {
        EnsureRuntimeEvents();
        Event_OnBeforeDataChanged.Invoke(itemSlots[index]);
        itemSlots[index].itemData = inputItemData;
        Event_OnDataChanged.Invoke(itemSlots[index]);
    }

    public ItemSlot GetItemSlot(int index)
    {
        if (index < 0 || index >= itemSlots.Count)
            return itemSlots[0];
        return itemSlots[index];
    }

    public void ChangeItemDataAmount(int index, float amount)
    {
        EnsureRuntimeEvents();
        Event_OnBeforeDataChanged.Invoke(itemSlots[index]);
        itemSlots[index].itemData.Stack.Amount += amount;
        Event_OnDataChanged.Invoke(itemSlots[index]);
    }

    #endregion

    #region 基础交互逻辑

    private ItemSlot GetOrCreateItemSlot(int index)
    {
        if (index < 0 || index >= itemSlots.Count)
        {
            Debug.LogError($"[Inventory_Data] 槽位索引超出范围: {index}, 槽位总数: {itemSlots.Count}");
            return null;
        }

        if (itemSlots[index] != null)
            return itemSlots[index];

        itemSlots[index] = new ItemSlot(index)
        {
            SlotMaxVolume = 100
        };
        Debug.LogError($"[Inventory_Data] 检测到空槽位引用，已在索引 {index} 处自动补齐 ItemSlot 实例");
        return itemSlots[index];
    }

    public void ChangeItemData_Default(int index, ItemSlot inputSlotHand)
    {
        EnsureRuntimeEvents();
        float rate = 1f;
        // 移除对 Belong_Inventory 的依赖，改为通过事件参数传递或其它方式获取
        // 如果需要获取来源背包信息，应该通过其他方式传入

        var localSlot = GetOrCreateItemSlot(index);
        if (localSlot == null)
            return;

        if (inputSlotHand == null)
        {
            Debug.LogError($"[Inventory_Data.ChangeItemData_Default] 输入槽位为空，index: {index}");
            return;
        }

        var localData = localSlot.itemData;
        var inputData = inputSlotHand.itemData;

        // 情况1：两个都为空
        if (localData == null && inputData == null) return;

        // 情况2：手有物体，本地空
        if (inputData != null && localData == null)
        {
            int changeAmount = Mathf.CeilToInt(inputData.Stack.Amount * rate);
            ChangeItemAmount(inputSlotHand, localSlot, changeAmount);

            Event_OnBeforeDataChanged.Invoke(localSlot);
            // 统一在交换完成后再触发事件
            Event_RefreshUI.Invoke(index);
            Event_OnDataChanged.Invoke(localSlot);
            Event_OnDataChanged_TwoSlots.Invoke(localSlot, inputSlotHand);
            return;
        }

        // 情况3：手空，本地有
        if (inputData == null && localData != null)
        {
            int changeAmount = Mathf.CeilToInt(localData.Stack.Amount * rate);
            ChangeItemAmount(localSlot, inputSlotHand, changeAmount);

            Event_OnBeforeDataChanged.Invoke(localSlot);
            Event_RefreshUI.Invoke(index);
            Event_OnDataChanged.Invoke(localSlot);
            Event_OnDataChanged_TwoSlots.Invoke(localSlot, inputSlotHand);
            return;
        }

        // 情况4：特殊交换（体积较大）
        if (inputData.Stack.Volume >= 2 || localData.Stack.Volume >= 2)
        {
            Event_OnBeforeDataChanged.Invoke(localSlot);
            localSlot.Change(inputSlotHand);
            Event_RefreshUI.Invoke(index);
            Event_OnDataChanged.Invoke(localSlot);
            Event_OnDataChanged_TwoSlots.Invoke(localSlot, inputSlotHand);
            return;
        }

        // 情况5：特殊交换（特殊数据不一致）
        if (!inputData.HasSameStackIdentity(localData) && inputData.IDName == localData.IDName)
        {
            Event_OnBeforeDataChanged.Invoke(localSlot);
            localSlot.Change(inputSlotHand);
            Event_RefreshUI.Invoke(index);
            Event_OnDataChanged.Invoke(localSlot);
            Event_OnDataChanged_TwoSlots.Invoke(localSlot, inputSlotHand);
            Debug.Log("特殊交换");
            return;
        }

        // 情况6：物品相同，堆叠交换
        if (inputData.CanStackWith(localData))
        {
            int changeAmount = Mathf.CeilToInt(localData.Stack.Amount * rate);
            ChangeItemAmount(localSlot, inputSlotHand, changeAmount);

            Event_OnBeforeDataChanged.Invoke(localSlot);
            Event_RefreshUI.Invoke(index);
            Event_OnDataChanged.Invoke(localSlot);
            Event_OnDataChanged_TwoSlots.Invoke(localSlot, inputSlotHand);
            return;
        }

        // 情况7：物品不同，直接交换
        localSlot.Change(inputSlotHand);
        Event_RefreshUI.Invoke(index);
        Event_OnBeforeDataChanged.Invoke(localSlot);
        Event_OnDataChanged.Invoke(localSlot);
        Event_OnDataChanged_TwoSlots.Invoke(localSlot, inputSlotHand);
        Debug.Log($"(物品不同)交换物品槽位:{index} 物品:{inputSlotHand.itemData.IDName}");
    }

    #region 触屏单件交互

    /// <summary>执行一次手机轻触的单件取放；同类物品可按当前交互方向取出或放入一件。</summary>
    public bool TouchTapItem(
        ItemSlot localSlot,
        Inventory_Data handInventory,
        ItemSlot handSlot,
        bool preferPickupSameType = false)
    {
        if (localSlot == null || handInventory == null || handSlot == null ||
            itemSlots == null || handInventory.itemSlots == null ||
            !itemSlots.Contains(localSlot) || !handInventory.itemSlots.Contains(handSlot) ||
            ReferenceEquals(localSlot, handSlot))
            return false;

        ItemData localData = localSlot.itemData;
        ItemData handData = handSlot.itemData;

        // 空手时从当前槽位取一件，不能把空槽当成可取来源。
        if (handData == null)
            return TransferItemQuantityTo(localSlot, handInventory, handSlot, 1);

        // 空槽只能接收手上物品的一件。
        if (localData == null)
            return handInventory.TransferItemQuantityTo(handSlot, this, localSlot, 1);

        // 同类槽按当前方向继续取出或放入一件。
        if (localData.CanStackWith(handData))
        {
            if (preferPickupSameType)
                return TransferItemQuantityTo(localSlot, handInventory, handSlot, 1);

            return handInventory.TransferItemQuantityTo(handSlot, this, localSlot, 1);
        }

        // 两种不同物品按整槽交换，避免异类物品被错误合并。
        return SwapItemSlotsTo(localSlot, handInventory, handSlot);
    }

    /// <summary>交换当前数据与另一个库存中的两个槽位，并通知双方库存监听器。</summary>
    private bool SwapItemSlotsTo(ItemSlot localSlot, Inventory_Data targetInventory, ItemSlot targetSlot)
    {
        if (targetInventory == null || targetInventory.itemSlots == null ||
            !targetInventory.itemSlots.Contains(targetSlot))
            return false;

        EnsureRuntimeEvents();
        targetInventory.EnsureRuntimeEvents();

        ItemData localData = localSlot.itemData;
        ItemData targetData = targetSlot.itemData;

        Event_OnBeforeDataChanged.Invoke(localSlot);
        targetInventory.Event_OnBeforeDataChanged.Invoke(targetSlot);

        localSlot.itemData = targetData;
        targetSlot.itemData = localData;

        localSlot.RefreshUI();
        targetSlot.RefreshUI();
        Event_RefreshUI.Invoke(localSlot.Index);
        targetInventory.Event_RefreshUI.Invoke(targetSlot.Index);
        Event_OnDataChanged.Invoke(localSlot);
        targetInventory.Event_OnDataChanged.Invoke(targetSlot);
        Event_OnDataChanged_TwoSlots.Invoke(localSlot, targetSlot);
        targetInventory.Event_OnDataChanged_TwoSlots.Invoke(targetSlot, localSlot);
        return true;
    }

    #endregion

    #region 拖拽放置

    /// <summary>将手持整组物品放入当前槽位：空槽放入、同类合并、异类交换；拖拽和长按整组放置共用。</summary>
    public bool DropDraggedItem(ItemSlot localSlot, Inventory_Data handInventory, ItemSlot handSlot)
    {
        if (localSlot == null || handInventory == null || handSlot == null ||
            itemSlots == null || handInventory.itemSlots == null ||
            !itemSlots.Contains(localSlot) || !handInventory.itemSlots.Contains(handSlot) ||
            ReferenceEquals(localSlot, handSlot))
            return false;

        ItemData handData = handSlot.itemData;
        if (handData?.Stack == null)
            return false;

        ItemData localData = localSlot.itemData;
        if (localData == null || localData.CanStackWith(handData))
        {
            int handAmount = Mathf.CeilToInt(handData.Stack.Amount);
            return handInventory.TransferItemQuantityTo(handSlot, this, localSlot, handAmount);
        }

        // 拖到不同物品上时保持原有拖拽交换语义。
        return SwapItemSlotsTo(localSlot, handInventory, handSlot);
    }

    #endregion

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
            !localSlot.itemData.HasSameStackIdentity(inputSlotHand.itemData))
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

        return changed > 0;
    }

    #endregion

    #region 添加与转移逻辑

    public ItemSlot FindFirstByTag(string tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName))
        {
            throw new ArgumentException("tagName 不能为空。", nameof(tagName));
        }

        foreach (var slot in itemSlots)
        {
            if (slot?.itemData?.Tags == null)
                continue;

            if (slot.itemData.Tags.ContainsTag(tagName))
                return slot;
        }

        return null;
    }

    [Obsolete("请改用 FindFirstByTag(tagName)。")]
    public ItemSlot FindItemByTagTypeAndTag(string tagType, string tagName)
    {
        return FindFirstByTag(tagName);
    }

    public bool TryAddItem(ItemData inputItemData, bool doAdd = true)
    {
        return TryAddItem(inputItemData, doAdd, out _);
    }

    /// <summary>尝试加入物品，并返回本次实际加入的数量；允许调用方正确处理容量不足时的剩余物品。</summary>
    public bool TryAddItem(ItemData inputItemData, bool doAdd, out float addedAmount)
    {
        addedAmount = 0f;
        if (inputItemData == null) return false;

        float unitVolume = inputItemData.Stack.Volume;
        float remainingAmount = inputItemData.Stack.Amount;
        float originalAmount = Mathf.Max(0f, remainingAmount);
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
                        inputItemData.Stack.CanBePickedUp = false;
                    }
                    addedAmount = originalAmount;
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
            bool sameItem = hasItem && slot.itemData.CanStackWith(inputItemData);

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
            }

            remainingAmount -= toAdd;
            addedAny = true;
        }

        addedAmount = Mathf.Max(0f, originalAmount - remainingAmount);
        if (doAdd && remainingAmount <= 0.0001f)
            inputItemData.Stack.CanBePickedUp = false;

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
        EnsureRuntimeEvents();

        if (!TryTransferItemQuantityCore(slotFrom, slotTo, upToCount))
            return false;

        // 兼容旧调用：调用方仍被视为两个槽位的共同事件所有者。
        Event_OnDataChanged.Invoke(slotFrom);
        Event_OnDataChanged.Invoke(slotTo);
        return true;
    }

    /// <summary>
    /// 在两个不同库存之间转移物品，并分别通知来源与目标库存。
    /// 快速转移等跨库存事务应使用此入口，避免来源库存的配方、装备等监听器漏更新。
    /// </summary>
    public bool TransferItemQuantityTo(
        ItemSlot slotFrom,
        Inventory_Data targetInventory,
        ItemSlot slotTo,
        int upToCount)
    {
        if (targetInventory == null)
            return false;

        if (ReferenceEquals(this, targetInventory))
            return TransferItemQuantity(slotFrom, slotTo, upToCount);

        EnsureRuntimeEvents();
        targetInventory.EnsureRuntimeEvents();

        if (itemSlots == null || targetInventory.itemSlots == null ||
            !itemSlots.Contains(slotFrom) || !targetInventory.itemSlots.Contains(slotTo))
        {
            Debug.LogError("[Inventory_Data] 跨库存转移的槽位不属于声明的来源或目标库存");
            return false;
        }

        if (!TryTransferItemQuantityCore(slotFrom, slotTo, upToCount))
            return false;

        Event_RefreshUI.Invoke(slotFrom.Index);
        targetInventory.Event_RefreshUI.Invoke(slotTo.Index);

        Event_OnDataChanged.Invoke(slotFrom);
        targetInventory.Event_OnDataChanged.Invoke(slotTo);
        Event_OnDataChanged_TwoSlots.Invoke(slotFrom, slotTo);
        targetInventory.Event_OnDataChanged_TwoSlots.Invoke(slotTo, slotFrom);
        return true;
    }

    private static bool TryTransferItemQuantityCore(ItemSlot slotFrom, ItemSlot slotTo, int upToCount)
    {
        if (slotFrom == null || slotTo == null || slotFrom == slotTo || upToCount <= 0)
            return false;

        var dataFrom = slotFrom.itemData;
        if (dataFrom?.Stack == null || dataFrom.Stack.Amount < 1f)
            return false;

        var dataTo = slotTo.itemData;
        if (dataTo != null && dataTo.Stack == null)
            return false;

        // 若目标槽位已有物品，需确保ID与特殊数据一致
        if (dataTo != null && !dataTo.HasSameStackIdentity(dataFrom))
            return false;

        // 若物品不可堆叠（Volume > 1），则不能进行堆叠式转移，只能直接移动单件到空槽
        if (dataFrom.Stack.Volume > 1)
        {
            // 非空槽位不能接收不可堆叠物品
            if (dataTo != null || slotTo.SlotMaxVolume < dataFrom.Stack.Volume)
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
                var newData = CloneForStackSplit(dataFrom);
                newData.Stack.Amount = 1;
                dataFrom.Stack.Amount -= 1;
                slotTo.itemData = newData;
            }

            slotFrom.RefreshUI();
            slotTo.RefreshUI();
            return true;
        }

        // 堆叠逻辑处理
        int availableSourceCount = Mathf.FloorToInt(dataFrom.Stack.Amount);
        int transferCount = Mathf.Min(upToCount, availableSourceCount);
        if (transferCount <= 0)
            return false;

        float unitVolume = dataFrom.Stack.Volume > 0f ? dataFrom.Stack.Volume : 1f;
        float targetCurrentVolume = dataTo?.Stack?.CurrentVolume ?? 0f;
        float availableTargetVolume = Mathf.Max(0f, slotTo.SlotMaxVolume - targetCurrentVolume);
        int targetCapacity = Mathf.FloorToInt((availableTargetVolume / unitVolume) + 0.0001f);
        transferCount = Mathf.Min(transferCount, targetCapacity);
        if (transferCount <= 0)
            return false;

        // 整堆移入空槽时直接搬迁引用，避免无意义克隆及重复实例标识。
        if (dataTo == null &&
            transferCount == availableSourceCount &&
            Mathf.Approximately(dataFrom.Stack.Amount, availableSourceCount))
        {
            slotTo.itemData = dataFrom;
            slotFrom.ClearData();
            slotFrom.RefreshUI();
            slotTo.RefreshUI();
            return true;
        }

        // 克隆一个转移对象，设置转移数量
        var transferData = CloneForStackSplit(dataFrom);
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

    private static ItemData CloneForStackSplit(ItemData source)
    {
        ItemData clone = FastCloner.FastCloner.DeepClone(source);
        int newGuid;
        do
        {
            newGuid = Guid.NewGuid().GetHashCode();
        }
        while (newGuid == 0 || newGuid == source.Guid);

        clone.Guid = newGuid;
        return clone;
    }


    #endregion

    public ItemData FindItemByTag(string tag)
    {
        foreach (var slot in itemSlots)
        {
            if (slot.itemData != null && slot.itemData.Tags.Contains(tag))
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

    public int GetItemCount()
    {
        int count = 0;
        foreach (var slot in itemSlots)
        {
            if (slot.itemData != null)
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// 使用背包默认规则整理物品：相同物品先合并堆叠，再按物品 ID、
    /// 特殊数据和实例 Guid 确定性排序，空槽统一移动到末尾。
    /// </summary>
    public bool SortDefault()
    {
        EnsureRuntimeEvents();

        if (itemSlots == null || itemSlots.Count == 0)
            return false;

        List<ItemData> items = new List<ItemData>(itemSlots.Count);
        for (int i = 0; i < itemSlots.Count; i++)
        {
            ItemData itemData = itemSlots[i]?.itemData;
            if (itemData == null)
                continue;

            if (itemData.Stack != null && itemData.Stack.Amount <= 0f)
                continue;

            items.Add(itemData);
        }

        if (items.Count == 0)
            return false;

        items.Sort(CompareItemsForDefaultSort);

        for (int i = 0; i < itemSlots.Count; i++)
        {
            ItemSlot slot = GetOrCreateItemSlot(i);
            if (slot == null)
                continue;

            Event_OnBeforeDataChanged.Invoke(slot);
            slot.itemData = null;
        }

        int occupiedSlotCount = 0;
        for (int itemIndex = 0; itemIndex < items.Count; itemIndex++)
        {
            ItemData source = items[itemIndex];
            if (source == null)
                continue;

            float remainingAmount = source.Stack != null ? source.Stack.Amount : 1f;
            if (CanUseDefaultStacking(source))
            {
                for (int slotIndex = 0; slotIndex < occupiedSlotCount && remainingAmount > 0f; slotIndex++)
                {
                    ItemSlot targetSlot = itemSlots[slotIndex];
                    ItemData target = targetSlot?.itemData;
                    if (!CanMergeForDefaultSort(source, target))
                        continue;

                    float availableAmount = GetAvailableStackAmount(targetSlot, target);
                    if (availableAmount <= 0f)
                        continue;

                    float movedAmount = Mathf.Min(remainingAmount, availableAmount);
                    target.Stack.Amount += movedAmount;
                    remainingAmount -= movedAmount;
                }
            }

            if (remainingAmount <= 0f)
                continue;

            if (occupiedSlotCount >= itemSlots.Count)
            {
                Debug.LogError("[Inventory_Data.SortDefault] 整理后槽位不足，已中止以避免丢失物品。");
                return false;
            }

            if (source.Stack != null)
                source.Stack.Amount = remainingAmount;
            itemSlots[occupiedSlotCount].itemData = source;
            occupiedSlotCount++;
        }

        for (int i = 0; i < itemSlots.Count; i++)
        {
            ItemSlot slot = itemSlots[i];
            slot.Index = i;
            slot.RefreshUI();
            Event_RefreshUI.Invoke(i);
            Event_OnDataChanged.Invoke(slot);
        }

        return true;
    }

    private static int CompareItemsForDefaultSort(ItemData left, ItemData right)
    {
        if (ReferenceEquals(left, right))
            return 0;
        if (left == null)
            return 1;
        if (right == null)
            return -1;

        int result = StringComparer.OrdinalIgnoreCase.Compare(left.IDName ?? string.Empty, right.IDName ?? string.Empty);
        if (result != 0)
            return result;

        result = StringComparer.Ordinal.Compare(left.ItemSpecialData ?? string.Empty, right.ItemSpecialData ?? string.Empty);
        if (result != 0)
            return result;

        result = StringComparer.OrdinalIgnoreCase.Compare(left.GameName ?? string.Empty, right.GameName ?? string.Empty);
        if (result != 0)
            return result;

        return left.Guid.CompareTo(right.Guid);
    }

    private static bool CanUseDefaultStacking(ItemData itemData)
    {
        return itemData?.Stack != null && itemData.Stack.Volume <= 1f;
    }

    private static bool CanMergeForDefaultSort(ItemData source, ItemData target)
    {
        return CanUseDefaultStacking(source) &&
               CanUseDefaultStacking(target) &&
               source.HasSameStackIdentity(target);
    }

    private static float GetAvailableStackAmount(ItemSlot slot, ItemData itemData)
    {
        if (slot == null || itemData?.Stack == null)
            return 0f;

        float unitVolume = itemData.Stack.Volume > 0f ? itemData.Stack.Volume : 1f;
        float availableVolume = Mathf.Max(0f, slot.SlotMaxVolume - itemData.Stack.CurrentVolume);
        return Mathf.Floor(availableVolume / unitVolume);
    }


}
