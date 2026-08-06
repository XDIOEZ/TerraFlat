using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using FlatWorld.WorldModel;
using NUnit.Framework;

namespace FlatWorld.GameTest.WorldModel
{
    [Category("WorldModel.Smoke")]
    public sealed class WorldModelSmokeTests
    {
        private static readonly ChunkGenerationProfileSnapshot Profile =
            new("smoke", 5, 8, 8);

        [Test]
        public void ModelAssemblyHasNoUnityReference()
        {
            Assert.That(typeof(WorldRuntime).Assembly.GetReferencedAssemblies()
                .Any(reference => reference.Name.StartsWith("UnityEngine", StringComparison.Ordinal)), Is.False);
        }

        [Test]
        public void ChunkLifecycleSeparatesDataSimulationAndPresentation()
        {
            using var world = new WorldRuntime("smoke", 1);
            var address = new FlatWorld.WorldModel.WorldAddress("surface", new Int2(0, 0));
            ChunkGenerationRequest request = world.BeginChunkGeneration(address, 17, Profile);
            using ChunkGenerationResult result = new DeterministicChunkGenerator().Generate(
                request, CancellationToken.None);
            Assert.That(world.TryCommit(result, out string reason), Is.True, reason);

            ChunkRuntime chunk = world.Chunks[address];
            Assert.That(chunk.DataStatus, Is.EqualTo(ChunkDataStatus.Ready));
            Assert.That(chunk.SimulationStatus, Is.EqualTo(ChunkSimulationStatus.Dormant));
            Assert.That(chunk.PresentationStatus, Is.EqualTo(ChunkPresentationStatus.Unbound));

            using (chunk.AcquireLease(ChunkLeaseKind.Simulation))
                Assert.That(chunk.SimulationStatus, Is.EqualTo(ChunkSimulationStatus.Active));
            Assert.That(chunk.SimulationStatus, Is.EqualTo(ChunkSimulationStatus.Dormant));
        }

        [Test]
        public void StaleResultIsDisposedAndNeverCommitted()
        {
            using var world = new WorldRuntime("smoke", 1);
            var address = new FlatWorld.WorldModel.WorldAddress("surface", new Int2(0, 0));
            ChunkGenerationRequest stale = world.BeginChunkGeneration(address, 17, Profile);
            world.BeginChunkGeneration(address, 17, Profile);
            ChunkGenerationResult result = new DeterministicChunkGenerator().Generate(
                stale, CancellationToken.None);

            Assert.That(world.TryCommit(result, out _), Is.False);
            Assert.That(result.IsDisposed, Is.True);
            Assert.That(world.Chunks[address].Terrain, Is.Null);
        }

        [Test]
        public void RepeatedPresentationLeaseDoesNotMutateTerrainHash()
        {
            using var world = new WorldRuntime("smoke", 1);
            var address = new FlatWorld.WorldModel.WorldAddress("surface", new Int2(0, 0));
            ChunkGenerationRequest request = world.BeginChunkGeneration(address, 17, Profile);
            using ChunkGenerationResult result = new DeterministicChunkGenerator().Generate(
                request, CancellationToken.None);
            Assert.That(world.TryCommit(result, out _), Is.True);
            ChunkRuntime chunk = world.Chunks[address];
            ulong hash = chunk.Terrain.ComputeStableHash();

            for (int i = 0; i < 3; i++)
            {
                using (chunk.AcquireLease(ChunkLeaseKind.Presentation))
                    chunk.MarkPresentationBound();
                Assert.That(chunk.PresentationStatus, Is.EqualTo(ChunkPresentationStatus.Unbound));
                Assert.That(chunk.Terrain.ComputeStableHash(), Is.EqualTo(hash));
            }
        }

        [Test]
        public void ChunkGeneratorDoesNotCreateHeadlessGameplayEntities()
        {
            Assert.That(typeof(WorldRuntime).Assembly.GetType(
                "FlatWorld.WorldModel.EntityRuntime"), Is.Null);
            Assert.That(typeof(ChunkGenerationResult).GetProperty("EntitySpawns"), Is.Null);
        }

