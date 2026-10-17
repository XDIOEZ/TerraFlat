using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// 玩家随身携带容量快照。
/// 主背包、快捷栏和玩家手工制作的实际输入输出共用随身携带空间。
/// </summary>
public readonly struct PlayerCarryCapacitySnapshot
{
    public PlayerCarryCapacitySnapshot(
        Inventory_Data bagData,
        float currentWeight,
        float currentVolume)
    {
        BagData = bagData;
        CurrentWeight = Mathf.Max(0f, currentWeight);
        CurrentVolume = Mathf.Max(0f, currentVolume);
    }

    public Inventory_Data BagData { get; }
    public float CurrentWeight { get; }
    public float CurrentVolume { get; }
    public float MaxWeight => BagData?.MaxCarryWeight ?? float.PositiveInfinity;
    public float MaxVolume => BagData?.MaxCarryVolume ?? float.PositiveInfinity;
    public float MaxAllowedWeight => BagData?.MaxAllowedCarryWeight ?? float.PositiveInfinity;
    public bool HasLimit => BagData != null && BagData.HasCarryCapacity;
    public bool IsUnlimited => !HasLimit || BagData.HasUnlimitedCarryCapacity;
    public bool IsOverNormalWeight => !IsUnlimited && CurrentWeight > MaxWeight + 0.0001f;

    /// <summary>按主背包上限和全部随身库存当前占用计算本次最多还能新增多少件。</summary>
    public float GetCapacityLimitedAmount(ItemData itemData, float requestedAmount)
    {
        if (BagData == null)
            return requestedAmount;

        return BagData.GetCapacityLimitedAmount(
            itemData,
            requestedAmount,
            CurrentWeight,
            CurrentVolume);
    }

    /// <summary>至少再加入一件时是否会先触发重量上限。</summary>
    public bool BlocksByWeight(ItemData itemData)
    {
        if (IsUnlimited || itemData?.Stack == null)
            return false;

        float unitWeight = Mathf.Max(0f, itemData.Stack.Weight);
        return unitWeight > 0.0001f && CurrentWeight + unitWeight > MaxAllowedWeight + 0.0001f;
    }

    /// <summary>至少再加入一件时是否会先触发体积上限。</summary>
    public bool BlocksByVolume(ItemData itemData)
    {
        if (IsUnlimited || itemData?.Stack == null)
            return false;

        float unitVolume = Mathf.Max(0f, itemData.Stack.Volume);
        return unitVolume > 0.0001f && CurrentVolume + unitVolume > MaxVolume + 0.0001f;
    }
}

/// <summary>
/// 统一解析玩家随身库存的容量，供拾取提示、背包 UI 和超重状态共用。
/// </summary>
public static class PlayerCarryCapacityUtility
{
    #region 随身库存与容量统计

    private const string OverweightBuffId = "背包超重";
    private const string OverweightBuffSource = "inventory.overweight";

    /// <summary>读取玩家当前随身重量、体积和主背包上限。</summary>
    public static bool TryGetSnapshot(Player player, out PlayerCarryCapacitySnapshot snapshot)
    {
        snapshot = default;
        if (player == null || player.itemMods == null)
            return false;

        Mod_Inventory bagModule = player.itemMods.GetMod_ByID<Mod_Inventory>(ModText.Bag);
        Inventory bagInventory = bagModule?.GetDefaultTargetInventory();
        Inventory_Data bagData = bagInventory?.Data;
        if (bagData == null)
            return false;

        using var lease = ListPool<Inventory_Data>.Get(out var inventories);
        CollectCarriedInventories(player, inventories);
        float totalWeight = 0f;
        float totalVolume = 0f;
        foreach (Inventory_Data data in inventories)
        {
            totalWeight += data.CurrentCarryWeight;
            totalVolume += data.CurrentCarryVolume;
        }

        snapshot = new PlayerCarryCapacitySnapshot(bagData, totalWeight, totalVolume);
        return true;
    }

    /// <summary>收集实际随身库存并按数据引用去重，不把配方预览或世界工作台计入。</summary>
    public static void CollectCarriedInventories(Player player, List<Inventory_Data> inventories)
    {
        inventories.Clear();
        if (player == null || player.itemMods == null)
            return;

        Add(player.itemMods.GetMod_ByID<Mod_Inventory>(ModText.Bag)?.GetDefaultTargetInventory()?.Data);
        Add(player.itemMods.GetMod_ByID<Mod_HotBar>(ModText.Hotbar)?.RuntimeInventory?.Data);
        foreach (Mod_HandCraftTable table in player.itemMods.GetCapabilities<Mod_HandCraftTable>())
        {
            Add(table.inputInventory?.Data);
            Add(table.outputInventory?.Data);
        }

        void Add(Inventory_Data data)
        {
            if (data != null && !inventories.Contains(data))
                inventories.Add(data);
        }
    }

    #endregion

    #region 超重状态

    /// <summary>依据玩家全部随身库存总重量维护来源型超重 Buff；体积超限不触发减速。</summary>
    public static void RefreshOverweightSlowdown(Player player)
    {
        if (player == null || player.itemMods == null)
            return;

        Mod_BuffManager buffManager = player.itemMods.GetMod_ByID<Mod_BuffManager>(ModText.Mod_BuffManager);
        if (buffManager == null)
            return;

        if (TryGetSnapshot(player, out PlayerCarryCapacitySnapshot snapshot) &&
            snapshot.IsOverNormalWeight)
        {
            buffManager.EnsureSourceBuff(OverweightBuffId, OverweightBuffSource);
            return;
        }

        buffManager.RemoveSourceBuff(OverweightBuffSource);
    }

    /// <summary>玩家背包模块卸载时清除本库存来源的超重减速。</summary>
    public static void ClearOverweightSlowdown(Player player)
    {
        if (player == null || player.itemMods == null)
            return;

        Mod_BuffManager buffManager = player.itemMods.GetMod_ByID<Mod_BuffManager>(ModText.Mod_BuffManager);
        buffManager?.RemoveSourceBuff(OverweightBuffSource);
    }

    #endregion
}
