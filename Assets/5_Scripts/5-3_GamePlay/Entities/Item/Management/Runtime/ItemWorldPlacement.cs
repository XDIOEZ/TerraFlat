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

        // 新版区块窗口下，散落物的临时 ChunkView 归属由具体世界状态在最终落点确认后建立；
        // 这里绝不能为了找父级触发旧 Chunk 同步加载。
        if (ChunkMgr.ExistingInstance != null &&
            ChunkMgr.ExistingInstance.IsWorldModelRuntimeActive)
        {
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

    /// <summary>
    /// 将一个运行时散落物绑定到新版 ChunkView 临时物品节点。
    /// 不访问旧 Chunk 字典，也不请求旧区块加载。
    /// </summary>
    internal static bool TryAttachWorldModelTransientItem(Item item, Vector2 position)
    {
        if (item == null || item.gameObject == null)
            return false;

        ChunkNaturalItemRenderer existingOwner =
            item.GetComponentInParent<ChunkNaturalItemRenderer>(true);
        ChunkMgr chunkMgr = ChunkMgr.ExistingInstance;
        if (chunkMgr == null || !chunkMgr.IsWorldModelRuntimeActive)
            return existingOwner != null;

        if (!chunkMgr.TryGetRuntimeDropParent(position, out ChunkNaturalItemRenderer owner))
        {
            existingOwner?.RegisterTransientItem(item);
            return existingOwner != null;
        }

        if (existingOwner != owner)
        {
            existingOwner?.UnregisterTransientItem(item);
            item.transform.SetParent(owner.transform, true);
        }
        owner.RegisterTransientItem(item);
        return true;
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
