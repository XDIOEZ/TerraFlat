using System;
using System.Collections.Generic;
using FlatWorld.Spaceflight;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>局部伤害样本只调用正式船体接口，业务断言由 Lua 持有。</summary>
public static class GameplayTestShipDamageProbe
{
    #region 独立领域采样

    public static JObject Capture(string operation)
    {
        JObject result = operation switch
        {
            "ship_local_damage" => CaptureLocalDamage(),
            "ship_hit_targeting" => CaptureTargeting(),
            "ship_damage_lines" => CaptureLines(),
            "ship_damage_fragmentation" => CaptureFragments(),
            _ => throw new ArgumentException("未知局部船体伤害采样：" + operation, nameof(operation))
        };
        result["operation"] = operation;
        result["scope"] = "production-domain";
        result["limitations"] = "只调用正式领域伤害、命中选择、碰撞几何和四邻分裂；不创建真实 Item、PhysicsScene2D、武器或多人会话。";
        return result;
    }

    private static ShipState NewShip(string id) => new()
    {
        ShipId = "ship-damage-probe-" + id,
        PlatformKind = ShipPlatformKind.Space,
        CellSizeMeters = 1d
    };

    private static ShipPieceState Piece(string id, ShipPieceKind kind, int x, int y,
        double health = 100d, double defense = 1d) => new()
    {
        PieceId = id, ItemId = "domain-damage-fixture", Kind = kind,
        CellX = x, CellY = y, Layer = kind == ShipPieceKind.Floor ? 0 : 1,
        MassKg = 10d, Health = health, MaxHealth = health, PhysicalDefense = defense,
        RequiresFloorSupport = kind != ShipPieceKind.Floor,
        BlocksMovement = kind != ShipPieceKind.Floor,
        DisableHealth = kind == ShipPieceKind.Engine ? 20d : 0d,
        MaxDisableHealth = kind == ShipPieceKind.Engine ? 20d : 0d
    };

    private static JObject Reading(ShipPieceState piece) => new()
    {
        ["id"] = piece.PieceId, ["kind"] = piece.Kind.ToString(),
        ["health"] = piece.Health, ["maxHealth"] = piece.MaxHealth,
        ["defense"] = piece.PhysicalDefense,
        ["disableHealth"] = piece.DisableHealth, ["maxDisableHealth"] = piece.MaxDisableHealth,
        ["alive"] = piece.IsAlive, ["destroyed"] = piece.Destroyed,
        ["operational"] = piece.IsOperational, ["snapshot"] = piece.SnapshotJson ?? string.Empty
    };

    private static JObject GeometryReading(ShipColliderGeometry geometry)
    {
        var rectangles = new JArray();
        foreach (ShipCollisionRect rectangle in geometry.Rectangles)
            rectangles.Add(new JObject
            {
                ["x"] = rectangle.X, ["y"] = rectangle.Y,
                ["width"] = rectangle.Width, ["height"] = rectangle.Height,
                ["blocksPassage"] = rectangle.BlocksPassage
            });
        return new JObject { ["rebuilds"] = geometry.RebuildCount, ["rectangles"] = rectangles };
    }

    private static JArray FragmentReadings(IEnumerable<ShipState> ships)
    {
        var result = new JArray();
        foreach (ShipState ship in ships)
        {
            var pieces = new JArray();
            foreach (ShipPieceState piece in ship.Pieces) pieces.Add(Reading(piece));
            result.Add(new JObject
            {
                ["id"] = ship.ShipId, ["wreck"] = ship.IsWreck,
                ["console"] = ShipStructureService.HasOperationalConsole(ship),
                ["engine"] = ShipStructureService.HasOperationalEngine(ship), ["pieces"] = pieces
            });
        }
        return result;
    }

    #endregion

    #region 部位耐久与弱点

