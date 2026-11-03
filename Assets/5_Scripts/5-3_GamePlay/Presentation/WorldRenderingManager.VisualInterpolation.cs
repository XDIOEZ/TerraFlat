using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.Rendering;

public sealed partial class WorldRenderingManager
{
    #region 角色与物品纯视觉插值

    private sealed class ItemVisualMotionEntry
    {
        internal readonly WorldVisualPoseHistory History = new();
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
        GameplayFixedStepSimulation.Register(this, GameplayFixedStepSimulationPhase.SnapshotCapture);
        RenderPipelineManager.beginContextRendering -= PresentInterpolatedItemVisuals;
        // 必须排在原有 PresentNaturalItemViews 之后，否则该帧同步会覆盖显示位置。
        RenderPipelineManager.beginContextRendering += PresentInterpolatedItemVisuals;
    }

    private void UnregisterVisualInterpolationRendering()
    {
        GameplayFixedStepSimulation.Unregister(this);
        RenderPipelineManager.beginContextRendering -= PresentInterpolatedItemVisuals;
        ClearVisualInterpolationHistory();
    }

    /// <summary>瞬移调用方可主动丢弃旧视觉历史；权威位置由调用方自行管理。</summary>
    public void SnapItemVisualPresentation(Item item)
    {
        ForgetItemVisualInterpolation(item);
        ResetViewSimulationPoseHistories(item);
    }

    private void ResetViewSimulationPoseHistories(Item item)
    {
        if (item == null) return;
        if (itemViews.TryGetValue(item, out var view)) view.Projection?.ResetSimulationPoses();
        if (naturalItemViews.TryGetValue(item, out var natural)) natural.Projection?.ResetSimulationPoses();
        if (actorViews.TryGetValue(item, out var actor)) actor.AnchorMotion = new WorldVisualPoseHistory();
    }

    private void ForgetItemVisualInterpolation(Item item)
    {
        if (!ReferenceEquals(item, null)) itemVisualMotion.Remove(item);
    }

    private void ClearVisualInterpolationHistory() => itemVisualMotion.Clear();

    /// <summary>相机与头顶 UI 只读同一份已提交姿态，不因查询次数改变插值历史。</summary>
    public Vector3 GetItemRenderedWorldPosition(Item item)
    {
        if (item == null) return Vector3.zero;
        return GetItemRenderedWorldPoint(item, item.transform.position);
    }

    public Vector3 GetItemRenderedWorldPoint(Item item, Vector3 sourceWorldPoint)
    {
        if (item == null) return sourceWorldPoint;
        // 手持物包含在持有者绘制树中，不能再独立插值一遍。
        Item owner = item.InHand && item.Owner != null ? item.Owner : item;
        return TryResolveVisualMotion(owner, out Matrix4x4 motion)
            ? motion.MultiplyPoint3x4(sourceWorldPoint) : sourceWorldPoint;
    }

    public Quaternion GetItemRenderedWorldRotation(Item item)
    {
        if (item == null) return Quaternion.identity;
        return TryResolveVisualMotion(item, out Matrix4x4 motion)
            ? motion.rotation * item.transform.rotation : item.transform.rotation;
    }

    /// <summary>挂点与拖尾同时读取根运动和局部动作姿态，已借出的视觉节点不重复平移。</summary>
    public Vector3 GetItemRenderedTransformPoint(Item item, Transform source, Vector3 localPoint)
        => GetItemRenderedTransformMatrix(item, source).MultiplyPoint3x4(localPoint);

