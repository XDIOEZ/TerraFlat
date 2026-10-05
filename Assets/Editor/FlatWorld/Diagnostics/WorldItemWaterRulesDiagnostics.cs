using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>散落物共用水体规则的隔离诊断；只计算数值，不创建世界、不读取或修改玩家存档。</summary>
public static class WorldItemWaterRulesDiagnostics
{
    #region 隔离校验入口

    [MenuItem("FlatWorld/诊断/验证掉落物水体规则")]
    public static void Run()
    {
        List<string> results = new();
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            results.Add("PASS " + message);
        }

        float freshwater = WorldItemWaterRules.ResolveSinkRatioThreshold(1f);
        float seawater = WorldItemWaterRules.ResolveSinkRatioThreshold(1.5f);
        Check(Mathf.Abs(freshwater - 0.64f) < 0.000001f && Mathf.Abs(seawater - 0.96f) < 0.000001f,
            "淡水阈值保持 0.64，海水浮力提高 50% 后阈值为 0.96");
        Check(!WorldItemWaterRules.ShouldSink(0.639f, freshwater) &&
            WorldItemWaterRules.ShouldSink(0.64f, freshwater),
            "淡水达到阈值就下沉，ECS 与原 Item 边界一致");
        Check(WorldItemWaterRules.ShouldSink(0.8f, freshwater) &&
            !WorldItemWaterRules.ShouldSink(0.8f, seawater) &&
            WorldItemWaterRules.ShouldSink(seawater, seawater),
            "中间重量物品淡水下沉海水漂浮，海水临界点仍下沉");
        ItemStack stack = new() { Weight = 0.32f, Volume = 1f, Amount = 1f };
        float singleRatio = WorldItemWaterRules.ResolveWeightVolumeRatio(stack);
        stack.Amount = 100f;
        Check(Mathf.Abs(singleRatio - WorldItemWaterRules.ResolveWeightVolumeRatio(stack)) < 0.000001f,
            "同类物品一件与一百件的浮沉比值相同");
        Check(Mathf.Abs(WorldItemWaterRules.ResolveFloatingDepth(0f) - 0.08f) < 0.000001f &&
            Mathf.Abs(WorldItemWaterRules.ResolveFloatingDepth(100f) - 0.42f) < 0.000001f,
            "漂浮水线限制为 0.08 至 0.42，轻物品仍有轻微入水效果");
        Check(Mathf.Abs(WorldItemWaterRules.ResolveSplashIntensity(0.08f) - 0.18f) < 0.000001f &&
            Mathf.Abs(WorldItemWaterRules.ResolveSplashIntensity(0.42f) - 0.6f) < 0.000001f,
            "轻重漂浮物的水花强度沿用原规则范围");
        Check(Mathf.Abs(WorldItemWaterRules.ResolveSinkDuration(freshwater) - 13.5f) < 0.000001f &&
            Mathf.Abs(WorldItemWaterRules.ResolveSinkDuration(freshwater * 1.3f) - 3f) < 0.000001f,
            "下沉时长保持现有三倍时长，即 13.5 秒至 3 秒区间");
        ValidateRiverWeightRules(Check);
        const string report = "Temp/WorldItemWaterRulesValidation.txt";
        Directory.CreateDirectory("Temp");
        File.WriteAllLines(report, results);
        Debug.Log($"[WorldItemWaterRulesValidation] PASS {results.Count} assertions; report={report}");
    }

    #endregion

    #region 河流重量规则

    /// <summary>覆盖重量边界、堆叠变化、不同流量及非河流水体，保证只能加速不能减速。</summary>
    private static void ValidateRiverWeightRules(Action<bool, string> check)
    {
        const RuntimeWaterCurrentKind river = RuntimeWaterCurrentKind.River;
        ItemStack stack = new() { Weight = 0.25f, Volume = 1f, Amount = 1f };
        float baseline = WorldItemWaterRules.ResolveDriftSpeed(river, 1f);
        check(Mathf.Abs(baseline - 0.45f) < 0.000001f, "不带物品的基础流速不变，水面表现不被加速");
        check(Mathf.Abs(WorldItemWaterRules.ResolveDriftSpeed(river, 1f, stack) - baseline * 2.5f) < 0.000001f,
            "总重 0.25 千克获得 2.5 倍河流漂移速度");
        stack.Amount = 2f;
        check(Mathf.Abs(WorldItemWaterRules.ResolveDriftSpeed(river, 1f, stack) - baseline * 2f) < 0.000001f,
            "同组数量增加后按当前总重重新计算加速");
        stack.Amount = 4f;
        check(WorldItemWaterRules.ResolveDriftSpeed(river, 1f, stack) == baseline,
            "总重达到一千克只取消额外加速，不低于原速");
        stack.Amount = 1f;

        float[] weights = { 0f, 0.01f, 0.25f, 0.5f, 0.999f, 1f, 10f, 1000f };
        float[] flows = { 0f, 0.05f, 0.4f, 1f, 4f, 100f };
        foreach (float flow in flows)
        {
            float baseSpeed = WorldItemWaterRules.ResolveDriftSpeed(river, flow);
            float previousSpeed = float.PositiveInfinity;
            foreach (float weight in weights)
            {
                stack.Weight = weight;
                float speed = WorldItemWaterRules.ResolveDriftSpeed(river, flow, stack);
                check(!float.IsNaN(speed) && !float.IsInfinity(speed) && speed >= baseSpeed &&
                    speed <= baseSpeed * WorldItemWaterRules.RiverLightItemMaxSpeedMultiplier + 0.000001f,
                    $"流量 {flow}、总重 {weight}：速度有限且始终位于原速与三倍原速之间");
                check(speed <= previousSpeed, $"流量 {flow}、总重 {weight}：变重不会更快");
                if (weight >= WorldItemWaterRules.RiverLightItemWeightLimitKg)
                    check(speed == baseSpeed, $"流量 {flow}、总重 {weight}：重物严格保持原速");
                previousSpeed = speed;
            }
        }

        stack.Weight = 0f;
        check(WorldItemWaterRules.ResolveDriftSpeed(river, 0f, stack) == 0f, "最轻物品在零流量河段仍不漂移");
        check(WorldItemWaterRules.ResolveDriftSpeed(river, 1f, null) == baseline, "缺少重量数据不改变原速");
        foreach (float invalidWeight in new[] { -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            stack.Weight = invalidWeight;
            check(WorldItemWaterRules.ResolveDriftSpeed(river, 1f, stack) == baseline,
                "无效重量不产生异常速度或减速");
        }
        foreach (RuntimeWaterCurrentKind kind in new[] { RuntimeWaterCurrentKind.None,
            RuntimeWaterCurrentKind.Ocean, RuntimeWaterCurrentKind.ExperimentalLiquid })
        {
            foreach (float weight in new[] { 0f, 0.25f, 1000f })
            {
                stack.Weight = weight;
                check(WorldItemWaterRules.ResolveDriftSpeed(kind, 0.4f, stack) ==
                    WorldItemWaterRules.ResolveDriftSpeed(kind, 0.4f), $"{kind} 不受物品重量加速影响");
            }
        }
    }

    #endregion
}
