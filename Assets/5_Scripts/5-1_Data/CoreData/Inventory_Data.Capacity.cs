using FastCloner.Code;
using MemoryPack;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>库存容量策略：槽位扩容、单格堆叠以及重量/体积总容量统一在这里维护。</summary>
public partial class Inventory_Data
{
    #region 动态槽位容量

    /// <summary>普通库存单格默认最多 100 件；字段名沿用旧 SlotMaxVolume 以避免破坏现有 Prefab。</summary>
    public const float DefaultSlotVolume = 100f;

    /// <summary>玩家普通主背包的基础重量上限，单位 kg。</summary>
    public const float DefaultPlayerBagMaxWeight = 60f;

    /// <summary>玩家普通主背包的基础体积上限，单位 L。</summary>
    public const float DefaultPlayerBagMaxVolume = 90f;

    /// <summary>玩家主背包的基础槽位数；自动收缩时不会低于该数量。</summary>
    public const int DefaultPlayerBagSlotCount = 27;

    /// <summary>空余槽位小于等于该数量时触发扩容。</summary>
    public const int PlayerBagExpandFreeSlotThreshold = 2;

    /// <summary>触发扩容后一次性补足到该空余槽位数，避免逐帧连续扩容。</summary>
    public const int PlayerBagTargetFreeSlotCount = PlayerBagExpandFreeSlotThreshold + 1;

    /// <summary>运行时容量策略；由所属玩家的独立状态恢复，不加入 MemoryPack 库存布局。</summary>
    [MemoryPackIgnore, FastClonerIgnore, JsonIgnore]
    public bool HasUnlimitedSlots { get; private set; }

    /// <summary>玩家主背包、手部槽和快捷栏允许可堆叠物品在单格内无限叠加。</summary>
    [MemoryPackIgnore, FastClonerIgnore, JsonIgnore]
    public bool HasUnlimitedStackSize { get; private set; }

    /// <summary>是否启用整包重量/体积约束；普通容器默认仍只受槽位规则约束。</summary>
    [MemoryPackIgnore, FastClonerIgnore, JsonIgnore]
    public bool HasCarryCapacity { get; private set; }

    /// <summary>创造背包绕过重量与体积上限，但仍保留物品自身的可堆叠属性。</summary>
    [MemoryPackIgnore, FastClonerIgnore, JsonIgnore]
    public bool HasUnlimitedCarryCapacity { get; private set; }

    [MemoryPackIgnore, FastClonerIgnore, JsonIgnore]
    public float MaxCarryWeight { get; private set; } = float.PositiveInfinity;

    [MemoryPackIgnore, FastClonerIgnore, JsonIgnore]
    public float MaxCarryVolume { get; private set; } = float.PositiveInfinity;

    /// <summary>只有玩家主背包启用“27 格基线 + 3 个预留空格”的动态收缩策略。</summary>
    [MemoryPackIgnore, FastClonerIgnore, JsonIgnore]
    private bool UsesPlayerBagDynamicSlotPolicy { get; set; }

    /// <summary>当前库存内所有物品的总重量。</summary>
    [MemoryPackIgnore, FastClonerIgnore, JsonIgnore]
    public float CurrentCarryWeight => CalculateCarryWeight();

    /// <summary>当前库存内所有物品的总体积。</summary>
    [MemoryPackIgnore, FastClonerIgnore, JsonIgnore]
    public float CurrentCarryVolume => CalculateCarryVolume();

    /// <summary>配置普通玩家主背包：格子自动扩容、可堆叠物单格无限，但总携带量受重量与体积双上限约束。</summary>
    public void ConfigurePlayerBagCapacity(float maxWeight, float maxVolume)
    {
        UsesPlayerBagDynamicSlotPolicy = true;
        SetUnlimitedSlots(true);
        SetUnlimitedStackSize(true);
        SetCarryCapacity(maxWeight, maxVolume);
    }

    /// <summary>普通容器恢复为只使用槽位规则，不参与玩家携带容量计算。</summary>
    public void ClearCarryCapacity()
    {
        HasCarryCapacity = false;
        HasUnlimitedCarryCapacity = false;
        MaxCarryWeight = float.PositiveInfinity;
        MaxCarryVolume = float.PositiveInfinity;
    }