    public Matrix4x4 GetItemRenderedTransformMatrix(Item item, Transform source)
    {
        if (source == null) return Matrix4x4.identity;
        if (item == null) return source.localToWorldMatrix;
        Item owner = item.InHand && item.Owner != null ? item.Owner : item;
        if (actorViews.TryGetValue(owner, out var actor) && actor.Anchor != null && actor.ViewRoot != null &&
            source.IsChildOf(actor.ViewRoot.transform))
        {
            WorldVisualPose pose = actor.AnchorMotion.Evaluate(WorldVisualPose.ReadWorld(actor.Anchor),
                GameplayFixedStepSimulation.PresentationTime);
            return pose.Matrix * actor.ViewRoot.transform.worldToLocalMatrix * source.localToWorldMatrix;
        }
        Matrix4x4 matrix = itemViews.TryGetValue(owner, out var view) && view.Projection != null
            ? view.Projection.GetRenderedSourceMatrix(source)
            : naturalItemViews.TryGetValue(owner, out var natural) && natural.Projection != null
                ? natural.Projection.GetRenderedSourceMatrix(source) : source.localToWorldMatrix;
        return TryResolveVisualMotion(owner, out Matrix4x4 motion) ? motion * matrix : matrix;
    }

    // 世界锚定的纯视觉子节点反投影到源层级，避免被父节点插值第二次移动。
    public Vector3 GetSourceWorldPointForRenderedParent(Item item, Transform parent, Vector3 displayedWorldPoint)
        => parent.localToWorldMatrix.MultiplyPoint3x4(
            GetItemRenderedTransformMatrix(item, parent).inverse.MultiplyPoint3x4(displayedWorldPoint));

    /// <summary>唯一的 GO 姿态采样入口，位于整轮物理和接触回调之后。</summary>
    public void SimulateFixedStep(float deltaTime)
    {
        if (!renderingServicesActive) return;
        double now = GameplayFixedStepSimulation.CurrentTime;
        foreach (ActorView view in actorViews.Values)
        {
            if (!CaptureItemSimulationPose(view.Item, view.Generation, view.ViewRoot, deltaTime, now)) continue;
            if (view.Anchor != null)
                view.AnchorMotion.Capture(WorldVisualPose.ReadWorld(view.Anchor), now,
                    ResolveContinuousMotionDistance(itemVisualMotion[view.Item].Body, deltaTime));
        }
        foreach (ItemView view in itemViews.Values)
            if (view.Projection != null && !view.Projection.IsReleased &&
                CaptureItemSimulationPose(view.Item, view.Generation, view.Projection.gameObject, deltaTime, now))
                view.Projection.CaptureSimulationPoses(now);
        foreach (NaturalItemView view in naturalItemViews.Values)
            if (view.Projection != null && !view.Projection.IsReleased &&
                CaptureItemSimulationPose(view.Item, view.Generation, view.Projection.gameObject, deltaTime, now))
                view.Projection.CaptureSimulationPoses(now);
    }

    private void PresentInterpolatedItemVisuals(ScriptableRenderContext context, List<Camera> cameras)
    {
        if (!renderingServicesActive) return;
        double renderTime = GameplayFixedStepSimulation.PresentationTime;

        foreach (ActorView view in actorViews.Values)
        {
            if (view.Item == null || view.Anchor == null || view.ViewRoot == null ||
                !view.ViewRoot.activeInHierarchy || view.Generation != view.Item.RuntimeGeneration)
                continue;
            MarkItemViewPresented(view.Item);
            WorldVisualPose live = WorldVisualPose.ReadWorld(view.Anchor);
            WorldVisualPose displayed = view.AnchorMotion.Evaluate(live, renderTime);
            if (!displayed.Matches(live))
                WorldPresentationProjection.ApplyWorldMotionToTransform(view.ViewRoot.transform,
                    displayed.Matrix * live.Matrix.inverse);
        }
        foreach (ItemView view in itemViews.Values)
        {
            if (view.Item == null || view.Projection == null || view.Projection.IsReleased ||
                !view.Projection.gameObject.activeInHierarchy || view.Generation != view.Item.RuntimeGeneration)
                continue;
            MarkItemViewPresented(view.Item);
            if (TryResolveVisualMotion(view.Item, out Matrix4x4 motion))
                view.Projection.ApplyVisualMotionTransform(motion);
        }
        foreach (NaturalItemView view in naturalItemViews.Values)
        {
            if (view.Item == null || view.Projection == null || view.Projection.IsReleased ||
                !view.Projection.gameObject.activeInHierarchy || view.Generation != view.Item.RuntimeGeneration)
                continue;
            MarkItemViewPresented(view.Item);
            if (TryResolveVisualMotion(view.Item, out Matrix4x4 motion))
                view.Projection.ApplyVisualMotionTransform(motion);
        }

        // 不可见、被回池或转移渲染所有权的旧视图不保留跨场景插值历史。
        using var scratch = UnityEngine.Pool.ListPool<Item>.Get(out var expired);
        foreach (var pair in itemVisualMotion)
            if (Time.frameCount - pair.Value.LastVisibleFrame > 1) expired.Add(pair.Key);
        foreach (Item item in expired) itemVisualMotion.Remove(item);
    }

