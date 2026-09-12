using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 玩家随身携带容量快照。
/// 当前将主背包与快捷栏视为同一份随身携带空间；装备栏、世界容器和制作输入输出不计入。
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
    public bool HasLimit => BagData != null && BagData.HasCarryCapacity;
    public bool IsUnlimited => !HasLimit || BagData.HasUnlimitedCarryCapacity;

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
        return unitWeight > 0.0001f && CurrentWeight + unitWeight > MaxWeight + 0.0001f;
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
/// 统一解析玩家“主背包 + 快捷栏”的随身容量，避免拾取提示和背包 UI 各自维护一套统计口径。
/// </summary>
public static class PlayerCarryCapacityUtility
{
    /// <summary>读取玩家当前随身重量、体积和主背包上限。</summary>
    public static bool TryGetSnapshot(Player player, out PlayerCarryCapacitySnapshot snapshot)
    {
        snapshot = default;
        if (player?.itemMods == null)
            return false;

        Mod_Inventory bagModule = player.itemMods.GetMod_ByID<Mod_Inventory>(ModText.Bag);
        Inventory bagInventory = bagModule?.GetDefaultTargetInventory();
        Inventory_Data bagData = bagInventory?.Data;
        if (bagData == null)
            return false;

        float totalWeight = 0f;
        float totalVolume = 0f;
        var counted = new HashSet<Inventory_Data>();
        AddInventoryUsage(bagData, counted, ref totalWeight, ref totalVolume);

        Inventory_HotBar hotbarModule = player.itemMods.GetMod_ByID<Inventory_HotBar>(ModText.Hotbar);
        AddInventoryUsage(hotbarModule?.RuntimeInventory?.Data, counted, ref totalWeight, ref totalVolume);

        snapshot = new PlayerCarryCapacitySnapshot(bagData, totalWeight, totalVolume);
        return true;
    }

    private static void AddInventoryUsage(
        Inventory_Data data,
        ISet<Inventory_Data> counted,
        ref float totalWeight,
        ref float totalVolume)
    {
        if (data == null || counted == null || !counted.Add(data))
            return;

        totalWeight += data.CurrentCarryWeight;
        totalVolume += data.CurrentCarryVolume;
    }
}
