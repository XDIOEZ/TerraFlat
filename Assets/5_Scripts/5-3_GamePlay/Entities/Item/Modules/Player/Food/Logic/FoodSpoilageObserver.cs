using System;
using UnityEngine;

/// <summary>食物腐败观察者的配置与运行时状态。</summary>
[Serializable]
public struct FoodSpoilageObserverData
{
    #region 腐败计时与含水分流

    public bool EnableSpoilage;
    public float SpoilageElapsedSeconds;
    public float SpoilageIntervalSeconds;
    public string SpoilageTargetItemID;
    public float DryingMaxMoisture;
    public string DryingTargetItemID;

    public bool HasValidInterval => float.IsFinite(SpoilageIntervalSeconds) && SpoilageIntervalSeconds > 0f;
    public float RemainingSeconds => EnableSpoilage && HasValidInterval
        ? Mathf.Max(0f, SpoilageIntervalSeconds - SpoilageElapsedSeconds)
        : float.PositiveInfinity;

    /// <summary>读取腐败计时状态。</summary>
    public static FoodSpoilageObserverData Load(ModData_FoodData persistentData) =>
        LoadFromState(persistentData?.FindSpoilageState());

    private static FoodSpoilageObserverData LoadFromState(FoodMechanicStateData state)
    {
        FoodTimerStateData timer = FoodObserverStateStore.ReadSpoilageTimer(state);
        return new FoodSpoilageObserverData
        {
            EnableSpoilage = timer.Enabled,
            SpoilageElapsedSeconds = timer.ElapsedSeconds,
            SpoilageIntervalSeconds = timer.IntervalSeconds,
            SpoilageTargetItemID = timer.TargetItemID,
            DryingMaxMoisture = timer.DryingMaxMoisture,
            DryingTargetItemID = timer.DryingTargetItemID
        };
    }

    /// <summary>保存腐败计时状态。</summary>
    public void Save(ModData_FoodData persistentData)
    {
        FoodMechanicStateData state = FoodObserverStateStore.GetOrCreate(
            persistentData,
            FoodObserverStateStore.SpoilageStateKey);
        FoodObserverStateStore.WriteSpoilageTimer(state, new FoodTimerStateData
        {
            Enabled = EnableSpoilage,
            ElapsedSeconds = SpoilageElapsedSeconds,
            IntervalSeconds = SpoilageIntervalSeconds,
            TargetItemID = SpoilageTargetItemID ?? string.Empty,
            DryingMaxMoisture = DryingMaxMoisture,
            DryingTargetItemID = DryingTargetItemID ?? string.Empty
        });
    }

    // 库存和落地食物共用游戏时间，到期后保留状态供替换失败时重试。
    public bool Advance(float seconds, ModData_FoodData persistentData)
    {
        if (!EnableSpoilage || !HasValidInterval || !float.IsFinite(seconds) || seconds <= 0f ||
            SpoilageElapsedSeconds >= SpoilageIntervalSeconds) return false;
        SpoilageElapsedSeconds = Mathf.Min(SpoilageIntervalSeconds, Mathf.Max(0f, SpoilageElapsedSeconds) + seconds);
        FoodObserverStateStore.WriteSpoilageElapsedSeconds(persistentData, SpoilageElapsedSeconds);
        return true;
    }

    // 只有显式配置干燥产物且含水量达标时才分流，其余仍使用腐败产物。
    public string ResolveTargetItemID(ItemData item, RuntimeItemDefinition definition = null)
    {
        float moisture;
        if (!string.IsNullOrWhiteSpace(DryingTargetItemID) && float.IsFinite(DryingMaxMoisture) &&
            DryingMaxMoisture >= 0f && DryingMaxMoisture <= 1f &&
            (definition != null ? ItemMatterInfoQuery.TryGetMoisture(item, definition, out moisture)
                : ItemMatterInfoQuery.TryGetMoisture(item, out moisture)) && moisture <= DryingMaxMoisture)
            return DryingTargetItemID.Trim();
        return SpoilageTargetItemID?.Trim();
    }

