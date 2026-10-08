using System;
using System.Collections.Generic;
using System.IO;

/// <summary>一颗星球一份有限来源；输出超额逸散，容量不会在抽排中扩大。</summary>
public static class AtmosphereService
{
    #region 精确物质量和查询
    public const decimal MinimumMoles = 0.000000001m;
    public static decimal Quantize(decimal value) => Math.Floor(value / MinimumMoles) * MinimumMoles;
    public static AtmosphereState GetOrCreate(PlanetData planet, string profileId = null)
    {
        if (planet == null) throw new ArgumentNullException(nameof(planet));
        if (planet.Atmosphere == null)
        {
            string id = profileId ?? planet.AtmosphereProfileId ?? AtmosphereCatalog.DefaultProfileId;
            planet.Atmosphere = AtmosphereCatalog.Default.Find(id).CreateState();
            planet.AtmosphereProfileId = id;
        }
        Validate(planet.Atmosphere, FluidCatalog.Default);
        return planet.Atmosphere;
    }
    public static bool TryGetForWorld(string worldKey, out AtmosphereState state)
    {
        state = null;
        if (string.IsNullOrWhiteSpace(worldKey)) return false;
        var saves = SaveDataMgr.Instance?.SaveData;
        if (saves?.PlanetData_Dict == null) return false;
        var address = WorldAddress.FromWorldKey(worldKey);
        if (!saves.PlanetData_Dict.TryGetValue(address.PlanetId, out var planet) || planet == null) return false;
        state = GetOrCreate(planet); return true;
    }
    public static double PressureKPa(AtmosphereState state) => state == null || state.CapacityMoles == 0m ||
        state.ReferenceMoles <= 0m ? 0d : state.ReferencePressureKPa * (double)(state.CurrentMoles / state.ReferenceMoles);
    public static double MaximumPressureKPa(AtmosphereState state) => state == null || state.CapacityMoles == 0m ||
        state.ReferenceMoles <= 0m ? 0d : state.ReferencePressureKPa * (double)(state.CapacityMoles / state.ReferenceMoles);
    public static decimal GetMoles(AtmosphereState state, string fluidId)
    {
        if (state == null) return 0m;
        foreach (var gas in state.Gases) if (FluidInventory.Same(gas.FluidId, fluidId)) return gas.Moles;
        return 0m;
    }
    public static bool CanBreathe(AtmosphereState state, string oxygenId = FluidIds.Oxygen,
        double minimumOxygenPartialPressureKPa = 16d, double maximumCarbonDioxidePartialPressureKPa = 1d)
    {
        if (state == null || state.CurrentMoles <= 0m || !FluidUnits.IsFinite(minimumOxygenPartialPressureKPa) ||
            !FluidUnits.IsFinite(maximumCarbonDioxidePartialPressureKPa) || minimumOxygenPartialPressureKPa < 0d ||
            maximumCarbonDioxidePartialPressureKPa < 0d) return false;
        double pressure = PressureKPa(state);
        double oxygen = pressure * (double)(GetMoles(state, oxygenId) / state.CurrentMoles);
        double carbonDioxide = pressure * (double)(GetMoles(state, FluidIds.CarbonDioxide) / state.CurrentMoles);
        return oxygen >= minimumOxygenPartialPressureKPa && carbonDioxide <= maximumCarbonDioxidePartialPressureKPa;
    }
    public static IReadOnlyList<AtmosphereGasSnapshot> GetComposition(AtmosphereState state, FluidCatalog catalog = null)
    {
        var result = new List<AtmosphereGasSnapshot>();
        if (state == null || state.CurrentMoles <= 0m) return result;
        catalog ??= FluidCatalog.Default;
        double pressure = PressureKPa(state);
        foreach (var gas in state.Gases)
        {
            decimal fraction = gas.Moles / state.CurrentMoles;
            double density = pressure * (double)fraction * catalog.Find(gas.FluidId).MolarMassKgPerMol /
                             (FluidUnits.GasConstant * state.TemperatureKelvin);
            result.Add(new AtmosphereGasSnapshot(gas.FluidId, gas.Moles, fraction, density));
        }
        return result;
    }
    #endregion

