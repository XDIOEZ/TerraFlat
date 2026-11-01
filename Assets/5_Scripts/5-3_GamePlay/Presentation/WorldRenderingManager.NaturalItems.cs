using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.Rendering;

public sealed partial class WorldRenderingManager
{
    #region 独立世界物品视图
    private sealed class NaturalItemView
    {
        internal Item Item;
        internal uint Generation;
        internal WorldPresentationProjection Projection;
    }
    private readonly Dictionary<Item, NaturalItemView> naturalItemViews = new(ItemReferenceComparer.Instance);

    internal bool TryGetNaturalItemView(Item item, out WorldPresentationProjection view)
    {
        view = item != null && naturalItemViews.TryGetValue(item, out NaturalItemView entry) ? entry.Projection : null;
        return view != null && !view.IsReleased;
    }

    // 原外壳只提供外观输入，实际绘制副本不复制模块、Item 或任何物理组件。
    internal void RequestNaturalItemView(Item item)
    {
        if (item == null) return;
        if (item.InHand)
        {
            ReleaseNaturalItemView(item);
            ReleaseItemPresentation(item);
            return;
        }
        if (!RegisterItemRendering(item)) return;
        ReleaseItemView(item);
        if (CanBorrowItemVisual(item))
        {
            ReleaseNaturalItemView(item);
            if (RequestBorrowedItemVisual(item)) return;
        }
        ReleaseActorView(item);
        if (naturalItemViews.TryGetValue(item, out NaturalItemView existing))
        {
            if (existing.Generation == item.RuntimeGeneration && existing.Projection != null && !existing.Projection.IsReleased) return;
            ReleaseNaturalItemView(item);
        }
        Transform parent = RuntimeSceneObjectHierarchy.GetOrCreateCategoryRoot(item.gameObject.scene,
            RuntimeSceneObjectCategory.Rendering);
        WorldPresentationProjection projection = WorldPresentationProjection.Create(item.transform, parent, visualHierarchyOnly: true);
        projection.gameObject.name = item.name + " View";
        try
        {
            projection.Synchronize(Matrix4x4.identity, false);
            naturalItemViews.Add(item, new NaturalItemView { Item = item,
                Generation = item.RuntimeGeneration, Projection = projection });
        }
        catch { projection.Release(); throw; }
    }

    private void PresentNaturalItemViews(ScriptableRenderContext context, List<Camera> cameras)
    {
        PresentActorViews();
        PresentItemViews();
        using var scratch = UnityEngine.Pool.ListPool<NaturalItemView>.Get(out var views);
        views.AddRange(naturalItemViews.Values);
        // 同一个世界的物品共享一次渲染上下文，避免逐物品反复初始化机械世界。
        GameplayWorldRegistry.WorldExecutionScope worldScope = default;
        string scopedWorldKey = null;
        GameplayWorldRuntime scopedWorld = null;
        try
        {
            foreach (NaturalItemView view in views)
            {
                Item item = view.Item;
                if (item == null || item.IsInPool || item.DestructionHandled || item.InHand ||
                    item.RuntimeGeneration != view.Generation || view.Projection == null || view.Projection.IsReleased ||
                    view.Projection.gameObject.scene != item.gameObject.scene)
                {
                    worldScope.Dispose();
                    worldScope = default;
                    scopedWorldKey = null;
                    scopedWorld = null;
                    ReleaseNaturalItemView(item);
                    if (item != null && !item.IsInPool && !item.DestructionHandled && !item.InHand &&
                        item.RuntimeGeneration == view.Generation)
                        RequestNaturalItemView(item);
                    continue;
                }
                string worldKey = GameplayWorldRegistry.GetItemWorldKey(item);
                if (worldKey != scopedWorldKey)
                {
                    worldScope.Dispose();
                    worldScope = default;
                    scopedWorldKey = null;
                    scopedWorld = null;
                    if (GameplayWorldRegistry.TryGet(worldKey, out GameplayWorldRuntime world))
                    {
                        worldScope = GameplayWorldRegistry.UseWorld(worldKey);
                        scopedWorldKey = worldKey;
                        scopedWorld = world;
                    }
                }
                bool visible = scopedWorld != null && item.gameObject.activeInHierarchy &&
                    worldKey == GameplayWorldRegistry.LocalPresentationWorldKey && scopedWorld.Chunks != null &&
                    scopedWorld.Chunks.TryGetRuntimeChunkView(item.transform.position, out _);
                view.Projection.Synchronize(Matrix4x4.identity, visible);
            }
        }
        finally { worldScope.Dispose(); }
    }

    private void ReleaseNaturalItemView(Item item)
    {
        if (!ReferenceEquals(item, null) && naturalItemViews.Remove(item, out NaturalItemView view))
            view.Projection?.Release();
    }

    private void ClearNaturalItemViews()
    {
        foreach (NaturalItemView view in naturalItemViews.Values) view.Projection?.Release();
        naturalItemViews.Clear();
    }
    #endregion
}
