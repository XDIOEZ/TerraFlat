using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using FlatWorld.Networking;

public static partial class MachineWorld
{
    #region 独立气网载荷和远端只读视图
    private static void CaptureGasNetworkState(MachineEntity node, FluidMachineState state)
    {
        if (!GameNetwork.HasStateAuthority)
        {
            MachinePersistence.Write(node.Snapshot, "fluid", state);
            return;
        }
        // 初次加载尚未接管的组主载荷必须原样保留；不能在图建立前被一次捕获清掉。
        if (state.GasNetwork != null)
        {
            MachinePersistence.Write(node.Snapshot, "fluid", state);
            return;
        }
        state.GasNetworkEpoch = scope.GasEpoch;
        if (gasGraph != null && gasGraph.TryGetGroup(node, out var group) && group.OwnerId == node.Id)
        {
            FluidMachineState snapshot = MachinePersistence.Clone(state);
            snapshot.GasNetwork = group.CaptureSnapshot();
            MachinePersistence.Write(node.Snapshot, "fluid", snapshot);
        }
        else MachinePersistence.Write(node.Snapshot, "fluid", state);
    }

    // 逐节点恢复时允许成员稍后到达，载荷在完整接管前始终保留在唯一组主。
    private static bool GasArchivesReady(List<MachineEntity> sorted)
    {
        scope.PendingGasRestore = false;
        if (!GameNetwork.HasStateAuthority) return true;
        var claimed = new HashSet<int>();
        foreach (MachineEntity node in sorted)
        {
            if (node.Definition.Fluid == null) continue;
            FluidGasNetworkSnapshot saved = GetFluidState(node).GasNetwork;
            if (saved == null) continue;
            saved.Validate();
            if (saved.OwnerId != node.Id) throw new InvalidOperationException("气网载荷不在其唯一组主上。");
            foreach (int id in saved.MemberIds)
            {
                if (!claimed.Add(id)) throw new InvalidOperationException("多个气网载荷重复拥有同一成员。");
                if (!nodes.TryGetValue(id, out MachineEntity member)) scope.PendingGasRestore = true;
                else if (member.Definition.Fluid == null) throw new InvalidOperationException("气网成员不是工业流体节点。");
            }
        }
        return !scope.PendingGasRestore;
    }

    // 存档只让组主携带总库存；恢复时先准备所有真实份额，再一次性接管，防止读取顺序造气。
    private static void RestoreSavedGasNetworks(List<MachineEntity> sorted)
    {
        if (!GameNetwork.HasStateAuthority) return;
        var plans = new Dictionary<FluidInventory, FluidInventory>();
        var restored = new List<FluidMachineState>();
        var claimedMembers = new HashSet<int>();
        long epoch = scope.GasEpoch;
        foreach (MachineEntity ownerNode in sorted)
        {
            if (ownerNode.Definition.Fluid == null) continue;
            FluidMachineState state = GetFluidState(ownerNode);
            FluidGasNetworkSnapshot saved = state.GasNetwork;
            epoch = Math.Max(epoch, state.GasNetworkEpoch);
            if (saved == null) continue;
            saved.Validate();
            if (saved.OwnerId != ownerNode.Id)
                throw new InvalidOperationException("气网保存载荷的所有者与节点不一致。");
            var spaces = new List<decimal>(saved.MemberIds.Count);
            var destinations = new List<MachineEntity>(saved.MemberIds.Count);
            foreach (int id in saved.MemberIds)
            {
                if (!claimedMembers.Add(id) || !nodes.TryGetValue(id, out MachineEntity member) || member.Definition.Fluid == null)
                    throw new InvalidOperationException("气网保存载荷包含缺失或重复成员：" + id);
                FluidInventory local = GetFluidInventory(member);
                spaces.Add((decimal)Math.Max(member.Definition.Fluid.MinimumGasSpaceLiters,
                    member.Definition.Fluid.VolumeLiters - local.GetLiquidLiters()));
                destinations.Add(member);
            }
            var source = new FluidInventory(saved.Inventory);
            FluidInventoryState[] shares = FluidGasPartition.Prepare(source, spaces, saved.MemberIds);
            for (int i = 0; i < destinations.Count; i++)
            {
                MachineEntity member = destinations[i];
                // 直接接管到新气网，不绕经本地液腔混温；保存/恢复不应制造一次额外传热。
                bool shared = gasGraph != null && gasGraph.TryGetGroup(member, out _);
                FluidInventory destination = shared ? GetPhaseFluidInventory(member, "main", FluidPhase.Gas) : GetFluidInventory(member);
                if (!plans.TryGetValue(destination, out FluidInventory candidate))
                    plans.Add(destination, candidate = new FluidInventory(destination.State.Clone()));
                MergeFluidInventory(candidate, new FluidInventory(shares[i]),
                    shared ? GetPhaseFluidVolumeLiters(member, FluidPhase.Gas) : member.Definition.Fluid.VolumeLiters,
                    shared ? GetPhaseFluidMinimumGasSpaceLiters(member, FluidPhase.Gas) : member.Definition.Fluid.MinimumGasSpaceLiters);
                FluidInventory.ValidateState(candidate.State, candidate.Catalog);
            }
            epoch = Math.Max(epoch, saved.Epoch);
            restored.Add(state);
        }
        foreach (var plan in plans) plan.Key.Restore(plan.Value.State);
        foreach (FluidMachineState state in restored) state.GasNetwork = null;
        scope.GasEpoch = epoch;
    }

