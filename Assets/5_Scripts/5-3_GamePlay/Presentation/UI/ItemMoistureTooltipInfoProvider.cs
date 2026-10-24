using System;
using System.Collections.Generic;
using FlatWorld.Localization;

/// <summary>物品含水量的 UI 扩展；数值只从物质数据接口读取。</summary>
public sealed class ItemMoistureTooltipInfoProvider : IItemTooltipInfoProvider
{
    #region 组合显示

    public static readonly ItemMoistureTooltipInfoProvider Default =
        new(ItemTooltipInfoFormatter.Default);

    private readonly IItemTooltipInfoProvider baseProvider;

    private ItemMoistureTooltipInfoProvider(IItemTooltipInfoProvider baseProvider)
    {
        this.baseProvider = baseProvider;
    }

    public void AppendItemTooltipInfo(ItemTooltipInfoContext context, ICollection<string> lines)
    {
        baseProvider.AppendItemTooltipInfo(context, lines);
        AppendMoisture(context.ItemData?.MatterState, context.Definition?.Matter, lines);
    }

    /// <summary>只追加本地化显示行，不改变物品状态；其他界面可以单独复用。</summary>
    public static bool AppendMoisture(ItemMatterState state, RuntimeItemMatterDefinition matter,
        ICollection<string> lines)
    {
        if (lines == null) throw new ArgumentNullException(nameof(lines));
        if (!ItemMatterInfoQuery.TryGetMoisture(state, matter, out float fraction)) return false;
        lines.Add(FlatWorldLocalizationService.GetUiFormat("含水量：{0}%",
            (fraction * 100f).ToString("0.#")));
        return true;
    }

    #endregion
}
