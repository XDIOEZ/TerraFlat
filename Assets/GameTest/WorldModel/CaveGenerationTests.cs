using System;
using System.Collections.Generic;
using System.Threading;
using FlatWorld.WorldModel;
using NUnit.Framework;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

namespace FlatWorld.GameTest.WorldModel
{
    /// <summary>验证旧矿洞布局迁入新版纯区块结果后的确定性、入口安全区与矿脉输出。</summary>
    [Category("WorldModel.Cave")]
    public sealed class CaveGenerationTests
    {
        [Test]
        [Category("WorldModel.Smoke")]
        [Category("Smoke")]
        public void SameCaveInputProducesStableTerrainResourcesAndPortals()
        {
            ChunkGenerationProfileSnapshot profile = CreateCaveProfile(portalChance: 1d,
                resourceDensity: 1d);
            CaveObservation first = GenerateCave(profile, 424242, new Int2(0, 0));
            CaveObservation second = GenerateCave(profile, 424242, new Int2(0, 0));

            Assert.That(second.TerrainHash, Is.EqualTo(first.TerrainHash));
            Assert.That(second.Placements.Count, Is.EqualTo(first.Placements.Count));
            for (int i = 0; i < first.Placements.Count; i++)
            {
                NaturalItemPlacement expected = first.Placements[i];
                NaturalItemPlacement actual = second.Placements[i];
                Assert.That(actual.Guid, Is.EqualTo(expected.Guid));
                Assert.That(actual.ItemId, Is.EqualTo(expected.ItemId));
                Assert.That(actual.LocalX, Is.EqualTo(expected.LocalX));
                Assert.That(actual.LocalY, Is.EqualTo(expected.LocalY));
                Assert.That(actual.TargetDimensionId, Is.EqualTo(expected.TargetDimensionId));
            }

            NaturalItemPlacement portal = first.Placements.Find(
                placement => placement.IsDimensionPortal);
            Assert.That(portal.ItemId, Is.EqualTo("CaveExit"));
            Assert.That(portal.TargetDimensionId, Is.EqualTo("surface"));
            Assert.That(first.IsWalkable(portal.LocalX, portal.LocalY), Is.True,
                "洞穴出口必须在迁移后的安全空地中。");
        }

        [Test]
        public void SurfaceEntranceAndCaveExitUseTheSameSingleCandidate()
        {
            const int surfaceSeed = 71;
            var origin = new Int2(0, 0);
            ChunkGenerationProfileSnapshot surface = CreateSurfaceProfile(portalChance: 1d);
            ChunkGenerationProfileSnapshot cave = CreateCaveProfile(portalChance: 1d,
                resourceDensity: 0d, pairedSurfaceProfile: surface, surfaceSeed: surfaceSeed);

            List<NaturalItemPlacement> surfacePlacements = GenerateSurface(surface, surfaceSeed,
                origin);
            CaveObservation caveResult = GenerateCave(cave, 91357, origin);
            NaturalItemPlacement surfacePortal = surfacePlacements.Find(
                placement => placement.IsDimensionPortal);
            List<NaturalItemPlacement> cavePortals = caveResult.Placements.FindAll(
                placement => placement.IsDimensionPortal);

            Assert.That(surfacePortal.ItemId, Is.EqualTo("CaveExit"));
            Assert.That(cavePortals, Has.Count.EqualTo(1),
                "一个地表入口只能对应一个地下出口，不能再输出全部回退候选。 ");
            Assert.That(cavePortals[0].LocalX, Is.EqualTo(surfacePortal.LocalX));
            Assert.That(cavePortals[0].LocalY, Is.EqualTo(surfacePortal.LocalY));
            Assert.That(caveResult.IsWalkable(cavePortals[0].LocalX,
                cavePortals[0].LocalY), Is.True);
        }

        [Test]
        public void CaveResourcesAppearDeterministicallyOnWallEdges()
        {
            ChunkGenerationProfileSnapshot profile = CreateCaveProfile(portalChance: 0d,
                resourceDensity: 1d);
            CaveObservation resourceChunk = default;
            bool found = false;
            for (int chunkY = -3; chunkY <= 3 && !found; chunkY++)
            {
                for (int chunkX = -3; chunkX <= 3; chunkX++)
                {
                    CaveObservation current = GenerateCave(profile, 91357,
                        new Int2(chunkX * profile.Width, chunkY * profile.Height));
                    if (!current.Placements.Exists(placement =>
                            placement.ItemId.StartsWith("Mine_", StringComparison.Ordinal)))
                    {
                        continue;
                    }

                    resourceChunk = current;
                    found = true;
                    break;
                }
            }

            Assert.That(found, Is.True, "固定搜索范围应至少命中一条洞壁矿脉。");
            foreach (NaturalItemPlacement placement in resourceChunk.Placements)
            {
                if (!placement.ItemId.StartsWith("Mine_", StringComparison.Ordinal))
                    continue;
                Assert.That(resourceChunk.IsWalkable(placement.LocalX, placement.LocalY), Is.True);
            }
        }

