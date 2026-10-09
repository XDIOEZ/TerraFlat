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
        public bool Dirty, SuppressRemoval;
        public float Elapsed;
        public uint Step;
        public Vector2 CombatPadding = Vector2.one;
        public readonly Dictionary<int, FluidMachineState> Fluids = new();
        public readonly Dictionary<string, FluidInventory> Inventories = new(StringComparer.Ordinal);
        public readonly Dictionary<int, FluidTankGroup> TankOwners = new();
        public readonly List<FluidTankGroup> Tanks = new();
        public readonly Dictionary<FluidInventory, FluidInventoryState> Stock = new();
        public readonly Dictionary<FluidInventory, double> Throughput = new();
        public readonly Dictionary<MachineEntity, double> MechanicalPower = new();
        public readonly Dictionary<int, int> Appearance = new();
    }

    private static ScopeState scope = new();
    private static readonly Dictionary<string, ScopeState> scopes = new(StringComparer.Ordinal);
    private static string scopeOverride;
    public static bool IsShipScope => worldKey != null && worldKey.StartsWith("ship:", StringComparison.Ordinal);

    private static void NotifySurfaceMechanicalChanged(Vector2Int cell)
    { if (!IsShipScope && worldKey == UnityEngine.SceneManagement.SceneManager.GetActiveScene().name) BuildingOccupancyRegistry.NotifyMechanicalChanged(cell); }
    private static void NotifySurfaceCellChanged(Vector2Int cell)
    { if (!IsShipScope && worldKey == UnityEngine.SceneManagement.SceneManager.GetActiveScene().name) CellChanged?.Invoke(cell); }

    public static void ReleaseShipScope(string shipId, bool capture = false)
    {
        string key = "ship:" + shipId;
        if (!scopes.TryGetValue(key, out ScopeState value)) return;
        ScopeState previous = scope;
        scope = value;
        if (capture) CaptureCurrentWorld();
        ReleaseRuntime();
        scopes.Remove(key);
        if (!capture) value.Owner?.Mechanical?.Worlds.Remove(key);
        scope = ReferenceEquals(previous, value) ? new ScopeState() : previous;
        ReleaseCombatBridgeIfNoScopes();
    }

    // 作用域只切换运行态引用，不休眠、复制库存或重建正在加工的设备。
    public static IDisposable UseScope(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("机器作用域为空。", nameof(key));
        var lease = new ScopeLease(scopeOverride);
        scopeOverride = key;
        EnsureScope();
        return lease;
    }
    private sealed class ScopeLease : IDisposable
    {
        private readonly string previous;
        private bool disposed;
        public ScopeLease(string value) => previous = value;
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
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
        scope = new ScopeState();
        scopeOverride = null;
        ReleaseCombatBridgeIfNoScopes();
    }

    public static void TickShip(string shipId, float seconds)
    {
        using (UseScope("ship:" + shipId)) Tick(seconds);
    }

    public static MachineEntity PlaceOnShip(string shipId, ItemData snapshot)
    {
        using (UseScope("ship:" + shipId)) return Place(snapshot);
    }

    public static ItemData RemoveFromShip(string shipId, int id)
    {
        using (UseScope("ship:" + shipId)) return Remove(id);
    }
    public static IDisposable UseNodeScope(MachineEntity node)
        => UseScope(node?.ScopeKey ?? UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);

    public static void TransferShipNode(string fromShip, string toShip, int id)
    {
        if (fromShip == toShip) return;
        MachineEntity node;
        FluidMachineState transferredFluid = null;
        using (UseScope("ship:" + toShip))
        {
            if (nodes.ContainsKey(id)) throw new InvalidOperationException("目标船体已经包含待转移的机器：" + id);
            RebuildGraphsIfDirty();
        }
        using (UseScope("ship:" + fromShip))
        {
            if (!nodes.TryGetValue(id, out node)) return;
            RebuildGraphsIfDirty();
            CaptureNode(node);
            if (node.Definition.Fluid != null)
            {
                // 旧作用域先释放来源租约，再按真实容积剥离共享库存。
                DisposeMachineRuntime(node);
                transferredFluid = DetachFluidNodeForScopeTransfer(node);
            }
            else if (interactions.Remove(id, out MachineInteractionTarget target)) target.Dispose();
            nodes.Remove(id); dirty = true;
            if (node.Processor != null) processorOwners.Remove(node.Processor);
            RebuildGraphsIfDirty();
        }
        using (UseScope("ship:" + toShip))
        {
            node.ScopeKey = worldKey;
            nodes.Add(id, node); dirty = true;
            if (node.Processor != null) processorOwners[node.Processor] = node;
            if (transferredFluid != null)
            {
                ForgetFluidNodeInventoryCaches(id);
                fluidStates[id] = transferredFluid;
                EnsureProcessor(node);
            }
            RebuildGraphsIfDirty();
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
