using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using UnityEngine;

public static partial class MachineWorld
{
    #region 相邻同类储罐组合
    private static void RebuildCombinedFluidTankGroups(List<MachineEntity> sorted)
    {
        foreach (MachineEntity node in sorted)
            if (node.Definition.Fluid != null) FluidDeviceOperations.ReleaseReservations(node);
        var previousOwners = new Dictionary<int, FluidTankGroup>(fluidTankOwners);
        if (GameNetwork.HasStateAuthority)
            foreach (FluidTankGroup previous in fluidTankGroups) previous.CombinedInventory?.SynchronizeLocalShares();
        fluidTankOwners.Clear(); fluidTankGroups.Clear();
        var queue = new Queue<MachineEntity>();
        foreach (MachineEntity root in sorted)
        {
            if (!IsFluidTankBlock(root) || fluidTankOwners.ContainsKey(root.Id)) continue;
            var group = new FluidTankGroup(); fluidTankGroups.Add(group);
            if (root.Definition.Fluid.CombineAdjacent && !GetFluidState(root).Ruptured)
                group.CombinedInventory = new CombinedFluidTankInventory();
            queue.Enqueue(root); fluidTankOwners.Add(root.Id, group);
            while (queue.Count > 0)
            {
                MachineEntity node = queue.Dequeue(); group.Members.Add(node);
                FluidMachineDefinition definition = node.Definition.Fluid;
                FluidInventory local = GetLocalFluidInventory(node);
                group.TotalVolume += definition.VolumeLiters;
                group.GasCapacityStandardLiters += definition.GasCapacityStandardLiters;
                group.MinimumGasSpace += definition.MinimumGasSpaceLiters;
                if (group.CombinedInventory == null) { group.Inventory = local; continue; }
                group.CombinedInventory.AddMember(node.Id, definition.VolumeLiters, definition.GasCapacityStandardLiters,
                    definition.MinimumGasSpaceLiters, local.State);
                group.Inventory = group.CombinedInventory.Inventory;
                if (string.IsNullOrEmpty(group.LiquidIdentity)) group.LiquidIdentity = GetCombinedFluidTankLiquidIdentity(group.Inventory);
                foreach (Vector2Int direction in MechanicalNetworkGraph.Directions)
                {
                    MachineEntity adjacent = graph.At(node.Cell + direction, node.Definition.Layer);
                    if (adjacent == null || fluidTankOwners.ContainsKey(adjacent.Id) ||
                        adjacent.Definition.Id != root.Definition.Id || adjacent.Definition.Fluid?.CombineAdjacent != true ||
                        GetFluidState(adjacent).Ruptured || !group.CombinedInventory.CanCombineLiquid(GetLocalFluidInventory(adjacent).State)) continue;
                    string candidateLiquid = GetCombinedFluidTankLiquidIdentity(GetLocalFluidInventory(adjacent));
                    if (!string.IsNullOrEmpty(group.LiquidIdentity) && !string.IsNullOrEmpty(candidateLiquid) &&
                        !FluidInventory.Same(group.LiquidIdentity, candidateLiquid)) continue;
                    if (!string.IsNullOrEmpty(candidateLiquid)) group.LiquidIdentity = candidateLiquid;
                    fluidTankOwners.Add(adjacent.Id, group); queue.Enqueue(adjacent);
                }
            }
            group.Members.Sort((a, b) => a.Id.CompareTo(b.Id));
            group.LiquidIdentity = GetCombinedFluidTankLiquidIdentity(group.Inventory);
            if (GameNetwork.HasStateAuthority && group.CombinedInventory != null)
            {
                bool changed = !previousOwners.TryGetValue(root.Id, out FluidTankGroup previous) || previous.Members.Count != group.Members.Count;
                if (!changed)
                    for (int i = 0; i < group.Members.Count; i++)
                        if (previous.Members[i].Id != group.Members[i].Id) { changed = true; break; }
                if (changed) foreach (MachineEntity member in group.Members) scope.CombinedTankNotifications.Add(member);
            }
        }
    }

    public static double GetFluidGasCapacityStandardLiters(MachineEntity node)
        => fluidTankOwners.TryGetValue(node.Id, out FluidTankGroup group) ? group.GasCapacityStandardLiters : node.Definition.Fluid.GasCapacityStandardLiters;

