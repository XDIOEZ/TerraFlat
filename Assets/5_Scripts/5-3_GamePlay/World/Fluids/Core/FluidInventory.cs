using System;
using System.Collections.Generic;
using System.IO;

/// <summary>所有载体共用的权威两相库存；预留和正式扣量分开办理。</summary>
public sealed class FluidInventory
{
    #region 状态与派生读数
    public const decimal MaximumMoles = 1000000000000000000m;
    public FluidInventory(FluidInventoryState state = null, FluidCatalog catalog = null)
    {
        State = state ?? new FluidInventoryState(); Catalog = catalog ?? FluidCatalog.Default;
        ValidateState(State, Catalog);
    }
    public FluidInventoryState State { get; }
    public FluidCatalog Catalog { get; }
    public decimal GasMoles { get { decimal n = 0m; foreach (var c in State.Components) n += c.GasMoles; return n; } }
    public decimal LiquidMoles { get { decimal n = 0m; foreach (var c in State.Components) n += c.LiquidMoles; return n; } }
    public bool IsEmpty => GasMoles == 0m && LiquidMoles == 0m;
    public decimal GetGasMoles(string id) => FindComponent(id)?.GasMoles ?? 0m;
    public decimal GetLiquidMoles(string id) => FindComponent(id)?.LiquidMoles ?? 0m;
    public decimal GetAvailableMoles(string id, FluidPhase phase)
    {
        decimal amount = phase == FluidPhase.Gas ? GetGasMoles(id) : GetLiquidMoles(id);
        foreach (var port in State.Ports)
            if (Same(port.FluidId, id)) amount -= phase == FluidPhase.Gas ? port.GasMoles : port.LiquidMoles;
        return Math.Max(0m, amount);
    }
    public double GetLiquidLiters()
    {
        double result = 0d;
        foreach (var component in State.Components)
            result += (double)FluidUnits.MolToLiquidLiters(Catalog.Find(component.FluidId), component.LiquidMoles);
        return result;
    }
    public double GetTemperatureKelvin()
    {
        if (IsEmpty) return FluidUnits.ReferenceTemperatureKelvin;
        GetEnergyTerms(out double capacity, out double offset);
        return (State.TotalInternalEnergyJoules - offset) / capacity;
    }
    public double GetPressureKPa(double volumeLiters, double minimumGasSpaceLiters)
    {
        ValidateGeometry(volumeLiters, minimumGasSpaceLiters);
        if (GasMoles == 0m) return 0d;
        double gasSpace = Math.Max(minimumGasSpaceLiters, volumeLiters - GetLiquidLiters());
        double pressure = (double)GasMoles / gasSpace * FluidUnits.GasConstant * GetTemperatureKelvin();
        if (!FluidUnits.IsFinite(pressure)) throw new InvalidDataException("流体气压超出首轮有限数值范围");
        return pressure;
    }
    public static FluidBatch CreateBatch(FluidDefinition definition, decimal gasMoles, decimal liquidMoles,
        double temperatureKelvin)
    {
        if (definition == null || !FluidUnits.IsFinite(temperatureKelvin) || temperatureKelvin <= 0d)
            throw new ArgumentOutOfRangeException(nameof(temperatureKelvin));
        return new FluidBatch(definition.Id, gasMoles, liquidMoles, definition.EnergyAt(gasMoles, liquidMoles, temperatureKelvin));
    }
    #endregion

