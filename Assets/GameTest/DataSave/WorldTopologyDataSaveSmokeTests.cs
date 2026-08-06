using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace FlatWorld.GameTest.DataSave
{
    public sealed class WorldTopologyDataSaveSmokeTests
    {
        [Test]
        [Category("DataSave.Smoke")]
        public void WrappedPlanetConfigurationRoundTripsThroughMemoryPackContainer()
        {
            var source = new PlanetData
            {
                Name = "WrappedSave",
                Radius = 777,
                ChunkSize = new Vector2Int(16, 24),
                TopologyMode = WorldTopologyMode.Wrapped
            };
            var container = new Ex_ModData_MemoryPackable();
            container.WriteData(source);

            PlanetData restored = new PlanetData();
            container.ReadData(ref restored);

            Assert.That(restored.TopologyMode, Is.EqualTo(WorldTopologyMode.Wrapped));
            Assert.That(restored.Radius, Is.EqualTo(777));
            Assert.That(restored.ChunkSize, Is.EqualTo(new Vector2Int(16, 24)));
            Assert.That(WorldTopologyBounds.TryCreate(restored, out _), Is.True);
        }

        [Test]
        [Category("DataSave.Smoke")]
        public void MissingTopologyFieldDefaultsToStableInfiniteValueAndRemainsLastInLayout()
        {
            Assert.That((int)WorldTopologyMode.Infinite, Is.Zero);
            Assert.That(new PlanetData().TopologyMode, Is.EqualTo(WorldTopologyMode.Infinite));

            string source = File.ReadAllText("Assets/5_Scripts/5-3_GamePlay/Map/Data/PlanetData.cs");
            Assert.That(source.LastIndexOf("public WorldTopologyMode TopologyMode", System.StringComparison.Ordinal),
                Is.GreaterThan(source.LastIndexOf("public float StormTemperatureOffset", System.StringComparison.Ordinal)));
        }
    }
}
