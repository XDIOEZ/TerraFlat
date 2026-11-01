using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

public sealed partial class WorldRenderingManager
{
    #region 玩家与普通物品独立绘制
    private sealed class ItemView
    {
        internal Item Item;
        internal uint Generation;
        internal WorldPresentationProjection Projection;
    }

    private readonly Dictionary<Item, ItemView> itemViews = new(ItemReferenceComparer.Instance);

    // GO 生物优先移动纯视觉节点，玩家和普通物品使用同一套无逻辑、无物理的绘制投影。
    internal void RequestItemPresentation(Item item)
    {
        if (item == null || item is Map || item.itemData == null || item.IsInPool ||
            item.IsPreparedCandidate || item.DestructionHandled)
            return;
        if (item.NaturalRuntimeOwner != null)
        {
            ReleaseItemPresentation(item);
            return;
        }
        ReleaseNaturalItemView(item);

        bool actor = item is not Player && GameRes.ExistingInstance != null &&
            GameRes.ExistingInstance.TryGetItemDefinition(item.itemData.IDName, out RuntimeItemDefinition definition) &&
            definition.IsActor;
        if (actor)
        {
            RequestActorView(item);
            if (actorViews.ContainsKey(item))
            {
                ReleaseItemView(item);
                return;
            }
        }
        else ReleaseActorView(item);

        if (!IsItemRenderingRegistered(item)) return;

        if (item is not Player &&
            (item.InHand || item.transform.parent != null &&
                item.transform.parent.GetComponentInParent<Item>(true) != null))
        {
            ReleaseItemView(item);
            return;
        }

        if (itemViews.TryGetValue(item, out ItemView current))
        {
            if (current.Generation == item.RuntimeGeneration && current.Projection != null &&
                !current.Projection.IsReleased && current.Projection.gameObject.scene == item.gameObject.scene)
                return;
            ReleaseItemView(item);
        }

        if (item.GetComponentInChildren<Renderer>(true) == null) return;
        Transform root = RuntimeSceneObjectHierarchy.GetOrCreateCategoryRoot(item.gameObject.scene,
            RuntimeSceneObjectCategory.Rendering);
        WorldPresentationProjection projection = WorldPresentationProjection.Create(
            item.transform, root, visualHierarchyOnly: true);
        projection.gameObject.name = item.name + " View";
        try
        {
            projection.Synchronize(Matrix4x4.identity, false);
            itemViews.Add(item, new ItemView { Item = item, Generation = item.RuntimeGeneration, Projection = projection });
        }
        catch
        {
            projection.Release();
            throw;
        }
    }

    internal bool TryGetItemPresentation(Item item, out WorldPresentationProjection view)
    {
        view = item != null && itemViews.TryGetValue(item, out ItemView entry) ? entry.Projection : null;
        return view != null && !view.IsReleased;
    }

    private void PresentItemViews()
    {
        using var scope = ListPool<ItemView>.Get(out var views);
        views.AddRange(itemViews.Values);
        foreach (ItemView view in views)
        {
            Item item = view.Item;
            if (item == null || item.DestructionHandled || item.IsInPool || item.NaturalRuntimeOwner != null ||
                item.RuntimeGeneration != view.Generation || view.Projection == null || view.Projection.IsReleased ||
                item is not Player && (item.InHand || item.transform.parent != null &&
                    item.transform.parent.GetComponentInParent<Item>(true) != null))
            {
                ReleaseItemView(item);
                continue;
            }
            if (view.Projection.gameObject.scene != item.gameObject.scene)
            {
                ReleaseItemView(item);
                RequestItemPresentation(item);
                continue;
            }
            bool visible = item.isActiveAndEnabled && item.gameObject.scene.isLoaded &&
                GameplayWorldRegistry.GetItemWorldKey(item) == GameplayWorldRegistry.LocalPresentationWorldKey;
            view.Projection.Synchronize(Matrix4x4.identity, visible);
        }
    }

    internal void ReleaseItemPresentation(Item item)
    {
        ReleaseActorView(item);
        ReleaseItemView(item);
    }

    private void ReleaseItemView(Item item)
    {
        if (!ReferenceEquals(item, null) && itemViews.Remove(item, out ItemView view))
            view.Projection?.Release();
    }

    private void ClearItemViews()
    {
        foreach (ItemView view in itemViews.Values) view.Projection?.Release();
        itemViews.Clear();
    }
    #endregion
}
