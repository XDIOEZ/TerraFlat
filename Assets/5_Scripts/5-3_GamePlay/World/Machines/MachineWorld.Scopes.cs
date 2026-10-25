using System;
using System.Collections.Generic;
using UnityEngine;

#region 机器跨作用域落点
// 转移方一次给出绝对格、位置和朝向，机器世界不再叠加船体旋转。
public readonly struct MachineScopePlacement
{
    public readonly Vector2Int Cell;
    public readonly Vector3 Position;
    public readonly int QuarterTurns;

    public MachineScopePlacement(Vector2Int cell, Vector3 position, int quarterTurns)
    { Cell = cell; Position = position; QuarterTurns = quarterTurns & 3; }
}
#endregion

#region 机械世界身份冻结

/// <summary>世界身份变更时解析一次船体 ID，避免每次环境 Tick 截取新的字符串。</summary>
internal readonly struct MachineScopeIdentity
{
    public readonly string WorldKey;
    public readonly string ShipId;

    public MachineScopeIdentity(string worldKey)
    {
        WorldKey = worldKey;
        ShipId = worldKey != null && worldKey.StartsWith("ship:", StringComparison.Ordinal)
            ? worldKey.Substring(5)
            : null;
    }
}

#endregion

public static partial class MachineWorld
{
    #region 独立本地格作用域
    private sealed class ScopeState
    {
        public readonly Dictionary<int, MachineEntity> Nodes = new();
        public readonly Dictionary<int, MachineInteractionTarget> Interactions = new();
        public readonly Dictionary<RecipeProcessor, MachineEntity> Processors = new();
        public readonly List<Vector2Int> Players = new();
        public MechanicalNetworkGraph Graph;
        public ElectricalNetworkGraph Electrical;
        public FluidNetworkGraph Fluid;
        public string FluidTopologyKey;
        public FluidCatalog FluidTopologyCatalog;
        public GameSaveData Owner;
        private MachineScopeIdentity identity;
        public string Key { get => identity.WorldKey; set => identity = new MachineScopeIdentity(value); }
        public string ShipId => identity.ShipId;
        public bool Dirty, SuppressRemoval, BackgroundSimulation;
        public float Elapsed;
        public uint Step;
        public int TopologyMutationDepth;
        public Vector2 CombatPadding = Vector2.one;
        public readonly Dictionary<int, FluidMachineState> Fluids = new();
        public readonly Dictionary<(int NodeId, string Chamber), FluidInventory> Inventories = new();
        public readonly Dictionary<int, FluidTankGroup> TankOwners = new();
        public readonly List<FluidTankGroup> Tanks = new();
        public readonly HashSet<MachineEntity> CombinedTankNotifications = new();
        public readonly Dictionary<FluidInventory, FluidInventoryState> Stock = new();
        public readonly Dictionary<FluidInventory, FluidInventoryState> StockCache = new();
        public readonly Dictionary<FluidInventory, double> Throughput = new();
        public readonly Dictionary<FluidInventory, FluidInventoryState> GasStock = new();
        public readonly Dictionary<FluidInventory, FluidInventoryState> GasStockCache = new();
        public readonly Dictionary<FluidInventory, double> GasThroughput = new();
        public readonly Dictionary<(int NodeId, string PortId), double> GasPortThroughput = new();
        public readonly Dictionary<int, double> GasNodeBudget = new(), LiquidNodeBudget = new();
        public readonly Dictionary<MachineEntity, double> MechanicalPower = new();
        public readonly Dictionary<int, int> Appearance = new();
    }

    private static ScopeState scope = new();
    private static readonly Dictionary<string, ScopeState> scopes = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> shipScopeKeys = new(StringComparer.Ordinal);
    private static string scopeOverride;
    private static string shipScopeId => scope.ShipId;
    public static bool IsShipScope => shipScopeId != null;
    private static bool KeepSimulatingWithoutPlayers => IsShipScope || scope.BackgroundSimulation;

    private static void NotifySurfaceMechanicalChanged(Vector2Int cell)
    { if (!IsShipScope && worldKey == GameplayWorldRegistry.ExecutionWorldKey) PublishSurfaceMechanicalChange(cell); }
    private static void NotifySurfaceCellChanged(Vector2Int cell)
    {
        NodeCellChanged?.Invoke(worldKey, cell);
        if (!IsShipScope && worldKey == GameplayWorldRegistry.ExecutionWorldKey) PublishSurfaceCellChange(cell);
    }

