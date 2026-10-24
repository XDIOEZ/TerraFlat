using System;
using System.Globalization;

/// <summary>制作效率保存为实例品质标记，机器创建快照时解析一次。</summary>
public static class CraftedWorkEfficiencyQuality
{
    #region 加工效率品质
    public const string TagPrefix = "CraftedQuality.WorkEfficiency:";

    public static float Read(ItemData data)
    {
        if (data?.Tags != null)
            foreach (string tag in data.Tags)
                if (tag?.StartsWith(TagPrefix, StringComparison.Ordinal) == true &&
                    float.TryParse(tag.Substring(TagPrefix.Length), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) &&
                    float.IsFinite(value) && value > 0f)
                    return value;
        return 1f;
    }
    #endregion
}
