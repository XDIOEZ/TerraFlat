using System;
using Newtonsoft.Json.Linq;

internal static class MachineArticulationProductionRegression
{
    #region 正式运动学与Lua采样离线回归

    private static int checks;

    private static void Require(bool condition, string message)
    {
        checks++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Near(double actual, double expected, string message, double tolerance = .000001)
        => Require(double.IsFinite(actual) && Math.Abs(actual - expected) < tolerance,
            message + $"；actual={actual:R} expected={expected:R}");

    private static void VerifyPumpCycle(bool lower)
    {
        JObject sample = GameplayTestSystemProbe.Cycle(new JObject { ["samples"] = 720, ["lowerBranch"] = lower });
        var frames = (JArray)sample["frames"];
        double radius = sample["radius"].Value<double>(), rodLength = sample["length"].Value<double>();
        double centerX = sample["centerX"].Value<double>(), centerY = sample["centerY"].Value<double>();
        double[] minima = { double.PositiveInfinity, double.PositiveInfinity };
        double[] maxima = { double.NegativeInfinity, double.NegativeInfinity };
        foreach (JObject frame in frames)
        {
            var left = (JObject)frame["left"];
            var right = (JObject)frame["right"];
            Near(left["pinX"].Value<double>(), right["pinX"].Value<double>(), "双杆曲柄销横坐标脱节");
            Near(left["pinY"].Value<double>(), right["pinY"].Value<double>(), "双杆曲柄销纵坐标脱节");
            int index = 0;
            foreach (string side in new[] { "left", "right" })
            {
                var rod = (JObject)frame[side];
                Require(rod["solved"].Value<bool>(), "气泵整圈存在无解相位");
                double px = rod["pinX"].Value<double>(), py = rod["pinY"].Value<double>();
                double sx = rod["sliderX"].Value<double>(), sy = rod["sliderY"].Value<double>();
                double angle = rod["rodAngle"].Value<double>();
                Near((px - centerX) * (px - centerX) + (py - centerY) * (py - centerY), radius * radius,
                    "曲柄半径改变");
                Near((sx - px) * (sx - px) + (sy - py) * (sy - py), rodLength * rodLength, "连杆改变长度");
                Near(px + Math.Cos(angle) * rodLength, sx, "连杆角度横向端点不吻合");
                Near(py + Math.Sin(angle) * rodLength, sy, "连杆角度纵向端点不吻合");
                Near(sx, sample[side + "AxisX"].Value<double>(), "活塞偏离轴线");
                minima[index] = Math.Min(minima[index], sy); maxima[index] = Math.Max(maxima[index], sy);
                index++;
            }
        }
        foreach (string side in new[] { "left", "right" })
            Near(frames[0][side]["sliderY"].Value<double>(), frames[frames.Count - 1][side]["sliderY"].Value<double>(), "整圈回位漂移");
        Require(maxima[0] - minima[0] > 2 * radius && maxima[1] - minima[1] > 2 * radius, "双缸行程不完整");
        double initial = frames[0]["left"]["sliderY"].Value<double>() - frames[0]["right"]["sliderY"].Value<double>();
        double opposite = frames[360]["left"]["sliderY"].Value<double>() - frames[360]["right"]["sliderY"].Value<double>();
        Require(initial * opposite < 0, "双缸没有交替做功");
        foreach (JToken accepted in (JArray)sample["invalidAccepted"])
            Require(!accepted.Value<bool>(), "无效几何或相位被接受");
    }

    private static void VerifyPumpWorkGate()
    {
        JObject result = GameplayTestSystemProbe.Working();
        Require(((JArray)result["states"]).Count == 48, "工作门禁采样不完整");
        foreach (JObject sample in (JArray)result["states"])
        {
            bool running = sample["active"].Value<bool>() && sample["powered"].Value<bool>() &&
                sample["transferring"].Value<bool>() && !sample["ruptured"].Value<bool>();
            Near(sample["rpm"].Value<double>(), running ? sample["inputRpm"].Value<double>() : 0,
                "待机、无负载、堵塞或损坏时假工作");
        }
        Near(result["nanRpm"].Value<double>(), 0, "NaN转速进入表现");
        Near(result["infiniteRpm"].Value<double>(), 0, "无限转速进入表现");
    }

    private static void VerifyContinuousClock()
    {
        JObject sample = GameplayTestSystemProbe.Clock();
        Near(sample["quarter"].Value<double>(), Math.PI / 2, "额定相位");
        Near(sample["beforeSwitch"].Value<double>(), sample["afterSwitch"].Value<double>(), "反转变速瞬间跳相");
        Near(sample["reversed"].Value<double>(), 0, "反转轨迹");
        Near(sample["stopped"].Value<double>(), sample["held"].Value<double>(), "停机仍动");
        Near(sample["restart"].Value<double>(), sample["stopped"].Value<double>(), "长停后重启跳相");
        Near(sample["resumed"].Value<double>(), Math.PI / 2, "重启实际速度");
        Near(sample["split"].Value<double>(), sample["whole"].Value<double>(), "分步相位漂移");
        Near(Math.Sin(sample["negative"].Value<double>()), -1, "反转周期方向");
        Near(Math.Cos(sample["negative"].Value<double>()), 0, "反转周期横向端点");
        foreach (JToken phase in (JArray)sample["invalidTime"]) Near(phase.Value<double>(), .3, "无效时间应忽略");
        Near(sample["invalidRpm"].Value<double>(), .3, "非法转速不破坏相位");
    }

    private static void VerifyDeterminismAndDegenerateGeometry()
    {
        JObject configured = GameplayTestSystemProbe.Cycle(new JObject { ["samples"] = 4 });
        double centerX = configured["centerX"].Value<double>(), centerY = configured["centerY"].Value<double>();
        double radius = configured["radius"].Value<double>(), sliderX = configured["leftAxisX"].Value<double>();
        double length = configured["length"].Value<double>();
        foreach (double angle in new[] { -1e5, -Math.PI * 2, -.0001, 0, Math.PI, Math.PI * 2, 1e5 })
        {
            Require(MachineArticulatedKinematics.TrySolveVerticalSliderCrank(angle, centerX, centerY, radius, sliderX, length, 1,
                out var first), "正负长相位必须可解");
            Require(MachineArticulatedKinematics.TrySolveVerticalSliderCrank(angle, centerX, centerY, radius, sliderX, length, 1,
                out var second), "重复采样不可失去解");
            Near(first.CrankPinX, second.CrankPinX, "同输入曲柄销非确定");
            Near(first.SliderPinY, second.SliderPinY, "同输入滑块非确定");
        }
        Require(MachineArticulatedKinematics.TrySolveVerticalSliderCrank(0, 0, 0, 0, 0, 1, 1,
            out var stationary), "零半径固定销仍应保持合法刚性连杆");
        Near(stationary.SliderPinX, 0, "零半径轴线"); Near(stationary.SliderPinY, 1, "零半径长度");
        Require(MachineArticulatedKinematics.TrySolveVerticalSliderCrank(0, 0, 0, 0, 1, 1, 1,
            out var tangent), "刚好相切的几何不应产生NaN");
        Near(tangent.SliderPinY, 0, "相切端点");
    }

    private static void Main()
    {
        VerifyPumpCycle(false); VerifyPumpCycle(true); VerifyPumpWorkGate();
        VerifyContinuousClock(); VerifyDeterminismAndDegenerateGeometry();
        string output = Path.Combine(AppContext.BaseDirectory, "production-pump-poses.json");
        File.WriteAllText(output, GameplayTestSystemProbe.Cycle(new JObject { ["samples"] = 128 }).ToString());
        Console.WriteLine($"PASS: {checks} assertions against production articulated kinematics, work gate, phase clock and shared Lua probe.");
        Console.WriteLine("Production articulation samples: " + output);
    }

    #endregion
}
