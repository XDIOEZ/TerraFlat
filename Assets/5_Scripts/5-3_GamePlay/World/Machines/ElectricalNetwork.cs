using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

/// <summary>一个连通电网的聚合运行状态；首版按整网功率分配，不做逐支路基尔霍夫解算。</summary>
public sealed class ElectricalNetwork
{
    #region 运行状态
    public readonly List<MachineEntity> Nodes = new();
    public float Voltage;
    public float GeneratedWatts;
    public float DemandWatts;
    public float DeliveredWatts;
    public float ChargingWatts;
    public float DischargingWatts;
    public float WastedWatts;
    public float CurrentAmps;
    public float SafeCurrentAmps;
    public float PowerRatio = 1f;
    public bool VoltageConflict;
    public bool VoltageIncompatible;
    public bool Overloaded;
    public string Status = "待机";
    #endregion
}

/// <summary>电线覆盖层与设备电气端口组成轻量电网，方向端口随设备旋转。</summary>
public sealed class ElectricalNetworkGraph
{
    #region 拓扑
    private static readonly Vector2Int[] Directions =
        { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down };
    private readonly Dictionary<Vector2Int, MachineEntity> wires = new();
    private readonly Dictionary<Vector2Int, List<MachineEntity>> endpoints = new();
    private readonly Dictionary<Vector2Int, MachineEntity> pressureSwitches = new();
    private readonly WorldTopologyDomain topology;
    public readonly List<ElectricalNetwork> Networks = new();
    internal readonly HashSet<MechanicalNetwork> ResolvedMechanicalNetworks = new();
    internal readonly Dictionary<ElectricalNetwork, HashSet<ElectricalNetwork>> ElectricalAncestors = new();
    internal readonly HashSet<ElectricalNetwork> CouplingInputs = new();

    public ElectricalNetworkGraph(WorldTopologyDomain topology) => this.topology = topology;

    private Vector2Int Normalize(Vector2Int cell)
        => new(topology.NormalizeX(cell.x), topology.NormalizeY(cell.y));

    /// <summary>表现只读取同一电网索引，设备连接位置由电气配置决定。</summary>
    public MachineEntity GetWire(Vector2Int cell)
        => wires.TryGetValue(Normalize(cell), out MachineEntity wire) ? wire : null;

    /// <summary>连接位固定为北1、东2、南4、西8，不随放置旋转变化。</summary>
    public int GetWireConnectionMask(Vector2Int cell)
    {
        int mask = 0;
        if (GetWire(cell + Vector2Int.up) != null) mask |= 1;
        if (GetWire(cell + Vector2Int.right) != null) mask |= 2;
        if (GetWire(cell + Vector2Int.down) != null) mask |= 4;
        if (GetWire(cell + Vector2Int.left) != null) mask |= 8;
        if (endpoints.TryGetValue(Normalize(cell), out var localEndpoints))
            foreach (MachineEntity node in localEndpoints)
                for (int direction = 0; direction < 4; direction++)
                    if (Normalize(cell + Directions[direction]) == node.Cell)
                        mask |= direction switch { 0 => 2, 1 => 1, 2 => 8, _ => 4 };
        return mask;
    }

