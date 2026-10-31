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
        if (item == null || item.InHand || !RegisterItemRendering(item)) return;
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
        using var scratch = UnityEngine.Pool.ListPool<NaturalItemView>.Get(out var views);
        views.AddRange(naturalItemViews.Values);
        foreach (NaturalItemView view in views)
        {
            Item item = view.Item;
            if (item == null || item.IsInPool || item.DestructionHandled || item.InHand ||
                item.RuntimeGeneration != view.Generation)
            {
                ReleaseNaturalItemView(item);
                continue;
            }
            if (view.Projection == null || view.Projection.IsReleased || view.Projection.gameObject.scene != item.gameObject.scene)
            {
                ReleaseNaturalItemView(item);
                RequestNaturalItemView(item);
                continue;
            }
            string worldKey = GameplayWorldRegistry.GetItemWorldKey(item);
            bool visible = false;
            if (GameplayWorldRegistry.TryGet(worldKey, out GameplayWorldRuntime world))
            {
                using var scope = GameplayWorldRegistry.UseWorld(worldKey);
                visible = item.gameObject.activeInHierarchy &&
                    worldKey == GameplayWorldRegistry.LocalPresentationWorldKey && world.Chunks != null &&
                    world.Chunks.TryGetRuntimeChunkView(item.transform.position, out _);
            }
            view.Projection.Synchronize(Matrix4x4.identity, visible);
        }
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