    private static JObject CaptureLocalDamage()
    {
        var ship = NewShip("local");
        var floor = Piece("local-floor", ShipPieceKind.Floor, 0, 0);
        var engine = Piece("local-engine", ShipPieceKind.Engine, 0, 0);
        ship.Pieces.Add(floor); ship.Pieces.Add(engine);
        var geometry = new ShipColliderGeometry();
        geometry.Synchronize(ship);
        JObject beforeGeometry = GeometryReading(geometry);
        var applied = new JArray();
        Action<ShipState, ShipPieceState, double, string> damageObserver = (owner, part, damage, _) =>
        {
            if (ReferenceEquals(owner, ship)) applied.Add(new JObject { ["id"] = part.PieceId, ["damage"] = damage });
        };
        ShipStructureService.DamageApplied += damageObserver;
        bool first, second;
        JObject firstState, secondState;
        try
        {
            first = ShipStructureService.ApplyPhysicalDamage(ship, engine.PieceId, 20d, "局部伤害第一发");
            firstState = Reading(engine);
            second = ShipStructureService.ApplyPhysicalDamage(ship, engine.PieceId, 20d, "局部伤害第二发");
            secondState = Reading(engine);
        }
        finally { ShipStructureService.DamageApplied -= damageObserver; }
        bool geometryRebuilt = geometry.Synchronize(ship);
        JObject disabledGeometry = GeometryReading(geometry);
        bool disabledEngineAvailable = ShipStructureService.HasOperationalEngine(ship);
        var invalid = new JArray();
        foreach (double raw in new[] { 0d, -1d, double.NaN, double.PositiveInfinity })
            invalid.Add(ShipStructureService.ApplyPhysicalDamage(ship, engine.PieceId, raw, "无效伤害"));
        var armored = Piece("armor-wall", ShipPieceKind.Wall, 0, 0, defense: 10d);
        var armorShip = NewShip("armor"); armorShip.Pieces.Add(armored);
        bool armorBlocked = ShipStructureService.ApplyPhysicalDamage(armorShip, armored.PieceId, 10d, "等于防御");
        double wallEffective = ShipStructureService.ResolvePhysicalDamage(armored, 20d);
        int destructionCallbacks = 0;
        bool destruction = ShipStructureService.ApplyPhysicalDamage(ship, engine.PieceId, 1000d,
            "结构摧毁", (_, _, _) => destructionCallbacks++);
        bool repeated = ShipStructureService.ApplyPhysicalDamage(ship, engine.PieceId, 1000d,
            "重复摧毁", (_, _, _) => destructionCallbacks++);
        geometry.Synchronize(ship);
        var thresholdShip = NewShip("threshold");
        var threshold = Piece("threshold-engine", ShipPieceKind.Engine, 0, 0);
        thresholdShip.Pieces.Add(threshold);
        bool thresholdAccepted = ShipStructureService.ApplyPhysicalDamage(thresholdShip, threshold.PieceId, 21d, "恰好瘫痪");
        return new JObject
        {
            ["firstAccepted"] = first, ["secondAccepted"] = second,
            ["first"] = firstState, ["second"] = secondState, ["events"] = applied,
            ["floor"] = Reading(floor), ["beforeGeometry"] = beforeGeometry,
            ["disabledGeometryRebuilt"] = geometryRebuilt,
            ["disabledGeometry"] = disabledGeometry,
            ["disabledEngineAvailable"] = disabledEngineAvailable,
            ["invalidAccepted"] = invalid, ["armorAccepted"] = armorBlocked,
            ["armor"] = Reading(armored), ["wallEffective"] = wallEffective,
            ["destroyAccepted"] = destruction, ["repeatDestroyAccepted"] = repeated,
            ["destroyCallbacks"] = destructionCallbacks, ["destroyed"] = Reading(engine),
            ["destroyedGeometry"] = GeometryReading(geometry),
            ["thresholdAccepted"] = thresholdAccepted, ["threshold"] = Reading(threshold)
        };
    }

    #endregion

    #region 命中位置与上层遮挡

