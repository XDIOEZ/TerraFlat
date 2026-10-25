using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using FlatWorld.Spaceflight;
using UnityEngine;

/// <summary>便携气罐从真实环境转移混合气体；压力求解和环境所有权互不耦合。</summary>
public static class PortableGasTankAirFilling
{
    #region 配置能力、操作资格与权威入口
    public const double TargetPressureKPa = 101.325d;
    private const double PressureToleranceKPa = .00001d;
    public static event Func<Player, int, bool> RemoteFillRequested;

    private static bool CanAccess(Item tank, Item actor)
    {
        if (tank?.itemData == null || actor == null || tank.DestructionHandled ||
            actor.DestructionHandled || !FluidTankStorage.AllowsManualAirFill(tank.itemData) ||
            tank.gameObject.scene.name != actor.gameObject.scene.name) return false;
        if (tank.Owner != null)
        {
            for (Item owner = tank.Owner; owner != null; owner = owner.Owner)
                if (owner == actor) return true;
            return false;
        }
        // 只允许操作已安装的世界建筑，散落气罐不能绕过放置事务直接充装。
        Mod_Building building = tank.itemMods?.GetMod_ByID<Mod_Building>(ModText.Building);
        if (building?.IsInstalled() != true || building.IsDismantlePending)
            return false;
        return WorldTopologyRuntime.Distance(
            WorldLocalPresentation.ToLogical(actor.transform.position),
            WorldLocalPresentation.ToLogical(tank.transform.position)) <= Mod_InteractSender.DefaultMaxInteractDistance + .05f;
    }

    public static bool CanFill(Item tank, Item actor)
    {
        if (!CanAccess(tank, actor) ||
            !FluidTankStorage.TryGet(tank.itemData, out FluidInventory contents, out FluidTankConfiguration config))
            return false;
        return TryGetSource(tank, out IShipAtmosphereSource source) &&
            CanFillFromSource(source, contents, config.VolumeLiters, config.MinimumGasSpaceLiters);
    }

    public static bool Fill(Item tank, Player actor)
    {
        if (tank == null || actor == null || !FluidTankStorage.AllowsManualAirFill(tank.itemData)) return false;
        if (!GameNetwork.HasStateAuthority)
            return CanAccess(tank, actor) && RemoteFillRequested?.Invoke(actor, tank.itemData.Guid) == true;
        if (!CanFill(tank, actor) ||
            !FluidTankStorage.TryGet(tank.itemData, out FluidInventory contents, out FluidTankConfiguration config) ||
            !TryGetSource(tank, out IShipAtmosphereSource source) ||
            !TryFillFromSource(source, contents, config.VolumeLiters, config.MinimumGasSpaceLiters))
            return false;
        FluidTankStorage.Write(tank.itemData, contents);
        if (tank.Owner != null) FluidTankStorage.NotifyOwner(actor, tank.itemData);
        else
        {
            tank.OnUIRefresh?.Invoke();
            ItemNetworkStateSerialization.NotifyRuntimeStateChanged(tank);
        }
        return true;
    }

    private static bool TryGetSource(Item tank, out IShipAtmosphereSource source)
    {
        source = null;
        if (tank == null) return false;
        if (SpaceSession.Current != null)
            return SpaceSession.Current.TryGetPortableTankAirSource(tank, out source);
        if (!AtmosphereService.TryGetForWorld(tank.gameObject.scene.name, out AtmosphereState atmosphere))
            return false;
        source = new PlanetShipAtmosphereSource(atmosphere, atmosphere.TemperatureKelvin - 273.15d);
        return true;
    }
    #endregion

    #region 真实混合气体与目标压力事务
    public static double GetTargetPressureKPa(Item tank) =>
        TryGetSource(tank, out IShipAtmosphereSource source) ? GetTargetPressureKPa(source) : 0d;

    // 自然补气以实际环境气压和一标准大气压中较低者为上限，100 kPa 环境也能使用。
    private static double GetTargetPressureKPa(IShipAtmosphereSource source) =>
        source != null && FluidUnits.IsFinite(source.PressureKPa)
            ? Math.Max(0d, Math.Min(TargetPressureKPa, source.PressureKPa)) : 0d;

