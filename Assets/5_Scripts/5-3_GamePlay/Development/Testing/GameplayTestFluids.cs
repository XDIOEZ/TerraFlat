using System;

/// <summary>Lua 只选择已登记案例，生产流体接口的真实断言逐项写入现有报告。</summary>
public static class GameplayTestFluids
{
    #region 受限案例入口
    public static bool IsScenario(string id) => id != null &&
        (GameplayTestFluidCore.Cases.ContainsKey(id) || GameplayTestFluidNetworks.Cases.ContainsKey(id) ||
         GameplayTestFluidDevices.Cases.ContainsKey(id) || GameplayTestFluidSurface.Cases.ContainsKey(id));

    public static bool Run(string id, Action<string, string, bool, string> record, out string message)
    {
        int checks = 0, failures = 0;
        if (!IsScenario(id)) { message = "未知流体测试案例：" + id; return false; }
        void Check(string label, bool passed, string detail)
        {
            checks++;
            if (!passed) failures++;
            record("fluid.assert", label, passed, detail ?? string.Empty);
        }
        try
        {
            if (GameplayTestFluidCore.Cases.TryGetValue(id, out var core)) core(Check);
            else if (GameplayTestFluidNetworks.Cases.TryGetValue(id, out var network)) network(Check);
            else if (GameplayTestFluidDevices.Cases.TryGetValue(id, out var device)) device(Check);
            else GameplayTestFluidSurface.Cases[id](Check);
        }
        catch (Exception exception) { Check(id + " 执行异常", false, exception.ToString()); }
        if (checks == 0) Check(id + " 必须产生真实断言", false, "案例没有执行任何断言。");
        message = $"{id}：{checks - failures}/{checks} 项通过，{failures} 项失败。";
        return failures == 0;
    }
    #endregion
}
