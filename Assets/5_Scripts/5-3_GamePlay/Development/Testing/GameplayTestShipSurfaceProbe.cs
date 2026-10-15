using FlatWorld.Combat;
using FlatWorld.Spaceflight;
using Newtonsoft.Json.Linq;

/// <summary>独立飞行状态调用正式空地规则，断言由 Lua 负责。</summary>
public static class GameplayTestShipSurfaceProbe
{
    #region 飞船整体运动模式与空地受击采样

    public static JObject Capture()
    {
        var samples = new JArray
        {
            Sample("no-flight", null),
            Sample("landed", Flight(ShipFlightPhase.Landed, 0d)),
            Sample("landed-stale-height", Flight(ShipFlightPhase.Landed, 100d)),
            Sample("floating", Flight(ShipFlightPhase.Floating, 100d)),
            Sample("ignited-ground", Flight(ShipFlightPhase.Landed, 0d, true)),
            Sample("lifting-negative", Flight(ShipFlightPhase.Lifting, -10d, true)),
            Sample("lifting-zero", Flight(ShipFlightPhase.Lifting, 0d, true)),
            Sample("lifting-below", Flight(ShipFlightPhase.Lifting, 24.999d, true)),
            Sample("lifting-threshold", Flight(ShipFlightPhase.Lifting, 25d, true)),
            Sample("lifting-above", Flight(ShipFlightPhase.Lifting, 25.001d, true)),
            Sample("lifting-unpowered", Flight(ShipFlightPhase.Lifting, 25d)),
            Sample("descending-above", Flight(ShipFlightPhase.SurfaceDescending, 25.001d)),
            Sample("descending-threshold", Flight(ShipFlightPhase.SurfaceDescending, 25d)),
            Sample("descending-below", Flight(ShipFlightPhase.SurfaceDescending, 24.999d)),
            Sample("descending-zero", Flight(ShipFlightPhase.SurfaceDescending, 0d)),
            Sample("cruising", Flight(ShipFlightPhase.Cruising, 0d, true)),
            Sample("cruising-unpowered", Flight(ShipFlightPhase.Cruising, 0d)),
            Sample("lifting-unpowered-zero", Flight(ShipFlightPhase.Lifting, 0d)),
            Sample("ocean-no-flight", null, ShipPlatformKind.Ocean),
            Sample("space-no-flight", null, ShipPlatformKind.Space),
            Sample("space-stale-landed", Flight(ShipFlightPhase.Landed, 100d), ShipPlatformKind.Space),
            Sample("space-stale-floating", Flight(ShipFlightPhase.Floating, 100d), ShipPlatformKind.Space),
            Sample("no-ship-cruising", Flight(ShipFlightPhase.Cruising, 0d), hasShip: false)
        };
        return new JObject
        {
            ["operation"] = "ship_surface_state", ["scope"] = "production-domain",
            ["airborneVisualRise"] = ShipSurfaceState.AirborneVisualRise, ["samples"] = samples,
            ["limitations"] = "只采样正式整体运动模式、排序资格、视觉升高与投送资格；不创建碰撞组件，也不证明真实远程命中、层级或联机。"
        };
    }

    private static ShipFlightState Flight(ShipFlightPhase phase, double height, bool ignited = false) =>
        new() { Phase = phase, HeightMeters = height, Ignited = ignited };

    // 只调用正式规则，不在夹具中复制阈值、阶段或伤害资格算法。
    private static JObject Sample(string name, ShipFlightState flight,
        ShipPlatformKind platform = ShipPlatformKind.Land, bool hasShip = true)
    {
        ShipState ship = hasShip ? new ShipState { PlatformKind = platform } : null;
        var damage = new JObject();
        Add("melee", CombatDeliveryCapabilities.None);
        Add("projectile-without-airborne", CombatDeliveryCapabilities.Projectile);
        Add("ranged", CombatDeliveryCapabilities.Projectile | CombatDeliveryCapabilities.AirborneTargets);
        Add("airborne-capability", CombatDeliveryCapabilities.AirborneTargets);
        Add("airborne-only", CombatDeliveryCapabilities.AirborneTargets | CombatDeliveryCapabilities.AirborneOnly);
        Add("airborne-only-projectile", CombatDeliveryCapabilities.Projectile |
            CombatDeliveryCapabilities.AirborneTargets | CombatDeliveryCapabilities.AirborneOnly);
        Add("airborne-only-without-capability", CombatDeliveryCapabilities.AirborneOnly);
        Add("airborne-only-projectile-without-capability",
            CombatDeliveryCapabilities.Projectile | CombatDeliveryCapabilities.AirborneOnly);
        return new JObject
        {
            ["name"] = name, ["phase"] = flight?.Phase.ToString() ?? "none",
            ["platform"] = ship?.PlatformKind.ToString() ?? "none",
            ["heightMeters"] = flight?.HeightMeters ?? 0d, ["ignited"] = flight?.Ignited ?? false,
            ["wholeShipBody"] = ShipSurfaceState.UsesWholeShipBody(ship, flight),
            ["visualRise"] = ShipSurfaceState.VisualRise(flight),
            ["airborne"] = ShipSurfaceState.IsAirborne(flight),
            ["elevated"] = ShipSurfaceState.IsElevated(flight), ["damage"] = damage
        };

        void Add(string key, CombatDeliveryCapabilities capabilities) =>
            damage[key] = ShipSurfaceState.AllowsDamage(flight, capabilities);
    }

    #endregion
}
