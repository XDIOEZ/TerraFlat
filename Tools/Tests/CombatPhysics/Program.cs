using FlatWorld.Combat;

internal static class Program
{
    #region 正式逻辑窗口离线回归
    private static int assertions;

    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Main()
    {
        var window = new CombatHitWindow();
        var original = new CombatIdentity { Backend = CombatBackend.External, Value = 19, Generation = 2, World = 3, Dimension = 4 };
        Check(!window.TryReserve(default, 3) && window.Count == 0, "非法身份不得消费名额");
        window.Begin();
        Check(window.Sequence == 1, "首次攻击必须有非零序号");
        Check(window.TryReserve(original, 3), "首次命中必须成功");
        for (int i = 0; i < 1000; i++)
            Check(!window.TryReserve(original, 3), "Trigger、主动扫描及镜像不能重复预约同一身份");
        Check(window.Count == 1 && window.Remaining(3) == 2, "重复候选不能消耗额外名额");
        var pooled = original; pooled.Generation++;
        var otherWorld = original; otherWorld.World++;
        Check(window.TryReserve(pooled, 3), "复用对象的新代际应独立");
        Check(window.TryReserve(otherWorld, 3), "不同世界的相同编号应独立");
        var otherDimension = original; otherDimension.Dimension++;
        Check(!window.TryReserve(otherDimension, 3), "超额目标必须被拒绝");
        Check(window.Remaining(1) == 0, "动态减少预算不能出现负名额");
        uint sequence = window.Sequence;
        window.Begin();
        Check(window.Sequence == sequence + 1 && window.Count == 0, "新攻击必须清空窗口并增加序号");
        Check(window.TryReserve(original, 4) && window.TryReserve(otherDimension, 4), "同一目标新攻击可受击且维度身份独立");
        var backend = original; backend.Backend = CombatBackend.Entity;
        Check(window.TryReserve(backend, 4), "不同权威后端不得合并");
        window.Clear();
        Check(window.Count == 0 && window.Sequence == sequence + 1, "关闭窗口不应伪造新攻击");
        Check(window.TryReserve(original, 0) && window.Remaining(0) == 0, "非法目标上限按单目标处理");
        window.Reset();
        Check(window.Count == 0 && window.Sequence == 0 && window.Remaining(3) == 3, "回池必须清除全部运行时预约");
        for (int attack = 0; attack < 100; attack++)
        {
            window.Begin();
            Check(window.TryReserve(original, 1), "重复攻击窗口不能遗留旧目标");
            Check(!window.TryReserve(original, 1), "每个窗口仍须去重");
        }
        Console.WriteLine($"PASS: {assertions} production CombatHitWindow assertions; Unity physics and Lua not executed.");
    }
    #endregion
}