    private static JObject CaptureTargeting()
    {
        var ship = NewShip("target");
        var floor = Piece("target-floor", ShipPieceKind.Floor, 0, 0);
        floor.Layer = 99;
        var engine = Piece("target-engine", ShipPieceKind.Engine, 0, 0, health: 10d);
        ship.Pieces.Add(floor); ship.Pieces.Add(engine);
        ShipPieceState selected = ShipDamageTargeting.SelectAtPoint(ship, .5d, .5d);
        bool accepted = selected != null && ShipStructureService.ApplyPhysicalDamage(ship, selected.PieceId, 20d, "上层命中");
        JObject floorAfterFirst = Reading(floor);
        ShipPieceState next = ShipDamageTargeting.SelectAtPoint(ship, .5d, .5d);
        bool nextAccepted = next != null && ShipStructureService.ApplyPhysicalDamage(ship, next.PieceId, 20d, "下一发命中");
        var rotatedShip = NewShip("rotated");
        var rotated = Piece("rotated-engine", ShipPieceKind.Engine, -4, -3);
        rotated.Width = 2; rotated.Height = 3; rotated.QuarterTurns = 1;
        rotatedShip.Pieces.Add(rotated);
        var overlays = new JArray();
        foreach (ShipPieceKind kind in new[] { ShipPieceKind.Wire, ShipPieceKind.Fiber })
        {
            var overlayShip = NewShip("overlay-" + kind);
            var overlayFloor = Piece("overlay-floor", ShipPieceKind.Floor, 0, 0);
            var overlayEngine = Piece("overlay-engine", ShipPieceKind.Engine, 0, 0, health: 10d);
            overlayEngine.Layer = 0;
            var overlay = Piece("overlay-line", kind, 0, 0); overlay.Layer = 5;
            overlayShip.Pieces.Add(overlayFloor); overlayShip.Pieces.Add(overlay); overlayShip.Pieces.Add(overlayEngine);
            ShipPieceState firstOverlay = ShipDamageTargeting.SelectAtPoint(overlayShip, .5d, .5d);
            bool overlayAccepted = ShipStructureService.ApplyPhysicalDamage(overlayShip, firstOverlay.PieceId, 20d, "设备遮挡线路");
            overlays.Add(new JObject
            {
                ["kind"] = kind.ToString(), ["firstTarget"] = firstOverlay.PieceId, ["accepted"] = overlayAccepted,
                ["nextTarget"] = ShipDamageTargeting.SelectAtPoint(overlayShip, .5d, .5d)?.PieceId ?? string.Empty,
                ["floorHealth"] = overlayFloor.Health, ["lineHealth"] = overlay.Health,
                ["engineDestroyed"] = overlayEngine.Destroyed
            });
        }
        var edgeShip = NewShip("contact-edge");
        var edgeFloor = Piece("edge-floor-left", ShipPieceKind.Floor, 0, 0);
        var adjacentFloor = Piece("edge-floor-right", ShipPieceKind.Floor, 1, 0);
        var edgeEngine = Piece("edge-engine", ShipPieceKind.Engine, 0, 0); edgeEngine.Layer = 0;
        var adjacentWall = Piece("edge-wall", ShipPieceKind.Wall, 1, 0); adjacentWall.Layer = 100;
        edgeShip.Pieces.Add(edgeFloor); edgeShip.Pieces.Add(adjacentFloor);
        edgeShip.Pieces.Add(edgeEngine); edgeShip.Pieces.Add(adjacentWall);
        return new JObject
        {
            ["firstTarget"] = selected?.PieceId ?? string.Empty, ["firstAccepted"] = accepted,
            ["engineAfterFirst"] = Reading(engine), ["floorAfterFirst"] = floorAfterFirst,
            ["nextTarget"] = next?.PieceId ?? string.Empty, ["nextAccepted"] = nextAccepted,
            ["floorAfterNext"] = Reading(floor),
            ["emptyTarget"] = ShipDamageTargeting.SelectAtPoint(ship, 3d, 3d)?.PieceId ?? string.Empty,
            ["invalidTarget"] = ShipDamageTargeting.SelectAtPoint(ship, double.NaN, .5d)?.PieceId ?? string.Empty,
            ["rotatedInside"] = ShipDamageTargeting.SelectAtPoint(rotatedShip, -1.5d, -1.5d)?.PieceId ?? string.Empty,
            ["rotatedOutside"] = ShipDamageTargeting.SelectAtPoint(rotatedShip, -.5d, -1.5d)?.PieceId ?? string.Empty,
            ["overlays"] = overlays,
            ["rightEdgeTarget"] = ShipDamageTargeting.SelectAtContact(edgeShip, edgeFloor, 1d, .5d)?.PieceId ?? string.Empty,
            ["leftEdgeTarget"] = ShipDamageTargeting.SelectAtContact(edgeShip, adjacentFloor, 1d, .5d)?.PieceId ?? string.Empty,
            ["invalidContact"] = ShipDamageTargeting.SelectAtContact(edgeShip, edgeFloor, double.NaN, .5d)?.PieceId ?? string.Empty
        };
    }

