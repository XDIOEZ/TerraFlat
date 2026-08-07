using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace FlatWorld.GameTest.Map
{
    public sealed class TileBuildingSmokeTests
    {
        private static readonly string[] PickaxePrefabPaths =
        {
            "Assets/2_Prefabs/Weapon/Pixkaxe/Pickaxe.prefab",
            "Assets/2_Prefabs/Weapon/Pixkaxe/Pickaxe_Copper.prefab",
            "Assets/2_Prefabs/Weapon/Pixkaxe/Pickaxe_Bronze.prefab",
            "Assets/2_Prefabs/Weapon/Pixkaxe/Pickaxe_RawIron.prefab",
            "Assets/2_Prefabs/Weapon/Pixkaxe/Pickaxe_Iron.prefab"
        };

        [Test]
        [Category("Map.TileBuilding")]
        public void CellBuildingCurrentHpRoundTripsThroughMemoryPack()
        {
            TileData source = new TileData_CellBuilding
            {
                ID = "TileBase_StoneWall",
                Name = "TileBase_StoneWall",
                TileTag = BlockingTilemapLayer.BlockingTileTag,
                IsWalkable = false,
                CurrentHp = 37.5f,
                Version = TileData_CellBuilding.CurrentVersion
            };

            var memoryPackContainer = new Ex_ModData_MemoryPackable();
            memoryPackContainer.WriteData<TileData>(source);
            TileData restoredBase = memoryPackContainer.GetData<TileData>();

            Assert.That(restoredBase, Is.TypeOf<TileData_CellBuilding>());
            TileData_CellBuilding restored = (TileData_CellBuilding)restoredBase;
            Assert.That(restored.CurrentHp, Is.EqualTo(37.5f));
            Assert.That(restored.Version, Is.EqualTo(TileData_CellBuilding.CurrentVersion));
            Assert.That(restored.ID, Is.EqualTo(source.ID));
        }

        [Test]
        [Category("Map.TileBuilding")]
        public void DamageCalculationAppliesWeaknessAndRemainingDefense()
        {
            SaveDataMgr preexistingSaveManager = Object.FindObjectOfType<SaveDataMgr>();
            try
            {
                var profile = new TileBuildingDamageProfile
                {
                    Damageable = true,
                    Defense = 8f,
                    RequiredTool = TileDamageToolKind.None,
                    RequireWeaknessMatch = true,
                    Weakness = new List<DamageType>
                    {
                        new DamageType((DamageTag)1, 3)
                    }
                };
                var sender = new FakeDamageSender
                {
                    Damage = new GameValue_float(50f),
                    Weakness = new List<DamageType>
                    {
                        new DamageType((DamageTag)1, 2)
                    }
                };

                float actual = TileBuildingSystem.CalculateDamage(profile, sender, out bool matched);
                float multiplier = GameDifficultyService.ResolveDirectDamageMultiplier(null, null);

                Assert.That(matched, Is.True);
                Assert.That(actual, Is.EqualTo(Mathf.Max(1f, 50f * multiplier - 4f)).Within(0.001f));

                sender.Weakness = new List<DamageType>
                {
                    new DamageType((DamageTag)2, 10)
                };
                Assert.That(TileBuildingSystem.CalculateDamage(profile, sender, out matched), Is.Zero);
                Assert.That(matched, Is.False);
            }
            finally
            {
                if (preexistingSaveManager == null)
                {
                    SaveDataMgr createdSaveManager = Object.FindObjectOfType<SaveDataMgr>();
                    if (createdSaveManager != null)
                        Object.DestroyImmediate(createdSaveManager.gameObject);
                }
            }
        }

        [Test]
        [Category("Map.TileBuilding")]
        public void BlockingLayerRecognizesCellBuildingAndKeepsGroundVisual()
        {
            TileData floor = new TileData_Universal
            {
                ID = "Floor",
                IsWalkable = true
            };
            TileData wall = new TileData_CellBuilding
            {
                ID = "Wall",
                TileTag = BlockingTilemapLayer.BlockingTileTag,
                IsWalkable = false,
                CurrentHp = 100f
            };

            Assert.That(BlockingTilemapLayer.IsBlockingTile(wall), Is.True);
            Assert.That(BlockingTilemapLayer.ResolveGroundTile(new[] { floor, wall }), Is.SameAs(floor));
        }

        [Test]
        [Category("Map.TileBuilding")]
        public void TriggerCoveringTwoCellsSelectsNearestCellAlongAttackDirection()
        {
            GameObject mapObject = new GameObject("TileBuildingHitTest", typeof(Grid));
            GameObject layerObject = new GameObject("BlockingLayer");
            GameObject attackObject = new GameObject("AttackTrigger");
            Tile tileAsset = ScriptableObject.CreateInstance<Tile>();
            try
            {
                layerObject.transform.SetParent(mapObject.transform, false);
                global::Map map = mapObject.AddComponent<global::Map>();
                Tilemap tilemap = layerObject.AddComponent<Tilemap>();
                layerObject.AddComponent<TilemapRenderer>();
                TilemapCollider2D tilemapCollider = layerObject.AddComponent<TilemapCollider2D>();
                TilemapDamageReceiver receiver = layerObject.AddComponent<TilemapDamageReceiver>();
                map.tileMap = tilemap;
                map.Data.position = new Vector2Int(-2, -1);
                map.Data.EnsureTileStorage(4, 3);

                AddWall(map, tilemap, tileAsset, new Vector2Int(-1, 0));
                AddWall(map, tilemap, tileAsset, new Vector2Int(0, 0));
                receiver.Bind(map, tilemap, tilemapCollider);

                BoxCollider2D attackCollider = attackObject.AddComponent<BoxCollider2D>();
                attackCollider.isTrigger = true;
                attackCollider.size = new Vector2(1.2f, 0.8f);
                attackObject.transform.position = new Vector3(0f, 0.5f, 0f);

                Assert.That(receiver.TryResolveHit(
                    attackCollider,
                    new Vector2(-2f, 0.5f),
                    Vector2.right,
                    out TileBuildingHitCandidate fromLeft), Is.True);
                Assert.That(fromLeft.Cell, Is.EqualTo(new Vector2Int(-1, 0)));

                Assert.That(receiver.TryResolveHit(
                    attackCollider,
                    new Vector2(2f, 0.5f),
                    Vector2.left,
                    out TileBuildingHitCandidate fromRight), Is.True);
                Assert.That(fromRight.Cell, Is.EqualTo(new Vector2Int(0, 0)));
            }
            finally
            {
                Object.DestroyImmediate(tileAsset);
                Object.DestroyImmediate(attackObject);
                Object.DestroyImmediate(mapObject);
            }
        }

        [Test]
        [Category("Map.TileBuilding")]
        public void TriggerBoundsDoNotSelectCellOutsideRotatedCollider()
        {
            GameObject mapObject = new GameObject("TileBuildingShapeHitTest", typeof(Grid));
            GameObject layerObject = new GameObject("BlockingLayer");
            GameObject attackObject = new GameObject("RotatedAttackTrigger");
            Tile tileAsset = ScriptableObject.CreateInstance<Tile>();
            try
            {
                layerObject.transform.SetParent(mapObject.transform, false);
                global::Map map = mapObject.AddComponent<global::Map>();
                Tilemap tilemap = layerObject.AddComponent<Tilemap>();
                layerObject.AddComponent<TilemapRenderer>();
                TilemapCollider2D tilemapCollider = layerObject.AddComponent<TilemapCollider2D>();
                TilemapDamageReceiver receiver = layerObject.AddComponent<TilemapDamageReceiver>();
                map.tileMap = tilemap;
                map.Data.position = new Vector2Int(0, -1);
                map.Data.EnsureTileStorage(2, 2);

                AddWall(map, tilemap, tileAsset, new Vector2Int(0, 0));
                AddWall(map, tilemap, tileAsset, new Vector2Int(1, -1));
                receiver.Bind(map, tilemap, tilemapCollider);

                BoxCollider2D attackCollider = attackObject.AddComponent<BoxCollider2D>();
                attackCollider.isTrigger = true;
                attackCollider.size = new Vector2(2f, 0.2f);
                attackObject.transform.SetPositionAndRotation(
                    new Vector3(0.5f, 0.5f, 0f),
                    Quaternion.Euler(0f, 0f, 45f));
                Physics2D.SyncTransforms();

                Assert.That(receiver.TryResolveHit(
                    attackCollider,
                    new Vector2(3f, -0.5f),
                    Vector2.left,
                    out TileBuildingHitCandidate hit), Is.True);
                Assert.That(hit.Cell, Is.EqualTo(new Vector2Int(0, 0)));
            }
            finally
            {
                Object.DestroyImmediate(tileAsset);
                Object.DestroyImmediate(attackObject);
                Object.DestroyImmediate(mapObject);
            }
        }

        [Test]
        [Category("Map.TileBuilding")]
        public void EveryPickaxePrefabIsConfiguredForTileDamage()
        {
            foreach (string path in PickaxePrefabPaths)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.That(prefab, Is.Not.Null, $"Missing pickaxe prefab: {path}");

                Mod_Damage damageModule = prefab.GetComponentInChildren<Mod_Damage>(true);
                Assert.That(damageModule, Is.Not.Null, $"Pickaxe has no Mod_Damage: {path}");
                Assert.That(damageModule.TileDamageToolKind, Is.EqualTo(TileDamageToolKind.Pickaxe),
                    $"Pickaxe does not use tileDamageToolKind=1: {path}");
            }
        }

        [Test]
        [Category("Map.TileBuilding")]
        public void CaveStoneWallHasDamageProfile()
        {
            const string path = "Assets/4_ScriptObjects/4-1_TileBlock/TileBase_StoneWall.asset";
            Tile_Block wall = AssetDatabase.LoadAssetAtPath<Tile_Block>(path);

            Assert.That(wall, Is.Not.Null, $"Missing cave wall definition: {path}");
            Assert.That(wall.damageProfile, Is.Not.Null);
            Assert.That(wall.damageProfile.Damageable, Is.True);
            Assert.That(wall.damageProfile.MaxHealth, Is.GreaterThan(0f));
            Assert.That(wall.damageProfile.RequiredTool, Is.EqualTo(TileDamageToolKind.Pickaxe));
            Assert.That(wall.TileBase, Is.TypeOf<Tile>());

            Tile tile = (Tile)wall.TileBase;
            Assert.That(tile.sprite, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(tile.sprite.texture),
                Is.EqualTo("Assets/6_Art/Tilemap/Cave_StoneWall.png"));
            Assert.That(tile.sprite.rect.width, Is.EqualTo(32f));
            Assert.That(tile.sprite.rect.height, Is.EqualTo(32f));
            Assert.That(tile.sprite.pixelsPerUnit, Is.EqualTo(32f));
        }

        [Test]
        [Category("Map.TileBuilding")]
        public void BuiltStoneWallUsesOneCellTileAndCellBuildingState()
        {
            const string path = "Assets/4_ScriptObjects/4-1_TileBlock/TileBase_BuiltStoneWall.asset";
            Tile_Block wall = AssetDatabase.LoadAssetAtPath<Tile_Block>(path);

            Assert.That(wall, Is.Not.Null, $"Missing built stone wall definition: {path}");
            Assert.That(wall.tileDataTemplate, Is.TypeOf<TileData_CellBuilding>());
            Assert.That(((TileData_CellBuilding)wall.tileDataTemplate).CurrentHp,
                Is.EqualTo(wall.damageProfile.MaxHealth));
            Assert.That(wall.TileBase, Is.TypeOf<Tile>());

            Tile tile = (Tile)wall.TileBase;
            Assert.That(tile.colliderType, Is.EqualTo(Tile.ColliderType.Grid));
            Assert.That(tile.sprite, Is.Not.Null);
            Assert.That(tile.sprite.rect.width, Is.EqualTo(32f));
            Assert.That(tile.sprite.rect.height, Is.EqualTo(32f));
            Assert.That(tile.sprite.pixelsPerUnit, Is.EqualTo(32f));
            Assert.That(tile.sprite.pivot, Is.EqualTo(new Vector2(16f, 16f)));
        }

        [Test]
        [Category("Map.TileBuilding")]
        public void TileStackRemovalPromotesFollowingLayersAndReleasesOverflow()
        {
            var data = new Data_TileMap { position = new Vector2Int(20, -10) };
            data.EnsureTileStorage(1, 1);
            Vector2Int cell = data.position;
            TileData first = NewTile("first");
            TileData second = NewTile("second");
            TileData third = NewTile("third");
            TileData fourth = NewTile("fourth");

            Assert.That(data.SetBaseTile(cell, first), Is.True);
            Assert.That(data.PushTile(cell, second), Is.True);
            Assert.That(data.PushTile(cell, third), Is.True);
            Assert.That(data.PushTile(cell, fourth), Is.True);
            Assert.That(data.GetLayerCount(cell), Is.EqualTo(4));
            Assert.That(data.CountOverflowAllocations(), Is.EqualTo(1));
            Assert.That(data.GetTopTile(cell), Is.SameAs(fourth));

            Assert.That(data.RemoveTile(cell, 0), Is.True);
            Assert.That(data.GetLayerCount(cell), Is.EqualTo(3));
            Assert.That(data.GetTileAt(cell, 0), Is.SameAs(second));
            Assert.That(data.GetTileAt(cell, 1), Is.SameAs(third));
            Assert.That(data.GetTileAt(cell, 2), Is.SameAs(fourth));

            Assert.That(data.RemoveTile(cell, 1), Is.True);
            Assert.That(data.GetLayerCount(cell), Is.EqualTo(2));
            Assert.That(data.GetTileAt(cell, 0), Is.SameAs(second));
            Assert.That(data.GetTileAt(cell, 1), Is.SameAs(fourth));
            Assert.That(data.CountOverflowAllocations(), Is.Zero);

            TileData replacement = NewTile("replacement");
            Assert.That(data.ReplaceTop(cell, replacement), Is.True);
            Assert.That(data.GetTopTile(cell), Is.SameAs(replacement));
            Assert.That(data.RemoveTile(cell), Is.True);
            Assert.That(data.GetTopTile(cell), Is.SameAs(second));
            Assert.That(data.RemoveTile(cell, 0), Is.True);
            Assert.That(data.GetLayerCount(cell), Is.Zero);
            Assert.That(data.CountNonEmptyCells(), Is.Zero);
        }

        [Test]
        [Category("Map.TileBuilding")]
        public void SingleAndDoubleLayerChunkCellsDoNotAllocateOverflowLists()
        {
            var data = new Data_TileMap();
            data.EnsureTileStorage(100, 100);
            TileData floor = NewTile("floor");
            TileData overlay = NewTile("overlay");

            for (int y = 0; y < 100; y++)
            {
                for (int x = 0; x < 100; x++)
                    data.SetBaseTile(new Vector2Int(x, y), floor);
            }

            Assert.That(data.CountNonEmptyCells(), Is.EqualTo(10_000));
            Assert.That(data.CountOverflowAllocations(), Is.Zero);

            for (int y = 0; y < 100; y++)
            {
                for (int x = 0; x < 100; x++)
                    data.PushTile(new Vector2Int(x, y), overlay);
            }

            Assert.That(data.CountOverflowAllocations(), Is.Zero);
            data.PushTile(new Vector2Int(37, 42), NewTile("third"));
            Assert.That(data.CountOverflowAllocations(), Is.EqualTo(1));
        }

        [Test]
        [Category("Map.TileBuilding")]
        public void TileMapStorageExposesViewsInsteadOfMutableTileLists()
        {
            Assert.That(typeof(Data_TileMap).GetField("TileData_Array"), Is.Null);
            Assert.That(typeof(Data_TileMap).GetMethod("GetTileListAt"), Is.Null);

            var data = new Data_TileMap();
            data.EnsureTileStorage(1, 1);
            TileData floor = NewTile("floor");
            TileData overlay = NewTile("overlay");
            data.ReplaceStack(Vector2Int.zero, new[] { floor, overlay });

            Assert.That(data.TryGetStackView(Vector2Int.zero, out TileStackView view), Is.True);
            Assert.That(view.Count, Is.EqualTo(2));
            Assert.That(view[0], Is.SameAs(floor));
            Assert.That(view.GetFromTop(), Is.SameAs(overlay));

            var buffer = new List<TileData>();
            Assert.That(data.CopyStackTo(Vector2Int.zero, buffer), Is.True);
            buffer.Clear();
            Assert.That(data.GetLayerCount(Vector2Int.zero), Is.EqualTo(2),
                "Mutating the caller-owned copy must not mutate terrain storage.");
            Assert.That(data.ClearCell(Vector2Int.zero), Is.True);
            Assert.That(data.GetLayerCount(Vector2Int.zero), Is.Zero);
        }

        private sealed class FakeDamageSender : IDamageSender
        {
            public GameValue_float Damage { get; set; }
            public Item attacker { get; set; }
            public List<DamageType> Weakness { get; set; }
        }

        private static void AddWall(
            global::Map map,
            Tilemap tilemap,
            Tile tileAsset,
            Vector2Int cell)
        {
            map.Data.SetBaseTile(cell, new TileData_CellBuilding
            {
                ID = "Wall",
                Name = "Wall",
                TileTag = BlockingTilemapLayer.BlockingTileTag,
                IsWalkable = false,
                CurrentHp = 100f,
                position = (Vector3Int)cell
            });
            tilemap.SetTile(new Vector3Int(cell.x, cell.y, 0), tileAsset);
        }

        private static TileData NewTile(string id)
        {
            return new TileData_Universal
            {
                ID = id,
                Name = id,
                IsWalkable = true
            };
        }
    }
}
