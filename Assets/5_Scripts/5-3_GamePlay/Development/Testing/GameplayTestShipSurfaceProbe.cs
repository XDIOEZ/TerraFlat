using System;
using System.Collections.Generic;
using FlatWorld.Combat;
using FlatWorld.Spaceflight;
using Newtonsoft.Json.Linq;
using UnityEngine;

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
            Sample("no-ship-cruising", Flight(ShipFlightPhase.Cruising, 0d), hasShip: false),
            Sample("ordinary-ground", null, hasShip: false)
        };
        return new JObject
        {
            ["operation"] = "ship_surface_state", ["scope"] = "production-domain",
            ["airborneVisualRise"] = ShipSurfaceState.AirborneVisualRise, ["samples"] = samples,
            ["groundAssembly"] = GroundAssemblySamples(),
            ["groundRestoreGate"] = GroundRestoreGateSamples(),
            ["uprightAssemblies"] = UprightAssemblySamples(),
            ["limitations"] = "只采样正式整体运动模式、排序资格、视觉升高与投送资格；不创建碰撞组件，也不证明真实远程命中、层级或联机。"
        };
    }

    // 失败输入未变化时保持阻塞，真实占地、结构和落点变化立即重新规划。
    private static JArray GroundRestoreGateSamples()
    {
        var gate = new ShipGroundRestoreGate();
        var ship = new ShipState { ShipId = "restore-gate", PositionX = 10d, PositionY = -20d };
        var members = new List<ShipState> { ship };
        var samples = new JArray();
        void Sample(string name, uint revision = 7, string world = "surface")
            => samples.Add(new JObject { ["name"] = name, ["attempt"] = gate.ShouldAttempt(world, revision, members) });
        Sample("initial");
        gate.Block("surface", 7, members);
        Sample("unchanged");
        bool repeatedBlocked = true;
        for (int i = 0; i < 1000; i++) repeatedBlocked &= !gate.ShouldAttempt("surface", 7, members);
        samples.Add(new JObject { ["name"] = "repeat-1000", ["attempt"] = !repeatedBlocked });
        Sample("occupancy-changed", 8);
        Sample("world-changed", world: "other");
        ship.PositionX++; Sample("position-changed"); ship.PositionX--;
        ship.AngleRadians = Math.PI * .5d; Sample("angle-changed"); ship.AngleRadians = 0d;
        ship.CenterOfMassLocalX = .25d; Sample("center-changed"); ship.CenterOfMassLocalX = 0d;
        ship.CellSizeMeters = 2d; Sample("cell-size-changed"); ship.CellSizeMeters = 1d;
        ship.NotifyStructureChanged(); Sample("structure-changed");
        gate.Block("surface", 7, members);
        ship.Pieces.Add(new ShipPieceState()); Sample("piece-count-changed"); ship.Pieces.Clear();
        members.Add(new ShipState { ShipId = "docked-member" }); Sample("member-added");
        gate.Block("surface", 7, members);
        members.RemoveAt(1); Sample("member-removed");
        gate.Block("surface", 7, members);
        members[0] = new ShipState { ShipId = ship.ShipId, PositionX = ship.PositionX, PositionY = ship.PositionY };
        Sample("identity-replaced");
        members[0] = ship;
        Sample("restored-input");
        gate = new ShipGroundRestoreGate(); Sample("preparation-replaced");
        gate.Block("surface", 7, members); Sample("blocked-again");
        return samples;
    }

    private static JObject GroundAssemblySamples()
    {
        var angles = new JArray();
        foreach (double degrees in new[] { 0d, 44.999d, 45d, 45.001d, 89.999d, 135d, 179.999d,
            225d, 315d, 359.999d, -44.999d, -45d, -135d, -225d, -315d, 765d })
            angles.Add(new JObject { ["degrees"] = degrees,
                ["quarterTurns"] = ShipGroundAssembly.SnapQuarterTurns(degrees / 180d * Math.PI) });
        var anchors = new JArray();
        foreach (var rectangle in new[] { (Name: "negative-x", X: -3, Y: 2, Width: 2, Height: 3),
            (Name: "negative-y", X: 2, Y: -4, Width: 3, Height: 2) })
        for (int turns = 0; turns < 4; turns++)
        {
            Vector2Int cell = ShipGroundAssembly.RotateAnchor(rectangle.X, rectangle.Y,
                rectangle.Width, rectangle.Height, turns);
            anchors.Add(new JObject { ["name"] = rectangle.Name, ["quarterTurns"] = turns,
                ["x"] = cell.x, ["y"] = cell.y,
                ["negativeOriginX"] = cell.x - 10, ["negativeOriginY"] = cell.y - 20 });
        }
        var origins = new JArray();
        foreach (var center in new[] { new Vector2(1.25f, 2.75f), new Vector2(-4.75f, 7.125f) })
        foreach (var origin in new[] { new Vector2(10.49f, 20.49f), new Vector2(-10.5f, -20.5f) })
        {
            var ship = new ShipState { PositionX = origin.x + (double)center.x,
                PositionY = origin.y + (double)center.y, CenterOfMassLocalX = center.x, CenterOfMassLocalY = center.y };
            Vector2Int snapped = ShipGroundAssembly.SnapOrigin(ship);
            origins.Add(new JObject { ["originX"] = origin.x, ["originY"] = origin.y,
                ["centerX"] = center.x, ["centerY"] = center.y, ["x"] = snapped.x, ["y"] = snapped.y });
        }
        // 用不同配置层及机器覆盖层采样正式落地层号，防止同格光纤再次被当成设备。
        var layers = new JArray();
        foreach (var sample in new[]
        {
            (Name: "floor", Kind: ShipPieceKind.Floor, Layer: -1, Machine: (int?)null),
            (Name: "floor-with-machine", Kind: ShipPieceKind.Floor, Layer: -1, Machine: (int?)0),
            (Name: "wall", Kind: ShipPieceKind.Wall, Layer: 0, Machine: (int?)null),
            (Name: "fiber", Kind: ShipPieceKind.Fiber, Layer: 5, Machine: (int?)null),
            (Name: "custom-equipment", Kind: ShipPieceKind.Equipment, Layer: 4, Machine: (int?)null),
            (Name: "machine", Kind: ShipPieceKind.Equipment, Layer: 5, Machine: (int?)0),
            (Name: "wire", Kind: ShipPieceKind.Wire, Layer: 0, Machine: (int?)3),
            (Name: "pipe", Kind: ShipPieceKind.Pipe, Layer: 0, Machine: (int?)2)
        })
            layers.Add(new JObject { ["name"] = sample.Name,
                ["layer"] = Mod_ShipPart.ResolveGroundOccupancyLayer(sample.Kind, sample.Layer, sample.Machine) });
        return new JObject { ["angles"] = angles, ["anchors"] = anchors, ["origins"] = origins, ["layers"] = layers };
    }

    // 使用正式组合缓存和成员提交入口，覆盖单船、对接船及残留角速度。
    private static JArray UprightAssemblySamples()
    {
        var samples = new JArray();
        foreach (int count in new[] { 1, 2 })
        {
            var ships = new List<ShipState>();
            for (int index = 0; index < count; index++)
                ships.Add(new ShipState { ShipId = "upright-" + index, MassKg = 10d, InertiaKgM2 = 5d,
                    PositionX = index * 4d, PositionY = index * 2d, VelocityX = 3d, VelocityY = -2d,
                    AngleRadians = .3d + index, AngularVelocityRadiansPerSecond = 2d - index,
                    Pieces = new List<ShipPieceState> { new() { PieceId = "upright-port-" + index, Kind = ShipPieceKind.DockingPort } } });
            var pairs = new List<ShipDockingPairState>();
            if (count == 2) pairs.Add(new ShipDockingPairState { PortAId = "upright-port-0", PortBId = "upright-port-1",
                ConfirmedA = true, ConfirmedB = true, Connected = true });
            var cache = new ShipAssemblyCache();
            List<ShipAssemblyState> groups = cache.Refresh(ships, pairs);
            ShipAssemblyState assembly = groups[0];
            var sample = new JObject { ["members"] = count, ["groups"] = groups.Count,
                ["refreshedAngle"] = assembly.AngleRadians, ["refreshedAngularVelocity"] = assembly.AngularVelocityRadiansPerSecond };
            assembly.PositionX += 5d; assembly.PositionY -= 3d;
            assembly.AngleRadians = .7d; assembly.AngularVelocityRadiansPerSecond = 4d;
            ShipDockingService.RigidlyCarryMembers(assembly, ships);
            var carried = new JArray();
            foreach (ShipState ship in ships)
                carried.Add(new JObject { ["x"] = ship.PositionX, ["y"] = ship.PositionY,
                    ["velocityX"] = ship.VelocityX, ["velocityY"] = ship.VelocityY,
                    ["angle"] = ship.AngleRadians, ["angularVelocity"] = ship.AngularVelocityRadiansPerSecond });
            sample["angle"] = assembly.AngleRadians; sample["angularVelocity"] = assembly.AngularVelocityRadiansPerSecond;
            sample["carried"] = carried; samples.Add(sample);
        }
        return samples;
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
