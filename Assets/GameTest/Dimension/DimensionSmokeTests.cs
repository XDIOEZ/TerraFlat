using System.Linq;
using System.IO;
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
            GameTestAssertions.AssertAssetExists("Assets/2_Prefabs/Building/MineEntrance.prefab");
            GameTestAssertions.AssertAssetExists("Assets/2_Prefabs/Building/Summoners/MineEntrance_Summoner.prefab");
            GameTestAssertions.AssertAssetExists("Assets/2_Prefabs/Dimension/CaveExit.prefab");
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
        public void DimensionTravelKeepsWorldPositionOneToOne()
        {
            Vector3 surfacePosition = new Vector3(-123.25f, 456.75f, 0f);

            Assert.That(
                DimensionManager.GetCorrespondingPosition(surfacePosition),
                Is.EqualTo(surfacePosition));
        }

        [Test]
        [Category("Dimension.Smoke")]
        public void ProbabilisticEntranceLayoutIsDeterministicAndOpenUnderground()
        {
            DimensionDefinition cave = DimensionDefinition.CreateCave();
            cave.CaveEntranceChunkChance = 1f;
            Vector2Int chunkOrigin = new Vector2Int(-32, 48);
            Vector2Int chunkSize = new Vector2Int(16, 16);
            const int caveSeed = 918273;

            Assert.That(DimensionPortalLayout.ShouldGenerateEntrance(
                chunkOrigin,
                caveSeed,
                cave.CaveEntranceChunkChance), Is.True);

            Vector2Int first = DimensionPortalLayout.GetCandidateCell(
                chunkOrigin,
                chunkSize,
                caveSeed,
                0);
            Vector2Int second = DimensionPortalLayout.GetCandidateCell(
                chunkOrigin,
                chunkSize,
                caveSeed,
                0);

            Assert.That(second, Is.EqualTo(first));
            Assert.That(first.x, Is.InRange(chunkOrigin.x, chunkOrigin.x + chunkSize.x - 1));
            Assert.That(first.y, Is.InRange(chunkOrigin.y, chunkOrigin.y + chunkSize.y - 1));
            Assert.That(CaveLayoutSampler.IsOpenAtWorld(first, cave, caveSeed, chunkSize), Is.True);
        }

        [Test]
        [Category("Dimension.Smoke")]
        public void PortalAnchorsPersistOncePerWorldCell()
        {
            Data_Player playerData = new Data_Player();
            Vector3 entrance = new Vector3(18.5f, -7.5f, 0f);

            Assert.That(DimensionTravelProgressStore.AddPortalAnchor(
                playerData,
                "地球",
                entrance), Is.True);
            Assert.That(DimensionTravelProgressStore.AddPortalAnchor(
                playerData,
                "地球",
                entrance), Is.False);

            Assert.That(
                DimensionTravelProgressStore.GetPortalAnchors(playerData, "地球"),
                Is.EqualTo(new[] { entrance }));
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
            Assert.That(surface.PortalOffset, Is.EqualTo(Vector3.zero));
            Assert.That(cave.PortalOffset, Is.EqualTo(Vector3.zero));
            Assert.That(cave.GenerationMode, Is.EqualTo(DimensionGenerationMode.Cave));
            Assert.That(cave.UseFixedLighting, Is.True);
            Assert.That(cave.SuppressWeather, Is.True);
            Assert.That(cave.CaveFloorTileId, Is.EqualTo("TileBase_Stone"));
            Assert.That(cave.CaveWallTileId, Is.EqualTo("TileBase_StoneWall"));
            Assert.That(cave.CaveEntranceChunkChance, Is.GreaterThan(0f));
            Assert.That(cave.CaveEntranceSafeRadius, Is.GreaterThanOrEqualTo(1f));
            Assert.That(cave.CaveResourceDensity, Is.GreaterThan(0.1f));
            Assert.That(cave.CaveLooseOreDensity, Is.GreaterThan(0f));
            Assert.That(cave.CaveLooseOreDensity, Is.LessThan(cave.CaveResourceDensity));
            Assert.That(cave.CaveResources.Select(rule => rule.ItemId), Does.Contain("Mine_Iron"));
        }

        [Test]
        [Category("Dimension.Smoke")]
        public void DefaultCaveResourcesAreOrderedFromRarestToMostCommon()
        {
            DimensionCatalogSO catalog = AssetDatabase.LoadAssetAtPath<DimensionCatalogSO>(
                "Assets/Resources/Config/DimensionCatalog_Default.asset");
            Assert.That(catalog, Is.Not.Null);
            DimensionDefinition cave = catalog.Find(WorldAddress.CaveDimensionId);
            Assert.That(cave, Is.Not.Null);
            string[] rareToCommon =
            {
                "Mine_Tin",
                "Mine_Iron",
                "Mine_Copper",
                "Mine_Coal",
                "Mine_Stone"
            };

            Assert.That(cave.CaveResourceDensity, Is.EqualTo(0.14f).Within(0.0001f));
            Assert.That(cave.CaveLooseOreDensity, Is.EqualTo(0.004f).Within(0.0001f));
            Assert.That(cave.CaveResources.Select(rule => rule.ItemId), Is.EqualTo(rareToCommon));
            for (int i = 0; i < cave.CaveResources.Count - 1; i++)
            {
                Assert.That(cave.CaveResources[i].VeinThreshold,
                    Is.GreaterThan(cave.CaveResources[i + 1].VeinThreshold));
            }
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
        public void CaveBurstGenerationMatchesScalarClassificationAndUsesTwoLayerWalls()
        {
            const string floorPath = "Assets/4_ScriptObjects/4-1_TileBlock/TileBase_Stone.asset";
            const string wallPath = "Assets/4_ScriptObjects/4-1_TileBlock/TileBase_StoneWall.asset";
            Tile_Block floorBlock = AssetDatabase.LoadAssetAtPath<Tile_Block>(floorPath);
            Tile_Block wallBlock = AssetDatabase.LoadAssetAtPath<Tile_Block>(wallPath);
            Assert.That(floorBlock, Is.Not.Null);
            Assert.That(wallBlock, Is.Not.Null);

            DimensionDefinition definition = DimensionDefinition.CreateCave();
            GameRes resources = GameRes.Instance;
            bool hadFloor = resources.TileBlockDict.TryGetValue(definition.CaveFloorTileId, out Tile_Block oldFloor);
            bool hadWall = resources.TileBlockDict.TryGetValue(definition.CaveWallTileId, out Tile_Block oldWall);
            GameObject mapObject = new GameObject("CaveJobConsistencyMap");
            try
            {
                resources.TileBlockDict[definition.CaveFloorTileId] = floorBlock;
                resources.TileBlockDict[definition.CaveWallTileId] = wallBlock;
                global::Map map = mapObject.AddComponent<global::Map>();
                map.Data = new Data_TileMap { position = Vector2Int.zero };
                var generator = new ChunkGenerator_Cave();
                const int worldSeed = 918273;
                var context = new MapGenerationContext(
                    map,
                    new PlanetData { NoiseScale = PlanetData.DefaultNoiseScale },
                    worldSeed,
                    new WorldAddress("cave_job", WorldAddress.CaveDimensionId),
                    definition);

                generator.Generate(context);

                Vector2Int size = new Vector2Int(map.Data.Width, map.Data.Height);
                int openCount = 0;
                int closedCount = 0;
                Vector2Int firstClosed = default;
                for (int y = 0; y < size.y; y++)
                {
                    for (int x = 0; x < size.x; x++)
                    {
                        Vector2Int world = map.Data.position + new Vector2Int(x, y);
                        byte classification = ChunkGenerator_Cave.SampleCellClassification(
                            world,
                            definition,
                            worldSeed,
                            size);
                        Assert.That(map.Data.GetTileAt(world, 0).ID, Is.EqualTo(definition.CaveFloorTileId));
                        if (classification == ChunkGenerator_Cave.CaveCellClassification.Closed)
                        {
                            if (closedCount == 0)
                                firstClosed = world;
                            closedCount++;
                            Assert.That(map.Data.GetLayerCount(world), Is.EqualTo(2));
                            Assert.That(map.Data.GetTopTile(world).ID, Is.EqualTo(definition.CaveWallTileId));
                            Assert.That(BlockingTilemapLayer.IsBlockingTile(map.Data.GetTopTile(world)), Is.True);
                        }
                        else
                        {
                            openCount++;
                            Assert.That(map.Data.GetLayerCount(world), Is.EqualTo(1));
                        }
                    }
                }

                Assert.That(openCount, Is.GreaterThan(0));
                Assert.That(closedCount, Is.GreaterThan(0));
                Assert.That(map.Data.CountOverflowAllocations(), Is.Zero);
                Assert.That(map.Data.EnvironmentLayers.IsValidSize(size.x, size.y), Is.True);

                var container = new Ex_ModData_MemoryPackable();
                container.WriteData<ItemData>(map.Data);
                Data_TileMap restored = container.GetData<ItemData>() as Data_TileMap;
                Assert.That(restored, Is.Not.Null);
                Assert.That(restored.GetLayerCount(firstClosed), Is.EqualTo(2));
                Assert.That(restored.GetTopTile(firstClosed).ID, Is.EqualTo(definition.CaveWallTileId));
            }
            finally
            {
                if (hadFloor)
                    resources.TileBlockDict[definition.CaveFloorTileId] = oldFloor;
                else
                    resources.TileBlockDict.Remove(definition.CaveFloorTileId);
                if (hadWall)
                    resources.TileBlockDict[definition.CaveWallTileId] = oldWall;
                else
                    resources.TileBlockDict.Remove(definition.CaveWallTileId);
                Object.DestroyImmediate(mapObject);
            }
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

        [Test]
        [Category("Dimension.Smoke")]
        public void MineEntranceAndCaveExitUseFormalPortalContracts()
        {
            GameObject placedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/Building/MineEntrance.prefab");
            GameObject summonerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/Building/Summoners/MineEntrance_Summoner.prefab");
            GameObject caveExitPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/Dimension/CaveExit.prefab");

            Assert.That(placedPrefab.GetComponent<Item>().itemData.IDName, Is.EqualTo("MineEntrance"));
            Assert.That(placedPrefab.GetComponent<DimensionPortal>().TargetDimensionId, Is.EqualTo(WorldAddress.CaveDimensionId));
            Assert.That(placedPrefab.GetComponent<DimensionPortal>().RequiresInstalledBuilding, Is.True);
            Assert.That(placedPrefab.GetComponentInChildren<Mod_Building>(true).Data.Role, Is.EqualTo(BuildingRole.PlacedBuilding));
            Assert.That(placedPrefab.GetComponents<BoxCollider2D>().Any(collider => collider.isTrigger), Is.True);

            Assert.That(summonerPrefab.GetComponent<Item>().itemData.IDName, Is.EqualTo("MineEntrance_Summoner"));
            Assert.That(summonerPrefab.GetComponent<Item>().itemData.Stack.CanBePickedUp, Is.True);
            Assert.That(summonerPrefab.GetComponentInChildren<Mod_Building>(true).Data.Role, Is.EqualTo(BuildingRole.Summoner));

            Assert.That(caveExitPrefab.GetComponent<Item>().itemData.IDName, Is.EqualTo("CaveExit"));
            Assert.That(caveExitPrefab.GetComponent<Item>().itemData.Stack.CanBePickedUp, Is.False);
            Assert.That(caveExitPrefab.GetComponent<DimensionPortal>().TargetDimensionId, Is.EqualTo(WorldAddress.SurfaceDimensionId));
            Assert.That(caveExitPrefab.GetComponent<DimensionPortal>().RequiresInstalledBuilding, Is.False);
            Assert.That(caveExitPrefab.GetComponent<BoxCollider2D>().isTrigger, Is.True);

            RecipeCatalogDto recipes = RecipeRuntimeFactory.Deserialize(File.ReadAllText(
                "Assets/StreamingAssets/GameConfig/Recipes/crafting/buildings.json"));
            RecipeDto mineEntranceRecipe = recipes.Recipes.Single(recipe => recipe.Id == "core:矿坑入口");
            Assert.That(mineEntranceRecipe.Outputs.Single().ItemId, Is.EqualTo("MineEntrance_Summoner"));
        }

        [Test]
        [Category("Dimension.Smoke")]
        public void CaveResourcesAreUprightSingleCellSolidObstacles()
        {
            Assert.That(ChunkGenerator_Cave.GeneratedResourceRotation, Is.EqualTo(Quaternion.identity));
            Assert.That(ChunkGenerator_Cave.GeneratedResourceUniformScale,
                Is.EqualTo(1f).Within(0.0001f));

            string[] mineIds = { "Mine_Coal", "Mine_Copper", "Mine_Tin", "Mine_Iron", "Mine_Stone" };
            int colliderLayer = LayerMask.NameToLayer("Collider");
            for (int i = 0; i < mineIds.Length; i++)
            {
                string path = $"Assets/2_Prefabs/Mine/{mineIds[i]}.prefab";
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Collider2D collider = prefab.GetComponent<Collider2D>();
                BoxCollider2D boxCollider = collider as BoxCollider2D;
                SpriteRenderer spriteRenderer = prefab.GetComponentInChildren<SpriteRenderer>(true);

                Assert.That(collider, Is.Not.Null, $"{mineIds[i]} 缺少实体碰撞体。");
                Assert.That(collider.isTrigger, Is.False, $"{mineIds[i]} 的碰撞体不能是触发器。");
                Assert.That(prefab.layer, Is.EqualTo(colliderLayer), $"{mineIds[i]} 未使用 Collider 层。");
                Assert.That(boxCollider, Is.Not.Null, $"{mineIds[i]} must use a bounded box collider.");
                Assert.That(spriteRenderer?.sprite, Is.Not.Null, $"{mineIds[i]} is missing its mine sprite.");

                Vector2 generatedColliderSize = Vector2.Scale(
                    boxCollider.size,
                    new Vector2(
                        Mathf.Abs(boxCollider.transform.localScale.x) * ChunkGenerator_Cave.GeneratedResourceUniformScale,
                        Mathf.Abs(boxCollider.transform.localScale.y) * ChunkGenerator_Cave.GeneratedResourceUniformScale));
                Vector2 generatedVisualSize = Vector2.Scale(
                    spriteRenderer.sprite.bounds.size,
                    new Vector2(
                        Mathf.Abs(spriteRenderer.transform.localScale.x) * ChunkGenerator_Cave.GeneratedResourceUniformScale,
                        Mathf.Abs(spriteRenderer.transform.localScale.y) * ChunkGenerator_Cave.GeneratedResourceUniformScale));

                Assert.That(generatedColliderSize.x, Is.LessThanOrEqualTo(1.001f),
                    $"{mineIds[i]} collider spans more than one navigation cell horizontally.");
                Assert.That(generatedColliderSize.y, Is.LessThanOrEqualTo(1.001f),
                    $"{mineIds[i]} collider spans more than one navigation cell vertically.");
                Assert.That(generatedVisualSize.x, Is.LessThanOrEqualTo(1.001f),
                    $"{mineIds[i]} sprite spans more than one cell horizontally.");
                Assert.That(generatedVisualSize.y, Is.LessThanOrEqualTo(1.001f),
                    $"{mineIds[i]} sprite spans more than one cell vertically.");
        }
        }

        [Test]
        [Category("Dimension.Smoke")]
        public void PortalAnchorRoundTripKeepsEntranceAndExitIdentity()
        {
            Data_Player playerData = new Data_Player();
            GameObject entranceObject = new GameObject("MineEntrance_Test");
            GameObject exitObject = new GameObject("CaveExit_Test");
            try
            {
                GameItem entrance = entranceObject.AddComponent<GameItem>();
                entrance.Data = CreatePortalItemData("MineEntrance", 112233);
                entrance.transform.position = new Vector3(18.5f, -7.5f, 0f);

                WorldAddress surface = new WorldAddress("TestPlanet", WorldAddress.SurfaceDimensionId);
                WorldAddress cave = surface.WithDimension(WorldAddress.CaveDimensionId);
                DimensionPortalAnchor anchor = DimensionTravelProgressStore.GetOrCreateCaveAnchor(
                    playerData,
                    surface,
                    entrance,
                    cave,
                    DimensionDefinition.CreateCave());

                GameItem caveExit = exitObject.AddComponent<GameItem>();
                caveExit.Data = CreatePortalItemData("CaveExit", anchor.CaveExitGuid);
                caveExit.transform.position = anchor.CaveExitPosition;
                DimensionTravelProgressStore.UpdateCaveExit(playerData, anchor, caveExit);

                Assert.That(DimensionTravelProgressStore.TryGetAnchorByCaveExit(
                    playerData,
                    cave,
                    caveExit,
                    out DimensionPortalAnchor restored), Is.True);
                Assert.That(restored.SurfaceEntranceGuid, Is.EqualTo(entrance.itemData.Guid));
                Assert.That(restored.SurfaceEntrancePosition, Is.EqualTo(entrance.transform.position));
                Assert.That(restored.CaveExitGuid, Is.EqualTo(caveExit.itemData.Guid));
                Assert.That(restored.CaveExitPosition, Is.EqualTo(caveExit.transform.position));
            }
            finally
            {
                Object.DestroyImmediate(entranceObject);
                Object.DestroyImmediate(exitObject);
            }
        }

        private static Data_GeneralItem CreatePortalItemData(string itemId, int guid)
        {
            return new Data_GeneralItem
            {
                IDName = itemId,
                Guid = guid,
                Stack = new ItemStack
                {
                    Amount = 1f,
                    Volume = 1f,
                    CanBePickedUp = false
                },
                transform = new ItemTransform()
            };
        }

        [Test]
        [Category("Dimension.Smoke")]
        public void RestoredCaveMineScaleIsNormalizedToOneCell()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/Mine/Mine_Stone.prefab");
            Assert.That(prefab, Is.Not.Null);

            GameObject owner = Object.Instantiate(prefab);
            try
            {
                GameItem mine = owner.GetComponent<GameItem>();
                Assert.That(mine?.itemData, Is.Not.Null);
                mine.transform.localScale = new Vector3(2.5f, 2.5f, 1f);
                mine.itemData.transform.scale = mine.transform.localScale;

                bool normalized = ChunkGenerator_Cave.ApplyGeneratedResourceTransform(
                    DimensionDefinition.CreateCave(),
                    mine);

                Assert.That(normalized, Is.True);
                Assert.That(mine.transform.localScale, Is.EqualTo(ChunkGenerator_Cave.GeneratedResourceScale));
                Assert.That(mine.itemData.transform.scale, Is.EqualTo(ChunkGenerator_Cave.GeneratedResourceScale));
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        [Category("Dimension.Smoke")]
        public void CaveMineResourcesHaveDirectPickupCounterparts()
        {
            DimensionDefinition cave = DimensionDefinition.CreateCave();
            for (int i = 0; i < cave.CaveResources.Count; i++)
            {
                string mineId = cave.CaveResources[i].ItemId;
                string pickupId = ChunkGenerator_Cave.GetLooseOreItemId(mineId);
                string path = $"Assets/2_Prefabs/Mineral/Ore/{pickupId}.prefab";
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Item pickup = prefab != null ? prefab.GetComponent<Item>() : null;
                Collider2D collider = prefab != null ? prefab.GetComponent<Collider2D>() : null;

                Assert.That(prefab, Is.Not.Null, $"{mineId} 缺少对应的可拾取矿石 {pickupId}。");
                Assert.That(pickup?.itemData?.Stack.CanBePickedUp, Is.True, $"{pickupId} 不能直接拾取。");
                Assert.That(collider?.isTrigger, Is.True, $"{pickupId} 应使用拾取触发器。");
        }
        }
    }
}
