using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace FlatWorld.WorldModel.Tests
{
    [Category("WorldModel.Smoke")]
    public sealed class WorldRuntimeTests
    {
        private static readonly ChunkGenerationProfileSnapshot Profile =
            new ChunkGenerationProfileSnapshot("test", 5, 16, 16);

        [Test]
        public void ModelAssembly_HasNoUnityEngineReference()
        {
            string[] references = typeof(WorldRuntime).Assembly.GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .ToArray();

            CollectionAssert.DoesNotContain(references, "UnityEngine.CoreModule");
            Assert.That(references.Any(name => name.StartsWith("UnityEngine", StringComparison.Ordinal)),
                Is.False);
        }

        [Test]
        public void FullChunk_GeneratesAndSimulatesWithoutPresentation()
        {
            using var world = new WorldRuntime("test", 1);
            WorldAddress address = Address(0);
            ChunkGenerationRequest request = world.BeginChunkGeneration(address, 123, Profile);
            using ChunkGenerationResult result = CreateResult(request, 7);

            Assert.That(world.TryCommit(result, out string rejection), Is.True, rejection);
            Assert.That(world.Chunks[address].PresentationStatus, Is.EqualTo(ChunkPresentationStatus.Unbound));

            using (world.AcquireChunkLease(address, ChunkLeaseKind.Simulation))
                Assert.That(world.Chunks[address].SimulationStatus,
                    Is.EqualTo(ChunkSimulationStatus.Active));
            Assert.That(world.Chunks[address].SimulationStatus,
                Is.EqualTo(ChunkSimulationStatus.Dormant));
        }

        [Test]
        public void PresentationLease_RebindsWithoutDuplicateCountsOrDataMutation()
        {
            using var world = new WorldRuntime("test", 1);
            WorldAddress address = Address(0);
            ChunkGenerationRequest request = world.BeginChunkGeneration(address, 123, Profile);
            using ChunkGenerationResult result = CreateResult(request, 11);
            Assert.That(world.TryCommit(result, out _), Is.True);

            ChunkRuntime chunk = world.Chunks[address];
            ulong hash = chunk.Terrain.ComputeStableHash();
            for (int i = 0; i < 3; i++)
            {
                using (ChunkLease lease = world.AcquireChunkLease(address, ChunkLeaseKind.Presentation))
                {
                    Assert.That(chunk.PresentationLeaseCount, Is.EqualTo(1));
                    Assert.That(chunk.PresentationStatus, Is.EqualTo(ChunkPresentationStatus.Binding));
                    chunk.MarkPresentationBound();
                    Assert.That(chunk.PresentationStatus, Is.EqualTo(ChunkPresentationStatus.Bound));
                }
                Assert.That(chunk.PresentationLeaseCount, Is.Zero);
                Assert.That(chunk.PresentationStatus, Is.EqualTo(ChunkPresentationStatus.Unbound));
                Assert.That(chunk.Terrain.ComputeStableHash(), Is.EqualTo(hash));
            }
        }

        [Test]
        public void StaleAndCancelledResults_NeverCommit()
        {
            using var world = new WorldRuntime("test", 1);
            WorldAddress address = Address(0);
            ChunkGenerationRequest stale = world.BeginChunkGeneration(address, 123, Profile);
            ChunkGenerationRequest current = world.BeginChunkGeneration(address, 123, Profile);

            using ChunkGenerationResult staleResult = CreateResult(stale, 1);
            Assert.That(world.TryCommit(staleResult, out _), Is.False);
            Assert.That(staleResult.IsDisposed, Is.True);

            Assert.That(world.CancelChunkGeneration(address), Is.True);
            using ChunkGenerationResult cancelledResult = CreateResult(current, 2);
            Assert.That(world.TryCommit(cancelledResult, out _), Is.False);
            Assert.That(cancelledResult.IsDisposed, Is.True);
            Assert.That(world.Chunks[address].Terrain, Is.Null);
        }

        [Test]
        public async Task CompletionOrder_DoesNotChangeTerrainHash()
        {
            var delays = new ConcurrentDictionary<int, int>();
            delays[0] = 80;
            delays[16] = 5;
            var generator = new DelayedGenerator(delays);
            using var scheduler = new ChunkGenerationScheduler(generator, 2);
            using var worldA = new WorldRuntime("a", 1);

            var requestsA = new[]
            {
                worldA.BeginChunkGeneration(Address(0), 99, Profile),
                worldA.BeginChunkGeneration(Address(16), 99, Profile)
            };
            Task<ChunkGenerationResult>[] tasksA = requestsA
                .Select(request => scheduler.ScheduleAsync(request)).ToArray();
            ChunkGenerationResult[] resultsA = await Task.WhenAll(tasksA);
            foreach (ChunkGenerationResult result in resultsA.OrderByDescending(value => value.Request.Address))
                Assert.That(worldA.TryCommit(result, out _), Is.True);
            ulong hashA = ComputeWorldHash(worldA);

            delays[0] = 5;
            delays[16] = 80;
            using var worldB = new WorldRuntime("b", 1);
            var requestsB = new[]
            {
                worldB.BeginChunkGeneration(Address(0), 99, Profile),
                worldB.BeginChunkGeneration(Address(16), 99, Profile)
            };
            ChunkGenerationResult[] resultsB = await Task.WhenAll(
                requestsB.Select(request => scheduler.ScheduleAsync(request)));
            foreach (ChunkGenerationResult result in resultsB)
                Assert.That(worldB.TryCommit(result, out _), Is.True);

            Assert.That(ComputeWorldHash(worldB), Is.EqualTo(hashA));
        }

        [Test]
        public void WorldSnapshot_ContainsOnlyCommittedChunks()
        {
            using var world = new WorldRuntime("test", 1);
            ChunkGenerationRequest committed = world.BeginChunkGeneration(Address(0), 1, Profile);
            world.BeginChunkGeneration(Address(16), 1, Profile);
            using ChunkGenerationResult result = CreateResult(committed, 3);
            Assert.That(world.TryCommit(result, out _), Is.True);

            WorldRuntimeSnapshot snapshot = world.CaptureSnapshot();
            Assert.That(snapshot.Chunks.Count, Is.EqualTo(1));
            Assert.That(snapshot.Chunks[0].Address, Is.EqualTo(Address(0)));
        }

        [Test]
        public void TerrainModel_OwnsGrassStacksEnvironmentAndChangeRevision()
        {
            using var buffer = new ChunkTerrainBuffer(2, 2);
            buffer.SetCell(0, 0, new TerrainCell(1, 0, 0, 4, 1,
                TerrainCellFlags.Walkable));
            buffer.SetGrass(0, 0, 2);
            buffer.SetEnvironmentValue("temperature.celsius", 0, 0, 21f);
            buffer.SetExtendedTileStack(0, 0, new[] { 1, 3, 4, 5 });
            using ChunkTerrainData terrain = buffer.Seal();
            int changes = 0;
            terrain.Changed += _ => changes++;

            Assert.That(terrain.GetGrass(0, 0), Is.EqualTo(2));
            Assert.That(terrain.GetTileLayerCount(0, 0), Is.EqualTo(4));
            Assert.That(terrain.GetTopTileId(0, 0), Is.EqualTo(5));
            Assert.That(terrain.TryGetEnvironmentValue("temperature.celsius", 0, 0,
                out float temperature), Is.True);
            Assert.That(temperature, Is.EqualTo(21f));

            ulong before = terrain.ComputeStableHash();
            terrain.SetGrass(0, 0, 3);
            terrain.SetEnvironmentValue("temperature.celsius", 0, 0, 18f);
            Assert.That(terrain.Revision, Is.EqualTo(2));
            Assert.That(changes, Is.EqualTo(2));
            Assert.That(terrain.ComputeStableHash(), Is.Not.EqualTo(before));
        }

        [Test]
        public async Task PureChunkManager_WindowSleepsReactivatesAndEvicts()
        {
            using var world = new WorldRuntime("window", 1);
            using var manager = new ChunkMgr(world, new DeterministicChunkGenerator(), 1);
            manager.RefreshWindow(new ChunkWindowRequest(Address(0), 1, 2, false, 77, Profile));
            await Settle(manager);
            ChunkRuntime origin = world.Chunks[Address(0)];
            ulong originHash = origin.Terrain.ComputeStableHash();
            Assert.That(origin.SimulationStatus, Is.EqualTo(ChunkSimulationStatus.Active));

            manager.RefreshWindow(new ChunkWindowRequest(Address(16), 1, 2, false, 77, Profile));
            await Settle(manager);
            Assert.That(origin.SimulationStatus, Is.EqualTo(ChunkSimulationStatus.Dormant));
            Assert.That(world.Chunks.ContainsKey(Address(0)), Is.True);

            manager.RefreshWindow(new ChunkWindowRequest(Address(0), 1, 2, false, 77, Profile));
            await Settle(manager);
            Assert.That(world.Chunks[Address(0)], Is.SameAs(origin));
            Assert.That(origin.SimulationStatus, Is.EqualTo(ChunkSimulationStatus.Active));
            Assert.That(origin.Terrain.ComputeStableHash(), Is.EqualTo(originHash));

            manager.RefreshWindow(new ChunkWindowRequest(Address(32), 1, 2, false, 77, Profile));
            await Settle(manager);
            Assert.That(world.Chunks.ContainsKey(Address(0)), Is.False);
        }

        private static async Task Settle(ChunkMgr manager)
        {
            for (int i = 0; manager.HasUnsettledGenerationTasks && i < 1000; i++)
            {
                manager.CommitCompleted();
                if (manager.HasUnsettledGenerationTasks)
                    await Task.Yield();
            }
            manager.CommitCompleted();
            Assert.That(manager.HasUnsettledGenerationTasks, Is.False);
        }

        private static WorldAddress Address(int x) => new WorldAddress("surface", new Int2(x, 0));

        private static ChunkGenerationResult CreateResult(ChunkGenerationRequest request, int salt)
        {
            var terrain = new ChunkTerrainBuffer(request.Profile.Width, request.Profile.Height);
            for (int y = 0; y < request.Profile.Height; y++)
            {
                for (int x = 0; x < request.Profile.Width; x++)
                {
                    int value = unchecked(request.WorldSeed * 31 + request.Address.ChunkOrigin.X * 17 +
                                          x * 7 + y * 13 + salt);
                    terrain.SetCell(x, y, new TerrainCell(value, 0, 0, value & 3, 1,
                        TerrainCellFlags.Walkable));
                    terrain.SetEnvironmentValue("temperature", x, y, value * 0.01f);
                }
            }

            return new ChunkGenerationResult(request, terrain);
        }

        private static ulong ComputeWorldHash(WorldRuntime world)
        {
            ulong hash = 14695981039346656037UL;
            foreach (KeyValuePair<WorldAddress, ChunkRuntime> pair in world.Chunks.OrderBy(pair => pair.Key))
            {
                hash ^= pair.Value.Terrain.ComputeStableHash();
                hash *= 1099511628211UL;
            }
            return hash;
        }

        private sealed class DelayedGenerator : IChunkPureGenerator
        {
            private readonly ConcurrentDictionary<int, int> _delays;

            public DelayedGenerator(ConcurrentDictionary<int, int> delays) => _delays = delays;

            public ChunkGenerationResult Generate(ChunkGenerationRequest request,
                CancellationToken cancellationToken)
            {
                cancellationToken.WaitHandle.WaitOne(_delays[request.Address.ChunkOrigin.X]);
                cancellationToken.ThrowIfCancellationRequested();
                return CreateResult(request, 5);
            }
        }

    }
}