    #region 接收与真实提取
    public bool CanAdd(FluidBatch batch, double volumeLiters, double minimumGasSpaceLiters,
        bool singleGas = false, bool singleLiquid = false)
    {
        ValidateGeometry(volumeLiters, minimumGasSpaceLiters);
        if (!IsValidBatch(batch) || !Catalog.TryGet(batch.FluidId, out var definition)) return false;
        double batchCapacity = (double)batch.GasMoles * definition.GasHeatCapacityJPerMolKelvin +
                               (double)batch.LiquidMoles * definition.LiquidHeatCapacityJPerMolKelvin;
        double batchOffset = (double)batch.GasMoles * definition.GasEnergyOffsetJPerMol;
        double batchTemperature = (batch.InternalEnergyJoules - batchOffset) / batchCapacity;
        if (!FluidUnits.IsFinite(batchTemperature) || batchTemperature <= 0d) return false;
        if (GasMoles + LiquidMoles + batch.TotalMoles > MaximumMoles) return false;
        var existing = FindComponent(batch.FluidId);
        if (existing != null &&
            (batch.GasMoles > 0m && existing.GasMoles + batch.GasMoles == existing.GasMoles ||
             batch.LiquidMoles > 0m && existing.LiquidMoles + batch.LiquidMoles == existing.LiquidMoles)) return false;
        if (singleGas && batch.GasMoles > 0m && HasOther(batch.FluidId, FluidPhase.Gas)) return false;
        if (singleLiquid && batch.LiquidMoles > 0m && HasOther(batch.FluidId, FluidPhase.Liquid)) return false;
        if (GetLiquidLiters() + (double)FluidUnits.MolToLiquidLiters(definition, batch.LiquidMoles) >
            volumeLiters - minimumGasSpaceLiters + 1e-10d) return false;
        GetEnergyTerms(out double capacity, out double offset);
        capacity += (double)batch.GasMoles * definition.GasHeatCapacityJPerMolKelvin +
                    (double)batch.LiquidMoles * definition.LiquidHeatCapacityJPerMolKelvin;
        offset += (double)batch.GasMoles * definition.GasEnergyOffsetJPerMol;
        double temperature = (State.TotalInternalEnergyJoules + batch.InternalEnergyJoules - offset) / capacity;
        double gasSpace = Math.Max(minimumGasSpaceLiters,
            volumeLiters - GetLiquidLiters() - (double)FluidUnits.MolToLiquidLiters(definition, batch.LiquidMoles));
        double pressure = (double)(GasMoles + batch.GasMoles) / gasSpace * FluidUnits.GasConstant * temperature;
        return FluidUnits.IsFinite(temperature) && temperature > 0d && FluidUnits.IsFinite(pressure);
    }
    public bool TryAdd(FluidBatch batch, double volumeLiters, double minimumGasSpaceLiters,
        bool singleGas = false, bool singleLiquid = false)
    {
        if (!CanAdd(batch, volumeLiters, minimumGasSpaceLiters, singleGas, singleLiquid)) return false;
        var component = FindComponent(batch.FluidId);
        if (component == null) { component = new FluidComponentState { FluidId = batch.FluidId }; State.Components.Add(component); }
        component.GasMoles += batch.GasMoles; component.LiquidMoles += batch.LiquidMoles;
        State.TotalInternalEnergyJoules += batch.InternalEnergyJoules;
        return true;
    }
    public bool TryTakeExact(string id, FluidPhase phase, decimal moles, out FluidBatch batch)
    {
        batch = default;
        if ((phase != FluidPhase.Gas && phase != FluidPhase.Liquid) || moles <= 0m || GetAvailableMoles(id, phase) < moles) return false;
        var component = FindComponent(id);
        batch = CurrentBatch(component.FluidId, phase == FluidPhase.Gas ? moles : 0m,
            phase == FluidPhase.Liquid ? moles : 0m);
        if (!CanRemove(batch)) { batch = default; return false; }
        Remove(batch); return true;
    }
    public void ClearGas(out List<FluidBatch> batches)
    {
        batches = new List<FluidBatch>();
        double temperature = GetTemperatureKelvin();
        foreach (var component in State.Components)
        {
            if (component.GasMoles <= 0m) continue;
            var batch = CreateBatch(Catalog.Find(component.FluidId), component.GasMoles, 0m, temperature);
            batches.Add(batch); State.TotalInternalEnergyJoules -= batch.InternalEnergyJoules; component.GasMoles = 0m;
        }
        foreach (var port in State.Ports) port.GasMoles = 0m;
        TrimEmpty();
    }
    public List<FluidBatch> Drain()
    {
        var batches = new List<FluidBatch>();
        double temperature = GetTemperatureKelvin();
        foreach (var component in State.Components)
            if (component.GasMoles + component.LiquidMoles > 0m)
                batches.Add(CreateBatch(Catalog.Find(component.FluidId), component.GasMoles, component.LiquidMoles, temperature));
        State.Components.Clear(); State.Ports.Clear(); State.TotalInternalEnergyJoules = 0d;
        return batches;
    }
    public void Restore(FluidInventoryState snapshot)
    {
        ValidateState(snapshot, Catalog);
        var copy = snapshot.Clone();
        State.Components = copy.Components; State.Ports = copy.Ports;
        State.RandomState = copy.RandomState; State.TotalInternalEnergyJoules = copy.TotalInternalEnergyJoules;
    }
    #endregion

