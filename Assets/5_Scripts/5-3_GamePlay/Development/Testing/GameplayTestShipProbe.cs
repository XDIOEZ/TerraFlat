using System;
using System.Collections.Generic;
using System.Globalization;
using FlatWorld.Spaceflight;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>独立船体样本调用正式领域规则，只返回实际读数，断言由 Lua 用例负责。</summary>
public static class GameplayTestShipProbe
{
    #region 领域探针入口

    public static bool Supports(string operation) => operation is "ship_structure" or "ship_docking" or
        "ship_atmosphere" or "ship_mass" or "ship_propulsion" or "ship_contacts" or
        "ship_snapshots" or "ship_engine_placement" or "ship_fuel" or "ship_optical" or "ship_collider_batch";

    // 样本不注册到正式会话，也不推进当前玩家、机器、存档或世界时钟。
    public static JObject Capture(string operation)
    {
        JObject result = operation switch
        {
            "ship_structure" => CaptureStructure(),
            "ship_docking" => CaptureDocking(),
            "ship_atmosphere" => CaptureAtmosphere(),
            "ship_mass" => CaptureMass(),
            "ship_propulsion" => CapturePropulsion(),
            "ship_contacts" => CaptureContacts(),
            "ship_snapshots" => CaptureSnapshots(),
            "ship_engine_placement" => CaptureEnginePlacement(),
            "ship_fuel" => CaptureFuel(),
            "ship_optical" => CaptureOptical(),
            "ship_collider_batch" => CaptureColliderBatch(),
            _ => throw new ArgumentException("未知飞船领域探针：" + operation, nameof(operation))
        };
        result["operation"] = operation;
        result["scope"] = "production-domain";
        return result;
    }

    private static ShipState NewShip(string id) => new()
    {
        ShipId = "gameplay-probe-" + id,
        PlatformKind = ShipPlatformKind.Space,
        CellSizeMeters = 1d
    };

    private static ShipPieceState Piece(string id, ShipPieceKind kind, int x, int y, double mass = 10d) => new()
    {
        PieceId = "gameplay-probe-" + id,
        ItemId = "domain-fixture",
        Kind = kind,
        CellX = x,
        CellY = y,
        Layer = kind == ShipPieceKind.Floor ? 0 : 1,
        MassKg = mass,
        RequiresFloorSupport = kind != ShipPieceKind.Floor
    };

    private static JObject Vector(SpaceVector2 value) => new() { ["x"] = value.X, ["y"] = value.Y };
    private static JObject Position(SpaceSectorPosition value) => new()
    {
        ["sectorX"] = value.Sector.X.ToString(CultureInfo.InvariantCulture),
        ["sectorY"] = value.Sector.Y.ToString(CultureInfo.InvariantCulture),
        ["x"] = value.LocalMeters.X, ["y"] = value.LocalMeters.Y
    };
    private static JObject ShipReading(ShipState ship) => new()
    {
        ["id"] = ship.ShipId, ["pieces"] = ship.Pieces.Count, ["wreck"] = ship.IsWreck,
        ["mass"] = ship.MassKg, ["centerX"] = ship.CenterOfMassLocalX,
        ["centerY"] = ship.CenterOfMassLocalY, ["inertia"] = ship.InertiaKgM2,
        ["x"] = ship.PositionX, ["y"] = ship.PositionY,
        ["velocityX"] = ship.VelocityX, ["velocityY"] = ship.VelocityY,
        ["sectorX"] = ship.Sector.X.ToString(CultureInfo.InvariantCulture),
        ["sectorY"] = ship.Sector.Y.ToString(CultureInfo.InvariantCulture)
    };
    private static JArray ShipReadings(IEnumerable<ShipState> ships)
    {
        var result = new JArray();
        foreach (ShipState ship in ships) result.Add(ShipReading(ship));
        return result;
    }

    #endregion

    #region 船体碰撞格合批

    private static JObject ColliderReading(ShipColliderGeometry geometry, bool rebuilt)
    {
        var rectangles = new JArray();
        foreach (ShipCollisionRect rectangle in geometry.Rectangles)
            rectangles.Add(new JObject
            {
                ["x"] = rectangle.X, ["y"] = rectangle.Y,
                ["width"] = rectangle.Width, ["height"] = rectangle.Height,
                ["blocksPassage"] = rectangle.BlocksPassage
            });
        return new JObject { ["rebuilt"] = rebuilt, ["rebuilds"] = geometry.RebuildCount,
            ["rectangles"] = rectangles };
    }

    private static JObject ColliderSample(ShipState ship)
    {
        var geometry = new ShipColliderGeometry();
        JObject reading = ColliderReading(geometry, geometry.Synchronize(ship));
        reading["inputPieces"] = ship.Pieces.Count;
        return reading;
    }