    public static void ReleaseShipScope(string shipId, bool capture = false)
    {
        string key = GetShipScopeKey(shipId);
        if (!scopes.TryGetValue(key, out ScopeState value)) return;
        ScopeState previous = scope;
        scope = value;
        if (capture) CaptureCurrentWorld();
        ReleaseRuntime();
        scopes.Remove(key);
        shipScopeKeys.Remove(shipId);
        if (!capture) value.Owner?.Mechanical?.Worlds.Remove(key);
        scope = ReferenceEquals(previous, value) ? new ScopeState() : previous;
        ReleaseCombatBridgeIfNoScopes();
    }

    // 作用域只切换运行态引用，不休眠、复制库存或重建正在加工的设备。
    public static ScopeLease UseScope(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("机器作用域为空。", nameof(key));
        var lease = new ScopeLease(scopeOverride);
        scopeOverride = key;
        EnsureScope();
        return lease;
    }

    #region 销毁阶段借用现有机器作用域
    // 世界释放只切换已有机器状态，不加载目录、建图或登记新作用域。
    internal static IDisposable UseExistingScopeForOwnerRelease(string key)
    {
        if (string.IsNullOrEmpty(key) || !scopes.TryGetValue(key, out ScopeState existing)) return null;
        var lease = new ExistingScopeOwnerReleaseLease(scope, scopeOverride);
        scope = existing;
        scopeOverride = key;
        return lease;
    }

