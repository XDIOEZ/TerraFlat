using System;
using System.Collections.Generic;

/// <summary>气网中的局部液体接触面；液体位置、容量和单液相约束仍属于本地容器。</summary>
public sealed class FluidLiquidPhaseContact
{
    public readonly FluidInventory Inventory;
    public readonly double VolumeLiters, MinimumGasSpaceLiters;
    public readonly bool SingleLiquid;
    public FluidLiquidPhaseContact(FluidInventory inventory, double volumeLiters, double minimumGasSpaceLiters, bool singleLiquid)
    {
        Inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        FluidInventory.ValidateGeometry(volumeLiters, minimumGasSpaceLiters);
        VolumeLiters = volumeLiters; MinimumGasSpaceLiters = minimumGasSpaceLiters; SingleLiquid = singleLiquid;
    }
}

/// <summary>相变是显式守恒事务，不是气液共用输送。计算读整网分压，液体不跨管段均压或搬家。</summary>
public static class FluidGasLiquidPhaseBridge
{
    #region 计算候选相变并原子提交
    public static FluidPhaseChangeResult Advance(FluidInventory gas, IReadOnlyList<FluidLiquidPhaseContact> contacts,
        double volumeLiters, double minimumGasSpaceLiters, double seconds, double maximumMolesPerSecond)
    {
        if (gas == null || contacts == null) throw new ArgumentNullException();
        if (!HasPhaseMaterial(gas, contacts)) return default;
        if (gas.LiquidMoles != 0m) throw new InvalidOperationException("独立气网不能保存液体。");
        var originalGas = gas.State.Clone();
        var originalLiquid = new FluidInventoryState[contacts.Count];
        var plannedLiquid = new FluidInventoryState[contacts.Count];
        var combined = gas.State.Clone();
        combined.Ports.Clear();
        var totals = new Dictionary<string, FluidComponentState>(StringComparer.OrdinalIgnoreCase);
        foreach (FluidComponentState component in combined.Components) totals.Add(component.FluidId, component);
        for (int i = 0; i < contacts.Count; i++)
        {
            FluidInventory local = contacts[i].Inventory;
            if (local.GasMoles != 0m) throw new InvalidOperationException("相变前必须把本地气体交给唯一气网。");
            originalLiquid[i] = local.State.Clone();
            plannedLiquid[i] = local.State.Clone();
            plannedLiquid[i].Ports.Clear();
            combined.TotalInternalEnergyJoules += local.State.TotalInternalEnergyJoules;
            foreach (FluidComponentState component in local.State.Components)
            {
                if (!totals.TryGetValue(component.FluidId, out var total))
                {
                    total = new FluidComponentState { FluidId = component.FluidId };
                    totals.Add(component.FluidId, total); combined.Components.Add(total);
                }
                total.LiquidMoles += component.LiquidMoles;
            }
        }
        var calculation = new FluidInventory(combined, gas.Catalog);
        FluidPhaseChangeResult phase = FluidThermodynamics.AdvancePhaseChange(calculation, volumeLiters,
            minimumGasSpaceLiters, seconds, maximumMolesPerSecond);
        if (phase.ConvertedMoles == 0m) return phase;

        // 先蒸发释放局部容量，再分配新凝液；原有液体从不被搬到别的成员。
        foreach (FluidComponentState component in calculation.State.Components)
        {
            decimal original = SumLiquid(originalLiquid, component.FluidId);
            if (component.LiquidMoles >= original) continue;
            decimal remaining = original - component.LiquidMoles;
            int last = -1;
            for (int i = 0; i < contacts.Count; i++) if (Find(originalLiquid[i], component.FluidId)?.LiquidMoles > 0m) last = i;
            for (int i = 0; i <= last; i++)
            {
                FluidComponentState local = Find(plannedLiquid[i], component.FluidId);
                if (local == null || local.LiquidMoles <= 0m) continue;
                decimal take = i == last ? remaining : Math.Min(remaining,
                    (original - component.LiquidMoles) * (local.LiquidMoles / original));
                take = Math.Min(take, local.LiquidMoles);
                local.LiquidMoles -= take; remaining -= take;
            }
            if (remaining != 0m) throw new InvalidOperationException("相变蒸发份额未守恒。");
        }
        foreach (FluidComponentState component in calculation.State.Components)
        {
            decimal remaining = component.LiquidMoles - SumLiquid(plannedLiquid, component.FluidId);
            if (remaining <= 0m) continue;
            FluidDefinition substance = gas.Catalog.Find(component.FluidId);
            for (int i = 0; i < contacts.Count && remaining > 0m; i++)
            {
                FluidLiquidPhaseContact contact = contacts[i];
                if (contact.SingleLiquid && plannedLiquid[i].Components.Exists(value =>
                    value.LiquidMoles > 0m && !FluidInventory.Same(value.FluidId, component.FluidId))) continue;
                double occupied = 0d;
                foreach (FluidComponentState local in plannedLiquid[i].Components)
                    occupied += (double)FluidUnits.MolToLiquidLiters(gas.Catalog.Find(local.FluidId), local.LiquidMoles);
                double free = Math.Max(0d, contact.VolumeLiters - contact.MinimumGasSpaceLiters - occupied);
                decimal amount = Math.Min(remaining, FluidUnits.LiquidLitersToMol(substance, (decimal)free));
                if (amount <= 0m) continue;
                FluidComponentState destination = Find(plannedLiquid[i], component.FluidId);
                if (destination == null) plannedLiquid[i].Components.Add(destination = new FluidComponentState { FluidId = component.FluidId });
                destination.LiquidMoles += amount; remaining -= amount;
            }
            if (remaining > 0m)
                return new FluidPhaseChangeResult(0m, "气网相变受本地液腔容量或相态限制，原库存和内能已保留", phase.UnsupportedFluidIds);
        }

        double temperature = calculation.GetTemperatureKelvin();
        var plannedGas = new FluidInventoryState { RandomState = originalGas.RandomState };
        foreach (FluidComponentState component in calculation.State.Components)
            if (component.GasMoles > 0m)
            {
                plannedGas.Components.Add(new FluidComponentState { FluidId = component.FluidId, GasMoles = component.GasMoles });
                plannedGas.TotalInternalEnergyJoules += gas.Catalog.Find(component.FluidId).EnergyAt(component.GasMoles, 0m, temperature);
            }
        double assignedEnergy = plannedGas.TotalInternalEnergyJoules;
        FluidInventoryState energyOwner = plannedGas.Components.Count == 0 ? null : plannedGas;
        for (int i = 0; i < contacts.Count; i++)
        {
            FluidInventoryState local = plannedLiquid[i];
            local.Components.RemoveAll(component => component.LiquidMoles == 0m);
            local.TotalInternalEnergyJoules = 0d;
            foreach (FluidComponentState component in local.Components)
                local.TotalInternalEnergyJoules += gas.Catalog.Find(component.FluidId).EnergyAt(0m, component.LiquidMoles, temperature);
            assignedEnergy += local.TotalInternalEnergyJoules;
            if (local.Components.Count > 0 && (energyOwner == null || local.TotalInternalEnergyJoules > energyOwner.TotalInternalEnergyJoules))
                energyOwner = local;
            KeepValidClaims(originalLiquid[i], local, FluidPhase.Liquid);
        }
        if (energyOwner != null) energyOwner.TotalInternalEnergyJoules += combined.TotalInternalEnergyJoules - assignedEnergy;
        KeepValidClaims(originalGas, plannedGas, FluidPhase.Gas);
        FluidInventory.ValidateState(plannedGas, gas.Catalog);
        foreach (FluidInventoryState local in plannedLiquid) FluidInventory.ValidateState(local, gas.Catalog);
        try
        {
            gas.Restore(plannedGas);
            for (int i = 0; i < contacts.Count; i++) contacts[i].Inventory.Restore(plannedLiquid[i]);
        }
        catch
        {
            gas.Restore(originalGas);
            for (int i = 0; i < contacts.Count; i++) contacts[i].Inventory.Restore(originalLiquid[i]);
            throw;
        }
        return phase;
    }
    #endregion

