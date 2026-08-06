using System;
using UnityEngine;

namespace FlatWorld.Automation
{
    /// <summary>
    /// Applies one validated, reproducible GoldenPath configuration through the
    /// game's public lifecycle and component APIs. It never edits Prefab assets.
    /// </summary>
    internal sealed class FlatWorldGoldenPathExecutor : IDisposable
    {
        internal FlatWorldGoldenPathConfiguration Configuration { get; }

        internal FlatWorldGoldenPathExecutor(FlatWorldGoldenPathConfiguration configuration)
        {
            Configuration = configuration ?? FlatWorldGoldenPathConfiguration.CreateDefault();
            Configuration.Validate();
        }

        internal NewWorldCreationRequest CreateWorldRequest(string suffix)
        {
            Configuration.TryResolveTopology(out WorldTopologyMode topology);
            Configuration.TryResolveDifficulty(out GameDifficultyId difficulty);
            GoldenPathWorldConfiguration world = Configuration.world;
            return new NewWorldCreationRequest(
                $"GoldenPathSave_{suffix}",
                $"GoldenPathPlayer_{suffix}",
                world.seed.ToString(),
                new PlanetData
                {
                    Name = $"GoldenPathWorld_{suffix}",
                    Radius = world.radius,
                    NoiseScale = world.noiseScale,
                    ChunkSize = new Vector2Int(world.chunkSizeX, world.chunkSizeY),
                    AutoGenerateMap = world.autoGenerateMap,
                    TopologyMode = topology
                },
                new TimeData(),
                difficulty);
        }

        internal void ConfigurePlayer(Player player, Mod_ChunkLoader chunkLoader)
        {
            if (player == null)
                throw new ArgumentNullException(nameof(player));
            Mod_Cam camera = player.itemMods.GetMod_ByID<Mod_Cam>(ModText.Camera);
            if (camera == null)
                throw new InvalidOperationException("GoldenPath executor cannot find the real player camera module.");
            if (Configuration.player.cameraOrthographicSize > camera.MaxPovValue)
                camera.EnableUnlimitedView();
            camera.SetOrthographicSize(Configuration.player.cameraOrthographicSize);
            chunkLoader?.RefreshChunksForCameraView();
        }

        public void Dispose() { }
    }
}
