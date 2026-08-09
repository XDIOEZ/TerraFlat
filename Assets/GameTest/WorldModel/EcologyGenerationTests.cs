using System.Collections.Generic;
using System.Threading;
using FlatWorld.WorldModel;
using MemoryPack;
using NUnit.Framework;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

namespace FlatWorld.GameTest.WorldModel
{
    /// <summary>生态纯生成阶段测试：验证确定性、概率边界、环境过滤和宿主伴生关系。</summary>
    public sealed class EcologyGenerationTests
    {
        [Test]
        [Category("WorldModel.Ecology")]
        [Category("WorldModel.Smoke")]
        [Category("Smoke")]
        public void SameSeedProfileAndChunkProduceIdenticalPlacements()
        {
            EcologySpawnRuleSnapshot tree = CreateRule(
                "forest.tree", "AppleTree", 1d, 32, providedTag: "Tree");
            EcologySpawnRuleSnapshot log = CreateRule(
                "forest.log", "Log", 1d, 32, companionOnly: true,
                companionHostTag: "Tree", companionChance: 1d,
                offsetX: 0.9d, offsetY: -0.3d);
            ChunkGenerationProfileSnapshot profile = CreateProfile(tree, log);

            ChunkEcologyData first = Generate(profile, 1234, new Int2(32, -16));
            ChunkEcologyData second = Generate(profile, 1234, new Int2(32, -16));

            Assert.That(second.Count, Is.EqualTo(first.Count));
            for (int i = 0; i < first.Count; i++)
            {
                Assert.That(second.Placements[i].Guid, Is.EqualTo(first.Placements[i].Guid));
                Assert.That(second.Placements[i].ItemId, Is.EqualTo(first.Placements[i].ItemId));
                Assert.That(second.Placements[i].LocalX, Is.EqualTo(first.Placements[i].LocalX));
                Assert.That(second.Placements[i].LocalY, Is.EqualTo(first.Placements[i].LocalY));
                Assert.That(second.Placements[i].HostGuid,
                    Is.EqualTo(first.Placements[i].HostGuid));
                Assert.That(second.Placements[i].OffsetX,
                    Is.EqualTo(first.Placements[i].OffsetX));
                Assert.That(second.Placements[i].OffsetY,
                    Is.EqualTo(first.Placements[i].OffsetY));
            }
        }

        [Test]
        [Category("WorldModel.Ecology")]
        public void RuleOrderDoesNotChangeGeneratedPlacements()
        {
            EcologySpawnRuleSnapshot tree = CreateRule(
                "forest.tree", "AppleTree", 1d, 32, providedTag: "Tree");
            EcologySpawnRuleSnapshot log = CreateRule(
                "forest.log", "Log", 1d, 32, companionOnly: true,
                companionHostTag: "Tree", companionChance: 1d);
            ChunkGenerationProfileSnapshot firstProfile = CreateProfile(tree, log);

            ChunkEcologyData first = Generate(firstProfile, 77, new Int2(0, 0),
                new[] { tree, log });
            ChunkEcologyData second = Generate(firstProfile, 77, new Int2(0, 0),
                new[] { log, tree });

            Assert.That(second.Count, Is.EqualTo(first.Count));
            for (int i = 0; i < first.Count; i++)
                Assert.That(second.Placements[i].Guid, Is.EqualTo(first.Placements[i].Guid));
        }

        [Test]
        [Category("WorldModel.Ecology")]
        public void ZeroChanceAndInvalidCellsDoNotSpawn()
        {
            EcologySpawnRuleSnapshot zero = CreateRule(
                "forest.zero", "AppleTree", 0d, 32);
            ChunkGenerationProfileSnapshot profile = CreateProfile(zero);
            using var terrain = new ChunkTerrainBuffer(2, 2);
            SetCell(terrain, 0, 0, 32, TerrainCellFlags.Walkable);
            SetCell(terrain, 1, 0, 32, TerrainCellFlags.Walkable | TerrainCellFlags.Water);
            SetCell(terrain, 0, 1, 32, TerrainCellFlags.Walkable);
            terrain.SetEnvironmentValue("structure", 0, 1, 1f);
            SetCell(terrain, 1, 1, 32, TerrainCellFlags.Blocking);

            ChunkEcologyData zeroResult = ChunkEcologyGenerator.Generate(
                CreateRequest(profile, 99, new Int2(0, 0)), terrain, 1d,
                profile.EcologyRules, CancellationToken.None);
            Assert.That(zeroResult.Count, Is.Zero);
        }

