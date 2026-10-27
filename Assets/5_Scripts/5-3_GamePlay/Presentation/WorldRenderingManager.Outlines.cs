using System.Collections.Generic;
using UnityEngine;

public sealed partial class WorldRenderingManager
{
    #region 描边请求与统一生命周期

    private readonly HashSet<InteractionTargetOutline> managedHighlightControllers = new();

    /// <summary>设置物品主动请求的高亮，独立于玩家交互预览。</summary>
    public void SetItemHighlight(Item item, bool highlighted)
    {
        if (item == null) return;
        InteractionTargetOutline outline = highlighted
            ? InteractionTargetOutline.GetOrCreate(item)
            : InteractionTargetOutline.GetExisting(item);
        if (outline == null) return;
        outline.SetHighlighted(highlighted);
        TrackHighlightController(outline);
    }

    /// <summary>按交互组件身份合并请求，同物品的多个来源互不取消。</summary>
    public InteractionTargetOutline SetInteractionTargetHighlight(Component target, bool highlighted)
    {
        if (target == null) return null;
        InteractionTargetOutline outline = highlighted
            ? InteractionTargetOutline.GetOrCreate(target)
            : InteractionTargetOutline.GetExisting(target);
        if (outline == null) return null;
        outline.SetInteractionHighlighted(target, highlighted);
        TrackHighlightController(outline);
        return outline;
    }

    /// <summary>目标组件重挂父级后仍向原控制器取消请求，避免旧物品残留描边。</summary>
    internal void ReleaseInteractionHighlightRequest(InteractionTargetOutline outline, Component source)
    {
        if (outline == null) return;
        outline.SetInteractionHighlighted(source, false);
        TrackHighlightController(outline);
    }

    /// <summary>轻量世界物与完整物品通过同一入口显示交互预览。</summary>
    public void SetWorldInteractionTargetHighlight(IInteractable target, bool highlighted)
    {
        if (target is IWorldInteractionPreview preview)
            preview.SetInteractionHighlighted(highlighted);
        else if (target is Component component && target is not IWorldInteractionTarget)
            SetInteractionTargetHighlight(component, highlighted);
    }

    /// <summary>读取物品手动与交互请求合并后的高亮状态。</summary>
    public bool IsItemHighlighted(Item item) =>
        item != null && InteractionTargetOutline.GetExisting(item) is { IsHighlighted: true };

    private void TrackHighlightController(InteractionTargetOutline outline)
    {
        if (outline.IsHighlighted) managedHighlightControllers.Add(outline);
        else managedHighlightControllers.Remove(outline);
    }

    /// <summary>控制器停用后立即移除登记，不持有已回池或销毁的目标。</summary>
    internal void ForgetHighlightController(InteractionTargetOutline outline) =>
        managedHighlightControllers.Remove(outline);

    partial void InitializeRenderingFeatureModules() { }

    partial void RefreshItemRenderingFeatures(Item item)
    {
        if (item != null)
            InteractionTargetOutline.GetExisting(item)?.RefreshHighlightedRenderers();
    }

    partial void UnregisterItemRenderingFeatures(Item item)
    {
        if (item != null)
            InteractionTargetOutline.GetExisting(item)?.ClearHighlightRequests();
        ReleaseItemVisualEffects(item);
    }

    partial void ReleaseRenderingFeatureModules()
    {
        // 先清集合再停用组件，允许组件回调在清理期间重复注销。
        InteractionTargetOutline[] outlines = new InteractionTargetOutline[managedHighlightControllers.Count];
        managedHighlightControllers.CopyTo(outlines);
        managedHighlightControllers.Clear();
        foreach (InteractionTargetOutline outline in outlines)
            if (outline != null) outline.ClearHighlightRequests();
        ReleaseWorldVisualEffects();
    }

    #endregion
}
