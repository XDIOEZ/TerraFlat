using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 统一处理运行时 Item 的显式父级或 Chunk 归属。
/// </summary>
internal static class ItemWorldPlacement
{
    public static void Attach(Item item, GameObject itemObject, Vector3 position, GameObject parent)
    {
        // 已登记物品以实际物理所有者的场景为准，不能回迁到同名旧场景。
        bool hasOwner = WorldPhysicsService2D.TryGetRegisteredItemOwner(item, out var owner);
        Scene target = hasOwner ? owner.PrimarySpace.Scene :
            parent != null ? parent.scene : GameplayWorldRegistry.CurrentScene;
        if (hasOwner && parent != null && parent.scene != target)
            throw new InvalidOperationException($"物品 {item.name} 的父级不属于已登记的世界场景：" +
                $"parent={parent.scene.name}({parent.scene.handle})，owner={target.name}({target.handle})，" +
                $"itemWorld={GameplayWorldRegistry.GetItemWorldKey(item)}，physicsWorld={owner.WorldKey}，" +
                $"itemScene={itemObject.scene.name}({itemObject.scene.handle})。");
        if (target.IsValid() && target.isLoaded && itemObject.scene != target)
        {
            itemObject.transform.SetParent(null, true);
            SceneManager.MoveGameObjectToScene(itemObject, target);
        }
        if (RuntimeWorldResidentEntityUtility.IsPersistentWorldEntity(item))
        {
            AttachRuntimeWorldResident(item, itemObject);
            return;
        }

        if (parent != null)
        {
            itemObject.transform.SetParent(parent.transform, true);
            return;
        }

        using var worldScope = hasOwner && GameplayWorldRegistry.TryGet(owner.WorldKey, out var world) &&
            world.Scene == target ? GameplayWorldRegistry.UseWorld(owner.WorldKey) : default;
        // 新版区块窗口下，散落物的临时 ChunkView 归属由具体世界状态在最终落点确认后建立；
        // 这里绝不能为了找父级触发旧 Chunk 同步加载。
        if (ChunkMgr.ExistingInstance != null &&
            ChunkMgr.ExistingInstance.IsWorldModelRuntimeActive)
        {
            AttachToSceneItemCategory(item, itemObject);
            return;
        }

        Vector2Int chunkPosition = Chunk.GetChunkPosition(position);
        if (ChunkMgr.Instance.TryGetActiveChunkByPos(chunkPosition, out Chunk chunk))
        {
            if (chunk == null)
            {
                ChunkMgr.Instance.GetClosestChunk(itemObject.transform.position, out chunk);
            }

            if (chunk != null && chunk.gameObject.scene == itemObject.scene)
            {
                itemObject.transform.SetParent(chunk.transform, true);
                chunk.AddItem(item);
                return;
            }
        }

        if (ChunkMgr.Instance.TryGetUnActiveChunkByPos(chunkPosition, out Chunk inactiveChunk) &&
            inactiveChunk != null && inactiveChunk.gameObject.scene == itemObject.scene)
        {
            itemObject.transform.SetParent(inactiveChunk.transform, true);
            return;
        }
        AttachToSceneItemCategory(item, itemObject);
    }

    // 未指定宿主的物品只归入当前场景分类，区块与手持物仍保留显式父级。
    internal static void AttachToSceneItemCategory(Item item, GameObject itemObject)
        => itemObject.transform.SetParent(RuntimeSceneObjectHierarchy.GetOrCreateCategoryRoot(
            itemObject.scene, item is Player ? RuntimeSceneObjectCategory.Players : RuntimeSceneObjectCategory.Items), true);

    /// <summary>
    /// 将一个运行时散落物绑定到世界逻辑区块的临时物品账本。
    /// 不访问旧 Chunk 字典，也不请求旧区块加载。
    /// </summary>
    internal static bool TryAttachWorldModelTransientItem(Item item, Vector2 position)
    {
        if (item == null || item.PersistenceOwner != null || item.gameObject == null)
            return false;
        if (RuntimeWorldResidentEntityUtility.IsPersistentWorldEntity(item))
        {
            AttachRuntimeWorldResident(item, item.gameObject);
            return true;
        }

        ChunkNaturalItemRuntime existingOwner =
            item.NaturalRuntimeOwner;
        ChunkMgr chunkMgr = ChunkMgr.ExistingInstance;
        if (chunkMgr == null || !chunkMgr.IsWorldModelRuntimeActive)
            return existingOwner != null;

        if (!chunkMgr.TryGetNaturalItemRuntime(position, out ChunkNaturalItemRuntime owner))
        {
            existingOwner?.RegisterTransientItem(item);
            return existingOwner != null;
        }

        if (existingOwner != owner)
        {
            if (existingOwner != null && existingOwner.TryGetSpawnedItem(item.itemData.Guid, out Item natural) && natural == item)
            {
                ItemMgr.Instance.SetRuntimeWorldResidentPersistence(item, true);
                return true;
            }
            existingOwner?.UnregisterTransientItem(item);
            item.transform.SetParent(owner.GetOrCreateLogicalItemRoot(), true);
        }
        if (!owner.TryGetSpawnedItem(item.itemData.Guid, out Item spawned) || spawned != item)
            owner.RegisterTransientItem(item);
        return true;
    }

    /// <summary>将实体 AI 从旧 Chunk 所有权中摘除并挂到场景中性根节点。</summary>
    internal static void AttachRuntimeAi(Item item, GameObject itemObject)
        => AttachRuntimeWorldResident(item, itemObject);

    internal static void AttachRuntimeWorldResident(Item item, GameObject itemObject)
    {
        if (item == null || itemObject == null)
            return;

        item.NaturalRuntimeOwner?.ReleaseItemToWorldResident(item);

        Chunk legacyOwner = item.GetComponentInParent<Chunk>();
        legacyOwner?.RemoveItem(item);

        string worldKey = GameplayWorldRegistry.GetItemWorldKey(item);
        using var worldScope = GameplayWorldRegistry.TryGet(worldKey, out _)
            ? GameplayWorldRegistry.UseWorld(worldKey) : default;
        Transform root = ItemMgr.Instance?.GetRuntimeEntityRoot(itemObject.scene);
        itemObject.transform.SetParent(root, true);
        ItemMgr.Instance?.NotifyRuntimeItemMoved(item);
    }
}
