using System;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 海岸分带洋流正式规则采样

    // 夹具只提供岸线与位置，流向、强度及角色和掉落物速度均调用正式规则。
    private static JObject EnvironmentOceanShoreCurrent(JObject args)
    {
        double distance = Number(args, "distance", 8d);
        double normalX = Number(args, "normalX", 1d), normalY = Number(args, "normalY", 0d);
        double transition = Number(args, "transition", 3d), reach = Number(args, "reach", 64d);
        OceanShoreCurrentKernel.ResolveCurrentVector(normalX, normalY, distance, 16d, transition, reach,
            Number(args, "windX", 0d), Number(args, "windY", 1d), out double x, out double y);
        x = Number(args, "baseX", x);
        y = Number(args, "baseY", y);
        using ChunkTerrainData terrain = EnvironmentBatchFixture(1, 1);
        terrain.SetCell(0, 0, new TerrainCell(1, 0, 0, (int)SurfaceBiomeKind.Ocean,
            1, TerrainCellFlags.Walkable));
        terrain.SetLiquid(0, 0, terrain.LiquidTypes.GetIndex(LiquidTypeCatalog.SeaWaterId), 80);
        terrain.SetEnvironmentValue(OceanShoreCurrentKernel.DirectionXLayer, 0, 0, (float)x);
        terrain.SetEnvironmentValue(OceanShoreCurrentKernel.DirectionYLayer, 0, 0, (float)y);
        terrain.SetEnvironmentValue("windX", 0, 0, 1f);
        terrain.SetEnvironmentValue("windY", 0, 0, 1f);
        terrain.SetEnvironmentValue(EstuaryCurrentKernel.DirectionXLayer, 0, 0, 1f);
        terrain.SetEnvironmentValue(EstuaryCurrentKernel.DirectionYLayer, 0, 0, 0f);
        terrain.SetEnvironmentValue(EstuaryCurrentKernel.InfluenceLayer, 0, 0,
            (float)Number(args, "plumeInfluence", 0d));
        var sample = new RuntimeTerrainTileSample(default, terrain, Vector2Int.zero,
            Vector2Int.zero, terrain.GetCell(0, 0), terrain.GetTopTileId(0, 0));
        bool active = ChunkMgr.TryResolveTerrainWaterCurrent(sample, out RuntimeWaterCurrentSample current);
        Vector2 visualCurrent = WaterEnvironmentRules.ResolveOceanCurrentVector(terrain, 0, 0);
        Vector2 actorVelocity = WorldMotionSystem.CalculateWaterCurrentVelocity(current, 2f);
        float drift = WorldItemWaterRules.ResolveDriftSpeed(current.Kind, current.Flow);
        terrain.SetEnvironmentValue(EstuaryCurrentKernel.DirectionXLayer, 0, 0, 1f);
        terrain.SetEnvironmentValue(EstuaryCurrentKernel.DirectionYLayer, 0, 0, 0f);
        terrain.SetEnvironmentValue(EstuaryCurrentKernel.InfluenceLayer, 0, 0, 1f);
        Vector2 riverMouthCurrent = WaterEnvironmentRules.ResolveOceanCurrentVector(terrain, 0, 0);
        terrain.SetEnvironmentValue(EstuaryCurrentKernel.InfluenceLayer, 0, 0, 0f);
        terrain.SetEnvironmentValue(TerrainSupportLayer.TileIdLayer, 0, 0, 90f);
        bool covered = ChunkMgr.TryResolveTerrainWaterCurrent(sample, out _);
        terrain.SetEnvironmentValue(TerrainSupportLayer.TileIdLayer, 0, 0, 0f);
        terrain.SetEnvironmentValue("riverKind", 0, 0, 2f);
        bool lake = ChunkMgr.TryResolveTerrainWaterCurrent(sample, out _);
        terrain.SetEnvironmentValue("riverKind", 0, 0, 0f);
        terrain.SetLiquid(0, 0, 0, 0);
        bool dry = ChunkMgr.TryResolveTerrainWaterCurrent(sample, out _);
        return new JObject
        {
            ["active"] = active, ["x"] = visualCurrent.x, ["y"] = visualCurrent.y,
            ["strength"] = visualCurrent.magnitude, ["flow"] = current.Flow,
            ["actorX"] = actorVelocity.x, ["actorY"] = actorVelocity.y,
            ["drift"] = drift, ["oceanDriftSpeed"] = WorldItemWaterRules.OceanDriftSpeed,
            ["signedStrength"] = OceanShoreCurrentKernel.ResolveSignedCurrentStrength(distance, 16d, transition),
            ["mouthX"] = riverMouthCurrent.x, ["mouthY"] = riverMouthCurrent.y,
            ["covered"] = covered, ["lake"] = lake, ["dry"] = dry
        };
    }

    private static JObject EnvironmentOceanShoreGeometry(JObject args)
    {
        string scenario = Text(args, "scenario", "straight");
        double Height(int x, int y)
        {
            if (scenario == "ocean") return .25d;
            if (scenario == "land") return .75d;
            if (scenario == "strait20") return x <= 0 || x >= 20 ? .75d : .25d;
            if (scenario == "strait40") return x <= 0 || x >= 40 ? .75d : .25d;
            if (scenario == "symmetric_islands")
                return (x - 8) * (x - 8) + y * y <= 4 || (x + 8) * (x + 8) + y * y <= 4 ? .75d : .25d;
            if (scenario == "wrapped") x = (x % 128 + 128) % 128;
            if (scenario == "island") return x * x + y * y <= 4 ? .75d : .25d;
            if (scenario == "diagonal") return x + y <= 0 ? .75d : .25d;
            return x <= 0 ? .75d : .25d;
        }
        int originX = scenario == "symmetric_islands" ? -8 : 0;
        int[] positions = scenario == "strait20" ? new[] { 4, 8, 9, 10, 11, 12, 15, 18, 19 } :
            scenario == "strait40" ? new[] { 4, 8, 16, 19, 20, 21, 24, 32, 36 } :
            scenario == "symmetric_islands" ? new[] { -7, -4, -1, 0, 1, 4, 7, 16, 24 } :
            new[] { 4, 8, 15, 16, 17, 24, 48, 63, 64 };
        using var whole = new OceanShoreDistanceBathymetryKernel(originX, 0, 65, 1, .5d, 16d, 4d, Height,
            currentSearchDistance: 64d, currentTransitionWidth: 3d);
        var samples = new JArray();
        foreach (int x in positions)
        {
            int localX = x - originX;
            whole.GetOutwardShoreDirection(localX, 0, out double normalX, out double normalY);
            OceanShoreCurrentSample current = whole.GetOceanCurrent(localX, 0, 0d, 1d);
            using var single = new OceanShoreDistanceBathymetryKernel(x, 0, 1, 1, .5d, 16d, 4d, Height,
                currentSearchDistance: 64d, currentTransitionWidth: 3d);
            OceanShoreCurrentSample individual = single.GetOceanCurrent(0, 0, 0d, 1d);
            samples.Add(new JObject
            {
                ["cellX"] = x, ["normalX"] = normalX, ["normalY"] = normalY,
                ["distance"] = whole.GetCurrentShoreDistance(localX, 0),
                ["bedDistance"] = whole.GetShoreDistance(localX, 0),
                ["x"] = current.X, ["y"] = current.Y,
                ["singleX"] = individual.X, ["singleY"] = individual.Y
            });
        }
        return new JObject { ["samples"] = samples };
    }

    #endregion
}
