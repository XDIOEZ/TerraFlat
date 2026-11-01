using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.Rendering;

public sealed partial class WorldRenderingManager
{
    #region 角色与物品纯视觉插值

    private sealed class ItemVisualMotionEntry
    {
        internal readonly WorldVisualMotionHistory History = new();
        internal uint Generation;
        internal int SceneHandle;
        internal int ViewInstanceId;
        internal int LastVisibleFrame;
        internal string WorldKey;
        internal Rigidbody2D Body;
    }

    private readonly Dictionary<Item, ItemVisualMotionEntry> itemVisualMotion =
        new(ItemReferenceComparer.Instance);

    private void RegisterVisualInterpolationRendering()
    {
        RenderPipelineManager.beginContextRendering -= PresentInterpolatedItemVisuals;
        // 必须排在原有 PresentNaturalItemViews 之后，否则该帧同步会覆盖显示位置。
        RenderPipelineManager.beginContextRendering += PresentInterpolatedItemVisuals;
    }

    private void UnregisterVisualInterpolationRendering()
    {
        RenderPipelineManager.beginContextRendering -= PresentInterpolatedItemVisuals;
        ClearVisualInterpolationHistory();
    }

    /// <summary>瞬移调用方可主动丢弃旧视觉历史；权威位置由调用方自行管理。</summary>
    public void SnapItemVisualPresentation(Item item) => ForgetItemVisualInterpolation(item);

    private void ForgetItemVisualInterpolation(Item item)
    {
        if (!ReferenceEquals(item, null)) itemVisualMotion.Remove(item);
    }

    private void ClearVisualInterpolationHistory() => itemVisualMotion.Clear();

    private Vector3 GetVisualMotionOffsetForReflection(Item item, Sprite sprite)
    {
        // 已借出的角色 Sprite 本身就在视觉根下，不可二次补偿。
        if (actorViews.ContainsKey(item) || !itemVisualMotion.TryGetValue(item, out var entry) ||
            entry.LastVisibleFrame != Time.frameCount ||
            entry.Body != null && entry.Body.interpolation != RigidbodyInterpolation2D.None)
            return Vector3.zero;
        Vector3 delta = entry.History.Evaluate(Time.timeAsDouble) - entry.History.Current;
        float ppu = sprite != null ? Mathf.Max(WorldVisualMotionHistory.PixelGrid, sprite.pixelsPerUnit)
            : WorldVisualMotionHistory.PixelGrid;
        return WorldVisualMotionHistory.PixelAlignTranslation(delta, ppu);
    }

    private void PresentInterpolatedItemVisuals(ScriptableRenderContext context, List<Camera> cameras)
    {
        if (!renderingServicesActive) return;
        double simulationTime = GameplayFixedStepSimulation.CurrentTime;
        double renderTime = Time.timeAsDouble;

        foreach (ActorView view in actorViews.Values)
        {
            if (view.Item == null || view.Anchor == null || view.ViewRoot == null ||
                !view.ViewRoot.activeInHierarchy || view.Generation != view.Item.RuntimeGeneration)
                continue;
            if (TryResolveVisualTranslation(view.Item, view.Generation, view.ViewRoot,
                view.Item.transform.position, view.Sprite != null ? view.Sprite.sprite : null,
                simulationTime, renderTime, out Vector3 translation))
                view.ViewRoot.transform.position += translation;
        }
        foreach (ItemView view in itemViews.Values)
        {
            if (view.Item == null || view.Projection == null || view.Projection.IsReleased ||
                !view.Projection.gameObject.activeInHierarchy || view.Generation != view.Item.RuntimeGeneration)
                continue;
            if (TryResolveVisualTranslation(view.Item, view.Generation, view.Projection.gameObject,
                view.Item.transform.position, view.Item.Sprite != null ? view.Item.Sprite.sprite : null,
                simulationTime, renderTime, out Vector3 translation))
                view.Projection.ApplyVisualMotionTranslation(translation);
        }
        foreach (NaturalItemView view in naturalItemViews.Values)
        {
            if (view.Item == null || view.Projection == null || view.Projection.IsReleased ||
                !view.Projection.gameObject.activeInHierarchy || view.Generation != view.Item.RuntimeGeneration)
                continue;
            if (TryResolveVisualTranslation(view.Item, view.Generation, view.Projection.gameObject,
                view.Item.transform.position, view.Item.Sprite != null ? view.Item.Sprite.sprite : null,
                simulationTime, renderTime, out Vector3 translation))
                view.Projection.ApplyVisualMotionTranslation(translation);
        }

        // 不可见、被回池或转移渲染所有权的旧视图不保留跨场景插值历史。
        using var scratch = UnityEngine.Pool.ListPool<Item>.Get(out var expired);
        foreach (var pair in itemVisualMotion)
            if (Time.frameCount - pair.Value.LastVisibleFrame > 1) expired.Add(pair.Key);
        foreach (Item item in expired) itemVisualMotion.Remove(item);
    }

    private bool TryResolveVisualTranslation(Item item, uint generation, GameObject viewRoot,
        Vector3 sourcePosition, Sprite sprite, double simulationTime, double renderTime,
        out Vector3 translation)
    {
        translation = Vector3.zero;
        if (!item.isActiveAndEnabled || item.IsInPool || item.DestructionHandled ||
            !item.gameObject.scene.isLoaded)
        {
            ForgetItemVisualInterpolation(item);
            return false;
        }

        if (!itemVisualMotion.TryGetValue(item, out ItemVisualMotionEntry entry))
        {
            entry = new ItemVisualMotionEntry();
            itemVisualMotion.Add(item, entry);
        }
        string worldKey = GameplayWorldRegistry.GetItemWorldKey(item);
        if (entry.Generation != generation || entry.SceneHandle != item.gameObject.scene.handle ||
            entry.ViewInstanceId != viewRoot.GetInstanceID() || entry.WorldKey != worldKey ||
            Time.frameCount - entry.LastVisibleFrame > 1 || !entry.History.HasSnapshot)
        {
            entry.Generation = generation;
            entry.SceneHandle = item.gameObject.scene.handle;
            entry.ViewInstanceId = viewRoot.GetInstanceID();
            entry.WorldKey = worldKey;
            entry.Body = item.GetComponent<Rigidbody2D>();
            entry.History.Reset(sourcePosition, simulationTime);
        }
        entry.LastVisibleFrame = Time.frameCount;
        // 已由原生 Rigidbody2D 插值的来源不能再次平滑，否则出现双重滞后。
        if (entry.Body != null && entry.Body.interpolation != RigidbodyInterpolation2D.None)
        {
            entry.History.Reset(sourcePosition, simulationTime);
            return false;
        }

        entry.History.Capture(sourcePosition, simulationTime);
        translation = entry.History.Evaluate(renderTime) - sourcePosition;
        // 像素风按源贴图像素网格量化位移，不量化真实坐标与物理节点。
        float pixelsPerUnit = sprite != null ? Mathf.Max(WorldVisualMotionHistory.PixelGrid, sprite.pixelsPerUnit)
            : WorldVisualMotionHistory.PixelGrid;
        translation = WorldVisualMotionHistory.PixelAlignTranslation(translation, pixelsPerUnit);
        return translation != Vector3.zero;
    }

    #endregion
}
