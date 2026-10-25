using System;
using System.Collections.Generic;

/// <summary>工业数量事务只处理组分、固定容量和流量，温度仅随物质交接给共用库存。</summary>
public static class IndustrialFluidTransfer
{
    #region 固定容量原子转移
    public static double FreeCapacity(FluidInventory target, FluidPhase phase, double gasCapacity, double liquidCapacity, string fluidId = null)
    {
        if (target == null) return 0d;
        if (phase == FluidPhase.Gas)
        {
            decimal free = (decimal)gasCapacity - FluidUnits.MolToStandardLiters(target.GasMoles);
            return free > .000000001m ? (double)free : 0d;
        }
        foreach (FluidComponentState component in target.State.Components)
            if (component.LiquidMoles > 0m && fluidId != null && !FluidInventory.Same(component.FluidId, fluidId)) return 0d;
        return Math.Max(0d, liquidCapacity - target.GetLiquidLiters());
    }

    // 筛选只取混合流中原有的比例，未通过的组分仍由来源持有。
    public static double Transfer(FluidInventory source, FluidInventory target, FluidPhase phase, double maximumLiters,
        double targetGasCapacity, double targetLiquidCapacity, string allowedFluidId = null)
    {
        if (source == null || target == null || ReferenceEquals(source.State, target.State) ||
            !double.IsFinite(maximumLiters) || maximumLiters <= 0d) return 0d;
        var requirements = new List<FluidRequirement>();
        decimal total = phase == FluidPhase.Gas ? source.GasMoles : source.LiquidMoles;
        if (total <= 0m) return 0d;
        decimal fraction;
        if (phase == FluidPhase.Gas)
            fraction = Math.Min(1m, FluidUnits.StandardLitersToMol((decimal)maximumLiters) / total);
        else
        {
            string liquid = null;
            foreach (FluidComponentState component in source.State.Components)
                if (component.LiquidMoles > 0m)
                {
                    if (liquid != null && !FluidInventory.Same(liquid, component.FluidId)) return 0d;
                    liquid = component.FluidId;
                }
            if (allowedFluidId != null && !FluidInventory.Same(liquid, allowedFluidId)) return 0d;
            fraction = Math.Min(1m, (decimal)maximumLiters / (decimal)source.GetLiquidLiters());
        }
        if (phase == FluidPhase.Gas)
            foreach (FluidComponentState component in source.State.Components)
                if (component.GasMoles > 0m && (allowedFluidId == null || FluidInventory.Same(component.FluidId, allowedFluidId)))
                    fraction = Math.Min(fraction, source.GetAvailableMoles(component.FluidId, phase) / component.GasMoles);
        double proposedLiters = 0d;
        string selectedLiquid = null;
        foreach (FluidComponentState component in source.State.Components)
        {
            if (allowedFluidId != null && !FluidInventory.Same(component.FluidId, allowedFluidId)) continue;
            decimal amount = (phase == FluidPhase.Gas ? component.GasMoles : component.LiquidMoles) * fraction;
            if (amount <= 0m) continue;
            requirements.Add(new FluidRequirement(component.FluidId, phase, amount));
            proposedLiters += (double)(phase == FluidPhase.Gas ? FluidUnits.MolToStandardLiters(amount) :
                FluidUnits.MolToLiquidLiters(source.Catalog.Find(component.FluidId), amount));
            if (phase == FluidPhase.Liquid) selectedLiquid = component.FluidId;
        }
        double free = FreeCapacity(target, phase, targetGasCapacity, targetLiquidCapacity, selectedLiquid);
        if (proposedLiters <= 0d || free <= 0d) return 0d;
        decimal scale = (decimal)Math.Min(1d, free / proposedLiters);
        if (scale < 1m)
            for (int i = 0; i < requirements.Count; i++)
                requirements[i] = new FluidRequirement(requirements[i].FluidId, phase, requirements[i].Moles * scale);
        FluidInventoryState beforeSource = source.State.Clone(), beforeTarget = target.State.Clone();
        try
        {
            if (!source.TryTakeRecipe(requirements, out List<FluidBatch> batches)) return 0d;
            double moved = 0d;
            foreach (FluidBatch batch in batches)
            {
                if (!target.TryAdd(batch, Math.Max(.001d, targetLiquidCapacity) + .001d, .001d, false, true))
                { source.Restore(beforeSource); target.Restore(beforeTarget); return 0d; }
                moved += (double)(phase == FluidPhase.Gas ? FluidUnits.MolToStandardLiters(batch.GasMoles) :
                    FluidUnits.MolToLiquidLiters(target.Catalog.Find(batch.FluidId), batch.LiquidMoles));
            }
            return moved;
        }
        catch { source.Restore(beforeSource); target.Restore(beforeTarget); throw; }
    }
    #endregion
}