    /// <summary>只有拓扑变化时重建，按同格或旋转后的邻格接入端口。</summary>
    public void Rebuild(IEnumerable<MachineEntity> source)
    {
        wires.Clear(); endpoints.Clear(); pressureSwitches.Clear(); Networks.Clear();
        ResolvedMechanicalNetworks.Clear(); ElectricalAncestors.Clear(); CouplingInputs.Clear();
        var sorted = new List<MachineEntity>(source);
        sorted.Sort((a, b) => a.Id.CompareTo(b.Id));
        foreach (MachineEntity node in sorted)
            if (MachineWorld.GetFluidDeviceBehavior(node)?.SwitchesElectricalPorts == true) pressureSwitches[Normalize(node.Cell)] = node;

        foreach (MachineEntity node in sorted)
        {
            node.ElectricalNetwork = null;
            ElectricalDefinition electrical = node.Definition?.Electrical;
            if (electrical?.HasConnection != true) continue;
            node.Cell = Normalize(node.Cell);
            if (MachineWorld.GetFluidDeviceBehavior(node)?.SwitchesElectricalPorts == true) continue;
            if (electrical.IsWire)
            {
                // 测点格中的普通线不能把探针的两个端点短接。
                if (pressureSwitches.ContainsKey(node.Cell)) continue;
                if (wires.ContainsKey(node.Cell))
                    throw new InvalidOperationException("电线格重复占用：" + node.Cell);
                wires.Add(node.Cell, node);
            }
            else
            {
                Vector2Int connectionCell = Normalize(node.Cell + electrical.GetConnectionOffset(node.RotationQuarterTurns));
                if (!endpoints.TryGetValue(connectionCell, out List<MachineEntity> list))
                    endpoints[connectionCell] = list = new List<MachineEntity>();
                list.Add(node);
            }
        }

        var queue = new Queue<MachineEntity>();
        foreach (MachineEntity root in sorted)
        {
            if (root.Definition?.Electrical?.IsWire != true || root.ElectricalNetwork != null) continue;
            var network = new ElectricalNetwork();
            Networks.Add(network);
            root.ElectricalNetwork = network;
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                MachineEntity wire = queue.Dequeue();
                network.Nodes.Add(wire);
                if (endpoints.TryGetValue(wire.Cell, out List<MachineEntity> localEndpoints))
                {
                    foreach (MachineEntity endpoint in localEndpoints)
                    {
                        if (endpoint.ElectricalNetwork != null) continue;
                        endpoint.ElectricalNetwork = network;
                        network.Nodes.Add(endpoint);
                    }
                }
                for (int portDirection = 0; portDirection < Directions.Length; portDirection++)
                {
                    Vector2Int direction = Directions[portDirection];
                    Vector2Int adjacent = Normalize(wire.Cell + direction);
                    if (pressureSwitches.TryGetValue(adjacent, out MachineEntity pressureSwitch) &&
                        pressureSwitch.FluidProbeConnected && (pressureSwitch.RotationQuarterTurns & 1) == (portDirection & 1))
                    {
                        if (pressureSwitch.ElectricalNetwork == null)
                        { pressureSwitch.ElectricalNetwork = network; network.Nodes.Add(pressureSwitch); }
                        adjacent = Normalize(adjacent + direction);
                    }
                    if (!wires.TryGetValue(adjacent, out MachineEntity next) ||
                        next.ElectricalNetwork != null) continue;
                    next.ElectricalNetwork = network;
                    queue.Enqueue(next);
                }
            }
            network.Nodes.Sort((a, b) => a.Id.CompareTo(b.Id));
        }

