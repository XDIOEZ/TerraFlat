using System.Collections.Generic;
using System.Threading;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;

public static partial class GameplayTestSystemProbe
{
    #region 地块定位规则采样

    // 夹具只指定地形输入，名字匹配、地块遍历和取消均调用正式入口。
    private static JObject WorldTileSearch(JObject args)
    {
        string scenario = (string)args["scenario"] ?? "found";
        bool cave = (bool?)args["cave"] ?? false;
        bool wrapped = (bool?)args["wrapped"] ?? true;
        var numbers = SmallGenerationParameters();
        numbers["river.enabled"] = 0;
        numbers["lake.large.enabled"] = 0;
        var profile = new ChunkGenerationProfileSnapshot("probe:tile-search", 1, 4, 4, numbers,
            new Dictionary<string, string> { ["terrain.mode"] = cave ? "cave" : "surface" });
        var topology = wrapped ? new ChunkGenerationTopologySnapshot(new Int2(-8, -8), new Int2(16, 16)) : default;
        using var cancellation = new CancellationTokenSource();
        var generator = new DeterministicChunkGenerator(terrainDecorator: (request, terrain, _) =>
        {
            for (int y = 0; y < terrain.Height; y++)
            for (int x = 0; x < terrain.Width; x++)
            {
                int worldX = request.Address.ChunkOrigin.X + x;
                int worldY = request.Address.ChunkOrigin.Y + y;
                bool target = worldX == 7 && worldY == -5 && scenario != "missing";
                bool unsafeCell = worldY == 0 && worldX >= 0 && worldX <= 2;
                terrain.SetCell(x, y, new TerrainCell(target || unsafeCell ? 30 : 1,
                    unsafeCell && worldX == 1 ? 2 : 0, unsafeCell && worldX == 2 ? 2 : 0,
                    1, 1, TerrainCellFlags.Walkable));
                terrain.SetLiquid(x, y, unsafeCell && worldX == 0 ? 1 : 0, unsafeCell && worldX == 0 ? 1 : 0);
            }
            if (scenario == "cancel_during") cancellation.Cancel();
        });
        if (scenario == "cancel_before") cancellation.Cancel();
        var excluded = scenario == "excluded" ? new[] { new Int2(4, -8) } : null;
        var rejected = scenario == "rejected" ? new[] { new Int2(7, -5) } : null;
        var progress = new DeterministicChunkGenerator.BiomeSearchProgress();
        DeterministicChunkGenerator.BiomeSearchResult result = default;
        string exception = WorldException(() => result = generator.FindGroundTile(cave ? "cave" : "surface",
            17413, profile, topology, new Int2(0, 0), 30, cancellation.Token, 1, progress, excluded, rejected));
        return new JObject
        {
            ["found"] = result.Found, ["x"] = result.Cell.X, ["y"] = result.Cell.Y,
            ["sampled"] = result.SampledCount, ["exception"] = exception,
            ["stage"] = progress.Latest.Stage,
            ["clay"] = TileSearchNameRules.Score("粘土地块", "Tile_Clay", "黏土", null),
            ["id"] = TileSearchNameRules.Score("Tile_Clay", "Tile_Clay", "黏土", null),
            ["alias"] = TileSearchNameRules.Score("沙地", "Tile_Sand", "Tile_Sand", new[] { "沙地" }),
            ["partial"] = TileSearchNameRules.Score("黏", "Tile_Clay", "黏土", null),
            ["empty"] = TileSearchNameRules.Score("地块", "Tile_Clay", "黏土", null),
            ["unknown"] = TileSearchNameRules.Score("不存在", "Tile_Clay", "黏土", null)
        };
    }

    #endregion
}
