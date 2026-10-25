using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using FlatWorld.Networking;

public static partial class MachineWorld
{
    #region 销毁时逐批守恒释放
    public static void ReleaseDestroyedFluidContents(MachineEntity node)
    {
        using var lease = UseNodeScope(node);
        if (!GameNetwork.HasStateAuthority || node?.Definition.Fluid == null || !Contains(node)) return;
        RebuildGraphsIfDirty();
        FluidMachineState state = GetFluidState(node);
        if (state.Ruptured) return;
        var inventories = new HashSet<FluidInventory>();
        if (owner?.Mechanical == null) throw new InvalidOperationException("工业物料释放缺少权威存档。");
        foreach (MachineEntity member in nodes.Values)
            if (member.Definition.Fluid != null) FluidDeviceOperations.ReleaseReservations(member);
        foreach (string chamber in state.Chambers.Keys)
        {
            FluidInventory inventory = GetFluidInventory(node, chamber);
            if (!inventories.Add(inventory)) continue;
            inventory.State.Ports.Clear(); inventory.State.SynchronizeReservations();
            var components = new List<FluidComponentState>(inventory.State.Components);
            foreach (FluidComponentState component in components)
            {
                if (component.GasMoles > 0m && inventory.TryTakeExact(component.FluidId, FluidPhase.Gas, component.GasMoles, out FluidBatch gas))
                {
                    try { RecordAtmosphereEmission(node, gas); }
                    catch
                    {
                        if (!inventory.TryAdd(gas, GetFluidVolumeLiters(node) + .001d, .001d))
                            throw new InvalidOperationException("销毁排气失败后无法恢复真实来源。");
                        throw;
                    }
                }
                if (component.LiquidMoles <= 0m) continue;
                string spillWorld = worldKey; UnityEngine.Vector2 spillPosition = node.Position;
                if (TryResolveFluidSurfacePoint(node.Position, out string surfaceWorld, out UnityEngine.Vector2 surfacePosition))
                { spillWorld = surfaceWorld; spillPosition = surfacePosition; }
                var spill = new PressureLiquidSpillState { WorldKey = spillWorld, FluidId = component.FluidId, Center = spillPosition };
                if (IsShipScope && RequireShipEnvironment().TryGetMachineSpillAnchor(shipScopeId, node.Position,
                    out string detachedWorld, out UnityEngine.Vector2 detachedPosition))
                { spill.DetachedWorldKey = detachedWorld; spill.DetachedCenter = detachedPosition; }
                if (!inventory.TryTakeExact(component.FluidId, FluidPhase.Liquid, component.LiquidMoles, out FluidBatch liquid))
                    throw new InvalidOperationException("销毁时液体来源已变化。");
                spill.LiquidMoles = liquid.LiquidMoles; spill.InternalEnergyJoules = liquid.InternalEnergyJoules;
                try { owner.Mechanical.PendingPressureLiquidSpills.Add(spill); }
                catch { inventory.TryAdd(liquid, GetFluidVolumeLiters(node) + .001d, .001d); throw; }
            }
        }
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
            Number(definition.ThroughputLitersPerSecond); Number(definition.GasCapacityStandardLiters);
            key.Append(state.Ruptured)
                .Append('/').Append(GetFluidDeviceBehavior(node).PortsEnabled(node)).Append(';');
            Text(state.OutletPhase); Text(state.SelectedGasId);
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
