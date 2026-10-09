using System;
using FlatWorld.Spaceflight;
using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class MachineWorld
{
    #region 流体环境的上下文适配
    internal static bool TryGetFluidAmbientTemperature(MachineEntity node, out float temperature)
    {
        temperature = TemperatureMgr.DefaultAmbientTemperature;
        if (node == null) return false;
        if (IsShipScope)
        {
            if (!RequireShipEnvironment().TryGetMachineEnvironment(worldKey.Substring(5), node.Position, out ShipEnvironmentSnapshot environment, out _))
                throw new InvalidOperationException("船上机器温度查询缺少所属船体。");
            temperature = (float)environment.TemperatureCelsius; return true;
        }
        if (SceneManager.GetActiveScene().name == worldKey && TemperatureMgr.Instance != null &&
            TemperatureMgr.Instance.TryGetAmbientTemperature(node.Position, out temperature)) return true;
        if (WorldAddress.FromWorldKey(worldKey).IsSurface && owner?.PlanetData_Dict.ContainsKey(worldKey) == true)
        { temperature = (float)SpaceSurfaceQuery.GetLandingSurface(worldKey, node.Position).TemperatureCelsius; return true; }
        return false;
    }

    internal static double GetFluidAmbientPressureKPa(MachineEntity node)
    {
        if (!IsShipScope) return AtmosphereService.PressureKPa(GetCurrentAtmosphere(node));
        if (!RequireShipEnvironment().TryGetMachineEnvironment(worldKey.Substring(5), node.Position, out ShipEnvironmentSnapshot environment, out _))
            throw new InvalidOperationException("船上机器外压查询缺少所属船体。");
        return environment.PressureKPa;
    }

    internal static decimal GetFluidAmbientGasMoles(MachineEntity node)
    {
        if (!IsShipScope) return GetCurrentAtmosphere(node)?.CurrentMoles ?? 0m;
        if (!RequireShipEnvironment().TryGetMachineEnvironment(worldKey.Substring(5), node.Position, out _, out decimal moles))
            throw new InvalidOperationException("船上机器进气源缺少所属船体。");
        return moles;
    }

    internal static bool TryExtractFluidAmbientGasTo(MachineEntity node, decimal standardLiters, ref ulong randomState,
        FluidInventory target, double volumeLiters, double minimumGasSpaceLiters, out FluidBatch batch)
        => IsShipScope ? RequireShipEnvironment().TryExtractMachineGasTo(worldKey.Substring(5), node.Position, standardLiters,
            ref randomState, target, volumeLiters, minimumGasSpaceLiters, out batch)
            : AtmosphereService.TryExtractTo(GetCurrentAtmosphere(node), standardLiters, ref randomState, target, volumeLiters, minimumGasSpaceLiters, out batch);

    internal static bool TryResolveFluidSurfacePoint(Vector2 localPosition, out string world, out Vector2 position)
    {
        if (IsShipScope) return RequireShipEnvironment().TryResolveMachineSurfacePoint(worldKey.Substring(5), localPosition, out world, out position);
        world = worldKey; position = localPosition;
        return WorldAddress.FromWorldKey(world).IsSurface;
    }

    private static SpaceSession RequireShipEnvironment()
        => SpaceSession.Current ?? throw new InvalidOperationException("船上机器缺少环境会话。");
    #endregion

    #region 明确世界地址的地表液体事务
    internal static bool TrySampleFluidEnvironmentLiquid(Vector2 localPosition, out string world, out Vector2 position,
        out LiquidDefinition liquid, out float depth)
    {
        liquid = null; depth = 0f;
        if (TryResolveFluidSurfacePoint(localPosition, out world, out position))
            return SpaceSurfaceQuery.TrySampleLiquid(world, position, out liquid, out depth);
        if (IsShipScope || worldKey != SceneManager.GetActiveScene().name || !WorldLiquidSourceResolver.TryResolve(localPosition, out WorldLiquidSourceTarget source))
            return false;
        world = worldKey; position = localPosition; liquid = source.Liquid; depth = source.Sample.LiquidDepth; return true;
    }

    internal static bool IsFluidEnvironmentSubmerged(MachineEntity node)
    {
        if (IsShipScope && RequireShipEnvironment().TryGetMachineEnvironment(worldKey.Substring(5), node.Position,
            out ShipEnvironmentSnapshot environment, out _) && environment.HasFloorSupport) return false;
        if (TryResolveFluidSurfacePoint(node.Position, out string world, out Vector2 position))
        {
            LandingSurfaceSample surface = SpaceSurfaceQuery.GetLandingSurface(world, position);
            return surface.SupportTileId == 0 && surface.LiquidDepth > 0f;
        }
        return !IsShipScope && worldKey == SceneManager.GetActiveScene().name && WorldLiquidSourceResolver.TryResolve(node.Position, out WorldLiquidSourceTarget source) &&
            TerrainSupportLayer.GetTileId(source.Sample.Terrain, source.Sample.LocalCell.x, source.Sample.LocalCell.y) == 0;
    }

    internal static bool TryPumpFluidEnvironmentLiquid(string world, Vector2 position, float requestedDepth,
        out string liquidId, out float removedDepth)
    {
        liquidId = null; removedDepth = 0f;
        if (WorldAddress.FromWorldKey(world).IsSurface)
            return SpaceSurfaceQuery.TryPumpLiquidInWorld(world, position, requestedDepth, out liquidId, out removedDepth);
        return world == SceneManager.GetActiveScene().name && WorldLiquidSystem.TryPump(position, requestedDepth, out liquidId, out removedDepth);
    }

    internal static float ReleaseFluidEnvironmentLiquid(string world, Vector2 position, string liquidId, float depth)
    {
        if (WorldAddress.FromWorldKey(world).IsSurface) return SpaceSurfaceQuery.ReleaseLiquidInWorld(world, position, liquidId, depth);
        return world == SceneManager.GetActiveScene().name && WorldLiquidSystem.TryPour(position, liquidId, depth, out float accepted) ? accepted : 0f;
    }
    #endregion
}