    private static JObject CaptureColliderBatch()
    {
        var full = NewShip("collider-full");
        for (int y = 0; y < 8; y++)
        for (int x = 0; x < 12; x++)
            full.Pieces.Add(Piece($"collider-floor-{x}-{y}", ShipPieceKind.Floor, x, y));
        var fullGeometry = new ShipColliderGeometry();
        JObject initial = ColliderReading(fullGeometry, fullGeometry.Synchronize(full));
        initial["inputPieces"] = full.Pieces.Count;
        full.Pieces.Add(Piece("collider-console", ShipPieceKind.Console, 5, 3));
        full.NotifyStructureChanged();
        JObject overlapping = ColliderReading(fullGeometry, fullGeometry.Synchronize(full));
        overlapping["inputPieces"] = full.Pieces.Count;
        var wall = Piece("collider-wall", ShipPieceKind.Wall, 5, 3);
        wall.BlocksMovement = true;
        full.Pieces.Add(wall); full.NotifyStructureChanged();
        JObject blockingOverlap = ColliderReading(fullGeometry, fullGeometry.Synchronize(full));
        blockingOverlap["inputPieces"] = full.Pieces.Count;

        var hole = NewShip("collider-hole");
        var corner = NewShip("collider-corner");
        for (int y = 0; y < 3; y++)
        for (int x = 0; x < 3; x++)
        {
            if (x != 1 || y != 1) hole.Pieces.Add(Piece($"hole-{x}-{y}", ShipPieceKind.Floor, x, y));
            if (x != 2 || y != 2) corner.Pieces.Add(Piece($"corner-{x}-{y}", ShipPieceKind.Floor, x, y));
        }
        var islands = NewShip("collider-islands");
        islands.Pieces.Add(Piece("island-origin", ShipPieceKind.Floor, 0, 0));
        islands.Pieces.Add(Piece("island-diagonal", ShipPieceKind.Floor, 1, 1));
        islands.Pieces.Add(Piece("island-left", ShipPieceKind.Floor, 5, 0));
        islands.Pieces.Add(Piece("island-right", ShipPieceKind.Floor, 6, 0));
        var rotated = NewShip("collider-rotated");
        var odd = Piece("rotated-odd", ShipPieceKind.Floor, -4, -7);
        odd.Width = 2; odd.Height = 3; odd.QuarterTurns = -1;
        rotated.Pieces.Add(odd);
        var even = Piece("rotated-even", ShipPieceKind.Floor, -9, -3);
        even.Width = 2; even.Height = 3; even.QuarterTurns = 2;
        rotated.Pieces.Add(even);
        var exclusions = NewShip("collider-exclusions");
        exclusions.Pieces.Add(Piece("exclusion-floor", ShipPieceKind.Floor, 0, 0));
        exclusions.Pieces.Add(Piece("exclusion-pipe", ShipPieceKind.Pipe, 1, 0));
        exclusions.Pieces.Add(Piece("exclusion-wire", ShipPieceKind.Wire, 20, 20));
        exclusions.Pieces.Add(Piece("exclusion-fiber", ShipPieceKind.Fiber, 21, 20));
        var openDoor = Piece("exclusion-open-door", ShipPieceKind.Door, 22, 20);
        openDoor.BlocksMovement = true; openDoor.DoorOpen = true;
        exclusions.Pieces.Add(openDoor);
        var destroyed = Piece("exclusion-destroyed", ShipPieceKind.Floor, 23, 20);
        destroyed.Destroyed = true; exclusions.Pieces.Add(destroyed);
        var dead = Piece("exclusion-dead", ShipPieceKind.Equipment, 24, 20);
        dead.Health = 0d; exclusions.Pieces.Add(dead);
        var blockingKinds = NewShip("collider-blocking-kinds");
        blockingKinds.Pieces.Add(Piece("blocking-floor-left", ShipPieceKind.Floor, 0, 0));
        blockingKinds.Pieces.Add(Piece("blocking-floor-right", ShipPieceKind.Floor, 1, 0));
        var blockingWall = Piece("blocking-kind-wall", ShipPieceKind.Wall, 0, 0);
        var blockingEquipment = Piece("blocking-kind-equipment", ShipPieceKind.Equipment, 1, 0);
        blockingWall.BlocksMovement = true; blockingEquipment.BlocksMovement = true;
        blockingKinds.Pieces.Add(blockingWall); blockingKinds.Pieces.Add(blockingEquipment);

        var cached = NewShip("collider-cache");
        var floor = Piece("cache-floor", ShipPieceKind.Floor, 0, 0);
        var door = Piece("cache-door", ShipPieceKind.Door, 1, 0);
        door.BlocksMovement = true;
        cached.Pieces.Add(floor); cached.Pieces.Add(door);
        var cache = new ShipColliderGeometry();
        JObject closed = ColliderReading(cache, cache.Synchronize(cached));
        var stableFrames = new JArray();
        for (int index = 0; index < 5; index++)
            stableFrames.Add(ColliderReading(cache, cache.Synchronize(cached)));
        cached.PositionX = 123d; cached.PositionY = -45d; cached.AngleRadians = .7d;
        cached.VelocityX = 9d; cached.VelocityY = -2d; cached.AngularVelocityRadiansPerSecond = .5d;
        JObject moved = ColliderReading(cache, cache.Synchronize(cached));
        cached.MassContributions.Add(new ShipMassContribution { SourceId = "collider-cargo",
            LocalX = 4d, LocalY = 2d, MassKg = 30d, Kind = ShipMassKind.Cargo });
        ShipStructureService.RecalculateMass(cached);
        JObject massChanged = ColliderReading(cache, cache.Synchronize(cached));
        ulong beforeDoorRevision = cached.StructureRevision;
        // 门只改变通行状态，领域缓存必须自行察觉；结构拆除仍使用正式失效通知。
        door.DoorOpen = true;
        JObject opened = ColliderReading(cache, cache.Synchronize(cached));
        door.DoorOpen = false;
        JObject reclosed = ColliderReading(cache, cache.Synchronize(cached));
        ulong afterDoorRevision = cached.StructureRevision;
        cached.Pieces.Remove(floor); cached.NotifyStructureChanged();
        JObject removed = ColliderReading(cache, cache.Synchronize(cached));
        bool destroyedByDamage = ShipStructureService.ApplyDamage(cached, door.PieceId, door.Health, "Lua 碰撞合批样本");
        JObject emptied = ColliderReading(cache, cache.Synchronize(cached));

        return new JObject
        {
            ["full"] = initial, ["overlapping"] = overlapping, ["blockingOverlap"] = blockingOverlap,
            ["hole"] = ColliderSample(hole), ["corner"] = ColliderSample(corner),
            ["islands"] = ColliderSample(islands), ["rotated"] = ColliderSample(rotated),
            ["exclusions"] = ColliderSample(exclusions), ["blockingKinds"] = ColliderSample(blockingKinds),
            ["closed"] = closed,
            ["stableFrames"] = stableFrames, ["moved"] = moved, ["massChanged"] = massChanged,
            ["opened"] = opened, ["reclosed"] = reclosed, ["removed"] = removed, ["emptied"] = emptied,
            ["doorStructureRevisionUnchanged"] = beforeDoorRevision == afterDoorRevision,
            ["destroyedByDamage"] = destroyedByDamage,
            ["limitations"] = "只读正式格合批与缓存输出，不创建 PhysicsScene2D；实际组件、船体碰撞和性能由现场验收。"
        };
    }