    #endregion

    #region 损伤线总预算

    private static JObject CaptureLines()
    {
        var ship = NewShip("line");
        for (int x = 0; x < 10; x++)
        {
            ShipPieceKind kind = x < 4 ? ShipPieceKind.Floor : x < 7 ? ShipPieceKind.Wall : ShipPieceKind.Engine;
            ship.Pieces.Add(Piece("line-" + x, kind, x, 0, defense: kind == ShipPieceKind.Wall ? 10d : 1d));
        }
        var outside = Piece("line-outside", ShipPieceKind.Floor, 0, 3);
        var dead = Piece("line-dead", ShipPieceKind.Floor, 10, 0, health: 0d); dead.Destroyed = true;
        ship.Pieces.Add(outside); ship.Pieces.Add(dead);
        var line = new ShipDamageLine(.5d, .5d, 0d);
        var selected = new JArray();
        foreach (ShipPieceState part in ShipDamagePattern.SelectPieces(ship, line)) selected.Add(part.PieceId);
        double loss = ShipDamagePattern.ApplyLineDamage(ship, line, 100d, "十个部位分担冲击");
        var parts = new JArray();
        foreach (ShipPieceState part in ship.Pieces) parts.Add(Reading(part));
        double zeroLoss = ShipDamagePattern.ApplyLineDamage(ship, line, 0d, "零预算");
        double filteredLoss = ShipDamagePattern.ApplyLineDamage(ship, line, 80d, "仅地板预算",
            eligible: part => part.Kind == ShipPieceKind.Floor);
        var filtered = new JArray();
        foreach (ShipPieceState part in ship.Pieces) filtered.Add(Reading(part));
        var fractional = NewShip("fractional");
        fractional.Pieces.Add(Piece("fraction-a", ShipPieceKind.Floor, 0, 0));
        fractional.Pieces.Add(Piece("fraction-b", ShipPieceKind.Floor, 1, 0));
        double fractionalLoss = ShipDamagePattern.ApplyLineDamage(fractional, line, 13d, "小数均分");
        var fractionalParts = new JArray();
        foreach (ShipPieceState part in fractional.Pieces) fractionalParts.Add(Reading(part));
        var changing = NewShip("changing");
        var weak = Piece("changing-weak", ShipPieceKind.Floor, 0, 0, health: 1d, defense: 0d);
        var strong = Piece("changing-strong", ShipPieceKind.Floor, 1, 0, defense: 0d);
        changing.Pieces.Add(weak); changing.Pieces.Add(strong);
        int callbacks = 0;
        // 正式摧毁回调移除部件后，后面的部件仍只能收到最初冻结的一半预算。
        double changingLoss = ShipDamagePattern.ApplyLineDamage(changing, line, 100d, "沿线摧毁回调",
            (owner, part, _) => { owner.Pieces.Remove(part); callbacks++; });
        return new JObject
        {
            ["selected"] = selected, ["loss"] = loss, ["parts"] = parts,
            ["zeroLoss"] = zeroLoss, ["filteredLoss"] = filteredLoss, ["filtered"] = filtered,
            ["fractionalLoss"] = fractionalLoss, ["fractional"] = fractionalParts,
            ["changingLoss"] = changingLoss, ["changingSurvivor"] = Reading(strong),
            ["changingCallbacks"] = callbacks, ["changingPieces"] = changing.Pieces.Count
        };
    }

