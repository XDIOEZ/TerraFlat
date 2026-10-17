using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 体温预警正式表现规则采样

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterTemperaturePresentationRules()
    {
        Register("survival", "temperature_presentation", EntityTemperaturePresentation);
        Register("survival", "temperature_presentation_range", EntityTemperaturePresentationRange);
        Register("survival", "temperature_presentation_transition", EntityTemperaturePresentationTransition);
        Register("survival", "temperature_presentation_color", EntityTemperaturePresentationColor);
    }

    // 夹具只采正式计算结果，期望强度、连续性和颜色语义由 Lua 判断。
    private static JObject EntityTemperaturePresentation(JObject args)
    {
        float temperature = (float)Number(args, "temperature", 36.5d);
        float minimum = (float)Number(args, "minimum", 5d);
        float maximum = (float)Number(args, "maximum", 40d);
        switch (Text(args, "invalid", ""))
        {
            case "temperature_nan": temperature = float.NaN; break;
            case "temperature_infinity": temperature = float.PositiveInfinity; break;
            case "minimum_nan": minimum = float.NaN; break;
            case "maximum_infinity": maximum = float.PositiveInfinity; break;
        }

        return TemperaturePresentationSample(TemperatureWarningPresentationRules.Evaluate(
            temperature, minimum, maximum));
    }

    private static JObject EntityTemperaturePresentationRange(JObject args)
    {
        return WithEntityProbeItem(receiver =>
        {
            Mod_Temperature temperature = receiver.gameObject.AddComponent<Mod_Temperature>();
            temperature.Data.SafeTemperatureMin = 5f;
            temperature.Data.SafeTemperatureMax = 40f;
            object equipment = new();
            JObject Capture()
            {
                temperature.GetSafeTemperatureRange(out float minimum, out float maximum);
                return new JObject
                {
                    ["minimum"] = minimum,
                    ["maximum"] = maximum,
                    ["cold"] = TemperaturePresentationSample(TemperatureWarningPresentationRules.Evaluate(4f, minimum, maximum)),
                    ["hot"] = TemperaturePresentationSample(TemperatureWarningPresentationRules.Evaluate(44f, minimum, maximum))
                };
            }

            JObject initial = Capture();
            temperature.SetSafeTemperatureRangeModifier(equipment, -10f, 10f);
            JObject equipped = Capture();
            temperature.RemoveSafeTemperatureRangeModifier(equipment);
            return new JObject { ["initial"] = initial, ["equipped"] = equipped, ["restored"] = Capture() };
        });
    }

    private static JObject EntityTemperaturePresentationTransition(JObject args)
    {
        if (args["steps"] is not JArray steps || steps.Count == 0 || steps.Count > 128)
            throw new ArgumentException("体温表现过渡需要 1 至 128 个步骤。");

        float severity = (float)Number(args, "current", 0d);
        float duration = (float)Number(args, "duration", 1d);
        string invalid = Text(args, "invalid", "");
        if (invalid == "current_nan") severity = float.NaN;
        if (invalid == "duration_nan") duration = float.NaN;
        var samples = new JArray();
        foreach (JToken token in steps)
        {
            if (token is not JObject step)
                throw new ArgumentException("体温表现过渡步骤必须是对象。");

            float target = (float)Number(step, "target", 0d);
            float seconds = (float)Number(step, "seconds", 0.25d);
            if (invalid == "seconds_nan") seconds = float.NaN;
            if (invalid == "target_infinity") target = float.PositiveInfinity;
            severity = TemperatureWarningPresentationRules.AdvanceScreenSeverity(severity, target, seconds, duration);
            samples.Add(severity);
        }

        return new JObject { ["samples"] = samples, ["final"] = severity };
    }

    private static JObject EntityTemperaturePresentationColor(JObject args)
    {
        Color normal = new Color(0.8f, 0.7f, 0.6f, 0.35f);
        Color Capture(float severity) => TemperatureWarningPresentationRules.ResolveBarColor(severity, normal);
        JArray Channels(Color value) => new JArray(value.r, value.g, value.b, value.a);
        return new JObject
        {
            ["normal"] = Channels(Capture(0f)),
            ["coldHalf"] = Channels(Capture(-0.5f)),
            ["coldFull"] = Channels(Capture(-1f)),
            ["coldClamped"] = Channels(Capture(-5f)),
            ["heatHalf"] = Channels(Capture(0.5f)),
            ["heatFull"] = Channels(Capture(1f)),
            ["heatClamped"] = Channels(Capture(5f)),
            ["invalid"] = Channels(Capture(float.NaN))
        };
    }

    private static JObject TemperaturePresentationSample(TemperatureWarningPresentationSample sample)
        => new JObject
        {
            ["coldWarning"] = sample.ColdWarning,
            ["heatWarning"] = sample.HeatWarning,
            ["coldDanger"] = sample.ColdDanger,
            ["heatDanger"] = sample.HeatDanger,
            ["signedWarning"] = sample.SignedWarning,
            ["signedDanger"] = sample.SignedDanger
        };

    #endregion
}
