using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

/// <summary>机械网络的轻量拓扑节点。冷状态只保留端口信息和物品快照，活动状态才持有加工器与可变运行数据。</summary>
public sealed class MachineEntity
{
    #region 节点身份
    public int Id;
    public Vector2Int Cell;
    public MachineDefinition Definition;
    public ItemData Snapshot;
    public MachineState State;
    public RecipeProcessor Processor;
    public MachineLogic Logic; // 非机械专用玩法由粗粒度领域对象执行。
    public bool Active;
    public float AwaySeconds;
    public float LogicElapsed;
    public BoundsInt SimulationBounds;
    public float Airflow; // 本轮环境输入，不保存第二份机械网络状态。
    public Vector3 Position => Snapshot.transform.position;
    public Mod_MechanicalNode View;
    public int RotationQuarterTurns; // 已安装机械节点的逆时针九十度步数。
    public bool Engaged = true;
    public int RatioIndex = 1;
    public float SpeedRatio = 1f;
    public float TorqueRatio = 1f;
    public float SourceFactor;
    public float SourceRpm; // 当前动力源按其环境输入计算出的实际转速。
    public bool IncomingPower; // 所在机械网已接入其他有效动力源，手推按钮据此置灰。
    public int EntryDirection = -1;
    public int GearboxCrossings;
    public bool FlowVisited;
    public int SourceDistance; // 距最近可达动力源的传动距离。
    public bool LoadSatisfied; // 当前用力器是否取得全部固定需求。
    public float AvailableTorque; // 当前节点在本次分配时可取得的本地扭矩。
    public float RemainingSourceTorque; // 按根侧单位记录尚未分配的动力源扭矩。
    public float Rpm;
    public float VisualRpm; // 上次提交给 BRG 的转速。
    public float VisualPhase; // 上次转速变化时的连续动画相位，单位弧度。
    public float VisualTime; // 相位采样时的 Unity 时间。
    public float GearboxLargePhase, GearboxLargeSpeed, GearboxLargeTime; // 双齿轮独立相位锚点。
    public float GearboxSmallPhase, GearboxSmallSpeed, GearboxSmallTime;
    public MechanicalNetwork Network;
    public readonly List<MechanicalLink> Links = new();
    public ElectricalNetwork ElectricalNetwork;
    public float ElectricalStoredJoules; // 电池轻量储能状态，节点休眠时仍保留。
    public float ElectricalVoltage;
    public float ElectricalRequestedWatts;
    public float ElectricalSuppliedWatts;
    public float ElectricalGeneratedWatts;
    public float ElectricalCurrentAmps;
    public float ElectricalPowerRatio;

    public bool HasPort(int direction)
    {
        if (!Definition.HasMechanicalPorts) return false;
        if (Definition.Kind == "clutch" && !Engaged) return false;
        return Definition.Ports == "all" || (RotationQuarterTurns & 1) == (direction & 1);
    }
    /// <summary>用力器只输入；扭矩源与传动件按当前供能方向标记输入侧和输出侧，同速扭矩源可接入已有网络。</summary>
    public string GetPortMode(int direction)
    {
        if (!HasPort(direction)) return "closed";
        if (Definition.ManualDriveTorque > 0)
        {
            if (SourceFactor <= 0) return "input";
            return FlowVisited && EntryDirection == direction ? "input" : "output";
        }
        string mode = Definition.GetPortMode();
        if (mode == "output" && Definition.Torque > 0 && FlowVisited && EntryDirection >= 0)
            return direction == EntryDirection ? "input" : "output";
        if (mode != "relay") return mode;
        if (!FlowVisited) return "relay";
        if (EntryDirection >= 0) return direction == EntryDirection ? "input" : "output";
        return SourceFactor > 0 && Definition.Torque > 0 ? "output" : "relay";
    }
    /// <summary>手推用力器在供能期间临时成为出力器，其余节点沿用定义扭矩。</summary>
    public float SourceTorque => Definition.ManualDriveTorque > 0 ? Definition.ManualDriveTorque : Definition.Torque;
    /// <summary>齿轮彼此仍按齿牙端口啮合；其他节点按配置的局部传动轴接口连通。</summary>
    public bool CanConnectTo(MachineDefinition other, int direction)
    {
        if (other == null || !HasPort(direction)) return false;
        if (Definition.Kind == "gear" && other.Kind == "gear") return true;
        return Definition.HasAxlePort((direction - RotationQuarterTurns + 4) % 4);
    }
    /// <summary>按箱体朝向把世界输入方向换回局部方向，右侧小齿轮输入时使用逆向传动比。</summary>
    public bool IsGearboxSmallGearInput()
        => EntryDirection >= 0 && ((EntryDirection - RotationQuarterTurns + 4) & 3) == 0;
    /// <summary>供给显示为沿传动路径实际可取得的扭矩；用力器始终显示自身固定需求。</summary>
    public void GetLocalTorque(out float supply, out float demand)
    {
        demand = MechanicalNetworkGraph.RoundLoadTorqueToTens(Definition.TorqueLoad);
        if (!FlowVisited || Network == null)
        {
            supply = 0f;
            return;
        }
        supply = Definition.TorqueLoad > 0f
            ? AvailableTorque
            : MechanicalNetworkGraph.GetRemainingLocalTorque(this);
    }
    /// <summary>局部负载不足只停止本用力器，传动冲突仍服从整网状态。</summary>
    public string GetOperatingStatus()
    {
        string status = Network?.Status ?? "停止";
        return status == "运行中" && Definition.TorqueLoad > 0f && !LoadSatisfied ? "过载" : status;
    }
    /// <summary>旧 MOD 使用的正向端口速比入口；实际双向传动由 GetTransmission 求解。</summary>
    public float PortRatio(int direction)
    {
        if (Definition.Kind != "gearbox" || direction >= 2) return 1f;
        return Definition.Ratios[Mathf.Clamp(RatioIndex, 0, Definition.Ratios.Length - 1)];
    }
    #endregion
}

