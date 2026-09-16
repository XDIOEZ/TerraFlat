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
        Check(Mathf.Abs(WorldItemWaterRules.ResolveSinkDuration(freshwater) - 4.5f) < 0.000001f &&
            Mathf.Abs(WorldItemWaterRules.ResolveSinkDuration(freshwater * 1.3f) - 1f) < 0.000001f,
            "下沉时长沿用原有 4.5 秒至 1 秒区间");
        const string report = "Temp/WorldItemWaterRulesValidation.txt";
        Directory.CreateDirectory("Temp");
        File.WriteAllLines(report, results);
        Debug.Log($"[WorldItemWaterRulesValidation] PASS {results.Count} assertions; report={report}");
    }

    #endregion
}