    #endregion

    #region 结构支撑与断裂

    private static JObject CaptureStructure()
    {
        var ship = NewShip("structure");
        var bridge = Piece("bridge", ShipPieceKind.Floor, 1, 0);
        ship.Pieces.Add(Piece("floor-left", ShipPieceKind.Floor, 0, 0));
        ship.Pieces.Add(bridge);
        ship.Pieces.Add(Piece("floor-right", ShipPieceKind.Floor, 2, 0));
        ship.Pieces.Add(Piece("console-left", ShipPieceKind.Console, 0, 0));
        ship.Pieces.Add(Piece("console-right", ShipPieceKind.Console, 2, 0));
        ship.Pieces.Add(Piece("engine-left", ShipPieceKind.Engine, 0, 0));
        ship.Pieces.Add(Piece("engine-right", ShipPieceKind.Engine, 2, 0));
        var unsupported = Piece("unsupported", ShipPieceKind.Equipment, 8, 0);
        ship.Pieces.Add(unsupported);
        var detached = new JArray();
        int inputPieces = ship.Pieces.Count;
        // 四邻接重建必须回收没有地板支撑的设备，并在桥梁摧毁后重新划分归属。
        List<ShipState> connected = ShipStructureService.RebuildAndSplit(ship, onDetached:
            (_, piece, reason) => detached.Add(new JObject { ["id"] = piece.PieceId, ["reason"] = reason }));
        var beforeRemoval = ShipReadings(connected);
        bool removed = ShipStructureService.ApplyDamage(ship, bridge.PieceId, bridge.Health, "Lua 领域样本");
        List<ShipState> split = ShipStructureService.RebuildAndSplit(ship);
        var rotated = Piece("rotated-floor", ShipPieceKind.Floor, -2, -3);
        rotated.Width = 2; rotated.Height = 3; rotated.QuarterTurns = 1;
        var rotatedCells = new JArray();
        foreach (ShipCell cell in ShipGeometry.Footprint(rotated))
            rotatedCells.Add(new JObject { ["x"] = cell.X, ["y"] = cell.Y });
        var stripped = NewShip("stripped");
        var orphan = Piece("orphan-console", ShipPieceKind.Console, 0, 0);
        stripped.Pieces.Add(orphan);
        List<ShipState> empty = ShipStructureService.RebuildAndSplit(stripped);
        return new JObject
        {
            ["inputPieces"] = inputPieces, ["connected"] = beforeRemoval,
            ["detached"] = detached, ["unsupportedDestroyed"] = unsupported.Destroyed,
            ["removeAccepted"] = removed, ["split"] = ShipReadings(split),
            ["bridgeDestroyed"] = bridge.Destroyed, ["originalId"] = ship.ShipId,
            ["rotatedFootprint"] = rotatedCells, ["noFloorGroups"] = empty.Count,
            ["noFloorWreck"] = stripped.IsWreck, ["noFloorOrphanDestroyed"] = orphan.Destroyed
        };
    }

    #endregion

    #region 对接确认与断开

    private static ShipState DockShip(string id, int quarterTurns)
    {
        var ship = NewShip(id);
        ship.Pieces.Add(Piece(id + "-floor", ShipPieceKind.Floor, 0, 0));
        ship.Pieces.Add(Piece(id + "-console", ShipPieceKind.Console, 0, 0));
        var port = Piece(id + "-port", ShipPieceKind.DockingPort, 0, 0);
        port.QuarterTurns = quarterTurns;
        ship.Pieces.Add(port);
        ShipStructureService.RecalculateMass(ship);
        ShipDockingService.SynchronizePorts(ship);
        return ship;
    }

    private static JObject CaptureDocking()
    {
        // 两个相向接口使用领域位姿求出实际贴合位置，再走双方确认事务。
        ShipState a = DockShip("dock-a", 0), b = DockShip("dock-b", 2);
        a.VelocityX = .2d; b.VelocityX = -.2d;
        ShipDockingService.PortWorldPose(a, a.DockingPorts[0], out double ax, out double ay, out _, out _);
        ShipDockingService.PortWorldPose(b, b.DockingPorts[0], out double bx, out double by, out _, out _);
        b.PositionX += ax - bx; b.PositionY += ay - by;
        var ships = new List<ShipState> { a, b };
        var pairs = new List<ShipDockingPairState>();
        bool candidate = ShipDockingService.TryCreateCandidate(ships, pairs, a.ShipId,
            a.DockingPorts[0].PortId, b.ShipId, b.DockingPorts[0].PortId, out ShipDockingPairState pair);
        int groupsBefore = ShipDockingService.RebuildAssemblies(ships, pairs).Count;
        bool first = pair != null && ShipDockingService.Confirm(ships, pairs, pair.PairId, a.ShipId);
        bool connectedAfterFirst = pair?.Connected == true;
        bool second = pair != null && ShipDockingService.Confirm(ships, pairs, pair.PairId, b.ShipId);
        List<ShipAssemblyState> groups = ShipDockingService.RebuildAssemblies(ships, pairs);
        bool connectedAfterSecond = pair?.Connected == true;
        int connectedGroups = groups.Count;
        double totalMomentumAfter = a.MassKg * a.VelocityX + b.MassKg * b.VelocityX;
        bool disconnect = pair != null && ShipDockingService.Disconnect(ships, pairs, pair.PairId);
        int disconnectedGroups = ShipDockingService.RebuildAssemblies(ships, pairs).Count;
        b.PositionX += 3d;
        bool distant = ShipDockingService.TryCreateCandidate(ships, pairs, a.ShipId,
            a.DockingPorts[0].PortId, b.ShipId, b.DockingPorts[0].PortId, out _);
        return new JObject
        {
            ["candidate"] = candidate, ["groupsBefore"] = groupsBefore,
            ["firstConfirm"] = first, ["connectedAfterFirst"] = connectedAfterFirst,
            ["secondConfirm"] = second, ["connectedAfterSecond"] = connectedAfterSecond,
            ["connectedGroups"] = connectedGroups, ["momentumAfter"] = totalMomentumAfter,
            ["disconnectAccepted"] = disconnect, ["disconnectedGroups"] = disconnectedGroups,
            ["connectedAfterDisconnect"] = pair?.Connected == true,
            ["distantCandidateAccepted"] = distant, ["ships"] = ShipReadings(ships)
        };
    }

