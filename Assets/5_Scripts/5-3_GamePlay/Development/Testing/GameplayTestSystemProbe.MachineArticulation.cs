using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 气泵机械关节规则登记

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterMachineArticulatedPartRules()
    {
        Register("mechanical", "articulated_pump_cycle", CaptureMachineArticulatedPumpCycle);
        Register("mechanical", "articulated_pump_working", CaptureMachineArticulatedPumpWorkingStates);
        Register("mechanical", "articulated_motion_clock", CaptureMachineArticulatedMotionClock);
    }

    #endregion

    #region 正式关节解算与工作状态采样

    // 探针只调用正式运动学与时钟，杆长、轴线、工作门禁和连续性由Lua断言。
    private static JObject CaptureMachineArticulatedPumpCycle(JObject arguments)
    {
        JObject rig = ReadConfiguredMachineArticulatedPumpRig();
        var center = (JObject)rig["CrankCenterPixels"];
        double centerX = Number(center, "x", 0), centerY = -Number(center, "y", 0);
        double radius = Number(rig, "CrankRadiusPixels", 0), length = Number(rig, "ConnectingRodLengthPixels", 0);
        var sliderAxes = new List<double>();
        foreach (JObject part in (JArray)rig["Parts"])
            if ((string)part["Motion"] == "slider") sliderAxes.Add(Number(part, "SliderAxisPixelsX", 0));
        if (sliderAxes.Count != 2) throw new InvalidOperationException("正式气泵关节定义缺少两枚活塞。");
        sliderAxes.Sort();
        double leftX = sliderAxes[0], rightX = sliderAxes[1];
        int count = (int)Number(arguments, "samples", 128);
        if (count < 4 || count > 1440) throw new ArgumentOutOfRangeException("samples");
        int branch = Flag(arguments, "lowerBranch", false) ? -1 : 1;
        var frames = new JArray();
        for (int i = 0; i <= count; i++)
        {
            double angle = (double)i / count * Math.PI * 2;
            bool left = MachineArticulatedKinematics.TrySolveVerticalSliderCrank(angle, centerX, centerY,
                radius, leftX, length, branch, out var leftPose);
            bool right = MachineArticulatedKinematics.TrySolveVerticalSliderCrank(angle, centerX, centerY,
                radius, rightX, length, branch, out var rightPose);
            frames.Add(new JObject
            {
                ["phase"] = angle,
                ["left"] = new JObject { ["solved"] = left, ["pinX"] = leftPose.CrankPinX,
                    ["pinY"] = leftPose.CrankPinY, ["sliderX"] = leftPose.SliderPinX,
                    ["sliderY"] = leftPose.SliderPinY, ["rodAngle"] = leftPose.ConnectingRodAngleRadians },
                ["right"] = new JObject { ["solved"] = right, ["pinX"] = rightPose.CrankPinX,
                    ["pinY"] = rightPose.CrankPinY, ["sliderX"] = rightPose.SliderPinX,
                    ["sliderY"] = rightPose.SliderPinY, ["rodAngle"] = rightPose.ConnectingRodAngleRadians }
            });
        }
        var rejected = new JArray();
        foreach (var invalid in new[] { (double.NaN, radius, length), (double.PositiveInfinity, radius, length),
                     (0d, -1d, length), (0d, double.NaN, length), (0d, radius, 0d), (0d, radius, 1d) })
            rejected.Add(MachineArticulatedKinematics.TrySolveVerticalSliderCrank(invalid.Item1,
                centerX, centerY, invalid.Item2, leftX, invalid.Item3, branch, out _));
        return new JObject { ["centerX"] = centerX, ["centerY"] = centerY, ["radius"] = radius,
            ["length"] = length, ["leftAxisX"] = leftX, ["rightAxisX"] = rightX,
            ["frames"] = frames, ["invalidAccepted"] = rejected };
    }

    private static JObject ReadConfiguredMachineArticulatedPumpRig()
    {
        string path = Path.Combine(Application.streamingAssetsPath, "GameConfig/Items/shells/chemical_machines.json");
        var items = (JArray)JObject.Parse(File.ReadAllText(path))["items"];
        foreach (JObject item in items)
            if ((string)item["id"] == "GasPump_Mechanical")
                return item["modules"]?["机械扭矩模块"]?["parameters"]?["Articulation"] as JObject
                    ?? throw new InvalidOperationException("正式机械气泵没有声明逐零件装配参数。");
        throw new InvalidOperationException("正式物品目录缺少机械气泵。");
    }

    private static JObject CaptureMachineArticulatedPumpWorkingStates(JObject arguments)
    {
        var states = new JArray();
        foreach (bool active in new[] { false, true })
        foreach (bool powered in new[] { false, true })
        foreach (bool transferring in new[] { false, true })
        foreach (bool ruptured in new[] { false, true })
        foreach (double rpm in new[] { -120d, 0d, 60d })
            states.Add(new JObject
            {
                ["active"] = active, ["powered"] = powered, ["transferring"] = transferring,
                ["ruptured"] = ruptured, ["inputRpm"] = rpm,
                ["rpm"] = MachineArticulatedKinematics.ResolveWorkingSignedRpm(active, powered,
                    transferring, ruptured, rpm)
            });
        return new JObject { ["states"] = states,
            ["nanRpm"] = MachineArticulatedKinematics.ResolveWorkingSignedRpm(true, true, true, false, double.NaN),
            ["infiniteRpm"] = MachineArticulatedKinematics.ResolveWorkingSignedRpm(true, true, true, false,
                double.PositiveInfinity) };
    }

    private static JObject CaptureMachineArticulatedMotionClock(JObject arguments)
    {
        var clock = new MachineArticulatedMotionClock();
        clock.Synchronize(10, 60);
        double quarter = clock.Evaluate(10.25), beforeSwitch = clock.Evaluate(10.25);
        clock.Synchronize(10.25, -120);
        double afterSwitch = clock.Evaluate(10.25), reversed = clock.Evaluate(10.375);
        clock.Synchronize(10.375, 0);
        double stopped = clock.Evaluate(10.375), held = clock.Evaluate(10000);
        clock.Synchronize(10000, 60);
        double restart = clock.Evaluate(10000), resumed = clock.Evaluate(10000.25);
        const double duration = 13.37, rpm = 52.73;
        double split = .17;
        for (int i = 0; i < 137; i++)
            split = MachineArticulatedKinematics.AdvanceContinuousPhase(split, rpm, duration / 137);
        var invalid = new JArray();
        foreach (double elapsed in new[] { 0d, -1d, double.NaN, double.PositiveInfinity })
            invalid.Add(MachineArticulatedKinematics.AdvanceContinuousPhase(.3, 60, elapsed));
        return new JObject
        {
            ["quarter"] = quarter, ["beforeSwitch"] = beforeSwitch, ["afterSwitch"] = afterSwitch,
            ["reversed"] = reversed, ["stopped"] = stopped, ["held"] = held,
            ["restart"] = restart, ["resumed"] = resumed, ["split"] = split,
            ["whole"] = MachineArticulatedKinematics.AdvanceContinuousPhase(.17, rpm, duration),
            ["negative"] = MachineArticulatedKinematics.AdvanceContinuousPhase(0, -60, .25),
            ["invalidTime"] = invalid,
            ["invalidRpm"] = MachineArticulatedKinematics.AdvanceContinuousPhase(.3, double.NaN, 1)
        };
    }

    #endregion
}
