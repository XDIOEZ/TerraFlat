using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FlatWorld.Networking;
using UnityEngine;

public static partial class MachineWorld
{
    #region 气相独立库存预算
    private static Dictionary<FluidInventory, FluidInventoryState> gasStepStock => scope.GasStock;
    private static Dictionary<FluidInventory, FluidInventoryState> gasStockCache => scope.GasStockCache;
    private static Dictionary<FluidInventory, double> gasThroughputRemaining => scope.GasThroughput;

    internal static bool TryGetGasTransferBudget(FluidInventory inventory, out FluidInventoryState stock, out double liters)
    {
        bool exists = gasStepStock.TryGetValue(inventory, out stock);
        return gasThroughputRemaining.TryGetValue(inventory, out liters) && exists;
    }
    internal static void SetGasTransferBudget(FluidInventory inventory, double remaining)
        => gasThroughputRemaining[inventory] = Math.Max(0d, remaining);
    private static void ConsumeGasStepStock(FluidInventory inventory, string fluidId, decimal moles)
    {
        if (!gasStepStock.TryGetValue(inventory, out var stock)) return;
        FluidComponentState component = stock.Components.Find(value => FluidInventory.Same(value.FluidId, fluidId));
        if (component != null) component.GasMoles = Math.Max(0m, component.GasMoles - moles);
    }
    private static void ResetGasBudgets(bool clearCache = false)
    {
        gasStepStock.Clear(); gasThroughputRemaining.Clear(); scope.GasPortThroughput.Clear();
        if (clearCache) gasStockCache.Clear();
    }
    private static void RememberGasInventory(FluidInventory inventory, double litersPerSecond, float seconds)
    {
        if (gasStepStock.ContainsKey(inventory)) return;
        if (!gasStockCache.TryGetValue(inventory, out FluidInventoryState stock))
            gasStockCache.Add(inventory, stock = new FluidInventoryState());
        stock.CopyFrom(inventory.State, false);
        gasStepStock.Add(inventory, stock);
        gasThroughputRemaining.Add(inventory, Math.Max(0d, litersPerSecond * seconds));
    }
    private static double GasPortBudget(MachineEntity node, FluidMachinePortDefinition port, float seconds)
    {
        var key = (node.Id, port.Id);
        if (!scope.GasPortThroughput.TryGetValue(key, out double remaining))
            scope.GasPortThroughput.Add(key, remaining = GetIndustrialThroughput(node) * seconds);
        return remaining;
    }
    private static void ConsumeGasPort(MachineEntity node, FluidMachinePortDefinition port, double liters)
    {
        var key = (node.Id, port.Id);
        if (scope.GasPortThroughput.TryGetValue(key, out double remaining))
            scope.GasPortThroughput[key] = Math.Max(0d, remaining - liters);
    }
    #endregion

}
