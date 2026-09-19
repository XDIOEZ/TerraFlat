using System;
using System.Collections.Generic;

/// <summary>新身体模板的惩罚默认值注册表；运行实例持久化自己的 Buff ID 列表，MOD 可注册/替换模板。</summary>
public static class BodyPartPenaltyRegistry
{
    #region 模板注册
    private static readonly Dictionary<BodyPartType, string[]> Defaults = new()
    {
        { BodyPartType.Head, new[] { "core:concussion" } },
        { BodyPartType.LeftLeg, new[] { "core:leg_fracture" } },
        { BodyPartType.RightLeg, new[] { "core:leg_fracture" } },
        { BodyPartType.LeftHand, new[] { "core:arm_fracture" } },
        { BodyPartType.RightHand, new[] { "core:arm_fracture" } }
    };

    public static void Register(BodyPartType part, params string[] buffIds)
    {
        if (buffIds == null) throw new ArgumentNullException(nameof(buffIds));
        foreach (string id in buffIds)
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("惩罚 Buff ID 不能为空。");
        Defaults[part] = (string[])buffIds.Clone();
    }

    public static List<string> GetDefaultBuffIds(BodyPartType part) =>
        Defaults.TryGetValue(part, out string[] ids) ? new List<string>(ids) : new List<string>();
    #endregion
}