    public static bool CanFillFromSource(IShipAtmosphereSource source, FluidInventory target,
        double volumeLiters, double minimumGasSpaceLiters)
    {
        if (source == null || target == null || target.GasMoles >= FluidInventory.MaximumMoles ||
            !FluidUnits.IsFinite(source.TemperatureCelsius) || source.TemperatureCelsius <= -273.15d ||
            !FluidUnits.IsFinite(volumeLiters) || !FluidUnits.IsFinite(minimumGasSpaceLiters) ||
            volumeLiters <= 0d || minimumGasSpaceLiters <= 0d || minimumGasSpaceLiters >= volumeLiters)
            return false;
        return target.GetPressureKPa(volumeLiters, minimumGasSpaceLiters) <
            GetTargetPressureKPa(source) - PressureToleranceKPa;
    }

    // 先取有限气源的实际组分，在影子库存中二分压力，余量退回来源后才提交气罐。
    public static bool TryFillFromSource(IShipAtmosphereSource source, FluidInventory target,
        double volumeLiters, double minimumGasSpaceLiters)
    {
        if (!CanFillFromSource(source, target, volumeLiters, minimumGasSpaceLiters)) return false;
        double targetPressure = GetTargetPressureKPa(source);
        double sourceKelvin = source.TemperatureCelsius + 273.15d;
        if (!FluidUnits.IsFinite(sourceKelvin) || sourceKelvin <= 0d) return false;
        double gasSpace = Math.Max(minimumGasSpaceLiters, volumeLiters - target.GetLiquidLiters());
        double expectedMoles = targetPressure * gasSpace / (FluidUnits.GasConstant * sourceKelvin);
        if (!FluidUnits.IsFinite(expectedMoles) || expectedMoles <= 0d) return false;
        decimal requested = AtmosphereService.Quantize((decimal)Math.Min((double)FluidInventory.MaximumMoles,
            expectedMoles * 2d + (double)AtmosphereService.MinimumMoles * 2d));
        if (requested <= 0m) return false;

        IReadOnlyList<FluidBatch> drawn = source.TakeGas(requested);
        if (drawn == null || drawn.Count == 0) return false;
        FluidInventoryState before = target.State.Clone();
        if (!TryStage(before, target.Catalog, drawn, volumeLiters, minimumGasSpaceLiters, targetPressure, 1m, out FluidInventoryState accepted))
        {
            decimal low = 0m, high = 1m;
            for (int step = 0; step < 52; step++)
            {
                decimal middle = (low + high) / 2m;
                if (TryStage(before, target.Catalog, drawn, volumeLiters, minimumGasSpaceLiters, targetPressure, middle, out _))
                    low = middle;
                else high = middle;
            }
            if (!TryStage(before, target.Catalog, drawn, volumeLiters, minimumGasSpaceLiters, targetPressure, low, out accepted))
            {
                ReturnUnused(source, drawn, 0m);
                return false;
            }
            if (low <= 0m)
            {
                ReturnUnused(source, drawn, 0m);
                return false;
            }
            ReturnUnused(source, drawn, low);
        }
        // 影子库存已经预检混合后的真实温压，提交时只替换同一份持久内容。
        target.Restore(accepted);
        return true;
    }

    private static bool TryStage(FluidInventoryState original, FluidCatalog catalog,
        IReadOnlyList<FluidBatch> drawn, double volume, double minimum, double targetPressure, decimal fraction,
        out FluidInventoryState accepted)
    {
        accepted = null;
        var trial = new FluidInventory(original.Clone(), catalog);
        bool added = false;
        foreach (FluidBatch gas in drawn)
        {
            if (!gas.IsGas || !catalog.TryGet(gas.FluidId, out _) ||
                !FluidUnits.IsFinite(gas.InternalEnergyJoules)) return false;
            decimal moles = AtmosphereService.Quantize(gas.GasMoles * fraction);
            if (moles <= 0m) continue;
            var piece = Portion(gas, moles);
            if (!trial.TryAdd(piece, volume, minimum)) return false;
            added = true;
        }
        if (!added || trial.GetPressureKPa(volume, minimum) > targetPressure) return false;
        accepted = trial.State;
        return true;
    }

    private static FluidBatch Portion(FluidBatch gas, decimal moles) =>
        new(gas.FluidId, moles, 0m, gas.InternalEnergyJoules * (double)(moles / gas.GasMoles));

    private static void ReturnUnused(IShipAtmosphereSource source, IReadOnlyList<FluidBatch> batches, decimal fraction)
    {
        foreach (FluidBatch gas in batches)
        {
            decimal accepted = AtmosphereService.Quantize(gas.GasMoles * fraction);
            decimal unused = gas.GasMoles - accepted;
            if (unused > 0m) source.ReleaseGas(Portion(gas, unused));
        }
    }
    #endregion
}