/// <summary>相邻机械端口的双向拓扑连接；Direction 是从当前节点指向邻节点的世界方向。</summary>
public readonly struct MechanicalLink
{
    public readonly MachineEntity Target;
    public readonly int Direction;
    public MechanicalLink(MachineEntity target, int direction) { Target = target; Direction = direction; }
}

/// <summary>实际连通网络的运行单位；Bounds 仅在拓扑变化时计算，SingleChunk 不建立跨区块端口表。</summary>
public sealed class MechanicalNetwork
{
    public readonly List<MachineEntity> Nodes = new();
    internal readonly Queue<MachineEntity> FlowQueue = new();
    public BoundsInt Bounds;
    public bool SingleChunk;
    public bool Active;
    public float AwaySeconds;
    public bool RatioConflict;
    public bool OutputConflict;
    public string Status = "停止";
    public float TorqueSupply;
    public float TorqueDemand;
}

/// <summary>确定性的分层端口构图与整网负载求解；邻块显示卸载不参与断网判断。</summary>
public sealed class MechanicalNetworkGraph
{
    #region 拓扑索引
    public static readonly Vector2Int[] Directions = { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down };
    private readonly Dictionary<Vector3Int, MachineEntity> cells = new();
    private readonly HashSet<Vector2Int> occupiedOnPlacementLayers = new();
    public readonly List<MechanicalNetwork> Networks = new();
    public readonly List<MachineEntity> Facilities = new(); // 无端口设施不构造扭矩网络。
    private readonly Func<Vector2Int, Vector2Int> normalize; // 旧 MOD 构图入口的坐标归一化委托。
    private readonly WorldTopologyDomain topology; // 核心机械世界的纯坐标域快照。
    private readonly bool hasTopologySnapshot; // 区分核心快照与旧委托入口。
    private readonly Vector2Int chunkSize;
    private readonly Vector2Int chunkPeriod;
    private readonly InteractionWindow pointerWindow = new(); // 光标查询候选窗口。
    private readonly InteractionWindow nearbyWindow = new(); // 近距离按键查询候选窗口。