        [Test]
        public void SurfacePortalChanceRespectsZeroAndOneBoundaries()
        {
            using var terrain = new ChunkTerrainBuffer(16, 16);
            for (int y = 0; y < terrain.Height; y++)
            for (int x = 0; x < terrain.Width; x++)
                terrain.SetCell(x, y, new TerrainCell(1, 0, 0, 4, 1,
                    TerrainCellFlags.Walkable));

            ChunkGenerationProfileSnapshot none = CreateSurfaceProfile(portalChance: 0d);
            ChunkGenerationProfileSnapshot always = CreateSurfaceProfile(portalChance: 1d);
            ChunkEcologyData noneResult = CaveGenerationFeatureGenerator.AppendSurfacePortals(
                CreateRequest(none, 71, new Int2(0, 0), "surface"), terrain,
                ChunkEcologyData.Empty);
            ChunkEcologyData alwaysResult = CaveGenerationFeatureGenerator.AppendSurfacePortals(
                CreateRequest(always, 71, new Int2(0, 0), "surface"), terrain,
                ChunkEcologyData.Empty);

            Assert.That(noneResult.Count, Is.Zero);
            Assert.That(alwaysResult.Count, Is.EqualTo(1));
            Assert.That(alwaysResult.Placements[0].IsDimensionPortal, Is.True);
            Assert.That(alwaysResult.Placements[0].TargetDimensionId, Is.EqualTo("cave"));
        }

        [Test]
        public void LargePreviewUsesCanonicalPortalChunkSize()
        {
            using var terrain = new ChunkTerrainBuffer(32, 16);
            for (int y = 0; y < terrain.Height; y++)
            for (int x = 0; x < terrain.Width; x++)
                terrain.SetCell(x, y, new TerrainCell(1, 0, 0, 4, 1,
                    TerrainCellFlags.Walkable));

            ChunkGenerationProfileSnapshot source = CreateSurfaceProfile(portalChance: 1d);
            var numbers = new Dictionary<string, double>(source.NumericParameters)
            {
                ["cave.portal.chunkWidth"] = 16d,
                ["cave.portal.chunkHeight"] = 16d
            };
            var preview = new ChunkGenerationProfileSnapshot("surface.preview.test",
                source.Signature, 32, 16, numbers,
                new Dictionary<string, string>(source.TextParameters));

            ChunkEcologyData result = CaveGenerationFeatureGenerator.AppendSurfacePortals(
                CreateRequest(preview, 71, new Int2(0, 0), "surface"), terrain,
                ChunkEcologyData.Empty);

            Assert.That(result.Count, Is.EqualTo(2),
                "连续大范围预览必须仍按两个正式 16×16 概率格生成入口。");
        }

        [Test]
        public void CaveResourceRulesParticipateInGenerationFingerprint()
        {
            ChunkGenerationProfileSnapshot stone = CreateCaveProfile(portalChance: 0d,
                resourceDensity: 0d);
            var changedRules = new[]
            {
                new CaveResourceRuleSnapshot("cave.resource.stone", "Mine_Stone", 0d,
                    0.08d, 5501)
            };
            ChunkGenerationProfileSnapshot changed = new(
                stone.ProfileId, stone.Signature, stone.Width, stone.Height,
                new Dictionary<string, double>(stone.NumericParameters),
                new Dictionary<string, string>(stone.TextParameters),
                stone.EcologyGlobalMultiplier, stone.EcologyRules, changedRules);

            Assert.That(changed.GenerationFingerprint,
                Is.Not.EqualTo(stone.GenerationFingerprint));
        }

        [Test]
        public void GroundwaterCreatesFreshwaterLakesOutsideSpawnSafeArea()
        {
            ChunkGenerationProfileSnapshot profile = CreateCaveProfile(portalChance: 0d,
                resourceDensity: 0d, groundwaterEnabled: true);
            int waterCells = 0;
            int deepWaterCells = 0;
            for (int chunkY = -4; chunkY <= 4; chunkY++)
            for (int chunkX = -4; chunkX <= 4; chunkX++)
            {
                CaveObservation cave = GenerateCave(profile, 91357,
                    new Int2(chunkX * profile.Width, chunkY * profile.Height));
                waterCells += cave.WaterCellCount;
                deepWaterCells += cave.DeepWaterCellCount;
                if (chunkX == 0 && chunkY == 0)
                    Assert.That(cave.IsWater(0, 0), Is.False, "默认出生安全区不得积水。");
            }

            Assert.That(waterCells, Is.GreaterThan(0), "固定范围内应生成确定性的地下湖。 ");
            Assert.That(deepWaterCells, Is.GreaterThan(0), "地下湖中心应具有高于岸边的水深。 ");
        }

