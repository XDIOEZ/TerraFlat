using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using MemoryPack;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>由 ItemMgr 调度的机械世界。权威模拟独立于地图表现；只有拓扑变化重构图，整网先恢复再 Tick，休眠时保存库存并释放加工器。</summary>
public static class MechanicalWorld
{
    /// <summary>区块显示格与规范化机械节点的配对，循环接缝处两套坐标不能混用。</summary>
    public readonly struct MechanicalRenderCell
    {
        public readonly MechanicalNode Node;
        public readonly Vector2Int DisplayCell;
        public MechanicalRenderCell(MechanicalNode node, Vector2Int displayCell)
        { Node = node; DisplayCell = displayCell; }
    }

    #region 会话与扩展
    private static readonly Dictionary<int, MechanicalNode> nodes = new();
    private static readonly Dictionary<int, MechanicalInteractionTarget> interactions = new();
    private static readonly Dictionary<MechanicalProcessor, MechanicalNode> processorOwners = new();
    private static readonly Dictionary<string, Func<MechanicalNode, float>> sourceProviders = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Func<MechanicalNode, float>> sourceRpmProviders = new(StringComparer.Ordinal);
    private static readonly List<Vector2Int> players = new();
    private static MechanicalNetworkGraph graph;
    private static GameSaveData owner;
    private static string worldKey;
    private static bool dirty;
    private static bool suppressRemoval;
    private static float elapsed;
    public static IReadOnlyList<MechanicalNetwork> Networks => graph?.Networks;
    /// <summary>机械数据格变化时通知区块表现与导航，不依赖世界物品实例。</summary>
    public static event Action<Vector2Int> CellChanged;
    public static event Action<MechanicalNode> NodeStateChanged; // 联机只在数据变化时采集节点快照。
    public static event Action<int> NodeRemoved; // 权威节点删除通知。
    public static event Action<MechanicalNode> VisualSpeedChanged; // 只传输转速，不传重复库存快照。

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        nodes.Clear(); interactions.Clear(); processorOwners.Clear(); sourceProviders.Clear(); sourceRpmProviders.Clear(); players.Clear();
        graph = null; owner = null; worldKey = null; dirty = false; suppressRemoval = false; elapsed = 0;
        CellChanged = null;
        NodeStateChanged = null; NodeRemoved = null; VisualSpeedChanged = null;
    }

    /// <summary>MOD 扭矩条件返回 0..1 可用比例，条件必须独立于 GameObject 与 ChunkView。</summary>
    public static void RegisterSource(string id, Func<MechanicalNode, float> provider)
    {
        if (string.IsNullOrWhiteSpace(id) || provider == null) throw new ArgumentException("扭矩来源注册无效。");
        sourceProviders[id] = provider;
    }
    /// <summary>MOD 为动态动力源注册实际 RPM；不注册时使用定义固定转速。</summary>
    public static void RegisterSourceRpm(string id, Func<MechanicalNode, float> provider)
    {
        if (string.IsNullOrWhiteSpace(id) || provider == null) throw new ArgumentException("动力源转速注册无效。");
        sourceRpmProviders[id] = provider;
    }
    public static void ClearSourceProviders() { sourceProviders.Clear(); sourceRpmProviders.Clear(); }

    private static void EnsureScope()
    {
        var save = SaveDataMgr.Instance?.SaveData;
        string key = SceneManager.GetActiveScene().name;
        if (ReferenceEquals(owner, save) && worldKey == key && graph != null) return;
        if (ReferenceEquals(owner, save)) CaptureCurrentWorld();
        ReleaseRuntime();
        owner = save; worldKey = key;
        MechanicalCatalog.EnsureLoaded();
        Vector2 size = ChunkMgr.ExistingInstance != null ? ChunkMgr.GetChunkSize() : new Vector2(16, 16);
        var chunkSize = new Vector2Int(Mathf.Max(1, Mathf.RoundToInt(size.x)), Mathf.Max(1, Mathf.RoundToInt(size.y)));
        Vector2Int period = Vector2Int.zero;
        if (WorldTopologyRuntime.TryGetActiveBounds(out var bounds))
            period = new Vector2Int(bounds.Span.x / chunkSize.x, bounds.Span.y / chunkSize.y);
        graph = new MechanicalNetworkGraph(chunkSize, period, WorldTopologyRuntime.NormalizeCell);
        GameplayCombatBridge.Register(MechanicalCombatBridge.Instance);
        if (save?.Mechanical?.Worlds != null && save.Mechanical.Worlds.TryGetValue(key, out var snapshots))
            foreach (var data in snapshots) RestoreDescriptor(data);
        dirty = true;
    }

    private static void RestoreDescriptor(ItemData snapshot)
    {
        if (snapshot == null || !TryGetModuleData(snapshot, out var data)) return;
        var definition = MechanicalCatalog.Get(snapshot.IDName);
        // 缺失 MOD 内容不丢弃存档，恢复目录后仍能重新载入。
        if (definition == null) return;
        var state = data.GetData<MechanicalNodeState>() ?? new MechanicalNodeState();
        if (state.Hp < 0f) state.Hp = ResolveMaximumHp(definition.Id);
        nodes.Add(snapshot.Guid, new MechanicalNode
        {
            Id = snapshot.Guid, Cell = CellOf(snapshot.transform.position), Definition = definition, Snapshot = snapshot,
            RotationQuarterTurns = state.RotationQuarterTurns, Engaged = state.Engaged, RatioIndex = state.RatioIndex
        });
    }

    /// <summary>仅正式安装提交和世界存档恢复调用；手持召唤器不会登记到网络。</summary>
    public static MechanicalNode Attach(Mod_MechanicalNode view)
    {
        if (!GameNetwork.HasStateAuthority) return null;
        EnsureScope();
        int id = view.item.itemData.Guid;
        if (!nodes.TryGetValue(id, out var node))
        {
            node = new MechanicalNode { Id = id, Cell = CellOf(view.item.transform.position), Definition = view.Definition,
                Snapshot = view.item.itemData, State = view.LocalState };
            nodes.Add(id, node); dirty = true;
            CopyTopology(node);
            EnsureProcessor(node);
        }
        else if (node.State == null) WakeNode(node);
        node.View = view;
        return node;
    }

    /// <summary>把已校验的机械建筑快照直接提交为世界节点，落地过程不创建 Item 或 GameObject。</summary>
    public static MechanicalNode Place(ItemData snapshot)
    {
        if (!GameNetwork.HasStateAuthority || snapshot == null || !OwnsWorldItem(snapshot))
            throw new InvalidOperationException("机械建筑数据无效或当前实例无世界写入权限。");
        EnsureScope();
        MechanicalDefinition definition = MechanicalCatalog.Get(snapshot.IDName)
            ?? throw new InvalidOperationException("机械定义不存在：" + snapshot.IDName);
        Vector2Int cell = CellOf(snapshot.transform.position);
        if (!ValidatePlacement(definition, cell, false, out string reason))
            throw new InvalidOperationException(reason);
        if (nodes.ContainsKey(snapshot.Guid))
            throw new InvalidOperationException("机械节点 GUID 重复：" + snapshot.Guid);
        if (!TryGetModuleData(snapshot, out Ex_ModData_MemoryPackable module))
            throw new InvalidOperationException("机械建筑缺少状态模块：" + snapshot.IDName);

        MechanicalNodeState state = module.GetData<MechanicalNodeState>() ?? new MechanicalNodeState();
        if (state.Hp < 0f) state.Hp = ResolveMaximumHp(definition.Id);
        var node = new MechanicalNode
        {
            Id = snapshot.Guid,
            Cell = cell,
            Definition = definition,
            Snapshot = FastCloner.FastCloner.DeepClone(snapshot),
            State = state
        };
        CopyTopology(node);
        EnsureProcessor(node);
        nodes.Add(node.Id, node);
        dirty = true;
        BuildingOccupancyRegistry.NotifyMechanicalChanged(cell);
        CellChanged?.Invoke(cell);
        NodeStateChanged?.Invoke(node);
        return node;
    }

    /// <summary>按稳定身份移除机械数据节点，并在释放加工器前捕获完整状态。</summary>
    public static ItemData Remove(int id)
    {
        EnsureScope();
        if (!GameNetwork.HasStateAuthority || !nodes.TryGetValue(id, out MechanicalNode node))
            return null;
        CaptureNode(node);
        ItemData snapshot = FastCloner.FastCloner.DeepClone(node.Snapshot);
        DisposeProcessor(node);
        if (interactions.Remove(id, out MechanicalInteractionTarget interaction)) interaction.Dispose();
        nodes.Remove(id);
        dirty = true;
        BuildingOccupancyRegistry.NotifyMechanicalChanged(node.Cell);
        CellChanged?.Invoke(node.Cell);
        NodeRemoved?.Invoke(id);
        return snapshot;
    }

    /// <summary>拆除事务先捕获纯数据节点的加工库存和朝向，不改变世界占地。</summary>
    public static ItemData CaptureSnapshot(MechanicalNode node)
    {
        EnsureScope();
        if (node == null || !nodes.TryGetValue(node.Id, out MechanicalNode current) ||
            !ReferenceEquals(node, current)) return null;
        CaptureNode(node);
        return FastCloner.FastCloner.DeepClone(node.Snapshot);
    }

    /// <summary>初次加入联机世界时枚举全部权威节点。</summary>
    public static void CollectNodes(List<MechanicalNode> result)
    {
        EnsureScope();
        result.Clear();
        result.AddRange(nodes.Values);
    }

    /// <summary>客户端应用服务端权威快照，不参与机械模拟。</summary>
    public static void ApplyRemoteReset()
    {
        EnsureScope();
        var changedCells = new List<Vector2Int>(nodes.Count);
        foreach (MechanicalNode node in nodes.Values) changedCells.Add(node.Cell);
        foreach (MechanicalInteractionTarget interaction in interactions.Values) interaction.Dispose();
        interactions.Clear();
        foreach (MechanicalNode node in nodes.Values) DisposeProcessor(node);
        nodes.Clear();
        dirty = true;
        foreach (Vector2Int cell in changedCells)
        {
            BuildingOccupancyRegistry.NotifyMechanicalChanged(cell);
            CellChanged?.Invoke(cell);
        }
    }

    /// <summary>客户端应用服务端权威快照，不参与机械模拟。</summary>
    public static void ApplyRemoteSnapshot(ItemData snapshot, float rpm, int entryDirection)
    {
        if (snapshot == null || !OwnsWorldItem(snapshot))
            throw new InvalidOperationException("联机机械快照无效");
        EnsureScope();
        MechanicalDefinition definition = MechanicalCatalog.Get(snapshot.IDName) ??
            throw new InvalidOperationException("联机机械定义缺失：" + snapshot.IDName);
        Vector2Int previousCell = default;
        bool moved = false;
        if (!nodes.TryGetValue(snapshot.Guid, out MechanicalNode node))
        {
            node = new MechanicalNode { Id = snapshot.Guid };
            nodes.Add(node.Id, node);
        }
        else
        {
            previousCell = node.Cell;
            moved = previousCell != CellOf(snapshot.transform.position);
        }
        node.Cell = CellOf(snapshot.transform.position);
        node.Definition = definition;
        node.Snapshot = FastCloner.FastCloner.DeepClone(snapshot);
        if (!TryGetModuleData(node.Snapshot, out Ex_ModData_MemoryPackable module))
            throw new InvalidOperationException("联机机械节点模块缺失");
        node.State = module.GetData<MechanicalNodeState>() ?? new MechanicalNodeState();
        CopyTopology(node);
        node.Rpm = rpm;
        node.EntryDirection = entryDirection;
        dirty = true;
        UpdateVisualSpeed(node);
        if (moved)
        {
            BuildingOccupancyRegistry.NotifyMechanicalChanged(previousCell);
            CellChanged?.Invoke(previousCell);
        }
        BuildingOccupancyRegistry.NotifyMechanicalChanged(node.Cell);
        CellChanged?.Invoke(node.Cell);
    }

    /// <summary>客户端以数据身份删除机械节点。</summary>
    public static void ApplyRemoteRemoval(int id)
    {
        EnsureScope();
        if (!nodes.TryGetValue(id, out MechanicalNode node)) return;
        if (interactions.Remove(id, out MechanicalInteractionTarget interaction)) interaction.Dispose();
        DisposeProcessor(node);
        nodes.Remove(id);
        dirty = true;
        BuildingOccupancyRegistry.NotifyMechanicalChanged(node.Cell);
        CellChanged?.Invoke(node.Cell);
    }

    /// <summary>客户端更新 GPU 动画速度，不重传加工库存。</summary>
    public static void ApplyRemoteSpeed(int id, float rpm, int entryDirection)
    {
        EnsureScope();
        if (!nodes.TryGetValue(id, out MechanicalNode node)) return;
        node.Rpm = rpm;
        node.EntryDirection = entryDirection;
        UpdateVisualSpeed(node);
    }

    /// <summary>战斗和 MOD 修改机械状态后的显式增量通知。</summary>
    public static void StateChanged(MechanicalNode node)
    {
        if (Contains(node)) NodeStateChanged?.Invoke(node);
    }

    /// <summary>直接按世界格和占地层查询机械数据节点。</summary>
    public static MechanicalNode GetAt(Vector2Int cell, int layer)
    {
        EnsureScope();
        if (dirty) { graph.Rebuild(nodes.Values); dirty = false; }
        return graph.At(cell, layer);
    }

    /// <summary>联机增量按节点身份查找当前世界的机械数据。</summary>
    public static MechanicalNode GetById(int id)
    {
        EnsureScope();
        return nodes.TryGetValue(id, out MechanicalNode node) ? node : null;
    }

    /// <summary>导航等高频查询只读取当前已建立的机械世界，不触发场景或存档作用域切换。</summary>
    public static MechanicalNode GetAtCurrentWorld(Vector2Int cell, int layer)
    {
        if (graph == null) return null;
        if (dirty) { graph.Rebuild(nodes.Values); dirty = false; }
        return graph.At(cell, layer);
    }

    /// <summary>交互半径内按数据格索引机械目标，查询量与玩家附近格数相关。</summary>
    public static void QueryInteractionTargets(Item actor, float radius, Vector2? pointer, List<IInteractable> result)
    {
        if (actor == null || result == null) return;
        EnsureScope();
        if (actor.gameObject.scene.name != worldKey) return;
        if (dirty) { graph.Rebuild(nodes.Values); dirty = false; }
        Vector3 position = actor.transform.position;
        int extent = Mathf.CeilToInt(radius) + 1;
        Vector2Int center = CellOf(position);
        for (int dy = -extent; dy <= extent; dy++)
        for (int dx = -extent; dx <= extent; dx++)
        {
            Vector2Int cell = WorldTopologyRuntime.NormalizeCell(center + new Vector2Int(dx, dy));
            for (int layer = 0; layer < 2; layer++)
            {
                MechanicalNode node = graph.At(cell, layer);
                if (node == null || WorldTopologyRuntime.Distance(position, node.Snapshot.transform.position) > radius ||
                    pointer.HasValue && WorldTopologyRuntime.Distance(pointer.Value, node.Snapshot.transform.position) > .65f)
                    continue;
                if (!interactions.TryGetValue(node.Id, out MechanicalInteractionTarget target))
                {
                    target = new MechanicalInteractionTarget(node);
                    interactions.Add(node.Id, target);
                }
                if (!result.Contains(target)) result.Add(target);
            }
        }
    }

    /// <summary>数据交互目标检查节点是否仍属于当前世界。</summary>
    public static bool Contains(MechanicalNode node)
        => node != null && nodes.TryGetValue(node.Id, out MechanicalNode current) &&
           ReferenceEquals(node, current);

    /// <summary>休眠节点由交互唤醒加工器，随后正常交给网络 Tick 管理。</summary>
    public static void WakeForInteraction(MechanicalNode node)
    {
        if (!Contains(node)) return;
        if (node.State == null) WakeNode(node);
    }

    /// <summary>建造耐久来自可替换的物品定义，与运行时碰撞体无关。</summary>
    public static float ResolveMaximumHp(string itemId)
    {
        if (GameRes.ExistingInstance == null ||
            !GameRes.ExistingInstance.TryGetItemDefinition(itemId, out RuntimeItemDefinition definition) ||
            definition.Health?.HasHp != true || definition.Health.Hp <= 0f)
            throw new InvalidOperationException("机械耐久定义缺失：" + itemId);
        return definition.Health.Hp;
    }

    /// <summary>按区块边界枚举节点，供 BRG 表现绑定使用。</summary>
    public static void CollectInBounds(BoundsInt bounds, List<MechanicalRenderCell> result)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));
        EnsureScope();
        result.Clear();
        if (dirty) { graph.Rebuild(nodes.Values); dirty = false; }
        for (int y = bounds.yMin; y < bounds.yMax; y++)
        for (int x = bounds.xMin; x < bounds.xMax; x++)
        for (int layer = 0; layer < 2; layer++)
        {
            MechanicalNode node = graph.At(new Vector2Int(x, y), layer);
            if (node != null) result.Add(new MechanicalRenderCell(node, new Vector2Int(x, y)));
        }
    }

    public static void TopologyChanged(MechanicalNode node)
    {
        if (node == null) return;
        CopyTopology(node); dirty = true;
        CellChanged?.Invoke(node.Cell);
        StateChanged(node);
    }
    private static void CopyTopology(MechanicalNode node)
    { node.RotationQuarterTurns = node.State.RotationQuarterTurns; node.Engaged = node.State.Engaged; node.RatioIndex = node.State.RatioIndex; }

    /// <summary>真实销毁/拆除删除拓扑；表现回收和退出世界由独立抑制范围保留权威记录。</summary>
    public static void BeforeDespawn(Item item)
    {
        if (suppressRemoval || item?.itemData == null || !nodes.TryGetValue(item.itemData.Guid, out var node)) return;
        if (node.View == null || node.View.item != item) return;
        node.View = null;
        node.Processor?.Dispose();
        nodes.Remove(node.Id); dirty = true;
    }
    public static void Detach(Mod_MechanicalNode view)
    {
        if (view.Node != null && view.Node.View == view) view.Node.View = null;
    }
    #endregion

    #region 调度与休眠
    public static void Tick(float deltaTime)
    {
        if (!GameNetwork.HasStateAuthority) return;
        EnsureScope();
        elapsed += Mathf.Max(0, deltaTime);
        if (elapsed < MechanicalCatalog.Settings.TickSeconds) return;
        // 不补算长期停顿，远离与恢复都不执行离线生产。
        float step = Mathf.Min(elapsed, MechanicalCatalog.Settings.TickSeconds * 2);
        elapsed = 0;
        if (dirty) { graph.Rebuild(nodes.Values); dirty = false; }
        CollectPlayerChunks();
        foreach (var network in graph.Networks)
        {
            bool active = graph.ShouldBeActive(network, players, step, MechanicalCatalog.Settings);
            if (!active)
            {
                if (network.Active) SleepNetwork(network);
                continue;
            }
            // 拓扑合并可能包含冷节点，全部恢复成功后才运行任何一个节点。
            foreach (var node in network.Nodes) if (node.State == null) WakeNode(node);
            network.Active = true;
            MechanicalNetworkGraph.Solve(network, GetSourceFactor, GetSourceRpm, MechanicalCatalog.Settings.ReferenceRpm);
            foreach (var node in network.Nodes)
            {
                UpdateVisualSpeed(node);
                SimulateNode(node, step);
            }
        }
    }

    private static void CollectPlayerChunks()
    {
        players.Clear();
        if (ItemMgr.Instance == null) return;
        foreach (var player in ItemMgr.Instance.Player_DIC.Values)
            if (player != null && player.gameObject.scene.name == worldKey)
                players.Add(graph.ChunkOf(CellOf(player.transform.position)));
    }

    private static void WakeNode(MechanicalNode node)
    {
        if (!TryGetModuleData(node.Snapshot, out var module)) throw new InvalidOperationException("机械快照缺少节点模块。");
        node.State = module.GetData<MechanicalNodeState>() ?? new MechanicalNodeState();
        if (node.State.Hp < 0f) node.State.Hp = ResolveMaximumHp(node.Definition.Id);
        EnsureProcessor(node);
    }
    private static void EnsureProcessor(MechanicalNode node)
    {
        if (node.Processor == null && !string.IsNullOrWhiteSpace(node.Definition.Station))
        {
            node.Processor = new MechanicalProcessor(node.Definition.Station, node.State.Processing);
            node.Processor.StateChanged += OnProcessorChanged;
            processorOwners[node.Processor] = node;
        }
    }

    /// <summary>加工库存与进度变化进入可节流的节点快照队列。</summary>
    private static void OnProcessorChanged(MechanicalProcessor processor)
    {
        if (processorOwners.TryGetValue(processor, out MechanicalNode node)) StateChanged(node);
    }

    /// <summary>释放加工器时解除节点映射，避免唤醒后旧库存回调污染新状态。</summary>
    private static void DisposeProcessor(MechanicalNode node)
    {
        if (node.Processor == null) return;
        node.Processor.StateChanged -= OnProcessorChanged;
        processorOwners.Remove(node.Processor);
        node.Processor.Dispose();
        node.Processor = null;
    }
    private static void SleepNetwork(MechanicalNetwork network)
    {
        // 先保存全部节点，成功后统一释放。中途失败不形成部分运行网络。
        foreach (var node in network.Nodes) CaptureNode(node);
        foreach (var node in network.Nodes)
        {
            RemoveView(node);
            DisposeProcessor(node); node.State = null; node.Rpm = 0;
            UpdateVisualSpeed(node);
        }
        network.Active = false; network.Status = "休眠";
    }

    private static void SimulateNode(MechanicalNode node, float step)
    {
        if (node.State == null) return;
        if (node.Definition.Source == "manual")
            node.State.ManualSeconds = Mathf.Max(0, node.State.ManualSeconds - step);
        if (node.Rpm > 0)
            node.Processor?.Advance(step * GetWorkEfficiency(node));
        if (interactions.TryGetValue(node.Id, out MechanicalInteractionTarget interaction))
            interaction.RefreshPanel();
    }

    /// <summary>用力器效率严格按需求转速线性计算；允许超过 100%，不得截断或重复除以基准转速。</summary>
    public static float GetWorkEfficiency(MechanicalNode node)
    {
        if (node?.Definition == null || node.Rpm <= 0) return 0;
        return node.Rpm / node.Definition.RequiredRpm;
    }

    private static float GetSourceFactor(MechanicalNode node)
    {
        if (node.State == null || node.Definition.Torque <= 0) return 0;
        string source = node.Definition.Source;
        if (sourceProviders.TryGetValue(source, out var provider)) return Mathf.Clamp01(provider(node));
        if (source == "manual") return node.State.ManualSeconds > 0 ? 1 : 0;
        if (source == "wind") return WeatherMgr.Instance != null ? WeatherMgr.Instance.GetCurrentWindStrength() : 0;
        if (source == "water") return GetWaterSourceFactor(node);
        return 0;
    }

    /// <summary>固定转速沿用机械定义；水车和 MOD 动力源可按实际环境速度提供 RPM。</summary>
    private static float GetSourceRpm(MechanicalNode node)
    {
        if (node?.Definition == null) return 0f;
        string source = node.Definition.Source;
        if (sourceRpmProviders.TryGetValue(source, out var provider)) return provider(node);
        if (source == "water") return GetWaterSourceRpm(node);
        return node.Definition.Rpm;
    }

    /// <summary>水车扭矩随自身水格的实际表层流速变化；湖泊和无流河段不供能。</summary>
    private static float GetWaterSourceFactor(MechanicalNode node)
    {
        if (!TryGetWaterCurrentSpeed(node, out float speed)) return 0f;
        return Mathf.Clamp01(speed / WorldItemWaterRules.RiverDriftSpeed);
    }

    /// <summary>水车每分钟转数由线速度除以叶轮周长换算，轮半径来自机械定义。</summary>
    private static float GetWaterSourceRpm(MechanicalNode node)
    {
        if (!TryGetWaterCurrentSpeed(node, out float speed)) return 0f;
        float circumference = 2f * Mathf.PI * node.Definition.SourceRadius;
        return speed / circumference * 60f;
    }

    /// <summary>读取水车占用水格的当前流速，不从岸边或其它邻格借用水流。</summary>
    private static bool TryGetWaterCurrentSpeed(MechanicalNode node, out float speed)
    {
        speed = 0f;
        ChunkMgr manager = ChunkMgr.ExistingInstance;
        if (manager == null || node == null || !IsWaterCell(node.Cell)) return false;
        Vector2 samplePosition = new(node.Cell.x + .5f, node.Cell.y + .5f);
        if (!manager.TryGetRuntimeWaterCurrent(samplePosition, out RuntimeWaterCurrentSample current)) return false;
        speed = WorldItemWaterRules.ResolveDriftSpeed(current.Kind, current.Flow);
        return speed > 0f;
    }
    #endregion

    #region 表现与占地
    /// <summary>仅在解算转速变化时更新相位锚点，连续动画由 BRG Shader 在 GPU 上执行。</summary>
    private static void UpdateVisualSpeed(MechanicalNode node)
    {
        float now = Time.time;
        bool changed = false;
        if (Mathf.Abs(node.VisualRpm - node.Rpm) >= .01f)
        {
            node.VisualPhase = Mathf.Repeat(node.VisualPhase +
                (now - node.VisualTime) * node.VisualRpm * Mathf.PI * 2f / 60f, Mathf.PI * 2f);
            node.VisualTime = now;
            node.VisualRpm = node.Rpm;
            changed = true;
        }
        if (node.Definition.Kind == "gearbox")
        {
            int input = (node.EntryDirection - node.RotationQuarterTurns + 4) & 3;
            float large = input == 2 ? 1f : input == 0 ? -.5f : 0f;
            float small = input == 2 ? -2f : input == 0 ? 1f : 0f;
            float radians = node.Rpm * Mathf.PI * 2f / 60f;
            changed |= UpdateGearboxTrack(ref node.GearboxLargePhase, ref node.GearboxLargeSpeed,
                ref node.GearboxLargeTime, radians * large, now);
            changed |= UpdateGearboxTrack(ref node.GearboxSmallPhase, ref node.GearboxSmallSpeed,
                ref node.GearboxSmallTime, radians * small, now);
        }
        if (!changed) return;
        CellChanged?.Invoke(node.Cell);
        VisualSpeedChanged?.Invoke(node);
    }

    /// <summary>输入方向或传动比改变时先积分旧角速度，使箱内齿轮不断帧跳相。</summary>
    private static bool UpdateGearboxTrack(ref float phase, ref float speed,
        ref float sampleTime, float nextSpeed, float now)
    {
        if (Mathf.Abs(speed - nextSpeed) < .001f) return false;
        phase = Mathf.Repeat(phase + (now - sampleTime) * speed, Mathf.PI * 2f);
        sampleTime = now;
        speed = nextSpeed;
        return true;
    }

    private static void RemoveView(MechanicalNode node)
    {
        if (node.View == null) return;
        Item item = node.View.item;
        node.View = null;
        suppressRemoval = true;
        try { if (item != null) ItemMgr.Instance.DespawnItem(item, false); }
        finally { suppressRemoval = false; }
    }

    public static Vector2Int CellOf(Vector3 position) => WorldTopologyRuntime.NormalizeCell(new Vector2Int(Mathf.FloorToInt(position.x), Mathf.FloorToInt(position.y)));
    public static bool IsOccupied(Vector2Int cell, int layer, int except = 0)
    {
        if (graph == null) return false;
        if (dirty) { graph.Rebuild(nodes.Values); dirty = false; }
        var node = graph.At(cell, layer);
        return node != null && node.Id != except;
    }

    public static bool ValidatePlacement(MechanicalDefinition definition, Vector2Int cell, bool vertical, out string reason)
    {
        EnsureScope();
        reason = null;
        if (IsOccupied(cell, definition.Layer)) { reason = "当前机械层已占用"; return false; }
        if (BuildingOccupancyRegistry.IsOccupied(cell, layer: definition.Layer))
        { reason = "当前建筑层已占用"; return false; }
        if (definition.Kind == "bridge")
        {
            if (graph.At(cell, 0) == null)
            { reason = "跨轴器需要放置在下层机械线路上方"; return false; }
        }
        if (definition.Source == "water" && !IsWaterCell(cell))
        { reason = "水车只能放在水格中"; return false; }
        return true;
    }

    /// <summary>水车占用格必须本身属于带水体行为的液面，邻接岸边不满足放置条件。</summary>
    public static bool IsWaterCell(Vector2Int cell)
    {
        ChunkMgr manager = ChunkMgr.ExistingInstance;
        GameRes resources = GameRes.Instance;
        Vector2 samplePosition = new(cell.x + .5f, cell.y + .5f);
        return manager != null && resources != null &&
               manager.TryGetRuntimeTerrainTile(samplePosition, out RuntimeTerrainTileSample sample) &&
               sample.LiquidDepth > 0f &&
               resources.TryGetLiquidDefinition(sample.LiquidId, out LiquidDefinition liquid) &&
               liquid.WorldWater != null;
    }

    /// <summary>机械风箱只增强出风口正对的相邻炉体；60 RPM 对应满增温，多个风箱取最高倍率。</summary>
    public static float GetBellowsBoost(Vector3 position)
    {
        if (graph == null) return 0;
        if (dirty) { graph.Rebuild(nodes.Values); dirty = false; }
        Vector2Int cell = CellOf(position);
        float boost = 0;
        foreach (var offset in MechanicalNetworkGraph.Directions)
        {
            var node = graph.At(WorldTopologyRuntime.NormalizeCell(cell + offset), 0);
            if (node?.Definition.Kind != "bellows" || node.Network?.Active != true)
                continue;

            Vector2Int outlet = WorldTopologyRuntime.NormalizeCell(
                node.Cell + MechanicalNetworkGraph.Directions[node.RotationQuarterTurns & 3]);
            if (outlet != cell)
                continue;

            boost = Mathf.Max(boost, Mathf.Clamp01(GetWorkEfficiency(node)));
        }
        return boost;
    }
    #endregion

    #region 独立存档
    public static bool TryGetModuleData(ItemData data, out Ex_ModData_MemoryPackable module)
    {
        module = null;
        if (data?.ModuleDataDic == null) return false;
        foreach (var entry in data.ModuleDataDic.Values)
            if (entry?.ID == Mod_MechanicalNode.ModuleId) { module = entry as Ex_ModData_MemoryPackable; return module != null; }
        return false;
    }
    public static bool OwnsWorldItem(ItemData data)
        => TryGetModuleData(data, out _) && Mod_Building.TryReadBuildingData(data, out _, out var building) && building.Role == BuildingRole.PlacedBuilding;

    private static void CaptureNode(MechanicalNode node)
    {
        if (node.View != null)
        {
            node.View.item.Save();
            node.Snapshot = FastCloner.FastCloner.DeepClone(node.View.item.itemData);
        }
        if (node.State != null && TryGetModuleData(node.Snapshot, out var module)) module.WriteData(node.State);
    }
    private static void CaptureCurrentWorld()
    {
        if (owner == null || graph == null || string.IsNullOrEmpty(worldKey)) return;
        owner.Mechanical ??= new MechanicalArchive();
        var saved = new List<ItemData>();
        if (owner.Mechanical.Worlds.TryGetValue(worldKey, out var previous))
            foreach (var data in previous)
                if (MechanicalCatalog.Get(data.IDName) == null) saved.Add(data);
        foreach (var node in nodes.Values) { CaptureNode(node); saved.Add(FastCloner.FastCloner.DeepClone(node.Snapshot)); }
        saved.Sort(CompareSnapshots);
        owner.Mechanical.Worlds[worldKey] = saved;
    }
    private static int CompareSnapshots(ItemData a, ItemData b) => a.Guid.CompareTo(b.Guid);
    public static byte[] CaptureArchive(GameSaveData save)
    {
        if (ReferenceEquals(save, owner)) CaptureCurrentWorld();
        return MemoryPackSerializer.Serialize(save.Mechanical ?? new MechanicalArchive());
    }
    public static void RestoreArchive(GameSaveData save, byte[] bytes)
    {
        var archive = bytes == null || bytes.Length == 0 ? new MechanicalArchive() : MemoryPackSerializer.Deserialize<MechanicalArchive>(bytes);
        if (archive == null || archive.Version != 1 || archive.Worlds == null) throw new InvalidOperationException("不支持的机械存档版本。");
        save.Mechanical = archive;
    }
    public static void ReleaseWorld(bool capture)
    {
        if (capture) CaptureCurrentWorld();
        ReleaseRuntime(); owner = null; worldKey = null; graph = null;
    }
    private static void ReleaseRuntime()
    {
        GameplayCombatBridge.Unregister(MechanicalCombatBridge.Instance);
        foreach (MechanicalInteractionTarget interaction in interactions.Values) interaction.Dispose();
        interactions.Clear();
        foreach (var node in nodes.Values) DisposeProcessor(node);
        processorOwners.Clear();
        nodes.Clear(); elapsed = 0;
    }
    #endregion
}