    public MechanicalNetworkGraph(Vector2Int chunkSize, Vector2Int chunkPeriod, Func<Vector2Int, Vector2Int> normalize)
    {
        this.chunkSize = new Vector2Int(Mathf.Max(1, chunkSize.x), Mathf.Max(1, chunkSize.y));
        this.chunkPeriod = chunkPeriod;
        this.normalize = normalize ?? Identity;
    }
    /// <summary>机械世界在作用域建立时冻结纯坐标域，热路径不再通过委托回查存档。</summary>
    public MechanicalNetworkGraph(Vector2Int chunkSize, Vector2Int chunkPeriod, WorldTopologyDomain topology)
    {
        this.chunkSize = new Vector2Int(Mathf.Max(1, chunkSize.x), Mathf.Max(1, chunkSize.y));
        this.chunkPeriod = chunkPeriod;
        this.topology = topology;
        hasTopologySnapshot = true;
    }
    private static Vector2Int Identity(Vector2Int value) => value;
    /// <summary>核心机械世界使用冻结坐标域；旧 MOD 构图入口继续使用原有归一化委托。</summary>
    internal Vector2Int NormalizeCell(Vector2Int cell)
        => hasTopologySnapshot
            ? new Vector2Int(topology.NormalizeX(cell.x), topology.NormalizeY(cell.y))
            : normalize(cell);
    /// <summary>交互距离使用与机械格索引相同的世界坐标域。</summary>
    internal WorldTopologyDomain Topology => topology;
    public Vector2Int ChunkOf(Vector2Int cell) => new(Mathf.FloorToInt((float)cell.x / chunkSize.x), Mathf.FloorToInt((float)cell.y / chunkSize.y));
    public MachineEntity At(Vector2Int cell, int layer)
        => AtNormalized(NormalizeCell(cell), layer);

    /// <summary>调用方已将世界格归一化时直接查索引，避免邻格或交互扫描重复读取世界拓扑。</summary>
    internal MachineEntity AtNormalized(Vector2Int cell, int layer)
        => cells.TryGetValue(new Vector3Int(cell.x, cell.y, layer), out var node) ? node : null;

    /// <summary>已归一化格上的任一放置层占用，供建筑与 LOS 一次查询。</summary>
    internal bool IsOccupiedOnPlacementLayersNormalized(Vector2Int cell)
        => occupiedOnPlacementLayers.Contains(cell);

    /// <summary>空机械世界跳过逐帧交互格扫描。</summary>
    internal bool HasNodes => cells.Count > 0;

    /// <summary>同格内光标和玩家移动复用候选节点；拓扑变化时随索引一起失效。</summary>
    internal IReadOnlyList<MachineEntity> GetInteractionCandidates(Vector2Int center, int extent, bool pointed)
    {
        InteractionWindow window = pointed ? pointerWindow : nearbyWindow;
        if (window.Valid && window.Center == center && window.Extent == extent)
            return window.Nodes;

        window.Reset();
        window.Center = center;
        window.Extent = extent;
        for (int dy = -extent; dy <= extent; dy++)
        for (int dx = -extent; dx <= extent; dx++)
        {
            Vector2Int cell = NormalizeCell(center + new Vector2Int(dx, dy));
            for (int layer = 0; layer <= 3; layer++)
            {
                MachineEntity node = AtNormalized(cell, layer);
                if (node != null && window.Seen.Add(node)) window.Nodes.Add(node);
            }
        }
        window.Valid = true;
        return window.Nodes;
    }

    /// <summary>拓扑索引拥有候选窗口及去重集合，重构时整体失效。</summary>
    private sealed class InteractionWindow
    {
        public bool Valid; // 当前窗口是否已建立。
        public Vector2Int Center; // 查询中心世界格。
        public int Extent; // 格半径。
        public readonly List<MachineEntity> Nodes = new(); // 已收集的候选节点。
        public readonly HashSet<MachineEntity> Seen = new(); // 循环世界接缝去重。
        /// <summary>清除拓扑变化前的候选节点。</summary>
        public void Reset() { Valid = false; Nodes.Clear(); Seen.Clear(); }
    }