        [Test]
        public void CaveVinesGenerateDeterministicallyOnDryWalkableWallEdges()
        {
            ChunkGenerationProfileSnapshot profile = CreateCaveProfile(portalChance: 0d,
                resourceDensity: 0d, groundwaterEnabled: true, vineEnabled: true);
            int vineCells = 0;
            for (int chunkY = -4; chunkY <= 4; chunkY++)
            for (int chunkX = -4; chunkX <= 4; chunkX++)
            {
                CaveObservation cave = GenerateCave(profile, 91357,
                    new Int2(chunkX * profile.Width, chunkY * profile.Height));
                List<NaturalItemPlacement> vines = cave.Placements.FindAll(
                    placement => placement.ItemId == "Twine" &&
                                 placement.RuleId == "cave.vine.twine");
                vineCells += vines.Count;
                foreach (NaturalItemPlacement vine in vines)
                {
                    Assert.That(cave.IsWater(vine.LocalX, vine.LocalY), Is.False);
                    Assert.That(cave.IsWalkable(vine.LocalX, vine.LocalY), Is.True,
                        "可采集藤蔓只能出现在无水可走的洞壁边缘。 ");
                }
            }

            Assert.That(vineCells, Is.GreaterThan(0), "固定范围内应生成确定性的洞壁藤蔓。 ");
        }

        #region 构造与读取

        private static CaveObservation GenerateCave(ChunkGenerationProfileSnapshot profile,
            int seed, Int2 origin)
        {
            var address = new RuntimeWorldAddress("cave", origin);
            using var world = new WorldRuntime("cave-generation-test", 1);
            ChunkGenerationRequest request = world.BeginChunkGeneration(address, seed, profile);
            using ChunkGenerationResult result = new DeterministicChunkGenerator().Generate(
                request, CancellationToken.None);
            Assert.That(world.TryCommit(result, out string rejection), Is.True, rejection);
            ChunkRuntime chunk = world.Chunks[address];
            var placements = new List<NaturalItemPlacement>(chunk.Ecology.Placements);
            var walkable = new bool[chunk.Terrain.Width, chunk.Terrain.Height];
            var water = new bool[chunk.Terrain.Width, chunk.Terrain.Height];
            int waterCellCount = 0;
            int deepWaterCellCount = 0;
            for (int y = 0; y < chunk.Terrain.Height; y++)
            for (int x = 0; x < chunk.Terrain.Width; x++)
            {
                walkable[x, y] = chunk.Terrain.IsWalkable(x, y);
                TerrainCell cell = chunk.Terrain.GetCell(x, y);
                water[x, y] = (cell.Flags & TerrainCellFlags.Water) != 0;
                if (!water[x, y])
                    continue;
                waterCellCount++;
                if (chunk.Terrain.TryGetEnvironmentValue("riverDepth", x, y, out float depth) &&
                    depth >= 0.5f)
                    deepWaterCellCount++;
            }
            return new CaveObservation(chunk.Terrain.ComputeStableHash(), placements, walkable,
                water, waterCellCount, deepWaterCellCount);
        }

        private static List<NaturalItemPlacement> GenerateSurface(
            ChunkGenerationProfileSnapshot profile, int seed, Int2 origin)
        {
            var address = new RuntimeWorldAddress("surface", origin);
            using var world = new WorldRuntime("surface-generation-test", 1);
            ChunkGenerationRequest request = world.BeginChunkGeneration(address, seed, profile);
            using ChunkGenerationResult result = new DeterministicChunkGenerator().Generate(
                request, CancellationToken.None);
            Assert.That(world.TryCommit(result, out string rejection), Is.True, rejection);
            return new List<NaturalItemPlacement>(world.Chunks[address].Ecology.Placements);
        }

