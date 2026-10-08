using System;
using System.Collections.Generic;

public readonly struct FluidPhaseChangeResult
{
    public FluidPhaseChangeResult(decimal convertedMoles, string blockedReason, IReadOnlyList<string> unsupported)
    { ConvertedMoles = convertedMoles; BlockedReason = blockedReason; UnsupportedFluidIds = unsupported; }
    public decimal ConvertedMoles { get; }
    public string BlockedReason { get; }
    public bool IsBlocked => !string.IsNullOrEmpty(BlockedReason);
    public IReadOnlyList<string> UnsupportedFluidIds { get; }
}

public readonly struct FluidCompressionResult
{
    public FluidCompressionResult(FluidBatch batch, double workJoules, double lossJoules)
    { Batch = batch; WorkJoules = workJoules; LossJoules = lossJoules; }
    public FluidBatch Batch { get; }
    public double WorkJoules { get; }
    public double LossJoules { get; }
    public bool Success => !Batch.IsEmpty;
}

/// <summary>总内能统一预算，封闭相变只搬相份额，不重复加冷凝热。</summary>
public static class FluidThermodynamics
{
    #region 有限热量与压缩做功
    public static double AddHeat(FluidInventory inventory, double joules)
    {
        if (inventory == null || inventory.IsEmpty || !FluidUnits.IsFinite(joules)) return 0d;
        inventory.GetEnergyTerms(out double capacity, out double offset);
        double minimumEnergy = offset + capacity * 0.000001d;
        double applied = Math.Max(joules, Math.Min(0d, minimumEnergy - inventory.State.TotalInternalEnergyJoules));
        if (!FluidUnits.IsFinite(inventory.State.TotalInternalEnergyJoules + applied)) return 0d;
        inventory.State.TotalInternalEnergyJoules += applied; return applied;
    }
    public static double ExchangeHeat(FluidInventory first, FluidInventory second, double conductanceJPerSecondKelvin, double seconds)
    {
        if (first == null || second == null || first.IsEmpty || second.IsEmpty || ReferenceEquals(first.State, second.State) ||
            !FluidUnits.IsFinite(conductanceJPerSecondKelvin) || conductanceJPerSecondKelvin <= 0d ||
            !FluidUnits.IsFinite(seconds) || seconds <= 0d) return 0d;
        double difference = first.GetTemperatureKelvin() - second.GetTemperatureKelvin();
        first.GetEnergyTerms(out double firstCapacity, out _); second.GetEnergyTerms(out double secondCapacity, out _);
        double equilibrium = Math.Abs(difference) / (1d / firstCapacity + 1d / secondCapacity);
        double transfer = Math.Min(equilibrium, Math.Abs(difference) * conductanceJPerSecondKelvin * seconds);
        if (!FluidUnits.IsFinite(transfer)) return 0d;
        transfer *= Math.Sign(difference);
        first.State.TotalInternalEnergyJoules -= transfer; second.State.TotalInternalEnergyJoules += transfer;
        return transfer;
    }
    public static FluidCompressionResult Compress(FluidBatch batch, double sourcePressureKPa, double targetPressureKPa,
        double availableWorkJoules, double efficiency, FluidCatalog catalog = null)
    {
        if (!batch.IsGas || !FluidUnits.IsFinite(sourcePressureKPa) || sourcePressureKPa <= 0d ||
            !FluidUnits.IsFinite(targetPressureKPa) || targetPressureKPa <= 0d ||
            !FluidUnits.IsFinite(availableWorkJoules) || availableWorkJoules < 0d ||
            !FluidUnits.IsFinite(efficiency) || efficiency <= 0d || efficiency > 1d) return default;
        var definition = (catalog ?? FluidCatalog.Default).Find(batch.FluidId);
        if (targetPressureKPa <= sourcePressureKPa) return new FluidCompressionResult(batch, 0d, 0d);
        double temperature = (batch.InternalEnergyJoules - (double)batch.GasMoles * definition.GasEnergyOffsetJPerMol) /
                             ((double)batch.GasMoles * definition.GasHeatCapacityJPerMolKelvin);
        double gamma = 1d + FluidUnits.GasConstant / definition.GasHeatCapacityJPerMolKelvin;
        double targetTemperature = temperature * Math.Pow(targetPressureKPa / sourcePressureKPa, (gamma - 1d) / gamma);
        double energyIncrease = (double)batch.GasMoles * definition.GasHeatCapacityJPerMolKelvin * (targetTemperature - temperature);
        double requiredWork = energyIncrease / efficiency;
        if (!FluidUnits.IsFinite(requiredWork) || requiredWork <= 0d || availableWorkJoules <= 0d || temperature <= 0d) return default;
        double fraction = Math.Min(1d, availableWorkJoules / requiredWork);
        var transferred = batch.Scale((decimal)fraction);
        double actualIncrease = energyIncrease * fraction;
        double actualWork = Math.Min(availableWorkJoules, requiredWork * fraction);
        return new FluidCompressionResult(new FluidBatch(batch.FluidId, transferred.GasMoles, 0m,
            transferred.InternalEnergyJoules + actualIncrease), actualWork, actualWork - actualIncrease);
    }
    #endregion

