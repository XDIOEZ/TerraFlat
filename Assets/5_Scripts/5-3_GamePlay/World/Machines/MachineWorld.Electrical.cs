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
        RebuildCombatSearchPadding();
        dirty = false;
    }

    /// <summary>从独立动力向外确定转换方向，已经过的电网不能再作为该条动力链的发电目标。</summary>
    private static void SolveElectricalNetworks(float seconds)
    {
        if (electricalGraph == null) return;
        bool hasConverters = false;
        foreach (MechanicalNetwork network in graph.Networks) hasConverters |= network.HasConverters;
        if (!hasConverters)
        {
            foreach (MechanicalNetwork network in graph.Networks)
                if (network.Active) SolveNetwork(network);
            SettleElectricalNetworks(seconds, true);
            return;
        }
        var resolved = new HashSet<MechanicalNetwork>();
        var ancestry = new Dictionary<ElectricalNetwork, HashSet<ElectricalNetwork>>();
        foreach (MachineEntity node in nodes.Values)
        {
            node.ConversionMode = ElectricalConversionMode.Idle;
            node.ElectricalPowerRatio = 0f;
            node.ElectricalSuppliedWatts = 0f;
        }
        foreach (MechanicalNetwork network in graph.Networks)
        {
            if (!network.Active) continue;
            SolveNetwork(network, true);
            bool independent = false;
            foreach (MachineEntity node in network.Nodes) independent |= node.SourceFactor > 0f;
            foreach (MachineEntity node in network.Nodes)
                if (node.Definition.IsConverter)
                    node.ConversionMode = independent ? ElectricalConversionMode.Generator : ElectricalConversionMode.Motor;
            if (!independent) continue;
            resolved.Add(network);
            SolveNetwork(network, true);
        }

        int depth = 0;
        for (; depth <= graph.Networks.Count; depth++)
        {
            SettleElectricalNetworks(seconds, false);
            bool advanced = false;
            foreach (MechanicalNetwork network in graph.Networks)
            {
                if (!network.Active || !network.HasConverters || resolved.Contains(network)) continue;
                var inputs = new HashSet<ElectricalNetwork>();
                foreach (MachineEntity node in network.Nodes)
                    if (node.Definition.IsConverter && node.ElectricalSuppliedWatts > 0f)
                    {
                        inputs.Add(node.ElectricalNetwork);
                        if (ancestry.TryGetValue(node.ElectricalNetwork, out var parents)) inputs.UnionWith(parents);
                    }
                if (inputs.Count == 0) continue;
                foreach (MachineEntity node in network.Nodes)
                {
                    if (!node.Definition.IsConverter || node.ElectricalSuppliedWatts > 0f) continue;
                    node.ConversionMode = inputs.Contains(node.ElectricalNetwork)
                        ? ElectricalConversionMode.Idle : ElectricalConversionMode.Generator;
                    if (node.ConversionMode != ElectricalConversionMode.Generator) continue;
                    if (!ancestry.TryGetValue(node.ElectricalNetwork, out var parents))
                        ancestry[node.ElectricalNetwork] = parents = new HashSet<ElectricalNetwork>();
                    parents.UnionWith(inputs);
                }
                resolved.Add(network);
                SolveNetwork(network);
                advanced = true;
            }
            if (!advanced) break;
        }

        // 方向固定后沿链传播实际功率；探算不写电池，正式储能结算每轮只执行一次。
        for (int pass = 0; pass <= depth; pass++)
        {
            SettleElectricalNetworks(seconds, false);
            foreach (MechanicalNetwork network in graph.Networks)
                if (network.Active) SolveNetwork(network);
        }
        SettleElectricalNetworks(seconds, true);
    }

    private static void SettleElectricalNetworks(float seconds, bool commitStorage)
    {
        foreach (ElectricalNetwork network in electricalGraph.Networks)
            ElectricalNetworkGraph.Solve(network, seconds,
                GetElectricalGeneratorFactor, GetElectricalDemandFactor, ElectricalBatteryChanged, commitStorage);
    }

    /// <summary>发电机和马达跨越机械网与电网，首版保持其机械网全局求解，避免远端输电因休眠断能。</summary>
    private static bool RequiresElectricalBridgeSimulation(MechanicalNetwork network)
    {
        if (network == null) return false;
        foreach (MachineEntity node in network.Nodes)
        {
            ElectricalDefinition electrical = node.Definition?.Electrical;
            if (electrical == null || node.ElectricalNetwork?.Nodes.Count <= 1) continue;
            if (electrical.IsConverter || electrical.PowerProvider == "mechanical" || electrical.DemandProvider == "motor") return true;
        }
        return false;
    }

    private static float GetElectricalGeneratorFactor(MachineEntity node)
    {
        ElectricalDefinition electrical = node?.Definition?.Electrical;
        if (electrical == null) return 0f;
        if (electrical.IsConverter)
            return node.ConversionMode == ElectricalConversionMode.Generator && node.LoadSatisfied
                ? Mathf.Clamp01(MechanicalNetworkGraph.RoundLoadTorqueToTens(node.LoadTorque) * node.SpeedRpm *
                    MachineCatalog.Settings.WattsPerTorqueRpm * electrical.ConversionEfficiency / electrical.PowerWatts)
                    * (node.Network?.ConversionGenerationFactor ?? 0f)
                : 0f;
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
        if (electrical.IsConverter)
        {
            if (node.ConversionMode != ElectricalConversionMode.Motor || !ConverterHasWork(node)) return 0f;
            return Mathf.Clamp01(node.SourceTorque * node.Definition.Rpm * MachineCatalog.Settings.WattsPerTorqueRpm /
                electrical.ConversionEfficiency / electrical.PowerWatts);
        }
        if (electricalDemandProviders.TryGetValue(electrical.DemandProvider ?? string.Empty, out var provider))
            return Mathf.Clamp01(provider(node));
        if (electrical.DemandProvider == "motor")
            return node.Network != null && node.Network.TorqueDemand > 0f ? 1f : 0f;
        if (!string.IsNullOrWhiteSpace(electrical.DemandProvider)) return 0f;
        return node.State != null ? 1f : 0f;
    }

    private static bool ConverterHasWork(MachineEntity motor)
    {
        // 纯齿轮/传动轴没有固定扭矩负载，但只要轴端已接上传动件，电机就应建立转速并带动它们空转。
        return motor?.Network != null && motor.Links.Count > 0;
    }

    private static void UpdateConversionPowerBudget(MechanicalNetwork network)
    {
        if (!network.HasConverters) return;
        float inputs = 0f, otherLoads = 0f, generation = 0f;
        float wattsScale = MachineCatalog.Settings.WattsPerTorqueRpm;
        foreach (MachineEntity node in network.Nodes)
        {
            if (node.SourceFactor > 0f && node.SpeedRpm > 0f)
            {
                float torque = node.Definition.IsConverter
                    ? Mathf.Floor((node.SourceTorque * node.SourceFactor + .0001f) / 10f) * 10f
                    : MechanicalNetworkGraph.RoundTorqueToTens(node.SourceTorque * node.SourceFactor);
                inputs += torque * Mathf.Abs(node.SourceRpm) * wattsScale;
            }
            if (node.LoadTorque <= 0f || !node.LoadSatisfied || node.SpeedRpm <= 0f) continue;
            float work = MechanicalNetworkGraph.RoundLoadTorqueToTens(node.LoadTorque) * node.SpeedRpm * wattsScale;
            if (node.Definition.IsConverter)
                generation += Mathf.Min(node.Definition.Electrical.PowerWatts,
                    work * node.Definition.Electrical.ConversionEfficiency);
            else otherLoads += work;
        }
        network.ConversionGenerationFactor = generation > 0f
            ? Mathf.Clamp01(Mathf.Max(0f, inputs - otherLoads) / generation) : 0f;
    }

    private static float GetConverterMotorFactor(MachineEntity node)
    {
        if (node.ConversionMode != ElectricalConversionMode.Motor || node.ElectricalSuppliedWatts <= 0f) return 0f;
        float ratedWatts = node.SourceTorque * node.Definition.Rpm * MachineCatalog.Settings.WattsPerTorqueRpm;
        return ratedWatts > 0f ? Mathf.Clamp01(node.ElectricalSuppliedWatts *
            node.Definition.Electrical.ConversionEfficiency / ratedWatts) : 0f;
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

    /// <summary>区块表现只查询已建立的电线格索引，不创建世界作用域。</summary>
    public static MachineEntity GetElectricalWireAtCurrentWorld(Vector2Int cell)
    {
        if (graph == null) return null;
        RebuildGraphsIfDirty();
        return electricalGraph?.GetWire(cell);
    }

    /// <summary>用权威电网的四邻连接选择贴图，不读取可见区块或碰撞体。</summary>
    public static int GetElectricalWireConnectionMask(Vector2Int cell)
    {
        if (graph == null) return 0;
        RebuildGraphsIfDirty();
        return electricalGraph?.GetWireConnectionMask(cell) ?? 0;
    }
    #endregion
}
