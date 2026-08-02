using System;
using System.Collections.Generic;
using System.Linq;
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

        public override ItemData itemData
        {
            get => Data;
            set => Data = (Data_GeneralItem)value;
        }
    }

    public sealed class DimensionTileEffectTests
    {
        [Test]
        [Category("Dimension.TileEffects")]
        public void TransitionExitRemovesWaterSlowdownBeforeBuffSave()
        {
            using TileEffectFixture fixture = new TileEffectFixture();

            Assert.That(fixture.BuffManager.HasBuff(fixture.SlowBuff.Id), Is.True);

            fixture.Receiver.PrepareForWorldTransition();
            fixture.Receiver.PrepareForWorldTransition();
            fixture.BuffManager.Save();

            BuffManagerSaveData savedBuffs =
                fixture.BuffManager.ModData.GetData<BuffManagerSaveData>();
            Assert.That(fixture.BuffManager.HasBuff(fixture.SlowBuff.Id), Is.False);
            Assert.That(
                savedBuffs.Buffs.Exists(runtime => runtime.DefinitionId == fixture.SlowBuff.Id),
                Is.False);
            Assert.That(fixture.Receiver.HasActiveTileEffects, Is.False);
        }

        [Test]
        [Category("Dimension.TileEffects")]
        public void SavingTileReceiverDoesNotExitWaterTile()
        {
            using TileEffectFixture fixture = new TileEffectFixture();

            fixture.Receiver.Save();

            Assert.That(fixture.BuffManager.HasBuff(fixture.SlowBuff.Id), Is.True);
            Assert.That(fixture.Receiver.HasActiveTileEffects, Is.True);
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
            private readonly BuffDefinition previousSlowBuff;
            private readonly bool hadPreviousSlowBuff;

            public BuffManager BuffManager { get; }
            public TileEffectReceiver Receiver { get; }
            public BuffDefinition SlowBuff { get; }

            public TileEffectFixture()
            {
                SlowBuff = BuffCatalogLoader.LoadBuiltInDefinitions()
                    .Single(definition => definition.Id == "水体减速");
                Assert.That(SlowBuff, Is.Not.Null);

                gameRes = GameRes.Instance;
                Assert.That(gameRes, Is.Not.Null);
                hadPreviousSlowBuff = gameRes.BuffDefinitions.TryGetValue(
                    SlowBuff.Id,
                    out BuffDefinition existingSlowBuff);
                previousSlowBuff = existingSlowBuff;
                gameRes.BuffDefinitions[SlowBuff.Id] = SlowBuff;

                waterBlock = ScriptableObject.CreateInstance<Tile_Block>();
                waterBlock.tileItemName = TestTileId;
                waterBlock.behaviours.Add(new Tile_Water
                {
                    BuffInfo = new List<string> { SlowBuff.Id }
                });
                hadPreviousBlock = gameRes.TileBlockDict.TryGetValue(TestTileId, out previousBlock);
                gameRes.TileBlockDict[TestTileId] = waterBlock;

                mapObject = new GameObject("DimensionTileEffectTestMap", typeof(Grid));
                GameObject tilemapObject = new GameObject("Tilemap", typeof(Tilemap));
                tilemapObject.transform.SetParent(mapObject.transform, false);
                global::Map map = mapObject.AddComponent<global::Map>();
                map.tileMap = tilemapObject.GetComponent<Tilemap>();
                map.Data.position = Vector2Int.zero;
                map.Data.EnsureTileDataArray(1, 1);
                map.ADDTileData(Vector2Int.zero, new TileData_Water
                {
                    ID = TestTileId,
                    Name = TestTileId,
                    deepValue = 0.5f
                });

                itemObject = new GameObject("DimensionTileEffectTestItem");
                itemObject.SetActive(false);
                DimensionTileEffectTestItem item = itemObject.AddComponent<DimensionTileEffectTestItem>();
                item.itemMods = new ItemMods(item);

                BuffManager = itemObject.AddComponent<BuffManager>();
                BuffManager.ModData = CreateModuleData(ModText.BuffManager, "TestBuffManager");

                GameObject receiverPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/2_Prefabs/Module/Manager/TileReciver.prefab");
                Assert.That(receiverPrefab, Is.Not.Null);
                GameObject receiverObject = UnityEngine.Object.Instantiate(receiverPrefab, itemObject.transform);
                Receiver = receiverObject.GetComponent<TileEffectReceiver>();
                Assert.That(Receiver, Is.Not.Null);
                Receiver.ModSaveData = CreateModuleData(ModText.TileEffectReceiver, "TestTileReceiver");

                item.itemMods.AddMod(BuffManager);
                item.itemMods.AddMod(Receiver);
                BuffManager.ModuleInit(item, BuffManager.ModData);
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

                    if (hadPreviousSlowBuff)
                        gameRes.BuffDefinitions[SlowBuff.Id] = previousSlowBuff;
                    else
                        gameRes.BuffDefinitions.Remove(SlowBuff.Id);
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
