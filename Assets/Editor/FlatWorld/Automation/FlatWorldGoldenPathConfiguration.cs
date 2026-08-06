using System;
using UnityEngine;

namespace FlatWorld.Automation
{
    [Serializable]
    internal sealed class FlatWorldGoldenPathConfiguration
    {
        public const int CurrentSchemaVersion = 1;

        public int schemaVersion = CurrentSchemaVersion;
        public string presetName = "default";
        public GoldenPathWorldConfiguration world = new();
        public GoldenPathPlayerConfiguration player = new();
        public GoldenPathScenarioConfiguration scenarios = new();
        public GoldenPathHydrologyConfiguration hydrology = new();
        public GoldenPathExecutionConfiguration execution = new();

        public static FlatWorldGoldenPathConfiguration CreateDefault() => new();

        public void Validate()
        {
            if (schemaVersion != CurrentSchemaVersion)
                throw new InvalidOperationException(
                    $"Unsupported GoldenPath configuration schema {schemaVersion}; " +
                    $"expected {CurrentSchemaVersion}.");
            world ??= new GoldenPathWorldConfiguration();
            player ??= new GoldenPathPlayerConfiguration();
            scenarios ??= new GoldenPathScenarioConfiguration();
            hydrology ??= new GoldenPathHydrologyConfiguration();
            execution ??= new GoldenPathExecutionConfiguration();

            Require(world.seed != 0, "world.seed must be non-zero.");
            Require(world.radius > 0 && world.radius <= 1_000_000,
                "world.radius must be in [1, 1000000].");
            Require(world.chunkSizeX > 0 && world.chunkSizeY > 0,
                "world chunk dimensions must be positive.");
            Require(PlanetData.IsValidNoiseScale(world.noiseScale),
                "world.noiseScale is outside the production range.");
            Require(TryResolveTopology(out WorldTopologyMode topology),
                "world.topologyMode must be Wrapped or Infinite.");
            Require(!scenarios.worldWrap || topology == WorldTopologyMode.Wrapped,
                "scenarios.worldWrap requires a Wrapped world.");
            Require(TryResolveDifficulty(out _),
                "world.difficulty must be Simple, Hard, or Custom.");

            Require(player.cameraOrthographicSize >= 5f && player.cameraOrthographicSize <= 100f,
                "player.cameraOrthographicSize must be in [5, 100].");
            Require(player.wrapMoveSpeed > 0f && player.maximumMoveSpeed > 0f,
                "player movement speeds must be positive.");
            Require(player.waypointCount >= 2 && player.waypointCount <= 256,
                "player.waypointCount must be in [2, 256].");
            Require(player.waypointStepChunks > 0f && player.waypointStepChunks <= 64f,
                "player.waypointStepChunks must be in (0, 64].");
            Require(player.middleScreenshotWaypointIndex >= 0 &&
                    player.middleScreenshotWaypointIndex < player.waypointCount - 1,
                "player.middleScreenshotWaypointIndex must select a non-final waypoint.");

            if (hydrology.overrideGeneration)
            {
                Require(hydrology.hydrologyRegionSize >= 64, "hydrologyRegionSize must be >= 64.");
                Require(hydrology.runoffCellSize >= 16, "runoffCellSize must be >= 16.");
                Require(hydrology.runoffSampleStride >= 1, "runoffSampleStride must be >= 1.");
                Require(hydrology.maxTraceSteps >= 32, "maxTraceSteps must be >= 32.");
                Require(hydrology.seaLevel is >= 0f and <= 1f, "seaLevel must be in [0, 1].");
                Require(hydrology.infiltrationFloor is >= 0f and <= 1f,
                    "infiltrationFloor must be in [0, 1].");
                Require(hydrology.riverStartFlow > 0f && hydrology.fullWidthFlow > 0f,
                    "river flow thresholds must be positive.");
                Require(hydrology.maxRiverWidth is >= 1 and <= 5,
                    "maxRiverWidth must be in [1, 5].");
            }

            Require(execution.startupTimeoutSeconds > 0d &&
                    execution.worldEntryTimeoutSeconds > 0d &&
                    execution.moveTimeoutSeconds > 0d &&
                    execution.screenshotTimeoutSeconds > 0d,
                "execution timeouts must be positive.");
            Require(execution.minimumVisitedChunks >= 1 && execution.minimumObservedChunks >= 1,
                "execution Chunk minimums must be positive.");
            Require(execution.screenshotSettleFrames >= 0 &&
                    execution.screenshotSettleSeconds >= 0d,
                "screenshot settle values cannot be negative.");
            Require(execution.positionTolerance > 0f,
                "execution.positionTolerance must be positive.");
        }

        public bool TryResolveTopology(out WorldTopologyMode mode) =>
            Enum.TryParse(world.topologyMode, true, out mode) &&
            (mode == WorldTopologyMode.Wrapped || mode == WorldTopologyMode.Infinite);

        public bool TryResolveDifficulty(out GameDifficultyId difficulty) =>
            Enum.TryParse(world.difficulty, true, out difficulty) &&
            Enum.IsDefined(typeof(GameDifficultyId), difficulty);

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException("GoldenPath configuration: " + message);
        }
    }

    [Serializable]
    internal sealed class GoldenPathWorldConfiguration
    {
        public int seed = 424242;
        public int radius = 512;
        public int chunkSizeX = 16;
        public int chunkSizeY = 16;
        public float noiseScale = PlanetData.DefaultNoiseScale;
        public string topologyMode = "Wrapped";
        public string difficulty = "Simple";
        public bool autoGenerateMap = true;
    }

    [Serializable]
    internal sealed class GoldenPathPlayerConfiguration
    {
        public float cameraOrthographicSize = 10f;
        public float wrapMoveSpeed = 12f;
        public float maximumMoveSpeed = 24f;
        public int waypointCount = 12;
        public float waypointStepChunks = 1.5f;
        public int middleScreenshotWaypointIndex = 5;
    }

    [Serializable]
    internal sealed class GoldenPathScenarioConfiguration
    {
        public bool worldWrap = true;
        public bool hydrology = true;
        public bool burningBuff = true;
    }

    [Serializable]
    internal sealed class GoldenPathHydrologyConfiguration
    {
        public bool overrideGeneration;
        public int hydrologyRegionSize = 256;
        public int runoffCellSize = 64;
        public int runoffSampleStride = 8;
        public int maxTraceSteps = 512;
        public float seaLevel = 0.5f;
        public float infiltrationFloor = 0.25f;
        public float riverStartFlow = 0.12f;
        public float fullWidthFlow = 2.5f;
        public int maxRiverWidth = 5;
        public float lakeMinFlow = 0.35f;
        public float windwardRainGain = 0.8f;
        public float leewardRainLoss = 0.6f;
    }

    [Serializable]
    internal sealed class GoldenPathExecutionConfiguration
    {
        public double startupTimeoutSeconds = 90d;
        public double worldEntryTimeoutSeconds = 180d;
        public double moveTimeoutSeconds = 20d;
        public double screenshotTimeoutSeconds = 15d;
        public int minimumVisitedChunks = 10;
        public int minimumObservedChunks = 50;
        public int screenshotSettleFrames = 2;
        public double screenshotSettleSeconds = 0.35d;
        public float positionTolerance = 0.5f;
    }
}
