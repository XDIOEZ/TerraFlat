using System;
using System.Collections.Generic;

/// <summary>读取腐败和融化规则的现有状态，不生成文案，也不代替规则结算。</summary>
public sealed class FoodRemainingTimeProvider : IItemInfoProvider
{
    #region 食物计时信息

    public void CollectItemInfo(ItemInfoContext context, ItemInfoBuffer results)
    {
        if (context.ModuleData is not ModData_FoodData foodData || !foodData.Enabled ||
            context.ModuleDefinition?.Enabled != true) return;

        FoodSpoilageObserverData spoilage = FoodSpoilageObserverData.Load(foodData);
        string targetId = spoilage.SpoilageTargetItemID?.Trim();
        if (spoilage.EnableSpoilage && !string.IsNullOrWhiteSpace(targetId) &&
            !string.Equals(context.ItemData?.IDName, targetId, StringComparison.OrdinalIgnoreCase) &&
            ItemRemainingTimeInfo.TryCreate(FoodObserverStateStore.SpoilageStateKey,
                context.ModuleDefinition.StableName, spoilage.SpoilageIntervalSeconds, spoilage.SpoilageElapsedSeconds,
                ItemTimerClock.Simulation, ItemTimerScope.WholeStack, out var spoilageInfo, targetId))
        {
            results.RemainingTimes.Add(spoilageInfo);
        }

        FoodMeltingObserverData melting = FoodMeltingObserverData.Load(foodData);
        if (melting.EnableMelting && ItemRemainingTimeInfo.TryCreate(FoodObserverStateStore.MeltingStateKey,
                context.ModuleDefinition.StableName, melting.MeltingIntervalSeconds, melting.MeltingElapsedSeconds,
                ItemTimerClock.Simulation, ItemTimerScope.NextUnit, out var meltingInfo))
            results.RemainingTimes.Add(meltingInfo);
    }
    #endregion
}
