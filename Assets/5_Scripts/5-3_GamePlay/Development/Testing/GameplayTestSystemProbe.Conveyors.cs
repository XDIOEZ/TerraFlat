using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 输送带侧轴规则采样
    // 独立节点只采样正式拓扑与几何结果，不注册或修改玩家正在使用的机器世界。
    private static JObject WorldConveyorSidePorts(JObject args)
    {
        MachineEntity Belt(int id, Vector2Int cell, int rotation = 1)
        {
            MachineEntity node = WorldMachineNode(id, cell.x);
            node.Cell = cell;
            node.Definition.Ports = "all";
            node.Definition.Transport = new MachineTransportDefinition();
            node.RotationQuarterTurns = rotation;
            node.ConveyorMode = rotation + 1;
            return node;
        }
        MachineEntity belt = Belt(100, Vector2Int.zero);
        MachineEntity right = WorldMachineNode(90, 1), left = WorldMachineNode(110, -1);
        MechanicalNetworkGraph graph = WorldMechanicalGraph(belt);
        JObject Sample() => new()
        {
            ["mask"] = belt.ConveyorConnectedDriveMask,
            ["changed"] = graph.ConveyorChanges.Contains(belt),
            ["changes"] = graph.ConveyorChanges.Count,
            ["route"] = belt.ConveyorRoute.Mask
        };
        var result = new JObject { ["empty"] = Sample() };
        graph.Rebuild(new[] { belt }); result["unchanged"] = Sample();
        graph.Rebuild(new[] { belt, right }); result["rightAdded"] = Sample();
        graph.Rebuild(new[] { belt, left, right }); result["bothAdded"] = Sample();
        graph.Rebuild(new[] { belt, left }); result["rightRemoved"] = Sample();
        left.RotationQuarterTurns = 1;
        graph.Rebuild(new[] { belt, left }); result["misaligned"] = Sample();
        left.RotationQuarterTurns = 0;
        graph.Rebuild(new[] { belt, left }); result["aligned"] = Sample();
        left.Definition.Kind = "clutch"; left.Engaged = false;
        graph.Rebuild(new[] { belt, left }); result["disengaged"] = Sample();
        left.Engaged = true;
        graph.Rebuild(new[] { belt, left }); result["engaged"] = Sample();
        belt.Rpm = -60f;
        graph.Rebuild(new[] { belt, left }); result["rpmChanged"] = Sample();
        left.Definition.Ports = "none";
        graph.Rebuild(new[] { belt, left }); result["noPort"] = Sample();
        graph.Rebuild(new[] { right });
        graph.Rebuild(new[] { right, belt }); result["shaftFirst"] = Sample();
        right.Definition.Kind = "bridge";
        graph.Rebuild(new[] { belt, right }); result["bridge"] = Sample();
        belt.Definition.PlacementLayer = 1;
        graph.Rebuild(new[] { belt, right }); result["wrongLayer"] = Sample();
        belt.Definition.PlacementLayer = -1;

        MachineEntity parallel = Belt(200, Vector2Int.right);
        graph.Rebuild(new[] { belt, parallel }); result["parallel"] = Sample();
        MachineEntity next = Belt(201, Vector2Int.up);
        graph.Rebuild(new[] { belt, next }); result["pathNeighbour"] = Sample();
        result["pathLinked"] = belt.Links.Count > 0;
        var rotations = new JArray();
        for (int rotation = 0; rotation < 4; rotation++)
        {
            belt = Belt(100, Vector2Int.zero, rotation);
            right = WorldMachineNode(90, 0); left = WorldMachineNode(110, 0);
            int direction = (rotation + 1) & 3;
            right.Cell = MechanicalNetworkGraph.Directions[direction];
            left.Cell = MechanicalNetworkGraph.Directions[(direction + 2) & 3];
            right.RotationQuarterTurns = left.RotationQuarterTurns = direction;
            graph.Rebuild(new[] { belt, right, left });
            rotations.Add(belt.ConveyorConnectedDriveMask);
        }
        result["rotations"] = rotations;
        belt = Belt(100, new Vector2Int(31, 0)); right = WorldMachineNode(90, 0);
        var wrapped = new MechanicalNetworkGraph(new Vector2Int(16, 16), new Vector2Int(2, 0),
            cell => new Vector2Int((cell.x % 32 + 32) % 32, cell.y));
        wrapped.Rebuild(new[] { belt }); wrapped.Rebuild(new[] { belt, right });
        result["wrappedMask"] = belt.ConveyorConnectedDriveMask;
        result["wrappedChanged"] = wrapped.ConveyorChanges.Contains(belt);
        result["curveHidden"] = !ConveyorPresentation.HasVisibleSidePort(new ConveyorPath(0, 1), 2);

        // 非中心 Pivot 与不同 PPU 仍必须把铁环外缘和木芯端点对齐半格边界。
        var texture = new Texture2D(128, 128);
        Sprite port = null, core = null;
        try
        {
            port = Sprite.Create(texture, new Rect(0, 0, 64, 128), new Vector2(.25f, .5f), 128f);
            core = Sprite.Create(texture, new Rect(0, 0, 80, 28), new Vector2(.2f, .7f), 64f);
            ConveyorPath route = ConveyorPath.Straight(1);
            var visual = new MachineTransportVisualDefinition();
            Vector3 connected = ConveyorPresentation.SidePortPosition(route, visual, port, true);
            result["portOuterEdge"] = connected.x + (port.rect.width - port.pivot.x) / port.pixelsPerUnit;
            result["retractedOffset"] = ConveyorPresentation.SidePortPosition(route, visual, port, false).x;
            ConveyorPresentation.SideShaftGeometry(core, out Vector3 offset, out Vector3 scale);
            result["shaftStart"] = offset.x - core.pivot.x / core.pixelsPerUnit * scale.x;
            result["shaftEnd"] = offset.x + (core.rect.width - core.pivot.x) / core.pixelsPerUnit * scale.x;
            result["shaftHeight"] = core.rect.height / core.pixelsPerUnit * scale.y;
            result["shaftCenterY"] = offset.y + (core.rect.height * .5f - core.pivot.y) / core.pixelsPerUnit * scale.y;
        }
        finally
        {
            if (port != null) Object.DestroyImmediate(port);
            if (core != null) Object.DestroyImmediate(core);
            Object.DestroyImmediate(texture);
        }
        return result;
    }
    #endregion
}