    /// <summary>设置整包重量/体积上限。</summary>
    public void SetCarryCapacity(float maxWeight, float maxVolume)
    {
        MaxCarryWeight = Mathf.Max(0f, maxWeight);
        MaxCarryVolume = Mathf.Max(0f, maxVolume);
        HasCarryCapacity = true;
    }

    /// <summary>设置是否绕过整包重量/体积上限。</summary>
    public void SetUnlimitedCarryCapacity(bool enabled)
    {
        HasUnlimitedCarryCapacity = enabled;
    }

    /// <summary>设置单格无限堆叠策略；不可堆叠物品仍然严格保持一格一件。</summary>
    public void SetUnlimitedStackSize(bool enabled)
    {
        HasUnlimitedStackSize = enabled;
        if (itemSlots == null)
            return;

        for (int i = 0; i < itemSlots.Count; i++)
        {
            ItemSlot slot = itemSlots[i];
            if (slot == null)
                continue;

            if (enabled)
                slot.SlotMaxVolume = float.MaxValue;
            else if (slot.SlotMaxVolume == float.MaxValue)
                slot.SlotMaxVolume = DefaultSlotVolume;
        }
    }

    /// <summary>启用或关闭自动扩容；关闭时保留现有槽位与物品。</summary>
    public void SetUnlimitedSlots(bool enabled)
    {
        HasUnlimitedSlots = enabled;
        EnsureSpareSlot();
    }

    /// <summary>
    /// 保证动态库存具备可用空槽。普通无限库存维持旧规则：末尾占用后补 1 格；
    /// 玩家主背包则在总空余槽位 <= 2 时一次补足到 3 格。
    /// </summary>
    public void EnsureSpareSlot()
    {
        if (!HasUnlimitedSlots || itemSlots == null)
            return;

        if (!UsesPlayerBagDynamicSlotPolicy)
        {
            if (itemSlots.Count > 0 && itemSlots[itemSlots.Count - 1].itemData == null)
                return;

            AddDynamicSlots(1);
            return;
        }

        int emptySlotCount = CountEmptySlots();
        int missingBaseSlots = Mathf.Max(0, DefaultPlayerBagSlotCount - itemSlots.Count);
        int missingSpareSlots = emptySlotCount <= PlayerBagExpandFreeSlotThreshold
            ? PlayerBagTargetFreeSlotCount - emptySlotCount
            : 0;
        int addCount = Mathf.Max(missingBaseSlots, missingSpareSlots);
        if (addCount > 0)
            AddDynamicSlots(addCount);
    }

    /// <summary>
    /// 玩家主背包自检：先按空余槽位阈值扩容，再在总槽位超过 27 时删除多余空槽。
    /// 收缩目标同时保留 3 个空槽，因此不会在 27/28 格之间反复扩缩。
    /// </summary>
    public void MaintainDynamicSlotCount()
    {
        EnsureSpareSlot();
        if (!HasUnlimitedSlots || !UsesPlayerBagDynamicSlotPolicy || itemSlots == null)
            return;

        int occupiedSlotCount = itemSlots.Count - CountEmptySlots();
        int targetSlotCount = Mathf.Max(
            DefaultPlayerBagSlotCount,
            occupiedSlotCount + PlayerBagTargetFreeSlotCount);
        int removeCount = itemSlots.Count - targetSlotCount;
        if (removeCount <= 0)
            return;

        for (int i = itemSlots.Count - 1; i >= 0 && removeCount > 0; i--)
        {
            ItemSlot slot = itemSlots[i];
            if (slot != null && slot.itemData != null)
                continue;

            itemSlots.RemoveAt(i);
            removeCount--;
        }

        ReindexDynamicSlots();
    }

    /// <summary>统计当前所有空余槽位；玩家背包扩容依据总空余量，而不是最后一格是否被占用。</summary>
    private int CountEmptySlots()
    {
        int emptySlotCount = 0;
        for (int i = 0; i < itemSlots.Count; i++)
        {
            if (itemSlots[i] == null || itemSlots[i].itemData == null)
                emptySlotCount++;
        }

        return emptySlotCount;
    }

    /// <summary>按当前库存策略追加空槽。</summary>
    private void AddDynamicSlots(int count)
    {
        for (int i = 0; i < count; i++)
        {
            itemSlots.Add(new ItemSlot(itemSlots.Count)
            {
                SlotMaxVolume = HasUnlimitedStackSize ? float.MaxValue : DefaultSlotVolume
            });
        }
    }

