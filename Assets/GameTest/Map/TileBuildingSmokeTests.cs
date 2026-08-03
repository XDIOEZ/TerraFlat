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
            "Assets/2_Prefabs/Weapon/Pixkaxe/Pickaxe_Stone.prefab",
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
                map.Data.EnsureTileDataArray(4, 3, initCells: false);

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
                map.Data.EnsureTileDataArray(2, 2, initCells: false);

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
            map.Data.AddTileData(cell, new TileData_CellBuilding
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
    }
}
