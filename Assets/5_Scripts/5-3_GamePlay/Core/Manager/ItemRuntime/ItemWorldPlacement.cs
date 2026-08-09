using UnityEngine;

/// <summary>
/// 统一处理运行时 Item 的显式父级或 Chunk 归属。
/// </summary>
internal static class ItemWorldPlacement
{
    public static void Attach(Item item, GameObject itemObject, Vector3 position, GameObject parent)
    {
        if (RuntimeAiEntityUtility.IsAiEntity(item))
        {
            AttachRuntimeAi(item, itemObject);
            return;
        }

        if (parent != null)
        {
            itemObject.transform.SetParent(parent.transform, true);
            return;
        }

        Vector2Int chunkPosition = Chunk.GetChunkPosition(position);
        if (ChunkMgr.Instance.TryGetActiveChunkByPos(chunkPosition, out Chunk chunk))
        {
            if (chunk == null)
            {
                ChunkMgr.Instance.GetClosestChunk(itemObject.transform.position, out chunk);
            }

            if (chunk != null)
            {
                itemObject.transform.SetParent(chunk.transform, true);
                chunk.AddItem(item);
                return;
            }
        }

        if (ChunkMgr.Instance.TryGetUnActiveChunkByPos(chunkPosition, out Chunk inactiveChunk) &&
            inactiveChunk != null)
        {
            itemObject.transform.SetParent(inactiveChunk.transform, true);
        }
    }

    /// <summary>将实体 AI 从旧 Chunk 所有权中摘除并挂到场景中性根节点。</summary>
    internal static void AttachRuntimeAi(Item item, GameObject itemObject)
    {
        if (item == null || itemObject == null)
            return;

        Chunk legacyOwner = item.GetComponentInParent<Chunk>();
        legacyOwner?.RemoveItem(item);

        Transform root = ItemMgr.Instance?.GetRuntimeEntityRoot(itemObject.scene);
        itemObject.transform.SetParent(root, true);
        ItemMgr.Instance?.NotifyRuntimeItemMoved(item);
    }
}
