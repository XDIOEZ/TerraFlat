using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FlatWorld.Networking;
using MemoryPack;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>由 ItemMgr 调度的机械世界。权威模拟独立于地图表现；只有拓扑变化重构图，整网先恢复再 Tick，休眠时保存库存并释放加工器。</summary>
public static partial class MachineWorld
{
    /// <summary>区块显示格与规范化机械节点的配对，循环接缝处两套坐标不能混用。</summary>
    public readonly struct MachineRenderCell
    {
        public readonly MachineEntity Node;
        public readonly Vector2Int DisplayCell;
        public MachineRenderCell(MachineEntity node, Vector2Int displayCell)
        { Node = node; DisplayCell = displayCell; }
    }

    #region 会话与扩展
    private static readonly Dictionary<int, MachineEntity> nodes = new();
    private static readonly Dictionary<int, MachineInteractionTarget> interactions = new();
    private static readonly Dictionary<RecipeProcessor, MachineEntity> processorOwners = new();
    private static readonly Dictionary<string, Func<MachineEntity, float>> sourceProviders = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Func<MachineEntity, float>> sourceRpmProviders = new(StringComparer.Ordinal);
    private static readonly List<Vector2Int> players = new();
    private const float PointerInteractionRadius = .65f; // 机械数据目标的准线命中半径，必须与查询格范围同步。
    private static MechanicalNetworkGraph graph;
    private static GameSaveData owner;
    private static string worldKey;
    private static bool dirty;
    private static bool suppressRemoval;
    private static float elapsed;
    private static Vector2 combatSearchPadding = Vector2.one; // 当前节点受击范围的最大外延，只在拓扑或资源变化时重算。
    private const int MaxCatchUpStepsPerFrame = 8;
    public static uint TransportStep { get; private set; } // 同一轮机械模拟中的跨带搬运只提交一次。
    public static IReadOnlyList<MechanicalNetwork> Networks => graph?.Networks;
    public static string WorldKey => worldKey;
    /// <summary>机械数据格变化时通知区块表现与导航，不依赖世界物品实例。</summary>
    public static event Action<Vector2Int> CellChanged;
    public static event Action<MachineEntity> NodeStateChanged; // 联机只在数据变化时采集节点快照。
    public static event Action<int> NodeRemoved; // 权威节点删除通知。
    public static event Action<MachineEntity> VisualSpeedChanged; // 只传输转速，不传重复库存快照。

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        UnbindMachineResources();
        airflowProviders.Clear();
        RemoteOperationRequested = null;
        MachineInventoryCommands.Reset();
        nodes.Clear(); interactions.Clear(); processorOwners.Clear(); sourceProviders.Clear(); sourceRpmProviders.Clear(); players.Clear();
        graph = null; owner = null; worldKey = null; dirty = false; suppressRemoval = false; elapsed = 0;
        TransportStep = 0;
        ResetElectricalRuntime();
        ResetFluidRuntime();
        CellChanged = null;
        NodeStateChanged = null; NodeRemoved = null; VisualSpeedChanged = null;
    }

    /// <summary>MOD 扭矩条件返回 0..1 可用比例，条件必须独立于 GameObject 与 ChunkView。</summary>
    public static void RegisterSource(string id, Func<MachineEntity, float> provider)
    {
        if (string.IsNullOrWhiteSpace(id) || provider == null) throw new ArgumentException("扭矩来源注册无效。");
        sourceProviders[id] = provider;
    }
    /// <summary>MOD 为动态动力源注册实际 RPM；不注册时使用定义固定转速。</summary>
    public static void RegisterSourceRpm(string id, Func<MachineEntity, float> provider)
    {
        if (string.IsNullOrWhiteSpace(id) || provider == null) throw new ArgumentException("动力源转速注册无效。");
        sourceRpmProviders[id] = provider;
    }
    public static void ClearSourceProviders()
    { sourceProviders.Clear(); sourceRpmProviders.Clear(); ClearElectricalProviders(); }

    private static void EnsureScope()
    {
        BindMachineResources();
        var save = SaveDataMgr.Instance?.SaveData;
        string key = SceneManager.GetActiveScene().name;
        if (ReferenceEquals(owner, save) && worldKey == key && graph != null && electricalGraph != null) return;
        if (ReferenceEquals(owner, save)) CaptureCurrentWorld();
        ReleaseRuntime();
        owner = save; worldKey = key;
        MachineCatalog.EnsureLoaded();
        Vector2 size = ChunkMgr.ExistingInstance != null
            ? ChunkMgr.GetChunkSize()
            : new Vector2(PlanetData.DefaultChunkDimension, PlanetData.DefaultChunkDimension);
        var chunkSize = new Vector2Int(Mathf.Max(1, Mathf.RoundToInt(size.x)), Mathf.Max(1, Mathf.RoundToInt(size.y)));
        WorldTopologyDomain topology = WorldTopologyRuntime.GetActiveDomain();
        Vector2Int period = topology.IsWrapped
            ? new Vector2Int(topology.Span.x / chunkSize.x, topology.Span.y / chunkSize.y)
            : Vector2Int.zero;
        graph = new MechanicalNetworkGraph(chunkSize, period, topology);
        InitializeElectricalScope(topology);
        GameplayCombatBridge.Register(MachineCombatBridge.Instance);
        if (save?.Mechanical?.Worlds != null && save.Mechanical.Worlds.TryGetValue(key, out var snapshots))
            foreach (var data in snapshots) RestoreDescriptor(data);
        dirty = true;
        BuildingOccupancyRegistry.RebuildSightBlockingIndex(nodes.Values, topology);
    }

    private static void RestoreDescriptor(ItemData snapshot)
    {
        if (snapshot == null) return;
        var definition = MachineCatalog.Get(snapshot.IDName);
        // 缺失 MOD 内容不丢弃存档，恢复目录后仍能重新载入。
        if (definition == null) return;
        ItemDefinitionRuntime.RebasePersistedData(GameRes.ExistingInstance, snapshot);
        var state = ReadMachineState(snapshot);
        if (state.Hp < 0f) state.Hp = ResolveMaximumHp(snapshot);
        var node = new MachineEntity
        {
            Id = snapshot.Guid, Cell = CellOf(snapshot.transform.position), Definition = definition, Snapshot = snapshot,
            RotationQuarterTurns = state.RotationQuarterTurns, ConveyorMode = state.ConveyorMode,
            Engaged = state.Engaged, RatioIndex = state.RatioIndex
        };
        InitializeElectricalState(node, state);
        nodes.Add(snapshot.Guid, node);
    }

    /// <summary>仅正式安装提交和世界存档恢复调用；手持召唤器不会登记到网络。</summary>
    public static MachineEntity Attach(Mod_MechanicalNode view)
    {
        if (!GameNetwork.HasStateAuthority) return null;
        EnsureScope();
        int id = view.item.itemData.Guid;
        bool added = false;
        if (!nodes.TryGetValue(id, out var node))
        {
            node = new MachineEntity { Id = id, Cell = CellOf(view.item.transform.position), Definition = view.Definition,
                Snapshot = view.item.itemData, State = view.LocalState };
            InitializeElectricalState(node, view.LocalState);
            nodes.Add(id, node); dirty = true;
            CopyTopology(node);
            EnsureProcessor(node);
            added = true;
        }
        else if (node.State == null) WakeNode(node);
        bool viewChanged = node.View != view;
        node.View = view;
        if (added) BuildingOccupancyRegistry.NotifyMechanicalChanged(node.Cell);
        if (viewChanged) CellChanged?.Invoke(node.Cell); // 外壳接管阻挡时撤销数据 Box，避免重复碰撞。
        return node;
    }

    /// <summary>把已校验的机械建筑快照直接提交为世界节点，落地过程不创建 Item 或 GameObject。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static MachineEntity Place(ItemData snapshot)
    {
        if (!GameNetwork.HasStateAuthority || snapshot == null || !OwnsWorldItem(snapshot))
            throw new InvalidOperationException("机械建筑数据无效或当前实例无世界写入权限。");
        EnsureScope();
        MachineDefinition definition = MachineCatalog.Get(snapshot.IDName)
            ?? throw new InvalidOperationException("机械定义不存在：" + snapshot.IDName);
        Vector2Int cell = CellOf(snapshot.transform.position);
        if (!ValidatePlacement(definition, cell, false, out string reason))
            throw new InvalidOperationException(reason);
        if (nodes.ContainsKey(snapshot.Guid))
            throw new InvalidOperationException("机械节点 GUID 重复：" + snapshot.Guid);
        MachineState state = ReadMachineState(snapshot);
        if (state.Hp < 0f) state.Hp = ResolveMaximumHp(snapshot);
        var node = new MachineEntity
        {
            Id = snapshot.Guid,
            Cell = cell,
            Definition = definition,
            Snapshot = FastCloner.FastCloner.DeepClone(snapshot),
            State = state
        };
        InitializeElectricalState(node, state);
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
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ItemData Remove(int id)
    {
        EnsureScope();
        if (!GameNetwork.HasStateAuthority || !nodes.TryGetValue(id, out MachineEntity node))
            return null;
        CaptureNode(node);
        ItemData snapshot = FastCloner.FastCloner.DeepClone(node.Snapshot);
        RememberGeneratedRemoval(node);
        DisposeProcessor(node);
        if (interactions.Remove(id, out MachineInteractionTarget interaction)) interaction.Dispose();
        nodes.Remove(id);
        dirty = true;
        BuildingOccupancyRegistry.NotifyMechanicalChanged(node.Cell);
        CellChanged?.Invoke(node.Cell);
        NodeRemoved?.Invoke(id);
        return snapshot;
    }

    /// <summary>拆除事务先捕获纯数据节点的加工库存和朝向，不改变世界占地。</summary>
    public static ItemData CaptureSnapshot(MachineEntity node)
    {
        EnsureScope();
        if (node == null || !nodes.TryGetValue(node.Id, out MachineEntity current) ||
            !ReferenceEquals(node, current)) return null;
        CaptureNode(node);
        return FastCloner.FastCloner.DeepClone(node.Snapshot);
    }

    /// <summary>初次加入联机世界时枚举全部权威节点。</summary>
    public static void CollectNodes(List<MachineEntity> result)
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
        foreach (MachineEntity node in nodes.Values) changedCells.Add(node.Cell);
        foreach (MachineInteractionTarget interaction in interactions.Values) interaction.Dispose();
        interactions.Clear();
        foreach (MachineEntity node in nodes.Values) DisposeProcessor(node);
        nodes.Clear();
        ResetFluidRuntime();
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
        MachineDefinition definition = MachineCatalog.Get(snapshot.IDName) ??
            throw new InvalidOperationException("联机机械定义缺失：" + snapshot.IDName);
        Vector2Int previousCell = default;
        bool moved = false;
        bool sameDefinition = false;
        if (!nodes.TryGetValue(snapshot.Guid, out MachineEntity node))
        {
            node = new MachineEntity { Id = snapshot.Guid };
            nodes.Add(node.Id, node);
        }
        else
        {
            previousCell = node.Cell;
            moved = previousCell != CellOf(snapshot.transform.position);
            sameDefinition = node.Definition?.Id == definition.Id && node.Definition?.LogicId == definition.LogicId;
        }
        node.Cell = CellOf(snapshot.transform.position);
        node.Definition = definition;
        node.Snapshot = FastCloner.FastCloner.DeepClone(snapshot);
        MachineState incomingState = ReadMachineState(node.Snapshot);
        bool applied = sameDefinition && node.Logic?.ApplyRemoteSnapshot(node.Snapshot) == true;
        if (!applied && sameDefinition && node.Processor != null && incomingState.Processing != null)
        {
            node.Processor.ApplyRemoteState(incomingState.Processing);
            incomingState.Processing = node.Processor.State;
            applied = true;
        }
        node.State = incomingState;
        ApplyFluidRemoteState(node, applied);
        InitializeElectricalState(node, incomingState);
        if (!applied) { DisposeProcessor(node); EnsureProcessor(node); }
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
        if (!nodes.TryGetValue(id, out MachineEntity node)) return;
        if (interactions.Remove(id, out MachineInteractionTarget interaction)) interaction.Dispose();
        DisposeProcessor(node);
        nodes.Remove(id);
        fluidStates.Remove(id);
        dirty = true;
        BuildingOccupancyRegistry.NotifyMechanicalChanged(node.Cell);
        CellChanged?.Invoke(node.Cell);
    }

    /// <summary>客户端更新 GPU 动画速度，不重传加工库存。</summary>
    public static void ApplyRemoteSpeed(int id, float rpm, int entryDirection)
    {
        EnsureScope();
        if (!nodes.TryGetValue(id, out MachineEntity node)) return;
        node.Rpm = rpm;
        node.EntryDirection = entryDirection;
        UpdateVisualSpeed(node);
    }

    /// <summary>战斗和 MOD 修改机械状态后的显式增量通知。</summary>
    public static void StateChanged(MachineEntity node)
    {
        if (!Contains(node)) return;
        NodeStateChanged?.Invoke(node);
        if (node.Definition.Fluid != null && fluidTankOwners.TryGetValue(node.Id, out FluidTankGroup group) && group.OwnerId != node.Id &&
            nodes.TryGetValue(group.OwnerId, out MachineEntity sharedOwner)) NodeStateChanged?.Invoke(sharedOwner);
    }

    /// <summary>直接按世界格和占地层查询机械数据节点。</summary>
    public static MachineEntity GetAt(Vector2Int cell, int layer)
    {
        EnsureScope();
        RebuildGraphsIfDirty();
        return graph.At(cell, layer);
    }

    /// <summary>联机增量按节点身份查找当前世界的机械数据。</summary>
    public static MachineEntity GetById(int id)
    {
        EnsureScope();
        return nodes.TryGetValue(id, out MachineEntity node) ? node : null;
    }

    /// <summary>导航等高频查询只读取当前已建立的机械世界，不触发场景或存档作用域切换。</summary>
    public static MachineEntity GetAtCurrentWorld(Vector2Int cell, int layer)
    {
        if (graph == null) return null;
        RebuildGraphsIfDirty();
        return graph.At(cell, layer);
    }

    /// <summary>攻击候选查询涵盖完整受击范围，而不是把所有机器假定为一格大小。</summary>
    public static Vector2 GetCombatSearchPadding()
    {
        if (graph == null) return Vector2.one;
        RebuildGraphsIfDirty();
        return combatSearchPadding;
    }

    private static void RebuildCombatSearchPadding()
    {
        combatSearchPadding = Vector2.one;
        foreach (MachineEntity node in nodes.Values)
        {
            if (GameRes.ExistingInstance == null ||
                !GameRes.ExistingInstance.TryGetItemDefinition(node.Definition.Id, out RuntimeItemDefinition definition))
                throw new InvalidOperationException("机器受击定义缺失：" + node.Definition.Id);
            MachineCombatBridge.ValidateHealth(definition.Health);
            FlatWorld.Geometry.PerceptionShape2D shape = MachineCombatBridge.ResolveHitShape(
                definition.Health, Vector2.zero, node.RotationQuarterTurns, node.Definition.IsConverter);
            float2 extents = shape.IsCircle != 0 ? new float2(shape.Radius) : shape.Extents;
            float2 padding = math.abs(shape.Center) + extents;
            combatSearchPadding = Vector2.Max(combatSearchPadding, new Vector2(padding.x, padding.y));
        }
    }

    /// <summary>交互半径内按数据格索引机械目标，查询量与玩家附近格数相关。</summary>
    public static void QueryInteractionTargets(Item actor, float radius, Vector2? pointer, List<IInteractable> result)
    {
        if (actor == null || result == null) return;
        EnsureScope();
        if (actor.gameObject.scene.name != worldKey) return;
        RebuildGraphsIfDirty();
        if (!graph.HasNodes) return;
        Vector3 position = actor.transform.position;
        // 命中点与机械锚点的格坐标差不会超过距离的向上取整，无需多扫外围一圈。
        int extent = Mathf.CeilToInt(pointer.HasValue ? PointerInteractionRadius : radius);
        Vector2 queryCenter = pointer ?? (Vector2)position;
        Vector2Int center = new Vector2Int(Mathf.FloorToInt(queryCenter.x), Mathf.FloorToInt(queryCenter.y));
        IReadOnlyList<MachineEntity> candidates = graph.GetInteractionCandidates(center, extent, pointer.HasValue);
        WorldTopologyDomain topology = graph.Topology;
        float2 actorPosition = new float2(position.x, position.y);
        Vector2 pointerPosition = pointer.GetValueOrDefault();
        float2 pointerPoint = new float2(pointerPosition.x, pointerPosition.y);
        for (int i = 0; i < candidates.Count; i++)
        {
            MachineEntity node = candidates[i];
            Vector3 targetPosition = node.Snapshot.transform.position;
            float2 targetPoint = new float2(targetPosition.x, targetPosition.y);
            if (topology.Distance(actorPosition, targetPoint) > radius ||
                pointer.HasValue && topology.Distance(pointerPoint, targetPoint) > PointerInteractionRadius)
                continue;
            MachineInteractionTarget target = GetOrCreateInteractionTarget(node);
            if (target != null && !result.Contains(target)) result.Add(target);
        }
    }

    /// <summary>正式查询和已加载视觉共用稳定目标身份，不唤醒机器或重建机械图。</summary>
    public static MachineInteractionTarget GetOrCreateInteractionTarget(MachineEntity node)
    {
        if (!Contains(node)) return null;
        if (!interactions.TryGetValue(node.Id, out MachineInteractionTarget target))
        {
            target = new MachineInteractionTarget(node);
            interactions.Add(node.Id, target);
        }
        return target;
    }

    /// <summary>数据交互目标检查节点是否仍属于当前世界。</summary>
    public static bool Contains(MachineEntity node)
        => node != null && nodes.TryGetValue(node.Id, out MachineEntity current) &&
           ReferenceEquals(node, current);

    /// <summary>休眠节点由交互唤醒加工器，随后正常交给网络 Tick 管理。</summary>
    public static void WakeForInteraction(MachineEntity node)
    {
        if (!Contains(node)) return;
        if (node.State == null) WakeNode(node);
    }

    /// <summary>外部动力已传入时禁止手推；面板据此保留按钮但禁用点击。</summary>
    public static bool CanManualDrive(MachineEntity node)
        => GameNetwork.HasStateAuthority && Contains(node) && node.State != null &&
           node.Definition.ManualDriveTorque > 0 && !node.IncomingPower;

    /// <summary>单次手推向节点补入短时动力，实际转速和扭矩仍由整网解算。</summary>
    public static bool TryManualDrive(MachineEntity node)
    {
        if (!CanManualDrive(node)) return false;
        node.State.ManualSeconds = Mathf.Max(node.State.ManualSeconds, node.Definition.ManualDriveSecondsPerPress);
        StateChanged(node);
        return true;
    }

    /// <summary>建造耐久来自可替换的物品定义，与运行时碰撞体无关。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static float ResolveMaximumHp(string itemId)
    {
        if (GameRes.ExistingInstance == null ||
            !GameRes.ExistingInstance.TryGetItemDefinition(itemId, out RuntimeItemDefinition definition) ||
            definition.Health?.HasHp != true || !MachineDefinition.Positive(definition.Health.MaxHp))
            throw new InvalidOperationException("机械耐久定义缺失：" + itemId);
        return definition.Health.MaxHp;
    }

    /// <summary>新放置机器和受损阈值使用实例品质，已恢复的当前生命不再重复乘倍率。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static float ResolveMaximumHp(ItemData snapshot)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        float maximum = CraftedDurabilityQuality.ResolveMaximumHp(ResolveMaximumHp(snapshot.IDName), snapshot);
        if (!MachineDefinition.Positive(maximum)) throw new InvalidOperationException("机器制作品质导致耐久无效：" + snapshot.IDName);
        return maximum;
    }

    /// <summary>按区块边界枚举节点，供 BRG 表现绑定使用。</summary>
    public static void CollectInBounds(BoundsInt bounds, List<MachineRenderCell> result)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));
        EnsureScope();
        result.Clear();
        RebuildGraphsIfDirty();
        for (int y = bounds.yMin; y < bounds.yMax; y++)
        for (int x = bounds.xMin; x < bounds.xMax; x++)
        for (int layer = 0; layer <= 5; layer++)
        {
            MachineEntity node = graph.At(new Vector2Int(x, y), layer);
            if (node != null) result.Add(new MachineRenderCell(node, new Vector2Int(x, y)));
        }
    }

    public static void TopologyChanged(MachineEntity node)
    {
        if (node == null) return;
        CopyTopology(node); dirty = true;
        CellChanged?.Invoke(node.Cell);
        StateChanged(node);
    }
    private static void CopyTopology(MachineEntity node)
    {
        node.RotationQuarterTurns = node.State.RotationQuarterTurns;
        node.ConveyorMode = node.State.ConveyorMode;
        node.Engaged = node.State.Engaged; node.RatioIndex = node.State.RatioIndex;
    }

    /// <summary>真实销毁/拆除删除拓扑；表现回收和退出世界由独立抑制范围保留权威记录。</summary>
    public static void BeforeDespawn(Item item)
    {
        if (suppressRemoval || item?.itemData == null || !nodes.TryGetValue(item.itemData.Guid, out var node)) return;
        if (node.View == null || node.View.item != item) return;
        node.View = null;
        DisposeProcessor(node);
        nodes.Remove(node.Id); dirty = true;
        CellChanged?.Invoke(node.Cell);
    }
    public static void Detach(Mod_MechanicalNode view)
    {
        if (view.Node != null && view.Node.View == view)
        {
            view.Node.View = null;
            CellChanged?.Invoke(view.Node.Cell); // 外壳卸载后由数据 Box 继续阻挡。
        }
    }
    #endregion

    #region 调度与休眠
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Tick(float deltaTime)
    {
        if (!GameNetwork.HasStateAuthority) return;
        EnsureScope();
        elapsed += Mathf.Max(0, deltaTime);
        float step = MachineCatalog.Settings.TickSeconds;
        if (elapsed < step) return;
        RebuildGraphsIfDirty();
        CollectPlayerChunks();
        // 帧内补算有上限，余量留到后续帧；暂停和时间缩放仍服从传入的世界时间。
        int catchUpSteps = Mathf.Min(MaxCatchUpStepsPerFrame, Mathf.FloorToInt(elapsed / step));
        for (int tick = 0; tick < catchUpSteps; tick++)
        {
            TransportStep++;
            elapsed -= step;
            BeginFluidStep(step);
            RebuildGraphsIfDirty();
            foreach (var network in graph.Networks)
            {
                bool active = graph.ShouldBeActive(network, players, step, MachineCatalog.Settings) ||
                    RequiresElectricalBridgeSimulation(network) || RequiresFluidSimulation(network);
                if (!active)
                {
                    if (network.Active) SleepNetwork(network);
                    continue;
                }
                // 拓扑合并可能包含冷节点，全部恢复成功后才运行任何一个节点。
                foreach (var node in network.Nodes) if (node.State == null) WakeNode(node);
                network.Active = true;
                foreach (var node in network.Nodes) node.Active = true;
            }
            // 双向电机先辨别外部输入，再按无回流的转换顺序结算本轮供能。
            SolveElectricalNetworks(step);
            foreach (var network in graph.Networks)
            {
                if (!network.Active) continue;
                SolveNetwork(network);
                foreach (var node in network.Nodes) UpdateVisualSpeed(node);
            }
            // 所有网络先完成转速分配，再推进设施，风箱跨网供风不读上一轮结果。
            foreach (var network in graph.Networks)
            {
                if (!network.Active) continue;
                foreach (var node in network.Nodes) SimulateNode(node, step);
            }
            AdvanceFacilities(step);
            CompleteFluidStep(step);
            PressureExplosionQueue.Tick(step);
        }
    }

    /// <summary>先排除手推源检查整网外部动力，再让无外部动力的手推石磨作为动力源入网。</summary>
    private static void SolveNetwork(MechanicalNetwork network, bool independentPowerOnly = false)
    {
        float referenceRpm = MachineCatalog.Settings.ReferenceRpm;
        MechanicalNetworkGraph.Solve(network, independentPowerOnly ? GetIndependentExternalSourceFactor : GetExternalSourceFactor,
            GetSourceRpm, referenceRpm);
        bool hasExternalSource = false;
        foreach (MachineEntity node in network.Nodes)
            if (node.Definition.ManualDriveTorque <= 0 && node.SourceFactor > 0)
                hasExternalSource = true;
        bool manualDriveActive = false;
        foreach (MachineEntity node in network.Nodes)
        {
            if (node.Definition.ManualDriveTorque <= 0) continue;
            node.IncomingPower = hasExternalSource;
            manualDriveActive |= !node.IncomingPower && node.State.ManualSeconds > 0;
        }
        if (manualDriveActive)
        {
            MechanicalNetworkGraph.Solve(network, independentPowerOnly ? GetIndependentSourceFactor : GetSourceFactor,
                GetSourceRpm, referenceRpm);
            foreach (MachineEntity node in network.Nodes)
                if (node.Definition.ManualDriveTorque > 0 && node.FlowVisited && node.EntryDirection >= 0)
                    node.IncomingPower = true;
        }
        UpdateConversionPowerBudget(network);
    }

    /// <summary>外部动力探测不计入手推石磨自身，避免按钮状态依赖上一次解算顺序。</summary>
    private static float GetExternalSourceFactor(MachineEntity node)
        => node.Definition.ManualDriveTorque > 0 ? 0f : GetSourceFactor(node);

    private static float GetIndependentSourceFactor(MachineEntity node)
        => node.Definition.IsConverter || node.Definition.Source == "electric" ? 0f : GetSourceFactor(node);
    private static float GetIndependentExternalSourceFactor(MachineEntity node)
        => node.Definition.ManualDriveTorque > 0 ? 0f : GetIndependentSourceFactor(node);

    private static void CollectPlayerChunks()
    {
        players.Clear();
        if (ItemMgr.Instance == null) return;
        foreach (var player in ItemMgr.Instance.Player_DIC.Values)
            if (player != null && player.gameObject.scene.name == worldKey)
                players.Add(graph.ChunkOf(CellOf(player.transform.position)));
    }

    private static void WakeNode(MachineEntity node)
    {
        node.State = ReadMachineState(node.Snapshot);
        if (node.Definition?.Electrical?.IsBattery == true)
            node.State.ElectricalStoredJoules = node.ElectricalStoredJoules;
        if (node.State.Hp < 0f) node.State.Hp = ResolveMaximumHp(node.Snapshot);
        EnsureProcessor(node);
    }
    private static void EnsureProcessor(MachineEntity node)
    {
        if (!string.IsNullOrWhiteSpace(node.Definition.LogicId))
        {
            node.Logic ??= MachineLogicRegistry.Create(node);
            return;
        }
        if (node.Processor == null &&
            (!string.IsNullOrWhiteSpace(node.Definition.Station) ||
             !string.IsNullOrWhiteSpace(node.Definition.ProcessCapability)))
        {
            node.Processor = new RecipeProcessor(
                node.Definition.Station,
                node.State.Processing ??= new RecipeProcessingState(),
                node.Definition.ProcessCapability,
                node.Definition.ProcessCapabilityLevel > 0 ? node.Definition.ProcessCapabilityLevel : null);
            node.Processor.Input.MachineOwner = node;
            node.Processor.Output.MachineOwner = node;
            node.Processor.StateChanged += OnProcessorChanged;
            processorOwners[node.Processor] = node;
        }
    }

    /// <summary>加工库存与进度变化进入可节流的节点快照队列。</summary>
    private static void OnProcessorChanged(RecipeProcessor processor)
    {
        if (processorOwners.TryGetValue(processor, out MachineEntity node)) StateChanged(node);
    }

    /// <summary>释放加工器时解除节点映射，避免唤醒后旧库存回调污染新状态。</summary>
    private static void DisposeProcessor(MachineEntity node)
    {
        DisposeMachineRuntime(node);
        if (node.Processor == null) return;
        node.Processor.StateChanged -= OnProcessorChanged;
        node.Processor.Input.MachineOwner = null;
        node.Processor.Output.MachineOwner = null;
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
            DisposeProcessor(node); node.State = null; node.Rpm = 0; node.IncomingPower = false;
            node.Active = false; node.LogicElapsed = 0f;
            UpdateVisualSpeed(node);
        }
        network.Active = false; network.Status = "休眠";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void SimulateNode(MachineEntity node, float step)
    {
        if (!GameNetwork.HasStateAuthority || node == null || !Contains(node) || node.State == null) return;
        // 自定义领域逻辑也必须消耗手摇供能时间，不能因提前返回变成永久动力源。
        if (node.Definition.Source == "manual" || node.Definition.ManualDriveTorque > 0)
            node.State.ManualSeconds = Mathf.Max(0, node.State.ManualSeconds - step);
        if (node.Definition.Transport != null && node.SpeedRpm > 0f)
            DroppedItemService.TransportItemBacked(node, step);
        if (node.Logic != null)
        {
            AdvanceFacilityLogic(node, step);
            return;
        }
        // 手推石磨虽临时向外供能，仍按自身解算后的转速执行研磨。
        if (node.SpeedRpm > 0)
            node.Processor?.Advance(CalculateWorkAmount(node, step));
        if (interactions.TryGetValue(node.Id, out MachineInteractionTarget interaction))
            interaction.RefreshPanel();
    }

    /// <summary>用力器效率严格按需求转速线性计算；允许超过 100%，不得截断或重复除以基准转速。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static float GetWorkEfficiency(MachineEntity node)
    {
        if (node?.Definition == null || node.SpeedRpm <= 0) return 0;
        return node.SpeedRpm / node.Definition.RequiredRpm;
    }

    /// <summary>加工工作量由最终分配的转速决定，扭矩只作为解算门槛。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static float CalculateWorkAmount(MachineEntity node, float seconds)
        => Mathf.Max(0f, seconds) * GetWorkEfficiency(node);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static float GetSourceFactor(MachineEntity node)
    {
        if (node == null || node.SourceTorque <= 0) return 0;
        if (node.Definition.IsConverter) return GetConverterMotorFactor(node);
        if (node.Definition.ManualDriveTorque > 0)
            return node.State != null && !node.IncomingPower && node.State.ManualSeconds > 0 ? 1 : 0;
        string source = node.Definition.Source;
        if (sourceProviders.TryGetValue(source, out var provider)) return Mathf.Clamp01(provider(node));
        if (source == "electric") return Mathf.Clamp01(node.ElectricalPowerRatio);
        if (source == "manual") return node.State != null && node.State.ManualSeconds > 0 ? 1 : 0;
        if (source == "wind") return WeatherMgr.Instance != null ? WeatherMgr.Instance.GetCurrentWindStrength() : 0;
        if (source == "water") return GetWaterSourceFactor(node);
        if (source == "fluid-engine") return GetFluidEngineSourceFactor(node);
        return 0;
    }

    /// <summary>内建动力源应用配置转向，MOD 的 RPM Provider 直接提供最终有符号转速。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static float GetSourceRpm(MachineEntity node)
    {
        if (node?.Definition == null) return 0f;
        if (node.Definition.ManualDriveTorque > 0)
            return node.Definition.ManualDriveRpm * node.Definition.SourceRotationDirection;
        string source = node.Definition.Source;
        if (sourceRpmProviders.TryGetValue(source, out var provider)) return provider(node);
        if (source == "water") return GetWaterSourceRpm(node) * node.Definition.SourceRotationDirection;
        return node.Definition.Rpm * node.Definition.SourceRotationDirection;
    }

    /// <summary>水车扭矩随自身水格的实际表层流速变化；湖泊和无流河段不供能。</summary>
    private static float GetWaterSourceFactor(MachineEntity node)
    {
        if (!TryGetWaterCurrentSpeed(node, out float speed)) return 0f;
        return Mathf.Clamp01(speed / WorldItemWaterRules.RiverDriftSpeed);
    }

    /// <summary>水车每分钟转数由线速度除以叶轮周长换算，轮半径来自机械定义。</summary>
    private static float GetWaterSourceRpm(MachineEntity node)
    {
        if (!TryGetWaterCurrentSpeed(node, out float speed)) return 0f;
        float circumference = 2f * Mathf.PI * node.Definition.SourceRadius;
        return speed / circumference * 60f;
    }

    /// <summary>读取水车占用水格的当前流速，不从岸边或其它邻格借用水流。</summary>
    private static bool TryGetWaterCurrentSpeed(MachineEntity node, out float speed)
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
    private static void UpdateVisualSpeed(MachineEntity node)
    {
        float now = Time.time;
        bool changed = UpdateConveyorVisualSpeed(node);
        if (Mathf.Abs(node.VisualRpm - node.Rpm) >= .01f || Math.Sign(node.VisualRpm) != Math.Sign(node.Rpm))
        {
            node.VisualPhase = Mathf.Repeat(node.VisualPhase +
                (now - node.VisualTime) * node.VisualRpm * Mathf.PI * 2f / 60f, Mathf.PI * 2f);
            node.VisualTime = now;
            node.VisualRpm = node.Rpm;
            changed = true;
        }
        if (node.Definition.Kind == "gearbox")
        {
            node.GetGearboxRpm(out float large, out float small);
            float radiansPerRpm = Mathf.PI * 2f / 60f;
            changed |= UpdateGearboxTrack(ref node.GearboxLargePhase, ref node.GearboxLargeSpeed,
                ref node.GearboxLargeTime, radiansPerRpm * large, now);
            changed |= UpdateGearboxTrack(ref node.GearboxSmallPhase, ref node.GearboxSmallSpeed,
                ref node.GearboxSmallTime, radiansPerRpm * small, now);
        }
        if (!changed) return;
        CellChanged?.Invoke(node.Cell);
        VisualSpeedChanged?.Invoke(node);
    }

    /// <summary>输入方向或传动比改变时先积分旧角速度，使箱内齿轮不断帧跳相。</summary>
    private static bool UpdateGearboxTrack(ref float phase, ref float speed,
        ref float sampleTime, float nextSpeed, float now)
    {
        if (Mathf.Abs(speed - nextSpeed) < .001f && Math.Sign(speed) == Math.Sign(nextSpeed)) return false;
        phase = Mathf.Repeat(phase + (now - sampleTime) * speed, Mathf.PI * 2f);
        sampleTime = now;
        speed = nextSpeed;
        return true;
    }

    private static void RemoveView(MachineEntity node)
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
        RebuildGraphsIfDirty();
        var node = graph.At(cell, layer);
        return node != null && node.Id != except;
    }

    /// <summary>调用方已归一化世界格，机械图仍沿用自己的冻结拓扑域。</summary>
    internal static bool IsOccupiedNormalized(Vector2Int cell, int layer)
    {
        if (graph == null) return false;
        RebuildGraphsIfDirty();
        return graph.At(cell, layer) != null;
    }

    /// <summary>两层机械共用一份格索引，避免逐格分别查询两次。</summary>
    internal static bool IsOccupiedOnPlacementLayersNormalized(Vector2Int cell)
    {
        if (graph == null) return false;
        RebuildGraphsIfDirty();
        return graph.IsOccupiedOnPlacementLayersNormalized(graph.NormalizeCell(cell));
    }

    public static bool ValidatePlacement(MachineDefinition definition, Vector2Int cell, bool vertical, out string reason)
    {
        EnsureScope();
        reason = null;
        if (IsOccupied(cell, definition.Layer)) { reason = "当前机械层已占用"; return false; }
        if (BuildingOccupancyRegistry.IsOccupied(cell, layer: definition.Layer))
        { reason = "当前建筑层已占用"; return false; }
        if (definition.Fluid?.CombineAdjacent == true)
            foreach (Vector2Int direction in MechanicalNetworkGraph.Directions)
            {
                MachineEntity neighbor = graph.At(cell + direction, 0);
                if (neighbor?.Definition.Fluid?.MaterialId == definition.Fluid.MaterialId &&
                    !string.IsNullOrEmpty(GetFluidState(neighbor).RuptureBudgetId))
                { reason = "相邻储罐正在结算破裂"; return false; }
            }
        if (definition.Kind == "bridge")
        {
            if (graph.At(cell, 0)?.Definition.HasMechanicalPorts != true)
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

    /// <summary>机械风箱只增强出风口正对的相邻炉体；20 RPM 对应满增温，多个风箱取最高倍率。</summary>
    public static float GetBellowsBoost(Vector3 position)
    {
        if (graph == null) return 0;
        RebuildGraphsIfDirty();
        Vector2Int cell = graph.NormalizeCell(new Vector2Int(Mathf.FloorToInt(position.x), Mathf.FloorToInt(position.y)));
        float boost = 0;
        foreach (var offset in MechanicalNetworkGraph.Directions)
        {
            var node = graph.At(cell + offset, 0);
            if (node?.Definition.Kind != "bellows" || node.Network?.Active != true)
                continue;

            Vector2Int outlet = graph.NormalizeCell(
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
        => data != null && Mod_Building.TryReadBuildingData(data, out _, out var building) &&
           building.Role == BuildingRole.PlacedBuilding && MachineCatalog.Get(data.IDName) != null;

    private static void CaptureNode(MachineEntity node)
    {
        CaptureFluidState(node);
        if (node.View != null)
        {
            node.View.item.Save();
            node.Snapshot = FastCloner.FastCloner.DeepClone(node.View.item.itemData);
        }
        node.Logic?.Capture();
        MachineState state = node.State ?? ReadMachineState(node.Snapshot);
        WriteElectricalState(node, state);
        MachinePersistence.Write(node.Snapshot, "core", state);
        if (TryGetModuleData(node.Snapshot, out var module)) module.WriteData(state);
    }
    private static void CaptureCurrentWorld()
    {
        if (owner == null || graph == null || string.IsNullOrEmpty(worldKey)) return;
        owner.Mechanical ??= new MachineArchive();
        var saved = new List<ItemData>();
        if (owner.Mechanical.Worlds.TryGetValue(worldKey, out var previous))
            foreach (var data in previous)
                if (MachineCatalog.Get(data.IDName) == null) saved.Add(data);
        foreach (var node in nodes.Values) { CaptureNode(node); saved.Add(FastCloner.FastCloner.DeepClone(node.Snapshot)); }
        saved.Sort(CompareSnapshots);
        owner.Mechanical.Worlds[worldKey] = saved;
    }
    private static int CompareSnapshots(ItemData a, ItemData b) => a.Guid.CompareTo(b.Guid);
    public static byte[] CaptureArchive(GameSaveData save)
    {
        if (ReferenceEquals(save, owner)) CaptureCurrentWorld();
        return MemoryPackSerializer.Serialize(save.Mechanical ?? new MachineArchive());
    }
    public static void RestoreArchive(GameSaveData save, byte[] bytes)
    {
        var archive = bytes == null || bytes.Length == 0 ? new MachineArchive() : MemoryPackSerializer.Deserialize<MachineArchive>(bytes);
        if (archive == null || archive.Version != 1 || archive.Worlds == null) throw new InvalidOperationException("不支持的机械存档版本。");
        save.Mechanical = archive;
    }
    public static void ReleaseWorld(bool capture)
    {
        UnbindMachineResources();
        if (capture) CaptureCurrentWorld();
        ReleaseRuntime(); owner = null; worldKey = null; graph = null; electricalGraph = null;
        BuildingOccupancyRegistry.RebuildSightBlockingIndex(null, default);
    }
    private static void ReleaseRuntime()
    {
        GameplayCombatBridge.Unregister(MachineCombatBridge.Instance);
        foreach (MachineInteractionTarget interaction in interactions.Values) interaction.Dispose();
        interactions.Clear();
        foreach (var node in nodes.Values) DisposeProcessor(node);
        processorOwners.Clear();
        nodes.Clear(); elapsed = 0;
        ResetFluidRuntime();
        ClearFacilityEnvironment();
    }
    #endregion
}
