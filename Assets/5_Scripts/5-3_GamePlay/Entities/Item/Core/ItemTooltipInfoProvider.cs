/// <summary>物品模块向通用悬浮信息面板追加实例信息时使用的只读上下文。</summary>
public readonly struct ItemTooltipInfoContext
{
    #region 数据

    public ItemTooltipInfoContext(
        ItemData itemData,
        RuntimeItemDefinition definition,
        RuntimeItemModuleDefinition moduleDefinition,
        ModuleData moduleData)
    {
        ItemData = itemData;
        Definition = definition;
        ModuleDefinition = moduleDefinition;
        ModuleData = moduleData;
    }

    public ItemData ItemData { get; }
    public RuntimeItemDefinition Definition { get; }
    public RuntimeItemModuleDefinition ModuleDefinition { get; }
    public ModuleData ModuleData { get; }

    #endregion
}

/// <summary>模块通过该接口向物品悬浮面板返回自身的动态说明，面板不感知具体模块类型。</summary>
public interface IItemTooltipInfoProvider
{
    bool TryGetItemTooltipInfo(ItemTooltipInfoContext context, out string info);
}
