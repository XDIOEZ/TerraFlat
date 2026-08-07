using System.Threading;
using FlatWorld.WorldModel;
using NUnit.Framework;

namespace FlatWorld.GameTest.WorldModel
{
    /// <summary>世界模型精简冒烟测试：验证区块生成后的数据、模拟与表现生命周期。</summary>
    public sealed class WorldModelSmokeTests
    {
        [Test]
        [Category("WorldModel.Smoke")]
        [Category("Smoke")]
        public void ChunkLifecycleSeparatesDataSimulationAndPresentation()
        {
            var profile = new ChunkGenerationProfileSnapshot("smoke", 5, 8, 8);
            using var world = new WorldRuntime("smoke", 1);
            var address = new FlatWorld.WorldModel.WorldAddress("surface", new Int2(0, 0));
            ChunkGenerationRequest request = world.BeginChunkGeneration(address, 17, profile);
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
    }
}
