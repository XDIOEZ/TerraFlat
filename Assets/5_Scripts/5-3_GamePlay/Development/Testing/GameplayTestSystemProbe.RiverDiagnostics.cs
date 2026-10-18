using System;
using System.Threading;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;

public static partial class GameplayTestSystemProbe
{
    #region 河流请求诊断累计采样

    // 正式生成两次共享一个请求诊断，只返回原始计数与分段，断言交给 Lua。
    private static JObject WorldRiverDiagnosticAggregation(JObject args)
    {
        var numbers = SmallGenerationParameters();
        numbers["terrain.seaLevel"] = 0d;
        numbers["terrain.beachLevel"] = .01d;
        numbers["climate.distance.minimumRainfall"] = 1d;
        numbers["climate.distance.maximumRainfall"] = 1d;
        numbers["climate.polarBand.enabled"] = 0d;
        numbers["climate.temperature.altitudeCoolingStrength"] = 0d;
        numbers["river.startFlow"] = .0001d;
        numbers["river.tributaryStartFlow"] = .0001d;
        numbers["river.floodplainStartFlow"] = .0001d;
        numbers["river.floodplainMaxRadius"] = 8d;
        numbers["river.floodplainMaxSlope"] = 1d;
        numbers["river.lakeChance"] = 0d;
        numbers["lake.large.enabled"] = 0d;
        var profile = new ChunkGenerationProfileSnapshot("probe:river-diagnostics", 1, 8, 8, numbers);
        var request = new ChunkGenerationRequest(1,
            new FlatWorld.WorldModel.WorldAddress("probe-surface", new Int2(0, 0)),
            17413, 1, profile, default);
        var diagnostics = new ChunkStreamingDiagnostics();
        ChunkGenerationTiming timing = diagnostics.BeginRequest(request);
        if (timing == null)
            throw new InvalidOperationException("河流诊断规则需要 Editor 或 Development Build 的正式请求诊断。");
        var generator = new DeterministicChunkGenerator();
        try
        {
            timing.StartGeneration();
            using (generator.Generate(request, CancellationToken.None, timing)) { }
            ChunkGenerationDiagnostic first = timing.Capture();
            using (generator.Generate(request, CancellationToken.None, timing)) { }
            timing.FinishGeneration();
            ChunkGenerationDiagnostic repeated = timing.Capture();
            return new JObject
            {
                ["firstCounts"] = JObject.FromObject(first.GenerationCounts),
                ["repeatedCounts"] = JObject.FromObject(repeated.GenerationCounts),
                ["firstStages"] = JObject.FromObject(first.GenerationStagesMs),
                ["repeatedStages"] = JObject.FromObject(repeated.GenerationStagesMs)
            };
        }
        finally { generator.BeginNewWorldHydrologyEpoch(1); }
    }

    #endregion
}
