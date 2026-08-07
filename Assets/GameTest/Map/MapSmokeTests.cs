using FlatWorld.GameTest.Shared;
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
