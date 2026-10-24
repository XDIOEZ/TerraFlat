using System;
using System.Collections.Generic;

/// <summary>按模块能力汇总实例信息，不创建物品表现或执行业务 Tick。</summary>
public static class ItemInfoQuery
{
    #region 能力提供者注册
    private static readonly Dictionary<string, IItemInfoProvider> providers = new(StringComparer.OrdinalIgnoreCase);

    static ItemInfoQuery()
    {
        Register(ModText.Food, new FoodRemainingTimeProvider());
        Register(Mod_TimedDespawn.ModuleId, new TimedDespawnRemainingTimeProvider());
        Register(nameof(Mod_Damage), new DamageItemInfoProvider());
    }

    public static void Register(string moduleId, IItemInfoProvider provider)
    {
        if (string.IsNullOrWhiteSpace(moduleId)) throw new ArgumentException("模块能力 ID 不能为空。", nameof(moduleId));
        if (provider == null) throw new ArgumentNullException(nameof(provider));
        if (providers.TryGetValue(moduleId, out var existing))
        {
            if (ReferenceEquals(existing, provider)) return;
            throw new InvalidOperationException("物品信息提供者已经注册：" + moduleId);
        }
        providers.Add(moduleId, provider);
    }

    public static bool Unregister(string moduleId, IItemInfoProvider provider)
        => !string.IsNullOrWhiteSpace(moduleId) && providers.TryGetValue(moduleId, out var existing) &&
           ReferenceEquals(existing, provider) && providers.Remove(moduleId);
    #endregion

    #region 只读查询
    /// <summary>向调用方复用的缓冲追加信息；同能力的不同实例槽分别返回，缺失状态不会被默认值伪造。</summary>
    public static void Collect(ItemData itemData, IReadOnlyList<RuntimeItemModuleDefinition> modules,
        ItemInfoBuffer results, ItemInfoClock clock = default)
    {
        if (results == null) throw new ArgumentNullException(nameof(results));
        CollectBaseValues(itemData, results);
        if (itemData?.ModuleDataDic == null || modules == null) return;
        foreach (RuntimeItemModuleDefinition module in modules)
        {
            if (module == null || !module.Enabled || string.IsNullOrEmpty(module.StableName) ||
                !providers.TryGetValue(module.ModuleId, out var provider) ||
                !itemData.ModuleDataDic.TryGetValue(module.StableName, out ModuleData data) || data?.Enabled != true)
                continue;
            if (!string.IsNullOrEmpty(data.ModuleId) && !string.Equals(data.ModuleId, module.ModuleId, StringComparison.OrdinalIgnoreCase))
                continue;
            provider.CollectItemInfo(new ItemInfoContext(itemData, module, data, clock), results);
        }
    }

    /// <summary>运行时便利入口；无需打开任何窗口即可读取同一份数据。</summary>
    public static void Collect(ItemData itemData, ItemInfoBuffer results, string worldKey = null)
    {
        if (results == null) throw new ArgumentNullException(nameof(results));
        GameRes resources = GameRes.ExistingInstance;
        RuntimeItemDefinition definition = null;
        if (itemData != null) resources?.TryGetItemDefinition(itemData.IDName, out definition);
        Collect(itemData, definition?.ModuleDefinitions, results, ReadClock(worldKey));
    }

    private static void CollectBaseValues(ItemData data, ItemInfoBuffer results)
    {
        if (data == null) return;
        void Add(string id, double value, double? maximum = null)
        {
            if (ItemNumericInfo.TryCreate(id, "$item", value, out var info, maximum)) results.NumericValues.Add(info);
        }
        if (data.Stack != null)
        {
            Add(ItemInfoKeys.Amount, data.Stack.Amount);
            Add(ItemInfoKeys.WeightKg, data.Stack.CurrentWeight);
            Add(ItemInfoKeys.VolumeLiters, data.Stack.CurrentVolume);
        }
        if (data.MaxDurability > 0f) Add(ItemInfoKeys.Durability, data.Durability, data.MaxDurability);
        float efficiency = CraftedWorkEfficiencyQuality.Read(data);
        if (efficiency != 1f) Add(ItemInfoKeys.WorkEfficiencyBonusPercent, (efficiency - 1d) * 100d);
        if (data.MatterState?.Initialized == true) Add(ItemInfoKeys.TemperatureCelsius, data.MatterState.TemperatureCelsius);
    }

    public static ItemInfoClock ReadClock(string worldKey)
    {
        DayTimeSystem clock = DayTimeSystem.GetExistingInstance();
        return clock != null && !string.IsNullOrWhiteSpace(worldKey) &&
               clock.TryGetResolvedTimeData(worldKey, out string resolved, out TimeData time)
            ? new ItemInfoClock(resolved, time.GetTotalGameTimeSeconds()) : default;
    }
    #endregion
}
