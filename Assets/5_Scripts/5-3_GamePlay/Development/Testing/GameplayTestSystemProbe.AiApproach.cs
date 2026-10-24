using FlatWorld.Navigation;
using Newtonsoft.Json.Linq;

public static partial class GameplayTestSystemProbe
{
    #region 追近距离与动作距离
    /// <summary>只采样正式共用规则，Lua 断言范围、计时与步长；不接触运行世界。</summary>
    private static JObject EntityAiApproachRanges(JObject args)
    {
        float range = (float)Number(args, "actionRange", .9d);
        float preferred = (float)Number(args, "stopDistance", .3d);
        float stop = AIApproachRange.ResolveStopDistance(preferred, range);
        float distance = range * .8f;
        return new JObject
        {
            ["stopDistance"] = stop,
            ["actionRange"] = range,
            ["movingTimer"] = AIApproachRange.AdvanceActionTimer(.2f, .1f, distance * distance, range),
            ["outsideTimer"] = AIApproachRange.AdvanceActionTimer(.7f, .1f, range * range * 4f, range),
            ["clampedStep"] = AIApproachRange.ClampMovementStep(range, range * 10f, stop),
            ["innerStep"] = AIApproachRange.ClampMovementStep(stop * .5f, 1f, stop),
            ["invalidStop"] = AIApproachRange.ResolveStopDistance(float.NaN, range),
            ["invalidAction"] = AIApproachRange.IsInActionRange(float.NaN, range)
        };
    }
    #endregion
}
