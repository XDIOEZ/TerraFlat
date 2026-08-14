using System;
using System.Collections.Generic;
using System.Linq;
using FlatWorld.WorldModel;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace FlatWorld.GameTest.Dimension
{
    public sealed class DimensionTileEffectTestItem : Item
    {
        public Data_GeneralItem Data = new Data_GeneralItem
        {
            IDName = "DimensionTileEffectTestItem"
        };

        public override ItemData itemData => Data;

        protected override void SetItemData(ItemData value)
        {
            Data = RequireData<Data_GeneralItem>(value);
        }
    }

    public sealed class DimensionTileEffectTests
    {
        [Test]
        [Category("Dimension.TileEffects")]
        public void RuntimeFreshWaterResolvesConfiguredTileBehaviourAndDepth()
        {
            ChunkGenerationProfileSO profile = AssetDatabase.LoadAssetAtPath<ChunkGenerationProfileSO>(
                "Assets/Resources/Config/WorldModel/ChunkGenerationProfile_Surface.asset");
            Assert.That(profile, Is.Not.Null);
            Assert.That(GameRes.Instance, Is.Not.Null);

            using var buffer = new ChunkTerrainBuffer(1, 1);
            buffer.SetCell(0, 0, new TerrainCell(
                2, 0, 0, 1, 1, TerrainCellFlags.Walkable | TerrainCellFlags.Water));
            buffer.SetEnvironmentValue("riverDepth", 0, 0, 0.65f);
            using ChunkTerrainData terrain = buffer.Seal();

            bool resolved = ChunkRuntimeTileEffectResolver.TryCreateTileEffectData(
                profile.CreateSnapshot(), terrain, Vector2Int.zero, new Vector2Int(10, 20),
                out TileData tileData, out Tile_Block tileBlock);

            Assert.That(resolved, Is.True);
            Assert.That(tileBlock.tileItemName, Is.EqualTo("Tile_Water_Fresh"));
            Assert.That(tileData, Is.TypeOf<TileData_Water>());
            Assert.That(((TileData_Water)tileData).deepValue, Is.EqualTo(0.65f).Within(0.0001f));
            Assert.That(tileData.position, Is.EqualTo(new Vector3Int(10, 20, 0)));
            Tile_Water behaviour = tileBlock.behaviours.OfType<Tile_Water>().Single();
            Assert.That(behaviour.BuffInfo, Does.Not.Contain("水体减速"));
            Assert.That(behaviour.BuffInfo, Does.Contain("潮湿"));
            Assert.That(behaviour.moveSpeedMultiplier, Is.EqualTo(0.5f));
        }

        [Test]
        [Category("Dimension.TileEffects")]
        public void TransitionExitRemovesEnvironmentSlowdownBeforeBuffSave()
        {
            using TileEffectFixture fixture = new TileEffectFixture();

            Assert.That(fixture.BuffManager.HasBuff(fixture.WetBuff.Id), Is.True);
            Assert.That(fixture.Mover.Speed.MultiplicativeModifier, Is.EqualTo(0.5f));

            fixture.Receiver.PrepareForWorldTransition();
            fixture.Receiver.PrepareForWorldTransition();
            fixture.BuffManager.Save();

            BuffManagerSaveData savedBuffs =
                fixture.BuffManager.ModData.GetData<BuffManagerSaveData>();
            Assert.That(fixture.BuffManager.HasBuff(fixture.WetBuff.Id), Is.False);
            Assert.That(
                savedBuffs.Buffs.Exists(runtime => runtime.DefinitionId == fixture.WetBuff.Id),
                Is.False);
            Assert.That(fixture.Mover.Speed.MultiplicativeModifier, Is.EqualTo(1f));
            Assert.That(fixture.Receiver.EnvironmentInteractions.ActiveEffectCount, Is.Zero);
            Assert.That(fixture.Receiver.HasActiveTileEffects, Is.False);
        }

        [Test]
        [Category("Dimension.TileEffects")]
        public void SavingTileReceiverDoesNotExitWaterTile()
        {
            using TileEffectFixture fixture = new TileEffectFixture();

            fixture.Receiver.Save();

            Assert.That(fixture.BuffManager.HasBuff(fixture.WetBuff.Id), Is.True);
            Assert.That(fixture.Mover.Speed.MultiplicativeModifier, Is.EqualTo(0.5f));
            Assert.That(fixture.Receiver.EnvironmentInteractions.ActiveEffectCount, Is.EqualTo(1));
            Assert.That(fixture.Receiver.HasActiveTileEffects, Is.True);
        }

        [Test]
        [Category("Dimension.TileEffects")]
        public void ClearingBuffsInWaterKeepsEnvironmentSlowdown()
        {
            using TileEffectFixture fixture = new TileEffectFixture();

            Assert.That(fixture.BuffManager.HasBuff(fixture.WetBuff.Id), Is.True);
            Assert.That(fixture.Mover.Speed.MultiplicativeModifier, Is.EqualTo(0.5f));

            fixture.BuffManager.ClearAllBuffs();

            Assert.That(fixture.BuffManager.HasBuff(fixture.WetBuff.Id), Is.False,
                "潮湿是真实状态，允许被清 Buff 操作移除。");
            Assert.That(fixture.Mover.Speed.MultiplicativeModifier, Is.EqualTo(0.5f),
                "水体减速来自环境实例，不能被清 Buff 操作解除。");
            Assert.That(fixture.Receiver.EnvironmentInteractions.ActiveEffectCount, Is.EqualTo(1));
        }

        [Test]
        [Category("Dimension.TileEffects")]
        public void RuntimeSaltWaterProvidesDrinkActionAndRemovesItOnExit()
        {
            using TileEffectFixture fixture = new TileEffectFixture(80f);

            Assert.That(fixture.Receiver.EnvironmentInteractions.TryGetDefinition(
                out DrinkWaterActionDefinition definition), Is.True,
                "进入盐水地块后必须获得喝水动作定义。");
            Assert.That(definition.WaterKind, Is.EqualTo(WaterEnvironmentKind.Salt));

            fixture.Receiver.PrepareForWorldTransition();

            Assert.That(fixture.Receiver.EnvironmentInteractions.AvailableActionCount, Is.Zero,
                "离开盐水地块后必须撤销喝水动作定义。");
        }

        private sealed class TileEffectFixture : IDisposable
        {
            private const string TestTileId = "Test_DimensionTransitionWater";

            private readonly GameObject itemObject;
            private readonly GameObject mapObject;
            private readonly Tile_Block waterBlock;
            private readonly GameRes gameRes;
            private readonly Tile_Block previousBlock;
            private readonly bool hadPreviousBlock;
            private readonly BuffDefinition previousWetBuff;
            private readonly bool hadPreviousWetBuff;

            public BuffManager BuffManager { get; }
            public Mover Mover { get; }
            public TileEffectReceiver Receiver { get; }
            public BuffDefinition WetBuff { get; }

            public TileEffectFixture(float salt = 0f)
            {
                WetBuff = BuffCatalogLoader.LoadBuiltInDefinitions()
                    .Single(definition => definition.Id == "潮湿");
                Assert.That(WetBuff, Is.Not.Null);

                gameRes = GameRes.Instance;
                Assert.That(gameRes, Is.Not.Null);
                hadPreviousWetBuff = gameRes.BuffDefinitions.TryGetValue(
                    WetBuff.Id,
                    out BuffDefinition existingWetBuff);
                previousWetBuff = existingWetBuff;
                gameRes.BuffDefinitions[WetBuff.Id] = WetBuff;

                waterBlock = ScriptableObject.CreateInstance<Tile_Block>();
                waterBlock.tileItemName = TestTileId;
                waterBlock.behaviours.Add(new Tile_Water
                {
                    BuffInfo = new List<string> { WetBuff.Id },
                    moveSpeedMultiplier = 0.5f
                });
                hadPreviousBlock = gameRes.TileBlockDict.TryGetValue(TestTileId, out previousBlock);
                gameRes.TileBlockDict[TestTileId] = waterBlock;

                mapObject = new GameObject("DimensionTileEffectTestMap", typeof(Grid));
                GameObject tilemapObject = new GameObject("Tilemap", typeof(Tilemap));
                tilemapObject.transform.SetParent(mapObject.transform, false);
                global::Map map = mapObject.AddComponent<global::Map>();
                map.tileMap = tilemapObject.GetComponent<Tilemap>();
                map.Data.position = Vector2Int.zero;
                map.Data.EnsureTileStorage(1, 1);
                map.Data.SetBaseTile(Vector2Int.zero, new TileData_Water
                {
                    ID = TestTileId,
                    Name = TestTileId,
                    deepValue = 0.5f,
                    salt = salt
                });

                itemObject = new GameObject("DimensionTileEffectTestItem");
                itemObject.SetActive(false);
                DimensionTileEffectTestItem item = itemObject.AddComponent<DimensionTileEffectTestItem>();
                item.itemMods = new ItemMods(item);

                BuffManager = itemObject.AddComponent<BuffManager>();
                BuffManager.ModData = CreateModuleData(ModText.BuffManager, "TestBuffManager");
                Mover = itemObject.AddComponent<Mover>();
                Mover.ModDataMemoryPack = CreateModuleData(ModText.Mover, "TestMover");

                GameObject receiverPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/2_Prefabs/Gameplay/Modules/Managers/TileReciver.prefab");
                Assert.That(receiverPrefab, Is.Not.Null);
                GameObject receiverObject = UnityEngine.Object.Instantiate(receiverPrefab, itemObject.transform);
                Receiver = receiverObject.GetComponent<TileEffectReceiver>();
                Assert.That(Receiver, Is.Not.Null);
                Receiver.ModSaveData = CreateModuleData(ModText.TileEffectReceiver, "TestTileReceiver");

                item.itemMods.AddMod(BuffManager);
                item.itemMods.AddMod(Mover);
                item.itemMods.AddMod(Receiver);
                BuffManager.ModuleInit(item, BuffManager.ModData);
                Mover.ModuleInit(item, Mover.ModDataMemoryPack);
                Receiver.ModuleInit(item, Receiver.ModSaveData);
                Receiver.Cache_map = map;

                itemObject.SetActive(true);
                Assert.That(Receiver.RefreshCurrentTileEffects(), Is.True);
            }

            public void Dispose()
            {
                if (gameRes != null)
                {
                    if (hadPreviousBlock)
                        gameRes.TileBlockDict[TestTileId] = previousBlock;
                    else
                        gameRes.TileBlockDict.Remove(TestTileId);

                    if (hadPreviousWetBuff)
                        gameRes.BuffDefinitions[WetBuff.Id] = previousWetBuff;
                    else
                        gameRes.BuffDefinitions.Remove(WetBuff.Id);

                }

                if (itemObject != null)
                    UnityEngine.Object.DestroyImmediate(itemObject);
                if (mapObject != null)
                    UnityEngine.Object.DestroyImmediate(mapObject);
                if (waterBlock != null)
                    UnityEngine.Object.DestroyImmediate(waterBlock);
            }

            private static Ex_ModData_MemoryPackable CreateModuleData(string id, string name)
            {
                return new Ex_ModData_MemoryPackable
                {
                    ID = id,
                    Name = name
                };
            }
        }
    }
}
