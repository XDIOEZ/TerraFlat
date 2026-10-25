using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using FlatWorld.Spaceflight;
using UnityEngine;

/// <summary>便携气罐从真实环境转移混合气体；固定容量和环境所有权互不耦合。</summary>
public static class PortableGasTankAirFilling
{
    #region 配置能力、操作资格与权威入口
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
            CanFillFromSource(source, contents, config.GasCapacityStandardLiters, config.NaturalFillFraction);
    }

    public static bool Fill(Item tank, Player actor)
    {
        if (tank == null || actor == null || !FluidTankStorage.AllowsManualAirFill(tank.itemData)) return false;
        if (!GameNetwork.HasStateAuthority)
            return CanAccess(tank, actor) && RemoteFillRequested?.Invoke(actor, tank.itemData.Guid) == true;
        if (!CanFill(tank, actor) ||
            !FluidTankStorage.TryGet(tank.itemData, out FluidInventory contents, out FluidTankConfiguration config) ||
            !TryGetSource(tank, out IShipAtmosphereSource source) ||
            !TryFillFromSource(source, contents, config.GasCapacityStandardLiters, config.NaturalFillFraction))
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

    public static void ReleaseContents(Item tank)
    {
        if (tank == null || !FluidTankStorage.TryGet(tank.itemData, out FluidInventory contents, out _) ||
            !TryGetSource(tank, out IShipAtmosphereSource source)) return;
        // 气罐不运行化学反应；排气直接交给所属环境的正式气体接口。
        foreach (FluidComponentState component in new List<FluidComponentState>(contents.State.Components))
        {
            if (component.GasMoles <= 0m) continue;
            var trial = new FluidInventory(contents.State.Clone(), contents.Catalog);
            if (!trial.TryTakeExact(component.FluidId, FluidPhase.Gas, component.GasMoles, out FluidBatch gas)) continue;
            source.ReleaseGas(gas);
            contents.Restore(trial.State);
            FluidTankStorage.Write(tank.itemData, contents);
        }
        FluidTankStorage.Write(tank.itemData, contents);
    }
    #endregion

    #region 真实混合气体与自然补气上限
    public static bool CanFillFromSource(IShipAtmosphereSource source, FluidInventory target,
        double capacityStandardLiters, double naturalFillFraction)
    {
        if (source == null || target == null || target.LiquidMoles > 0m ||
            !FluidUnits.IsFinite(source.PressureKPa) || source.PressureKPa <= 0d ||
            !FluidUnits.IsFinite(capacityStandardLiters) || capacityStandardLiters <= 0d ||
            !FluidUnits.IsFinite(naturalFillFraction) || naturalFillFraction <= 0d || naturalFillFraction > 1d)
            return false;
        decimal room = FluidUnits.StandardLitersToMol((decimal)(capacityStandardLiters * naturalFillFraction)) - target.GasMoles;
        return AtmosphereService.Quantize(Math.Max(0m, room)) > 0m;
    }

    // 只补到配置占比，实际组分从真实气源扣除；高于上限时保持原存量。
    public static bool TryFillFromSource(IShipAtmosphereSource source, FluidInventory target,
        double capacityStandardLiters, double naturalFillFraction)
    {
        if (!CanFillFromSource(source, target, capacityStandardLiters, naturalFillFraction)) return false;
        decimal room = FluidUnits.StandardLitersToMol((decimal)(capacityStandardLiters * naturalFillFraction)) - target.GasMoles;
        decimal requested = AtmosphereService.Quantize(Math.Min(room, FluidInventory.MaximumMoles - target.GasMoles));
        if (requested <= 0m) return false;
        IReadOnlyList<FluidBatch> drawn = source.TakeGas(requested);
        if (drawn == null || drawn.Count == 0) return false;
        bool committed = false;
        try
        {
            var trial = new FluidInventory(target.State.Clone(), target.Catalog);
            decimal received = 0m;
            foreach (FluidBatch gas in drawn)
            {
                if (!gas.IsGas || gas.LiquidMoles > 0m || gas.GasMoles <= 0m ||
                    !trial.Catalog.TryGet(gas.FluidId, out _) || !FluidUnits.IsFinite(gas.InternalEnergyJoules)) return false;
                received += gas.GasMoles;
                if (received > requested || !trial.TryAdd(gas, 1d, .001d)) return false;
            }
            if (received <= 0m || received > room) return false;
            target.Restore(trial.State);
            committed = true;
            return true;
        }
        finally
        {
            if (!committed) foreach (FluidBatch gas in drawn) source.ReleaseGas(gas);
        }
    }
    #endregion
}
