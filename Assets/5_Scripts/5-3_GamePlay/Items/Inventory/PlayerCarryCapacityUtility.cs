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
/// 统一解析玩家“主背包 + 快捷栏”的随身容量，避免拾取提示和背包 UI 各自维护一套统计口径。
/// </summary>
public static class PlayerCarryCapacityUtility
{
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

        float totalWeight = bagData.CurrentCarryWeight;
        float totalVolume = bagData.CurrentCarryVolume;

        Inventory_HotBar hotbarModule = player.itemMods.GetMod_ByID<Inventory_HotBar>(ModText.Hotbar);
        Inventory_Data hotbarData = hotbarModule?.RuntimeInventory?.Data;
        if (hotbarData != null && !ReferenceEquals(hotbarData, bagData))
        {
            totalWeight += hotbarData.CurrentCarryWeight;
            totalVolume += hotbarData.CurrentCarryVolume;
        }

        snapshot = new PlayerCarryCapacitySnapshot(bagData, totalWeight, totalVolume);
        return true;
    }

    /// <summary>依据玩家背包与快捷栏总重量维护来源型超重 Buff；体积超限不触发减速。</summary>
    public static void RefreshOverweightSlowdown(Player player)
    {
        if (player == null || player.itemMods == null)
            return;

        BuffManager buffManager = player.itemMods.GetMod_ByID<BuffManager>(ModText.BuffManager);
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

        BuffManager buffManager = player.itemMods.GetMod_ByID<BuffManager>(ModText.BuffManager);
        buffManager?.RemoveSourceBuff(OverweightBuffSource);
    }
}