    /// <summary>仅放置、拆除、离合器和变速操作调用；恢复时先构建完整网络，再允许任意节点模拟。</summary>
    public void Rebuild(IEnumerable<MachineEntity> nodes)
    {
        pointerWindow.Reset(); nearbyWindow.Reset();
        cells.Clear(); occupiedOnPlacementLayers.Clear(); Networks.Clear(); Facilities.Clear();
        var sorted = new List<MachineEntity>(nodes);
        sorted.Sort(CompareNodes);
        foreach (var node in sorted)
        {
            node.Cell = NormalizeCell(node.Cell);
            var key = new Vector3Int(node.Cell.x, node.Cell.y, node.Definition.Layer);
            if (cells.ContainsKey(key)) throw new InvalidOperationException("机械格重复占用：" + key);
            cells.Add(key, node);
            if (node.Definition.Layer == 0 || node.Definition.Layer == 1)
                occupiedOnPlacementLayers.Add(node.Cell);
            node.Network = null;
            node.Links.Clear();
            node.FlowVisited = false;
            Vector2Int nodeChunk = ChunkOf(node.Cell);
            node.SimulationBounds = new BoundsInt(nodeChunk.x, nodeChunk.y, 0, 1, 1, 1);
            if (!node.Definition.HasMechanicalPorts) Facilities.Add(node);
        }
        var queue = new Queue<MachineEntity>();
        foreach (var root in sorted)
        {
            if (root.Network != null || !root.Definition.HasMechanicalPorts) continue;
            var network = new MechanicalNetwork();
            Networks.Add(network);
            root.Network = network;
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                network.Nodes.Add(node);
                network.Active |= node.State != null;
                for (int direction = 0; direction < 4; direction++)
                {
                    if (!node.HasPort(direction)) continue;
                    Vector2Int neighborCell = NormalizeCell(node.Cell + Directions[direction]);
                    // Layer1 只接两侧 Layer0 端点；绝不连接同格下层或另一座跨轴器。
                    Link(node, AtNormalized(neighborCell, 0), direction, network, queue);
                    if (node.Definition.Layer == 0)
                        Link(node, AtNormalized(neighborCell, 1), direction, network, queue);
                }
            }
            network.Bounds = CalculateBounds(network.Nodes);
            network.SingleChunk = network.Bounds.size.x == 1 && network.Bounds.size.y == 1;
        }
    }

    private static int CompareNodes(MachineEntity a, MachineEntity b) => a.Id.CompareTo(b.Id);
    private static void Link(MachineEntity from, MachineEntity to, int direction, MechanicalNetwork network, Queue<MachineEntity> queue)
    {
        if (to == null || ReferenceEquals(from, to) || !from.CanConnectTo(to.Definition, direction) ||
            !to.CanConnectTo(from.Definition, (direction + 2) % 4)) return;
        string fromMode = from.Definition.GetPortMode();
        string toMode = to.Definition.GetPortMode();
        if (fromMode == "input" && toMode == "input") return; // 输入端彼此不传动。
        RegisterLink(from, to, direction);
        if (to.Network == null)
        {
            to.Network = network;
            queue.Enqueue(to);
        }
    }
    /// <summary>每对相邻端口只登记一次，后续扭矩求解从供能端沿连接传播。</summary>
    private static void RegisterLink(MachineEntity from, MachineEntity to, int direction)
    {
        foreach (MechanicalLink link in from.Links)
            if (ReferenceEquals(link.Target, to) && link.Direction == direction) return;
        from.Links.Add(new MechanicalLink(to, direction));
        to.Links.Add(new MechanicalLink(from, (direction + 2) % 4));
    }
    #endregion

    #region 区块范围与整网滞回
    private BoundsInt CalculateBounds(List<MachineEntity> nodes)
    {
        var xs = new List<int>(nodes.Count);
        var ys = new List<int>(nodes.Count);
        foreach (var node in nodes) { Vector2Int chunk = ChunkOf(node.Cell); xs.Add(chunk.x); ys.Add(chunk.y); }
        GetAxisBounds(xs, chunkPeriod.x, out int x, out int width);
        GetAxisBounds(ys, chunkPeriod.y, out int y, out int height);
        return new BoundsInt(x, y, 0, width, height, 1);
    }

    /// <summary>循环世界去掉最大空白弧，接缝两侧的小网络仍保持最短包围范围。</summary>
    private static void GetAxisBounds(List<int> values, int period, out int minimum, out int length)
    {
        values.Sort();
        minimum = values[0];
        length = values[values.Count - 1] - minimum + 1;
        if (period <= 0 || values.Count < 2) return;
        int gap = values[0] + period - values[values.Count - 1];
        for (int i = 1; i < values.Count; i++)
        {
            int candidate = values[i] - values[i - 1];
            if (candidate <= gap) continue;
            gap = candidate; minimum = values[i];
        }
        length = period - gap + 1;
    }

    public bool IsNear(MechanicalNetwork network, Vector2Int playerChunk, int expansion)
        => IsNear(network.Bounds, playerChunk, expansion);

    public bool IsNear(BoundsInt bounds, Vector2Int playerChunk, int expansion)
        => AxisContains(playerChunk.x, bounds.xMin, bounds.size.x, expansion, chunkPeriod.x) &&
           AxisContains(playerChunk.y, bounds.yMin, bounds.size.y, expansion, chunkPeriod.y);

    private static bool AxisContains(int value, int minimum, int length, int expansion, int period)
    {
        long start = (long)minimum - expansion;
        long span = (long)length + expansion * 2L;
        if (period <= 0) return value >= start && value < start + span;
        if (span >= period) return true;
        long offset = ((long)value - start) % period;
        if (offset < 0) offset += period;
        return offset < span;
    }

    /// <summary>每网只查玩家 Chunk 与缓存 Bounds，远离冷却不会扫描节点距离。</summary>
    public bool ShouldBeActive(MechanicalNetwork network, IReadOnlyList<Vector2Int> players, float elapsed, MechanicalSettings settings)
    {
        int range = network.Active ? settings.DeactivationChunks : settings.ActivationChunks;
        for (int i = 0; i < players.Count; i++)
            if (IsNear(network, players[i], range)) { network.AwaySeconds = 0; return true; }
        if (!network.Active) return false;
        network.AwaySeconds += Mathf.Max(0, elapsed);
        return network.AwaySeconds < settings.UnloadDelaySeconds;
    }
    #endregion

    #region 扭矩求解
    /// <summary>同速扭矩源沿连接合流；输入端不转送扭矩，转速或闭环倍率冲突时整网停转。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Solve(MechanicalNetwork network, Func<MachineEntity, float> sourceFactor, float referenceRpm)
        => Solve(network, sourceFactor, node => node?.Definition?.Rpm ?? 0f, referenceRpm);

    /// <summary>动力源分别提供可用扭矩比例与实际转速；动态水流源可按采样速度驱动整网。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Solve(MechanicalNetwork network, Func<MachineEntity, float> sourceFactor,
        Func<MachineEntity, float> sourceRpm, float referenceRpm)
    {
        // 保留 referenceRpm 公开参数供旧 MOD 调用；固定扭矩需求不再依赖基准转速。
        network.TorqueSupply = 0; network.TorqueDemand = 0;
        network.RatioConflict = false; network.OutputConflict = false;
        MachineEntity root = null;
        foreach (var node in network.Nodes)
        {
            node.Rpm = 0;
            node.SpeedRatio = 1f; node.TorqueRatio = 1f;
            node.EntryDirection = -1; node.GearboxCrossings = 0; node.FlowVisited = false;
            node.SourceDistance = int.MaxValue;
            node.LoadSatisfied = false; node.AvailableTorque = 0f;
            node.RemainingSourceTorque = 0f;
            node.SourceFactor = node.SourceTorque > 0 ? Mathf.Clamp01(sourceFactor(node)) : 0f;
            float suppliedRpm = node.SourceFactor > 0f ? sourceRpm(node) : 0f;
            node.SourceRpm = MachineDefinition.Positive(suppliedRpm) ? suppliedRpm : 0f;
            // 不足半档的环境动力不参与同速源冲突，也不阻止手推石磨接管。
            if (node.SourceRpm <= 0f || RoundTorqueToTens(node.SourceTorque * node.SourceFactor) <= 0f)
            { node.SourceFactor = 0f; node.SourceRpm = 0f; }
            if (root == null && node.SourceFactor > 0 && node.SourceTorque > 0) root = node;
        }
        if (root == null) { network.Status = "无扭矩"; return; }
        PropagateTransmission(network, root, root);
        // 输入终点不转送转速；另一侧若有同速扭矩源，仍从自己的输出侧传播。
        foreach (var node in network.Nodes)
        {
            if (node.SourceFactor <= 0 || node.SourceTorque <= 0 || node.FlowVisited) continue;
            if (!SameSpeed(node.SourceRpm, root.SourceRpm))
            { network.OutputConflict = true; continue; }
            PropagateTransmission(network, root, node);
        }
        foreach (var node in network.Nodes)
            if (node.SourceFactor > 0 && node.SourceTorque > 0 && !node.FlowVisited)
                network.OutputConflict = true;
        if (network.OutputConflict) { network.Status = "机械卡死"; return; }
        if (network.RatioConflict) { network.Status = "传动比冲突"; return; }
        float rootRpm = root.SourceRpm;
        var consumers = new List<MachineEntity>();
        foreach (var node in network.Nodes)
        {
            if (!node.FlowVisited) continue; // 用力器输入端以外的断开支路不吃动力。
            if (node.SourceFactor > 0 && node.SourceTorque > 0)
            {
                node.RemainingSourceTorque = RoundTorqueToTens(node.SourceTorque * node.SourceFactor) / node.TorqueRatio;
                network.TorqueSupply += node.RemainingSourceTorque;
            }
            if (node.Definition.TorqueLoad > 0f)
            {
                network.TorqueDemand += GetRootSideLoad(node);
                consumers.Add(node);
            }
        }
        if (network.TorqueSupply <= 0f) { network.Status = "无扭矩"; return; }
        var sourceRoutes = new Dictionary<MachineEntity, List<SourceRoute>>(consumers.Count);
        foreach (var consumer in consumers)
        {
            List<SourceRoute> routes = CollectSourceRoutes(consumer);
            sourceRoutes.Add(consumer, routes);
            if (routes.Count > 0) consumer.SourceDistance = routes[0].Distance;
        }
        consumers.Sort(CompareConsumers);
        foreach (var consumer in consumers)
        {
            float required = GetRootSideLoad(consumer);
            List<SourceRoute> routes = sourceRoutes[consumer];
            float available = RouteTorque(routes, float.PositiveInfinity, false);
            consumer.AvailableTorque = available * consumer.TorqueRatio;
            if (available + .001f < required) continue;
            RouteTorque(routes, required, true);
            consumer.LoadSatisfied = true;
        }
        network.Status = "运行中";
        foreach (var node in network.Nodes)
        {
            if (!node.FlowVisited) continue;
            if (node.Definition.TorqueLoad <= 0f || node.LoadSatisfied)
                node.Rpm = node.SpeedRatio * rootRpm;
        }
    }

    /// <summary>传动箱从输入侧到另一侧才换算倍率；多个方向到同一节点时核对速度与扭矩，防止闭环无端增益。</summary>
    private static void PropagateTransmission(MechanicalNetwork network, MachineEntity referenceSource, MachineEntity start)
    {
        Queue<MachineEntity> queue = network.FlowQueue;
        queue.Clear();
        start.FlowVisited = true;
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            MachineEntity from = queue.Dequeue();
            foreach (MechanicalLink link in from.Links)
            {
                string fromPort = from.GetPortMode(link.Direction);
                if (fromPort == "input" || fromPort == "closed") continue;
                MachineEntity to = link.Target;
                int entry = (link.Direction + 2) % 4;
                float speed = from.SpeedRatio;
                float torque = from.TorqueRatio;
                int crossings = from.GearboxCrossings;
                if (from.Definition.Kind == "gearbox" && from.EntryDirection >= 0 && link.Direction != from.EntryDirection)
                {
                    from.Definition.GetTransmission(from.IsGearboxSmallGearInput(), from.RatioIndex,
                        out float speedStep, out float torqueStep);
                    speed *= speedStep; torque *= torqueStep; crossings++;
                }
                if (!MachineDefinition.Positive(speed) || !MachineDefinition.Positive(torque) ||
                    speed > 10000f || speed < .0001f || torque > 10000f || torque < .0001f)
                { network.RatioConflict = true; continue; }
                if (to.GetPortMode(entry) == "output" && to.SourceTorque <= 0)
                { network.OutputConflict = true; continue; }
                // 扭矩源既能受同网带动，也能叠加扭矩；必须与根源及到达自身的实际转速一致。
                if (to.SourceFactor > 0 && to.SourceTorque > 0 &&
                    (!SameSpeed(to.SourceRpm, referenceSource.SourceRpm) ||
                     !SameSpeed(to.SourceRpm, referenceSource.SourceRpm * speed)))
                { network.OutputConflict = true; continue; }
                if (to.FlowVisited)
                {
                    if (!SameRatio(to.SpeedRatio, speed) || !SameRatio(to.TorqueRatio, torque) ||
                        to.GearboxCrossings != crossings ||
                        (to.Definition.Kind == "gearbox" && to.EntryDirection != entry))
                        network.RatioConflict = true;
                    continue;
                }
                to.FlowVisited = true;
                to.SpeedRatio = speed; to.TorqueRatio = torque;
                to.EntryDirection = entry; to.GearboxCrossings = crossings;
                queue.Enqueue(to);
            }
        }
    }

    private static bool SameRatio(float left, float right)
        => Mathf.Abs(left - right) <= Mathf.Max(.001f, Mathf.Abs(right) * .001f);

    private static bool SameSpeed(float left, float right)
        => Mathf.Abs(left - right) <= Mathf.Max(.001f, Mathf.Max(Mathf.Abs(left), Mathf.Abs(right)) * .0001f);

    /// <summary>较近的用力器先取力，同距离按稳定节点 ID 排序。</summary>
    private static int CompareConsumers(MachineEntity left, MachineEntity right)
    {
        int distance = left.SourceDistance.CompareTo(right.SourceDistance);
        return distance != 0 ? distance : left.Id.CompareTo(right.Id);
    }

    /// <summary>用力器需求固定为配置值，仅为上游分配换算到根侧单位。</summary>
    private static float GetRootSideLoad(MachineEntity node)
        => RoundLoadTorqueToTens(node.Definition.TorqueLoad) / node.TorqueRatio;

    /// <summary>从用力器沿实际连接查找可达动力源，输入终点不能作为其它节点的传动通道。</summary>
    private static List<SourceRoute> CollectSourceRoutes(MachineEntity destination)
    {
        var routes = new List<SourceRoute>();
        var visited = new HashSet<MachineEntity> { destination };
        var queue = new Queue<SourceRoute>();
        queue.Enqueue(new SourceRoute(destination, 0));
        while (queue.Count > 0)
        {
            SourceRoute current = queue.Dequeue();
            MachineEntity node = current.Node;
            if (node.SourceFactor > 0f && node.SourceTorque > 0f)
                routes.Add(current);
            if (!ReferenceEquals(node, destination) && node.SourceFactor <= 0f &&
                (node.Definition.GetPortMode() == "input" || node.Definition.ManualDriveTorque > 0f)) continue;
            foreach (MechanicalLink link in node.Links)
            {
                if (!link.Target.FlowVisited || !visited.Add(link.Target)) continue;
                queue.Enqueue(new SourceRoute(link.Target, current.Distance + 1));
            }
        }
        routes.Sort(CompareSourceRoutes);
        return routes;
    }

    /// <summary>同距离的动力源按稳定节点 ID 补入扭矩。</summary>
    private static int CompareSourceRoutes(SourceRoute left, SourceRoute right)
    {
        int distance = left.Distance.CompareTo(right.Distance);
        return distance != 0 ? distance : left.Node.Id.CompareTo(right.Node.Id);
    }

    /// <summary>依距离顺序预留扭矩；轴与齿轮只传递，不限制通过量。</summary>
    private static float RouteTorque(List<SourceRoute> routes, float target, bool commit)
    {
        float delivered = 0f;
        foreach (SourceRoute route in routes)
        {
            float draw = Mathf.Min(route.Node.RemainingSourceTorque, target - delivered);
            if (draw <= 0f) continue;
            delivered += draw;
            if (commit) route.Node.RemainingSourceTorque -= draw;
            if (delivered + .001f >= target) break;
        }
        return delivered;
    }

    /// <summary>传动件面板按需读取已完成用力器分配后的剩余本地扭矩。</summary>
    public static float GetRemainingLocalTorque(MachineEntity node)
        => node == null || !node.FlowVisited ? 0f
            : RouteTorque(CollectSourceRoutes(node), float.PositiveInfinity, false) * node.TorqueRatio;

    /// <summary>动力源及其到目标的最短传动距离。</summary>
    private readonly struct SourceRoute
    {
        public readonly MachineEntity Node;
        public readonly int Distance;
        public SourceRoute(MachineEntity node, int distance) { Node = node; Distance = distance; }
    }

    /// <summary>动力源先在自身所在侧取最近十位档，再沿变速箱倍率换算。</summary>
    public static float RoundTorqueToTens(float torque)
    {
        if (torque <= 0f) return 0f;
        return Mathf.Floor(torque / 10f + .5f) * 10f;
    }

    /// <summary>非零用力器至少消耗一档；需求只由自身配置决定，不随转速变化。</summary>
    public static float RoundLoadTorqueToTens(float torque)
        => torque > 0f ? Mathf.Max(10f, RoundTorqueToTens(torque)) : 0f;
    #endregion
}
