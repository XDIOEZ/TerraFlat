using System;
using System.Collections.Generic;
using FlatWorld.Localization;

/// <summary>默认物品信息表现；基础参数与倒计时文案集中在 UI 层。</summary>
public sealed class ItemTooltipInfoFormatter : IItemTooltipInfoProvider
{
    #region 显示语义
    public static readonly ItemTooltipInfoFormatter Default = new();
    private readonly Dictionary<string, string> timerLabels = new(StringComparer.Ordinal)
    {
        [FoodObserverStateStore.SpoilageStateKey] = "距离完全腐败：{0}",
        [FoodObserverStateStore.MeltingStateKey] = "下一个融化：{0}",
        [TimedDespawnRemainingTimeProvider.TimerId] = "距离消失：{0}"
    };
    private readonly Dictionary<string, string> numericLabels = new(StringComparer.Ordinal)
    {
        [ItemInfoKeys.Amount] = "数量：{0}", [ItemInfoKeys.WeightKg] = "重量：{0} kg",
        [ItemInfoKeys.VolumeLiters] = "体积：{0} L", [ItemInfoKeys.Durability] = "耐久：{0} / {1}",
        [ItemInfoKeys.TemperatureCelsius] = "温度：{0}℃", [ItemInfoKeys.PhysicalAttack] = "物理攻击力：{0}",
        [ItemInfoKeys.WorkEfficiencyBonusPercent] = "加工效率加成：{0}%"
    };

    /// <summary>新增能力可单独提供显示文案，不必修改通用窗口或数据查询接口。</summary>
    public void RegisterTimerLabel(string timerId, string localizedFormat)
    {
        if (string.IsNullOrWhiteSpace(timerId)) throw new ArgumentException("倒计时 ID 不能为空。", nameof(timerId));
        if (string.IsNullOrWhiteSpace(localizedFormat)) throw new ArgumentException("显示格式不能为空。", nameof(localizedFormat));
        timerLabels[timerId] = localizedFormat;
    }

    public void RegisterNumericLabel(string valueId, string localizedFormat)
    {
        if (string.IsNullOrWhiteSpace(valueId)) throw new ArgumentException("数值 ID 不能为空。", nameof(valueId));
        if (string.IsNullOrWhiteSpace(localizedFormat)) throw new ArgumentException("显示格式不能为空。", nameof(localizedFormat));
        numericLabels[valueId] = localizedFormat;
    }

    public void AppendItemTooltipInfo(ItemTooltipInfoContext context, ICollection<string> lines)
    {
        if (lines == null) throw new ArgumentNullException(nameof(lines));
        AppendNumericValues(context.NumericValues, lines);
        if (context.RemainingTimes == null) return;
        foreach (ItemRemainingTimeInfo timer in context.RemainingTimes)
        {
            if (string.IsNullOrEmpty(timer.Id)) continue;
            string time = FormatRemainingTime(timer.RemainingSeconds);
            if (timer.Clock == ItemTimerClock.World)
                time = FlatWorldLocalizationService.GetUiFormat("{0}（游戏时间）", time);
            string label = timerLabels.TryGetValue(timer.Id, out string text) ? text : "剩余时间：{0}";
            lines.Add(FlatWorldLocalizationService.GetUiFormat(label, time));
        }
    }
    #endregion

    #region 文本格式化
    private void AppendNumericValues(IReadOnlyList<ItemNumericInfo> values, ICollection<string> lines)
    {
        if (values == null) return;
        foreach (ItemNumericInfo value in values)
        {
            if (string.IsNullOrEmpty(value.Id) || !numericLabels.TryGetValue(value.Id, out string label)) continue;
            // 默认的完整 1 点耐久不占显示行，但原始数据仍然对其他系统开放。
            if (value.Id == ItemInfoKeys.Durability && value.Maximum.HasValue &&
                value.Maximum.Value <= 1.001d && value.Value >= value.Maximum.Value - 0.001d) continue;
            string format = value.Id == ItemInfoKeys.TemperatureCelsius ? "0.#" :
                value.Id == ItemInfoKeys.PhysicalAttack ? "0.##" : "0.###";
            lines.Add(FlatWorldLocalizationService.GetUiFormat(label,
                value.Value.ToString(format), value.Maximum?.ToString(format) ?? string.Empty));
        }
    }

    public static string FormatRemainingTime(double seconds)
    {
        double total = double.IsFinite(seconds) ? Math.Ceiling(Math.Max(0d, seconds)) : 0d;
        if (total >= 3600d)
        {
            double hours = Math.Floor(total / 3600d);
            double minutes = Math.Floor(total % 3600d / 60d);
            return minutes > 0d
                ? FlatWorldLocalizationService.GetUiFormat("{0}小时{1}分钟", hours, minutes)
                : FlatWorldLocalizationService.GetUiFormat("{0}小时", hours);
        }
        if (total >= 60d)
        {
            double minutes = Math.Floor(total / 60d);
            double remainder = total % 60d;
            return remainder > 0d
                ? FlatWorldLocalizationService.GetUiFormat("{0}分{1}秒", minutes, remainder)
                : FlatWorldLocalizationService.GetUiFormat("{0}分钟", minutes);
        }
        return FlatWorldLocalizationService.GetUiFormat("{0}秒", total);
    }

    #endregion
}