    #region 每口独立预留与输出
    public bool TryReserve(string portId, FluidPhase phase, decimal maximumMoles, out FluidBatch batch)
    {
        batch = default;
        if ((phase != FluidPhase.Gas && phase != FluidPhase.Liquid) || string.IsNullOrWhiteSpace(portId) || maximumMoles <= 0m) return false;
        var existing = GetReservation(portId);
        if (existing != null)
        { batch = CurrentBatch(existing.FluidId, existing.GasMoles, existing.LiquidMoles); return true; }
        decimal sum = 0m;
        foreach (var component in State.Components)
        {
            decimal amount = GetAvailableMoles(component.FluidId, phase);
            sum += phase == FluidPhase.Gas ? amount : FluidUnits.MolToServings(Catalog.Find(component.FluidId), amount);
        }
        if (sum <= 0m) return false;
        ulong random = State.RandomState;
        decimal roll = FluidRandom.Next01(ref random) * sum;
        FluidComponentState chosen = null;
        foreach (var component in State.Components)
        {
            decimal amount = GetAvailableMoles(component.FluidId, phase);
            decimal weight = phase == FluidPhase.Gas ? amount : FluidUnits.MolToServings(Catalog.Find(component.FluidId), amount);
            if (weight <= 0m) continue;
            chosen = component;
            if (roll < weight) break;
            roll -= weight;
        }
        if (chosen == null) return false;
        decimal reserved = Math.Min(maximumMoles, GetAvailableMoles(chosen.FluidId, phase));
        var port = new FluidOutputReservation
        { PortId = portId, FluidId = chosen.FluidId, GasMoles = phase == FluidPhase.Gas ? reserved : 0m,
            LiquidMoles = phase == FluidPhase.Liquid ? reserved : 0m };
        State.Ports.Add(port); State.RandomState = random;
        batch = CurrentBatch(port.FluidId, port.GasMoles, port.LiquidMoles); return true;
    }
    public FluidOutputReservation GetReservation(string portId)
    {
        foreach (var port in State.Ports) if (string.Equals(port.PortId, portId, StringComparison.Ordinal)) return port;
        return null;
    }
    public void ReleaseReservation(string portId)
    {
        var port = GetReservation(portId);
        if (port != null) State.Ports.Remove(port);
    }
    public bool TryTransfer(string portId, FluidInventory target, decimal maximumMoles, double targetVolumeLiters,
        double targetMinimumGasSpaceLiters, bool singleGas = false, bool singleLiquid = false) =>
        TryTransfer(portId, target, maximumMoles, targetVolumeLiters, targetMinimumGasSpaceLiters, out _, singleGas, singleLiquid);
    public bool TryTransfer(string portId, FluidInventory target, decimal maximumMoles, double targetVolumeLiters,
        double targetMinimumGasSpaceLiters, out FluidBatch transferred, bool singleGas = false, bool singleLiquid = false)
    {
        transferred = default;
        var port = GetReservation(portId);
        if (port == null || target == null || ReferenceEquals(target.State, State) || maximumMoles <= 0m) return false;
        FluidBatch batch = CurrentBatch(port.FluidId, port.GasMoles, port.LiquidMoles);
        decimal fraction = Math.Min(1m, maximumMoles / batch.TotalMoles);
        if (batch.LiquidMoles > 0m)
        {
            ValidateGeometry(targetVolumeLiters, targetMinimumGasSpaceLiters);
            double free = Math.Max(0d, targetVolumeLiters - targetMinimumGasSpaceLiters - target.GetLiquidLiters());
            double required = (double)FluidUnits.MolToLiquidLiters(Catalog.Find(batch.FluidId), batch.LiquidMoles);
            fraction = Math.Min(fraction, (decimal)Math.Min(1d, free / required));
        }
        if (fraction <= 0m) return false;
        batch = batch.Scale(fraction);
        if (!CanRemove(batch) ||
            !target.CanAdd(batch, targetVolumeLiters, targetMinimumGasSpaceLiters, singleGas, singleLiquid)) return false;
        target.TryAdd(batch, targetVolumeLiters, targetMinimumGasSpaceLiters, singleGas, singleLiquid);
        port.GasMoles -= batch.GasMoles; port.LiquidMoles -= batch.LiquidMoles;
        Remove(batch); transferred = batch; return true;
    }
    public bool TryTakeReserved(string portId, decimal maximumMoles, out FluidBatch batch)
    {
        batch = default;
        var port = GetReservation(portId);
        if (port == null || maximumMoles <= 0m) return false;
        batch = CurrentBatch(port.FluidId, port.GasMoles, port.LiquidMoles).Scale(Math.Min(1m, maximumMoles / port.TotalMoles));
        if (!CanRemove(batch)) { batch = default; return false; }
        port.GasMoles -= batch.GasMoles; port.LiquidMoles -= batch.LiquidMoles;
        Remove(batch); return true;
    }
    #endregion

