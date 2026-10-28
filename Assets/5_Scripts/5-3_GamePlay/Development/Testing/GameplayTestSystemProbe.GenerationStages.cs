using System;
using System.Collections.Generic;
using System.Threading;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;

public static partial class GameplayTestSystemProbe
{
    #region 生成阶段与水文缓存生命周期

    // 使用真实生成、提交和扩展回调，检查阶段顺序与独立取消不会污染共享河网。
    private static JObject WorldGenerationStageOwnership(JObject args)
    {
        var numbers = SmallGenerationParameters();
        numbers["terrain.seaLevel"] = 0d;
        numbers["terrain.beachLevel"] = .01d;
        numbers["lake.large.enabled"] = 0d;
        numbers["structure.enabled"] = 1d;
        numbers["structure.regionSize"] = 8d;
        numbers["structure.spawnChance"] = 1d;
        numbers["structure.radius"] = 3d;
        numbers["structure.groundTileId"] = 91d;
        var profile = new ChunkGenerationProfileSnapshot("probe:generation-stages", 1, 8, 8, numbers,
            new Dictionary<string, string> { ["climate.algorithm"] = "simple" });
        var address = new FlatWorld.WorldModel.WorldAddress("probe-surface", new Int2(16, 16));
        using var firstWorld = new WorldRuntime("probe:stage-world", 1);
        ChunkGenerationRequest request = firstWorld.BeginChunkGeneration(address, 17413, profile);
        int decoratorCalls = 0, surfaceStructures = 0, caveStructures = 0;
        var generator = new DeterministicChunkGenerator(terrainDecorator: (input, terrain, token) =>
        {
            decoratorCalls++;
            int structures = 0;
            for (int y = 0; y < terrain.Height; y++)
            for (int x = 0; x < terrain.Width; x++)
                if (terrain.GetCell(x, y).GroundTileId == 91) structures++;
            if (input.Profile.Settings.Mode == ChunkGenerationMode.Surface) surfaceStructures = structures;
            else caveStructures = structures;
            TerrainCell current = terrain.GetCell(0, 0);
            terrain.SetCell(0, 0, new TerrainCell(777, current.BackTileId, current.BlockingTileId,
                current.BiomeId, current.NavigationCost, current.Flags));
        });
        try
        {
            bool pendingCold = generator.GetPendingGenerationGroupKey(request) != null;
            string Capture(WorldRuntime world, ChunkGenerationRequest input)
            {
                using ChunkGenerationResult generated = generator.Generate(input, CancellationToken.None);
                if (!world.TryCommit(generated, out string error)) throw new InvalidOperationException(error);
                if (!world.TryGetChunk(input.Address, out ChunkRuntime chunk))
                    throw new InvalidOperationException("生成阶段采样缺少已提交区块。");
                return chunk.Terrain.ComputeStableHash().ToString();
            }
            string firstHash = Capture(firstWorld, request);
            firstWorld.TryGetChunk(address, out ChunkRuntime firstChunk);
            bool pendingWarm = generator.GetPendingGenerationGroupKey(request) != null;
            int beforeCancelled = decoratorCalls;
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            bool cancelledRejected = false;
            try { using var ignored = generator.Generate(request, cancelled.Token); }
            catch (OperationCanceledException) { cancelledRejected = true; }
            int cancelledDecoratorCalls = decoratorCalls - beforeCancelled;
            bool warmAfterCancel = generator.GetPendingGenerationGroupKey(request) == null;
            generator.BeginNewWorldHydrologyEpoch(1);
            bool pendingAfterReset = generator.GetPendingGenerationGroupKey(request) != null;
            bool retiredRejected = false;
            try { using var ignored = generator.Generate(request, CancellationToken.None); }
            catch (OperationCanceledException) { retiredRejected = true; }
            using var nextWorld = new WorldRuntime("probe:stage-world", 2);
            string nextHash = Capture(nextWorld, nextWorld.BeginChunkGeneration(address, 17413, profile));
            var caveProfile = new ChunkGenerationProfileSnapshot("probe:generation-stages", 1, 8, 8, numbers,
                new Dictionary<string, string> { ["terrain.mode"] = "cave", ["climate.algorithm"] = "simple" });
            using var caveWorld = new WorldRuntime("probe:cave-stage", 2);
            string caveHash = Capture(caveWorld, caveWorld.BeginChunkGeneration(address, 17413, caveProfile));
            caveWorld.TryGetChunk(address, out ChunkRuntime caveChunk);
            return new JObject { ["pendingCold"] = pendingCold, ["pendingWarm"] = pendingWarm,
                ["warmAfterCancel"] = warmAfterCancel, ["pendingAfterReset"] = pendingAfterReset,
                ["cancelledRejected"] = cancelledRejected, ["retiredRejected"] = retiredRejected,
                ["cancelledDecoratorCalls"] = cancelledDecoratorCalls, ["firstHash"] = firstHash,
                ["nextHash"] = nextHash, ["caveHash"] = caveHash, ["decoratorCalls"] = decoratorCalls,
                ["surfaceStructures"] = surfaceStructures, ["caveStructures"] = caveStructures,
                ["surfaceMarker"] = firstChunk.Terrain.GetCell(0, 0).GroundTileId,
                ["caveMarker"] = caveChunk.Terrain.GetCell(0, 0).GroundTileId };
        }
        finally { generator.BeginNewWorldHydrologyEpoch(2); }
    }

    #endregion
}