        // 没有铺到电线上的电器仍保留孤立网络，方便 UI 明确显示“未接线”。
        foreach (MachineEntity endpoint in sorted)
        {
            if (endpoint.Definition?.Electrical == null || endpoint.Definition.Electrical.IsWire ||
                endpoint.ElectricalNetwork != null) continue;
            var network = new ElectricalNetwork();
            endpoint.ElectricalNetwork = network;
            network.Nodes.Add(endpoint);
            Networks.Add(network);
        }
    }
    #endregion

    #region 功率解算
    private static readonly ProfilerMarker ElectricalSolveMarker = new("MachineWorld.Electrical.Solve");
    /// <summary>发电优先供负载，余电充电、缺口放电；电压参与兼容性，电流由 P/V 推导，电阻首版只保留数据。</summary>
    public static void Solve(ElectricalNetwork network, float seconds,
        Func<MachineEntity, float> generatorFactor,
        Func<MachineEntity, float> demandFactor,
        Action<MachineEntity> batteryChanged, bool commitStorage = true)
    {
        if (network == null) return;
        using var solveSample = ElectricalSolveMarker.Auto();
        seconds = Mathf.Max(0.0001f, seconds);
        Reset(network);
        // 同一次电网解算只采样一次发电条件，电压和功率使用同一份真实出力。
        foreach (MachineEntity node in network.Nodes)
            node.SampledElectricalGeneration = node.IsElectricalGenerator
                ? Mathf.Clamp01(generatorFactor?.Invoke(node) ?? 1f) : 0f;

        float voltage = ResolveVoltage(network, out bool sourceConflict);
        network.Voltage = voltage;
        network.VoltageConflict = sourceConflict;
        foreach (MachineEntity node in network.Nodes) node.ElectricalVoltage = voltage;
        if (sourceConflict)
        {
            network.Status = "电压冲突";
            return;
        }

        int unlimitedSources = 0;
        foreach (MachineEntity node in network.Nodes)
        {
            ElectricalDefinition electrical = node.Definition.Electrical;
            if (node.IsElectricalGenerator)
            {
                if (electrical.UnlimitedGeneration)
                {
                    if (node.SampledElectricalGeneration > 0f && voltage > 0f && electrical.AcceptsVoltage(voltage))
                        unlimitedSources++;
                    continue;
                }
                float factor = node.SampledElectricalGeneration;
                float output = electrical.PowerWatts * factor;
                if (output > 0f && voltage > 0f && electrical.AcceptsVoltage(voltage))
                {
                    node.ElectricalGeneratedWatts = output;
                    network.GeneratedWatts += output;
                }
            }
            else if (node.IsElectricalConsumer)
            {
                float factor = Mathf.Clamp01(demandFactor?.Invoke(node) ?? 1f);
                float request = electrical.PowerWatts * factor;
                node.ElectricalRequestedWatts = request;
                network.DemandWatts += request;
                if (request > 0f && voltage > 0f && !electrical.AcceptsVoltage(voltage))
                    network.VoltageIncompatible = true;
            }
            else if (electrical.IsBattery && voltage > 0f && !electrical.AcceptsVoltage(voltage))
                network.VoltageIncompatible = true;
            else if (electrical.IsWire && voltage > 0f && !electrical.AcceptsVoltage(voltage))
                network.VoltageIncompatible = true;
        }

        if (voltage <= 0f)
        {
            network.PowerRatio = network.DemandWatts > 0f ? 0f : 1f;
            network.Status = network.DemandWatts > 0f ? "无供电" : "待机";
            return;
        }
        if (HasUnsafeWire(network, voltage))
        {
            network.PowerRatio = 0f;
            network.Status = "线路电压不兼容";
            return;
        }

        float compatibleDemand = 0f;
        foreach (MachineEntity node in network.Nodes)
            if (node.IsElectricalConsumer && node.Definition.Electrical.AcceptsVoltage(voltage))
                compatibleDemand += node.ElectricalRequestedWatts;

        if (unlimitedSources > 0)
            SupplyUnlimitedGeneration(network, voltage, compatibleDemand, seconds, unlimitedSources);
        float deficit = Mathf.Max(0f, compatibleDemand - network.GeneratedWatts);
        if (deficit > 0f)
            network.DischargingWatts = DischargeBatteries(network, voltage, deficit, seconds, batteryChanged, commitStorage);

        float available = network.GeneratedWatts + network.DischargingWatts;
        network.DeliveredWatts = Mathf.Min(compatibleDemand, available);
        network.PowerRatio = compatibleDemand > 0f
            ? Mathf.Clamp01(network.DeliveredWatts / compatibleDemand)
            : 1f;

        foreach (MachineEntity node in network.Nodes)
        {
            ElectricalDefinition electrical = node.Definition.Electrical;
            if (!node.IsElectricalConsumer || !electrical.AcceptsVoltage(voltage)) continue;
            node.ElectricalPowerRatio = network.PowerRatio;
            node.ElectricalSuppliedWatts = node.ElectricalRequestedWatts * network.PowerRatio;
            node.ElectricalCurrentAmps = node.ElectricalSuppliedWatts / voltage;
        }

        float surplusGeneration = Mathf.Max(0f, network.GeneratedWatts - network.DeliveredWatts);
        if (surplusGeneration > 0f)
            network.ChargingWatts = ChargeBatteries(network, voltage, surplusGeneration, seconds, batteryChanged, commitStorage);
        network.WastedWatts = Mathf.Max(0f, surplusGeneration - network.ChargingWatts);

        float transmittedWatts = network.DeliveredWatts + network.ChargingWatts;
        network.CurrentAmps = transmittedWatts / voltage;
        network.SafeCurrentAmps = ResolveSafeCurrent(network);
        network.Overloaded = network.SafeCurrentAmps > 0f && network.CurrentAmps > network.SafeCurrentAmps + 0.001f;
        foreach (MachineEntity node in network.Nodes)
            if (node.Definition.Electrical.IsWire)
            {
                node.ElectricalCurrentAmps = network.CurrentAmps;
                node.ElectricalPowerRatio = network.PowerRatio;
            }

        network.Status = network.Overloaded ? "过载" :
            network.VoltageIncompatible ? "部分设备电压不兼容" :
            compatibleDemand > network.DeliveredWatts + 0.001f ? "供电不足" :
            network.GeneratedWatts > 0f || network.DischargingWatts > 0f ? "运行中" : "待机";
    }

    private static void Reset(ElectricalNetwork network)
    {
        network.Voltage = 0f; network.GeneratedWatts = 0f; network.DemandWatts = 0f;
        network.DeliveredWatts = 0f; network.ChargingWatts = 0f; network.DischargingWatts = 0f;
        network.WastedWatts = 0f; network.CurrentAmps = 0f; network.SafeCurrentAmps = 0f;
        network.PowerRatio = 1f; network.VoltageConflict = false; network.VoltageIncompatible = false;
        network.Overloaded = false; network.Status = "待机";
        foreach (MachineEntity node in network.Nodes)
        {
            node.ElectricalVoltage = 0f;
            node.ElectricalRequestedWatts = 0f;
            node.ElectricalSuppliedWatts = 0f;
            node.ElectricalGeneratedWatts = 0f;
            node.ElectricalCurrentAmps = 0f;
            node.ElectricalPowerRatio = 0f;
        }
    }

    private static float ResolveVoltage(ElectricalNetwork network, out bool conflict)
    {
        conflict = false;
        float voltage = 0f;
        foreach (MachineEntity node in network.Nodes)
        {
            ElectricalDefinition electrical = node.Definition.Electrical;
            if (!node.IsElectricalGenerator || (!electrical.UnlimitedGeneration && electrical.PowerWatts <= 0f) ||
                node.SampledElectricalGeneration <= 0f) continue;
            if (!MergeSourceVoltage(ref voltage, electrical.NominalVoltage)) conflict = true;
        }
        if (voltage > 0f)
        {
            foreach (MachineEntity node in network.Nodes)
            {
                ElectricalDefinition electrical = node.Definition.Electrical;
                if (!electrical.IsBattery || node.ElectricalStoredJoules <= 0.001f) continue;
                if (!MergeSourceVoltage(ref voltage, electrical.NominalVoltage)) conflict = true;
            }
            return voltage;
        }
        foreach (MachineEntity node in network.Nodes)
        {
            ElectricalDefinition electrical = node.Definition.Electrical;
            if (!electrical.IsBattery || node.ElectricalStoredJoules <= 0.001f) continue;
            if (!MergeSourceVoltage(ref voltage, electrical.NominalVoltage)) conflict = true;
        }
        return voltage;
    }

    private static bool MergeSourceVoltage(ref float current, float next)
    {
        if (current <= 0f) { current = next; return true; }
        return Mathf.Abs(current - next) <= 0.001f;
    }

    private static bool HasUnsafeWire(ElectricalNetwork network, float voltage)
    {
        foreach (MachineEntity node in network.Nodes)
            if (node.Definition.Electrical.IsWire && !node.Definition.Electrical.AcceptsVoltage(voltage))
                return true;
        return false;
    }

    private static void SupplyUnlimitedGeneration(ElectricalNetwork network, float voltage,
        float demandWatts, float seconds, int sources)
    {
        float chargingDemand = 0f;
        foreach (MachineEntity node in network.Nodes)
        {
            ElectricalDefinition electrical = node.Definition.Electrical;
            if (electrical.IsBattery && electrical.AcceptsVoltage(voltage))
                chargingDemand += GetBatteryChargeWatts(node, seconds);
        }
        // 只补实际功率缺口，多个无限源分担出力，避免无穷数污染电流和储能读数。
        float remaining = Mathf.Max(0f, demandWatts + chargingDemand - network.GeneratedWatts);
        foreach (MachineEntity node in network.Nodes)
        {
            ElectricalDefinition electrical = node.Definition.Electrical;
            if (!node.IsElectricalGenerator || !electrical.UnlimitedGeneration ||
                node.SampledElectricalGeneration <= 0f || !electrical.AcceptsVoltage(voltage)) continue;
            float output = remaining / sources;
            sources--;
            node.ElectricalGeneratedWatts = output;
            node.ElectricalCurrentAmps = output / voltage;
            network.GeneratedWatts += output;
            remaining = Mathf.Max(0f, remaining - output);
        }
    }

    private static float GetBatteryChargeWatts(MachineEntity node, float seconds)
    {
        ElectricalDefinition electrical = node.Definition.Electrical;
        float storedJoules = Mathf.Clamp(node.ElectricalStoredJoules, 0f, electrical.CapacityJoules);
        return Mathf.Min(electrical.MaxChargeWatts, (electrical.CapacityJoules - storedJoules) / seconds);
    }

    private static float DischargeBatteries(ElectricalNetwork network, float voltage, float requestedWatts,
        float seconds, Action<MachineEntity> batteryChanged, bool commitStorage)
    {
        float remaining = requestedWatts, total = 0f;
        foreach (MachineEntity node in network.Nodes)
        {
            ElectricalDefinition electrical = node.Definition.Electrical;
            if (!electrical.IsBattery || !electrical.AcceptsVoltage(voltage) || remaining <= 0f) continue;
            float storedJoules = Mathf.Clamp(node.ElectricalStoredJoules, 0f, electrical.CapacityJoules);
            float watts = Mathf.Min(remaining, Mathf.Min(electrical.MaxDischargeWatts,
                storedJoules / seconds));
            if (watts <= 0f) continue;
            if (commitStorage) node.ElectricalStoredJoules = storedJoules - watts * seconds;
            node.ElectricalGeneratedWatts = watts;
            node.ElectricalCurrentAmps = watts / voltage;
            remaining -= watts; total += watts;
            if (commitStorage) batteryChanged?.Invoke(node);
        }
        return total;
    }

    private static float ChargeBatteries(ElectricalNetwork network, float voltage, float availableWatts,
        float seconds, Action<MachineEntity> batteryChanged, bool commitStorage)
    {
        float remaining = availableWatts, total = 0f;
        foreach (MachineEntity node in network.Nodes)
        {
            ElectricalDefinition electrical = node.Definition.Electrical;
            if (!electrical.IsBattery || !electrical.AcceptsVoltage(voltage) || remaining <= 0f) continue;
            float storedJoules = Mathf.Clamp(node.ElectricalStoredJoules, 0f, electrical.CapacityJoules);
            float watts = Mathf.Min(remaining, GetBatteryChargeWatts(node, seconds));
            if (watts <= 0f) continue;
            if (commitStorage) node.ElectricalStoredJoules = storedJoules + watts * seconds;
            node.ElectricalSuppliedWatts = watts;
            node.ElectricalCurrentAmps = watts / voltage;
            remaining -= watts; total += watts;
            if (commitStorage) batteryChanged?.Invoke(node);
        }
        return total;
    }

    private static float ResolveSafeCurrent(ElectricalNetwork network)
    {
        float limit = float.PositiveInfinity;
        bool hasWire = false;
        foreach (MachineEntity node in network.Nodes)
        {
            ElectricalDefinition electrical = node.Definition.Electrical;
            if (!electrical.IsWire) continue;
            hasWire = true;
            limit = Mathf.Min(limit, electrical.MaxCurrentAmps);
        }
        return hasWire ? limit : 0f;
    }
    #endregion
}
