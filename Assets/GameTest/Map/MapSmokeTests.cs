using FlatWorld.GameTest.Shared;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FlatWorld.GameTest.Map
{
    /// <summary>地图基础冒烟测试：保护 Chunk、Map 与地图资源入口。</summary>
    public sealed class MapSmokeTests
    {
        [Test]
        [Category("Map.Smoke")]
        public void RequiredEntryPointsAndAssetsExist()
        {
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Manager/ChunkMgr.cs", "ChunkMgr");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Chunk/Chunk.cs", "Chunk");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Map/Base/Map.cs", "Map");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Map/BlockingTilemapLayer.cs", "BlockingTilemapLayer");
            GameTestAssertions.AssertFolderContainsAsset("Assets/2_Prefabs/Map", "t:Prefab");
            GameTestAssertions.AssertAssetExists("Assets/Resources/Config/StructureCatalog_Default.asset");
        }

        [Test]
        [Category("Map.Smoke")]
        public void MapCoreHasExplicitTerrainNoiseChannels()
        {
            const string prefabPath = "Assets/2_Prefabs/Map/MapCore.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, $"缺少地图核心 Prefab：{prefabPath}");

            global::Map map = prefab.GetComponent<global::Map>();
            Assert.That(map, Is.Not.Null, "MapCore 缺少 Map 组件。");

            ChunkGenerator_Land land = map.mapGenerators.OfType<ChunkGenerator_Land>().SingleOrDefault();
            Assert.That(land, Is.Not.Null, "MapCore 缺少大陆生成器。");
            Assert.That(land.NoiseConfigs, Is.Not.Null.And.Not.Empty, "大陆生成器缺少噪声配置。");
            Assert.That(land.NoiseConfigs, Has.None.Null, "大陆生成器存在空噪声配置项。");
            Assert.That(land.NoiseConfigs.Any(noise => noise.noiseType == NoiseType.Land), Is.True, "缺少高度噪声通道。");
            Assert.That(land.NoiseConfigs.Any(noise => noise.noiseType == NoiseType.Temperature), Is.True, "缺少温度噪声通道。");
            Assert.That(land.NoiseConfigs.Any(noise => noise.noiseType == NoiseType.Precipitation), Is.True, "缺少降水噪声通道。");
        }

        [Test]
        [Category("Map.Smoke")]
        public void PerlinNoiseInvalidParametersStillReturnFiniteValue()
        {
            var noise = new PerlinNoise
            {
                octaves = 0,
                lacunarity = float.NaN,
                persistence = float.NegativeInfinity,
                coordScale = float.NaN,
                frequency = float.PositiveInfinity
            };

            float sample = noise.Sample(128f, -64f, 12345);

            Assert.That(float.IsNaN(sample) || float.IsInfinity(sample), Is.False);
            Assert.That(sample, Is.InRange(0f, 1f));
        }

        [Test]
        [Category("Map.Smoke")]
        public void PlanetNoiseScaleUsesSharedValidationRules()
        {
            Assert.That(PlanetData.IsValidNoiseScale(PlanetData.DefaultNoiseScale), Is.True);
            Assert.That(PlanetData.IsValidNoiseScale(float.NaN), Is.False);
            Assert.That(PlanetData.IsValidNoiseScale(PlanetData.MaxNoiseScale + 1f), Is.False);
            Assert.That(PlanetData.NormalizeNoiseScale(float.PositiveInfinity), Is.EqualTo(PlanetData.DefaultNoiseScale));
        }

        [Test]
        [Category("Map.Smoke")]
        public void MapDoesNotBecomeReadyBeforeTilemapVisualCompletes()
        {
            GameObject mapObject = new GameObject("MapVisualReadyTest");
            try
            {
                global::Map map = mapObject.AddComponent<global::Map>();
                map.Data = new Data_TileMap { TileLoaded = true };

                Assert.That(map.IsTilemapVisualReady, Is.False);
                Assert.That(map.IsReadyForChunkLifecycle, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(mapObject);
            }
        }

        [Test]
        [Category("Map.Smoke")]
        public void BlockingLayerKeepsUnderlyingGroundVisual()
        {
            TileData floor = new TileData_Universal
            {
                ID = "Floor",
                IsWalkable = true,
                Penalty = 1000
            };
            TileData wall = new TileData_Universal
            {
                ID = "Wall",
                TileTag = BlockingTilemapLayer.BlockingTileTag,
                IsWalkable = false,
                Penalty = 0
            };

            Assert.That(BlockingTilemapLayer.IsBlockingTile(wall), Is.True);
            Assert.That(BlockingTilemapLayer.ResolveGroundTile(new[] { floor, wall }), Is.SameAs(floor));
        }

        [Test]
        [Category("Map.Smoke")]
        public void StructureContainerContentsCloneIsIndependent()
        {
            StructureContainerContents source = new StructureContainerContents
            {
                OverrideContents = true,
                TargetInventoryIndex = 0,
                TargetInventoryName = "背包模块",
                Items = new List<StructureContainerItemEntry>
                {
                    new StructureContainerItemEntry
                    {
                        SlotIndex = 1,
                        ItemPrefabId = "Dagger_Bone",
                        Amount = 1
                    }
                }
            };

            StructureContainerContents clone = source.Clone();
            clone.Items[0].ItemPrefabId = "Bonfire";

            Assert.AreEqual("Dagger_Bone", source.Items[0].ItemPrefabId);
            Assert.AreEqual("Bonfire", clone.Items[0].ItemPrefabId);
        }

        [Test]
        [Category("Map.Smoke")]
        public void StructureContainerContentsAffectCatalogHash()
        {
            StructureCatalogSO catalog = ScriptableObject.CreateInstance<StructureCatalogSO>();
            StructureDefinitionSO definition = ScriptableObject.CreateInstance<StructureDefinitionSO>();
            StructureTemplateSO template = ScriptableObject.CreateInstance<StructureTemplateSO>();
            try
            {
                definition.StructureId = "container_hash_test";
                template.TemplateId = "container_hash_template";
                template.ItemStamps.Add(new StructureItemStamp
                {
                    ItemPrefabId = "Chest_Wood",
                    MemberId = "chest_wood_1",
                    ContainerContents = new StructureContainerContents
                    {
                        OverrideContents = true,
                        TargetInventoryIndex = 0,
                        TargetInventoryName = "背包模块"
                    }
                });
                definition.Templates.Add(new WeightedStructureTemplate
                {
                    Template = template,
                    Weight = 1f
                });
                catalog.Definitions.Add(definition);

                uint emptyHash = catalog.CalculateContentHash();
                template.ItemStamps[0].ContainerContents.Items.Add(
                    new StructureContainerItemEntry
                    {
                        SlotIndex = 0,
                        ItemPrefabId = "Dagger_Bone",
                        Amount = 1
                    });
                uint configuredHash = catalog.CalculateContentHash();

                Assert.AreNotEqual(emptyHash, configuredHash);
            }
            finally
            {
                Object.DestroyImmediate(template);
                Object.DestroyImmediate(definition);
                Object.DestroyImmediate(catalog);
            }
        }
    }
}