    #region 抽气与排放权威事务
    public static bool TryExtract(AtmosphereState state, decimal standardLiters, ref ulong randomState, out FluidBatch batch)
    {
        batch = default;
        if (!TryChoose(state, standardLiters, randomState, out var source, out var selected, out ulong nextRandom)) return false;
        source.Moles -= selected.GasMoles; randomState = nextRandom; batch = selected;
        return true;
    }
    public static bool TryExtractTo(AtmosphereState state, decimal standardLiters, ref ulong randomState,
        FluidInventory target, double targetVolumeLiters, double targetMinimumGasSpaceLiters, out FluidBatch batch)
    {
        batch = default;
        if (target == null || !TryChoose(state, standardLiters, randomState, out var source, out var selected, out ulong nextRandom) ||
            !target.CanAdd(selected, targetVolumeLiters, targetMinimumGasSpaceLiters, true, true)) return false;
        target.TryAdd(selected, targetVolumeLiters, targetMinimumGasSpaceLiters, true, true);
        source.Moles -= selected.GasMoles; randomState = nextRandom; batch = selected;
        return true;
    }
    private static bool TryChoose(AtmosphereState state, decimal standardLiters, ulong randomState,
        out AtmosphereGasAmount source, out FluidBatch batch, out ulong nextRandom)
    {
        source = null; batch = default; nextRandom = randomState;
        if (state == null || standardLiters <= 0m || standardLiters > FluidUnits.MolToStandardLiters(FluidInventory.MaximumMoles) ||
            state.CurrentMoles <= 0m) return false;
        decimal requestedMoles = Quantize(FluidUnits.StandardLitersToMol(standardLiters));
        if (requestedMoles <= 0m) return false;
        decimal roll = FluidRandom.Next01(ref nextRandom) * state.CurrentMoles;
        foreach (var gas in state.Gases)
        {
            if (gas.Moles <= 0m) continue;
            source = gas;
            if (roll < gas.Moles) break;
            roll -= gas.Moles;
        }
        if (source == null) return false;
        decimal actualMoles = Math.Min(requestedMoles, source.Moles);
        batch = FluidInventory.CreateBatch(FluidCatalog.Default.Find(source.FluidId), actualMoles, 0m, state.TemperatureKelvin);
        return true;
    }
    public static AtmosphereEmissionResult Emit(AtmosphereState state, FluidBatch batch)
    {
        if (batch.GasMoles < 0m || batch.GasMoles > FluidInventory.MaximumMoles ||
            !FluidCatalog.Default.TryGet(batch.FluidId, out _)) throw new InvalidDataException("排气批次的身份或气相数量无效");
        if (batch.GasMoles == 0m) return default;
        if (state == null) return new AtmosphereEmissionResult(batch.GasMoles, 0m);
        decimal retained = Quantize(Math.Min(batch.GasMoles, Math.Max(0m, state.CapacityMoles - state.CurrentMoles)));
        if (retained > 0m)
        {
            AtmosphereGasAmount component = null;
            foreach (var gas in state.Gases) if (FluidInventory.Same(gas.FluidId, batch.FluidId)) { component = gas; break; }
            if (component == null) { component = new AtmosphereGasAmount { FluidId = batch.FluidId }; state.Gases.Add(component); }
            component.Moles += retained;
        }
        state.TotalEscapedMoles += batch.GasMoles - retained;
        return new AtmosphereEmissionResult(batch.GasMoles, retained);
    }
    #endregion

    #region 恢复与配置预检
    public static void Validate(AtmosphereState state, FluidCatalog catalog)
    {
        if (state == null || state.Gases == null || state.Gases.Count > 4096 || state.CapacityMoles < 0m ||
            state.CapacityMoles > FluidInventory.MaximumMoles || state.ReferenceMoles < 0m ||
            state.ReferenceMoles > FluidInventory.MaximumMoles || state.TotalEscapedMoles < 0m ||
            !FluidUnits.IsFinite(state.ReferencePressureKPa) || state.ReferencePressureKPa < 0d || state.ReferencePressureKPa > 1e20d ||
            !FluidUnits.IsFinite(state.TemperatureKelvin) || state.TemperatureKelvin <= 0d ||
            (state.CapacityMoles > 0m && (state.ReferenceMoles < MinimumMoles || state.ReferencePressureKPa < 1e-12d)))
            throw new InvalidDataException("星球大气容量、基准或温度无效");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        decimal total = 0m;
        foreach (var gas in state.Gases)
        {
            if (gas == null || !catalog.TryGet(gas.FluidId, out _) || !ids.Add(gas.FluidId) ||
                gas.Moles < 0m || gas.Moles > FluidInventory.MaximumMoles || Quantize(gas.Moles) != gas.Moles)
                throw new InvalidDataException($"大气存在未知、重复或非精确单位的成分：{gas?.FluidId}");
            total += gas.Moles;
        }
        if (total > state.CapacityMoles || Quantize(state.CapacityMoles) != state.CapacityMoles)
            throw new InvalidDataException("星球大气库存超过固定容量或容量单位无效");
    }
    #endregion
}