    /// <summary>动态收缩后统一重建槽位索引并修正当前索引。</summary>
    private void ReindexDynamicSlots()
    {
        for (int i = 0; i < itemSlots.Count; i++)
        {
            if (itemSlots[i] != null)
                itemSlots[i].Index = i;
        }

        Index = itemSlots.Count > 0 ? Mathf.Clamp(Index, 0, itemSlots.Count - 1) : 0;
    }

    /// <summary>按当前整包容量计算这次最多还能加入多少件指定物品。</summary>
    public float GetCapacityLimitedAmount(ItemData itemData, float requestedAmount)
    {
        return GetCapacityLimitedAmount(
            itemData,
            requestedAmount,
            CalculateCarryWeight(),
            CalculateCarryVolume());
    }

    /// <summary>给事务模拟层使用的纯计算版本；current* 可以来自尚未提交的工作快照。</summary>
    public float GetCapacityLimitedAmount(
        ItemData itemData,
        float requestedAmount,
        float currentWeight,
        float currentVolume)
    {
        if (itemData?.Stack == null || requestedAmount <= 0f)
            return 0f;
        if (!HasCarryCapacity || HasUnlimitedCarryCapacity)
            return requestedAmount;

        float unitWeight = Mathf.Max(0f, itemData.Stack.Weight);
        float unitVolume = Mathf.Max(0f, itemData.Stack.Volume);
        float weightAmount = unitWeight <= 0.0001f
            ? float.PositiveInfinity
            : Mathf.Max(0f, MaxCarryWeight - currentWeight) / unitWeight;
        float volumeAmount = unitVolume <= 0.0001f
            ? float.PositiveInfinity
            : Mathf.Max(0f, MaxCarryVolume - currentVolume) / unitVolume;

        float allowedAmount = Mathf.Max(0f, Mathf.Min(requestedAmount, Mathf.Min(weightAmount, volumeAmount)));

        // 当前正式物品数量均为整数；容量边界不能把“1 根木棍”切成 0.5 根塞进背包。
        // 若未来有明确使用小数 Amount 的资源，则保留调用方传入的小数语义。
        if (Mathf.Abs(requestedAmount - Mathf.Round(requestedAmount)) <= 0.0001f)
            allowedAmount = Mathf.Floor(allowedAmount + 0.0001f);

        return allowedAmount;
    }

    /// <summary>跨库存交换前检查移出旧堆并放入新堆后的总容量是否仍合法。</summary>
    public bool CanReplaceItem(ItemData removing, ItemData adding)
    {
        if (!HasCarryCapacity || HasUnlimitedCarryCapacity)
            return true;

        float weight = CalculateCarryWeight() - GetWeight(removing) + GetWeight(adding);
        float volume = CalculateCarryVolume() - GetVolume(removing) + GetVolume(adding);
        return weight <= MaxCarryWeight + 0.0001f && volume <= MaxCarryVolume + 0.0001f;
    }

    private float CalculateCarryWeight()
    {
        if (itemSlots == null)
            return 0f;

        float total = 0f;
        for (int i = 0; i < itemSlots.Count; i++)
            total += GetWeight(itemSlots[i]?.itemData);
        return total;
    }

    private float CalculateCarryVolume()
    {
        if (itemSlots == null)
            return 0f;

        float total = 0f;
        for (int i = 0; i < itemSlots.Count; i++)
            total += GetVolume(itemSlots[i]?.itemData);
        return total;
    }

    private static float GetWeight(ItemData itemData)
    {
        return itemData?.Stack == null ? 0f : Mathf.Max(0f, itemData.Stack.CurrentWeight);
    }

    private static float GetVolume(ItemData itemData)
    {
        return itemData?.Stack == null ? 0f : Mathf.Max(0f, itemData.Stack.CurrentVolume);
    }

    /// <summary>数据事务提交后先维护容量，再通知订阅者；拾取、拖放、整理共用同一边界。</summary>
    private void NotifyItemDataChanged(ItemSlot slot)
    {
        EnsureSpareSlot();
        Event_OnDataChanged.Invoke(slot);
    }

    #endregion
}
