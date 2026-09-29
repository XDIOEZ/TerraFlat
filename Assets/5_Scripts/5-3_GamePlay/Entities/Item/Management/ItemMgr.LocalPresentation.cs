using UnityEngine;

public partial class ItemMgr
{
    #region 环世界局部表现

    /// <summary>本地玩家跨周时批量重选运行时 Item 的最近镜像；只在事件边界执行，不新增逐 Item Tick。</summary>
    internal void ReprojectRuntimeItemsToLocalAnchor()
    {
        if (!WorldLocalPresentation.HasAnchor ||
            !WorldTopologyRuntime.TryGetActiveBounds(out _))
        {
            return;
        }

        for (int i = 0; i < RuntimeItems.Count; i++)
        {
            Item item = RuntimeItems[i];
            if (item == null || item.itemData == null || item.InHand || item is Map ||
                item is Player player && player.IsLocalProfile ||
                item.GetComponentInParent<ChunkView>(true) != null)
            {
                continue;
            }

            Vector3 current = item.transform.position;
            Vector3 logical = WorldLocalPresentation.ToLogical(current);
            Vector3 projected = WorldLocalPresentation.ProjectPosition(logical);
            if (((Vector2)(projected - current)).sqrMagnitude <= 0.000001f)
            {
                if (item.itemData.transform != null)
                    item.itemData.transform.position = logical;
                continue;
            }

            Rigidbody2D body = item.GetComponent<Rigidbody2D>();
            if (body != null)
                body.position = projected;
            item.transform.position = projected;
            if (item.itemData.transform != null)
                item.itemData.transform.position = logical;

            NotifyRuntimeItemMoved(item);
        }
    }

    #endregion
}