    #endregion

    #region 舱气与通行边界

    private static ShipState CabinShip()
    {
        var ship = NewShip("cabin");
        for (int y = 0; y < 5; y++)
        for (int x = 0; x < 5; x++)
        {
            ship.Pieces.Add(Piece($"cabin-floor-{x}-{y}", ShipPieceKind.Floor, x, y));
            if (x != 0 && y != 0 && x != 4 && y != 4) continue;
            var boundary = Piece($"cabin-boundary-{x}-{y}",
                x == 2 && y == 0 ? ShipPieceKind.Door : ShipPieceKind.Wall, x, y);
            boundary.SealsAtmosphere = true; boundary.BlocksMovement = true;
            ship.Pieces.Add(boundary);
        }
        ShipAtmosphereService.RebuildCompartments(ship);
        return ship;
    }

    private static FluidBatch Gas(string id, decimal moles) =>
        FluidInventory.CreateBatch(FluidCatalog.Default.Find(id), moles, 0m, 293.15d);

    private static JObject Environment(ShipEnvironmentSnapshot value) => new()
    {
        ["sealed"] = value.IsSealed, ["floor"] = value.HasFloorSupport,
        ["pressure"] = value.PressureKPa, ["oxygenPressure"] = value.OxygenPartialPressureKPa,
        ["carbonDioxidePressure"] = value.CarbonDioxidePartialPressureKPa,
        ["canBreathe"] = value.CanBreathe, ["temperature"] = value.TemperatureCelsius
    };

    private static JObject CaptureAtmosphere()
    {
        var ship = CabinShip();
        int roomCount = ship.Compartments.Count;
        double volume = roomCount > 0 ? ship.Compartments[0].VolumeLiters : 0d;
        decimal plannedMoles = ShipAtmosphereService.MolesAtPressure(101.325d, volume, 20d);
        bool addOxygen = ShipAtmosphereService.TryAddGas(ship, 2, 2, Gas(FluidIds.Oxygen, plannedMoles * .21m));
        bool addNitrogen = ShipAtmosphereService.TryAddGas(ship, 2, 2, Gas(FluidIds.Nitrogen, plannedMoles * .79m));
        ShipEnvironmentSnapshot filled = ShipAtmosphereService.GetEnvironment(ship, 2, 2);
        decimal beforeTake = roomCount > 0 ? ShipAtmosphereService.TotalMoles(ship.Compartments[0].Gas) : 0m;
        bool take = ShipAtmosphereService.TryTakeGas(ship, 2, 2, FluidIds.Oxygen, 10m, out FluidBatch taken);
        var supplyInventory = new FluidInventory();
        bool prepareSupply = supplyInventory.TryAdd(Gas(FluidIds.Oxygen, 40m), 10000d, .001d);
        decimal sourceBefore = supplyInventory.GasMoles;
        bool supplied = ShipAtmosphereService.TrySupplyGas(ship, 2, 2,
            new TankShipAtmosphereSource(supplyInventory, 10000d, .001d), 101.325d, 5m, out decimal received);
        decimal sourceAfter = supplyInventory.GasMoles;
        var door = ship.Pieces.Find(value => value.Kind == ShipPieceKind.Door);
        bool blockedClosed = door?.BlocksPassage == true;
        if (door != null) door.DoorOpen = true;
        // 通行开关与密闭配置独立，泄气检查使用实际移除边界后的重建结果。
        ShipAtmosphereService.RebuildCompartments(ship);
        ShipEnvironmentSnapshot opened = ShipAtmosphereService.GetEnvironment(ship, 2, 2);
        decimal beforeLeak = ship.Compartments.Count > 0 ? ShipAtmosphereService.TotalMoles(ship.Compartments[0].Gas) : 0m;
        var exhaustInventory = new FluidInventory();
        var exhaust = new TankShipAtmosphereSource(exhaustInventory, 1000000d, .001d);
        bool broken = door != null && ShipStructureService.ApplyDamage(ship, door.PieceId, door.Health, "Lua 领域样本");
        ShipAtmosphereService.RebuildCompartments(ship, exhaust);
        int breachedRooms = ship.Compartments.Count;
        decimal leaked = exhaustInventory.GasMoles;
        var repaired = Piece("repaired-door", ShipPieceKind.Door, 2, 0);
        repaired.SealsAtmosphere = true;
        ship.Pieces.Add(repaired); ship.NotifyStructureChanged();
        ShipAtmosphereService.RebuildCompartments(ship, exhaust);
        decimal repairedMoles = ship.Compartments.Count > 0 ? ShipAtmosphereService.TotalMoles(ship.Compartments[0].Gas) : 0m;
        return new JObject
        {
            ["rooms"] = roomCount, ["volumeLiters"] = volume, ["plannedMoles"] = plannedMoles,
            ["oxygenAdded"] = addOxygen, ["nitrogenAdded"] = addNitrogen, ["filled"] = Environment(filled),
            ["beforeTakeMoles"] = beforeTake, ["takeAccepted"] = take, ["takenMoles"] = taken.GasMoles,
            ["supplyPrepared"] = prepareSupply, ["supplyAccepted"] = supplied, ["receivedMoles"] = received,
            ["sourceBeforeMoles"] = sourceBefore, ["sourceAfterMoles"] = sourceAfter,
            ["blocksPassageClosed"] = blockedClosed, ["blocksPassageOpen"] = door?.BlocksPassage == true,
            ["doorOpenEnvironment"] = Environment(opened), ["breachAccepted"] = broken,
            ["beforeLeakMoles"] = beforeLeak, ["breachedRooms"] = breachedRooms, ["releasedMoles"] = leaked,
            ["repairedRooms"] = ship.Compartments.Count, ["repairedMoles"] = repairedMoles
        };
    }