        [Test]
        [Category("WorldModel.Ecology")]
        public void CaveGenerationReturnsEmptyEcology()
        {
            var texts = new Dictionary<string, string>
            {
                ["terrain.mode"] = "cave"
            };
            ChunkGenerationProfileSnapshot profile = new(
                "cave", DeterministicChunkGenerator.CurrentGenerationSignature, 2, 2,
                textParameters: texts,
                ecologyRules: new[] { CreateRule("cave.tree", "AppleTree", 1d, 0) });
            ChunkEcologyData result = Generate(profile, 1, new Int2(0, 0));
            Assert.That(result.Count, Is.Zero);
        }

        [Test]
        [Category("DataSave.Ecology")]
        public void EcologySaveDataRoundTripsConfigurationAndChunkDelta()
        {
            ChunkGenerationProfileSnapshot profile = CreateProfile(
                CreateRule("forest.tree", "AppleTree", 1d, 32, providedTag: "Tree"));
            var save = new EcologyWorldSaveData();
            save.CaptureConfiguration(profile);
            save.MarkRemoved(16, -16, 123);
            var changed = new Data_GeneralItem
            {
                IDName = "Stick_Wood",
                Guid = 456
            };
            save.CaptureChangedItem(16, -16, changed);

            byte[] bytes = MemoryPackSerializer.Serialize(save);
            EcologyWorldSaveData restored =
                MemoryPackSerializer.Deserialize<EcologyWorldSaveData>(bytes);

            Assert.That(restored, Is.Not.Null);
            Assert.That(restored.HasConfiguration, Is.True);
            Assert.That(restored.ConfigurationFingerprint,
                Is.EqualTo(save.ConfigurationFingerprint));
            Assert.That(restored.HasGenerationConfiguration, Is.True);
            Assert.That(restored.TryApplyGenerationConfiguration(profile,
                out ChunkGenerationProfileSnapshot restoredProfile), Is.True);
            Assert.That(restoredProfile.GenerationFingerprint,
                Is.EqualTo(profile.GenerationFingerprint));
            Assert.That(restored.IsRemoved(16, -16, 123), Is.True);
            Assert.That(restored.TryGetChangedItem(16, -16, 456, out ItemData restoredItem),
                Is.True);
            Assert.That(restoredItem.IDName, Is.EqualTo("Stick_Wood"));
        }

        private static ChunkEcologyData Generate(ChunkGenerationProfileSnapshot profile,
            int seed, Int2 origin,
            IReadOnlyList<EcologySpawnRuleSnapshot> rules = null)
        {
            using var terrain = new ChunkTerrainBuffer(4, 4);
            for (int y = 0; y < terrain.Height; y++)
            for (int x = 0; x < terrain.Width; x++)
                SetCell(terrain, x, y, 32, TerrainCellFlags.Walkable);

            return ChunkEcologyGenerator.Generate(
                CreateRequest(profile, seed, origin), terrain, profile.EcologyGlobalMultiplier,
                rules ?? profile.EcologyRules, CancellationToken.None);
        }

        private static void SetCell(ChunkTerrainBuffer terrain, int x, int y, int biomeId,
            TerrainCellFlags flags)
        {
            terrain.SetCell(x, y, new TerrainCell(1, 0, 0, biomeId, 1, flags));
            terrain.SetEnvironmentValue("temperature", x, y, 0.5f);
            terrain.SetEnvironmentValue("precipitation", x, y, 0.5f);
            terrain.SetEnvironmentValue("height", x, y, 0.5f);
        }

        private static ChunkGenerationRequest CreateRequest(
            ChunkGenerationProfileSnapshot profile, int seed, Int2 origin)
        {
            return new ChunkGenerationRequest(
                1,
                new RuntimeWorldAddress("surface", origin),
                seed,
                1,
                profile);
        }

        private static ChunkGenerationProfileSnapshot CreateProfile(
            params EcologySpawnRuleSnapshot[] rules)
        {
            return new ChunkGenerationProfileSnapshot(
                "ecology-test",
                DeterministicChunkGenerator.CurrentGenerationSignature,
                4,
                4,
                ecologyRules: rules);
        }

        private static EcologySpawnRuleSnapshot CreateRule(
            string ruleId, string itemId, double chance, int biomeMask,
            string providedTag = null, bool companionOnly = false,
            string companionHostTag = null, double companionChance = 0d,
            double offsetX = 0d, double offsetY = 0d)
        {
            var tags = string.IsNullOrWhiteSpace(providedTag)
                ? null
                : new[] { providedTag };
            return new EcologySpawnRuleSnapshot(
                ruleId, itemId, 1, chance, 1d, biomeMask,
                0d, 1d, 0d, 1d, 0d, 1d,
                tags, companionOnly, companionHostTag, companionChance,
                offsetX, offsetY);
        }
    }
}
