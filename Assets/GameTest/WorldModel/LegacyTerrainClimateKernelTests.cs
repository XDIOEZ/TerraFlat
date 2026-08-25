using System.Collections.Generic;
using FlatWorld.WorldModel;
using NUnit.Framework;
using UnityEngine;

namespace FlatWorld.GameTest.WorldModel
{
    /// <summary>验证无头气候核复用旧 Land 噪声，并在其上统一叠加海拔降温。</summary>
    [Category("Map.Climate")]
    public sealed class LegacyTerrainClimateKernelTests
    {
        [Test]
        public void PureKernelMatchesLegacyLandAtFixedWorldCells()
        {
            ChunkGenerator_Land legacyLand = CreateLegacyLand();
            ChunkGenerationProfileSnapshot profile = CreateProfile();
            var address = new FlatWorld.WorldModel.WorldAddress("surface", new Int2(0, 0));
            using var world = new WorldRuntime("legacy-climate-equivalence", 1);
            ChunkGenerationRequest request = world.BeginChunkGeneration(address, 424242, profile);
            Vector2Int[] positions =
            {
                new Vector2Int(0, 0),
                new Vector2Int(19, -37),
                new Vector2Int(-143, 281),
                new Vector2Int(1024, -2048)
            };

            foreach (Vector2Int position in positions)
            {
                ClimateSample expected = legacyLand.SampleClimateAtWorld(
                    position, request.WorldSeed, null);
                LegacyClimateSample actual = LegacyTerrainClimateKernel.SampleClimate(
                    request, profile.Settings, position.x, position.y);
                double expectedTemperature = profile.Settings.ApplyAltitudeTemperatureCooling(
                    expected.Environment.Height, expected.Environment.Temperature);
                double expectedTemperatureCelsius = profile.Settings.TemperatureCelsiusMin +
                    (profile.Settings.TemperatureCelsiusMax -
                     profile.Settings.TemperatureCelsiusMin) * expectedTemperature;

                Assert.That(actual.Height,
                    Is.EqualTo(expected.Environment.Height).Within(0.00001d), position.ToString());
                Assert.That(actual.Temperature,
                    Is.EqualTo(expectedTemperature).Within(0.00001d),
                    position.ToString());
                Assert.That(actual.TemperatureCelsius,
                    Is.EqualTo(expectedTemperatureCelsius).Within(0.0001d),
                    position.ToString());
                Assert.That(actual.BasePrecipitation,
                    Is.EqualTo(expected.BasePrecipitation).Within(0.00001d), position.ToString());
                Assert.That(actual.Precipitation,
                    Is.EqualTo(expected.Environment.Precipitation).Within(0.00001d),
                    position.ToString());
                Assert.That(actual.WindX,
                    Is.EqualTo(expected.Wind.Direction.x).Within(0.00001d), position.ToString());
                Assert.That(actual.WindY,
                    Is.EqualTo(expected.Wind.Direction.y).Within(0.00001d), position.ToString());
            }
        }

        [Test]
        public void WrappedClimateRepeatsAcrossBothWorldAxes()
        {
            ChunkGenerationProfileSnapshot profile = CreateProfile();
            var topology = new ChunkGenerationTopologySnapshot(
                new Int2(-128, -64), new Int2(256, 128));
            var address = new FlatWorld.WorldModel.WorldAddress("surface", new Int2(0, 0));
            using var world = new WorldRuntime("legacy-climate-wrapped", 1);
            ChunkGenerationRequest request = world.BeginChunkGeneration(
                address, -780190301, profile, topology);

            LegacyClimateSample baseline = LegacyTerrainClimateKernel.SampleClimate(
                request, profile.Settings, 37, -21);
            LegacyClimateSample horizontal = LegacyTerrainClimateKernel.SampleClimate(
                request, profile.Settings, 37 + topology.Span.X, -21);
            LegacyClimateSample vertical = LegacyTerrainClimateKernel.SampleClimate(
                request, profile.Settings, 37, -21 + topology.Span.Y);

            AssertSamplesEqual(baseline, horizontal);
            AssertSamplesEqual(baseline, vertical);
        }

