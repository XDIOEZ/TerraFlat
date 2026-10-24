using System;
using System.Collections.Generic;

public static partial class MachineWorld
{
    #region 气液拆分正式世界生命周期回归
    internal static void GameplayGasNetworkLifecycle(Action<string, bool, string> report)
    {
        using var fixture = new GameplayFluidScope();
        MachineEntity first = fixture.Add("FluidPipe"), second = fixture.Add("FluidPipe", 1);
        fixture.Rebuild();
        FluidInventory gas = GetPhaseFluidInventory(first, "main", FluidPhase.Gas);
        FluidBatch input = FluidInventory.CreateBatch(FluidCatalog.Default.Find(FluidIds.Oxygen), .2m, 0m, 321.25);
        if (!gas.TryAdd(input, GetPhaseFluidVolumeLiters(first, FluidPhase.Gas), GetPhaseFluidMinimumGasSpaceLiters(first, FluidPhase.Gas)))
            throw new InvalidOperationException("正式气网装料失败。");
        report("普通管段共享唯一气相，液相仍分段", ReferenceEquals(gas, GetPhaseFluidInventory(second, "main", FluidPhase.Gas)) &&
            !ReferenceEquals(GetFluidInventory(first), GetFluidInventory(second)) && gasGraph.Edges.Count == 0, "检查正式组库存和边界图。");

        IndustrialGasNetworkGraph before = gasGraph;
        long epoch = scope.GasEpoch;
        dirty = true; RebuildGraphsIfDirty();
        report("非流体拓扑脏标记不拆分或重建气网", ReferenceEquals(before, gasGraph) && scope.GasEpoch == epoch &&
            ReferenceEquals(gas, GetPhaseFluidInventory(first, "main", FluidPhase.Gas)), "模拟无流体连接变化的机械/电力刷新。");

        MachineEntity ownerNode = first.Id < second.Id ? first : second;
        MachineEntity other = ReferenceEquals(ownerNode, first) ? second : first;
        CaptureFluidState(ownerNode); CaptureFluidState(other);
        FluidMachineState ownerSaved = MachinePersistence.Read<FluidMachineState>(ownerNode.Snapshot, "fluid");
        FluidMachineState memberSaved = MachinePersistence.Read<FluidMachineState>(other.Snapshot, "fluid");
        report("保存只由组主携带独立气网载荷", ownerSaved.GasNetwork?.Inventory != null && memberSaved.GasNetwork == null &&
            new FluidInventory(ownerSaved.GasNetwork.Inventory).GasMoles == .2m &&
            new FluidInventory(ownerSaved.Chambers["main"]).GasMoles == 0m, "本地液腔不混入全网气体。");

        double energy = gas.State.TotalInternalEnergyJoules;
        ItemData recovered = Remove(ownerNode.Id);
        fixture.Rebuild();
        FluidMachineState recoveredState = MachinePersistence.Read<FluidMachineState>(recovered, "fluid");
        FluidInventory recoveredGas = new(recoveredState.Chambers["main"]);
        FluidInventory survivingGas = GetPhaseFluidInventory(other, "main", FluidPhase.Gas);
        report("回收组主只带走其真实份额", recoveredState.GasNetwork == null && recoveredGas.GasMoles > 0m &&
            survivingGas.GasMoles > 0m && recoveredGas.GasMoles + survivingGas.GasMoles == .2m,
            "使用正式 Remove 路径，剩余管网库存保留。");
        report("回收及重建内能守恒", Math.Abs(recoveredGas.State.TotalInternalEnergyJoules +
            survivingGas.State.TotalInternalEnergyJoules - energy) <= 1e-10 * Math.Max(1d, Math.Abs(energy)), "比较原始总内能。");
    }

    internal static void GameplayGasNetworkRestore(Action<string, bool, string> report)
    {
        using var fixture = new GameplayFluidScope();
        MachineEntity a = fixture.Add("FluidPipe"), b = fixture.Add("FluidPipe", 1);
        fixture.Rebuild();
        FluidInventory initial = GetPhaseFluidInventory(a, "main", FluidPhase.Gas);
        if (!initial.TryAdd(FluidInventory.CreateBatch(FluidCatalog.Default.Find(FluidIds.Nitrogen), .3m, 0m, 345.67),
            GetPhaseFluidVolumeLiters(a, FluidPhase.Gas), GetPhaseFluidMinimumGasSpaceLiters(a, FluidPhase.Gas)))
            throw new InvalidOperationException("保存夹具无法装入氮气。");
        double energy = initial.State.TotalInternalEnergyJoules;
        MachineEntity ownerNode = a.Id < b.Id ? a : b;
        MachineEntity member = ReferenceEquals(ownerNode, a) ? b : a;
        ItemData ownerSnapshot = CaptureSnapshot(ownerNode), memberSnapshot = CaptureSnapshot(member);
        DiscardPlacementCandidate(a.Id); ForgetDetachedFluidNode(a);
        DiscardPlacementCandidate(b.Id); ForgetDetachedFluidNode(b);
        fixture.Rebuild();

        MachineEntity restoredOwner = RestoreMachine(ownerSnapshot);
        fixture.Rebuild();
        CaptureFluidState(restoredOwner);
        report("组主先恢复时保留载荷并等待成员", scope.PendingGasRestore &&
            MachinePersistence.Read<FluidMachineState>(restoredOwner.Snapshot, "fluid").GasNetwork != null,
            "没有用缺失成员生成第二份库存或提前清空原载荷。");
        MachineEntity restoredMember = RestoreMachine(memberSnapshot);
        fixture.Rebuild();
        FluidInventory restored = GetPhaseFluidInventory(restoredOwner, "main", FluidPhase.Gas);
        report("成员到齐后一次接管气网库存", !scope.PendingGasRestore && restored.GetGasMoles(FluidIds.Nitrogen) == .3m &&
            ReferenceEquals(restored, GetPhaseFluidInventory(restoredMember, "main", FluidPhase.Gas)) &&
            GetFluidState(restoredOwner).GasNetwork == null, "读取顺序不改变唯一所有权。");
        report("存档恢复保持真实内能", Math.Abs(restored.State.TotalInternalEnergyJoules - energy) <= 1e-10 * Math.Max(1d, energy),
            "恢复不把全网气体先混进主管段液腔。");
    }
    #endregion
}
