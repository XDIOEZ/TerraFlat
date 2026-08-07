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
        private Mod_Cam _camera;
        private Mod_ChunkLoader _chunkLoader;

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
            _camera = player.itemMods.GetMod_ByID<Mod_Cam>(ModText.Camera);
            _chunkLoader = chunkLoader ?? throw new ArgumentNullException(nameof(chunkLoader));
            if (_camera == null)
                throw new InvalidOperationException("GoldenPath executor cannot find the real player camera module.");
            ApplyViewSize(Configuration.player.cameraOrthographicSize);
        }

        internal void ConfigureScreenshotView()
        {
            EnsureCameraConfigured();
            ApplyViewSize(Configuration.player.screenshotOrthographicSize);
        }

        internal void RestoreTraversalView()
        {
            if (_camera == null)
                return;
            ApplyViewSize(Configuration.player.cameraOrthographicSize);
        }

        private void ApplyViewSize(float orthographicSize)
        {
            if (orthographicSize > _camera.MaxPovValue)
                _camera.EnableUnlimitedView();
            _camera.SetOrthographicSize(orthographicSize);
            _chunkLoader.RefreshChunksForCameraView();
        }

        private void EnsureCameraConfigured()
        {
            if (_camera == null || _chunkLoader == null)
                throw new InvalidOperationException(
                    "GoldenPath screenshot view was requested before the player camera was configured.");
        }

        public void Dispose()
        {
            RestoreTraversalView();
            _camera = null;
            _chunkLoader = null;
        }
    }
}
