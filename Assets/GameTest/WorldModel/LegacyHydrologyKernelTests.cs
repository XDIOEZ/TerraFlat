using System;
using System.Collections.Generic;
using System.Threading;
using FlatWorld.WorldModel;
using NUnit.Framework;

namespace FlatWorld.GameTest.WorldModel
{
    /// <summary>验证旧版水文迁移后的盆地、出口、接缝、确定性和取消契约。</summary>
    public sealed class LegacyHydrologyKernelTests
    {
        [Test]
        [Category("Map.Hydrology")]
        public void BasinCreatesLakeAndSpillOutletContinuesDownstream()
        {
            ChunkGenerationProfileSnapshot profile = CreateLegacyProfile(64, 64);
            ChunkGenerationRequest request = CreateRequest(profile, new Int2(0, 0), 314159);
            var kernel = new LegacyHydrologyKernel();
            GeneratedHydrologyMap map = kernel.Build(
                request,
                profile.Settings,
                SpillBasinHeight,
                _ => 1d,
                CancellationToken.None);

            Assert.That(map.TryGet(32, 32, out GeneratedHydrologyCell lake), Is.True);
            Assert.That(lake.Kind, Is.EqualTo(GeneratedHydrologyKind.Lake));
            Assert.That(lake.SurfaceLevel, Is.GreaterThanOrEqualTo(SpillBasinHeight(new Int2(32, 32))));

            bool downstreamRiver = false;
            for (int x = 36; x < 64 && !downstreamRiver; x++)
            for (int y = 0; y < 64 && !downstreamRiver; y++)
            {
                downstreamRiver = map.TryGet(x, y, out GeneratedHydrologyCell cell) &&
                                  cell.Kind == GeneratedHydrologyKind.River;
            }
            Assert.That(downstreamRiver, Is.True, "湖泊最低出口之后必须继续形成下游河流。");
        }

        [Test]
        [Category("Map.Hydrology")]
        public void AdjacentChunksAgreeOnCrossRegionRiverAndRepeatedOutput()
        {
            ChunkGenerationProfileSnapshot profile = CreateLegacyProfile(16, 64);
            var kernel = new LegacyHydrologyKernel();
            Func<Int2, double> height = position => Clamp01(
                0.82d - position.X * 0.0015d + Math.Abs(position.Y - 31.5d) * 0.002d);
            ChunkGenerationRequest leftRequest = CreateRequest(profile, new Int2(48, 16), 88271);
            ChunkGenerationRequest rightRequest = CreateRequest(profile, new Int2(64, 16), 88271);

            GeneratedHydrologyMap left = kernel.Build(
                leftRequest, profile.Settings, height, _ => 1d, CancellationToken.None);
            GeneratedHydrologyMap right = kernel.Build(
                rightRequest, profile.Settings, height, _ => 1d, CancellationToken.None);
            GeneratedHydrologyMap repeated = new LegacyHydrologyKernel().Build(
                leftRequest, profile.Settings, height, _ => 1d, CancellationToken.None);

            bool crossesBoundary = false;
            for (int y = 16; y < 32; y++)
            {
                bool leftWater = left.TryGet(63, y, out GeneratedHydrologyCell leftCell) &&
                                 leftCell.Kind == GeneratedHydrologyKind.River;
                bool rightWater = right.TryGet(64, y, out GeneratedHydrologyCell rightCell) &&
                                  rightCell.Kind == GeneratedHydrologyKind.River;
                crossesBoundary |= leftWater && rightWater;

                bool firstHasCell = left.TryGet(63, y, out GeneratedHydrologyCell first);
                bool repeatedHasCell = repeated.TryGet(63, y, out GeneratedHydrologyCell second);
                Assert.That(repeatedHasCell, Is.EqualTo(firstHasCell));
                if (firstHasCell)
                {
                    Assert.That(second.Kind, Is.EqualTo(first.Kind));
                    Assert.That(second.Flow, Is.EqualTo(first.Flow).Within(0.000001d));
                    Assert.That(second.Depth, Is.EqualTo(first.Depth).Within(0.000001d));
                }
            }

            Assert.That(crossesBoundary, Is.True, "河道必须连续跨过 64 格水文区域边界。");
        }

        [Test]
        [Category("Map.Hydrology")]
        public void PreCanceledBuildStopsBeforeCreatingRegion()
        {
            ChunkGenerationProfileSnapshot profile = CreateLegacyProfile(16, 64);
            ChunkGenerationRequest request = CreateRequest(profile, new Int2(0, 0), 7654);
            var kernel = new LegacyHydrologyKernel();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(() => kernel.Build(
                request,
                profile.Settings,
                _ => 0.75d,
                _ => 1d,
                cancellation.Token));
            Assert.That(kernel.CachedRegionCount, Is.Zero);
        }

        private static ChunkGenerationProfileSnapshot CreateLegacyProfile(
            int chunkSize,
            int regionSize)
        {
            return new ChunkGenerationProfileSnapshot(
                "legacy-hydrology-test",
                DeterministicChunkGenerator.CurrentGenerationSignature,
                chunkSize,
                chunkSize,
                new Dictionary<string, double>
                {
                    ["terrain.seaLevel"] = 0.5d,
                    ["river.enabled"] = 1d,
                    ["river.hydrologyRegionSize"] = regionSize,
                    ["river.runoffCellSize"] = 16d,
                    ["river.runoffSampleStride"] = 4d,
                    ["river.maxTraceSteps"] = 64d,
                    ["river.infiltrationFloor"] = 0d,
                    ["river.startFlow"] = 0.1d,
                    ["river.fullWidthFlow"] = 2d,
                    ["river.maxWidth"] = 5d,
                    ["river.meanderTieTolerance"] = 0.002d,
                    ["river.floodplainStartFlow"] = 0.1d,
                    ["river.floodplainMaxRadius"] = 6d,
                    ["river.minLakeCells"] = 18d,
                    ["river.maxLakeCells"] = 80d,
                    ["river.maxLakeLevelRise"] = 0.05d,
                    ["river.lakeMinFlow"] = 0.1d,
                    ["river.maxCachedRegions"] = 3d,
                    ["structure.enabled"] = 0d
                },
                new Dictionary<string, string>
                {
                    ["river.algorithm"] = "legacy"
                });
        }

        private static ChunkGenerationRequest CreateRequest(
            ChunkGenerationProfileSnapshot profile,
            Int2 origin,
            int seed)
        {
            return new ChunkGenerationRequest(
                1,
                new FlatWorld.WorldModel.WorldAddress("surface", origin),
                seed,
                1,
                profile);
        }

        private static double SpillBasinHeight(Int2 position)
        {
            if (position.Y == 32 && position.X >= 36)
                return Clamp(0.61d - (position.X - 36) * 0.0015d, 0.51d, 0.95d);
            int ring = Math.Max(Math.Abs(position.X - 32), Math.Abs(position.Y - 32));
            return Clamp(0.6d + ring * 0.005d, 0.51d, 0.95d);
        }

        private static double Clamp01(double value) => Clamp(value, 0d, 1d);

        private static double Clamp(double value, double minimum, double maximum) =>
            value < minimum ? minimum : value > maximum ? maximum : value;
    }
}
