using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 四邻补水规则采样
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterLiquidReplenishmentRules()
    {
        Register("surface_fluids", "replenishment", WorldLiquidReplenishmentRules);
        Register("surface_fluids", "replenishment_limits", WorldLiquidReplenishmentLimits);
    }

    private static TerrainCell ReplenishmentGround(int blocking = 0, TerrainCellFlags flags = TerrainCellFlags.Walkable) =>
        new(17, 0, blocking, 2, 5, flags);

    private static RuntimeTerrainTileSample ReplenishmentSample(ChunkTerrainData terrain, int x, int y) =>
        new(default, terrain, new Vector2Int(x, y), new Vector2Int(x, y), terrain.GetCell(x, y), 0);

    private static JObject WorldLiquidReplenishmentRules(JObject args)
    {
        int level = (int)Number(args, "level", 10);
        using var buffer = new ChunkTerrainBuffer(3, 3);
        int water = buffer.LiquidTypes.GetIndex(LiquidTypeCatalog.DirtyWaterId);
        Vector2Int[] neighbours = { Vector2Int.left, Vector2Int.right, Vector2Int.down, Vector2Int.up };
        for (int y = 0; y < 3; y++)
        for (int x = 0; x < 3; x++)
        {
            buffer.SetCell(x, y, ReplenishmentGround());
            buffer.SetLiquid(x, y, water, 1);
        }
        buffer.SetLiquid(1, 1, water, level);
        using ChunkTerrainData terrain = buffer.Seal();
        foreach (Vector2Int direction in neighbours)
            terrain.SetLiquid(1 + direction.x, 1 + direction.y, 0, 0);
        var source = ReplenishmentSample(terrain, 1, 1);
        var supplied = new JArray();
        var received = new JArray();
        int total = 0;
        bool groundUnchanged = true;
        // 夹具只写回正式规则返回的水量，预期值和守恒断言由 Lua 持有。
        foreach (Vector2Int direction in neighbours)
        {
            int x = 1 + direction.x, y = 1 + direction.y;
            var target = ReplenishmentSample(terrain, x, y);
            int amount = WorldLiquidReplenishment.GetTransferLevel(source, target);
            if (amount > 0)
            {
                terrain.SetLiquid(1, 1, water, terrain.GetLiquidLevel(1, 1) - amount);
                terrain.SetLiquid(x, y, water, amount);
            }
            supplied.Add(amount);
            received.Add(terrain.GetLiquidLevel(x, y));
            total += terrain.GetLiquidLevel(x, y);
            groundUnchanged &= terrain.GetCell(x, y).GroundTileId == 17;
        }
        int remaining = terrain.GetLiquidLevel(1, 1);
        return new JObject
        {
            ["remaining"] = remaining, ["supplied"] = supplied, ["neighbours"] = received,
            ["total"] = total + remaining,
            ["groundUnchanged"] = groundUnchanged && terrain.GetCell(1, 1).GroundTileId == 17
        };
    }

    private static JObject WorldLiquidReplenishmentLimits(JObject args)
    {
        using var buffer = new ChunkTerrainBuffer(8, 1);
        int water = buffer.LiquidTypes.GetIndex(LiquidTypeCatalog.DirtyWaterId);
        int sea = buffer.LiquidTypes.GetIndex(LiquidTypeCatalog.SeaWaterId);
        for (int x = 0; x < 8; x++)
        {
            buffer.SetCell(x, 0, ReplenishmentGround());
            if (x != 2 && x != 7) buffer.SetLiquid(x, 0, x == 4 ? sea : water, x == 0 ? 10 : 1);
        }
        buffer.SetCell(5, 0, ReplenishmentGround(99));
        buffer.SetCell(6, 0, ReplenishmentGround(flags: TerrainCellFlags.Blocking));
        using ChunkTerrainData terrain = buffer.Seal();
        foreach (int x in new[] { 1, 4, 5, 6 }) terrain.SetLiquid(x, 0, 0, 0);
        terrain.SetLiquid(7, 0, water, 5);
        terrain.SetLiquid(7, 0, 0, 0);
        var source = ReplenishmentSample(terrain, 0, 0);
        int Read(int x) => WorldLiquidReplenishment.GetTransferLevel(source, ReplenishmentSample(terrain, x, 0));
        var result = new JObject
        {
            ["emptyBasin"] = Read(1), ["originalLevel"] = terrain.GetLiquidBaseline(1, 0).LiquidLevel,
            ["dryLand"] = Read(2), ["wetNeighbour"] = Read(3), ["otherLiquid"] = Read(4),
            ["wall"] = Read(5), ["blockedFlag"] = Read(6), ["pouredLand"] = Read(7),
            ["sameCell"] = Read(0)
        };
        terrain.SetCell(0, 0, ReplenishmentGround(99));
        result["blockedSource"] = Read(1);
        terrain.SetCell(0, 0, ReplenishmentGround());
        terrain.SetLiquid(0, 0, 0, 0);
        result["emptySource"] = Read(1);

        using var left = new ChunkTerrainBuffer(1, 1,
            new LiquidTypeCatalog(new[] { "a:extra", LiquidTypeCatalog.DirtyWaterId }));
        using var right = new ChunkTerrainBuffer(1, 1,
            new LiquidTypeCatalog(new[] { LiquidTypeCatalog.DirtyWaterId, "z:extra" }));
        left.SetCell(0, 0, ReplenishmentGround());
        right.SetCell(0, 0, ReplenishmentGround());
        left.SetLiquid(0, 0, left.LiquidTypes.GetIndex(LiquidTypeCatalog.DirtyWaterId), 10);
        right.SetLiquid(0, 0, right.LiquidTypes.GetIndex(LiquidTypeCatalog.DirtyWaterId), 1);
        using ChunkTerrainData leftTerrain = left.Seal();
        using ChunkTerrainData rightTerrain = right.Seal();
        rightTerrain.SetLiquid(0, 0, 0, 0);
        result["crossChunk"] = WorldLiquidReplenishment.GetTransferLevel(
            ReplenishmentSample(leftTerrain, 0, 0), ReplenishmentSample(rightTerrain, 0, 0));
        return result;
    }
    #endregion
}
