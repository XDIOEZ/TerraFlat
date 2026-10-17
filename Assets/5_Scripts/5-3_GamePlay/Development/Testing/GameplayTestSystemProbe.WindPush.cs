using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using Unity.Mathematics;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 风推动时间与空间正式采样

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterWindPushRules() => Register("environment", "wind_push_transition", WorldWindPushTransition);

    private static RegionalWeatherEngine WindPushEngine(float strength, WorldTopologyDomain domain = default)
    {
        var engine = new RegionalWeatherEngine(new RegionalWeatherSettings(), 17413, domain, 16, 16);
        WindPushSource(engine, new Int2(0, 0), new float2(1, 0), strength);
        return engine;
    }

    private static void WindPushSource(RegionalWeatherEngine engine, Int2 region, float2 direction, float strength)
    {
        var settings = new RegionalWeatherSettings();
        engine.RegisterChunk(region, settings.CreateProfile(new ChunkWeatherClimate(
            .5f, direction.x, direction.y, 1f, 20f)), 0d);
        engine.ForceElement(region, 0d, WeatherElementIds.Wind, strength, 100f);
    }

    private static JObject WindPushSample(RegionalWindPushField field, double x = 64d, double y = 64d, int level = 8)
    {
        Vector2 velocity = field.Sample(new double2(x, y), level);
        return new JObject { ["x"] = velocity.x, ["y"] = velocity.y };
    }

    private static JObject WindPushBoundary(RegionalWindPushField field, double x, double y, bool corner = false)
        => new()
        {
            ["before"] = WindPushSample(field, x - .001d, y - (corner ? .001d : 0d)),
            ["middle"] = WindPushSample(field, x, y),
            ["after"] = WindPushSample(field, x + .001d, y + (corner ? .001d : 0d))
        };

    private static JObject WorldWindPushTransition(JObject args)
    {
        // 只推进独立正式风场并返回原始推速，期望数值和连续性判断全部留给 Lua。
        var origin = new Int2(0, 0);
        var engine = WindPushEngine(.8f);
        var field = new RegionalWindPushField();
        var result = new JObject();
        field.Synchronize(engine, 0d, 3f);
        result["start"] = WindPushSample(field);
        field.Synchronize(engine, 1.5d, 3f);
        result["rising"] = WindPushSample(field);
        field.Synchronize(engine, 3d, 3f);
        result["steady"] = WindPushSample(field);
        engine.ForceElement(origin, 0d, WeatherElementIds.Wind, .9f, 100f);
        field.Synchronize(engine, 3d, 3f);
        result["levelChanged"] = WindPushSample(field);
        field.Synchronize(engine, 4.5d, 3f);
        result["levelMiddle"] = WindPushSample(field);
        field.Synchronize(engine, 6d, 3f);
        result["levelEnd"] = WindPushSample(field);
        engine.ForceElements(origin, 0d, 100d, windStrength: 0f);
        field.Synchronize(engine, 6d, 3f);
        result["stopping"] = WindPushSample(field);
        field.Synchronize(engine, 7.5d, 3f);
        result["falling"] = WindPushSample(field);
        result["pushDuringTail"] = field.HasWindPush;
        field.Synchronize(engine, 9d, 3f);
        result["stopped"] = WindPushSample(field);
        result["pushAfterTail"] = field.HasWindPush;

        engine = WindPushEngine(1f);
        field.Synchronize(engine, 0d, 3f);
        field.Synchronize(engine, 1.5d, 3f);
        result["interruptedBefore"] = WindPushSample(field);
        engine.ForceElement(origin, 0d, WeatherElementIds.Wind, .8f, 100f);
        field.Synchronize(engine, 1.5d, 3f);
        result["interruptedAfter"] = WindPushSample(field);
        field.Synchronize(engine, 3d, 3f);
        result["interruptedMiddle"] = WindPushSample(field);
        field.Synchronize(engine, 4.5d, 3f);
        engine.TryGetSnapshot(origin, out RegionalWeatherSnapshot reversed);
        reversed.WindX = -.8f;
        reversed.Revision++;
        engine.ApplyReplicatedSnapshot(reversed);
        field.Synchronize(engine, 4.5d, 3f);
        result["reverseStart"] = WindPushSample(field);
        field.Synchronize(engine, 6d, 3f);
        result["reverseMiddle"] = WindPushSample(field);
        field.Synchronize(engine, 7.5d, 3f);
        result["reverseEnd"] = WindPushSample(field);
        result["heavy"] = WindPushSample(field, level: 11);

        engine = WindPushEngine(1f);
        field.Synchronize(engine, 0d, 0f);
        result["instant"] = WindPushSample(field);
        result["required9"] = WindPushSample(field, level: 9);
        result["required10"] = WindPushSample(field, level: 10);
        field.Reset();
        result["reset"] = WindPushSample(field);
        result["pushAfterReset"] = field.HasWindPush;
        field.Synchronize(engine, 0d, 3f);
        result["newWorldStart"] = WindPushSample(field);
        field.Synchronize(engine, 3d, 3f);
        result["missingNeighbor"] = WindPushSample(field, 128d);
        result["createdNeighbor"] = engine.TryGetSnapshot(new Int2(128, 0), out _);

        WindPushSpatialSamples(result);
        WindPushReplicaSamples(result);
        engine = WindPushEngine(.9f);
        engine.ForceElement(origin, 0d, WeatherElementIds.Wind, .9f, 4f);
        field.Synchronize(engine, 0d, 3f);
        field.Synchronize(engine, 3d, 3f);
        engine.Advance(origin, 4d, false);
        field.Synchronize(engine, 3d, 3f);
        result["expiryStart"] = WindPushSample(field);
        field.Synchronize(engine, 4.5d, 3f);
        result["expiryTail"] = WindPushSample(field);
        field.Synchronize(engine, 6d, 3f);
        result["expiryEnd"] = WindPushSample(field);
        return result;
    }

    private static void WindPushSpatialSamples(JObject result)
    {
        var engine = WindPushEngine(1f);
        WindPushSource(engine, new Int2(128, 0), new float2(1, 0), 0f);
        var field = new RegionalWindPushField();
        field.Synchronize(engine, 0d, 3f);
        field.Synchronize(engine, 1.5d, 3f);
        result["transitionBoundary"] = WindPushSample(field, 128d);
        field.Synchronize(engine, 3d, 3f);
        result["spatial"] = WindPushBoundary(field, 128d, 64d);

        var corner = new RegionalWeatherEngine(new RegionalWeatherSettings(), 17413, default, 16, 16);
        WindPushSource(corner, new Int2(0, 0), new float2(1, 0), 1f);
        WindPushSource(corner, new Int2(128, 0), new float2(0, 1), .9f);
        WindPushSource(corner, new Int2(0, 128), new float2(-1, 0), .8f);
        WindPushSource(corner, new Int2(128, 128), new float2(0, -1), .79f);
        field.Synchronize(corner, 0d, 3f);
        field.Synchronize(corner, 3d, 3f);
        result["corner"] = WindPushBoundary(field, 128d, 128d, true);

        var domain = new WorldTopologyDomain(new int2(-128, -128), new int2(256, 256), true);
        engine = WindPushEngine(1f, domain);
        WindPushSource(engine, new Int2(-128, 0), new float2(1, 0), 0f);
        field.Synchronize(engine, 0d, 3f);
        field.Synchronize(engine, 3d, 3f);
        result["wrapped"] = WindPushBoundary(field, 128d, 64d);
        result["wrappedEquivalent"] = WindPushSample(field, -128d);
    }

    private static void WindPushReplicaSamples(JObject result)
    {
        var origin = new Int2(0, 0);
        RegionalWeatherEngine authority = WindPushEngine(1f);
        authority.TryGetSnapshot(origin, out RegionalWeatherSnapshot snapshot);
        var replica = new RegionalWeatherEngine(new RegionalWeatherSettings(), 17413, default, 16, 16);
        replica.ApplyReplicatedSnapshot(snapshot);
        var field = new RegionalWindPushField();
        field.Synchronize(replica, 0d, 3f);
        result["replicaStart"] = WindPushSample(field);
        field.Synchronize(replica, 1.5d, 3f);
        result["replicaMiddle"] = WindPushSample(field);
        replica.TryGetSnapshot(origin, out RegionalWeatherSnapshot raw);
        result["rawStrength"] = raw.WindStrength;
        snapshot.WindX = snapshot.WindY = snapshot.WindStrength = 0f;
        snapshot.Revision++;
        replica.ApplyReplicatedSnapshot(snapshot);
        field.Synchronize(replica, 1.5d, 3f);
        field.Synchronize(replica, 3d, 3f);
        result["replicaTail"] = WindPushSample(field);
        field.Synchronize(replica, 4.5d, 3f);
        result["replicaStopped"] = WindPushSample(field);
    }

    #endregion
}
