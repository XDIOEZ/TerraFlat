using UnityEngine;

/// <summary>
/// 统一处理运行时 Item 的显式父级或 Chunk 归属。
/// </summary>
internal static class ItemWorldPlacement
{
    public static void Attach(Item item, GameObject itemObject, Vector3 position, GameObject parent)
    {
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
}
