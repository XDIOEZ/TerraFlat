using UnityEngine;

/// <summary>
/// 当前 GameObject 世界的可选物理接入层，只消费坐标核心与通用生命周期通知。
/// 无场景扫描、无自身 Update；创建或调用 Domain 不会触发这里的注册或生成代理。
/// </summary>
internal static class WrappedWorldPhysicsAdapter
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        // 关闭 Domain Reload 时仍保证每个入口只订阅一次。
        ItemMgr.RuntimeItemRegistered -= RegisterItem;
        ItemMgr.RuntimeItemRegistered += RegisterItem;
        ItemMgr.RuntimeItemUnregistered -= UnregisterItem;
        ItemMgr.RuntimeItemUnregistered += UnregisterItem;
        Item.RuntimeStructureChanged -= RefreshItemStructure;
        Item.RuntimeStructureChanged += RefreshItemStructure;
        Map.TilemapPresentationChanged -= RefreshMap;
        Map.TilemapPresentationChanged += RefreshMap;
        ChunkCollisionRenderer.PresentationChanged -= RefreshChunkCollision;
        ChunkCollisionRenderer.PresentationChanged += RefreshChunkCollision;
        WorldTopologyRuntime.LocalPlayerWrapped -= ReprojectLocalWorld;
        WorldTopologyRuntime.LocalPlayerWrapped += ReprojectLocalWorld;
    }

    private static bool IsRegistered(Item item)
    {
        return item != null && item.itemData != null && ItemMgr.Instance != null &&
               ItemMgr.Instance.GetItemByGuid(item.itemData.Guid) == item;
    }

    private static void RegisterItem(Item item)
    {
        WrappedRigidbody2DAdapter.Ensure(item);
    }

    private static void RefreshItemStructure(Item item)
    {
        if (!IsRegistered(item))
            return;
        WrappedRigidbody2DAdapter.Ensure(item);
    }

    private static void UnregisterItem(Item item)
    {
        if (item == null)
            return;
        item.GetComponent<WrappedRigidbody2DAdapter>()?.Suspend();
        item.GetComponent<WrappedTilemapPhysicsAdapter>()?.Suspend();
    }

    private static void RefreshMap(Map map)
    {
        if (map == null)
            return;
        if (map.isActiveAndEnabled && map.IsTilemapVisualReady)
            WrappedTilemapPhysicsAdapter.Ensure(map);
        else
            map.GetComponent<WrappedTilemapPhysicsAdapter>()?.Suspend();
    }

    private static void RefreshChunkCollision(ChunkCollisionRenderer renderer)
    {
        if (renderer == null) return;
        if (renderer.isActiveAndEnabled && renderer.BoundChunk?.Terrain != null)
            WrappedTilemapPhysicsAdapter.Ensure(renderer);
        else
            renderer.GetComponent<WrappedTilemapPhysicsAdapter>()?.Suspend();
    }

    /// <summary>跨周只批量重选本机镜像；不复制 Item Collider，也不改任何逻辑坐标。</summary>
    private static void ReprojectLocalWorld()
    {
        ItemMgr.Instance?.ReprojectRuntimeItemsToLocalAnchor();
        ChunkMgr.ExistingInstance?.ReprojectRuntimeChunkViewsToLocalAnchor();
    }
}
