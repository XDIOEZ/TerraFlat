using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

/// <summary>当前定义共享的只读配置；ItemData 保留现有 Unity 与玩法字段作为适配外壳。</summary>
public sealed class ItemSharedConfiguration
{
    #region 共享配置

    public string DefinitionId { get; }
    public string GameName { get; }
    public string Description { get; }
    public float MaxDurability { get; }
    public float UnitWeight { get; }
    public float UnitVolume { get; }
    public bool Stackable { get; }
    public float HeatConductionRate { get; }
    public IReadOnlyList<string> Tags { get; }

    internal ItemSharedConfiguration(ItemData source)
    {
        DefinitionId = source.IDName;
        GameName = source.GameName;
        Description = source.Description;
        float multiplier = source.CraftedDurabilityMultiplier;
        MaxDurability = source.MaxDurability / (!float.IsNaN(multiplier) && !float.IsInfinity(multiplier) && multiplier > 0f ? multiplier : 1f);
        UnitWeight = source.Stack?.Weight ?? 1f;
        UnitVolume = source.Stack?.Volume ?? 1f;
        Stackable = source.Stack?.Stackable ?? true;
        HeatConductionRate = source.HeatConductionRate;
        Tags = new ReadOnlyCollection<string>(source.Tags == null ? new List<string>() : new List<string>(source.Tags));
    }

    internal void BindTo(ItemData data)
    {
        data.SharedConfiguration = this;
        data.IDName = DefinitionId;
        data.GameName = GameName;
        data.Description = Description;
        data.MaxDurability = MaxDurability;
        data.HeatConductionRate = HeatConductionRate;
        data.Stack ??= new ItemStack();
        data.Stack.Weight = UnitWeight;
        data.Stack.Volume = UnitVolume;
        data.Stack.Stackable = Stackable;
        // 历史 Tags API 允许玩法临时添加标签，适配列表必须独立，不能污染共享定义。
        data.Tags = new List<string>(Tags);
    }

    #endregion
}

/// <summary>一次编译定义基线，生成时只创建实例外壳与必需的独立可变状态。</summary>
public static class ItemInstanceDataFactory
{
    #region 实例工厂

    public static Func<ItemData> Compile(ItemData templateData)
    {
        if (templateData == null) throw new ArgumentNullException(nameof(templateData));
        ItemInstanceKind kind = ItemInstanceSnapshot.GetKind(templateData);
        ItemSharedConfiguration configuration = new(templateData);
        ItemInstanceSnapshot initialState = ItemInstanceSnapshot.Capture(templateData);
        Dictionary<string, Inventory_Data> playerInventoryDefaults = templateData is Data_Player playerTemplate
            ? FastCloner.FastCloner.DeepClone(playerTemplate._inventoryData) : null;
        var moduleDefaults = new List<ModuleData>();
        if (templateData.ModuleDataDic != null)
            foreach (ModuleData state in templateData.ModuleDataDic.Values)
                if (state != null) moduleDefaults.Add(CloneModuleDefault(state));

        return () =>
        {
            ItemData instance = ItemInstanceSnapshot.CreateData(kind);
            configuration.BindTo(instance);
            if (instance is Data_Player player)
                player._inventoryData = FastCloner.FastCloner.DeepClone(playerInventoryDefaults) ?? new();
            instance.ModuleDataDic = new Dictionary<string, ModuleData>(moduleDefaults.Count, StringComparer.Ordinal);
            for (int i = 0; i < moduleDefaults.Count; i++)
            {
                ModuleData module = CloneModuleDefault(moduleDefaults[i]);
                instance.ModuleDataDic.Add(module.StableName, module);
            }
            initialState.RestoreTo(instance);
            return instance;
        };
    }

    public static ItemData Create(ItemData templateData) => Compile(templateData)();

    internal static ModuleData CloneModuleDefault(ModuleData source)
    {
        ModuleData clone = FastCloner.FastCloner.DeepClone<object>(source) as ModuleData;
        if (clone == null || clone.GetType() != source.GetType())
            throw new InvalidOperationException($"模块 {source.StableName} 的默认数据复制失败：期望 {source.GetType().Name}。");
        return clone;
    }

    /// <summary>原位恢复当前定义，库存槽位与手持实例继续共享同一数据引用。</summary>
    public static void ApplyCurrentDefinition(ItemData target, ItemData definitionDefaults)
    {
        if (target == null || definitionDefaults == null) throw new ArgumentNullException(nameof(target));
        if (ItemInstanceSnapshot.GetKind(target) != ItemInstanceSnapshot.GetKind(definitionDefaults))
            throw new InvalidOperationException($"物品定义的数据类别不一致：{target.IDName}");
        ItemInstanceSnapshot instanceState = ItemInstanceSnapshot.Capture(target);
        ItemSharedConfiguration configuration = definitionDefaults.SharedConfiguration ?? new ItemSharedConfiguration(definitionDefaults);
        configuration.BindTo(target);
        target.CraftedDurabilityMultiplier = 1f;
        target.ModuleDataDic = definitionDefaults.ModuleDataDic;
        instanceState.RestoreTo(target);
    }

    #endregion
}
