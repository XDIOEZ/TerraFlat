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

    /// <summary>运行时容量策略；由所属玩家的独立状态恢复，不加入 MemoryPack 库存布局。</summary>
    [MemoryPackIgnore, FastClonerIgnore, JsonIgnore]
    public bool HasUnlimitedSlots { get; private set; }

    /// <summary>玩家主背包允许可堆叠物品在单格内无限叠加。</summary>
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

    /// <summary>当前库存内所有物品的总重量。</summary>
    [MemoryPackIgnore, FastClonerIgnore, JsonIgnore]
    public float CurrentCarryWeight => CalculateCarryWeight();

    /// <summary>当前库存内所有物品的总体积。</summary>
    [MemoryPackIgnore, FastClonerIgnore, JsonIgnore]
    public float CurrentCarryVolume => CalculateCarryVolume();

    /// <summary>配置普通玩家主背包：可堆叠物单格无限，但总携带量受重量与体积双上限约束。</summary>
    public void ConfigurePlayerBagCapacity(float maxWeight, float maxVolume)
    {
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

    /// <summary>无限库存末尾始终预留一个空槽，已有空槽不重复扩容。</summary>
    public void EnsureSpareSlot()
    {
        if (!HasUnlimitedSlots ||
            (itemSlots.Count > 0 && itemSlots[itemSlots.Count - 1].itemData == null))
            return;

        itemSlots.Add(new ItemSlot(itemSlots.Count)
        {
            SlotMaxVolume = HasUnlimitedStackSize ? float.MaxValue : DefaultSlotVolume
        });
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
