using FlatWorld.GameTest.Shared;
using FlatWorld.WorldModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

namespace FlatWorld.GameTest.Map
{
    /// <summary>地图基础冒烟测试：保护 Chunk、Map 与地图资源入口。</summary>
    public sealed class MapSmokeTests
    {
        #region 出生地采样回归

        [Test]
        [Category("Map.Smoke")]
        [Category("Smoke")]
        public void SurfaceSpawnSearchUsesFullConfiguredRadiusWithinSampleBudget()
        {
            ChunkGenerationProfileSO profileAsset =
                AssetDatabase.LoadAssetAtPath<ChunkGenerationProfileSO>(
                    "Assets/Resources/Config/WorldModel/ChunkGenerationProfile_Surface.asset");
            Assert.That(profileAsset, Is.Not.Null);

            ChunkGenerationProfileSnapshot profile = profileAsset.CreateSnapshot()
                .WithNumericParameter("world.coordinateScale", PlanetData.DefaultNoiseScale);
            var topology = new ChunkGenerationTopologySnapshot(
                new Int2(-512, -512),
                new Int2(1024, 1024));
            var generator = new DeterministicChunkGenerator();

            bool found = generator.TryFindWalkableSurfaceNear(
                "surface",
                -329089282,
                profile,
                topology,
                new Int2(368, -429),
                512,
                4096,
                out Int2 spawnCell);

            Assert.That(found, Is.True,
                "出生搜索必须用有限预算覆盖完整配置半径，不能退化为只检查锚点附近约 32 格。");
            Assert.That(spawnCell.X, Is.InRange(topology.Min.X,
                topology.Min.X + topology.Span.X - 1));
            Assert.That(spawnCell.Y, Is.InRange(topology.Min.Y,
                topology.Min.Y + topology.Span.Y - 1));
        }

        [Test]
        [Category("Map.Smoke")]
        [Category("Smoke")]
        public void RuntimeWindowUsesTheSameDerivedSeedAsSpawnSearch()
        {
            const string runtimeWindowPath =
                "Assets/5_Scripts/5-3_GamePlay/Core/Manager/ChunkMgr.RuntimeWindow.cs";
            string source = File.ReadAllText(runtimeWindowPath);
            int methodStart = source.IndexOf(
                "public void RefreshRuntimeWindow",
                System.StringComparison.Ordinal);
            int methodEnd = source.IndexOf(
                "#endregion",
                methodStart,
                System.StringComparison.Ordinal);

            Assert.That(methodStart, Is.GreaterThanOrEqualTo(0));
            Assert.That(methodEnd, Is.GreaterThan(methodStart));
            string methodSource = source.Substring(methodStart, methodEnd - methodStart);
            Assert.That(methodSource,
                Does.Contain("dimensionManager.GetActiveGenerationSeed(baseSeed)"),
                "实际区块必须与出生点纯采样共用维度派生种子，否则安全陆地会在加载后变成水面。");
        }

        #endregion
















        [Test]
        [Category("Map.Smoke")]
        [Category("Smoke")]
        public void DirectChunkLoadReusesAnAlreadyActiveChunk()
        {
            const string chunkManagerPath = "Assets/5_Scripts/5-3_GamePlay/Core/Manager/ChunkMgr.cs";
            string source = File.ReadAllText(chunkManagerPath);
            int methodStart = source.IndexOf("public Chunk LoadChunk_By_Position", System.StringComparison.Ordinal);
            int createStart = source.IndexOf("// === 第二优先级", methodStart, System.StringComparison.Ordinal);

            Assert.That(methodStart, Is.GreaterThanOrEqualTo(0));
            Assert.That(createStart, Is.GreaterThan(methodStart));
            string activeLookupSection = source.Substring(methodStart, createStart - methodStart);
            Assert.That(activeLookupSection, Does.Contain("TryGetActiveChunkByPos(chunkPos, out Chunk activeChunk)"));
            Assert.That(activeLookupSection, Does.Contain("return activeChunk;"));
        }











        private sealed class NoOpBaseTerrainGenerator : ChunkGeneratorBase
        {
            public override GenerationStage Stage => GenerationStage.BaseTerrain;

            public override System.Collections.IEnumerator GenerateAsync(
                MapGenerationContext context,
                int workBatchSize)
            {
                context.Map.Data.EnsureTileStorage(1, 1);
                yield break;
            }
        }

        private sealed class ThrowingHydrologyGenerator : ChunkGeneratorBase
        {
            public override GenerationStage Stage => GenerationStage.Hydrology;

            public override System.Collections.IEnumerator GenerateAsync(
                MapGenerationContext context,
                int workBatchSize)
            {
                yield return null;
                throw new System.InvalidOperationException("Injected hydrology failure");
            }
        }

        private sealed class BatchRenderMapProbe : global::Map
        {
            protected override bool ShouldBakePenaltyAfterTilemapLoad => false;
        }
    }
}