        [Test]
        public void GeneratedSurfaceWritesWindAndOrographicInputs()
        {
            ChunkGenerationProfileSnapshot profile = CreateProfile();
            var address = new FlatWorld.WorldModel.WorldAddress("surface", new Int2(-16, 32));
            using var world = new WorldRuntime("legacy-climate-layers", 1);
            ChunkGenerationRequest request = world.BeginChunkGeneration(address, 123456, profile);
            using ChunkGenerationResult result = new DeterministicChunkGenerator().Generate(
                request, default);
            Assert.That(world.TryCommit(result, out string rejection), Is.True, rejection);
            Assert.That(world.TryGetChunk(address, out ChunkRuntime chunk), Is.True);

            bool adjustedPrecipitationObserved = false;
            for (int y = 0; y < chunk.Terrain.Height; y++)
            for (int x = 0; x < chunk.Terrain.Width; x++)
            {
                Assert.That(chunk.Terrain.TryGetEnvironmentValue(
                    "basePrecipitation", x, y, out float basePrecipitation), Is.True);
                Assert.That(chunk.Terrain.TryGetEnvironmentValue(
                    "precipitation", x, y, out float precipitation), Is.True);
                Assert.That(chunk.Terrain.TryGetEnvironmentValue(
                    "temperature", x, y, out float temperature), Is.True);
                Assert.That(chunk.Terrain.TryGetEnvironmentValue(
                    "temperature.celsius", x, y, out float temperatureCelsius), Is.True);
                Assert.That(temperatureCelsius, Is.EqualTo(temperature * 50f).Within(0.0001f));
                Assert.That(chunk.Terrain.TryGetEnvironmentValue("windX", x, y, out float windX),
                    Is.True);
                Assert.That(chunk.Terrain.TryGetEnvironmentValue("windY", x, y, out float windY),
                    Is.True);
                float windLength = Mathf.Sqrt(windX * windX + windY * windY);
                Assert.That(windLength, Is.EqualTo(1f).Within(0.0001f));
                adjustedPrecipitationObserved |=
                    Mathf.Abs(basePrecipitation - precipitation) > 0.000001f;
            }

            Assert.That(adjustedPrecipitationObserved, Is.True,
                "固定区块必须至少出现一个受迎风增雨或背风雨影影响的格子。");
        }

        [Test]
        public void LegacyBiomeResolverKeepsOrderedLandRules()
        {
            ChunkGenerationSettingsSnapshot settings = CreateProfile()
                .WithNumericParameter("terrain.seaLevel", 0.5d)
                .WithNumericParameter("terrain.beachLevel", 0.51d)
                .Settings;

            Assert.That(SurfaceBiomeClassifier.Resolve(
                    settings, 0.8d, 0.5d, 0.1d, 0.5d, false),
                Is.EqualTo(SurfaceBiomeKind.Stone));
            Assert.That(SurfaceBiomeClassifier.Resolve(
                    settings, 0.8d, 0.1d, 0.7d, 0.7d, false),
                Is.EqualTo(SurfaceBiomeKind.Snow));
            Assert.That(SurfaceBiomeClassifier.Resolve(
                    settings, 0.6d, 0.1d, 0.7d, 0.7d, false),
                Is.EqualTo(SurfaceBiomeKind.Snow));
            Assert.That(SurfaceBiomeClassifier.Resolve(
                    settings, 0.73d, 0.5d, 0.1d, 0.5d, false),
                Is.EqualTo(SurfaceBiomeKind.Stone));
            Assert.That(SurfaceBiomeClassifier.Resolve(
                    settings, 0.6d, 0.5d, 0.2d, 0.5d, false),
                Is.EqualTo(SurfaceBiomeKind.Desert));
            Assert.That(SurfaceBiomeClassifier.Resolve(
                    settings, 0.505d, 0.5d, 0.5d, 0.5d, false),
                Is.EqualTo(SurfaceBiomeKind.Beach));
            Assert.That(SurfaceBiomeClassifier.Resolve(
                    settings, 0.6d, 0.5d, 0.5d, 0.5d, false),
                Is.EqualTo(SurfaceBiomeKind.Grassland));
            Assert.That(SurfaceBiomeClassifier.Resolve(
                    settings, 0.6d, 0.9d, 0.5d, 0.5d, false),
                Is.EqualTo(SurfaceBiomeKind.Forest));
            Assert.That(SurfaceBiomeClassifier.Resolve(
                    settings, 0.4d, 0.5d, 0.5d, 0.5d, false),
                Is.EqualTo(SurfaceBiomeKind.Ocean));
        }