        [Test]
        public void WrappedTerrainRepeatsAcrossNorthSouthAndCornerImages()
        {
            var topology = new ChunkGenerationTopologySnapshot(
                new Int2(-32, -32), new Int2(64, 64));
            var profile = new ChunkGenerationProfileSnapshot("wrapped", 5, 16, 16,
                new Dictionary<string, double>(StringComparer.Ordinal)
                {
                    ["structure.enabled"] = 0d,
                    ["terrain.noiseScale"] = 0.035d,
                    ["climate.noiseScale"] = 0.027d,
                    ["river.noiseScale"] = 0.021d
                });

            ulong canonical = GenerateHash(new Int2(-16, -32), profile, topology);
            ulong northImage = GenerateHash(new Int2(-16, 32), profile, topology);
            ulong cornerImage = GenerateHash(new Int2(48, 32), profile, topology);

            Assert.That(northImage, Is.EqualTo(canonical),
                "North/south images must sample the same periodic terrain domain.");
            Assert.That(cornerImage, Is.EqualTo(canonical),
                "Corner images must repeat on both wrapped axes.");
        }

        [Test]
        public void WrappedWindowRetainsChunksAcrossNorthSouthSeam()
        {
            var topology = new ChunkGenerationTopologySnapshot(
                new Int2(-32, -32), new Int2(64, 64));
            using var world = new WorldRuntime("wrapped-window", 1);
            var northAddress = new FlatWorld.WorldModel.WorldAddress("surface", new Int2(0, 24));
            var southAddress = new FlatWorld.WorldModel.WorldAddress("surface", new Int2(0, -32));
            ChunkGenerationRequest request = world.BeginChunkGeneration(
                southAddress, 17, Profile, topology);
            using ChunkGenerationResult result = new DeterministicChunkGenerator().Generate(
                request, CancellationToken.None);
            Assert.That(world.TryCommit(result, out string rejection), Is.True, rejection);

            using var manager = new FlatWorld.WorldModel.ChunkMgr(world,
                new DeterministicChunkGenerator(), 1, new WrappedAddressNormalizer(topology));
            manager.RefreshWindow(new ChunkWindowRequest(northAddress, 1, 2,
                false, 17, Profile, topology));

            Assert.That(manager.Chunks.ContainsKey(southAddress), Is.True,
                "A destroy-distance neighbor across the seam must not be evicted.");
        }

        private static ulong GenerateHash(Int2 origin, ChunkGenerationProfileSnapshot profile,
            ChunkGenerationTopologySnapshot topology)
        {
            using var world = new WorldRuntime($"hash-{origin}", 1);
            var address = new FlatWorld.WorldModel.WorldAddress("surface", origin);
            ChunkGenerationRequest request = world.BeginChunkGeneration(address, 42017,
                profile, topology);
            using ChunkGenerationResult result = new DeterministicChunkGenerator().Generate(
                request, CancellationToken.None);
            Assert.That(world.TryCommit(result, out string rejection), Is.True, rejection);
            return world.Chunks[address].Terrain.ComputeStableHash();
        }

        private sealed class WrappedAddressNormalizer : IWorldAddressNormalizer
        {
            private readonly ChunkGenerationTopologySnapshot topology;

            public WrappedAddressNormalizer(ChunkGenerationTopologySnapshot topology)
            {
                this.topology = topology;
            }

            public FlatWorld.WorldModel.WorldAddress Normalize(
                FlatWorld.WorldModel.WorldAddress address)
            {
                return new FlatWorld.WorldModel.WorldAddress(address.DimensionId,
                    new Int2(topology.NormalizeX(address.ChunkOrigin.X),
                        topology.NormalizeY(address.ChunkOrigin.Y)));
            }
        }
    }
}
