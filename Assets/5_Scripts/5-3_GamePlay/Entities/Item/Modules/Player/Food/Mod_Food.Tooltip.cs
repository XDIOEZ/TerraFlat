using FlatWorld.Localization;
using UnityEngine;

/// <summary>食物模块只返回自己的动态状态，不让通用悬浮面板认识腐败规则。</summary>
public partial class Mod_Food : IItemTooltipInfoProvider
{
    #region 悬浮信息

    public bool TryGetItemTooltipInfo(ItemTooltipInfoContext context, out string info)
    {
        info = string.Empty;
        if (!(context.ModuleData is ModData_FoodData foodData))
            return false;

        FoodSpoilageObserverData spoilage = FoodSpoilageObserverData.Load(foodData);
        if (!spoilage.EnableSpoilage ||
            spoilage.SpoilageIntervalSeconds <= 0f ||
            float.IsNaN(spoilage.SpoilageIntervalSeconds) ||
            float.IsInfinity(spoilage.SpoilageIntervalSeconds))
        {
            return false;
        }

        string targetId = spoilage.SpoilageTargetItemID?.Trim();
        if (string.IsNullOrWhiteSpace(targetId) ||
            string.Equals(context.ItemData?.IDName, targetId, System.StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        float remainingSeconds = Mathf.Max(
            0f,
            spoilage.SpoilageIntervalSeconds - spoilage.SpoilageElapsedSeconds);
        info = FlatWorldLocalizationService.GetUiFormat(
            "距离完全腐败：{0}",
            FormatRemainingTime(remainingSeconds));
        return true;
    }

    private static string FormatRemainingTime(float seconds)
    {
        int totalSeconds = Mathf.Max(0, Mathf.CeilToInt(seconds));
        if (totalSeconds >= 3600)
        {
            int hours = totalSeconds / 3600;
            int minutes = totalSeconds % 3600 / 60;
            return minutes > 0
                ? FlatWorldLocalizationService.GetUiFormat("{0}小时{1}分钟", hours, minutes)
                : FlatWorldLocalizationService.GetUiFormat("{0}小时", hours);
        }

        if (totalSeconds >= 60)
        {
            int minutes = totalSeconds / 60;
            int remainSeconds = totalSeconds % 60;
            return remainSeconds > 0
                ? FlatWorldLocalizationService.GetUiFormat("{0}分{1}秒", minutes, remainSeconds)
                : FlatWorldLocalizationService.GetUiFormat("{0}分钟", minutes);
        }

        return FlatWorldLocalizationService.GetUiFormat("{0}秒", totalSeconds);
    }

    #endregion
}