    #region 同腔相变与分压近似
    public static FluidPhaseChangeResult AdvancePhaseChange(FluidInventory inventory, double volumeLiters,
        double minimumGasSpaceLiters, double seconds, double maximumMolesPerSecond,
        bool singleGas = false, bool singleLiquid = false)
    {
        FluidInventory.ValidateGeometry(volumeLiters, minimumGasSpaceLiters);
        var unsupported = new List<string>();
        if (inventory == null || inventory.IsEmpty || !FluidUnits.IsFinite(seconds) || seconds <= 0d ||
            !FluidUnits.IsFinite(maximumMolesPerSecond) || maximumMolesPerSecond <= 0d)
            return new FluidPhaseChangeResult(0m, null, unsupported);
        decimal budget = (decimal)Math.Min((double)FluidInventory.MaximumMoles, maximumMolesPerSecond * seconds);
        decimal converted = 0m;
        string blocked = null;
        var ids = new List<string>();
        foreach (var component in inventory.State.Components) ids.Add(component.FluidId);
        ids.Sort(StringComparer.Ordinal);
        foreach (string id in ids)
        {
            var definition = inventory.Catalog.Find(id);
            var curve = definition.PhaseChange;
            if (curve == null) continue;
            double temperature = inventory.GetTemperatureKelvin();
            if (!curve.TrySaturationPressure(temperature, out double saturation))
            {
                if (inventory.GetLiquidMoles(id) > 0m || temperature < curve.MinimumTemperatureKelvin)
                    unsupported.Add(id);
                continue;
            }
            var component = inventory.FindComponent(id);
            double gasSpace = Math.Max(minimumGasSpaceLiters, volumeLiters - inventory.GetLiquidLiters());
            double error = (double)component.GasMoles * FluidUnits.GasConstant * temperature / gasSpace - saturation;
            if (Math.Abs(error) <= Math.Max(0.00001d, saturation * 0.00001d)) continue;
            bool vaporize = error < 0d;
            decimal source = vaporize ? component.LiquidMoles : component.GasMoles;
            if (source <= 0m) continue;
            if ((vaporize && singleGas && inventory.HasOther(id, FluidPhase.Gas)) ||
                (!vaporize && singleLiquid && inventory.HasOther(id, FluidPhase.Liquid)))
            { blocked ??= "相变目标相已存在另一种物质"; continue; }
            decimal maximum = Math.Min(source, budget);
            if (!vaporize)
            {
                double free = Math.Max(0d, volumeLiters - minimumGasSpaceLiters - inventory.GetLiquidLiters());
                maximum = Math.Min(maximum, FluidUnits.LiquidLitersToMol(definition, (decimal)free));
                if (maximum <= 0m) { blocked ??= "相变目标液相空间已满"; continue; }
            }
            inventory.GetEnergyTerms(out double capacity, out double offset);
            double liquidLiters = inventory.GetLiquidLiters();
            double sign = vaporize ? 1d : -1d;
            decimal low = 0m, high = maximum;
            for (int iteration = 0; iteration < 48; iteration++)
            {
                decimal candidate = (low + high) / 2m;
                double delta = (double)candidate * sign;
                double newCapacity = capacity + delta * (definition.GasHeatCapacityJPerMolKelvin - definition.LiquidHeatCapacityJPerMolKelvin);
                double newOffset = offset + delta * definition.GasEnergyOffsetJPerMol;
                double newTemperature = (inventory.State.TotalInternalEnergyJoules - newOffset) / newCapacity;
                double newGasSpace = volumeLiters - liquidLiters + delta * definition.MolarMassKgPerMol / definition.LiquidDensityKgPerLiter;
                double newSaturation = 0d;
                bool valid = FluidUnits.IsFinite(newTemperature) && newTemperature > 0d && newGasSpace >= minimumGasSpaceLiters - 1e-10d &&
                             curve.TrySaturationPressure(newTemperature, out newSaturation);
                double newError = valid ? ((double)component.GasMoles + delta) * FluidUnits.GasConstant * newTemperature /
                    Math.Max(minimumGasSpaceLiters, newGasSpace) - newSaturation : double.NaN;
                if (valid && newError * error >= 0d) low = candidate; else high = candidate;
            }
            if (low <= 0m) continue;
            if (!inventory.ApplyPhaseChange(id, vaporize ? low : -low))
            { blocked ??= "相变量超出当前精确库存分辨率"; continue; }
            converted += low;
            budget -= low;
            if (budget <= 0m) break;
        }
        return new FluidPhaseChangeResult(converted, blocked, unsupported);
    }
    #endregion
}
