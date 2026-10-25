using System;
using FlatWorld.Networking;
using FlatWorld.Spaceflight;
using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class MachineWorld
{
    #region 流体环境的上下文适配
    private static bool TryGetLocalAtmosphereKey(MachineEntity node, out string key)
    {
        key = shipScopeId ?? worldKey;
        return IsShipScope || node != null && SpaceSession.Current != null &&
            SpaceSession.Current.TryGetGroundEnvironment(worldKey, node.Position, out _);
    }

    internal static bool TryGetFluidAmbientTemperature(MachineEntity node, out float temperature)
    {
        temperature = TemperatureMgr.DefaultAmbientTemperature;
        if (node == null) return false;
        if (TryGetLocalAtmosphereKey(node, out string atmosphereKey))
        {
            if (!RequireShipEnvironment().TryGetMachineEnvironment(atmosphereKey, node.Position, out ShipEnvironmentSnapshot environment))
                throw new InvalidOperationException("船上机器温度查询缺少所属船体。");
            temperature = (float)environment.TemperatureCelsius; return true;
        }
        if (SceneManager.GetActiveScene().name == worldKey && TemperatureMgr.Instance != null &&
            TemperatureMgr.Instance.TryGetAmbientTemperature(node.Position, out temperature)) return true;
        if (WorldAddress.FromWorldKey(worldKey).IsSurface && owner?.PlanetData_Dict.ContainsKey(worldKey) == true)
            return SpaceSurfaceQuery.TryGetAmbientTemperature(worldKey, node.Position, out temperature);
        return false;
    }

    internal static double GetFluidAmbientPressureKPa(MachineEntity node)
    {
        if (!TryGetLocalAtmosphereKey(node, out string atmosphereKey)) return AtmosphereService.PressureKPa(GetCurrentAtmosphere(node));
        if (!RequireShipEnvironment().TryGetMachineEnvironment(atmosphereKey, node.Position, out ShipEnvironmentSnapshot environment))
            throw new InvalidOperationException("船上机器外压查询缺少所属船体。");
        return environment.PressureKPa;
    }

    internal static decimal GetFluidAmbientGasMoles(MachineEntity node)
    {
        if (!TryGetLocalAtmosphereKey(node, out string atmosphereKey)) return GetCurrentAtmosphere(node)?.CurrentMoles ?? 0m;
        if (!RequireShipEnvironment().TryGetMachineEnvironment(atmosphereKey, node.Position, out _, out decimal moles))
            throw new InvalidOperationException("船上机器进气源缺少所属船体。");
        return moles;
    }

    internal static bool TryExtractFluidAmbientGasTo(MachineEntity node, decimal standardLiters, ref ulong randomState,
        FluidInventory target, double volumeLiters, double minimumGasSpaceLiters, out FluidBatch batch)
        => TryGetLocalAtmosphereKey(node, out string atmosphereKey) ? RequireShipEnvironment().TryExtractMachineGasTo(atmosphereKey, node.Position, standardLiters,
            ref randomState, target, volumeLiters, minimumGasSpaceLiters, out batch)
            : AtmosphereService.TryExtractTo(GetCurrentAtmosphere(node), standardLiters, ref randomState, target, volumeLiters, minimumGasSpaceLiters, out batch);

    internal static bool TryResolveFluidSurfacePoint(Vector2 localPosition, out string world, out Vector2 position)
    {
        if (IsShipScope) return RequireShipEnvironment().TryResolveMachineSurfacePoint(shipScopeId, localPosition, out world, out position);
        world = worldKey; position = localPosition;
        return WorldAddress.FromWorldKey(world).IsSurface;
    }

    private static SpaceSession RequireShipEnvironment()
        => SpaceSession.Current ?? throw new InvalidOperationException("船上机器缺少环境会话。");
    #endregion

    #region 储罐手动充入环境空气
    public static bool CanFillFluidTankAir(MachineEntity node)
    {
        if (node?.Definition.Fluid?.AllowManualAirFill != true) return false;
        using var nodeScope = UseNodeScope(node);
        if (!Contains(node)) return false;
        RebuildGraphsIfDirty();
        FluidMachineState state = GetFluidState(node);
        return !state.Ruptured && TryGetFluidAirSource(node, out IShipAtmosphereSource source) &&
            PortableGasTankAirFilling.CanFillFromSource(source, GetFluidInventory(node),
                node.Definition.Fluid.GasCapacityStandardLiters, node.Definition.Fluid.NaturalFillFraction);
    }

    internal static bool TryGetFluidAirSource(MachineEntity node, out IShipAtmosphereSource source)
    {
        if (TryGetLocalAtmosphereKey(node, out string atmosphereKey))
            return RequireShipEnvironment().TryGetMachineAirSource(atmosphereKey, node.Position, out source);
        AtmosphereState atmosphere = GetCurrentAtmosphere(node);
        source = atmosphere == null ? null : new PlanetShipAtmosphereSource(atmosphere, atmosphere.TemperatureKelvin - 273.15d);
        return source != null;
    }

    internal static bool FillFluidTankAir(MachineEntity node)
    {
        if (!GameNetwork.HasStateAuthority || node == null) return false;
        using var nodeScope = UseNodeScope(node);
        return CanFillFluidTankAir(node) && TryGetFluidAirSource(node, out IShipAtmosphereSource source) &&
            PortableGasTankAirFilling.TryFillFromSource(source, GetFluidInventory(node),
                node.Definition.Fluid.GasCapacityStandardLiters, node.Definition.Fluid.NaturalFillFraction);
    }

    #endregion

    #region 明确世界地址的地表液体事务
    internal static bool TrySelectFluidEnvironmentPumpSource(Vector2 localPosition, out string world, out Vector2 position,
        out LiquidDefinition liquid, out int level, out WorldLiquidPumpSourceSelection selection)
    {
        liquid = null; level = 0;
        selection = WorldLiquidPumpSourceSelection.Unavailable;
        if (TryResolveFluidSurfacePoint(localPosition, out world, out position))
            return SpaceSurfaceQuery.TrySelectConnectedPumpLiquidInWorld(world, position, out position, out liquid, out level, out selection);
        if (IsShipScope || worldKey != SceneManager.GetActiveScene().name)
            return false;
        if (ChunkMgr.ExistingInstance == null) return false;
        selection = ChunkMgr.ExistingInstance.SelectConnectedLiquidPumpSource(localPosition, out RuntimeTerrainTileSample sample);
        if (selection == WorldLiquidPumpSourceSelection.Searching || selection == WorldLiquidPumpSourceSelection.Unavailable ||
            !WorldLiquidSystem.TryGetDefinition(sample, out liquid)) return false;
        world = worldKey; position = (Vector2)sample.WorldCell + Vector2.one * .5f;
        level = sample.LiquidLevel; return true;
    }

    internal static bool IsFluidEnvironmentSubmerged(MachineEntity node)
    {
        if (TryGetLocalAtmosphereKey(node, out string atmosphereKey) && RequireShipEnvironment().TryGetMachineEnvironment(atmosphereKey, node.Position,
            out ShipEnvironmentSnapshot environment) && environment.HasFloorSupport) return false;
        if (TryResolveFluidSurfacePoint(node.Position, out string world, out Vector2 position))
        {
            LandingSurfaceSample surface = SpaceSurfaceQuery.GetLandingSurface(world, position);
            return surface.SupportTileId == 0 && surface.LiquidDepth > 0f;
        }
        return !IsShipScope && worldKey == SceneManager.GetActiveScene().name && WorldLiquidSourceResolver.TryResolve(node.Position, out WorldLiquidSourceTarget source) &&
            TerrainSupportLayer.GetTileId(source.Sample.Terrain, source.Sample.LocalCell.x, source.Sample.LocalCell.y) == 0;
    }

    internal static bool TryPumpFluidEnvironmentLiquid(string world, Vector2 position, int requestedLevel,
        int minimumRemainingLevel, out string liquidId, out int removedLevel)
    {
        liquidId = null; removedLevel = 0;
        if (WorldAddress.FromWorldKey(world).IsSurface)
            return SpaceSurfaceQuery.TryPumpLiquidInWorld(world, position, requestedLevel, out liquidId, out removedLevel, minimumRemainingLevel);
        return world == SceneManager.GetActiveScene().name && WorldLiquidSystem.TryPumpPreservingMinimumLevel(
            position, requestedLevel, minimumRemainingLevel, out liquidId, out removedLevel);
    }

    internal static int ReleaseFluidEnvironmentLiquid(string world, Vector2 position, string liquidId, int level)
    {
        if (WorldAddress.FromWorldKey(world).IsSurface) return SpaceSurfaceQuery.ReleaseLiquidInWorld(world, position, liquidId, level);
        return world == SceneManager.GetActiveScene().name && WorldLiquidSystem.TryPour(position, liquidId, level, out int accepted) ? accepted : 0;
    }
    #endregion
}
