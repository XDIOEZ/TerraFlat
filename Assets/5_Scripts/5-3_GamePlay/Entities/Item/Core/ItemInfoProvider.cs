using System;
using System.Collections.Generic;

/// <summary>区分运行步进时间与世界时钟，调用方不能把两种秒数混作现实时间。</summary>
public enum ItemTimerClock { Simulation, World }
public enum ItemTimerScope { WholeStack, NextUnit }

/// <summary>调用方提供所属世界的只读时钟采样，缺失时不猜测当前玩家的世界。</summary>
public readonly struct ItemInfoClock
{
    #region 只读时钟
    public ItemInfoClock(string worldKey, double gameSeconds)
    {
        WorldKey = worldKey;
        GameSeconds = gameSeconds;
    }
    public string WorldKey { get; }
    public double GameSeconds { get; }
    public bool IsValid => !string.IsNullOrWhiteSpace(WorldKey) && double.IsFinite(GameSeconds) && GameSeconds >= 0d;
    #endregion
}

/// <summary>纯数据查询上下文；信息提供者不得修改物品、模块或推进真实计时。</summary>
public readonly struct ItemInfoContext
{
    #region 实例数据
    public ItemInfoContext(ItemData itemData, RuntimeItemModuleDefinition moduleDefinition,
        ModuleData moduleData, ItemInfoClock clock = default)
    {
        ItemData = itemData;
        ModuleDefinition = moduleDefinition;
        ModuleData = moduleData;
        Clock = clock;
    }
    public ItemData ItemData { get; }
    public RuntimeItemModuleDefinition ModuleDefinition { get; }
    public ModuleData ModuleData { get; }
    public ItemInfoClock Clock { get; }
    #endregion
}

/// <summary>结构化倒计时，不携带本地化文字、UI 对象或可变模块引用。</summary>
public readonly struct ItemRemainingTimeInfo
{
    #region 只读结果
    private ItemRemainingTimeInfo(string id, string moduleSlot, double durationSeconds, double remainingSeconds,
        ItemTimerClock clock, ItemTimerScope scope, string targetItemId)
    {
        Id = id;
        ModuleSlot = moduleSlot;
        DurationSeconds = durationSeconds;
        RemainingSeconds = remainingSeconds;
        Clock = clock;
        Scope = scope;
        TargetItemId = targetItemId;
    }
    public string Id { get; }
    public string ModuleSlot { get; }
    public double DurationSeconds { get; }
    public double RemainingSeconds { get; }
    public ItemTimerClock Clock { get; }
    public ItemTimerScope Scope { get; }
    public string TargetItemId { get; }

    public static bool TryCreate(string id, string moduleSlot, double durationSeconds, double elapsedSeconds,
        ItemTimerClock clock, ItemTimerScope scope, out ItemRemainingTimeInfo info, string targetItemId = null)
    {
        info = default;
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(moduleSlot) ||
            !double.IsFinite(durationSeconds) || durationSeconds <= 0d || !double.IsFinite(elapsedSeconds))
            return false;
        info = new ItemRemainingTimeInfo(id, moduleSlot, durationSeconds,
            Math.Max(0d, durationSeconds - Math.Max(0d, elapsedSeconds)), clock, scope, targetItemId);
        return true;
    }
    #endregion
}

/// <summary>数值信息的稳定业务键，单位属于数据语义而不是显示文字。</summary>
public static class ItemInfoKeys
{
    public const string Amount = "item.amount";
    public const string WeightKg = "item.weight_kg";
    public const string VolumeLiters = "item.volume_liters";
    public const string Durability = "item.durability";
    public const string TemperatureCelsius = "item.temperature_celsius";
    public const string PhysicalAttack = "combat.physical_attack";
    public const string WorkEfficiencyBonusPercent = "crafting.work_efficiency_bonus_percent";
}

/// <summary>通用数值结果；其他系统直接使用值与可选上限，不解析本地化字符串。</summary>
public readonly struct ItemNumericInfo
{
    #region 数值结果
    private ItemNumericInfo(string id, string moduleSlot, double value, double? maximum)
    { Id = id; ModuleSlot = moduleSlot; Value = value; Maximum = maximum; }
    public string Id { get; }
    public string ModuleSlot { get; }
    public double Value { get; }
    public double? Maximum { get; }
    public static bool TryCreate(string id, string moduleSlot, double value, out ItemNumericInfo info, double? maximum = null)
    {
        info = default;
        if (string.IsNullOrWhiteSpace(id) || !double.IsFinite(value) || maximum.HasValue && !double.IsFinite(maximum.Value))
            return false;
        info = new ItemNumericInfo(id, moduleSlot, value, maximum);
        return true;
    }
    #endregion
}

/// <summary>调用方拥有并复用查询缓冲；条目均为值快照，不保留模块实例引用。</summary>
public sealed class ItemInfoBuffer
{
    #region 查询缓冲
    public List<ItemRemainingTimeInfo> RemainingTimes { get; } = new(4);
    public List<ItemNumericInfo> NumericValues { get; } = new(8);
    public void Clear() { RemainingTimes.Clear(); NumericValues.Clear(); }
    #endregion
}

/// <summary>物品能力只暴露真实结构化数据，UI、AI、分拣等系统共用此接口。</summary>
public interface IItemInfoProvider
{
    void CollectItemInfo(ItemInfoContext context, ItemInfoBuffer results);
}
