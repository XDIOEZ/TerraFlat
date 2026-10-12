using System;
using System.Collections.Generic;
using UnityEngine;

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
        public GameSaveData Owner;
        public string Key;
        public bool Dirty, SuppressRemoval, BackgroundSimulation;
        public float Elapsed;
        public uint Step;
        public int TopologyMutationDepth;
        public Vector2 CombatPadding = Vector2.one;
        public readonly Dictionary<int, FluidMachineState> Fluids = new();
        public readonly Dictionary<(int NodeId, string Chamber), FluidInventory> Inventories = new();
        public readonly Dictionary<int, FluidTankGroup> TankOwners = new();
        public readonly List<FluidTankGroup> Tanks = new();
        public readonly Dictionary<FluidInventory, FluidInventoryState> Stock = new();
        public readonly Dictionary<FluidInventory, FluidInventoryState> StockCache = new();
        public readonly Dictionary<FluidInventory, double> Throughput = new();
        public readonly Dictionary<MachineEntity, double> MechanicalPower = new();
        public readonly Dictionary<int, int> Appearance = new();
    }

    private static ScopeState scope = new();
    private static readonly Dictionary<string, ScopeState> scopes = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> shipScopeKeys = new(StringComparer.Ordinal);
    private static string scopeOverride;
    public static bool IsShipScope => worldKey != null && worldKey.StartsWith("ship:", StringComparison.Ordinal);
    private static bool KeepSimulatingWithoutPlayers => IsShipScope || scope.BackgroundSimulation;

    private static void NotifySurfaceMechanicalChanged(Vector2Int cell)
    { if (!IsShipScope && worldKey == UnityEngine.SceneManagement.SceneManager.GetActiveScene().name) BuildingOccupancyRegistry.NotifyMechanicalChanged(cell); }
    private static void NotifySurfaceCellChanged(Vector2Int cell)
    { if (!IsShipScope && worldKey == UnityEngine.SceneManagement.SceneManager.GetActiveScene().name) CellChanged?.Invoke(cell); }

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
        => UseScope(node?.ScopeKey ?? UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);

    public static void TransferShipNode(string fromShip, string toShip, int id)
        => TransferShipNodes(fromShip, toShip, new[] { id });

    public static string GetShipScopeKey(string shipId)
    {
        if (string.IsNullOrWhiteSpace(shipId)) throw new ArgumentException("船体身份为空。", nameof(shipId));
        if (!shipScopeKeys.TryGetValue(shipId, out string key))
            shipScopeKeys.Add(shipId, key = "ship:" + shipId);
        return key;
    }

    // 分裂先剥离一批节点的真实库存，整批移交完成后才各重建一次网络。
    public static void TransferShipNodes(string fromShip, string toShip, IReadOnlyList<int> ids)
    {
        if (fromShip == toShip || ids == null || ids.Count == 0) return;
        var candidates = new List<MachineEntity>(ids.Count);
        var transferred = new List<FluidMachineState>(ids.Count);
        var identities = new HashSet<int>();
        using (UseScope(GetShipScopeKey(fromShip)))
        {
            RebuildGraphsIfDirty();
            for (int i = 0; i < ids.Count; i++)
            {
                if (!identities.Add(ids[i])) throw new ArgumentException("批量转移包含重复机器身份。", nameof(ids));
                if (nodes.TryGetValue(ids[i], out MachineEntity node)) candidates.Add(node);
            }
        }
        if (candidates.Count == 0) return;
        using (UseScope(GetShipScopeKey(toShip)))
        {
            RebuildGraphsIfDirty();
            foreach (MachineEntity node in candidates)
                if (nodes.ContainsKey(node.Id)) throw new InvalidOperationException("目标船体已经包含待转移的机器：" + node.Id);
        }
        using (UseScope(GetShipScopeKey(fromShip)))
        {
            foreach (MachineEntity node in candidates) CaptureNode(node);
            ScopeState source = scope;
            source.TopologyMutationDepth++;
            try
            {
                var groups = new HashSet<FluidTankGroup>();
                foreach (MachineEntity node in candidates)
                    if (fluidTankOwners.TryGetValue(node.Id, out FluidTankGroup group)) groups.Add(group);
                foreach (FluidTankGroup group in groups) PartitionFluidGroupForScopeTransfer(group);
                foreach (MachineEntity node in candidates)
                {
                    FluidMachineState contents = null;
                    if (node.Definition.Fluid != null)
                    { DisposeMachineRuntime(node); contents = DetachFluidNodeForScopeTransfer(node); }
                    else if (interactions.Remove(node.Id, out MachineInteractionTarget target)) target.Dispose();
                    transferred.Add(contents);
                    nodes.Remove(node.Id); dirty = true;
                    if (node.Processor != null) processorOwners.Remove(node.Processor);
                }
            }
            finally { source.TopologyMutationDepth--; RebuildGraphsIfDirty(); }
        }
        using (UseScope(GetShipScopeKey(toShip)))
        {
            ScopeState destination = scope;
            destination.TopologyMutationDepth++;
            try
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    MachineEntity node = candidates[i];
                    node.ScopeKey = worldKey;
                    nodes.Add(node.Id, node); dirty = true;
                    if (node.Processor != null) processorOwners[node.Processor] = node;
                    if (transferred[i] == null) continue;
                    ForgetFluidNodeInventoryCaches(node.Id);
                    fluidStates[node.Id] = transferred[i];
                    EnsureProcessor(node);
                }
            }
            finally { destination.TopologyMutationDepth--; RebuildGraphsIfDirty(); }
        }
    }

    // 回收只取该部件的真实份额，其他成员保留剩余库存并重建自己的网络。
    public static ItemData RemoveShipPieceForRecovery(MachineEntity node)
    {
        if (node == null || !FlatWorld.Networking.GameNetwork.HasStateAuthority) return null;
        using (UseNodeScope(node))
        {
            if (!IsShipScope || !nodes.TryGetValue(node.Id, out MachineEntity current) || !ReferenceEquals(node, current)) return null;
            RebuildGraphsIfDirty();
            CaptureNode(node);
            if (fluidTankOwners.TryGetValue(node.Id, out FluidTankGroup group)) PartitionFluidGroupForScopeTransfer(group);
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
