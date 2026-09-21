using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using MemoryPack;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>由 ItemMgr 调度的机械世界。权威模拟独立于地图表现；只有拓扑变化重构图，整网先恢复再 Tick，休眠时保存库存并释放加工器。</summary>
public static class MechanicalWorld
{
    #region 会话与扩展
    private static readonly Dictionary<int, MechanicalNode> nodes = new();
    private static readonly Dictionary<string, Func<MechanicalNode, float>> sourceProviders = new(StringComparer.Ordinal);
    private static readonly List<Vector2Int> players = new();
    private static readonly List<MechanicalNode> presentation = new();
    private static MechanicalNetworkGraph graph;
    private static GameSaveData owner;
    private static string worldKey;
    private static bool dirty;
    private static bool suppressRemoval;
    private static float elapsed;
    private static float viewElapsed;
    public static IReadOnlyList<MechanicalNetwork> Networks => graph?.Networks;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        nodes.Clear(); sourceProviders.Clear(); players.Clear(); presentation.Clear();
        graph = null; owner = null; worldKey = null; dirty = false; suppressRemoval = false; elapsed = viewElapsed = 0;
    }

    /// <summary>MOD 动力条件返回 0..1 可用比例，条件必须独立于 GameObject 与 ChunkView。</summary>
    public static void RegisterSource(string id, Func<MechanicalNode, float> provider)
    {
        if (string.IsNullOrWhiteSpace(id) || provider == null) throw new ArgumentException("动力来源注册无效。");
        sourceProviders[id] = provider;
    }
    public static void ClearSourceProviders() => sourceProviders.Clear();

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
        nodes.Add(snapshot.Guid, new MechanicalNode
        {
            Id = snapshot.Guid, Cell = CellOf(snapshot.transform.position), Definition = definition, Snapshot = snapshot,
            Vertical = state.Vertical, Engaged = state.Engaged, RatioIndex = state.RatioIndex
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

    public static void TopologyChanged(MechanicalNode node)
    {
        if (node == null) return;
        CopyTopology(node); dirty = true;
    }
    private static void CopyTopology(MechanicalNode node)
    { node.Vertical = node.State.Vertical; node.Engaged = node.State.Engaged; node.RatioIndex = node.State.RatioIndex; }

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
        elapsed += Mathf.Max(0, deltaTime); viewElapsed += Mathf.Max(0, deltaTime);
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
            MechanicalNetworkGraph.Solve(network, GetSourceFactor, MechanicalCatalog.Settings.ReferenceRpm);
            foreach (var node in network.Nodes) SimulateNode(node, step);
        }
        if (viewElapsed >= .5f) { viewElapsed = 0; RefreshPresentation(); }
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
        EnsureProcessor(node);
    }
    private static void EnsureProcessor(MechanicalNode node)
    {
        if (node.Processor == null && !string.IsNullOrWhiteSpace(node.Definition.Station))
            node.Processor = new MechanicalProcessor(node.Definition.Station, node.State.Processing);
    }
    private static void SleepNetwork(MechanicalNetwork network)
    {
        // 先保存全部节点，成功后统一释放。中途失败不形成部分运行网络。
        foreach (var node in network.Nodes) CaptureNode(node);
        foreach (var node in network.Nodes)
        {
            RemoveView(node);
            node.Processor?.Dispose(); node.Processor = null; node.State = null; node.Rpm = 0;
        }
        network.Active = false; network.Status = "休眠";
    }

    private static void SimulateNode(MechanicalNode node, float step)
    {
        if (node.State == null) return;
        if (node.Definition.Source == "manual")
            node.State.ManualSeconds = Mathf.Max(0, node.State.ManualSeconds - step);
        if (node.Rpm > 0)
            node.Processor?.Advance(step * node.Rpm / MechanicalCatalog.Settings.ReferenceRpm);
        node.View?.RefreshPanel();
    }

    private static float GetSourceFactor(MechanicalNode node)
    {
        if (node.State == null || node.Definition.Power <= 0) return 0;
        string source = node.Definition.Source;
        if (sourceProviders.TryGetValue(source, out var provider)) return Mathf.Clamp01(provider(node));
        if (source == "manual") return node.State.ManualSeconds > 0 ? 1 : 0;
        if (source == "wind") return WeatherMgr.Instance != null ? WeatherMgr.Instance.GetCurrentWindStrength() : 0;
        // 首版水车为沿岸水力：安装时记录水体资格；不消耗水，不依赖远端 ChunkView。
        if (source == "water") return node.State.WaterSupported ? 1 : 0;
        return 0;
    }
    #endregion

    #region 表现与占地
    private static void RefreshPresentation()
    {
        if (ItemMgr.Instance == null || ChunkMgr.ExistingInstance == null) return;
        presentation.Clear(); presentation.AddRange(nodes.Values);
        foreach (var node in presentation)
        {
            Vector2 center = new(node.Cell.x + .5f, node.Cell.y + .5f);
            bool visible = node.Network?.Active == true && ChunkMgr.ExistingInstance.TryGetRuntimeChunkView(center, out _);
            if (!visible) { if (node.View != null) { CaptureNode(node); RemoveView(node); } continue; }
            if (node.View != null) continue;
            CaptureNode(node);
            var data = ItemDefinitionRuntime.RebasePersistedData(GameRes.Instance, FastCloner.FastCloner.DeepClone(node.Snapshot));
            Item view = null;
            try
            {
                view = ItemMgr.Instance.InstantiateItem(data, data.transform.position, Quaternion.identity, Vector3.one);
                view.Load();
            }
            catch
            {
                suppressRemoval = true;
                try { if (view != null) ItemMgr.Instance.DespawnItem(view, false); }
                finally { suppressRemoval = false; }
                throw;
            }
        }
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
        if (definition.Kind == "bridge")
        {
            int a = vertical ? 1 : 0;
            var first = graph.At(cell + MechanicalNetworkGraph.Directions[a], 0);
            var second = graph.At(cell + MechanicalNetworkGraph.Directions[a + 2], 0);
            if (first == null || second == null || !first.HasPort(a + 2) || !second.HasPort(a))
            { reason = "跨轴器两端需要朝向匹配的下层机械接口"; return false; }
        }
        if (definition.Source == "water" && !HasWaterNeighbor(cell))
        { reason = "水车需要放在水边"; return false; }
        return true;
    }

    public static bool HasWaterNeighbor(Vector2Int cell)
    {
        var manager = ChunkMgr.ExistingInstance;
        if (manager == null) return false;
        foreach (var offset in MechanicalNetworkGraph.Directions)
        {
            Vector2Int adjacent = WorldTopologyRuntime.NormalizeCell(cell + offset);
            if (manager.TryGetRuntimeTerrainTile(new Vector2(adjacent.x + .5f, adjacent.y + .5f), out var sample) &&
                sample.LiquidDepth > 0f) return true;
        }
        return false;
    }

    /// <summary>机械风箱只增强邻格正在燃烧的熔炉；无动力立即归零，多个风箱取最强值。</summary>
    public static float GetBellowsBoost(Vector3 position)
    {
        if (graph == null) return 0;
        Vector2Int cell = CellOf(position);
        float boost = 0;
        foreach (var offset in MechanicalNetworkGraph.Directions)
        {
            var node = graph.At(cell + offset, 0);
            if (node?.Definition.Kind == "bellows" && node.Network?.Active == true)
                boost = Mathf.Max(boost, Mathf.Clamp01(node.Rpm / MechanicalCatalog.Settings.ReferenceRpm));
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
        foreach (var node in nodes.Values) { node.Processor?.Dispose(); }
        nodes.Clear(); elapsed = viewElapsed = 0;
    }
    #endregion
}