    #endregion

    #region 载荷质量与重心守恒

    private static JObject PointReading(ShipState ship, double localX, double localY)
    {
        ShipGeometry.LocalToWorld(ship, localX, localY, out double x, out double y);
        ShipGeometry.PointVelocity(ship, x, y, out double vx, out double vy);
        return new JObject { ["x"] = x, ["y"] = y, ["velocityX"] = vx, ["velocityY"] = vy };
    }

    private static JObject CaptureMass()
    {
        var ship = NewShip("mass");
        ship.PositionX = 100d; ship.PositionY = -10d; ship.AngleRadians = .3d;
        ship.VelocityX = 12d; ship.VelocityY = 4d; ship.AngularVelocityRadiansPerSecond = .5d;
        var floor = Piece("mass-floor", ShipPieceKind.Floor, 0, 0, 12d);
        floor.Width = 2; floor.Height = 1;
        ship.Pieces.Add(floor);
        ShipStructureService.RecalculateMass(ship);
        var initial = ShipReading(ship);
        var pointBefore = PointReading(ship, .25d, .5d);
        var cargo = new ShipMassContribution { SourceId = "cargo", LocalX = 3d, LocalY = .5d,
            MassKg = 6d, Kind = ShipMassKind.Cargo };
        ship.MassContributions.Add(cargo);
        // 真实汇总会按稳定来源去重，并修正重心而保持船体上的世界点与点速度。
        ship.MassContributions.Add(new ShipMassContribution { SourceId = "cargo", LocalX = 90d, MassKg = 99d });
        ShipStructureService.RecalculateMass(ship);
        var loaded = ShipReading(ship);
        var pointAfter = PointReading(ship, .25d, .5d);
        ship.MassContributions.Clear();
        ShipStructureService.RecalculateMass(ship);
        var unloaded = ShipReading(ship);
        bool invalidMassRejected = false;
        ship.MassContributions.Add(new ShipMassContribution { SourceId = "invalid", MassKg = -1d });
        try { ShipStructureService.RecalculateMass(ship); }
        catch (System.IO.InvalidDataException) { invalidMassRejected = true; }
        return new JObject
        {
            ["floorInputMass"] = floor.MassKg, ["cargoInputMass"] = cargo.MassKg,
            ["initial"] = initial, ["loaded"] = loaded, ["unloaded"] = unloaded,
            ["pointBefore"] = pointBefore, ["pointAfter"] = pointAfter,
            ["invalidMassRejected"] = invalidMassRejected
        };
    }

    #endregion

    #region 推进分配与惯性导航

