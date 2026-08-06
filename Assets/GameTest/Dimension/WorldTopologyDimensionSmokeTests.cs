using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace FlatWorld.GameTest.Dimension
{
    public sealed class WorldTopologyDimensionSmokeTests
    {
        [Test]
        [Category("Dimension.Smoke")]
        public void DimensionWorldCloneInheritsRadiusAndTopologyAndClearsOnlyMaps()
        {
            var surface = new PlanetData
            {
                Name = "Surface",
                Radius = 321,
                ChunkSize = new Vector2Int(16, 16),
                TopologyMode = WorldTopologyMode.Wrapped
            };
            var container = new Ex_ModData_MemoryPackable();
            container.WriteData(surface);
            PlanetData clone = new PlanetData();
            container.ReadData(ref clone);
            clone.Name = "Surface::cave";
            clone.MapData_Dict.Clear();

            Assert.That(clone.TopologyMode, Is.EqualTo(WorldTopologyMode.Wrapped));
            Assert.That(clone.Radius, Is.EqualTo(321));
            Assert.That(WorldTopologyBounds.TryCreate(clone, out _), Is.True);

            string source = File.ReadAllText(
                "Assets/5_Scripts/5-3_GamePlay/Dimension/DimensionManager.cs");
            Assert.That(source, Does.Contain("FastCloner.FastCloner.DeepClone(source)"));
            Assert.That(source, Does.Contain("worldData.MapData_Dict = new Dictionary<string, MapSave>()"));
        }

        [Test]
        [Category("Dimension.Smoke")]
        public void CaveLayoutAndDepositsRepeatAcrossWrappedSidesAndCorners()
        {
            var planet = new PlanetData
            {
                Radius = 33,
                ChunkSize = new Vector2Int(16, 16),
                TopologyMode = WorldTopologyMode.Wrapped
            };
            Assert.That(WorldTopologyBounds.TryCreate(planet, out WorldTopologyBounds bounds), Is.True);
            DimensionDefinition cave = DimensionDefinition.CreateCave();
            const int seed = 918273;
            Vector2Int chunkSize = new(16, 16);

            for (int index = 0; index < 24; index++)
            {
                Vector2Int point = new(
                    bounds.Min.x + 1 + index * 7 % (bounds.Span.x - 2),
                    bounds.Min.y + 1 + index * 11 % (bounds.Span.y - 2));
                byte expected = ChunkGenerator_Cave.SampleCellClassification(
                    point,
                    cave,
                    seed,
                    chunkSize,
                    planet);
                float expectedDeposit = CaveLayoutSampler.GetDepositStrength(point, seed, planet);
                Vector2Int[] images =
                {
                    point + new Vector2Int(bounds.Span.x, 0),
                    point + new Vector2Int(0, -bounds.Span.y),
                    point + bounds.Span,
                    point + new Vector2Int(-2 * bounds.Span.x, 3 * bounds.Span.y)
                };
                foreach (Vector2Int image in images)
                {
                    Assert.That(
                        ChunkGenerator_Cave.SampleCellClassification(image, cave, seed, chunkSize, planet),
                        Is.EqualTo(expected),
                        $"classification at {image}");
                    Assert.That(
                        CaveLayoutSampler.GetDepositStrength(image, seed, planet),
                        Is.EqualTo(expectedDeposit).Within(0.00001f),
                        $"deposit at {image}");
                }
            }
        }
    }
}
