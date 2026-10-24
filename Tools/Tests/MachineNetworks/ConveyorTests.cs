using UnityEngine;

internal static class ConveyorTests
{
    #region 正式拓扑与侧轴几何回归
    internal static void Run(Action<bool, string> check)
    {
        MachineEntity Belt(int id, Vector2Int cell, int rotation = 1) => new()
        {
            Id = id, Cell = cell, State = new(), Snapshot = new(),
            RotationQuarterTurns = rotation, ConveyorMode = rotation + 1,
            Definition = new() { Ports = "all", Transport = new() }
        };
        MachineEntity Shaft(int id, Vector2Int cell, int rotation = 0) => new()
        {
            Id = id, Cell = cell, State = new(), Snapshot = new(),
            RotationQuarterTurns = rotation, Definition = new() { Ports = "axis" }
        };
        var graph = new MechanicalNetworkGraph(new(16, 16), Vector2Int.zero, default(WorldTopologyDomain));
        MachineEntity belt = Belt(100, Vector2Int.zero);
        MachineEntity right = Shaft(90, Vector2Int.right), left = Shaft(110, Vector2Int.left);
        void Rebuild(int mask, bool changed, string label, params MachineEntity[] nodes)
        {
            graph.Rebuild(nodes);
            check(belt.ConveyorConnectedDriveMask == mask, label + "：连接方向错误");
            check(graph.ConveyorChanges.Contains(belt) == changed, label + "：缺少或多余表现刷新");
            check(graph.ConveyorChanges.Count(node => ReferenceEquals(node, belt)) <= 1, label + "：重复刷新同一格");
        }
        Rebuild(0, true, "空带", belt);
        Rebuild(0, false, "稳定空带", belt);
        Rebuild(1, true, "后放右轴", belt, right);
        check(belt.Rpm == 0 && belt.ConveyorRoute.Mask == 10, "无动力连接不应改变带形或伪造转速");
        Rebuild(5, true, "两侧接轴", belt, right, left);
        Rebuild(4, true, "拆右轴", belt, left);
        left.RotationQuarterTurns = 1; Rebuild(0, true, "邻轴错向", belt, left);
        left.RotationQuarterTurns = 0; Rebuild(4, true, "恢复朝向", belt, left);
        left.Definition.Kind = "clutch"; left.Engaged = false;
        Rebuild(0, true, "离合器断开", belt, left);
        left.Engaged = true; Rebuild(4, true, "离合器接合", belt, left);
        belt.Rpm = -60; Rebuild(4, false, "反转不改变连接", belt, left);
        left.Definition.HasMechanicalPorts = false; Rebuild(0, true, "没有端口", belt, left);
        graph.Rebuild(new[] { right }); Rebuild(1, true, "先轴后带", right, belt);
        right.Definition.Layer = 1; Rebuild(1, false, "跨轴器接零层", belt, right);
        belt.Definition.Layer = 1; Rebuild(0, true, "一层不接一层", belt, right);
        belt.Definition.Layer = 0;
        Rebuild(0, false, "平行带不伸轴", belt, Belt(200, Vector2Int.right));
        Rebuild(0, false, "带端不伸轴", belt, Belt(201, Vector2Int.up));
        check(belt.Links.Count == 1, "测试必须真实连上相邻带端");

        for (int rotation = 0; rotation < 4; rotation++)
        {
            belt = Belt(100, Vector2Int.zero, rotation);
            int direction = (rotation + 1) & 3;
            right = Shaft(90, MechanicalNetworkGraph.Directions[direction], direction);
            left = Shaft(110, MechanicalNetworkGraph.Directions[(direction + 2) & 3], direction);
            Rebuild((rotation & 1) == 0 ? 10 : 5, true, "旋转" + rotation, belt, right, left);
        }
        belt = Belt(100, new(31, 0)); right = Shaft(90, Vector2Int.zero);
        var wrapped = new MechanicalNetworkGraph(new(16, 16), new(2, 0),
            cell => new Vector2Int((cell.x % 32 + 32) % 32, cell.y));
        wrapped.Rebuild(new[] { belt }); wrapped.Rebuild(new[] { right, belt });
        check(belt.ConveyorConnectedDriveMask == 1 && wrapped.ConveyorChanges.Contains(belt), "环绕与跨区块邻接漏刷新");
        check(!ConveyorPresentation.HasVisibleSidePort(new(0, 1), 2), "拐角应继续隐藏侧轴");

        // 正式世界重建入口必须在转速为零时也发出传送带本格的通知。
        belt = Belt(100, Vector2Int.zero); right = Shaft(90, Vector2Int.right);
        graph.Rebuild(new[] { belt });
        MachineWorld.SetWorld(graph, new ElectricalNetworkGraph(default), new[] { belt });
        var cells = new List<Vector2Int>();
        MachineWorld.CellChanged += cells.Add;
        try
        {
            MachineWorld.RebuildTopologyForTest(new[] { belt, right });
            check(cells.Count == 1 && cells[0] == belt.Cell, "放轴后未通过正式事件刷新传送带");
            cells.Clear(); MachineWorld.RebuildTopologyForTest(new[] { belt, right });
            check(cells.Count == 0, "稳定连接不应重复发送侧轴通知");
            MachineWorld.RebuildTopologyForTest(new[] { belt });
            check(cells.Count == 1 && cells[0] == belt.Cell, "拆轴后未通知传送带收回侧轴");
        }
        finally { MachineWorld.CellChanged -= cells.Add; }

        var visual = new MachineTransportVisualDefinition();
        foreach (float pivot in new[] { .2f, .5f, .8f })
        foreach (float ppu in new[] { 64f, 128f })
        {
            var port = new Sprite { rect = new(0, 0, 64, 128), pivot = new(64 * pivot, 64), pixelsPerUnit = ppu };
            var core = new Sprite { rect = new(0, 0, 80, 28), pivot = new(80 * pivot, 28 * pivot), pixelsPerUnit = ppu };
            Vector3 position = ConveyorPresentation.SidePortPosition(ConveyorPath.Straight(1), visual, port, true);
            check(MathF.Abs(position.x + (port.rect.width - port.pivot.x) / ppu - .5f) < .00001f, "铁环未对齐格边");
            check(ConveyorPresentation.SidePortPosition(ConveyorPath.Straight(1), visual, port, false).x == .1f, "短口位置改变");
            ConveyorPresentation.SideShaftGeometry(core, out Vector3 offset, out Vector3 scale);
            check(MathF.Abs(offset.x - core.pivot.x / ppu * scale.x) < .00001f, "木芯内端未藏入带框");
            check(MathF.Abs(offset.x + (core.rect.width - core.pivot.x) / ppu * scale.x - .5f) < .00001f, "木芯外端留缝");
            check(MathF.Abs(core.rect.height / ppu * scale.y - 28f / 128f) < .00001f, "木芯粗细不一致");
            check(MathF.Abs(offset.y + (core.rect.height * .5f - core.pivot.y) / ppu * scale.y) < .00001f, "木芯偏离轴线");
        }
    }
    #endregion
}