    private static void ApplyGasReplica(FluidMachineState incoming)
    {
        if (GameNetwork.HasStateAuthority || incoming == null) return;
        if (incoming.GasNetwork != null) scope.GasReplica.Apply(incoming.GasNetwork);
        else if (incoming.GasNetworkEpoch > 0) scope.GasReplica.AdvanceEpoch(incoming.GasNetworkEpoch);
    }

    private static void PublishGasTopology()
    {
        if (!scope.PublishGasTopologyPending || !GameNetwork.HasStateAuthority) return;
        scope.PublishGasTopologyPending = false;
        // 新代次会撤销客户端旧映射，因此每一个新组主都要有一份权威载荷，不只通知操作的节点。
        if (gasGraph != null)
            foreach (var group in gasGraph.Groups)
                if (nodes.TryGetValue(group.OwnerId, out MachineEntity ownerNode)) NodeStateChanged?.Invoke(ownerNode);
    }

    private static void DetachGasNetworksToLocal()
    {
        if (gasGraph == null || !GameNetwork.HasStateAuthority) return;
        gasGraph.FlushToLocal(member => nodes.TryGetValue(member.Id, out MachineEntity current) && ReferenceEquals(current, member),
            member => GetFluidInventory(member));
        gasGraph = null;
        scope.FluidTopologyKey = null;
        ResetGasBudgets(true);
        fluidStockCache.Clear(); fluidStepStock.Clear(); fluidThroughputRemaining.Clear();
    }

    /// <summary>销毁先交还本段份额，再将真实物料移交持久化释放队列；不直接在拓扑回调中排放。</summary>
    public static void ReleaseDestroyedFluidContents(MachineEntity node)
    {
        using var lease = UseNodeScope(node);
        if (!GameNetwork.HasStateAuthority || node?.Definition.Fluid == null || !Contains(node)) return;
        RebuildGraphsIfDirty();
        if (scope.PendingGasRestore) throw new InvalidOperationException("气网成员尚未恢复完整，不能提前销毁共享载荷。");
        if (IsSharedGasNetworkMember(node)) DetachGasNetworksToLocal();
        FluidMachineState state = GetFluidState(node);
        if (state.Ruptured) return;
        var inventories = new HashSet<FluidInventory>();
        var materials = new List<FluidBatch>();
        double pressureEnergy = 0d;
        foreach (string chamber in state.Chambers.Keys)
        {
            FluidInventory inventory = GetFluidInventory(node, chamber);
            if (!inventories.Add(inventory)) continue;
            double volume = GetFluidVolumeLiters(node), minimum = GetFluidMinimumGasSpaceLiters(node);
            if (inventory.GasMoles > 0m)
                pressureEnergy += Math.Max(0d, inventory.GetPressureKPa(volume, minimum) - GetFluidAmbientPressureKPa(node)) *
                    Math.Max(minimum, volume - inventory.GetLiquidLiters());
            materials.AddRange(new FluidInventory(inventory.State.Clone(), inventory.Catalog).Drain());
        }
        // 入队失败时原库存完全不动；入队成功后队列是唯一所有者，船体/地表使用同一正式释放路径。
        if (materials.Count > 0)
            PressureExplosionQueue.Enqueue(worldKey, node.Position, pressureEnergy,
                worldKey + ":" + node.Id + ":fluid-destruction:" + Guid.NewGuid().ToString("N"), materials, null);
        foreach (FluidInventory inventory in inventories) inventory.Restore(new FluidInventoryState());
        state.Ruptured = true;
        CaptureFluidState(node);
    }
    #endregion

    #region 只由真实流体拓扑失效重建
    private static string GetFluidTopologyKey(List<MachineEntity> sorted)
    {
        var key = new StringBuilder();
        void Text(string value) { value ??= ""; key.Append(value.Length).Append(':').Append(value).Append(';'); }
        void Number(double value) => Text(value.ToString("R", CultureInfo.InvariantCulture));
        foreach (MachineEntity node in sorted)
        {
            FluidMachineDefinition definition = node.Definition.Fluid;
            if (definition == null) continue;
            FluidMachineState state = GetFluidState(node);
            key.Append(node.Id).Append('/').Append(node.Cell.x).Append('/').Append(node.Cell.y)
                .Append('/').Append(node.RotationQuarterTurns).Append('/').Append(node.Definition.Layer).Append(';');
            Text(node.Definition.Id); Text(node.Definition.LogicId); Text(definition.Kind); Text(definition.MaterialId);
            Number(definition.VolumeLiters); Number(definition.MinimumGasSpaceLiters);
            Number(definition.ThroughputLitersPerSecond); Number(definition.MaxSafePressureKPa);
            key.Append(definition.CombineAdjacent).Append('/').Append(state.Ruptured)
                .Append('/').Append(GetFluidDeviceBehavior(node).PortsEnabled(node)).Append(';');
            Text(state.RuptureBudgetId); Text(state.OutletPhase);
            foreach (FluidMachinePortDefinition port in definition.Ports)
            {
                Text(port.Id); Text(port.Chamber); Text(port.Mode); Text(port.Phase); Text(port.FluidId);
                key.Append(port.Direction).Append('/').Append(port.Outlet).Append(';');
            }
            key.Append('|');
        }
        return key.ToString();
    }
    #endregion
}