    private static string GetCombinedFluidTankLiquidIdentity(FluidInventory inventory)
    {
        foreach (FluidComponentState component in inventory.State.Components)
            if (component.LiquidMoles > 0m) return component.FluidId;
        return string.Empty;
    }

    private static void SynchronizeCombinedFluidTankLocalShares(MachineEntity node)
    {
        if (GameNetwork.HasStateAuthority && fluidTankOwners.TryGetValue(node.Id, out FluidTankGroup group)) group.CombinedInventory?.SynchronizeLocalShares();
    }

    private static void DetachCombinedFluidTankMember(MachineEntity node)
    {
        if (!fluidTankOwners.Remove(node.Id, out FluidTankGroup group) || group.CombinedInventory == null) return;
        group.CombinedInventory.RemoveMember(node.Id, GameNetwork.HasStateAuthority); group.Members.Remove(node);
        group.TotalVolume = group.CombinedInventory.VolumeLiters;
        group.GasCapacityStandardLiters = group.CombinedInventory.GasCapacityStandardLiters;
        group.MinimumGasSpace = group.CombinedInventory.MinimumGasSpaceLiters;
        if (group.Members.Count == 0) fluidTankGroups.Remove(group);
        scope.FluidTopologyKey = null;
    }

    private static void InvalidateCombinedFluidTankRemoteMember(MachineEntity node)
    {
        if (!fluidTankOwners.TryGetValue(node.Id, out FluidTankGroup group) || group.CombinedInventory == null) return;
        // 客户端成员份额来自服务端，逐包更新期间不能用尚未齐全的组总量重新分摊。
        if (GameNetwork.HasStateAuthority) group.CombinedInventory.SynchronizeLocalShares();
        foreach (MachineEntity member in group.Members) fluidTankOwners.Remove(member.Id);
        fluidTankGroups.Remove(group); scope.FluidTopologyKey = null; scope.FluidTopologyDirty = true;
    }

    private static bool NotifyCombinedFluidTankGroupStateChanged(MachineEntity node)
    {
        if (!GameNetwork.HasStateAuthority) return false;
        if (!fluidTankOwners.TryGetValue(node.Id, out FluidTankGroup group) || group.CombinedInventory == null) return false;
        string liquid = GetCombinedFluidTankLiquidIdentity(group.Inventory);
        if (liquid != group.LiquidIdentity)
        { group.LiquidIdentity = liquid; scope.FluidTopologyKey = null; scope.FluidTopologyDirty = true; }
        long revision = group.Inventory.State.ContentRevision;
        if (group.PublishedContentRevision == revision) return false;
        // 同一计算步的多份抽水只在收尾分摊和发布一次，读取或拆回仍可即时取得真实份额。
        if (scope.SimulationStepDepth > 0) scope.PendingCombinedTankStateChanges.Add(group);
        else PublishCombinedFluidTankGroupStateChanged(group);
        return true;
    }

    private static void PublishCombinedFluidTankGroupStateChanged(FluidTankGroup group)
    {
        group.PublishedContentRevision = group.Inventory.State.ContentRevision;
        group.CombinedInventory.SynchronizeLocalShares();
        foreach (MachineEntity member in group.Members)
            if (Contains(member)) NodeStateChanged?.Invoke(member);
    }

    private static void PublishPendingCombinedFluidTankStateChanges()
    {
        var pending = scope.PendingCombinedTankStateChanges;
        if (pending.Count == 0) return;
        var work = scope.CombinedTankStateChangeWork;
        work.Clear(); work.AddRange(pending); pending.Clear();
        try
        {
            foreach (FluidTankGroup group in work)
                PublishCombinedFluidTankGroupStateChanged(group);
        }
        finally { work.Clear(); }
    }

    private static void PublishCombinedFluidTankTopologyChanges()
    {
        if (scope.CombinedTankNotifications.Count == 0) return;
        var pending = new List<MachineEntity>(scope.CombinedTankNotifications);
        scope.CombinedTankNotifications.Clear();
        foreach (MachineEntity member in pending)
            if (Contains(member)) { SynchronizeCombinedFluidTankLocalShares(member); NodeStateChanged?.Invoke(member); }
    }
    #endregion
}
