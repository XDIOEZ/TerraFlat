using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using FlatWorld.Dialogue;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 环境水体待办的确定性定向诊断，不启动世界、不读取存档、不修改 Prefab。
/// 覆盖十分位降温、0.3 游泳边界、流量与湖海分类、风潮分离、提示去抖冷却及 BRG 契约。
/// </summary>
public static class EnvironmentWaterDiagnostics
{
    #region 显式菜单

    private static int assertions;

    [MenuItem("FlatWorld/Diagnostics/Validate Environment Water And Temperature")]
    public static void Validate()
    {
        assertions = 0;
        ValidateImmersionRules();
        ValidateCurrentRules();
        ValidateWindAndTide();
        ValidateTemperatureTransitions();
        ValidateSpeechConfiguration();
        ValidateProductionContracts();
        Debug.Log($"[EnvironmentWaterDiagnostics] PASS {assertions} deterministic assertions. No assets or saves modified.");
    }

    #endregion

    #region 纯规则断言

    /// <summary>0.5 自然深度与 0.3 有效深度分别控制消耗和降温。</summary>
    private static void ValidateImmersionRules()
    {
        Near(WaterEnvironmentRules.ResolveCoolingDrop(0f), 0f, "干燥不降温");
        for (int level = 1; level <= 10; level++)
            Near(WaterEnvironmentRules.ResolveCoolingDrop(level / 10f), level * 2f, $"第 {level} 档降温");
        Near(WaterEnvironmentRules.ResolveCoolingDrop(0.3f), 6f, "游泳有效浸没 0.3 降 6℃");
        Near(WaterEnvironmentRules.ResolveCoolingDrop(0.5f), 10f, "无浮力 0.5 降 10℃");
        Near(WaterEnvironmentRules.ResolveCoolingTarget(36.5f, 6f, 10f), 30.5f, "游泳目标");
        Near(WaterEnvironmentRules.ResolveCoolingTarget(36.5f, 10f, 10f), 26.5f, "下沉仍从首次入水基准算");
        Near(WaterEnvironmentRules.ResolveCoolingTarget(15f, 20f, 10f), 10f, "保留降温下限");
        Near(WaterEnvironmentRules.ResolveSwimmingMultiplier(0.2999f), 0f, "0.3 以下不消耗");
        Near(WaterEnvironmentRules.ResolveSwimmingMultiplier(0.3f), 0f, "0.3 边界不消耗");
        Check(WaterEnvironmentRules.ResolveSwimmingMultiplier(0.3001f) > 0f &&
            WaterEnvironmentRules.ResolveSwimmingMultiplier(0.3001f) < 0.001f, "0.3 上方连续起耗");
        Near(WaterEnvironmentRules.ResolveSwimmingMultiplier(0.5f), 2f / 7f, "0.5 自然水深消耗倍率");
        Near(WaterEnvironmentRules.ResolveSwimmingMultiplier(1f), 1f, "最深水恢复基础消耗");
        Check(WaterEnvironmentRules.ResolveSwimmingMultiplier(0.8f) >
            WaterEnvironmentRules.ResolveSwimmingMultiplier(0.5f), "深水消耗递增，不能传有效 0.3");
    }

    /// <summary>所有掉落路径共享流量响应；水文湖泊不能被海洋群系覆盖。</summary>
    private static void ValidateCurrentRules()
    {
        Near(WorldItemWaterRules.ResolveDriftSpeed(RuntimeWaterCurrentKind.River, 0f), 0f, "零流量无漂移");
        Near(WorldItemWaterRules.ResolveDriftSpeed(RuntimeWaterCurrentKind.River, 1f), 0.45f, "单位流量保留原漂移速度");
        Check(WorldItemWaterRules.ResolveDriftSpeed(RuntimeWaterCurrentKind.River, 0.4f) <
            WorldItemWaterRules.ResolveDriftSpeed(RuntimeWaterCurrentKind.River, 4f), "流量增大漂移加速");
        Near(WorldItemWaterRules.ResolveDriftSpeed(RuntimeWaterCurrentKind.None, 99f), 0f, "湖泊静止");
        Near(WorldItemWaterRules.ResolveDriftSpeed(RuntimeWaterCurrentKind.Ocean, 0f),
            WorldItemWaterRules.ResolveDriftSpeed(RuntimeWaterCurrentKind.Ocean, 99f), "洋流不使用河流流量");
        Check(WaterEnvironmentRules.ResolveCurrentKind(1, true) == RuntimeWaterCurrentKind.River, "河口保留河流类型");
        Check(WaterEnvironmentRules.ResolveCurrentKind(2, true) == RuntimeWaterCurrentKind.None, "湖泊优先于海洋群系");
        Check(WaterEnvironmentRules.ResolveCurrentKind(0, true) == RuntimeWaterCurrentKind.Ocean, "海洋独立");
        Check(WaterEnvironmentRules.ResolveCurrentKind(0, false) == RuntimeWaterCurrentKind.None, "地下静水不生成海浪");
    }

    /// <summary>风浪的三个通道都随风力增强，潮汐函数没有风速输入。</summary>
    private static void ValidateWindAndTide()
    {
        Vector3 calm = WaterEnvironmentRules.ResolveOceanWaveFactors(0f);
        Vector3 windy = WaterEnvironmentRules.ResolveOceanWaveFactors(1f);
        Check(windy.x > calm.x && windy.y > calm.y && windy.z > calm.z, "风力提高浪速、浪高与白沫");
        Near(WaterEnvironmentRules.ResolveTidePhase(0.125f, 2f, 1f), 96f, "四分之一潮汐周期到峰值");
        Near(WaterEnvironmentRules.ResolveTidePhase(0.375f, 2f, 1f), -96f, "四分之三潮汐周期反向");
        string common = ReadAsset("Assets/9_Shaders/Shader/WaterSurfaceCommon.hlsl");
        int start = common.IndexOf("float ResolveTideFlowPhase", StringComparison.Ordinal);
        int end = common.IndexOf("\n}", start, StringComparison.Ordinal);
        string tide = common.Substring(start, end - start);
        Check(!tide.Contains("Wind") && !tide.Contains("OceanWave"), "生产 Shader 潮汐不依赖风浪参数");
    }

