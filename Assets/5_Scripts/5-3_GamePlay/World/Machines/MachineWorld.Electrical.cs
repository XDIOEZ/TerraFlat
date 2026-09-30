using System;
using System.Collections.Generic;
using UnityEngine;

public static partial class MachineWorld
{
    #region 电网与扩展
    private static ElectricalNetworkGraph electricalGraph;
    private static readonly Dictionary<string, Func<MachineEntity, float>> electricalPowerProviders =
        new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Func<MachineEntity, float>> electricalDemandProviders =
        new(StringComparer.Ordinal);

    public static IReadOnlyList<ElectricalNetwork> ElectricalNetworks => electricalGraph?.Networks;

    /// <summary>MOD 可以给发电机提供 0..1 的动态出力比例。</summary>
    public static void RegisterElectricalPowerProvider(string id, Func<MachineEntity, float> provider)
    {
        if (string.IsNullOrWhiteSpace(id) || provider == null) throw new ArgumentException("电力来源注册无效。");
        electricalPowerProviders[id] = provider;
    }

    /// <summary>MOD 可以给用电器提供 0..1 的动态需求比例。</summary>
    public static void RegisterElectricalDemandProvider(string id, Func<MachineEntity, float> provider)
    {
        if (string.IsNullOrWhiteSpace(id) || provider == null) throw new ArgumentException("用电需求注册无效。");
        electricalDemandProviders[id] = provider;
    }

    internal static void ClearElectricalProviders()
    {
        electricalPowerProviders.Clear();
        electricalDemandProviders.Clear();
    }

    private static void ResetElectricalRuntime()
    {
        electricalGraph = null;
        ClearElectricalProviders();
    }

    private static void InitializeElectricalScope(WorldTopologyDomain topology)
        => electricalGraph = new ElectricalNetworkGraph(topology);

    /// <summary>机械与电力共用同一批世界节点，拓扑变化时两张图必须在同一时刻重建。</summary>
    private static void RebuildGraphsIfDirty()
    {
        if (!dirty) return;
        graph.Rebuild(nodes.Values);
        electricalGraph?.Rebuild(nodes.Values);
        dirty = false;
    }

    /// <summary>首版整网电力解算；机械侧先求发电机输入，电气侧再决定马达可输出的比例。</summary>
    private static void SolveElectricalNetworks(float seconds)
    {
        if (electricalGraph == null) return;
        foreach (ElectricalNetwork network in electricalGraph.Networks)
            ElectricalNetworkGraph.Solve(network, seconds,
                GetElectricalGeneratorFactor, GetElectricalDemandFactor, ElectricalBatteryChanged);
    }

    /// <summary>发电机和马达跨越机械网与电网，首版保持其机械网全局求解，避免远端输电因休眠断能。</summary>
    private static bool RequiresElectricalBridgeSimulation(MechanicalNetwork network)
    {
        if (network == null) return false;
        foreach (MachineEntity node in network.Nodes)
        {
            ElectricalDefinition electrical = node.Definition?.Electrical;
            if (electrical == null || node.ElectricalNetwork?.Nodes.Count <= 1) continue;
            if (electrical.PowerProvider == "mechanical" || electrical.DemandProvider == "motor") return true;
        }
        return false;
    }

    private static float GetElectricalGeneratorFactor(MachineEntity node)
    {
        ElectricalDefinition electrical = node?.Definition?.Electrical;
        if (electrical == null) return 0f;
        if (electricalPowerProviders.TryGetValue(electrical.PowerProvider ?? string.Empty, out var provider))
            return Mathf.Clamp01(provider(node));
        if (electrical.PowerProvider == "mechanical")
            return node.Definition.TorqueLoad > 0f && node.LoadSatisfied
                ? Mathf.Clamp01(GetWorkEfficiency(node))
                : 0f;
        if (!string.IsNullOrWhiteSpace(electrical.PowerProvider)) return 0f;
        return node.State != null ? 1f : 0f;
    }

    private static float GetElectricalDemandFactor(MachineEntity node)
    {
        ElectricalDefinition electrical = node?.Definition?.Electrical;
        if (electrical == null) return 0f;
        if (electricalDemandProviders.TryGetValue(electrical.DemandProvider ?? string.Empty, out var provider))
            return Mathf.Clamp01(provider(node));
        if (electrical.DemandProvider == "motor")
            return node.Network != null && node.Network.TorqueDemand > 0f ? 1f : 0f;
        if (!string.IsNullOrWhiteSpace(electrical.DemandProvider)) return 0f;
        return node.State != null ? 1f : 0f;
    }

    private static void ElectricalBatteryChanged(MachineEntity node)
    {
        if (!Contains(node)) return;
        if (node.State != null) node.State.ElectricalStoredJoules = node.ElectricalStoredJoules;
        StateChanged(node);
    }

    /// <summary>从持久状态初始化轻量电池值；电线、发电机和用电器保持零储能。</summary>
    private static void InitializeElectricalState(MachineEntity node, MachineState state)
    {
        if (node?.Definition?.Electrical?.IsBattery != true)
        {
            if (node != null) node.ElectricalStoredJoules = 0f;
            return;
        }
        float capacity = node.Definition.Electrical.CapacityJoules;
        node.ElectricalStoredJoules = Mathf.Clamp(state?.ElectricalStoredJoules ?? 0f, 0f, capacity);
    }

    /// <summary>轻量电池状态在休眠时仍由节点保存，写快照前回填到机器状态。</summary>
    private static void WriteElectricalState(MachineEntity node, MachineState state)
    {
        if (node?.Definition?.Electrical?.IsBattery == true && state != null)
            state.ElectricalStoredJoules = Mathf.Clamp(node.ElectricalStoredJoules, 0f,
                node.Definition.Electrical.CapacityJoules);
    }

    public static ElectricalNetwork GetElectricalNetwork(MachineEntity node)
    {
        EnsureScope();
        RebuildGraphsIfDirty();
        return node != null && Contains(node) ? node.ElectricalNetwork : null;
    }
    #endregion
}