        private static ChunkGenerationProfileSnapshot CreateCaveProfile(double portalChance,
            double resourceDensity, ChunkGenerationProfileSnapshot pairedSurfaceProfile = null,
            int surfaceSeed = 424242, bool groundwaterEnabled = false,
            bool vineEnabled = false)
        {
            var numbers = new Dictionary<string, double>
            {
                ["terrain.stoneTileId"] = 4d,
                ["terrain.waterTileId"] = 2d,
                ["cave.floorTileId"] = 4d,
                ["cave.wallTileId"] = 7d,
                ["cave.groundwater.enabled"] = groundwaterEnabled ? 1d : 0d,
                ["cave.groundwater.roomChance"] = 1d,
                ["cave.groundwater.minRadiusRatio"] = 0.42d,
                ["cave.groundwater.maxRadiusRatio"] = 0.68d,
                ["cave.groundwater.minDepth"] = 0.25d,
                ["cave.groundwater.maxDepth"] = 0.85d,
                ["cave.vine.enabled"] = vineEnabled ? 1d : 0d,
                ["cave.vine.wallChance"] = 0.065d,
                ["cave.vine.wetMultiplier"] = 2.5d,
                ["cave.vine.dryMultiplier"] = 0.2d,
                ["cave.portal.enabled"] = 1d,
                ["cave.portal.chunkChance"] = portalChance,
                ["cave.portal.safeRadius"] = 3d,
                ["cave.portal.baseSeed"] = 424242d,
                ["cave.portal.seedSalt"] = 7919d,
                ["cave.resource.density"] = resourceDensity,
                ["cave.resource.looseDensity"] = 0d
            };
            var texts = new Dictionary<string, string>
            {
                ["terrain.mode"] = "cave",
                ["cave.portal.itemId"] = "CaveExit",
                ["cave.portal.targetDimensionId"] = "surface"
            };
            var resources = new[]
            {
                new CaveResourceRuleSnapshot("cave.resource.tin", "Mine_Tin", 0.82d,
                    0.032d, 2207),
                new CaveResourceRuleSnapshot("cave.resource.iron", "Mine_Iron", 0.77d,
                    0.036d, 1103),
                new CaveResourceRuleSnapshot("cave.resource.stone", "Mine_Stone", 0d,
                    0.06d, 5501)
            };
            var cave = new ChunkGenerationProfileSnapshot("cave.test",
                DeterministicChunkGenerator.CurrentGenerationSignature, 16, 16, numbers,
                texts, caveResourceRules: resources);
            pairedSurfaceProfile ??= CreateSurfaceProfile(portalChance);
            return cave.WithCavePortalPairing(new CavePortalPairingSnapshot(
                "surface", surfaceSeed, pairedSurfaceProfile));
        }

        private static ChunkGenerationProfileSnapshot CreateSurfaceProfile(double portalChance)
        {
            var numbers = new Dictionary<string, double>
            {
                ["cave.portal.enabled"] = 1d,
                ["cave.portal.chunkChance"] = portalChance,
                ["cave.portal.baseSeed"] = 424242d,
                ["cave.portal.seedSalt"] = 7919d,
                // 让配对回归固定落在可走地表，专注验证跨维度候选选择而非水文覆盖率。
                ["terrain.seaLevel"] = 0d,
                ["river.enabled"] = 0d,
                ["structure.enabled"] = 0d
            };
            var texts = new Dictionary<string, string>
            {
                ["terrain.mode"] = "surface",
                ["cave.portal.itemId"] = "CaveExit",
                ["cave.portal.targetDimensionId"] = "cave"
            };
            return new ChunkGenerationProfileSnapshot("surface.portal.test",
                DeterministicChunkGenerator.CurrentGenerationSignature, 16, 16, numbers, texts);
        }

        private static ChunkGenerationRequest CreateRequest(ChunkGenerationProfileSnapshot profile,
            int seed, Int2 origin, string dimensionId)
        {
            return new ChunkGenerationRequest(1,
                new RuntimeWorldAddress(dimensionId, origin), seed, 1, profile);
        }

        private readonly struct CaveObservation
        {
            public CaveObservation(ulong terrainHash, List<NaturalItemPlacement> placements,
                bool[,] walkable, bool[,] water, int waterCellCount, int deepWaterCellCount)
            {
                TerrainHash = terrainHash;
                Placements = placements;
                this.walkable = walkable;
                this.water = water;
                WaterCellCount = waterCellCount;
                DeepWaterCellCount = deepWaterCellCount;
            }

            private readonly bool[,] walkable;
            private readonly bool[,] water;
            public ulong TerrainHash { get; }
            public List<NaturalItemPlacement> Placements { get; }
            public int WaterCellCount { get; }
            public int DeepWaterCellCount { get; }
            public bool IsWalkable(int x, int y) =>
                walkable != null && (uint)x < (uint)walkable.GetLength(0) &&
                (uint)y < (uint)walkable.GetLength(1) && walkable[x, y];
            public bool IsWater(int x, int y) =>
                water != null && (uint)x < (uint)water.GetLength(0) &&
                (uint)y < (uint)water.GetLength(1) && water[x, y];
        }

        #endregion
    }
}
