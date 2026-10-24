using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 入海口正式采样

    private static JObject EnvironmentEstuaryCurrent(JObject args)
    {
        double dx = (double?)args["x"] ?? 4d, dy = (double?)args["y"] ?? 0d;
        bool inside = EstuaryCurrentKernel.TrySample(dx, dy, 1d, 0d, 4d, 18d,
            System.Math.Tan(35d * System.Math.PI / 180d), out EstuaryCurrentSample plume);
        using ChunkTerrainData terrain = EnvironmentBatchFixture(1, 1);
        terrain.SetCell(0, 0, new TerrainCell(1, 0, 0, (int)SurfaceBiomeKind.Ocean,
            1, TerrainCellFlags.Walkable));
        terrain.SetLiquid(0, 0, terrain.LiquidTypes.GetIndex(LiquidTypeCatalog.SeaWaterId), 8);
        terrain.SetEnvironmentValue("windX", 0, 0, -1f);
        terrain.SetEnvironmentValue(EstuaryCurrentKernel.DirectionXLayer, 0, 0, (float)plume.X);
        terrain.SetEnvironmentValue(EstuaryCurrentKernel.DirectionYLayer, 0, 0, (float)plume.Y);
        terrain.SetEnvironmentValue(EstuaryCurrentKernel.InfluenceLayer, 0, 0, (float)plume.Influence);
        var sample = new RuntimeTerrainTileSample(default, terrain, Vector2Int.zero,
            Vector2Int.zero, terrain.GetCell(0, 0), terrain.GetTopTileId(0, 0));
        bool active = ChunkMgr.TryResolveTerrainWaterCurrent(sample, out RuntimeWaterCurrentSample current);
        Vector2 velocity = WorldMotionSystem.CalculateWaterCurrentVelocity(current, 1f);
        bool seawater = sample.LiquidId == LiquidTypeCatalog.SeaWaterId && sample.LiquidLevel == 8;
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
            ["inside"] = inside, ["influence"] = plume.Influence,
            ["active"] = active, ["kind"] = current.Kind.ToString(),
            ["x"] = velocity.x, ["y"] = velocity.y,
            ["speed"] = velocity.magnitude, ["seawaterPreserved"] = seawater,
            ["covered"] = covered, ["lake"] = lake, ["dry"] = dry
        };
    }

    #endregion
}