    public bool TryGetExpiredTarget(ItemData item, out string targetItemID, RuntimeItemDefinition definition = null)
    {
        targetItemID = null;
        if (!EnableSpoilage || !HasValidInterval || !float.IsFinite(SpoilageElapsedSeconds) ||
            SpoilageElapsedSeconds < SpoilageIntervalSeconds) return false;
        targetItemID = ResolveTargetItemID(item, definition);
        return !string.IsNullOrWhiteSpace(targetItemID) &&
            !string.Equals(item?.IDName, targetItemID, StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryLoad(ItemData item, out ModData_FoodData foodData, out FoodSpoilageObserverData state)
    {
        foodData = null;
        state = default;
        ModuleDataCollection modules = item?.ModuleDataDic;
        if (modules == null) return false;
        foreach (var pair in modules)
        {
            if (pair.Value is not ModData_FoodData food || !food.Enabled ||
                !string.Equals(food.ModuleId, ModText.Food, StringComparison.Ordinal)) continue;
            FoodMechanicStateData timerState = food.FindSpoilageState();
            if (timerState == null) continue;
            FoodObserverStateStore.InitializeSpoilageTimer(timerState);
            FoodSpoilageObserverData candidate = LoadFromState(timerState);
            if (!candidate.EnableSpoilage || !candidate.HasValidInterval) continue;
            foodData = food;
            state = candidate;
            return true;
        }
        return false;
    }

    #endregion
}

/// <summary>
/// 监听库存中的食物数据 Tick，负责腐败计时和到期后的原槽位状态替换。
/// 观察者只处理规则判断，实际库存写入交给 IFoodItemOperationGateway 的默认实现。
/// </summary>
public sealed class FoodSpoilageModuleDataObserver : IModuleDataRule
{
    #region 库存腐败结算

    /// <summary>只接收食物模块数据，避免观察者处理其他库存模块。</summary>
    public bool CanStep(ModuleDataTickContext context)
    {
        return context.ModuleData is ModData_FoodData;
    }

    /// <summary>每次库存 Tick 推进计时，到期后请求网关替换当前槽位物品。</summary>
    public void Step(ModuleDataTickContext context)
    {
        // 没有开启腐败，或上下文不是有效的食物库存槽位时，不做任何处理。
        if (!(context.ModuleData is ModData_FoodData foodData))
            return;

        FoodObserverStateStore.InitializeSpoilageTimer(foodData.FindSpoilageState());
        FoodSpoilageObserverData observerData = FoodSpoilageObserverData.Load(foodData);
        if (!observerData.EnableSpoilage)
            return;

        if (context.ItemData == null || context.Slot == null ||
            !ReferenceEquals(context.Slot.itemData, context.ItemData))
            return;

        // 配置异常时停止计时，避免无效数据导致物品立即替换或计时溢出。
        if (!observerData.HasValidInterval)
        {
            Debug.LogWarning($"[FoodSpoilage] 腐败间隔无效，物品={context.ItemData.IDName}");
            return;
        }

        string targetItemID = observerData.SpoilageTargetItemID?.Trim();
        if (string.IsNullOrWhiteSpace(targetItemID))
        {
            Debug.LogWarning($"[FoodSpoilage] 腐败目标为空，物品={context.ItemData.IDName}");
            observerData.SpoilageElapsedSeconds = 0f;
            observerData.Save(foodData);
            return;
        }

        // 物品已经是目标状态时清空计时，防止重复触发替换。
        if (string.Equals(context.ItemData.IDName, targetItemID, System.StringComparison.OrdinalIgnoreCase))
        {
            observerData.SpoilageElapsedSeconds = 0f;
            observerData.Save(foodData);
            return;
        }

        // 使用游戏 Tick 累加运行时间，不能用现实时间直接替代。
        observerData.Advance(context.DeltaTime, foodData);
        if (!observerData.TryGetExpiredTarget(context.ItemData, out targetItemID, context.Definition)) return;
        // 由库存操作网关负责保留槽位、数量、GUID 和 UI 等运行时状态。
        if (!InventoryFoodItemOperationGateway.TryReplaceSlot(
            context.InventoryData,
            context.Slot,
            context.SlotIndex,
            targetItemID,
            out string reason))
        {
            Debug.LogWarning($"[FoodSpoilage] 状态替换失败，物品={context.ItemData.IDName}，原因={reason}");
            return;
        }

        Debug.Log($"[FoodSpoilage] 状态替换完成，原物品={context.ItemData.IDName}，目标物品={targetItemID}");
    }

    #endregion
}

/// <summary>可融化食物的库存计时状态；复用食物观察者存储，不额外挂载运行时模块。</summary>
[Serializable]
public struct FoodMeltingObserverData
{
    #region 融化计时状态

    public bool EnableMelting;
    public float MeltingElapsedSeconds;
    public float MeltingIntervalSeconds;

    /// <summary>读取融化计时状态。</summary>
    public static FoodMeltingObserverData Load(ModData_FoodData persistentData)
    {
        FoodTimerStateData timer = FoodObserverStateStore.ReadMeltingTimer(persistentData?.FindMeltingState());
        return new FoodMeltingObserverData
        {
            EnableMelting = timer.Enabled,
            MeltingElapsedSeconds = timer.ElapsedSeconds,
            MeltingIntervalSeconds = timer.IntervalSeconds
        };
    }

    /// <summary>保存融化计时状态。</summary>
    public void Save(ModData_FoodData persistentData)
    {
        FoodMechanicStateData state = FoodObserverStateStore.GetOrCreate(
            persistentData,
            FoodObserverStateStore.MeltingStateKey);
        FoodObserverStateStore.WriteMeltingTimer(state, new FoodTimerStateData
        {
            Enabled = EnableMelting,
            ElapsedSeconds = MeltingElapsedSeconds,
            IntervalSeconds = MeltingIntervalSeconds
        });
    }

    #endregion
}

/// <summary>
/// 监听库存中的可融化食物，每经过一个配置周期就从当前堆叠中融化掉 1 个。
/// 与腐败共用库存 ModuleData Tick 和持久化框架，但不把冰块转换成虚构的“水物品”。
/// </summary>
public sealed class FoodMeltingModuleDataObserver : IModuleDataRule
{
    #region 库存融化结算

    /// <summary>只观察显式声明了融化状态的食物数据。</summary>
    public bool CanStep(ModuleDataTickContext context)
    {
        return context.ModuleData is ModData_FoodData foodData &&
               foodData.FindMeltingState() != null;
    }

    /// <summary>推进融化计时；允许一次较大的 Tick 跨过多个周期并一次性扣除对应数量。</summary>
    public void Step(ModuleDataTickContext context)
    {
        if (!(context.ModuleData is ModData_FoodData foodData))
            return;

        FoodObserverStateStore.InitializeMeltingTimer(foodData.FindMeltingState());
        FoodMeltingObserverData observerData = FoodMeltingObserverData.Load(foodData);
        if (!observerData.EnableMelting)
            return;

        if (context.InventoryData == null || context.ItemData?.Stack == null || context.Slot == null ||
            !ReferenceEquals(context.Slot.itemData, context.ItemData))
            return;

        float intervalSeconds = observerData.MeltingIntervalSeconds;
        if (intervalSeconds <= 0f || float.IsNaN(intervalSeconds) || float.IsInfinity(intervalSeconds))
        {
            Debug.LogWarning($"[FoodMelting] 融化间隔无效，物品={context.ItemData.IDName}");
            return;
        }

        float deltaTime = context.DeltaTime;
        if (deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
            return;

        float elapsedSeconds = observerData.MeltingElapsedSeconds + deltaTime;
        int elapsedIntervals = Mathf.FloorToInt(elapsedSeconds / intervalSeconds);
        if (elapsedIntervals <= 0)
        {
            observerData.MeltingElapsedSeconds = elapsedSeconds;
            FoodObserverStateStore.WriteMeltingElapsedSeconds(foodData, observerData.MeltingElapsedSeconds);
            return;
        }

        int availableAmount = Mathf.Max(0, Mathf.FloorToInt(context.ItemData.Stack.Amount + 0.0001f));
        int meltAmount = Mathf.Min(elapsedIntervals, availableAmount);
        if (meltAmount <= 0)
        {
            observerData.MeltingElapsedSeconds = elapsedSeconds % intervalSeconds;
            FoodObserverStateStore.WriteMeltingElapsedSeconds(foodData, observerData.MeltingElapsedSeconds);
            return;
        }

        float remainderSeconds = elapsedSeconds - elapsedIntervals * intervalSeconds;
        observerData.MeltingElapsedSeconds = Mathf.Max(0f, remainderSeconds);
        FoodObserverStateStore.WriteMeltingElapsedSeconds(foodData, observerData.MeltingElapsedSeconds);

        if (!context.InventoryData.TryConsumeFromSlot(context.Slot, meltAmount, out _))
        {
            // 库存事务失败时保留已经累计的完整时间，下一次 Tick 继续尝试，避免凭空少融化。
            observerData.MeltingElapsedSeconds = elapsedSeconds;
            FoodObserverStateStore.WriteMeltingElapsedSeconds(foodData, observerData.MeltingElapsedSeconds);
            Debug.LogWarning($"[FoodMelting] 融化扣减失败，物品={context.ItemData.IDName}，数量={meltAmount}");
        }
    }

    #endregion
}
