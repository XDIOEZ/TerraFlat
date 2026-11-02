using System;
using System.Collections.Generic;

/// <summary>液网只统一温度，真实液量仍由各设备库存持有。</summary>
public sealed class IndustrialLiquidNetworkTemperature
{
    #region 去重库存与统一液温
    private readonly List<FluidInventory> inventories = new();
    private readonly HashSet<FluidInventory> uniqueInventories = new();
    private readonly List<long> revisions = new();
    private readonly HashSet<FluidInventory> changedTemperatures = new();
    public IReadOnlyList<FluidInventory> Inventories => inventories;
    public double TotalLiquidLiters { get; private set; }
    public double TemperatureKelvin { get; private set; }
    public bool HasLiquid => TotalLiquidLiters > 0d;
    public bool ChangedInventoryTemperature(FluidInventory inventory) => changedTemperatures.Contains(inventory);

    public void AddInventory(FluidInventory inventory)
    {
        if (!uniqueInventories.Add(inventory)) return;
        inventories.Add(inventory); revisions.Add(-1);
    }

    // 仅内容变化后重新汇总；网络均温写回内能，分网和存档自然继承当前温度。
    public bool RefreshContents(bool synchronizeTemperature = true)
    {
        changedTemperatures.Clear();
        bool changed = false;
        for (int i = 0; i < inventories.Count; i++)
            changed |= revisions[i] != inventories[i].State.ContentRevision;
        if (!changed) return false;
        ReadInventoryTotals(synchronizeTemperature);
        if (synchronizeTemperature && HasLiquid) SetTemperatureKelvin(TemperatureKelvin);
        RememberContentRevisions();
        return true;
    }

    public void ReadTemperatureAndVolume()
    {
        for (int i = 0; i < inventories.Count; i++)
            if (revisions[i] != inventories[i].State.ContentRevision) { ReadInventoryTotals(true); return; }
    }

    private void ReadInventoryTotals(bool calculateTemperature)
    {
        double liters = 0d, weightedTemperature = 0d;
        foreach (FluidInventory inventory in inventories)
        {
            double amount = inventory.GetLiquidLiters();
            liters += amount;
            if (calculateTemperature && amount > 0d)
                weightedTemperature += amount * inventory.GetTemperatureKelvin();
        }
        TotalLiquidLiters = liters;
        if (calculateTemperature) TemperatureKelvin = liters > 0d ? weightedTemperature / liters : 0d;
    }

    public bool SetTemperatureKelvin(double temperature)
    {
        TemperatureKelvin = temperature;
        bool changed = false;
        foreach (FluidInventory inventory in inventories)
        {
            if (inventory.LiquidMoles <= 0m) continue;
            inventory.GetEnergyTerms(out double capacity, out double offset);
            double energy = offset + capacity * temperature;
            if (Math.Abs(inventory.GetTemperatureKelvin() - temperature) <= .000000001d) continue;
            inventory.State.TotalInternalEnergyJoules = energy;
            changedTemperatures.Add(inventory);
            changed = true;
        }
        RememberContentRevisions();
        return changed;
    }

    public void MirrorAuthoritativeTemperature(double temperature)
    {
        RefreshContents(false);
        TemperatureKelvin = HasLiquid ? temperature : 0d;
    }

    private void RememberContentRevisions()
    {
        for (int i = 0; i < inventories.Count; i++) revisions[i] = inventories[i].State.ContentRevision;
    }

    public static double AverageWithAmbientCells(double temperatureKelvin, double ambientCelsiusSum, int cellCount)
        => (temperatureKelvin - 273.15d + ambientCelsiusSum) / (cellCount + 1) + 273.15d;
    #endregion
}