    /// <summary>显式推进时间覆盖低/高温进入、恢复、阈值抖动和共享冷却。</summary>
    private static void ValidateTemperatureTransitions()
    {
        var settings = new TemperatureSpeechSettings { Hysteresis = 0.5f, DebounceSeconds = 3f, CooldownSeconds = 20f };
        var tracker = new TemperatureSpeechTransitionTracker();
        Check(!tracker.Tick(4f, 5f, 40f, 0f, settings), "低温先去抖");
        Check(!tracker.Tick(4f, 5f, 40f, 2.9f, settings), "去抖不足不提示");
        Check(tracker.Tick(4f, 5f, 40f, 3f, settings) && tracker.Transition == "ColdEnter", "稳定低温进入");
        Check(!tracker.Tick(5.1f, 5f, 40f, 4f, settings), "边界浮动未达到恢复滞回");
        Check(!tracker.Tick(6f, 5f, 40f, 5f, settings), "恢复也先去抖");
        Check(!tracker.Tick(6f, 5f, 40f, 8f, settings), "恢复仍受共享冷却");
        Check(tracker.Tick(6f, 5f, 40f, 23f, settings) && tracker.Transition == "ColdExit", "低温离开");
        Check(!tracker.Tick(41f, 5f, 40f, 44f, settings), "高温去抖");
        Check(tracker.Tick(41f, 5f, 40f, 47f, settings) && tracker.Transition == "HotEnter", "稳定高温进入");
        Check(!tracker.Tick(39.9f, 5f, 40f, 48f, settings), "高温恢复滞回");
        Check(!tracker.Tick(39f, 5f, 40f, 49f, settings), "高温恢复候选");
        Check(tracker.Tick(39f, 5f, 40f, 67f, settings) && tracker.Transition == "HotExit", "高温离开");
        var jitter = new TemperatureSpeechTransitionTracker();
        Check(!jitter.Tick(4f, 5f, 40f, 0f, settings) &&
            !jitter.Tick(6f, 5f, 40f, 2f, settings) &&
            !jitter.Tick(4f, 5f, 40f, 3f, settings) &&
            !jitter.Tick(4f, 5f, 40f, 5f, settings), "反复穿越会重置去抖计时");
    }

    #endregion

    #region 生产接线契约

    private static void ValidateSpeechConfiguration()
    {
        TemperatureSpeechContextContributor.LoadSettings().Validate();
        CharacterSpeechConfigLoadResult result = CharacterSpeechConfigLoader.LoadSources(
            new[] { new CharacterSpeechConfigSource("temperature_transitions.json",
                ReadAsset("Assets/Resources/Dialogue/Soliloquy/temperature_transitions.json")) });
        Check(!result.HasErrors && result.Entries.Count == 4, "四条温度台词及 Fact 可加载");
        Check(result.Entries.All(entry => entry.RetryWhileMatched && entry.CooldownGroup == "temperature" &&
            entry.LocalizedLines.ContainsKey("en")), "温度台词具备共享冷却、受阻重试及英文文本");
    }

    private static void ValidateProductionContracts()
    {
        string receiver = ReadAsset("Assets/5_Scripts/5-3_GamePlay/World/Chunk/TileEffectReceiver.cs");
        Check(receiver.Contains("ConsumeSwimStamina(safeDeltaTime, naturalImmersion)") &&
            receiver.Contains("waterStamina.AddStamina(-consumePerSecond * deltaTime)"), "体力使用自然深度且保留难度入口");
        string renderer = ReadAsset("Assets/5_Scripts/5-3_GamePlay/World/WorldModel/Presentation/ChunkTilemapRenderer.cs");
        Check(renderer.Contains("ResolveCornerCurrent(terrain, x - 1, y - 1)") &&
            renderer.Contains("TryResolveTerrainCell(terrain, left + offsetX, bottom + offsetY"), "河流格角读取邻区权威数据");
        string shader = ReadAsset("Assets/9_Shaders/Shader/Chunk-BRG-Water-Lit.shader");
        Check(shader.Contains("positionWS - velocity * phase * 8.0") &&
            shader.Contains("CalculateChunkWaterSurface(input.positionWS, input.lightingUV") &&
            shader.Contains("CalculateChunkWaterSurface(input.positionWS, input.screenUV"), "双 Pass 均接入下游平流而非材质固定方向");
        Type backend = typeof(ChunkTilemapRenderer).Assembly.GetType("ChunkBatchRendererGroupService", true);
        Type instance = backend.GetNestedType("InstanceData", BindingFlags.NonPublic);
        Check(Marshal.SizeOf(instance) == 112, "CPU BRG 实例尺寸为 112 字节");
        string instanceShader = ReadAsset("Assets/9_Shaders/Shader/ChunkBRGInstance.hlsl");
        Check(instanceShader.Contains("unity_InstanceID * 112u") && instanceShader.Contains("address + 96u"), "GPU BRG 步长与末字段偏移一致");
    }

    private static string ReadAsset(string path) => File.ReadAllText(path);

    private static void Near(float actual, float expected, string message) =>
        Check(Mathf.Abs(actual - expected) < 0.0001f, message);

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("[EnvironmentWaterDiagnostics] " + message);
        assertions++;
    }

    #endregion
}
