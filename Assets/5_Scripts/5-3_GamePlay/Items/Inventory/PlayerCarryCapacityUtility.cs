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
            data.GetCarryUsage(out float weight, out float volume);
            totalWeight += weight;
            totalVolume += volume;
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

    #region 本地携带容量上限调整

    /// <summary>仅为本地权威玩家更新实际负重策略，保留体积、槽位和创造状态。</summary>
    public static bool TrySetMaxWeight(Player player, float maxWeight, out string failureReason)
        => TrySetCarryCapacity(player, maxWeight, setWeight: true, out failureReason);

    /// <summary>仅为本地权威玩家更新实际体积上限，保留重量、槽位和创造状态。</summary>
    public static bool TrySetMaxVolume(Player player, float maxVolume, out string failureReason)
        => TrySetCarryCapacity(player, maxVolume, setWeight: false, out failureReason);

    /// <summary>重量与体积共用资格检查，每次仅替换指定容量并按真实重量刷新超重状态。</summary>
    private static bool TrySetCarryCapacity(Player player, float capacity, bool setWeight, out string failureReason)
    {
        string label = setWeight ? "负重" : "体积";
        if (float.IsNaN(capacity) || float.IsInfinity(capacity) || capacity <= 0f)
        {
            failureReason = $"{label}上限必须是大于 0 的有限数字。";
            return false;
        }

        if (player == null || !player.IsLocalProfile || player.Data == null)
        {
            failureReason = "当前本地玩家尚未就绪。";
            return false;
        }

        if (!FlatWorld.Networking.GameNetwork.HasStateAuthority)
        {
            failureReason = $"该命令仅支持单机或主机的本地玩家，联机客户端不能修改{label}上限。";
            return false;
        }

        Inventory bag = player.itemMods?.GetMod_ByID<Mod_Inventory>(ModText.Bag)?.GetDefaultTargetInventory();
        if (bag?.Data == null || bag.item != player || !bag.Data.HasCarryCapacity)
        {
            failureReason = "玩家主背包尚未初始化。";
            return false;
        }

        if (bag.Data.HasUnlimitedCarryCapacity)
        {
            failureReason = $"当前背包处于创造状态，{label}已经无限；未修改{label}上限。";
            return false;
        }

        if (setWeight)
            player.Data.MaxCarryWeight = capacity;
        else
            player.Data.MaxCarryVolume = capacity;
        bag.Data.SetCarryCapacity(
            setWeight ? capacity : bag.Data.MaxCarryWeight,
            setWeight ? bag.Data.MaxCarryVolume : capacity);
        RefreshOverweightSlowdown(player);
        bag.RefreshUI();
        failureReason = string.Empty;
        return true;
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

        // 创造模式不可能超重，跳过随身库存全量计重。
        if (player.itemMods.GetMod_ByID<Mod_Inventory>(ModText.Bag)
                ?.GetDefaultTargetInventory()?.Data?.HasUnlimitedCarryCapacity == true)
        {
            buffManager.RemoveSourceBuff(OverweightBuffSource);
            return;
        }

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