    private static JObject CapturePropulsion()
    {
        var settings = new SpaceGameplaySettings();
        var directions = new JArray();
        SpaceVector2[] requests = { new(0d, 2d), new(-2d, 0d), new(0d, -2d), new(2d, 0d) };
        var allocation = new ShipThrustAllocation();
        // 四个安装朝向共用真实分配器，供油比例只作为领域输入写入，不伪造机器供油结果。
        foreach (SpaceVector2 request in requests)
        {
            allocation.Reset(request, 2d, 0d);
            for (int nozzle = 0; nozzle < 4; nozzle++)
                allocation.Add(ShipThrustAllocation.GetThrustDirection(0d, nozzle), default, 100d);
            var throttle = new JArray();
            for (int nozzle = 0; nozzle < allocation.Count; nozzle++)
            {
                double value = allocation.GetThrottle(nozzle);
                throttle.Add(value); allocation.SetActualOutput(nozzle, value);
            }
            directions.Add(new JObject { ["request"] = Vector(request), ["throttle"] = throttle,
                ["actualChange"] = Vector(allocation.ResolveVelocityChange()) });
        }
        allocation.Reset(new SpaceVector2(1.2d, 1.6d), 2d, 0d);
        for (int nozzle = 0; nozzle < 4; nozzle++)
            allocation.Add(ShipThrustAllocation.GetThrustDirection(0d, nozzle), default, 100d);
        allocation.SetActualOutput(0, allocation.GetThrottle(0) * .5d);
        var partial = Vector(allocation.ResolveVelocityChange());
        allocation.Reset(default, 2d, 1d);
        allocation.Add(new SpaceVector2(0d, 1d), new SpaceVector2(2d, 0d), 100d);
        double turnThrottle = allocation.GetThrottle(0);
        allocation.SetActualOutput(0, turnThrottle);
        var pureTurnTranslation = Vector(allocation.ResolveVelocityChange());
        double turnAuthority = allocation.ResolveTurnAuthority();
        SpaceVector2 coastInput = new(35d, 12d), coast = coastInput;
        for (int step = 0; step < 500; step++)
            coast = ShipCruiseGuidance.StepVelocity(coast,
                ShipCruiseGuidance.ManualVelocity(coast, default, settings, .02d), settings, .02d);
        SpaceVector2 sideways = ShipCruiseGuidance.ManualVelocity(coastInput, new SpaceVector2(0d, 1d), settings, .02d);
        SpaceVector2 reverseInput = new(settings.CruiseMaxSpeedMetersPerSecond, 0d);
        SpaceVector2 reverse = ShipCruiseGuidance.ManualVelocity(reverseInput, new SpaceVector2(-1d, 0d), settings, .02d);
        SpaceVector2 noPower = ShipCruiseGuidance.StepVelocity(new SpaceVector2(180d, 0d), default, settings, .02d, 0d);
        var arrivals = new JArray();
        foreach (double seconds in new[] { .01d, .02d, .05d, .1d })
        {
            SpaceVector2 position = default, velocity = new(0d, 40d), target = new(50d, 0d);
            int step = 0;
            for (; step < 10000; step++)
            {
                SpaceVector2 next = ShipCruiseGuidance.StepVelocity(velocity,
                    ShipCruiseGuidance.ArrivalVelocity(target - position, settings, seconds), settings, seconds);
                position += (velocity + next) * (.5d * seconds); velocity = next;
                if ((target - position).Magnitude <= settings.NavigationArrivalDistanceMeters &&
                    velocity.Magnitude <= settings.NavigationArrivalSpeedMetersPerSecond) break;
            }
            arrivals.Add(new JObject { ["seconds"] = seconds, ["steps"] = step,
                ["distance"] = (target - position).Magnitude, ["speed"] = velocity.Magnitude });
        }
        return new JObject
        {
            ["cardinal"] = directions, ["partialOutputChange"] = partial,
            ["pureTurnTranslation"] = pureTurnTranslation, ["turnAuthority"] = turnAuthority,
            ["coastInput"] = Vector(coastInput), ["coastAfterTenSeconds"] = Vector(coast),
            ["sideways"] = Vector(sideways), ["reverseInput"] = Vector(reverseInput), ["reverse"] = Vector(reverse),
            ["unpoweredOverspeed"] = Vector(noPower), ["arrivals"] = arrivals,
            ["arrivalDistance"] = settings.NavigationArrivalDistanceMeters,
            ["arrivalSpeed"] = settings.NavigationArrivalSpeedMetersPerSecond
        };
    }

    #endregion

    #region 乘员接触与跨区边界

    private static UniverseSimulation ContactUniverse(SpaceSectorId sector = default) => new(new UniverseState
    {
        Sector = sector,
        Bodies = new List<BodyState> { new() { BodyId = "gameplay-probe-body", PlanetId = "gameplay-probe-planet",
            RadiusMeters = 100d, SurfaceGravity = 9.8d } }
    });

    private static JObject CaptureContacts()
    {
        var universe = ContactUniverse();
        var motion = new SpaceMotionState { PositionMeters = new SpaceVector2(150d, 0d),
            VelocityMetersPerSecond = new SpaceVector2(-100d, 0d), RadiusMeters = 1d };
        bool hit = universe.StepMotionAtTime(motion, default, 0d, 1d, out SurfaceContact contact);
        var freeShip = new SpaceMotionState { PositionMeters = new SpaceVector2(150d, 0d),
            VelocityMetersPerSecond = new SpaceVector2(-100d, 0d), RadiusMeters = 10d };
        universe.StepFreeMotion(freeShip, default, 1d);
        var left = new SpacePassengerContactState
        {
            PreviousPosition = new SpaceSectorPosition(default, new SpaceVector2(-2d, 0d)),
            Position = new SpaceSectorPosition(default, new SpaceVector2(2d, 0d)),
            VelocityMetersPerSecond = new SpaceVector2(4d, 0d), RadiusMeters = .5d, MassKg = 80d
        };
        var right = new SpacePassengerContactState
        {
            PreviousPosition = new SpaceSectorPosition(default, new SpaceVector2(2d, 0d)),
            Position = new SpaceSectorPosition(default, new SpaceVector2(-2d, 0d)),
            VelocityMetersPerSecond = new SpaceVector2(-4d, 0d), RadiusMeters = .5d, MassKg = 40d
        };
        double momentumBefore = left.MassKg * left.VelocityMetersPerSecond.X + right.MassKg * right.VelocityMetersPerSecond.X;
        // 独立乘员走真实扫掠入口；船对船和散落物接船的会话接线另由场景流程覆盖。
        bool pairContact = SpacePassengerContacts.ResolvePair(left, right, 1d);
        double momentumAfter = left.MassKg * left.VelocityMetersPerSecond.X + right.MassKg * right.VelocityMetersPerSecond.X;
        var remote = new SpacePassengerContactState
        {
            PreviousPosition = new SpaceSectorPosition(new SpaceSectorId(1000, 0), left.Position.LocalMeters),
            Position = new SpaceSectorPosition(new SpaceSectorId(1000, 0), left.Position.LocalMeters)
        };
        bool distantPair = SpacePassengerContacts.ResolvePair(left, remote, 1d);
        SpaceSectorPosition crossing = SpaceSectorCoordinates.Normalize(default,
            new SpaceVector2(SpaceSectorCoordinates.HalfSizeMeters + .25d, 0d));
        SpaceSectorPosition negative = SpaceSectorCoordinates.Normalize(default,
            new SpaceVector2(-SpaceSectorCoordinates.HalfSizeMeters - .25d, 0d));
        SpaceSectorPosition high = new(new SpaceSectorId(long.MaxValue - 2, long.MinValue + 2), new SpaceVector2(10d, -10d));
        SpaceVector2 highRelative = SpaceSectorCoordinates.Relative(high,
            new SpaceSectorId(long.MaxValue - 3, long.MinValue + 2));
        return new JObject
        {
            ["surfaceContact"] = hit, ["contactFraction"] = contact.Fraction,
            ["contactPosition"] = Vector(contact.PositionMeters), ["contactBody"] = contact.BodyId,
            ["freeShipPosition"] = Vector(freeShip.PositionMeters),
            ["passengerPairContact"] = pairContact, ["momentumBefore"] = momentumBefore,
            ["momentumAfter"] = momentumAfter, ["leftVelocity"] = Vector(left.VelocityMetersPerSecond),
            ["rightVelocity"] = Vector(right.VelocityMetersPerSecond),
            ["separation"] = SpaceSectorCoordinates.Difference(right.Position, left.Position).Magnitude,
            ["distantPairContact"] = distantPair, ["crossing"] = Position(crossing),
            ["negativeCrossing"] = Position(negative), ["sectorSize"] = SpaceSectorCoordinates.SizeMeters,
            ["highSectorRelative"] = Vector(highRelative),
            ["limitations"] = "领域乘员和星体扫掠；未执行船体接触会话、场景加载或多人网络。"
        };
    }

