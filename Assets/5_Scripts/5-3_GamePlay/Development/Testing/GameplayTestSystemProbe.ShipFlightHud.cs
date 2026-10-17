using System;
using System.Collections.Generic;
using FlatWorld.Spaceflight;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static partial class GameplayTestSystemProbe
{
    #region 飞船起降高度 HUD 规则采样

    private static void RegisterShipFlightHudOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("ui_settings/ship_flight_hud_height", ServiceShipFlightHudHeight);
        operations.Add("ui_settings/ship_flight_hud_sequence", ServiceShipFlightHudSequence);
        operations.Add("ui_settings/ship_flight_hud_receipt_memory", ServiceShipFlightHudReceiptMemory);
    }

    private static double ShipFlightHudProbeHeight(JObject args)
    {
        return Text(args, "heightMode", "finite") switch
        {
            "finite" => Number(args, "height", 0d),
            "nan" => double.NaN,
            "positive_infinity" => double.PositiveInfinity,
            "negative_infinity" => double.NegativeInfinity,
            _ => throw new ArgumentException("未知高度边界夹具。")
        };
    }

    private static JObject ServiceShipFlightHudHeight(JObject args)
    {
        double height = ShipFlightHudProbeHeight(args);
        double maximum = Number(args, "maximum", 100d);
        bool touchdown = Flag(args, "touchdown", false);
        return new JObject
        {
            ["usable"] = ShipFlightHudPresentationRules.IsUsableHeight(height),
            ["number"] = ShipFlightHudPresentationRules.FormatHeightNumber(height, touchdown),
            ["normalized"] = ShipFlightHudPresentationRules.GetNormalizedHeight(height, maximum, touchdown),
            ["maximum"] = ShipFlightHudPresentationRules.ChooseScaleMaximum(height),
            ["expandedMaximum"] = ShipFlightHudPresentationRules.ExpandScaleMaximum(maximum, height),
            ["nearGround"] = ShipFlightHudPresentationRules.IsNearGround(height, Flag(args, "descending", true))
        };
    }

    private static JObject ServiceShipFlightHudSequence(JObject args)
    {
        if (args["steps"] is not JArray steps || steps.Count == 0 || steps.Count > 128)
            throw new ArgumentException("高度 HUD 采样需要 1 至 128 个连续步骤。");

        var tracker = new ShipFlightHudPresentationTracker();
        var samples = new JArray();
        ShipLandingReceipt receipt = default;
        foreach (JToken value in steps)
        {
            if (value is not JObject step) throw new ArgumentException("高度 HUD 步骤必须是对象。");
            double now = Number(step, "time", 0d);
            string world = Text(step, "world", "hud-world");
            if (Flag(step, "clearReceipt", false)) receipt = default;
            if (step["receipt"] is JObject suppliedReceipt)
                receipt = ShipFlightHudProbeReceipt(suppliedReceipt, now);
            if (Flag(step, "reset", false)) tracker.Reset();
            if (step["manualExitShip"] != null) tracker.CancelCurrentFlight(Text(step, "manualExitShip", string.Empty));
            if (step["manualEnterShip"] != null) tracker.ResumeCurrentFlight(Text(step, "manualEnterShip", string.Empty));
            ShipFlightPhase phase = Text(step, "phase", "Lifting") switch
            {
                "Lifting" => ShipFlightPhase.Lifting,
                "Cruising" => ShipFlightPhase.Cruising,
                "SurfaceDescending" => ShipFlightPhase.SurfaceDescending,
                "Landed" => ShipFlightPhase.Landed,
                "Floating" => ShipFlightPhase.Floating,
                _ => throw new ArgumentException("未知高度 HUD 飞行阶段。")
            };
            var snapshot = new ShipFlightHudSnapshot(Flag(step, "driving", false),
                Flag(step, "landed", false), Text(step, "ship", "hud-ship"), phase,
                ShipFlightHudProbeHeight(step), Number(step, "verticalSpeed", -1d), receipt);

            // 连续调用正式展示跟踪器，探针只回传状态，显隐与落地结论交给 Lua。
            ShipFlightHudPresentationState state = tracker.Advance(snapshot, Flag(step, "alive", true), world, now);
            samples.Add(new JObject
            {
                ["visible"] = state.Visible, ["touchdown"] = state.Touchdown,
                ["groundContact"] = state.GroundContact, ["nearGround"] = state.NearGround,
                ["height"] = state.HeightMeters, ["maximum"] = state.ScaleMaximum,
                ["normalized"] = state.NormalizedHeight,
                ["number"] = ShipFlightHudPresentationRules.FormatHeightNumber(state.HeightMeters, state.GroundContact)
            });
        }
        return new JObject { ["samples"] = samples };
    }

    private static ShipLandingReceipt ShipFlightHudProbeReceipt(JObject args, double fallbackTime)
        => new(Text(args, "ship", "hud-ship"), Text(args, "event", "hud-landing"),
            Text(args, "world", "hud-world"), Flag(args, "water", false), Number(args, "time", fallbackTime));

    #endregion

    #region 飞船落地凭证的会话快照往返

    private static JObject ServiceShipFlightHudReceiptMemory(JObject args)
    {
        ShipLandingReceipt receipt = ShipFlightHudProbeReceipt(args, 123.5d);
        var state = new SpaceSessionState { SimulationSeconds = 125d };
        state.Ships.Add(new ShipState { ShipId = receipt.ShipId });
        state.Flights.Add(new ShipFlightState { ShipId = receipt.ShipId, Phase = ShipFlightPhase.Landed,
            HeightMeters = 0d, LandingResolved = true, LandingEventId = receipt.EventId });
        state.Passengers.Add(new SpacePassengerState { ProfileId = "hud-memory-fixture", LastShipLanding = receipt });
        state.Ships.Clear();
        state.Flights.Clear();

        // 使用正式乘员模型的 JSON 路径检查清船后的凭证，不载入世界或访问磁盘。
        string json = JsonConvert.SerializeObject(state);
        var restored = JsonConvert.DeserializeObject<SpaceSessionState>(json)
            ?? throw new InvalidOperationException("高度 HUD 会话快照读回为空。");
        ShipLandingReceipt saved = restored.Passengers[0].LastShipLanding;
        return new JObject
        {
            ["shipCount"] = restored.Ships.Count, ["flightCount"] = restored.Flights.Count,
            ["passengerCount"] = restored.Passengers.Count, ["profileId"] = restored.Passengers[0].ProfileId,
            ["valid"] = saved.IsValid, ["shipId"] = saved.ShipId, ["eventId"] = saved.EventId,
            ["worldKey"] = saved.WorldKey, ["water"] = saved.IsWater, ["time"] = saved.SimulationSeconds,
            ["stableRoundtrip"] = JToken.DeepEquals(JToken.Parse(json),
                JToken.Parse(JsonConvert.SerializeObject(restored)))
        };
    }

    #endregion
}
