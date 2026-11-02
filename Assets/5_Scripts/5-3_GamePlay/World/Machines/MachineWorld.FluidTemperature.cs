using System;
using FlatWorld.Networking;
using UnityEngine;

public static partial class MachineWorld
{
    #region 液网统一液温与实际液量查询
    public static bool TryGetLiquidNetworkTemperatureCelsius(MachineEntity node, out float temperature, string chamber = "main")
    {
        temperature = 0f;
        if (node?.Definition.Fluid == null) return false;
        using var nodeScope = UseNodeScope(node);
        RebuildGraphsIfDirty();
        FluidNetworkGraph.LiquidTemperatureComponent component = FindLiquidTemperatureComponent(node, chamber);
        if (component == null) return false;
        RefreshLiquidTemperatureComponent(component, false);
        if (!component.Temperature.HasLiquid) return false;
        temperature = (float)(component.Temperature.TemperatureKelvin - 273.15d);
        return true;
    }

    public static double GetLiquidNetworkContainedLiters(MachineEntity node, string chamber = "main")
    {
        if (node?.Definition.Fluid == null) return 0d;
        using var nodeScope = UseNodeScope(node);
        RebuildGraphsIfDirty();
        var component = FindLiquidTemperatureComponent(node, chamber);
        if (component == null) return 0d;
        RefreshLiquidTemperatureComponent(component, false);
        return component.Temperature.TotalLiquidLiters;
    }

    private static FluidNetworkGraph.LiquidTemperatureComponent FindLiquidTemperatureComponent(MachineEntity node, string chamber)
    {
        if (fluidGraph == null || !IsLiquidTemperatureChamber(node, chamber)) return null;
        fluidGraph.PrepareLiquidTemperatureInventories();
        FluidInventory inventory = GetFluidInventory(node, chamber);
        foreach (FluidComponentState liquid in inventory.State.Components)
            if (liquid.LiquidMoles > 0m) return fluidGraph.GetLiquidTemperatureComponent(node, chamber, liquid.FluidId);
        var members = fluidGraph.GetLiquidComponent(node, chamber);
        if (members == null) return null;
        foreach (var member in members)
        {
            if (GetFluidDeviceBehavior(member.Node).IsTransit) continue;
            foreach (FluidComponentState liquid in GetFluidInventory(member.Node, member.Chamber).State.Components)
            {
                if (liquid.LiquidMoles <= 0m) continue;
                var component = fluidGraph.GetLiquidTemperatureComponent(member.Node, member.Chamber, liquid.FluidId);
                foreach (var candidate in component.Members)
                    if (candidate.Node == node && candidate.Chamber == chamber) return component;
            }
        }
        return null;
    }

    private static bool IsLiquidTemperatureChamber(MachineEntity node, string chamber)
    {
        FluidMachineDefinition definition = node.Definition.Fluid;
        foreach (FluidMachinePortDefinition port in definition.Ports)
            if (port.Phase != "gas" && port.Chamber == chamber) return true;
        return !string.IsNullOrEmpty(definition.LiquidResidueFluidId) && definition.LiquidResidueChamber == chamber;
    }

    private static void RefreshLiquidTemperatureComponent(FluidNetworkGraph.LiquidTemperatureComponent component, bool synchronize = true)
    {
        if (scope.SynchronizingLiquidTemperature) return;
        bool previous = scope.SynchronizingLiquidTemperature;
        scope.SynchronizingLiquidTemperature = true;
        try
        {
            if (GameNetwork.HasStateAuthority)
            {
                if (synchronize)
                {
                    component.Temperature.RefreshContents();
                    PublishLiquidTemperatureChanges(component);
                }
                else component.Temperature.ReadTemperatureAndVolume();
            }
            else
            {
                ulong sequence = 0;
                double temperature = 0d;
                foreach (var member in component.Members)
                    if (scope.RemoteLiquidTemperatures.TryGetValue((member.Node.Id, member.Chamber), out var incoming) && incoming.Sequence > sequence)
                    { sequence = incoming.Sequence; temperature = incoming.Kelvin; }
                if (sequence > 0) component.Temperature.MirrorAuthoritativeTemperature(temperature);
                else component.Temperature.RefreshContents(false);
            }
        }
        finally { scope.SynchronizingLiquidTemperature = previous; }
    }

    private static void PublishLiquidTemperatureChanges(FluidNetworkGraph.LiquidTemperatureComponent component)
    {
        foreach (var member in component.Members)
            if (Contains(member.Node) && !GetFluidDeviceBehavior(member.Node).IsTransit &&
                component.Temperature.ChangedInventoryTemperature(GetFluidInventory(member.Node, member.Chamber)))
                StateChanged(member.Node);
    }