    #endregion

    #region 船体安装与光纤连接

    private static JObject CaptureEnginePlacement()
    {
        var ship = NewShip("engine-placement");
        var floor = Piece("engine-floor", ShipPieceKind.Floor, 0, 0);
        floor.Width = 3; floor.Height = 3; ship.Pieces.Add(floor);
        int[] xs = { 1, 2, 1, 0 }, ys = { 0, 1, 2, 1 };
        var cardinal = new JArray();
        for (int direction = 0; direction < 4; direction++)
        {
            var engine = Piece("edge-engine-" + direction, ShipPieceKind.Engine, xs[direction], ys[direction]);
            engine.QuarterTurns = direction;
            bool allowed = ShipEnginePlacement.TryValidateEngine(ship, engine, out string reason);
            cardinal.Add(new JObject { ["quarterTurns"] = direction, ["allowed"] = allowed, ["reason"] = reason });
        }
        var center = Piece("center-engine", ShipPieceKind.Engine, 1, 1);
        bool centerAllowed = ShipEnginePlacement.TryValidateEngine(ship, center, out string centerReason);
        var bottom = Piece("bottom-engine", ShipPieceKind.Engine, 1, 0);
        ship.Pieces.Add(bottom);
        var obstruction = Piece("exhaust-obstacle", ShipPieceKind.Floor, 1, -1);
        // 安装预检直接复用生产规则，后续地板和设备不能堵住已有喷口。
        bool obstacleAllowed = ShipEnginePlacement.TryValidateAddition(ship, obstruction, out string obstacleReason);
        obstruction.Kind = ShipPieceKind.Pipe;
        bool pipeAllowed = ShipEnginePlacement.TryValidateAddition(ship, obstruction, out _);
        var unsupported = Piece("unsupported-engine", ShipPieceKind.Engine, 10, 10);
        bool unsupportedAllowed = ShipEnginePlacement.TryValidateEngine(ship, unsupported, out string unsupportedReason);
        return new JObject
        {
            ["cardinal"] = cardinal, ["centerAllowed"] = centerAllowed, ["centerReason"] = centerReason,
            ["exhaustObstacleAllowed"] = obstacleAllowed, ["exhaustObstacleReason"] = obstacleReason,
            ["pipeAllowed"] = pipeAllowed, ["unsupportedAllowed"] = unsupportedAllowed,
            ["unsupportedReason"] = unsupportedReason
        };
    }

    private static JObject CaptureOptical()
    {
        var ship = NewShip("optical");
        var console = Piece("optical-console", ShipPieceKind.Console, 0, 0);
        var navigation = Piece("optical-navigation", ShipPieceKind.Navigation, 2, 0);
        var fiber = Piece("optical-fiber", ShipPieceKind.Fiber, 1, 0);
        ship.Pieces.Add(console); ship.Pieces.Add(navigation); ship.Pieces.Add(fiber);
        var workspace = new ShipOpticalGraph.Workspace();
        // 光纤网络断开和重新安装都重新查询真实连通规则，集合工作区保持复用。
        bool connected = ShipOpticalGraph.Connected(ship, console, navigation, workspace);
        fiber.Destroyed = true; ship.NotifyStructureChanged();
        bool disconnected = ShipOpticalGraph.Connected(ship, console, navigation, workspace);
        fiber.Destroyed = false; ship.NotifyStructureChanged();
        bool repaired = ShipOpticalGraph.Connected(ship, console, navigation, workspace);
        return new JObject { ["connected"] = connected, ["afterRemoval"] = disconnected,
            ["afterRepair"] = repaired };
    }

    #endregion

    #region 实际燃料库存

    private static JObject CaptureFuel()
    {
        var fuel = new FluidInventory();
        bool hydrogenAdded = fuel.TryAdd(Gas(FluidIds.Hydrogen, 20m), 10000d, .001d);
        bool oxygenAdded = fuel.TryAdd(Gas(FluidIds.Oxygen, 10m), 10000d, .001d);
        var engine = new ShipPartConfiguration();
        bool eligible = ShipFuel.IsEligible(fuel, engine);
        decimal available = ShipFuel.AvailableReactionMoles(fuel, engine);
        // 燃烧直接扣除真实氢氧库存，失败请求必须保留原来的组分数量。
        bool burned = ShipFuel.TryBurn(fuel, 3m, out double energy);
        decimal hydrogenAfter = fuel.GetGasMoles(FluidIds.Hydrogen), oxygenAfter = fuel.GetGasMoles(FluidIds.Oxygen);
        bool overdraw = ShipFuel.TryBurn(fuel, 100m, out double overdrawEnergy);
        decimal hydrogenAfterFailure = fuel.GetGasMoles(FluidIds.Hydrogen), oxygenAfterFailure = fuel.GetGasMoles(FluidIds.Oxygen);
        bool impurityAdded = fuel.TryAdd(Gas(FluidIds.Nitrogen, 10m), 10000d, .001d);
        bool impureEligible = ShipFuel.IsEligible(fuel, engine);
        var singleGas = new FluidInventory();
        bool singlePrepared = singleGas.TryAdd(Gas(FluidIds.Hydrogen, 20m), 10000d, .001d);
        bool singleEligible = ShipFuel.IsEligible(singleGas, engine);
        return new JObject
        {
            ["hydrogenAdded"] = hydrogenAdded, ["oxygenAdded"] = oxygenAdded,
            ["eligible"] = eligible, ["availableReactionMoles"] = available, ["burnAccepted"] = burned,
            ["burnRequestedOxygen"] = 3m, ["energyJoules"] = energy,
            ["hydrogenAfter"] = hydrogenAfter, ["oxygenAfter"] = oxygenAfter,
            ["overdrawAccepted"] = overdraw, ["overdrawEnergy"] = overdrawEnergy,
            ["hydrogenAfterFailure"] = hydrogenAfterFailure, ["oxygenAfterFailure"] = oxygenAfterFailure,
            ["impurityAdded"] = impurityAdded, ["impureEligible"] = impureEligible,
            ["singleGasPrepared"] = singlePrepared, ["singleGasEligible"] = singleEligible
        };
    }