    #endregion

    #region 腰部截断与残骸递归分裂

    private static JObject CaptureFragments()
    {
        var ship = NewShip("long");
        var floors = new List<ShipPieceState>();
        for (int x = 0; x < 9; x++)
        {
            var floor = Piece("long-floor-" + x, ShipPieceKind.Floor, x, 0, health: x == 3 ? 40d : 100d);
            if (x == 2) floor.Health = 83d;
            if (x == 5) floor.Health = 67d;
            floors.Add(floor); ship.Pieces.Add(floor);
        }
        var console = Piece("long-console", ShipPieceKind.Console, 0, 0);
        var engine = Piece("long-engine", ShipPieceKind.Engine, 1, 0);
        var wall = Piece("long-waist-wall", ShipPieceKind.Wall, 3, 0, health: 10d, defense: 10d);
        var cargo = Piece("long-cargo", ShipPieceKind.Equipment, 7, 0, health: 74d);
        cargo.SnapshotJson = "{\"fixtureInventory\":{\"item\":\"ore\",\"amount\":7}}";
        ship.Pieces.Add(console); ship.Pieces.Add(engine); ship.Pieces.Add(wall); ship.Pieces.Add(cargo);
        ship.IdentityAnchorPieceId = console.PieceId;
        ShipPieceState firstTarget = ShipDamageTargeting.SelectAtPoint(ship, 3.5d, .5d);
        bool wallHit = ShipStructureService.ApplyPhysicalDamage(ship, firstTarget.PieceId, 20d, "攻击腰墙");
        List<ShipState> wallGroups = ShipStructureService.RebuildAndSplit(ship);
        JArray afterWall = FragmentReadings(wallGroups);
        JObject floorAfterWall = Reading(floors[3]);
        ShipPieceState nextTarget = ShipDamageTargeting.SelectAtPoint(ship, 3.5d, .5d);
        bool floorHit = ShipStructureService.ApplyPhysicalDamage(ship, nextTarget.PieceId, 41d, "攻击腰部地板");
        List<ShipState> split = ShipStructureService.RebuildAndSplit(ship);
        JArray firstSplit = FragmentReadings(split);
        ShipState tail = split.Find(part => part.IsWreck);
        ShipPieceState tailTarget = tail == null ? null : ShipDamageTargeting.SelectAtPoint(tail, 6.5d, .5d);
        bool tailHit = tailTarget != null && ShipStructureService.ApplyPhysicalDamage(tail, tailTarget.PieceId, 101d, "继续攻击船尾");
        List<ShipState> tailSplit = tail == null ? new List<ShipState>() : ShipStructureService.RebuildAndSplit(tail);
        var restored = JsonConvert.DeserializeObject<List<ShipState>>(JsonConvert.SerializeObject(tailSplit));
        var diagonal = NewShip("diagonal");
        diagonal.Pieces.Add(Piece("diag-a", ShipPieceKind.Floor, 0, 0));
        diagonal.Pieces.Add(Piece("diag-b", ShipPieceKind.Floor, 1, 1));
        List<ShipState> diagonalSplit = ShipStructureService.RebuildAndSplit(diagonal);
        return new JObject
        {
            ["originalId"] = ship.ShipId, ["wallTarget"] = firstTarget.PieceId,
            ["wallHit"] = wallHit, ["afterWall"] = afterWall, ["floorAfterWall"] = floorAfterWall,
            ["floorTarget"] = nextTarget.PieceId, ["floorHit"] = floorHit,
            ["firstSplit"] = firstSplit, ["tailTarget"] = tailTarget?.PieceId ?? string.Empty,
            ["tailHit"] = tailHit, ["tailSplit"] = FragmentReadings(tailSplit),
            ["restoredTailSplit"] = FragmentReadings(restored),
            ["diagonalSplit"] = FragmentReadings(diagonalSplit), ["cargoSnapshot"] = cargo.SnapshotJson
        };
    }

    #endregion
}
