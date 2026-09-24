using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>机械网络的轻量拓扑节点。冷状态只保留端口信息和物品快照，活动状态才持有加工器与可变运行数据。</summary>
public sealed class MechanicalNode
{
    #region 节点身份
    public int Id;
    public Vector2Int Cell;
    public MechanicalDefinition Definition;
    public ItemData Snapshot;
    public MechanicalNodeState State;
    public MechanicalProcessor Processor;
    public Mod_MechanicalNode View;
    public bool Vertical;
    public bool Engaged = true;
    public int RatioIndex = 1;
    public float SpeedRatio = 1f;
    public float TorqueRatio = 1f;
    public float SourceFactor;
    public int EntryDirection = -1;
    public int GearboxCrossings;
    public bool FlowVisited;
    public float Rpm;
    public MechanicalNetwork Network;
    public readonly List<MechanicalLink> Links = new();

    public bool HasPort(int direction)
    {
        if (Definition.Kind == "clutch" && !Engaged) return false;
        return Definition.Ports == "all" || (Vertical ? direction % 2 == 1 : direction % 2 == 0);
    }
    /// <summary>用力器只输入；扭矩源与传动件按当前供能方向标记输入侧和输出侧，同速扭矩源可接入已有网络。</summary>
    public string GetPortMode(int direction)
    {
        if (!HasPort(direction)) return "closed";
        string mode = Definition.GetPortMode();
        if (mode == "output" && Definition.Torque > 0 && FlowVisited && EntryDirection >= 0)
            return direction == EntryDirection ? "input" : "output";
        if (mode != "relay") return mode;
        if (!FlowVisited) return "relay";
        if (EntryDirection >= 0) return direction == EntryDirection ? "input" : "output";
        return SourceFactor > 0 && Definition.Torque > 0 ? "output" : "relay";
    }
    /// <summary>齿轮与非齿轮节点只经可见轴接头相连；齿轮之间仍按齿牙端口啮合。</summary>
    public bool CanConnectTo(MechanicalDefinition other, int direction)
    {
        if (other == null || !HasPort(direction)) return false;
        return Definition.Kind != "gear" || other.Kind == "gear" || Definition.HasAxlePort(direction);
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
    public readonly MechanicalNode Target;
    public readonly int Direction;
    public MechanicalLink(MechanicalNode target, int direction) { Target = target; Direction = direction; }
}

/// <summary>实际连通网络的运行单位；Bounds 仅在拓扑变化时计算，SingleChunk 不建立跨区块端口表。</summary>
public sealed class MechanicalNetwork
{
    public readonly List<MechanicalNode> Nodes = new();
    internal readonly Queue<MechanicalNode> FlowQueue = new();
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
    private readonly Dictionary<Vector3Int, MechanicalNode> cells = new();
    public readonly List<MechanicalNetwork> Networks = new();
    private readonly Func<Vector2Int, Vector2Int> normalize;
    private readonly Vector2Int chunkSize;
    private readonly Vector2Int chunkPeriod;

    public MechanicalNetworkGraph(Vector2Int chunkSize, Vector2Int chunkPeriod, Func<Vector2Int, Vector2Int> normalize)
    {
        this.chunkSize = new Vector2Int(Mathf.Max(1, chunkSize.x), Mathf.Max(1, chunkSize.y));
        this.chunkPeriod = chunkPeriod;
        this.normalize = normalize ?? Identity;
    }
    private static Vector2Int Identity(Vector2Int value) => value;
    public Vector2Int ChunkOf(Vector2Int cell) => new(Mathf.FloorToInt((float)cell.x / chunkSize.x), Mathf.FloorToInt((float)cell.y / chunkSize.y));
    public MechanicalNode At(Vector2Int cell, int layer)
        => cells.TryGetValue(new Vector3Int(normalize(cell).x, normalize(cell).y, layer), out var node) ? node : null;

