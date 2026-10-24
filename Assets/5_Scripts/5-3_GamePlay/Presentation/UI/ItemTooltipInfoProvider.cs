using System.Collections.Generic;

/// <summary>表现层只接收基础物品数据和已查询的计时结果，不读取模块私有状态。</summary>
public readonly struct ItemTooltipInfoContext
{
    #region 数据

    public ItemTooltipInfoContext(
        ItemData itemData,
        RuntimeItemDefinition definition,
        ItemInfoBuffer information)
    {
        ItemData = itemData;
        Definition = definition;
        RemainingTimes = information?.RemainingTimes;
        NumericValues = information?.NumericValues;
    }

    public ItemData ItemData { get; }
    public RuntimeItemDefinition Definition { get; }
    public IReadOnlyList<ItemRemainingTimeInfo> RemainingTimes { get; }
    public IReadOnlyList<ItemNumericInfo> NumericValues { get; }

    #endregion
}

/// <summary>UI 信息接口只负责把查询结果转换成本地化显示行，不参与生命周期计算。</summary>
public interface IItemTooltipInfoProvider
{
    void AppendItemTooltipInfo(ItemTooltipInfoContext context, ICollection<string> lines);
}
