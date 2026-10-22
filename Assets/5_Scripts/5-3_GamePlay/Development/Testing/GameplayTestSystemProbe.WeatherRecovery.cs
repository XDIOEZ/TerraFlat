using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;

public static partial class GameplayTestSystemProbe
{
    #region 自然天气恢复规则采样
    private static readonly string[] WeatherRecoveryIds =
        { WeatherElementIds.Rain, WeatherElementIds.Wind, WeatherElementIds.Thunder, WeatherElementIds.Fog };

    private static void RegisterWeatherRecoveryOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("environment/natural_recovery_probability", WorldNaturalRecoveryProbability);
        operations.Add("environment/natural_recovery_lifecycle", WorldNaturalRecoveryLifecycle);
        operations.Add("environment/natural_recovery_draw", WorldNaturalRecoveryDraw);
        operations.Add("environment/natural_recovery_clock", WorldNaturalRecoveryClock);
        operations.Add("environment/natural_recovery_manual", WorldNaturalRecoveryManual);
        operations.Add("environment/natural_recovery_validation", WorldNaturalRecoveryValidation);
        operations.Add("environment/natural_draw_all", WorldNaturalDrawAll);
        operations.Add("environment/weather_associations", WorldWeatherAssociations);
    }

    private static JObject WorldWeatherAssociations(JObject args)
    {
        RegionalWeatherSettings settings = WeatherRecoverySettings(WeatherElementIds.Rain);
        foreach (WeatherElementRule rule in settings.Elements) rule.RecoveryProbabilityMultiplier = 1f;
        settings.Associations = new[]
        {
            new WeatherAssociationRule { SourceId = WeatherElementIds.Rain, TargetId = WeatherElementIds.Wind, ProbabilityBonus = 1f },
            new WeatherAssociationRule { SourceId = WeatherElementIds.Wind, TargetId = WeatherElementIds.Thunder, ProbabilityBonus = 1f }
        };
        var engine = WeatherRecoveryEngine(settings, 17413);
        engine.RegisterChunk(new Int2(128, 0), settings.CreateProfile(new ChunkWeatherClimate(1f, 1f, 0f, 1f, 20f, 1f)), 0d);
        JObject Sample(RegionalWeatherState state)
        {
            var sample = new JObject();
            foreach (string id in WeatherRecoveryIds)
            {
                var element = WeatherRecoveryElement(state, id);
                sample[id] = new JObject { ["intensity"] = element.Intensity, ["bonus"] = element.PendingProbabilityBonus };
            }
            return sample;
        }
        engine.Advance(default, 30d);
        RegionalWeatherState first = WeatherRecoveryState(engine);
        JObject saved = Sample(WeatherElementRecordRoundTrip(first));
        engine.Advance(default, 60d);
        JObject second = Sample(WeatherRecoveryState(engine));
        RegionalWeatherState isolatedThird = WeatherRecoveryWithProbability(WeatherRecoveryState(engine), WeatherElementIds.Rain, 0f);
        isolatedThird = WeatherRecoveryWithProbability(isolatedThird, WeatherElementIds.Wind, 0f);
        WeatherRecoveryElement(isolatedThird, WeatherElementIds.Wind).PendingProbabilityBonus = 0f;
        engine.RestoreState(isolatedThird);
        engine.Advance(default, 90d);
        var config = ChunkWeatherConfiguration.Load();
        var profile = new WeatherElementProfile(WeatherElementIds.Wind, .2f, .5f, 3f);
        config.NaturalEventProbabilityMultiplier = 1f;
        return new JObject
        {
            ["first"] = Sample(first), ["saved"] = saved, ["second"] = second,
            ["third"] = Sample(WeatherRecoveryState(engine)),
            ["neighbor"] = Sample(WeatherRecoveryState(engine, new Int2(128, 0))),
            ["probability"] = config.ResolveActivationProbability(profile, 0d, 0d, .1f),
            ["clamped"] = config.ResolveActivationProbability(profile, 0d, 0d, 1f),
            ["configuredRainBonus"] = config.Associations.Length == 0 ? 0f : config.Associations[0].ProbabilityBonus,
            ["configuredWindBonus"] = config.Associations.Length < 2 ? 0f : config.Associations[1].ProbabilityBonus
        };
    }

    private static RegionalWeatherState WeatherElementRecordRoundTrip(RegionalWeatherState source)
    {
        RegionalWeatherState result = source.Clone();
        for (int i = 0; i < result.Elements.Length; i++)
            result.Elements[i] = WeatherElementStateRecord.FromState(source.Elements[i]).ToState();
        return result;
    }

    private static JObject WorldNaturalDrawAll(JObject args)
    {
        RegionalWeatherSettings settings = ChunkWeatherConfiguration.Load();
        settings.NaturalEventProbabilityMultiplier = 1f;
        settings.Associations = Array.Empty<WeatherAssociationRule>();
        // 只把夹具概率固定为零或一，时钟和逐项抽签沿用正式引擎。
        foreach (WeatherElementRule rule in settings.Elements)
        {
            rule.ActivationProbability = (bool?)args[rule.Id] == true ? 1f : 0f;
            rule.MinimumIntensity = rule.MaximumIntensity = .5f;
            rule.MinimumDurationSeconds = rule.MaximumDurationSeconds = 45f;
            rule.RecoveryProbabilityMultiplier = 1f;
            rule.IntensityDistribution = rule.DurationDistribution = null;
        }
        RegionalWeatherEngine engine = WeatherRecoveryEngine(settings, 17413);
        JObject Sample()
        {
            RegionalWeatherState state = WeatherRecoveryState(engine);
            var sample = new JObject { ["time"] = state.LastStepTime };
            foreach (string id in WeatherRecoveryIds)
                sample[id] = WeatherRecoverySample(settings, state, id);
            return sample;
        }
        engine.Advance(default, 298d);
        JObject before = Sample();
        engine.Advance(default, 300d);
        JObject first = Sample();
        engine.Advance(default, 598d);
        JObject between = Sample();
        engine.Advance(default, 600d);
        return new JObject
        {
            ["before"] = before, ["first"] = first, ["between"] = between, ["second"] = Sample(),
            ["checkSeconds"] = settings.CheckIntervalSeconds,
            ["defaultCheckSeconds"] = new RegionalWeatherSettings().CheckIntervalSeconds
        };
    }

    private static JObject WorldNaturalRecoveryProbability(JObject args)
    {
        string id = (string)args["id"] ?? WeatherElementIds.Wind;
        RegionalWeatherSettings settings = ChunkWeatherConfiguration.Load();
        WeatherElementRule rule = Array.Find(settings.Elements, value => value.Id == id);
        WeatherElementRule defaults = Array.Find(new RegionalWeatherSettings().Elements, value => value.Id == id);
        settings.NaturalEventProbabilityMultiplier = .3f;
        rule.RecoveryProbabilityMultiplier = .2f;
        rule.RecoverySeconds = 300f;
        var profile = new WeatherElementProfile(id, .4f, .5f, 60f);
        double recoveryEnd = settings.ResolveNaturalRecoveryEndTime(id, 1000d);
        var maximum = new WeatherElementProfile(id, .4f, 1f, 60f);
        return new JObject
        {
            ["floor"] = rule.RecoveryProbabilityMultiplier, ["seconds"] = rule.RecoverySeconds,
            ["defaultFloor"] = defaults.RecoveryProbabilityMultiplier, ["defaultSeconds"] = defaults.RecoverySeconds,
            ["checkSeconds"] = settings.CheckIntervalSeconds, ["recoveryEnd"] = recoveryEnd,
            ["basis"] = settings.ResolveActivationProbability(profile),
            ["initial"] = settings.ResolveActivationProbability(profile, 1000d, 0d),
            ["beforeEnd"] = settings.ResolveActivationProbability(profile, 700d, recoveryEnd),
            ["atEnd"] = settings.ResolveActivationProbability(profile, 1000d, recoveryEnd),
            ["halfway"] = settings.ResolveActivationProbability(profile, 1150d, recoveryEnd),
            ["recovered"] = settings.ResolveActivationProbability(profile, 1300d, recoveryEnd),
            ["later"] = settings.ResolveActivationProbability(profile, 1600d, recoveryEnd),
            ["low"] = settings.ResolveActivationProbability(new WeatherElementProfile(id, .2f, .5f, 60f), 1150d, recoveryEnd),
            ["high"] = settings.ResolveActivationProbability(new WeatherElementProfile(id, .8f, .5f, 60f), 1150d, recoveryEnd),
            ["maximumBasis"] = settings.ResolveActivationProbability(maximum),
            ["maximumAtEnd"] = settings.ResolveActivationProbability(maximum, 1000d, recoveryEnd),
            ["belowGaleMultiplier"] = rule.ResolveProbabilityMultiplier(.799f),
            ["galeMultiplier"] = rule.ResolveProbabilityMultiplier(.8f),
            ["strongGaleMultiplier"] = rule.ResolveProbabilityMultiplier(.9f),
            ["stormMultiplier"] = rule.ResolveProbabilityMultiplier(1f),
            ["defaultGaleMultiplier"] = defaults.ResolveProbabilityMultiplier(.8f),
            ["defaultStrongGaleMultiplier"] = defaults.ResolveProbabilityMultiplier(.9f),
            ["defaultStormMultiplier"] = defaults.ResolveProbabilityMultiplier(1f)
        };
    }

    private static JObject WorldNaturalRecoveryLifecycle(JObject args)
    {
        string id = (string)args["id"] ?? WeatherElementIds.Wind;
        RegionalWeatherSettings settings = WeatherRecoverySettings(id);
        RegionalWeatherEngine engine = WeatherRecoveryEngine(settings, 17413, temperature: (float)Number(args, "temperature", 20d));
        engine.Advance(default, 28d, false);
        JObject before = WeatherRecoverySample(settings, WeatherRecoveryState(engine), id);
        RegionalWeatherSnapshot firstSnapshot = engine.Advance(default, 30d).Snapshot;
        RegionalWeatherState first = WeatherRecoveryState(engine);
        engine.RegisterChunk(new Int2(16, 0), settings.CreateProfile(new ChunkWeatherClimate(0f, 0f, 0f, 0f, -20f)), 30d);
        RegionalWeatherState neighbor = WeatherRecoveryState(engine, new Int2(100, 1));
        engine.RegisterChunk(new Int2(128, 0), first.Profile, 30d);
        RegionalWeatherState otherRegion = WeatherRecoveryState(engine, new Int2(128, 0));
        engine.Advance(default, 32d, false);
        JObject stillActive = WeatherRecoverySample(settings, WeatherRecoveryState(engine), id);
        engine.Advance(default, 34d, false);
        RegionalWeatherState expired = WeatherRecoveryState(engine);
        RegionalWeatherState converted = RegionalWeatherRecord.FromState(expired).ToState();
        RegionalWeatherEngine restored = WeatherRecoveryEngine(settings, 17413, converted);
        WeatherRecoveryElement(converted, id).NaturalRecoveryEndTime = 555d;
        RegionalWeatherState clone = expired.Clone();
        WeatherRecoveryElement(clone, id).NaturalRecoveryEndTime = 999d;
        RegionalWeatherState exported = WeatherRecoveryState(engine);
        WeatherRecoveryElement(exported, id).NaturalRecoveryEndTime = 777d;
        var otherElements = new JArray();
        foreach (string otherId in WeatherRecoveryIds)
            if (otherId != id)
                otherElements.Add(WeatherRecoverySample(settings, expired, otherId));
        return new JObject
        {
            ["before"] = before, ["first"] = WeatherRecoverySample(settings, first, id),
            ["rain"] = firstSnapshot.RainIntensity, ["snow"] = firstSnapshot.SnowIntensity,
            ["neighbor"] = WeatherRecoverySample(settings, neighbor, id),
            ["otherRegion"] = WeatherRecoverySample(settings, otherRegion, id), ["otherElements"] = otherElements,
            ["stillActive"] = stillActive, ["expired"] = WeatherRecoverySample(settings, expired, id),
            ["restored"] = WeatherRecoverySample(settings, WeatherRecoveryState(restored), id),
            ["cloneChanged"] = WeatherRecoveryElement(clone, id).NaturalRecoveryEndTime,
            ["sourceAfterClone"] = WeatherRecoveryElement(expired, id).NaturalRecoveryEndTime,
            ["engineAfterExport"] = WeatherRecoveryElement(WeatherRecoveryState(engine), id).NaturalRecoveryEndTime
        };
    }

    private static JObject WorldNaturalRecoveryDraw(JObject args)
    {
        string id = (string)args["id"] ?? WeatherElementIds.Wind;
        RegionalWeatherSettings settings = WeatherRecoverySettings(id);
        var trials = new JArray();
        // 每个种子只比较同一份历史的下一次抽签，成功后的新历史不带入下一组。
        for (int seed = 1; seed <= 64; seed++)
        {
            RegionalWeatherEngine bootstrap = WeatherRecoveryEngine(settings, seed);
            bootstrap.Advance(default, 30d);
            bootstrap.Advance(default, 34d, false);
            RegionalWeatherState history = WeatherRecoveryState(bootstrap);
            RegionalWeatherState noHistory = history.Clone();
            WeatherRecoveryElement(noHistory, id).NaturalRecoveryEndTime = 0d;
            RegionalWeatherEngine basis = WeatherRecoveryEngine(settings, seed, noHistory);
            RegionalWeatherEngine regulated = WeatherRecoveryEngine(settings, seed, history);
            RegionalWeatherState lowState = WeatherRecoveryWithProbability(history, id, .25f);
            RegionalWeatherState highState = WeatherRecoveryWithProbability(history, id, .75f);
            RegionalWeatherEngine low = WeatherRecoveryEngine(settings, seed, lowState);
            RegionalWeatherEngine high = WeatherRecoveryEngine(settings, seed, highState);
            basis.Advance(default, 60d);
            regulated.Advance(default, 60d);
            low.Advance(default, 60d);
            high.Advance(default, 60d);
            JObject renewed = WeatherRecoverySample(settings, WeatherRecoveryState(regulated), id);
            regulated.Advance(default, 64d, false);
            trials.Add(new JObject
            {
                ["seed"] = seed, ["history"] = WeatherRecoveryElement(history, id).NaturalRecoveryEndTime,
                ["basisIntensity"] = WeatherRecoveryElement(WeatherRecoveryState(basis), id).Intensity,
                ["lowIntensity"] = WeatherRecoveryElement(WeatherRecoveryState(low), id).Intensity,
                ["highIntensity"] = WeatherRecoveryElement(WeatherRecoveryState(high), id).Intensity,
                ["renewed"] = renewed, ["afterEnd"] = WeatherRecoverySample(settings, WeatherRecoveryState(regulated), id)
            });
        }
        return new JObject { ["trials"] = trials };
    }

    private static JObject WorldNaturalRecoveryClock(JObject args)
    {
        const string id = WeatherElementIds.Wind;
        RegionalWeatherSettings settings = WeatherRecoverySettings(id, 1000f);
        RegionalWeatherEngine engine = WeatherRecoveryEngine(settings, 17413);
        engine.Advance(default, 30d);
        RegionalWeatherState started = WeatherRecoveryState(engine);
        engine.Advance(default, 330d, false);
        JObject longActive = WeatherRecoverySample(settings, WeatherRecoveryState(engine), id);
        engine.Advance(default, 1030d, false);
        JObject ended = WeatherRecoverySample(settings, WeatherRecoveryState(engine), id);
        engine.Advance(default, 1230d, false);
        RegionalWeatherState partlyRecovered = WeatherRecoveryState(engine);
        engine.RebaseTime(-1230d);
        JObject nearZero = WeatherRecoverySample(settings, WeatherRecoveryState(engine), id);
        RegionalWeatherEngine beforeZero = WeatherRecoveryEngine(settings, 17413, partlyRecovered);
        beforeZero.RebaseTime(-2000d);
        RegionalWeatherEngine recovered = WeatherRecoveryEngine(settings, 17413, started);
        recovered.Advance(default, 1500d, false);
        JObject beforeRewind = WeatherRecoverySample(settings, WeatherRecoveryState(recovered), id);
        recovered.RebaseTime(-1400d);
        RegionalWeatherEngine jumped = WeatherRecoveryEngine(settings, 17413, started);
        jumped.Advance(default, 5000d, false);
        return new JObject
        {
            ["started"] = WeatherRecoverySample(settings, started, id), ["longActive"] = longActive,
            ["ended"] = ended, ["partlyRecovered"] = WeatherRecoverySample(settings, partlyRecovered, id),
            ["nearZero"] = nearZero, ["beforeZero"] = WeatherRecoverySample(settings, WeatherRecoveryState(beforeZero), id),
            ["beforeRewind"] = beforeRewind, ["afterRewind"] = WeatherRecoverySample(settings, WeatherRecoveryState(recovered), id),
            ["jumped"] = WeatherRecoverySample(settings, WeatherRecoveryState(jumped), id)
        };
    }

    private static JObject WorldNaturalRecoveryManual(JObject args)
    {
        RegionalWeatherSettings settings = WeatherRecoverySettings(WeatherElementIds.Wind);
        var elements = new JArray();
        foreach (string id in WeatherRecoveryIds)
        {
            RegionalWeatherEngine manual = WeatherRecoveryEngine(settings, 17413);
            manual.ForceElement(default, 0d, id, .5f, 3f);
            JObject active = WeatherRecoverySample(settings, WeatherRecoveryState(manual), id);
            manual.Advance(default, 4d, false);
            elements.Add(new JObject { ["id"] = id, ["active"] = active,
                ["expired"] = WeatherRecoverySample(settings, WeatherRecoveryState(manual), id) });
        }
        RegionalWeatherEngine snow = WeatherRecoveryEngine(settings, 17413);
        snow.ForcePrecipitation(default, 0d, .5f, 3f, true);
        snow.TryGetSnapshot(default, out RegionalWeatherSnapshot snowSnapshot);
        snow.Advance(default, 4d, false);
        RegionalWeatherEngine overrides = WeatherRecoveryEngine(settings, 17413);
        overrides.ForceElements(default, 0d, 3d, .4f, .5f, .6f);
        overrides.StrikeLightning(default, 0d, .5f);
        overrides.Clear(default, 2d);
        var overrideHistories = new JArray();
        foreach (WeatherElementState element in WeatherRecoveryState(overrides).Elements)
            overrideHistories.Add(element.NaturalRecoveryEndTime);
        RegionalWeatherEngine natural = WeatherRecoveryEngine(settings, 17413);
        natural.Advance(default, 30d);
        natural.ForceElement(default, 31d, WeatherElementIds.Wind, .7f, 100f);
        JObject replaced = WeatherRecoverySample(settings, WeatherRecoveryState(natural), WeatherElementIds.Wind);
        natural.Advance(default, 133d, false);
        JObject manualEnded = WeatherRecoverySample(settings, WeatherRecoveryState(natural), WeatherElementIds.Wind);
        natural.Clear(default, 134d);
        return new JObject
        {
            ["elements"] = elements, ["snowIntensity"] = snowSnapshot.SnowIntensity,
            ["snowHistory"] = WeatherRecoveryElement(WeatherRecoveryState(snow), WeatherElementIds.Rain).NaturalRecoveryEndTime,
            ["overrideHistories"] = overrideHistories, ["replaced"] = replaced, ["manualEnded"] = manualEnded,
            ["cleared"] = WeatherRecoverySample(settings, WeatherRecoveryState(natural), WeatherElementIds.Wind)
        };
    }

    private static JObject WorldNaturalRecoveryValidation(JObject args)
    {
        string RejectRule(Action<WeatherElementRule> change)
        {
            var settings = new RegionalWeatherSettings();
            change(Array.Find(settings.Elements, value => value.Id == WeatherElementIds.Wind));
            return WorldException(settings.Validate);
        }
        string RejectHistory(double recoveryEnd)
        {
            RegionalWeatherSettings settings = WeatherRecoverySettings(WeatherElementIds.Wind);
            RegionalWeatherState state = WeatherRecoveryState(WeatherRecoveryEngine(settings, 17413));
            WeatherRecoveryElement(state, WeatherElementIds.Wind).NaturalRecoveryEndTime = recoveryEnd;
            return WorldException(() => WeatherRecoveryEngine(settings, 17413, state));
        }
        var zeroSeconds = WeatherRecoverySettings(WeatherElementIds.Wind);
        Array.Find(zeroSeconds.Elements, value => value.Id == WeatherElementIds.Wind).RecoverySeconds = 0f;
        var profile = new WeatherElementProfile(WeatherElementIds.Wind, 1f, .5f, 3f);
        return new JObject
        {
            ["negativeMultiplier"] = RejectRule(rule => rule.RecoveryProbabilityMultiplier = -.01f),
            ["largeMultiplier"] = RejectRule(rule => rule.RecoveryProbabilityMultiplier = 1.01f),
            ["nanMultiplier"] = RejectRule(rule => rule.RecoveryProbabilityMultiplier = float.NaN),
            ["infiniteMultiplier"] = RejectRule(rule => rule.RecoveryProbabilityMultiplier = float.PositiveInfinity),
            ["negativeSeconds"] = RejectRule(rule => rule.RecoverySeconds = -1f),
            ["nanSeconds"] = RejectRule(rule => rule.RecoverySeconds = float.NaN),
            ["infiniteSeconds"] = RejectRule(rule => rule.RecoverySeconds = float.PositiveInfinity),
            ["negativeHistory"] = RejectHistory(-1d), ["nanHistory"] = RejectHistory(double.NaN),
            ["infiniteHistory"] = RejectHistory(double.PositiveInfinity),
            ["zeroSecondsValidation"] = WorldException(zeroSeconds.Validate),
            ["zeroSecondsProbability"] = zeroSeconds.ResolveActivationProbability(profile, 30d, 333d)
        };
    }
    #endregion

    #region 独立天气夹具与原始状态
    private static RegionalWeatherSettings WeatherRecoverySettings(string activeId, float duration = 3f)
    {
        RegionalWeatherSettings settings = ChunkWeatherConfiguration.Load();
        settings.Associations = Array.Empty<WeatherAssociationRule>();
        settings.NaturalEventProbabilityMultiplier = 1f;
        settings.StepSeconds = 2f;
        settings.CheckIntervalSeconds = 30f;
        foreach (WeatherElementRule rule in settings.Elements)
        {
            rule.ActivationProbability = rule.Id == activeId ? 1f : 0f;
            rule.MinimumIntensity = rule.MaximumIntensity = .5f;
            rule.MinimumDurationSeconds = rule.MaximumDurationSeconds = duration;
            rule.RecoveryProbabilityMultiplier = .2f;
            rule.RecoverySeconds = 300f;
            rule.ProbabilityBands = Array.Empty<WeatherProbabilityBand>();
            rule.IntensityDistribution = rule.DurationDistribution = null;
        }
        return settings;
    }

    private static RegionalWeatherEngine WeatherRecoveryEngine(RegionalWeatherSettings settings, int seed,
        RegionalWeatherState state = null, float temperature = 20f)
    {
        var engine = new RegionalWeatherEngine(settings, seed, default, 16, 16);
        if (state != null) engine.RestoreState(state);
        else engine.RegisterChunk(default,
            settings.CreateProfile(new ChunkWeatherClimate(1f, 1f, 0f, 1f, temperature, 1f)), 0d);
        return engine;
    }

    private static RegionalWeatherState WeatherRecoveryState(RegionalWeatherEngine engine, Int2 cell = default)
    {
        if (!engine.TryGetState(cell, out RegionalWeatherState state))
            throw new InvalidOperationException("自然天气恢复夹具缺少正式区域状态。");
        return state;
    }

    private static WeatherElementState WeatherRecoveryElement(RegionalWeatherState state, string id)
        => Array.Find(state.Elements, element => element.Id == id)
           ?? throw new InvalidOperationException("自然天气恢复夹具缺少正式要素。");

    private static JObject WeatherRecoverySample(RegionalWeatherSettings settings, RegionalWeatherState state, string id)
    {
        WeatherElementState element = WeatherRecoveryElement(state, id);
        if (!state.Profile.TryGetElement(id, out WeatherElementProfile profile))
            throw new InvalidOperationException("自然天气恢复夹具缺少固定要素档案。");
        return new JObject
        {
            ["time"] = state.LastStepTime, ["started"] = element.StartedTime, ["end"] = element.EndTime,
            ["intensity"] = element.Intensity, ["recoveryEnd"] = element.NaturalRecoveryEndTime,
            ["probability"] = settings.ResolveActivationProbability(profile, state.LastStepTime, element.NaturalRecoveryEndTime)
        };
    }

    private static RegionalWeatherState WeatherRecoveryWithProbability(RegionalWeatherState source, string id, float probability)
    {
        RegionalWeatherState state = source.Clone();
        var elements = new WeatherElementProfile[state.Profile.Elements.Count];
        for (int index = 0; index < elements.Length; index++)
        {
            WeatherElementProfile profile = state.Profile.Elements[index];
            elements[index] = profile.Id == id
                ? new WeatherElementProfile(id, probability, profile.Intensity, profile.DurationSeconds) : profile;
        }
        state.Profile = new ChunkWeatherProfile(state.Profile.Climate, elements);
        return state;
    }
    #endregion
}
