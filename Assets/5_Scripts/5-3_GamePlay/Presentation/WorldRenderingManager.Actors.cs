using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public sealed partial class WorldRenderingManager
{
    #region 角色独立视觉租约
    private sealed class ActorView
    {
        internal Item Item;
        internal Mod_AnimatorController Animation;
        internal ActorRenderEffectController Effects;
        internal uint Generation;
        internal Transform Anchor;
        internal Transform Visual;
        internal Transform OriginalParent;
        internal int OriginalSibling;
        internal Vector3 OriginalPosition;
        internal Quaternion OriginalRotation;
        internal Vector3 OriginalScale;
        internal GameObject ViewRoot;
        internal SpriteRenderer Sprite;
        internal SortingGroup SourceSorting;
        internal SortingGroup ViewSorting;
    }

    private readonly Dictionary<Item, ActorView> actorViews = new(ItemReferenceComparer.Instance);

    /// <summary>角色模块只提供动作状态，真正的 Sprite/Animator 节点交给世界渲染根持有。</summary>
    internal void RequestActorView(Item item)
    {
        if (item == null || item is Player || item is Map || item.itemData == null ||
            GameRes.ExistingInstance == null ||
            !GameRes.ExistingInstance.TryGetItemDefinition(item.itemData.IDName, out RuntimeItemDefinition definition) ||
            !definition.IsActor)
        {
            ReleaseActorView(item);
            return;
        }

        Mod_AnimatorController animation = item.itemMods?.GetMod_ByID<Mod_AnimatorController>(ModText.AnimatorReceiver);
        if (animation == null || !animation.IsSingleSpriteMode)
        {
            ReleaseActorView(item);
            return;
        }
        if (actorViews.TryGetValue(item, out ActorView existing))
        {
            if (existing.Generation == item.RuntimeGeneration && existing.Visual != null && existing.Sprite == item.Sprite)
                return;
            ReleaseActorView(item);
        }

        SpriteRenderer sprite = item.Sprite;
        if (sprite == null) return;
        Transform visual = sprite.transform;
        // 允许晚于 Module.Load 接管，但先归还旧 PoseRoot，绝不记住临时动画节点作为池基线。
        if (animation.IsRuntimeLoaded && visual.IsChildOf(item.transform))
            animation.ReleaseSeparatedPresentation();
        Transform originalParent = visual.parent;
        if (originalParent == null || !visual.IsChildOf(item.transform) ||
            visual.GetComponentInChildren<Module>(true) != null ||
            visual.GetComponentInChildren<Item>(true) != null ||
            visual.GetComponentInChildren<Collider2D>(true) != null ||
            visual.GetComponentInChildren<Rigidbody2D>(true) != null)
            return;

        Transform parent = RuntimeSceneObjectHierarchy.GetOrCreateCategoryRoot(item.gameObject.scene,
            RuntimeSceneObjectCategory.Rendering);
        var holder = new GameObject(item.name + " ActorView") { hideFlags = HideFlags.DontSave };
        holder.transform.SetParent(parent, false);
        SortingGroup sourceSorting = null;
        for (Transform current = originalParent; current != null; current = current.parent)
            if (current.TryGetComponent(out SortingGroup found) && found.enabled)
            { sourceSorting = found; break; }
        var entry = new ActorView
        {
            Item = item, Animation = animation, Generation = item.RuntimeGeneration,
            Anchor = originalParent, Visual = visual, OriginalParent = originalParent,
            OriginalSibling = visual.GetSiblingIndex(), OriginalPosition = visual.localPosition,
            OriginalRotation = visual.localRotation, OriginalScale = visual.localScale,
            Sprite = sprite, ViewRoot = holder, SourceSorting = sourceSorting,
            Effects = originalParent.GetComponent<ActorRenderEffectController>()
        };
        actorViews.Add(item, entry);
        try
        {
            // 仅移动不含业务组件的视觉节点；模块、Item 和物理代理原地不动。
            visual.SetParent(holder.transform, false);
            if (sourceSorting != null)
                entry.ViewSorting = holder.AddComponent<SortingGroup>();
            entry.Effects?.RegisterOwnedRenderer(sprite);
            originalParent.GetComponent<ActorBuffLightController>()?.RebindVisualParent();
            SynchronizeActorView(entry);
        }
        catch
        {
            ReleaseActorView(item);
            throw;
        }
    }

    internal bool OwnsActorVisual(Item item, Transform visual)
        => item != null && visual != null && actorViews.TryGetValue(item, out ActorView entry) &&
           entry.Visual != null && (entry.Visual == visual || visual.IsChildOf(entry.Visual));

    internal void SynchronizeActorViewForPose(Item item)
    {
        if (item != null && actorViews.TryGetValue(item, out ActorView entry))
            SynchronizeActorView(entry);
    }

    internal Transform GetActorVisualParent(Item item, Transform fallback)
        => item != null && actorViews.TryGetValue(item, out ActorView entry) && entry.Visual != null
            ? entry.Visual : fallback;

    internal Transform GetActorProjectionSource(Item item)
        => item != null && actorViews.TryGetValue(item, out ActorView entry) && entry.ViewRoot != null
            ? entry.ViewRoot.transform : item != null ? item.transform : null;

    private static void SynchronizeActorView(ActorView entry)
    {
        if (entry.Item == null || entry.Visual == null || entry.Anchor == null || entry.ViewRoot == null) return;
        Transform root = entry.ViewRoot.transform;
        if (entry.ViewRoot.scene != entry.Item.gameObject.scene)
        {
            root.SetParent(null, true);
            SceneManager.MoveGameObjectToScene(entry.ViewRoot, entry.Item.gameObject.scene);
            root.SetParent(RuntimeSceneObjectHierarchy.GetOrCreateCategoryRoot(entry.Item.gameObject.scene,
                RuntimeSceneObjectCategory.Rendering), true);
        }
        Matrix4x4 matrix = root.parent != null
            ? root.parent.worldToLocalMatrix * entry.Anchor.localToWorldMatrix : entry.Anchor.localToWorldMatrix;
        Vector3 position = matrix.GetColumn(3);
        Quaternion rotation = matrix.rotation;
        Vector3 scale = matrix.lossyScale;
        if (root.localPosition != position || root.localRotation != rotation)
            root.SetLocalPositionAndRotation(position, rotation);
        if (root.localScale != scale) root.localScale = scale;
        if (entry.SourceSorting != null && entry.ViewSorting != null)
        {
            if (entry.ViewSorting.enabled != entry.SourceSorting.enabled)
                entry.ViewSorting.enabled = entry.SourceSorting.enabled;
            if (entry.ViewSorting.sortAtRoot != entry.SourceSorting.sortAtRoot)
                entry.ViewSorting.sortAtRoot = entry.SourceSorting.sortAtRoot;
            if (entry.ViewSorting.sortingLayerID != entry.SourceSorting.sortingLayerID)
                entry.ViewSorting.sortingLayerID = entry.SourceSorting.sortingLayerID;
            if (entry.ViewSorting.sortingOrder != entry.SourceSorting.sortingOrder)
                entry.ViewSorting.sortingOrder = entry.SourceSorting.sortingOrder;
        }
        bool visible = entry.Item.isActiveAndEnabled &&
            entry.Anchor.gameObject.activeInHierarchy && entry.Item.gameObject.scene.isLoaded &&
            GameplayWorldRegistry.GetItemWorldKey(entry.Item) == GameplayWorldRegistry.LocalPresentationWorldKey;
        if (entry.ViewRoot.activeSelf != visible) entry.ViewRoot.SetActive(visible);
    }

    private void PresentActorViews()
    {
        using var scope = UnityEngine.Pool.ListPool<ActorView>.Get(out var views);
        views.AddRange(actorViews.Values);
        foreach (ActorView view in views)
        {
            if (view.Item == null || view.Item.DestructionHandled || view.Item.IsInPool ||
                view.Item.RuntimeGeneration != view.Generation || view.Visual == null || view.Anchor == null)
            {
                ReleaseActorView(view.Item);
                continue;
            }
            SynchronizeActorView(view);
        }
    }

    internal void ReleaseActorView(Item item)
    {
        if (ReferenceEquals(item, null) || !actorViews.Remove(item, out ActorView entry)) return;
        // 先拆掉动画姿态层，再把 Prefab 的视觉子节点原样还给对象池。
        if (entry.Animation != null) entry.Animation.ReleaseSeparatedPresentation();
        if (entry.Effects != null) entry.Effects.UnregisterOwnedRenderer(entry.Sprite);
        if (entry.Visual != null && entry.OriginalParent != null)
        {
            entry.Visual.SetParent(entry.OriginalParent, false);
            entry.Visual.SetSiblingIndex(entry.OriginalSibling);
            entry.Visual.SetLocalPositionAndRotation(entry.OriginalPosition, entry.OriginalRotation);
            entry.Visual.localScale = entry.OriginalScale;
        }
        if (entry.ViewRoot != null)
        {
            if (Application.isPlaying) Destroy(entry.ViewRoot);
            else DestroyImmediate(entry.ViewRoot);
        }
    }

    private void ClearActorViews()
    {
        using var scope = UnityEngine.Pool.ListPool<Item>.Get(out var items);
        items.AddRange(actorViews.Keys);
        foreach (Item item in items) ReleaseActorView(item);
    }
    #endregion
}