    private static void SynchronizeNodeLiquidNetworkTemperature(MachineEntity node)
    {
        if (scope.SynchronizingLiquidTemperature || node.Definition.Fluid == null ||
            GetFluidDeviceBehavior(node).IsTransit || fluidGraph == null) return;
        if (!Array.Exists(node.Definition.Fluid.Ports, port => port.Phase != "gas") &&
            string.IsNullOrEmpty(node.Definition.Fluid.LiquidResidueFluidId)) return;
        fluidGraph.PrepareLiquidTemperatureInventories();
        foreach (string chamber in GetFluidState(node).Chambers.Keys)
        {
            var component = FindLiquidTemperatureComponent(node, chamber);
            if (component != null) RefreshLiquidTemperatureComponent(component);
        }
    }

    internal static void SynchronizeLiquidNetworkTemperatureForTransfer(MachineEntity node, string chamber)
    {
        using var nodeScope = UseNodeScope(node);
        RebuildGraphsIfDirty();
        var component = FindLiquidTemperatureComponent(node, chamber);
        if (component != null) RefreshLiquidTemperatureComponent(component);
    }
    #endregion

    #region 每个管道步集中交换动态网格温度
    private static void CollectLiquidTemperatureComponents()
    {
        scope.LiquidTemperatureComponents.Clear();
        fluidGraph.PrepareLiquidTemperatureInventories();
        foreach (MachineEntity node in nodes.Values)
        {
            if (node.Definition.Fluid == null || GetFluidDeviceBehavior(node).IsTransit || GetFluidState(node).Ruptured) continue;
            foreach (string chamber in GetFluidState(node).Chambers.Keys)
            {
                if (!IsLiquidTemperatureChamber(node, chamber)) continue;
                foreach (FluidComponentState liquid in GetFluidInventory(node, chamber).State.Components)
                    if (liquid.LiquidMoles > 0m)
                        scope.LiquidTemperatureComponents.Add(fluidGraph.GetLiquidTemperatureComponent(node, chamber, liquid.FluidId));
            }
        }
    }

    private static void AdvanceLiquidNetworkTemperatureExchange(bool exchangeEnvironment)
    {
        if (fluidGraph == null) return;
        CollectLiquidTemperatureComponents();
        var targets = scope.LiquidTemperatureTargets;
        var published = scope.LiquidTemperaturePublishedTargets;
        targets.Clear(); published.Clear();
        TemperatureMgr environment = !IsShipScope && GameplayWorldRegistry.TryGet(worldKey, out _)
            ? TemperatureMgr.ExistingInstance : null;
        using (var sampling = environment != null ? environment.BeginAmbientPointSampling() : default)
        foreach (var component in scope.LiquidTemperatureComponents)
        {
            RefreshLiquidTemperatureComponent(component);
            if (!component.Temperature.HasLiquid || !exchangeEnvironment) continue;
            bool active = !GameNetwork.HasStateAuthority;
            foreach (var member in component.Members) active |= IsFluidActive(member.Node);
            if (!active || environment == null) continue;
            double sum = 0d;
            int count = 0;
            foreach (Vector2Int cell in component.Cells)
                if (sampling.TryGetAmbientTemperature((Vector2)cell + Vector2.one * .5f, out float ambient))
                { sum += ambient; count++; }
            double next = GameNetwork.HasStateAuthority
                ? IndustrialLiquidNetworkTemperature.AverageWithAmbientCells(component.Temperature.TemperatureKelvin, sum, count)
                : component.Temperature.TemperatureKelvin;
            if (GameNetwork.HasStateAuthority)
            {
                component.Temperature.SetTemperatureKelvin(next);
                bool previous = scope.SynchronizingLiquidTemperature;
                scope.SynchronizingLiquidTemperature = true;
                try { PublishLiquidTemperatureChanges(component); }
                finally { scope.SynchronizingLiquidTemperature = previous; }
            }
            foreach (Vector2Int cell in component.Cells)
            {
                targets.TryGetValue(cell, out var value);
                targets[cell] = (value.Sum + next - 273.15d, value.Count + 1);
            }
        }
        if (!exchangeEnvironment) return;
        foreach (var pair in targets) published[pair.Key] = (float)(pair.Value.Sum / pair.Value.Count);
        if (environment != null) environment.ReplaceDynamicGridTemperatureExchangeTargets(scope, published);
        else TemperatureMgr.ExistingInstance?.RemoveDynamicGridTemperatureExchangeTargets(scope);
    }

    private static void RememberRemoteLiquidNetworkTemperatures(MachineEntity node, FluidMachineState incoming)
    {
        foreach (var chamber in incoming.Chambers)
        {
            var inventory = GetLocalFluidInventory(node, chamber.Key);
            var key = (node.Id, chamber.Key);
            if (inventory.LiquidMoles > 0m)
                scope.RemoteLiquidTemperatures[key] = (++scope.RemoteLiquidTemperatureSequence, inventory.GetTemperatureKelvin());
            else scope.RemoteLiquidTemperatures.Remove(key);
        }
        scope.RemoteLiquidTemperatureDirty = true;
    }
    #endregion
}
