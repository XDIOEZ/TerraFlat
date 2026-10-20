using System.Collections.Generic;
using FlatWorld.Spaceflight;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 独立线路连接表现规则采样

    // 只准备独立格子与端口，连接结果交正式图计算，Lua 保留期望与断言。
    private static JObject WorldElectricalLineConnections(JObject args)
    {
        Vector2Int[] directions = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
        MachineEntity Wire(int id, Vector2Int cell)
        {
            MachineEntity node = WorldMachineNode(id, cell.x);
            node.Cell = cell;
            node.Definition.Ports = "none";
            node.Definition.Electrical = new ElectricalDefinition { Role = "wire" };
            return node;
        }
        var graph = new ElectricalNetworkGraph(default);
        var center = Wire(1, new Vector2Int(-3, 2));
        var nodes = new List<MachineEntity>();
        var masks = new JArray();
        for (int configuration = 0; configuration < 16; configuration++)
        {
            nodes.Clear(); nodes.Add(center);
            for (int direction = 0; direction < 4; direction++)
                if ((configuration & (1 << direction)) != 0)
                    nodes.Add(Wire(direction + 2, center.Cell + directions[direction]));
            graph.Rebuild(nodes);
            masks.Add(graph.GetWireConnectionMask(center.Cell));
        }

        nodes.Clear(); nodes.Add(center);
        var neighbor = Wire(10, center.Cell + Vector2Int.right);
        nodes.Add(neighbor); graph.Rebuild(nodes);
        int beforeRemoval = graph.GetWireConnectionMask(center.Cell);
        nodes.Remove(neighbor); graph.Rebuild(nodes);
        int afterRemoval = graph.GetWireConnectionMask(center.Cell);
        nodes.Add(neighbor); graph.Rebuild(nodes);
        int afterRepair = graph.GetWireConnectionMask(center.Cell);
        var isolated = new ElectricalNetworkGraph(default);
        isolated.Rebuild(new[] { Wire(100, center.Cell), Wire(101, center.Cell + Vector2Int.up) });

        var endpoints = new JArray();
        Vector2Int[] positions = { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down };
        for (int rotation = 0; rotation < 4; rotation++)
        {
            MachineEntity endpoint = WorldMachineNode(20 + rotation, 0);
            endpoint.Cell = center.Cell + positions[rotation];
            endpoint.RotationQuarterTurns = rotation;
            endpoint.Definition.Electrical = new ElectricalDefinition { Role = "consumer", Connection = "left" };
            graph.Rebuild(new[] { center, endpoint });
            int facing = graph.GetWireConnectionMask(center.Cell);
            endpoint.RotationQuarterTurns = (rotation + 1) & 3;
            graph.Rebuild(new[] { center, endpoint });
            endpoints.Add(new JObject { ["facing"] = facing, ["away"] = graph.GetWireConnectionMask(center.Cell) });
        }
        MachineEntity colocated = WorldMachineNode(30, center.Cell.x);
        colocated.Cell = center.Cell;
        colocated.Definition.Electrical = new ElectricalDefinition { Role = "consumer", Connection = "cell" };
        graph.Rebuild(new[] { center, colocated });
        return new JObject { ["masks"] = masks, ["beforeRemoval"] = beforeRemoval,
            ["afterRemoval"] = afterRemoval, ["afterRepair"] = afterRepair,
            ["independentGraphMask"] = isolated.GetWireConnectionMask(center.Cell),
            ["endpoints"] = endpoints, ["colocatedMask"] = graph.GetWireConnectionMask(center.Cell) };
    }

    private static JObject WorldFiberConnections(JObject args)
    {
        ShipPieceState Piece(string id, ShipPieceKind kind, int x, int y) => new()
        { PieceId = id, Kind = kind, CellX = x, CellY = y };
        var ship = new ShipState { ShipId = "probe:fiber-connections" };
        var center = Piece("center", ShipPieceKind.Fiber, -3, 2);
        (int X, int Y)[] directions = { (0, 1), (1, 0), (0, -1), (-1, 0) };
        var masks = new JArray();
        for (int configuration = 0; configuration < 16; configuration++)
        {
            ship.Pieces.Clear(); ship.Pieces.Add(center);
            for (int direction = 0; direction < 4; direction++)
                if ((configuration & (1 << direction)) != 0)
                    ship.Pieces.Add(Piece("neighbor-" + direction, ShipPieceKind.Fiber,
                        center.CellX + directions[direction].X, center.CellY + directions[direction].Y));
            ship.NotifyStructureChanged();
            masks.Add(ShipOpticalGraph.GetConnectionMask(ship, center));
        }
        ship.Pieces.Clear(); ship.Pieces.Add(center);
        var console = Piece("console", ShipPieceKind.Console, -2, 2);
        var navigation = Piece("navigation", ShipPieceKind.Navigation, -4, 2);
        ship.Pieces.Add(console); ship.Pieces.Add(navigation); ship.NotifyStructureChanged();
        int endpoints = ShipOpticalGraph.GetConnectionMask(ship, center);
        var workspace = new ShipOpticalGraph.Workspace();
        bool connected = ShipOpticalGraph.Connected(ship, console, navigation, workspace);
        console.Destroyed = true; ship.NotifyStructureChanged();
        int destroyedEndpoint = ShipOpticalGraph.GetConnectionMask(ship, center);
        console.Destroyed = false; ship.Pieces.Remove(navigation); ship.NotifyStructureChanged();
        int removedEndpoint = ShipOpticalGraph.GetConnectionMask(ship, center);
        ship.Pieces.Add(navigation); ship.NotifyStructureChanged();
        int repairedEndpoint = ShipOpticalGraph.GetConnectionMask(ship, center);
        center.Destroyed = true; ship.NotifyStructureChanged();
        int destroyedFiber = ShipOpticalGraph.GetConnectionMask(ship, center);
        bool disconnected = ShipOpticalGraph.Connected(ship, console, navigation, workspace);
        center.Destroyed = false; ship.NotifyStructureChanged();
        bool repaired = ShipOpticalGraph.Connected(ship, console, navigation, workspace);
        var otherShip = new ShipState { ShipId = "probe:other-fiber-ship" };
        otherShip.Pieces.Add(Piece("other-center", ShipPieceKind.Fiber, -3, 2));
        otherShip.Pieces.Add(Piece("other-north", ShipPieceKind.Fiber, -3, 3));
        otherShip.NotifyStructureChanged();
        int isolated = ShipOpticalGraph.GetConnectionMask(otherShip, otherShip.Pieces[0]);
        int originalAfterOtherShip = ShipOpticalGraph.GetConnectionMask(ship, center);
        int foreignFiber = ShipOpticalGraph.GetConnectionMask(otherShip, center);
        ship.Pieces.Remove(console); ship.Pieces.Remove(navigation);
        ship.Pieces.Add(Piece("wire", ShipPieceKind.Wire, -2, 2));
        ship.Pieces.Add(Piece("pipe", ShipPieceKind.Pipe, -4, 2));
        ship.NotifyStructureChanged();
        return new JObject { ["masks"] = masks, ["endpoints"] = endpoints,
            ["destroyedEndpoint"] = destroyedEndpoint, ["removedEndpoint"] = removedEndpoint,
            ["repairedEndpoint"] = repairedEndpoint, ["destroyedFiber"] = destroyedFiber,
            ["connected"] = connected, ["disconnected"] = disconnected, ["repaired"] = repaired,
            ["isolatedMask"] = isolated, ["originalAfterOtherShip"] = originalAfterOtherShip,
            ["foreignFiber"] = foreignFiber,
            ["sortingOrders"] = new JArray(
                ShipPieceView.ResolveSortingOrder(Piece("floor", ShipPieceKind.Floor, 0, 0), null),
                ShipPieceView.ResolveSortingOrder(Piece("pipe", ShipPieceKind.Pipe, 0, 0),
                    new MachineDefinition { Fluid = new FluidMachineDefinition { Kind = "pipe" } }),
                ShipPieceView.ResolveSortingOrder(Piece("wire", ShipPieceKind.Wire, 0, 0),
                    new MachineDefinition { Electrical = new ElectricalDefinition { Role = "wire" } }),
                ShipPieceView.ResolveSortingOrder(center, null)),
            ["nonOpticalNeighbors"] = ShipOpticalGraph.GetConnectionMask(ship, center) };
    }

    #endregion
}
