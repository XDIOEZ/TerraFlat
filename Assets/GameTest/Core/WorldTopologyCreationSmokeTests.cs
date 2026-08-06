using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace FlatWorld.GameTest.Core
{
    public sealed class WorldTopologyCreationSmokeTests
    {
        [Test]
        [Category("Core.Smoke")]
        public void WrappedCreationRequiresConstructibleChunkAlignedBounds()
        {
            var validPlanet = new PlanetData
            {
                Name = "Finite",
                Radius = 17,
                ChunkSize = new Vector2Int(16, 16),
                TopologyMode = WorldTopologyMode.Wrapped
            };
            var valid = new NewWorldCreationRequest(
                "FiniteSave", "Player", "1", validPlanet, new TimeData());
            Assert.That(valid.TryValidate(out string validError), Is.True, validError);

            validPlanet.ChunkSize = new Vector2Int(0, 16);
            var invalid = new NewWorldCreationRequest(
                "InvalidFiniteSave", "Player", "1", validPlanet, new TimeData());
            Assert.That(invalid.TryValidate(out _), Is.False);
        }

        [Test]
        [Category("Core.Smoke")]
        public void InfiniteCreationRetainsLegacyChunkValidationBehavior()
        {
            var planet = new PlanetData
            {
                Name = "Infinite",
                Radius = 1,
                ChunkSize = Vector2Int.zero,
                TopologyMode = WorldTopologyMode.Infinite
            };
            var request = new NewWorldCreationRequest(
                "InfiniteSave", "Player", "1", planet, new TimeData());
            Assert.That(request.TryValidate(out string error), Is.True, error);
        }

        [Test]
        [Category("Core.Smoke")]
        public void NewWorldUiOwnsWrappedDefaultWithoutConvertingExistingPlanetDefaults()
        {
            Assert.That(new PlanetData().TopologyMode, Is.EqualTo(WorldTopologyMode.Infinite));
            string source = File.ReadAllText("Assets/5_Scripts/5-3_GamePlay/Manager/GameManager.UI.cs");
            Assert.That(source, Does.Contain("pendingNewWorldTopology = WorldTopologyMode.Wrapped"));
            Assert.That(source, Does.Contain("ReadyPlanetData.TopologyMode = topologyMode"));
        }

        [Test]
        [Category("Core.Smoke")]
        public void GoldenPathUsesValidatedConfigurationAndProductionSystemExecutor()
        {
            string configurationSource = File.ReadAllText(
                "Assets/Editor/FlatWorld/Automation/FlatWorldGoldenPathConfiguration.cs");
            string executorSource = File.ReadAllText(
                "Assets/Editor/FlatWorld/Automation/FlatWorldGoldenPathExecutor.cs");
            string commandSource = File.ReadAllText(
                "Assets/Editor/FlatWorld/Automation/FlatWorldGoldenPathCommand.cs");
            string runnerSource = File.ReadAllText(
                ".agents/skills/flatworld-test-automation/scripts/run_unity_tests.py");

            Assert.That(configurationSource, Does.Contain("public void Validate()"));
            Assert.That(configurationSource, Does.Contain("GoldenPathHydrologyConfiguration"));
            Assert.That(configurationSource, Does.Contain("cameraOrthographicSize"));
            Assert.That(executorSource, Does.Contain("CreateWorldRequest"));
            Assert.That(executorSource, Does.Contain("ConfigurePlayer"));
            Assert.That(executorSource, Does.Contain(
                "WorldGenerationRuntimeHooks.BeforeMapGeneration"));
            Assert.That(commandSource, Does.Contain("_executor.Configuration"));
            Assert.That(commandSource, Does.Contain("public FlatWorldGoldenPathConfiguration configuration"));
            Assert.That(runnerSource, Does.Contain("--golden-config"));
            Assert.That(runnerSource, Does.Contain("--golden-set"));
        }
    }
}