    #region 内能和相变的统一维护
    internal void GetEnergyTerms(out double capacity, out double offset)
    {
        capacity = 0d; offset = 0d;
        foreach (var component in State.Components)
        {
            var definition = Catalog.Find(component.FluidId);
            capacity += (double)component.GasMoles * definition.GasHeatCapacityJPerMolKelvin +
                        (double)component.LiquidMoles * definition.LiquidHeatCapacityJPerMolKelvin;
            offset += (double)component.GasMoles * definition.GasEnergyOffsetJPerMol;
        }
    }
    internal bool HasOther(string id, FluidPhase phase)
    {
        foreach (var component in State.Components)
            if (!Same(component.FluidId, id) && (phase == FluidPhase.Gas ? component.GasMoles : component.LiquidMoles) > 0m) return true;
        return false;
    }
    internal bool ApplyPhaseChange(string id, decimal gasIncrease)
    {
        var component = FindComponent(id);
        decimal newGas = component.GasMoles + gasIncrease, newLiquid = component.LiquidMoles - gasIncrease;
        if (newGas < 0m || newLiquid < 0m || gasIncrease != 0m &&
            (newGas == component.GasMoles || newLiquid == component.LiquidMoles)) return false;
        decimal source = gasIncrease >= 0m ? component.LiquidMoles : component.GasMoles;
        decimal reserved = 0m;
        foreach (var port in State.Ports)
            if (Same(port.FluidId, id)) reserved += gasIncrease >= 0m ? port.LiquidMoles : port.GasMoles;
        decimal remaining = Math.Max(0m, Math.Abs(gasIncrease) - (source - reserved));
        // 先转换未预留份额，再按保存的端口顺序更新批次，避免比例舍入产生重复预留。
        foreach (var port in State.Ports)
        {
            if (!Same(port.FluidId, id)) continue;
            decimal converted = Math.Min(remaining, gasIncrease >= 0m ? port.LiquidMoles : port.GasMoles);
            if (gasIncrease >= 0m) { port.LiquidMoles -= converted; port.GasMoles += converted; }
            else { port.GasMoles -= converted; port.LiquidMoles += converted; }
            remaining -= converted;
        }
        component.GasMoles = newGas; component.LiquidMoles = newLiquid;
        return true;
    }
    internal FluidComponentState FindComponent(string id)
    {
        foreach (var component in State.Components) if (Same(component.FluidId, id)) return component;
        return null;
    }
    private FluidBatch CurrentBatch(string id, decimal gas, decimal liquid) =>
        CreateBatch(Catalog.Find(id), gas, liquid, GetTemperatureKelvin());
    private bool CanRemove(FluidBatch batch)
    {
        // 数量小到无法改变真实库存时拒绝提交，不能只搬热量或生成免费物料。
        if (batch.IsEmpty) return false;
        var component = FindComponent(batch.FluidId);
        return component != null && component.GasMoles >= batch.GasMoles && component.LiquidMoles >= batch.LiquidMoles &&
            (batch.GasMoles == 0m || component.GasMoles - batch.GasMoles != component.GasMoles) &&
            (batch.LiquidMoles == 0m || component.LiquidMoles - batch.LiquidMoles != component.LiquidMoles);
    }
    private void Remove(FluidBatch batch)
    {
        var component = FindComponent(batch.FluidId);
        component.GasMoles -= batch.GasMoles; component.LiquidMoles -= batch.LiquidMoles;
        State.TotalInternalEnergyJoules -= batch.InternalEnergyJoules;
        TrimEmpty();
    }
    private void TrimEmpty()
    {
        State.Components.RemoveAll(c => c.GasMoles == 0m && c.LiquidMoles == 0m);
        State.Ports.RemoveAll(p => p.TotalMoles == 0m);
        if (State.Components.Count == 0) State.TotalInternalEnergyJoules = 0d;
    }
    internal static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    internal static void ValidateGeometry(double volume, double minimum)
    {
        if (!FluidUnits.IsFinite(volume) || !FluidUnits.IsFinite(minimum) || minimum < 1e-12d || volume <= minimum || volume > 1e12d)
            throw new ArgumentOutOfRangeException(nameof(volume), "流体几何容积必须大于最小正气相空间");
    }
    private static bool IsValidBatch(FluidBatch batch) => !string.IsNullOrWhiteSpace(batch.FluidId) &&
        batch.GasMoles >= 0m && batch.LiquidMoles >= 0m && batch.GasMoles <= MaximumMoles && batch.LiquidMoles <= MaximumMoles &&
        batch.TotalMoles > 0m && batch.TotalMoles <= MaximumMoles &&
        FluidUnits.IsFinite(batch.InternalEnergyJoules) && batch.InternalEnergyJoules > 0d;
    public static void ValidateState(FluidInventoryState state, FluidCatalog catalog)
    {
        if (state == null || state.Components == null || state.Ports == null ||
            state.Components.Count > 4096 || state.Ports.Count > 65536 || !FluidUnits.IsFinite(state.TotalInternalEnergyJoules))
            throw new InvalidDataException("流体库存结构或内能无效");
        var components = new Dictionary<string, FluidComponentState>(StringComparer.OrdinalIgnoreCase);
        decimal total = 0m; double capacity = 0d, offset = 0d;
        foreach (var component in state.Components)
        {
            if (component == null || !catalog.TryGet(component.FluidId, out var definition) ||
                component.GasMoles < 0m || component.LiquidMoles < 0m || component.GasMoles > MaximumMoles ||
                component.LiquidMoles > MaximumMoles || !components.TryAdd(component.FluidId, component))
                throw new InvalidDataException($"流体库存出现未知身份、负量或重复项：{component?.FluidId}");
            total += component.GasMoles + component.LiquidMoles;
            capacity += (double)component.GasMoles * definition.GasHeatCapacityJPerMolKelvin +
                        (double)component.LiquidMoles * definition.LiquidHeatCapacityJPerMolKelvin;
            offset += (double)component.GasMoles * definition.GasEnergyOffsetJPerMol;
        }
        if (total > MaximumMoles || (total == 0m ? state.TotalInternalEnergyJoules != 0d :
            !FluidUnits.IsFinite((state.TotalInternalEnergyJoules - offset) / capacity) || state.TotalInternalEnergyJoules <= offset))
            throw new InvalidDataException("流体物质量或推导温度无效");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var reservedGas = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var reservedLiquid = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var port in state.Ports)
        {
            if (port == null || string.IsNullOrWhiteSpace(port.PortId) || !ids.Add(port.PortId) ||
                !components.TryGetValue(port.FluidId ?? string.Empty, out var component) || port.GasMoles < 0m ||
                port.LiquidMoles < 0m || port.TotalMoles <= 0m)
                throw new InvalidDataException("流体出口预留无效或重复");
            reservedGas.TryGetValue(port.FluidId, out decimal gas); reservedLiquid.TryGetValue(port.FluidId, out decimal liquid);
            gas += port.GasMoles; liquid += port.LiquidMoles;
            if (gas > component.GasMoles || liquid > component.LiquidMoles) throw new InvalidDataException("流体出口重复预留同一份物料");
            reservedGas[port.FluidId] = gas; reservedLiquid[port.FluidId] = liquid;
        }
    }
    #endregion
}
