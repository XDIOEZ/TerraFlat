using System.Collections.Generic;
using System.Runtime.CompilerServices;

/// <summary>自然消失的只读视图；只在状态副本上投影世界时间，不写回计时或消耗物品。</summary>
public sealed class TimedDespawnRemainingTimeProvider : IItemInfoProvider
{
    #region 生命周期信息
    public const string TimerId = "item.timed_despawn";
    private sealed class Settings
    {
        public readonly float Lifetime;
        public Settings(RuntimeItemModuleDefinition definition) => Lifetime = TimedDespawnRules.ReadLifetime(definition);
    }
    private readonly ConditionalWeakTable<RuntimeItemModuleDefinition, Settings> settings = new();
    private static readonly ConditionalWeakTable<RuntimeItemModuleDefinition, Settings>.CreateValueCallback createSettings
        = definition => new Settings(definition);

    public void CollectItemInfo(ItemInfoContext context, ItemInfoBuffer results)
    {
        if (context.ModuleData is not Ex_ModData_MemoryPackable data || !data.Enabled ||
            context.ModuleDefinition?.Enabled != true) return;
        float lifetime = settings.GetValue(context.ModuleDefinition, createSettings).Lifetime;
        TimedDespawnState snapshot = data.GetData<TimedDespawnState>();
        if (context.Clock.IsValid)
            TimedDespawnRules.Advance(ref snapshot, context.Clock.GameSeconds, context.Clock.WorldKey, lifetime);
        double elapsed = snapshot.Initialized ? snapshot.ElapsedGameSeconds : 0d;
        if (ItemRemainingTimeInfo.TryCreate(TimerId, context.ModuleDefinition.StableName, lifetime, elapsed,
                ItemTimerClock.World, ItemTimerScope.WholeStack, out var info))
            results.RemainingTimes.Add(info);
    }
    #endregion
}