    private sealed class ExistingScopeOwnerReleaseLease : IDisposable
    {
        private readonly ScopeState previousScope;
        private readonly string previousOverride;
        private bool disposed;
        public ExistingScopeOwnerReleaseLease(ScopeState previousScope, string previousOverride)
        { this.previousScope = previousScope; this.previousOverride = previousOverride; }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            scope = previousScope;
            scopeOverride = previousOverride;
        }
    }
    #endregion

    public struct ScopeLease : IDisposable
    {
        private readonly string previous;
        private bool active;
        internal ScopeLease(string value) { previous = value; active = true; }
        public void Dispose()
        {
            if (!active) return;
            active = false;
            scopeOverride = previous;
            EnsureScope();
        }
    }

    private static void SelectScope(GameSaveData save, string key)
    {
        if (ReferenceEquals(scope.Owner, save) && scope.Key == key) return;
        if (scope.Owner != null && !ReferenceEquals(scope.Owner, save)) ReleaseAllScopes(false);
        if (!scopes.TryGetValue(key, out ScopeState selected))
            scopes.Add(key, selected = new ScopeState { Owner = save, Key = key });
        scope = selected;
    }
    private static void CaptureAllScopes(GameSaveData save)
    {
        ScopeState previous = scope;
        try
        {
            foreach (ScopeState value in scopes.Values)
            {
                if (!ReferenceEquals(value.Owner, save)) continue;
                scope = value;
                CaptureCurrentWorld();
            }
        }
        finally { scope = previous; }
    }
    private static void ReleaseAllScopes(bool capture)
    {
        foreach (ScopeState value in scopes.Values)
        {
            scope = value;
            if (capture) CaptureCurrentWorld();
            ReleaseRuntime();
        }
        scopes.Clear();
        shipScopeKeys.Clear();
        scope = new ScopeState();
        scopeOverride = null;
        ReleaseCombatBridgeIfNoScopes();
    }

    public static void TickShip(string shipId, float seconds)
    {
        using (UseScope(GetShipScopeKey(shipId))) Tick(seconds);
    }

    public static MachineEntity PlaceOnShip(string shipId, ItemData snapshot)
    {
        using (UseScope(GetShipScopeKey(shipId))) return Place(snapshot);
    }

    public static ItemData RemoveFromShip(string shipId, int id)
    {
        using (UseScope(GetShipScopeKey(shipId))) return Remove(id);
    }
    public static ScopeLease UseNodeScope(MachineEntity node)
        => UseScope(node?.ScopeKey ?? GameplayWorldRegistry.ExecutionWorldKey);

    public static void TransferShipNode(string fromShip, string toShip, int id)
        => TransferShipNodes(fromShip, toShip, new[] { id });

    public static string GetShipScopeKey(string shipId)
    {
        if (string.IsNullOrWhiteSpace(shipId)) throw new ArgumentException("船体身份为空。", nameof(shipId));
        if (!shipScopeKeys.TryGetValue(shipId, out string key))
            shipScopeKeys.Add(shipId, key = "ship:" + shipId);
        return key;
    }

    public static void TransferShipNodes(string fromShip, string toShip, IReadOnlyList<int> ids)
    {
        if (fromShip == toShip || ids == null || ids.Count == 0) return;
        TransferScopeNodes(GetShipScopeKey(fromShip), GetShipScopeKey(toShip), ids);
    }

    #region 真实节点批量移交
    private sealed class ScopeNodeTransfer
    {
        public MachineEntity Node;
        public MachineScopePlacement Original, Placement;
        public FluidMachineState Fluid;
        public bool Detached;
    }

    // 先冻结并校验整批落点，再交接真实节点和加工器，库存不经过拆除或重新放置。
    public static void TransferScopeNodes(string fromKey, string toKey, IReadOnlyList<int> ids,
        Func<MachineEntity, MachineScopePlacement> placement = null)
    {
        if (string.IsNullOrWhiteSpace(fromKey)) throw new ArgumentException("源机器作用域为空。", nameof(fromKey));
        if (string.IsNullOrWhiteSpace(toKey)) throw new ArgumentException("目标机器作用域为空。", nameof(toKey));
        if (ids == null || ids.Count == 0 || fromKey == toKey && placement == null) return;
        if (!FlatWorld.Networking.GameNetwork.HasStateAuthority)
            throw new InvalidOperationException("只有权威端可以移交机器节点。");
        using var surfaceChanges = BeginSurfaceChangeBatch();
        bool sameScope = string.Equals(fromKey, toKey, StringComparison.Ordinal);
        var candidates = new List<ScopeNodeTransfer>(ids.Count);
        var identities = new HashSet<int>();
        using (UseScope(fromKey))
        {
            RebuildGraphsIfDirty();
            for (int i = 0; i < ids.Count; i++)
            {
                if (!identities.Add(ids[i])) throw new ArgumentException("批量转移包含重复机器身份。", nameof(ids));
                if (!nodes.TryGetValue(ids[i], out MachineEntity node)) continue;
                if (node.Snapshot?.transform == null || node.Definition == null || node.ScopeKey != fromKey)
                    throw new InvalidOperationException("待转移机器状态无效：" + node.Id);
                var original = new MachineScopePlacement(node.Cell, node.Snapshot.transform.position, node.RotationQuarterTurns);
                candidates.Add(new ScopeNodeTransfer { Node = node, Original = original,
                    Placement = placement != null ? placement(node) : original });
            }
        }
        if (candidates.Count == 0) return;
        using (UseScope(toKey))
        {
            RebuildGraphsIfDirty();
            var occupied = new HashSet<Vector3Int>();
            foreach (ScopeNodeTransfer candidate in candidates)
            {
                MachineEntity node = candidate.Node;
                if (nodes.TryGetValue(node.Id, out MachineEntity existing) && (!sameScope || !ReferenceEquals(existing, node)))
                    throw new InvalidOperationException("目标作用域已经包含待转移的机器：" + node.Id);
                MachineScopePlacement target = candidate.Placement;
                if (!FluidUnits.IsFinite(target.Position.x) || !FluidUnits.IsFinite(target.Position.y) || !FluidUnits.IsFinite(target.Position.z))
                    throw new InvalidOperationException("机器转移落点不是有限坐标：" + node.Id);
                Vector2Int cell = graph.NormalizeCell(target.Cell);
                if (CellOf(target.Position) != cell)
                    throw new InvalidOperationException("机器转移的世界格与位置不一致：" + node.Id);
                Vector2Int rawCell = new(Mathf.FloorToInt(target.Position.x), Mathf.FloorToInt(target.Position.y));
                Vector3 position = target.Position + new Vector3(cell.x - (float)rawCell.x, cell.y - (float)rawCell.y, 0f);
                candidate.Placement = new MachineScopePlacement(cell, position, target.QuarterTurns);
                if (!occupied.Add(new Vector3Int(cell.x, cell.y, node.Definition.Layer)))
                    throw new InvalidOperationException("同一批机器转移到重复的格子和层：" + cell);
                MachineEntity occupant = graph.At(cell, node.Definition.Layer);
                if (occupant != null && !(sameScope && identities.Contains(occupant.Id)))
                    throw new InvalidOperationException("机器转移落点的机械层已经占用：" + cell);
                if (!sameScope && !IsShipScope && worldKey == GameplayWorldRegistry.ExecutionWorldKey &&
                    BuildingOccupancyRegistry.IsOccupiedNormalized(cell, layer: node.Definition.Layer))
                    throw new InvalidOperationException("机器转移落点的建筑层已经占用：" + cell);
            }
        }

        using (UseScope(fromKey))
        {
            foreach (ScopeNodeTransfer candidate in candidates)
            {
                if (!nodes.TryGetValue(candidate.Node.Id, out MachineEntity current) || !ReferenceEquals(current, candidate.Node))
                    throw new InvalidOperationException("预检后源机器身份发生变化：" + candidate.Node.Id);
                CaptureNode(candidate.Node);
            }
        }
        try
        {
            DetachScopeTransferNodes(fromKey, candidates);
            AttachScopeTransferNodes(toKey, candidates, restoreOriginal: false);
        }
        catch
        {
            // 交接中断时先收回目标节点，再按原坐标恢复，通知只在完整提交后发布。
            RollbackScopeTransfer(fromKey, toKey, candidates);
            throw;
        }
        RebuildTransferredScope(fromKey);
        if (!sameScope) RebuildTransferredScope(toKey);
        PublishScopeTransferChanges(fromKey, toKey, candidates);
    }

    private static void DetachScopeTransferNodes(string key, List<ScopeNodeTransfer> candidates)
    {
        using (UseScope(key))
        {
            ScopeState source = scope;
            source.TopologyMutationDepth++;
            try
            {
                dirty = true;
                foreach (ScopeNodeTransfer candidate in candidates)
                {
                    MachineEntity node = candidate.Node;
                    if (node.Definition.Fluid != null)
                    {
                        candidate.Fluid = CreatePersistentFluidState(node);
                        DisposeMachineRuntime(node);
                        candidate.Fluid = DetachFluidNodeForScopeTransfer(node);
                    }
                    else if (interactions.Remove(node.Id, out MachineInteractionTarget target)) target.Dispose();
                    RemoveView(node);
                    nodes.Remove(node.Id); dirty = true;
                    candidate.Detached = true;
                    if (node.Processor != null) processorOwners.Remove(node.Processor);
                }
            }
            finally { source.TopologyMutationDepth--; }
        }
    }

    private static void AttachScopeTransferNodes(string key, List<ScopeNodeTransfer> candidates, bool restoreOriginal)
    {
        using (UseScope(key))
        {
            ScopeState destination = scope;
            destination.TopologyMutationDepth++;
            try
            {
                foreach (ScopeNodeTransfer candidate in candidates)
                {
                    MachineEntity node = candidate.Node;
                    MachineScopePlacement target = restoreOriginal ? candidate.Original : candidate.Placement;
                    node.ScopeKey = worldKey;
                    node.Cell = target.Cell;
                    node.Snapshot.transform.position = target.Position;
                    node.State ??= ReadMachineState(node.Snapshot);
                    node.State.RotationQuarterTurns = target.QuarterTurns;
                    CopyTopology(node);
                    WriteMachineState(node.Snapshot, node.State);
                    if (!nodes.ContainsKey(node.Id)) nodes.Add(node.Id, node);
                    dirty = true;
                    if (node.Processor != null) processorOwners[node.Processor] = node;
                    if (candidate.Fluid != null)
                    {
                        ForgetFluidNodeInventoryCaches(node.Id);
                        fluidStates[node.Id] = candidate.Fluid;
                    }
                    EnsureProcessor(node);
                }
            }
            finally { destination.TopologyMutationDepth--; }
        }
    }

    private static void RollbackScopeTransfer(string fromKey, string toKey, List<ScopeNodeTransfer> candidates)
    {
        using (UseScope(toKey))
        {
            foreach (ScopeNodeTransfer candidate in candidates)
            {
                MachineEntity node = candidate.Node;
                if (!candidate.Detached || !nodes.TryGetValue(node.Id, out MachineEntity current) || !ReferenceEquals(current, node)) continue;
                if (node.Definition.Fluid != null)
                {
                    try { DisposeMachineRuntime(node); }
                    catch (Exception error) { Debug.LogException(error); }
                    candidate.Fluid = DetachFluidNodeForScopeTransfer(node);
                }
                nodes.Remove(node.Id);
                if (node.Processor != null) processorOwners.Remove(node.Processor);
                dirty = true;
            }
        }
        AttachScopeTransferNodes(fromKey, candidates, restoreOriginal: true);
        RebuildTransferredScope(fromKey);
        if (fromKey != toKey) RebuildTransferredScope(toKey);
    }

    // 所有权已整批提交，表现或自定义拓扑回调异常只留下脏标记，不能让调用者重复迁移。
    private static void RebuildTransferredScope(string key)
    {
        try { using (UseScope(key)) RebuildGraphsIfDirty(); }
        catch (Exception error)
        {
            if (scopes.TryGetValue(key, out ScopeState value)) value.Dirty = true;
            Debug.LogException(error);
        }
    }

    private static void PublishScopeTransferChanges(string fromKey, string toKey, List<ScopeNodeTransfer> candidates)
    {
        using (UseScope(fromKey))
        {
            foreach (ScopeNodeTransfer candidate in candidates)
            {
                NotifyScopeTransferChange(() => NotifySurfaceMechanicalChanged(candidate.Original.Cell));
                NotifyScopeTransferChange(() => NotifySurfaceCellChanged(candidate.Original.Cell));
                if (fromKey != toKey) NotifyScopeTransferChange(() => NodeRemoved?.Invoke(candidate.Node.Id));
            }
        }
        using (UseScope(toKey))
        {
            foreach (ScopeNodeTransfer candidate in candidates)
            {
                NotifyScopeTransferChange(() => NotifySurfaceMechanicalChanged(candidate.Placement.Cell));
                NotifyScopeTransferChange(() => NotifySurfaceCellChanged(candidate.Placement.Cell));
                NotifyScopeTransferChange(() => NodeStateChanged?.Invoke(candidate.Node));
            }
        }
    }

    private static void NotifyScopeTransferChange(Action notification)
    {
        try { notification(); }
        catch (Exception error) { Debug.LogException(error); }
    }
    #endregion

    // 回收只取该部件的真实份额，其他成员保留剩余库存并重建自己的网络。
    public static ItemData RemoveShipPieceForRecovery(MachineEntity node)
    {
        if (node == null || !FlatWorld.Networking.GameNetwork.HasStateAuthority) return null;
        using (UseNodeScope(node))
        {
            if (!IsShipScope || !nodes.TryGetValue(node.Id, out MachineEntity current) || !ReferenceEquals(node, current)) return null;
            RebuildGraphsIfDirty();
            CaptureNode(node);
            ItemData snapshot = Remove(node.Id);
            if (node.Definition.Fluid != null) ForgetDetachedFluidNode(node);
            RebuildGraphsIfDirty();
            return snapshot;
        }
    }

    private static void ReleaseSurfaceScopes(bool capture)
    {
        var remove = new List<string>();
        foreach (var pair in scopes)
        {
            if (pair.Key.StartsWith("ship:", StringComparison.Ordinal)) continue;
            scope = pair.Value;
            if (capture) CaptureCurrentWorld();
            ReleaseRuntime(); remove.Add(pair.Key);
        }
        foreach (string key in remove) scopes.Remove(key);
        scope = new ScopeState();
        ReleaseCombatBridgeIfNoScopes();
    }
    // 战斗桥属于整个机器会话，单艘船或单个地表释放不撤销其他作用域的能力。
    private static void ReleaseCombatBridgeIfNoScopes()
    { if (scopes.Count == 0) GameplayCombatBridge.Unregister(MachineCombatBridge.Instance); }
    #endregion
}