    private bool CaptureItemSimulationPose(Item item, uint generation, GameObject viewRoot,
        float deltaTime, double simulationTime)
    {
        if (item == null || viewRoot == null || !viewRoot.activeInHierarchy || generation != item.RuntimeGeneration ||
            !item.isActiveAndEnabled || item.IsInPool || item.DestructionHandled ||
            !item.gameObject.scene.isLoaded || !FlatWorld.Networking.GameNetwork.HasStateAuthority)
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
            entry.Body = WorldPhysicsService2D.GetItemRigidbody(item);
            entry.LastVisibleFrame = Time.frameCount;
            ResetViewSimulationPoseHistories(item);
            entry.History.Reset(WorldVisualPose.ReadWorld(item.transform), simulationTime);
        }
        if (UsesNativeBodyInterpolation(entry))
        {
            entry.History.Reset(WorldVisualPose.ReadWorld(item.transform), simulationTime);
            return false;
        }
        float distance = ResolveContinuousMotionDistance(entry.Body, deltaTime);
        entry.History.Capture(WorldVisualPose.ReadWorld(item.transform), simulationTime, distance);
        return true;
    }

    private void MarkItemViewPresented(Item item)
    { if (itemVisualMotion.TryGetValue(item, out var entry)) entry.LastVisibleFrame = Time.frameCount; }

    private static float ResolveContinuousMotionDistance(Rigidbody2D body, float deltaTime) => body != null
        ? Mathf.Max(WorldVisualMotionHistory.TeleportDistance, body.velocity.magnitude * deltaTime * 2f)
        : WorldVisualMotionHistory.TeleportDistance;

    private static bool UsesNativeBodyInterpolation(ItemVisualMotionEntry entry) =>
        Physics2D.simulationMode != SimulationMode2D.Script && entry.Body != null &&
        entry.Body.interpolation != RigidbodyInterpolation2D.None;

    private bool TryResolveVisualMotion(Item item, out Matrix4x4 motion)
    {
        motion = Matrix4x4.identity;
        if (!renderingServicesActive || !itemVisualMotion.TryGetValue(item, out var entry) ||
            item.RuntimeGeneration != entry.Generation || item.IsInPool || item.DestructionHandled ||
            !item.isActiveAndEnabled || item.gameObject.scene.handle != entry.SceneHandle ||
            entry.WorldKey != GameplayWorldRegistry.GetItemWorldKey(item) ||
            Time.frameCount - entry.LastVisibleFrame > 1 || UsesNativeBodyInterpolation(entry)) return false;
        WorldVisualPose live = WorldVisualPose.ReadWorld(item.transform);
        WorldVisualPose displayed = entry.History.Evaluate(live, GameplayFixedStepSimulation.PresentationTime);
        if (displayed.Matches(live)) return false;
        motion = displayed.Matrix * live.Matrix.inverse;
        return true;
    }

    #endregion
}
