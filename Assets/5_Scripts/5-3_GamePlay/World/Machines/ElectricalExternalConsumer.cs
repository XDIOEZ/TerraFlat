using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>移动设备通过临时负载取电，探算不交付能量，正式电网结算才交付焦耳。</summary>
public sealed class ElectricalExternalConsumer : IDisposable
{
    #region 临时用电租约
    internal readonly ElectricalNetworkGraph Graph;
    internal readonly MachineEntity Wire;
    private Func<float, float> requestWatts;
    private Action<float> receiveJoules;
    internal float RequestedWatts;
    internal float SuppliedWatts;
    public float LastSuppliedWatts { get; private set; }
    public bool IsDisposed { get; private set; }
    public bool IsConnected => !IsDisposed && ReferenceEquals(Graph.GetWire(Wire.Cell), Wire);

    internal ElectricalExternalConsumer(ElectricalNetworkGraph graph, MachineEntity wire,
        Func<float, float> request, Action<float> receive)
    { Graph = graph; Wire = wire; requestWatts = request; receiveJoules = receive; }

    internal void Prepare(float seconds)
    {
        float requested = IsConnected ? requestWatts(seconds) : 0f;
        RequestedWatts = float.IsFinite(requested) ? Mathf.Max(0f, requested) : 0f;
        SuppliedWatts = 0f;
        LastSuppliedWatts = 0f;
    }

    internal void CommitSuppliedEnergy(float seconds)
    {
        LastSuppliedWatts = IsConnected ? SuppliedWatts : 0f;
        if (LastSuppliedWatts > 0f) receiveJoules(LastSuppliedWatts * Mathf.Max(0f, seconds));
    }

    public void Dispose()
    { IsDisposed = true; RequestedWatts = SuppliedWatts = LastSuppliedWatts = 0f; requestWatts = null; receiveJoules = null; }
    #endregion
}

public sealed partial class ElectricalNetworkGraph
{
    #region 外部设备接入
    private readonly List<ElectricalExternalConsumer> externalConsumers = new();

    public ElectricalExternalConsumer RegisterExternalConsumer(MachineEntity wire,
        Func<float, float> requestWatts, Action<float> receiveJoules)
    {
        if (wire == null || !ReferenceEquals(GetWire(wire.Cell), wire) || requestWatts == null || receiveJoules == null)
            throw new ArgumentException("临时用电设备必须连接当前电网中的真实电线。");
        var consumer = new ElectricalExternalConsumer(this, wire, requestWatts, receiveJoules);
        externalConsumers.Add(consumer);
        return consumer;
    }

    // 同一轮机械/电力耦合探算冻结需求，充电回调不能影响同轮后续探算。
    internal void PrepareExternalConsumers(float seconds)
    {
        foreach (ElectricalNetwork network in Networks) network.ExternalConsumers.Clear();
        for (int i = externalConsumers.Count - 1; i >= 0; i--)
        {
            ElectricalExternalConsumer consumer = externalConsumers[i];
            if (consumer.IsDisposed) { externalConsumers.RemoveAt(i); continue; }
            consumer.Prepare(seconds);
            if (consumer.IsConnected && consumer.Wire.ElectricalNetwork != null)
                consumer.Wire.ElectricalNetwork.ExternalConsumers.Add(consumer);
        }
    }
    #endregion
}

public static partial class MachineWorld
{
    #region 临时电线供电接口
    public static ElectricalExternalConsumer ConnectExternalElectricalConsumer(MachineEntity wire,
        Func<float, float> requestWatts, Action<float> receiveJoules)
    {
        RebuildGraphsIfDirty();
        return electricalGraph?.RegisterExternalConsumer(wire, requestWatts, receiveJoules);
    }

    public static bool IsExternalElectricalConsumerConnected(ElectricalExternalConsumer consumer)
        => consumer != null && ReferenceEquals(consumer.Graph, electricalGraph) && consumer.IsConnected;

    public static bool IsWireEnergized(MachineEntity wire)
    {
        if (wire?.Definition?.Electrical?.IsWire != true || !ReferenceEquals(GetById(wire.Id), wire)) return false;
        ElectricalNetwork network = wire.ElectricalNetwork;
        return network != null && network.Voltage > 0f && !network.VoltageConflict && network.PowerRatio > 0f;
    }
    #endregion
}