        #region 测试配置

        private static ChunkGenerator_Land CreateLegacyLand()
        {
            return new ChunkGenerator_Land
            {
                NoiseConfigs = new List<TerrainNoiseConfig>
                {
                    new TerrainNoiseConfig
                    {
                        noiseType = NoiseType.Height,
                        coordScale = 2f,
                        frequency = 0.05f,
                        octaves = 5,
                        lacunarity = 2f,
                        persistence = 0.45f,
                        coordOffset = new Vector2(9000f, 0f)
                    },
                    new TerrainNoiseConfig
                    {
                        noiseType = NoiseType.Precipitation,
                        coordScale = 10f,
                        frequency = 0.02f,
                        octaves = 4,
                        lacunarity = 2f,
                        persistence = 0.55f,
                        coordOffset = Vector2.zero
                    },
                    new TerrainNoiseConfig
                    {
                        noiseType = NoiseType.Temperature,
                        coordScale = 10f,
                        frequency = 0.015f,
                        octaves = 4,
                        lacunarity = 2f,
                        persistence = 0.55f,
                        coordOffset = Vector2.zero
                    }
                },
                enableHeightSecondaryBoost = true,
                heightSecondaryBoostStrength = 1f,
                WindField = WindFieldConfig.Default,
                OrographicSampleDistance = 64,
                OrographicSampleCount = 4,
                WindwardRainGain = 0.8f,
                LeewardRainLoss = 0.6f
            };
        }

        private static ChunkGenerationProfileSnapshot CreateProfile()
        {
            return new ChunkGenerationProfileSnapshot(
                "legacy-climate",
                DeterministicChunkGenerator.CurrentGenerationSignature,
                16,
                16,
                new Dictionary<string, double>
                {
                    ["world.coordinateScale"] = PlanetData.DefaultNoiseScale,
                    ["terrain.groundTileId"] = 1,
                    ["terrain.waterTileId"] = 2,
                    ["terrain.saltWaterTileId"] = 6,
                    ["terrain.sandTileId"] = 3,
                    ["terrain.seaLevel"] = 0.3d,
                    ["river.enabled"] = 0d,
                    ["structure.enabled"] = 0d,
                    ["grass.density"] = 0d
                },
                new Dictionary<string, string>
                {
                    ["climate.algorithm"] = "legacyLand"
                });
        }

        private static void AssertSamplesEqual(
            LegacyClimateSample expected, LegacyClimateSample actual)
        {
            Assert.That(actual.Height, Is.EqualTo(expected.Height).Within(0.000001d));
            Assert.That(actual.Temperature, Is.EqualTo(expected.Temperature).Within(0.000001d));
            Assert.That(actual.TemperatureCelsius,
                Is.EqualTo(expected.TemperatureCelsius).Within(0.000001d));
            Assert.That(actual.BasePrecipitation,
                Is.EqualTo(expected.BasePrecipitation).Within(0.000001d));
            Assert.That(actual.Precipitation,
                Is.EqualTo(expected.Precipitation).Within(0.000001d));
            Assert.That(actual.WindX, Is.EqualTo(expected.WindX).Within(0.000001d));
            Assert.That(actual.WindY, Is.EqualTo(expected.WindY).Within(0.000001d));
        }

        #endregion
    }
}