    #region 材料和预留查询
    private static bool HasPhaseMaterial(FluidInventory gas, IReadOnlyList<FluidLiquidPhaseContact> contacts)
    {
        foreach (FluidComponentState component in gas.State.Components)
            if (component.GasMoles > 0m && gas.Catalog.Find(component.FluidId).PhaseChange != null) return true;
        foreach (FluidLiquidPhaseContact contact in contacts)
            foreach (FluidComponentState component in contact.Inventory.State.Components)
                if (component.LiquidMoles > 0m && gas.Catalog.Find(component.FluidId).PhaseChange != null) return true;
        return false;
    }
    private static FluidComponentState Find(FluidInventoryState state, string id)
        => state.Components.Find(component => FluidInventory.Same(component.FluidId, id));
    private static decimal SumLiquid(FluidInventoryState[] states, string id)
    {
        decimal total = 0m;
        foreach (FluidInventoryState state in states) total += Find(state, id)?.LiquidMoles ?? 0m;
        return total;
    }
    private static void KeepValidClaims(FluidInventoryState original, FluidInventoryState target, FluidPhase phase)
    {
        foreach (FluidOutputReservation reservation in original.Ports)
        {
            FluidComponentState component = Find(target, reservation.FluidId);
            if (component == null) continue;
            decimal available = phase == FluidPhase.Gas ? component.GasMoles : component.LiquidMoles;
            foreach (FluidOutputReservation kept in target.Ports)
                if (FluidInventory.Same(kept.FluidId, reservation.FluidId)) available -= kept.TotalMoles;
            decimal amount = Math.Min(Math.Max(0m, available), phase == FluidPhase.Gas ? reservation.GasMoles : reservation.LiquidMoles);
            if (amount > 0m) target.Ports.Add(new FluidOutputReservation { PortId = reservation.PortId, FluidId = reservation.FluidId,
                GasMoles = phase == FluidPhase.Gas ? amount : 0m, LiquidMoles = phase == FluidPhase.Liquid ? amount : 0m });
        }
    }
    #endregion
}
