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
        ChunkCollisionRenderer.PresentationChanged -= RefreshChunk;
        ChunkCollisionRenderer.PresentationChanged += RefreshChunk;
    }

    private static bool IsRegistered(Item item)
    {
        return item != null && item.itemData != null && ItemMgr.Instance != null &&
               ItemMgr.Instance.GetItemByGuid(item.itemData.Guid) == item;
    }

    private static void RegisterItem(Item item)
    {
        WrappedRigidbody2DAdapter.Ensure(item);
        WrappedItemPhysicsAdapter.Ensure(item);
    }

    private static void RefreshItemStructure(Item item)
    {
        if (!IsRegistered(item))
            return;
        WrappedRigidbody2DAdapter.Ensure(item);
        WrappedItemPhysicsAdapter adapter = item.GetComponent<WrappedItemPhysicsAdapter>();
        if (adapter != null)
            adapter.InvalidateSources();
        else
            WrappedItemPhysicsAdapter.Ensure(item);
    }

    private static void UnregisterItem(Item item)
    {
        if (item == null)
            return;
        item.GetComponent<WrappedRigidbody2DAdapter>()?.Suspend();
        item.GetComponent<WrappedItemPhysicsAdapter>()?.Suspend();
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

    private static void RefreshChunk(ChunkCollisionRenderer renderer)
    {
        if (renderer == null)
            return;
        if (renderer.isActiveAndEnabled && renderer.BoundChunk?.Terrain != null && renderer.SourceCollider != null)
            WrappedTilemapPhysicsAdapter.Ensure(renderer);
        else
            renderer.GetComponent<WrappedTilemapPhysicsAdapter>()?.Suspend();
    }
}
