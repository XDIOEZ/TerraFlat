using Newtonsoft.Json.Linq;

public static partial class GameplayTestSystemProbe
{
    #region 帧率选项与输入规则采样

    /// <summary>只调用正式解析规则，不修改当前帧率、PlayerPrefs 或用户的设置会话。</summary>
    private static JObject ServiceFrameRate(JObject args)
    {
        var ids = new JArray();
        var targets = new JArray();
        int custom = (int)Number(args, "custom", 137);
        for (int index = 0; index < FrameRateUserSettings.Options.Count; index++)
        {
            ids.Add(FrameRateUserSettings.Options[index].Id);
            targets.Add(FrameRateUserSettings.ResolveTargetFrameRate(index, custom));
        }
        bool parsed = FrameRateUserSettings.TryParseCustomFrameRate(Text(args, "text", "137"), out int value);
        return new JObject
        {
            ["ids"] = ids,
            ["targets"] = targets,
            ["defaultTarget"] = FrameRateUserSettings.ResolveTargetFrameRate(FrameRateUserSettings.DefaultOptionIndex, custom),
            ["invalidOptionTarget"] = FrameRateUserSettings.ResolveTargetFrameRate(-2, custom),
            ["customTarget"] = FrameRateUserSettings.ResolveTargetFrameRate(FrameRateUserSettings.CustomOptionIndex, custom),
            ["parsed"] = parsed,
            ["parsedValue"] = value
        };
    }

    #endregion
}