    #endregion

    #region 会话快照往返

    private static JObject CaptureSnapshots()
    {
        var state = new SpaceSessionState { SimulationSeconds = 123.5d };
        ShipState ship = DockShip("snapshot", 0);
        ship.Sector = new SpaceSectorId(-3, 4); ship.VelocityX = 17d; ship.VelocityY = -8d;
        state.Ships.Add(ship);
        state.Flights.Add(new ShipFlightState { ShipId = ship.ShipId, Phase = ShipFlightPhase.Cruising,
            Ignited = true, Motion = new SpaceMotionState { Sector = ship.Sector,
                PositionMeters = new SpaceVector2(ship.PositionX, ship.PositionY),
                VelocityMetersPerSecond = new SpaceVector2(ship.VelocityX, ship.VelocityY) } });
        state.Navigation.Add(new ShipNavigationState { ShipId = ship.ShipId, Active = true,
            TargetSector = new SpaceSectorId(-2, 4), TargetX = 11d, TargetY = -4d, Sequence = 7 });
        state.Passengers.Add(new SpacePassengerState { ProfileId = "domain-fixture", IsInSpace = true,
            FreeMotion = new SpaceMotionState { Sector = ship.Sector,
                PositionMeters = new SpaceVector2(3d, 4d), VelocityMetersPerSecond = new SpaceVector2(20d, -2d) } });
        var passenger = state.Passengers[0];
        passenger.UpdateFreeWalkVelocity(new SpaceVector2(3d, 4d));
        state.LooseItems.Add(new SpaceLooseItemState { Id = "domain-loose", Snapshot = "opaque-fixture-payload",
            WorldKey = SpaceSectorCoordinates.SceneKey(ship.Sector), InSpace = true,
            RotationDegrees = 25d, SurfaceHeight = 12d, SurfaceVerticalSpeed = -3d,
            Scale = new Vector3(-2f, 0f, 3.5f), Motion = new SpaceMotionState { Sector = ship.Sector,
                PositionMeters = new SpaceVector2(8d, -9d), VelocityMetersPerSecond = new SpaceVector2(10d, -11d) } });
        // 保存只操作独立状态，缩放转换和自由步行分量均使用生产序列化模型。
        string json = JsonConvert.SerializeObject(state);
        var restored = JsonConvert.DeserializeObject<SpaceSessionState>(json)
            ?? throw new InvalidOperationException("独立会话快照读回为空。");
        string secondJson = JsonConvert.SerializeObject(restored);
        bool stable = JToken.DeepEquals(JToken.Parse(json), JToken.Parse(secondJson));
        restored.Passengers[0].UpdateFreeWalkVelocity(default);
        var saved = JObject.Parse(json);
        JObject scale = saved["LooseItems"]?[0]?["Scale"] as JObject;
        var scaleKeys = new JArray();
        if (scale != null) foreach (JProperty property in scale.Properties()) scaleKeys.Add(property.Name);
        return new JObject
        {
            ["version"] = restored.Version, ["simulationSeconds"] = restored.SimulationSeconds,
            ["shipCount"] = restored.Ships.Count, ["flightCount"] = restored.Flights.Count,
            ["navigationCount"] = restored.Navigation.Count, ["looseCount"] = restored.LooseItems.Count,
            ["stableRepeatSnapshot"] = stable, ["ship"] = ShipReading(restored.Ships[0]),
            ["flightPhase"] = restored.Flights[0].Phase.ToString(), ["flightIgnited"] = restored.Flights[0].Ignited,
            ["navigationSectorX"] = restored.Navigation[0].TargetSector.X.ToString(CultureInfo.InvariantCulture),
            ["navigationSectorY"] = restored.Navigation[0].TargetSector.Y.ToString(CultureInfo.InvariantCulture),
            ["passengerReleasedVelocity"] = Vector(restored.Passengers[0].FreeMotion.VelocityMetersPerSecond),
            ["scaleKeys"] = scaleKeys, ["scaleX"] = restored.LooseItems[0].Scale.x,
            ["scaleY"] = restored.LooseItems[0].Scale.y, ["scaleZ"] = restored.LooseItems[0].Scale.z,
            ["loosePayload"] = restored.LooseItems[0].Snapshot,
            ["loosePosition"] = Vector(restored.LooseItems[0].Motion.PositionMeters),
            ["looseVelocity"] = Vector(restored.LooseItems[0].Motion.VelocityMetersPerSecond),
            ["limitations"] = "只覆盖生产模型 JSON 往返，不代表磁盘保存、退出世界或重新加载场景已验收。"
        };
    }

    #endregion
}
