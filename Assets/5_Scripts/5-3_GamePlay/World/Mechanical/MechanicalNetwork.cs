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
    public float Rpm;
    public MechanicalNetwork Network;

    public bool HasPort(int direction)
    {
        if (Definition.Kind == "clutch" && !Engaged) return false;
        return Definition.Ports == "all" || (Vertical ? direction % 2 == 1 : direction % 2 == 0);
    }
    /// <summary>齿轮与非齿轮节点只经可见轴接头相连；齿轮之间仍按齿牙端口啮合。</summary>
    public bool CanConnectTo(MechanicalDefinition other, int direction)
    {
        if (other == null || !HasPort(direction)) return false;
        return Definition.Kind != "gear" || other.Kind == "gear" || Definition.HasAxlePort(direction);
    }
    public float PortRatio(int direction)
    {
        if (Definition.Kind != "gearbox" || direction >= 2) return 1f;
        return Definition.Ratios[Mathf.Clamp(RatioIndex, 0, Definition.Ratios.Length - 1)];
    }
    #endregion
}

/// <summary>实际连通网络的运行单位；Bounds 仅在拓扑变化时计算，SingleChunk 不建立跨区块端口表。</summary>
public sealed class MechanicalNetwork
{
    public readonly List<MechanicalNode> Nodes = new();
    public BoundsInt Bounds;
    public bool SingleChunk;
    public bool Active;
    public float AwaySeconds;
    public bool RatioConflict;
    public string Status = "停止";
    public float Supply;
    public float Demand;
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
        }
        var queue = new Queue<MechanicalNode>();
        foreach (var root in sorted)
        {
            if (root.Network != null) continue;
            var network = new MechanicalNetwork();
            Networks.Add(network);
            root.SpeedRatio = 1;
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
        if (to == null || !from.CanConnectTo(to.Definition, direction) ||
            !to.CanConnectTo(from.Definition, (direction + 2) % 4)) return;
        float expected = from.SpeedRatio * from.PortRatio(direction) / to.PortRatio((direction + 2) % 4);
        if (!MechanicalDefinition.Positive(expected) || expected > 10000 || expected < 0.0001f)
        { network.RatioConflict = true; expected = 1; }
        if (to.Network == null)
        {
            to.Network = network;
            to.SpeedRatio = expected;
            queue.Enqueue(to);
        }
        else if (Mathf.Abs(to.SpeedRatio - expected) > Mathf.Max(0.001f, Mathf.Abs(expected) * 0.001f))
            network.RatioConflict = true;
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

    #region 动力求解
    /// <summary>动力源按可用功率供给，变速按端口比传播；过载、超传动容量或闭环速比矛盾时整网停转。</summary>
    public static void Solve(MechanicalNetwork network, Func<MechanicalNode, float> sourceFactor, float referenceRpm)
    {
        network.Supply = 0; network.Demand = 0;
        float rootRpm = float.PositiveInfinity;
        float capacity = float.PositiveInfinity;
        foreach (var node in network.Nodes)
        {
            node.Rpm = 0;
            capacity = Mathf.Min(capacity, node.Definition.Capacity);
            float factor = sourceFactor(node);
            if (factor > 0 && node.Definition.Power > 0)
            {
                network.Supply += node.Definition.Power * factor;
                rootRpm = Mathf.Min(rootRpm, node.Definition.Rpm / node.SpeedRatio);
            }
        }
        if (network.RatioConflict) { network.Status = "传动比冲突"; return; }
        if (network.Supply <= 0 || float.IsInfinity(rootRpm)) { network.Status = "无动力"; return; }
        foreach (var node in network.Nodes)
            network.Demand += node.Definition.Load * node.SpeedRatio * rootRpm / referenceRpm;
        if (network.Demand > network.Supply + .001f || network.Demand > capacity + .001f)
        { network.Status = "过载"; return; }
        network.Status = "运行中";
        foreach (var node in network.Nodes) node.Rpm = node.SpeedRatio * rootRpm;
    }
    #endregion
}
