using FlatWorld.GameTest.Shared;
using FlatWorld.WorldModel;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FlatWorld.GameTest.Building
{
    /// <summary>建筑基础冒烟测试：保护放置、占地和建筑资源入口。</summary>
    public sealed class BuildingSmokeTests
    {
        [Test]
        [Category("Building.Smoke")]
        [Category("Smoke")]
        public void RequiredEntryPointsAndAssetsExist()
        {
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/World/Building/Mod_Building.cs", "Mod_Building");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/World/Map/SO/Tile_Block.cs", "Tile_Block");
            GameTestAssertions.AssertScriptType(
                "Assets/5_Scripts/5-3_GamePlay/World/WorldModel/Presentation/ChunkTilePaletteSO.cs",
                "ChunkTilePaletteSO");
            GameTestAssertions.AssertScriptType(
                "Assets/5_Scripts/5-3_GamePlay/World/WorldModel/Configuration/ChunkGenerationProfileSO.cs",
                "ChunkGenerationProfileSO");
            GameTestAssertions.AssertScriptType(
                "Assets/5_Scripts/5-3_GamePlay/World/Map/Structures/StructureCatalogSO.cs",
                "StructureCatalogSO");
            GameTestAssertions.AssertScriptType(
                "Assets/5_Scripts/5-3_GamePlay/World/Map/Structures/StructureDefinitionSO.cs",
                "StructureDefinitionSO");
            GameTestAssertions.AssertScriptType(
                "Assets/5_Scripts/5-3_GamePlay/World/Map/Structures/StructureTemplateSO.cs",
                "StructureTemplateSO");
            GameTestAssertions.AssertScriptType(
                "Assets/5_Scripts/5-3_GamePlay/World/Map/SO/BiomeData.cs",
                "BiomeData");
            Assert.That(typeof(BuildingOccupancyRegistry).IsAbstract, Is.True,
                "BuildingOccupancyRegistry 必须保持静态权威注册表");
            Assert.That(typeof(BuildingOccupancyRegistry).IsSealed, Is.True,
                "BuildingOccupancyRegistry 必须保持静态权威注册表");
            GameTestAssertions.AssertAssetExists("Assets/2_Prefabs/Building/BuildingShadow.prefab");
            GameTestAssertions.AssertAssetExists("Assets/2_Prefabs/Building/MineEntrance.prefab");
            GameTestAssertions.AssertAssetExists("Assets/2_Prefabs/Building/Summoners/MineEntrance_Summoner.prefab");
            GameTestAssertions.AssertAssetExists("Assets/2_Prefabs/Building/Door_Stone.prefab");
            GameTestAssertions.AssertAssetExists("Assets/2_Prefabs/Building/Wall_Stone.prefab");
            GameTestAssertions.AssertAssetExists("Assets/2_Prefabs/Building/Summoners/Wall_Stone_Summoner.prefab");
            GameTestAssertions.AssertAssetExists("Assets/2_Prefabs/TileBlock/TileItem_StoneWall.prefab");
            GameTestAssertions.AssertAssetExists("Assets/4_ScriptObjects/4-1_TileBlock/TileBase_BuiltStoneWall.asset");
            GameTestAssertions.AssertAssetExists("Assets/Resources/Config/WorldModel/ChunkTilePalette_Default.asset");
            GameTestAssertions.AssertAssetExists("Assets/Resources/Config/WorldModel/ChunkGenerationProfile_Surface.asset");
            GameTestAssertions.AssertAssetExists("Assets/Resources/Config/StructureCatalog_Default.asset");

            GameTestAssertions.AssertScriptType(
                "Assets/5_Scripts/5-3_GamePlay/Player/Controller/Mod_InteractSender.cs",
                "Mod_InteractSender");

            GameObject stoneDoorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/Building/Door_Stone.prefab");
            Mod_Door stoneDoor = stoneDoorPrefab.GetComponentInChildren<Mod_Door>(true);
            Assert.That(stoneDoor, Is.Not.Null, "石门预制体缺少 Mod_Door");
            Assert.That(stoneDoor.DoorCollider, Is.Not.Null, "石门缺少阻挡碰撞体");
            Assert.That(stoneDoor.DoorRenderer, Is.Not.Null, "石门缺少门贴图引用");

            GameObject shadowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/Building/BuildingShadow.prefab");
            BuildingShadow shadow = shadowPrefab.GetComponentInChildren<BuildingShadow>(true);
            Assert.That(shadow, Is.Not.Null, "建筑虚影预制体缺少 BuildingShadow");
            Assert.That(shadow.ShadowRenderer, Is.Not.Null, "建筑虚影缺少 SpriteRenderer 引用");
            Assert.That(shadow.ShadowRenderer.sharedMaterial, Is.Not.Null, "建筑虚影材质引用已丢失");
            Assert.That(SortingLayer.NameToID("Shadow"), Is.Not.Zero, "项目缺少建筑虚影排序层 Shadow");
            Assert.That(shadow.ShadowRenderer.sortingLayerID, Is.EqualTo(SortingLayer.NameToID("Shadow")),
                "建筑虚影 Prefab 必须默认使用 Shadow 排序层");
            Assert.That(shadow.ShadowRenderer.sortingOrder, Is.GreaterThan(0),
                "建筑虚影必须使用正排序序号，避免被同层地表精灵覆盖");

            GameObject legacyStoneWallPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/TileBlock/TileItem_StoneWall.prefab");
            Assert.That(legacyStoneWallPrefab.GetComponent<Item_Tile_Grass>(), Is.Not.Null,
                "旧石墙物品必须保留兼容脚本");
            Assert.That(Item_Tile_Grass.TryResolveRuntimeTileBlockId(
                "TileItem_StoneWall", out string runtimeTileBlockId), Is.True,
                "旧石墙物品必须映射到新区块建筑地块");
            Assert.That(runtimeTileBlockId, Is.EqualTo(Item_Tile_Grass.RuntimeStoneWallTileBlockId));

            SortingLayer[] sortingLayers = SortingLayer.layers;
            int defaultIndex = System.Array.FindIndex(sortingLayers,
                layer => layer.name == "Default");
            int shadowIndex = System.Array.FindIndex(sortingLayers,
                layer => layer.name == "Shadow");
            Assert.That(shadowIndex, Is.GreaterThan(defaultIndex),
                "Shadow 排序层必须位于 Default 之后才能显示在普通世界精灵之上");
        }

        [Test]
        [Category("Building.Smoke")]
        [Category("Smoke")]
        public void ShadowKeepsFallbackMaterialWhenLegacyBuildingMaterialIsMissing()
        {
            GameObject shadowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/Building/BuildingShadow.prefab");
            GameObject buildingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/Building/Wall_Wood.prefab");
            GameObject shadowObject = Object.Instantiate(shadowPrefab);
            GameObject buildingObject = Object.Instantiate(buildingPrefab);
            try
            {
                BuildingShadow shadow = shadowObject.GetComponentInChildren<BuildingShadow>(true);
                SpriteRenderer source = buildingObject.GetComponentInChildren<SpriteRenderer>(true);
                Assert.That(shadow, Is.Not.Null);
                Assert.That(source, Is.Not.Null);

                shadow.InitShadow(source, buildingObject.transform,
                    new Bounds(Vector3.zero, Vector3.one));

                Assert.That(shadow.ShadowRenderer.enabled, Is.True);
                Assert.That(shadow.ShadowRenderer.sprite, Is.EqualTo(source.sprite));
                Assert.That(shadow.ShadowRenderer.sharedMaterial, Is.Not.Null,
                    "建筑材质引用丢失时，虚影必须保留 Prefab 默认 Sprite 材质。");
            }
            finally
            {
                Object.DestroyImmediate(shadowObject);
                Object.DestroyImmediate(buildingObject);
            }
        }

        [Test]
        [Category("Building.Smoke")]
        [Category("Smoke")]
        public void RuntimeTerrainBlockingTileCanBePlacedAndRemoved()
        {
            using ChunkTerrainBuffer buffer = new ChunkTerrainBuffer(2, 2);
            buffer.SetCell(0, 0, new TerrainCell(1, 0, 0, 0, 1, TerrainCellFlags.Walkable));
            using ChunkTerrainData terrain = buffer.Seal();

            Assert.That(terrain.TrySetBlockingTile(0, 0, 8), Is.True);
            TerrainCell blocked = terrain.GetCell(0, 0);
            Assert.That(blocked.BlockingTileId, Is.EqualTo(8));
            Assert.That(blocked.Flags & TerrainCellFlags.Blocking, Is.EqualTo(TerrainCellFlags.Blocking));
            Assert.That(terrain.IsWalkable(0, 0), Is.False);

            Assert.That(terrain.TryRemoveBlockingTile(0, 0, 8), Is.True);
            TerrainCell restored = terrain.GetCell(0, 0);
            Assert.That(restored.BlockingTileId, Is.Zero);
            Assert.That(terrain.IsWalkable(0, 0), Is.True);
        }
    }
}
