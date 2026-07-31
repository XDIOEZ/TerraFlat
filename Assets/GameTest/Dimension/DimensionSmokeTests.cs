using System.Linq;
using FlatWorld.GameTest.Shared;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FlatWorld.GameTest.Dimension
{
    public sealed class DimensionSmokeTests
    {
        [Test]
        [Category("Dimension.Smoke")]
        public void RequiredDimensionEntriesExist()
        {
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Dimension/DimensionManager.cs", "DimensionManager");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Dimension/ChunkGenerator_Cave.cs", "ChunkGenerator_Cave");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Dimension/CaveLayoutSampler.cs", "CaveLayoutSampler");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Dimension/DimensionPortal.cs", "DimensionPortal");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Map/BlockingTilemapLayer.cs", "BlockingTilemapLayer");
            GameTestAssertions.AssertAssetExists("Assets/Resources/Config/DimensionCatalog_Default.asset");
            GameTestAssertions.AssertAssetExists("Assets/4_ScriptObjects/4-1_TileBlock/TileBase_Stone.asset");
            GameTestAssertions.AssertAssetExists("Assets/4_ScriptObjects/4-1_TileBlock/TileBase_StoneWall.asset");
        }

        [Test]
        [Category("Dimension.Smoke")]
        public void WorldAddressKeepsSurfaceCompatibility()
        {
            WorldAddress surface = WorldAddress.FromWorldKey("地球");
            WorldAddress cave = surface.WithDimension(WorldAddress.CaveDimensionId);

            Assert.That(surface.WorldKey, Is.EqualTo("地球"));
            Assert.That(cave.WorldKey, Is.EqualTo("地球__dimension__cave"));
            Assert.That(WorldAddress.FromWorldKey(cave.WorldKey), Is.EqualTo(cave));
        }

        [Test]
        [Category("Dimension.Smoke")]
        public void DefaultCatalogContainsMineDimension()
        {
            DimensionCatalogSO catalog = AssetDatabase.LoadAssetAtPath<DimensionCatalogSO>(
                "Assets/Resources/Config/DimensionCatalog_Default.asset");
            Assert.That(catalog, Is.Not.Null);

            DimensionDefinition surface = catalog.Find(WorldAddress.SurfaceDimensionId);
            DimensionDefinition cave = catalog.Find(WorldAddress.CaveDimensionId);
            Assert.That(surface, Is.Not.Null);
            Assert.That(cave, Is.Not.Null);
            Assert.That(surface.PortalTargetDimensionId, Is.EqualTo(WorldAddress.CaveDimensionId));
            Assert.That(cave.PortalTargetDimensionId, Is.EqualTo(WorldAddress.SurfaceDimensionId));
            Assert.That(cave.GenerationMode, Is.EqualTo(DimensionGenerationMode.Cave));
            Assert.That(cave.UseFixedLighting, Is.True);
            Assert.That(cave.SuppressWeather, Is.True);
            Assert.That(cave.CaveFloorTileId, Is.EqualTo("TileBase_Stone"));
            Assert.That(cave.CaveWallTileId, Is.EqualTo("TileBase_StoneWall"));
            Assert.That(cave.CaveResourceDensity, Is.GreaterThan(0.1f));
            Assert.That(cave.CaveResources.Select(rule => rule.ItemId), Does.Contain("Mine_Iron"));
        }

        [Test]
        [Category("Dimension.Smoke")]
        public void CaveLayoutIsDeterministicAndContainsTunnels()
        {
            DimensionDefinition cave = DimensionDefinition.CreateCave();
            const int worldSeed = 24681357;
            int openCount = 0;
            int wallCount = 0;
            int edgeCount = 0;

            for (int x = -32; x < 32; x++)
            {
                for (int y = -32; y < 32; y++)
                {
                    Vector2Int cell = new Vector2Int(x, y);
                    bool first = CaveLayoutSampler.IsOpenAtWorld(cell, cave, worldSeed);
                    bool second = CaveLayoutSampler.IsOpenAtWorld(cell, cave, worldSeed);
                    Assert.That(second, Is.EqualTo(first));

                    if (first)
                    {
                        openCount++;
                        if (CaveLayoutSampler.IsWallEdge(cell, cave, worldSeed))
                            edgeCount++;
                    }
                    else
                    {
                        wallCount++;
                    }
                }
            }

            Assert.That(CaveLayoutSampler.IsOpenAtWorld(Vector2Int.zero, cave, worldSeed), Is.True);
            Assert.That(openCount, Is.GreaterThan(400));
            Assert.That(wallCount, Is.GreaterThan(400));
            Assert.That(edgeCount, Is.GreaterThan(80));
        }

        [Test]
        [Category("Dimension.Smoke")]
        public void CaveTileBlocksSeparateFloorAndWallNavigation()
        {
            Tile_Block floor = AssetDatabase.LoadAssetAtPath<Tile_Block>(
                "Assets/4_ScriptObjects/4-1_TileBlock/TileBase_Stone.asset");
            Tile_Block wall = AssetDatabase.LoadAssetAtPath<Tile_Block>(
                "Assets/4_ScriptObjects/4-1_TileBlock/TileBase_StoneWall.asset");

            Assert.That(floor, Is.Not.Null);
            Assert.That(wall, Is.Not.Null);
            Assert.That(floor.tileDataTemplate.IsWalkable, Is.True);
            Assert.That(floor.tileDataTemplate.Penalty, Is.GreaterThan(0u));
            Assert.That(wall.tileDataTemplate.IsWalkable, Is.False);
            Assert.That(wall.tileDataTemplate.Penalty, Is.EqualTo(0u));
            Assert.That(BlockingTilemapLayer.IsBlockingTile(wall.tileDataTemplate), Is.True);
            Assert.That(BlockingTilemapLayer.ResolveGroundTile(new[]
            {
                floor.tileDataTemplate,
                wall.tileDataTemplate
            }), Is.SameAs(floor.tileDataTemplate));
            Assert.That(wall.GetTileBaseAsset(), Is.Not.Null);
        }

        [Test]
        [Category("Dimension.Smoke")]
        public void MinePrefabsDropOnlyMineralItems()
        {
            string[] mineIds = { "Mine_Coal", "Mine_Copper", "Mine_Tin", "Mine_Iron", "Mine_Stone" };
            for (int i = 0; i < mineIds.Length; i++)
            {
                string path = $"Assets/2_Prefabs/Mine/{mineIds[i]}.prefab";
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                DamageReceiver receiver = prefab.GetComponentInChildren<DamageReceiver>(true);

                Assert.That(receiver, Is.Not.Null, $"{mineIds[i]} 缺少 DamageReceiver。");
                Assert.That(receiver.Data.LootTable, Is.Not.Empty, $"{mineIds[i]} 缺少矿石掉落。");
                Assert.That(receiver.Data.LootTable.All(entry =>
                    entry != null && entry.LootPrefabName.StartsWith("Ore_")), Is.True,
                    $"{mineIds[i]} 包含非矿石掉落。");
            }
        }
    }
}
