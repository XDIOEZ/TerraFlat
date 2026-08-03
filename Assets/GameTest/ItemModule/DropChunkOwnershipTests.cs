using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace FlatWorld.GameTest.ItemModule
{
    public sealed class DropChunkTestItem : Item
    {
        private Data_GeneralItem data = new Data_GeneralItem
        {
            IDName = "DropItemTest",
            Guid = 77123,
            Stack = new ItemStack()
        };

        public override ItemData itemData
        {
            get => data;
            set => data = (Data_GeneralItem)value;
        }
    }

    public sealed class DropChunkOwnershipTests
    {
        [Test]
        [Category("ItemModule.Drop")]
        public void MissingTargetChunkDoesNotDetachItemFromPreviousOwner()
        {
            GameObject chunkObject = new GameObject("DropOwnerChunkTest");
            GameObject itemObject = new GameObject("DropItemTest");
            chunkObject.SetActive(false);
            itemObject.SetActive(false);

            try
            {
                Chunk oldChunk = chunkObject.AddComponent<Chunk>();
                oldChunk.MapSave = new MapSave
                {
                    Name = "(0, 0)",
                    MapPosition = Vector2Int.zero
                };

                DropChunkTestItem item = itemObject.AddComponent<DropChunkTestItem>();
                item.transform.SetParent(oldChunk.transform, false);
                oldChunk.RunTimeItems[item.itemData.Guid] = item;

                Mod_Droping dropping = itemObject.AddComponent<Mod_Droping>();
                dropping.LastChunk = oldChunk;

                MethodInfo updateChunkOwner = typeof(Mod_Droping).GetMethod(
                    "UpdateChunkOwner",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(updateChunkOwner, Is.Not.Null);

                bool assigned = (bool)updateChunkOwner.Invoke(
                    dropping,
                    new object[] { item, new Vector2(1_000_000.5f, 1_000_000.5f) });

                Assert.That(assigned, Is.False);
                Assert.That(dropping.LastChunk, Is.SameAs(oldChunk));
                Assert.That(item.transform.parent, Is.SameAs(oldChunk.transform));
                Assert.That(oldChunk.RunTimeItems.ContainsKey(item.itemData.Guid), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(itemObject);
                Object.DestroyImmediate(chunkObject);
            }
        }
    }
}