    /// <summary>仅放置、拆除、离合器和变速操作调用；恢复时先构建完整网络，再允许任意节点模拟。</summary>
    public void Rebuild(IEnumerable<MechanicalNode> nodes)
    {
        cells.Clear(); Networks.Clear();
        var sorted = new List<MechanicalNode>(nodes);
        sorted.Sort(CompareNodes);
        foreach (var node in sorted)
        {
            node.Cell = normalize(node.Cell);
            var key = new Vector3Int(node.Cell.x, node.Cell.y, node.Definition.Layer);
            if (cells.ContainsKey(key)) throw new InvalidOperationException("机械格重复占用：" + key);
            cells.Add(key, node);
            node.Network = null;
            node.Links.Clear();
            node.FlowVisited = false;
        }
        var queue = new Queue<MechanicalNode>();
        foreach (var root in sorted)
        {
            if (root.Network != null) continue;
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
                    Vector2Int neighborCell = normalize(node.Cell + Directions[direction]);
                    // Layer1 只接两侧 Layer0 端点；绝不连接同格下层或另一座跨轴器。
                    Link(node, At(neighborCell, 0), direction, network, queue);
                    if (node.Definition.Layer == 0)
                        Link(node, At(neighborCell, 1), direction, network, queue);
                }
            }
            network.Bounds = CalculateBounds(network.Nodes);
            network.SingleChunk = network.Bounds.size.x == 1 && network.Bounds.size.y == 1;
        }
    }

    private static int CompareNodes(MechanicalNode a, MechanicalNode b) => a.Id.CompareTo(b.Id);
    private static void Link(MechanicalNode from, MechanicalNode to, int direction, MechanicalNetwork network, Queue<MechanicalNode> queue)
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
    private static void RegisterLink(MechanicalNode from, MechanicalNode to, int direction)
    {
        foreach (MechanicalLink link in from.Links)
            if (ReferenceEquals(link.Target, to) && link.Direction == direction) return;
        from.Links.Add(new MechanicalLink(to, direction));
        to.Links.Add(new MechanicalLink(from, (direction + 2) % 4));
    }
    #endregion

    #region 区块范围与整网滞回
    private BoundsInt CalculateBounds(List<MechanicalNode> nodes)
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
        => AxisContains(playerChunk.x, network.Bounds.xMin, network.Bounds.size.x, expansion, chunkPeriod.x) &&
           AxisContains(playerChunk.y, network.Bounds.yMin, network.Bounds.size.y, expansion, chunkPeriod.y);

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
    /// <summary>同速扭矩源合并供给；输入端不转送扭矩，扭矩源转速冲突、闭环倍率冲突或过载时整网停转。</summary>
    public static void Solve(MechanicalNetwork network, Func<MechanicalNode, float> sourceFactor, float referenceRpm)
    {
        network.TorqueSupply = 0; network.TorqueDemand = 0;
        network.RatioConflict = false; network.OutputConflict = false;
        MechanicalNode root = null;
        foreach (var node in network.Nodes)
        {
            node.Rpm = 0;
            node.SpeedRatio = 1f; node.TorqueRatio = 1f;
            node.EntryDirection = -1; node.GearboxCrossings = 0; node.FlowVisited = false;
            node.SourceFactor = node.Definition.Torque > 0 ? sourceFactor(node) : 0;
            if (root == null && node.SourceFactor > 0 && node.Definition.Torque > 0) root = node;
        }
        if (root == null) { network.Status = "无扭矩"; return; }
        PropagateTransmission(network, root, root);
        // 输入终点不转送转速；另一侧若有同速扭矩源，仍从自己的输出侧传播。
        foreach (var node in network.Nodes)
        {
            if (node.SourceFactor <= 0 || node.Definition.Torque <= 0 || node.FlowVisited) continue;
            if (!SameSpeed(node.Definition.Rpm, root.Definition.Rpm))
            { network.OutputConflict = true; continue; }
            PropagateTransmission(network, root, node);
        }
        foreach (var node in network.Nodes)
            if (node.SourceFactor > 0 && node.Definition.Torque > 0 && !node.FlowVisited)
                network.OutputConflict = true;
        if (network.OutputConflict) { network.Status = "机械卡死"; return; }
        if (network.RatioConflict) { network.Status = "传动比冲突"; return; }
        float rootRpm = root.Definition.Rpm;
        float torqueCapacity = float.PositiveInfinity;
        foreach (var node in network.Nodes)
        {
            if (!node.FlowVisited) continue; // 用力器输入端以外的断开支路不吃动力。
            torqueCapacity = Mathf.Min(torqueCapacity, node.Definition.TorqueCapacity / node.TorqueRatio);
            if (node.SourceFactor > 0 && node.Definition.Torque > 0)
                network.TorqueSupply += node.Definition.Torque * node.SourceFactor / node.TorqueRatio;
        }
        foreach (var node in network.Nodes)
        {
            if (!node.FlowVisited) continue;
            float localTorqueLoad = node.Definition.TorqueLoad * node.SpeedRatio * rootRpm / referenceRpm;
            network.TorqueDemand += localTorqueLoad / node.TorqueRatio;
        }
        if (network.TorqueDemand > network.TorqueSupply + .001f || network.TorqueDemand > torqueCapacity + .001f)
        { network.Status = "过载"; return; }
        network.Status = "运行中";
        foreach (var node in network.Nodes)
            if (node.FlowVisited) node.Rpm = node.SpeedRatio * rootRpm;
    }

    /// <summary>传动箱从输入侧到另一侧才换算倍率；多个方向到同一节点时核对速度与扭矩，防止闭环无端增益。</summary>
    private static void PropagateTransmission(MechanicalNetwork network, MechanicalNode referenceSource, MechanicalNode start)
    {
        Queue<MechanicalNode> queue = network.FlowQueue;
        queue.Clear();
        start.FlowVisited = true;
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            MechanicalNode from = queue.Dequeue();
            foreach (MechanicalLink link in from.Links)
            {
                string fromPort = from.GetPortMode(link.Direction);
                if (fromPort == "input" || fromPort == "closed") continue;
                MechanicalNode to = link.Target;
                int entry = (link.Direction + 2) % 4;
                float speed = from.SpeedRatio;
                float torque = from.TorqueRatio;
                int crossings = from.GearboxCrossings;
                if (from.Definition.Kind == "gearbox" && from.EntryDirection >= 0 && link.Direction != from.EntryDirection)
                {
                    from.Definition.GetTransmission(from.EntryDirection < 2, from.RatioIndex,
                        out float speedStep, out float torqueStep);
                    speed *= speedStep; torque *= torqueStep; crossings++;
                }
                if (!MechanicalDefinition.Positive(speed) || !MechanicalDefinition.Positive(torque) ||
                    speed > 10000f || speed < .0001f || torque > 10000f || torque < .0001f)
                { network.RatioConflict = true; continue; }
                if (to.GetPortMode(entry) == "output" && to.Definition.Torque <= 0)
                { network.OutputConflict = true; continue; }
                // 扭矩源既能受同网带动，也能叠加扭矩；必须与根源及到达自身的实际转速一致。
                if (to.SourceFactor > 0 && to.Definition.Torque > 0 &&
                    (!SameSpeed(to.Definition.Rpm, referenceSource.Definition.Rpm) ||
                     !SameSpeed(to.Definition.Rpm, referenceSource.Definition.Rpm * speed)))
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
    #endregion
}
