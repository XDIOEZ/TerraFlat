using System;
using FlatWorld.WorldModel;
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
        private bool _generationHooksRegistered;

        internal FlatWorldGoldenPathExecutor(FlatWorldGoldenPathConfiguration configuration)
        {
            Configuration = configuration ?? FlatWorldGoldenPathConfiguration.CreateDefault();
            Configuration.Validate();
            if (Configuration.hydrology.overrideGeneration)
            {
                WorldGenerationRuntimeHooks.BeforeMapGeneration += ApplyLegacyGenerationOverrides;
                WorldGenerationRuntimeHooks.BeforeWorldModelGeneration +=
                    ApplyWorldModelGenerationOverrides;
                _generationHooksRegistered = true;
            }
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

        #region 临时世界生成覆盖

        /// <summary>兼容旧 MapCore 管线；只修改本次运行时实例。</summary>
        private void ApplyLegacyGenerationOverrides(Map map)
        {
            GoldenPathHydrologyConfiguration hydrology = Configuration.hydrology;
            ChunkGenerator_River river = map.GetGenerator<ChunkGenerator_River>();
            ChunkGenerator_Land land = map.GetGenerator<ChunkGenerator_Land>();
            if (river == null || land == null)
                throw new InvalidOperationException("GoldenPath hydrology override requires Land and River generators.");

            river.hydrologyRegionSize = hydrology.hydrologyRegionSize;
            river.runoffCellSize = hydrology.runoffCellSize;
            river.runoffSampleStride = hydrology.runoffSampleStride;
            river.maxTraceSteps = hydrology.maxTraceSteps;
            river.seaLevel = hydrology.seaLevel;
            river.infiltrationFloor = hydrology.infiltrationFloor;
            river.riverStartFlow = hydrology.riverStartFlow;
            river.fullWidthFlow = hydrology.fullWidthFlow;
            river.maxRiverWidth = hydrology.maxRiverWidth;
            river.lakeMinFlow = hydrology.lakeMinFlow;
            land.WindwardRainGain = hydrology.windwardRainGain;
            land.LeewardRainLoss = hydrology.leewardRainLoss;
            ChunkGenerator_River.ClearHydrologyCache();
        }

        /// <summary>把黄金路径水文配置映射到当前后台 WorldModel 的纯参数快照。</summary>
        private ChunkGenerationProfileSnapshot ApplyWorldModelGenerationOverrides(
            ChunkGenerationProfileSnapshot profile)
        {
            GoldenPathHydrologyConfiguration hydrology = Configuration.hydrology;
            return profile
                .WithNumericParameter("river.enabled", 1d)
                .WithNumericParameter("terrain.seaLevel", hydrology.seaLevel)
                .WithNumericParameter("river.hydrologyRegionSize", hydrology.hydrologyRegionSize)
                .WithNumericParameter("river.runoffCellSize", hydrology.runoffCellSize)
                .WithNumericParameter("river.runoffSampleStride", hydrology.runoffSampleStride)
                .WithNumericParameter("river.maxTraceSteps", hydrology.maxTraceSteps)
                .WithNumericParameter("river.minimumVisibleCourseLength",
                    hydrology.minimumVisibleCourseLength)
                .WithNumericParameter("river.infiltrationFloor", hydrology.infiltrationFloor)
                .WithNumericParameter("river.startFlow", hydrology.riverStartFlow)
                .WithNumericParameter("river.tributaryStartFlow", hydrology.tributaryStartFlow)
                .WithNumericParameter("river.fullWidthFlow", hydrology.fullWidthFlow)
                .WithNumericParameter("river.maxWidth", hydrology.maxRiverWidth)
                .WithNumericParameter("river.floodplainStartFlow", hydrology.floodplainStartFlow)
                .WithNumericParameter("river.lakeMinFlow", hydrology.lakeMinFlow)
                .WithNumericParameter("climate.orographic.windwardGain",
                    hydrology.windwardRainGain)
                .WithNumericParameter("climate.orographic.leewardLoss",
                    hydrology.leewardRainLoss);
        }

        #endregion

        public void Dispose()
        {
            if (_generationHooksRegistered)
            {
                WorldGenerationRuntimeHooks.BeforeMapGeneration -= ApplyLegacyGenerationOverrides;
                WorldGenerationRuntimeHooks.BeforeWorldModelGeneration -=
                    ApplyWorldModelGenerationOverrides;
                _generationHooksRegistered = false;
            }
            RestoreTraversalView();
            _camera = null;
            _chunkLoader = null;
        }
    }
}
