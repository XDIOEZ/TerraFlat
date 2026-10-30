using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 生存数值连续批量采样

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterSurvivalNumericBatchRules()
    {
        Register("survival", "numeric_batch", CaptureSurvivalNumericBatch);
        Register("survival", "oxygen_callback_order", CaptureOxygenSupplyDuringConsumptionCallback);
    }

    // 夹具只调用正式批计算入口，数值、顺序和边界由 Lua 断言。
    private static JObject CaptureSurvivalNumericBatch(JObject args)
    {
        var consumption = new[]
        {
            new SurvivalNumericBatch.OxygenConsumption { Current = 10f, Maximum = 100f, ConsumePerSecond = 10f, Seconds = .2f },
            new SurvivalNumericBatch.OxygenConsumption { Current = 1f, Maximum = 100f, ConsumePerSecond = 10f, Seconds = .2f },
            new SurvivalNumericBatch.OxygenConsumption { Current = 0f, Maximum = 0f, ConsumePerSecond = 10f, Seconds = .2f },
            new SurvivalNumericBatch.OxygenConsumption { Current = 8f, Maximum = 100f, ConsumePerSecond = float.NaN, Seconds = .2f }
        };
        var breathing = new[]
        {
            new SurvivalNumericBatch.OxygenBreathing { Supplied = 2f, ConsumePerSecond = 10f, Seconds = .2f },
            new SurvivalNumericBatch.OxygenBreathing { Supplied = 1.99f, ConsumePerSecond = 10f, Seconds = .2f },
            new SurvivalNumericBatch.OxygenBreathing { Supplied = 0f, ConsumePerSecond = 0f, Seconds = .2f }
        };
        var hypoxia = new[]
        {
            new SurvivalNumericBatch.OxygenHypoxia { BeforeConsumption = 1f, Current = 0f, ConsumePerSecond = 10f, Seconds = .2f, DamageClock = 4.95f },
            new SurvivalNumericBatch.OxygenHypoxia { BeforeConsumption = 3f, Current = 1f, ConsumePerSecond = 10f, Seconds = .2f, DamageClock = 4f },
            new SurvivalNumericBatch.OxygenHypoxia { BeforeConsumption = 0f, Current = 0f, ConsumePerSecond = 0f, Seconds = .2f, DamageClock = 4.8f }
        };
        var pressure = new[]
        {
            new SurvivalNumericBatch.PressureExposure { EnvironmentPressure = 100, SafeMinimum = 50f, SafeMaximum = 150f, Buffer = 1f, BufferMaximum = 30f, RecoveryPerSecond = 2f, Seconds = .5f, Danger = -1, LowDamageClock = 4f },
            new SurvivalNumericBatch.PressureExposure { EnvironmentPressure = 0, SafeMinimum = 50f, SafeMaximum = 150f, Buffer = .3f, BufferMaximum = 30f, RecoveryPerSecond = 1f, Seconds = .2f },
            new SurvivalNumericBatch.PressureExposure { EnvironmentPressure = 0, SafeMinimum = 50f, SafeMaximum = 150f, Buffer = .1f, BufferMaximum = 30f, RecoveryPerSecond = 1f, Seconds = .2f, Danger = -1, LowDamageClock = 4.9f },
            new SurvivalNumericBatch.PressureExposure { EnvironmentPressure = 200, SafeMinimum = 50f, SafeMaximum = 150f, Buffer = 0f, BufferMaximum = 30f, RecoveryPerSecond = 1f, Seconds = 1f, Danger = -1, LowDamageClock = 4f, HighDamageClock = 2f }
        };
        var cooling = new[]
        {
            new SurvivalNumericBatch.TemperatureWaterCooling { NaturalTemperature = 36f, TargetTemperature = 30f, CoolingSpeed = 2f, Protection = .25f, Seconds = 2f, HasTarget = true, InWater = true },
            new SurvivalNumericBatch.TemperatureWaterCooling { NaturalTemperature = 36f, TargetTemperature = 30f, CoolingSpeed = 2f, Protection = 1f, Seconds = 2f, HasTarget = true, InWater = true },
            new SurvivalNumericBatch.TemperatureWaterCooling { NaturalTemperature = 36f, TargetTemperature = 30f, HasTarget = true, WaterExitExpired = true },
            new SurvivalNumericBatch.TemperatureWaterCooling { NaturalTemperature = 30f, TargetTemperature = 30f, HasTarget = true, InWater = true },
            new SurvivalNumericBatch.TemperatureWaterCooling { NaturalTemperature = 31f, TargetTemperature = 30f, CoolingSpeed = 2f, Seconds = 2f, HasTarget = true, InWater = true }
        };
        var ambient = new[]
        {
            new SurvivalNumericBatch.TemperatureAmbientTransfer { NaturalTemperature = 30f, AmbientTemperature = 10f, Seconds = 10f },
            new SurvivalNumericBatch.TemperatureAmbientTransfer { NaturalTemperature = 10f, AmbientTemperature = 30f, Seconds = 10f },
            new SurvivalNumericBatch.TemperatureAmbientTransfer { NaturalTemperature = 60f, AmbientTemperature = 60f, Seconds = 10f }
        };
        var safety = new[]
        {
            new SurvivalNumericBatch.TemperatureSafety { Temperature = 0f, SafeMinimum = 5f, SafeMaximum = 50f, Buffer = .25f, BufferMaximum = 60f, RecoveryPerSecond = 1f, Seconds = .25f },
            new SurvivalNumericBatch.TemperatureSafety { Temperature = 60f, SafeMinimum = 5f, SafeMaximum = 50f, Buffer = 0f, BufferMaximum = 60f, RecoveryPerSecond = 1f, Seconds = .25f },
            new SurvivalNumericBatch.TemperatureSafety { Temperature = 5f, SafeMinimum = 5f, SafeMaximum = 50f, Buffer = .5f, BufferMaximum = 60f, RecoveryPerSecond = 1f, Seconds = .25f },
            new SurvivalNumericBatch.TemperatureSafety { Temperature = 50f, SafeMinimum = 5f, SafeMaximum = 50f, Buffer = 60f, BufferMaximum = 60f, RecoveryPerSecond = 1f, Seconds = .25f }
        };
        SurvivalNumericBatch.Advance(consumption.AsSpan());
        SurvivalNumericBatch.Advance(breathing.AsSpan());
        SurvivalNumericBatch.Advance(hypoxia.AsSpan());
        SurvivalNumericBatch.Advance(pressure.AsSpan());
        SurvivalNumericBatch.Advance(cooling.AsSpan());
        SurvivalNumericBatch.Advance(ambient.AsSpan());
        SurvivalNumericBatch.Advance(safety.AsSpan());

        var oxygenResults = new JArray();
        foreach (var step in consumption) oxygenResults.Add(new JObject { ["next"] = step.Next, ["finite"] = step.HasFiniteChange });
        var breathResults = new JArray();
        foreach (var step in breathing) breathResults.Add(step.Blocked);
        var hypoxiaResults = new JArray();
        foreach (var step in hypoxia) hypoxiaResults.Add(step.DamageClock);
        var pressureResults = new JArray();
        foreach (var step in pressure) pressureResults.Add(new JObject
        {
            ["danger"] = step.Danger, ["buffer"] = step.Buffer, ["lowClock"] = step.LowDamageClock,
            ["highClock"] = step.HighDamageClock, ["exposed"] = step.HasExposure, ["notify"] = step.NotifyChanged
        });
        var coolingResults = new JArray();
        foreach (var step in cooling) coolingResults.Add(new JObject
            { ["next"] = step.NextTemperature, ["apply"] = step.ApplyTemperature, ["clear"] = step.ClearTarget });
        var ambientResults = new JArray();
        foreach (var step in ambient) ambientResults.Add(step.NextTemperature);
        var safetyResults = new JArray();
        foreach (var step in safety) safetyResults.Add(new JObject
            { ["buffer"] = step.Buffer, ["cold"] = step.TooCold, ["hot"] = step.TooHot });
        return new JObject
        {
            ["oxygen"] = oxygenResults, ["breathing"] = breathResults, ["hypoxia"] = hypoxiaResults,
            ["pressure"] = pressureResults, ["cooling"] = coolingResults,
            ["ambient"] = ambientResults, ["safety"] = safetyResults
        };
    }

    // 在正式氧气变化通知中供氧，验证通知后的阻塞与缺氧使用最新状态。
    private static JObject CaptureOxygenSupplyDuringConsumptionCallback(JObject args)
    {
        return WithEntityProbeItem(receiver =>
        {
            receiver.BindData(EntityProbeData("probe:oxygen-callback", 1f));
            var health = receiver.gameObject.AddComponent<Mod_DamageReceiver>();
            health.ModuleInit(receiver, new Ex_ModData { ID = ModText.Hp, Enabled = true });
            receiver.itemMods.AddMod(health);
            var oxygen = receiver.gameObject.AddComponent<Mod_Oxygen>();
            oxygen.ModuleInit(receiver, new Ex_ModData_MemoryPackable { ID = Mod_Oxygen.ModuleId, Enabled = true });
            receiver.itemMods.AddMod(oxygen);
            oxygen.BindModuleDependencies(receiver.itemMods);
            oxygen.Load();

            oxygen.Data.CurrentOxygen = 0f;
            oxygen.ModUpdate(4f);
            bool initiallyBlocked = oxygen.IsBreathBlocked;
            oxygen.Data.CurrentOxygen = 1f;
            var events = new JArray();
            float supplied = 0f;
            oxygen.OxygenValueChanged += value =>
            {
                events.Add(new JObject { ["kind"] = "oxygen", ["value"] = value });
                if (value == 0f) supplied = oxygen.SupplyOxygen(2f);
            };
            oxygen.BreathBlockedChanged += blocked => events.Add(new JObject { ["kind"] = "blocked", ["value"] = blocked });
            oxygen.ModUpdate(.2f);
            oxygen.Save();
            return new JObject
            {
                ["loaded"] = oxygen.IsRuntimeLoaded, ["initiallyBlocked"] = initiallyBlocked,
                ["supplied"] = supplied, ["oxygen"] = oxygen.CurrentValue,
                ["blocked"] = oxygen.IsBreathBlocked, ["hypoxiaSeconds"] = oxygen.Data.HypoxiaSeconds,
                ["events"] = events
            };
        });
    }

    #endregion
}
