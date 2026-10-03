using System;
using UnityEngine;

/// <summary>机械的轻量交互/灯光桥；所有主体和运动部件提交行网格，不含 SpriteRenderer 或逐帧动画脚本。</summary>
public sealed partial class MechanicalDepthVisual : MonoBehaviour, ISpatialInteractionShape
{
    #region 部件与生命周期
    private sealed class PartVisual
    {
        internal Sprite Sprite;
        internal Material Material;
        internal Quaternion Rotation;
        internal Vector3 Offset, Scale;
        internal Color Tint;
        internal Vector4 Animation, Conveyor;
        internal int Id, Order;
        internal bool Touched, Visible, DrawBelowMechanical;
    }
    private readonly PartVisual[] parts = new PartVisual[4];
    private ChunkTilemapRenderer owner;
    private int entityId, occupancy;
    private bool highlighted, disposed;

    internal static MechanicalDepthVisual Create(ChunkTilemapRenderer owner, Vector3 position, int id, int occupancy)
    {
        var root = new GameObject("MechanicalInteraction_" + id) { hideFlags = HideFlags.DontSave };
        root.transform.SetParent(owner.transform, false);
        root.transform.position = position;
        var visual = root.AddComponent<MechanicalDepthVisual>();
        visual.owner = owner; visual.entityId = id; visual.occupancy = occupancy;
        return visual;
    }

    internal void BeginUpdate(Vector3 position)
    {
        transform.position = position;
        foreach (PartVisual part in parts) if (part != null) part.Touched = false;
        foreach (PartVisual part in facilityParts) if (part != null) part.Touched = false;
        if (facilityLight != null) facilityLight.enabled = false;
    }

    internal void SetPart(int index, Sprite sprite, Material material, Quaternion rotation,
        Vector3 offset, Vector3 scale, int mode, Vector4 animation, bool drawBelowMechanical = false, Vector4 conveyor = default)
    {
        if ((uint)index >= (uint)parts.Length) throw new ArgumentOutOfRangeException(nameof(index));
        PartVisual part = parts[index] ??= new PartVisual { Id = index, Order = occupancy * 32 + index };
        if (part.DrawBelowMechanical != drawBelowMechanical) owner?.RemoveMachineDepthPart(entityId, part.Id);
        part.Sprite = sprite; part.Material = material; part.Rotation = rotation;
        part.Offset = rotation * offset; part.Scale = scale; part.Animation = animation; part.Conveyor = conveyor;
        part.DrawBelowMechanical = drawBelowMechanical;
        part.Tint = Color.white; part.Touched = part.Visible = true;
        Submit(part);
    }

    internal void EndUpdate()
    {
        foreach (PartVisual part in parts) RemoveUntouched(part);
        foreach (PartVisual part in facilityParts) RemoveUntouched(part);
    }

    private void Submit(PartVisual part)
    {
        if (owner == null || disposed || !part.Visible || !owner.IsBatchPresentationRegistered) return;
        ChunkDepthMeshRenderer renderer = part.DrawBelowMechanical
            ? owner.MechanicalConnectorDepthMesh : owner.DepthMesh;
        renderer.Set(ChunkDepthMeshRenderer.MachineDomain, entityId, part.Id,
            part.Sprite, part.Material,
            Matrix4x4.TRS(transform.position + part.Offset, part.Rotation, part.Scale),
            transform.position, part.Order, part.Tint, animation: part.Animation, highlighted: highlighted, conveyor: part.Conveyor);
    }

    private void RemoveUntouched(PartVisual part)
    {
        if (part == null || part.Touched || !part.Visible) return;
        part.Visible = false;
        owner?.RemoveMachineDepthPart(entityId, part.Id);
    }

    internal void Dispose()
    {
        if (disposed) return;
        disposed = true;
        UnregisterInteraction();
        foreach (PartVisual part in parts) if (part != null) owner?.RemoveMachineDepthPart(entityId, part.Id);
        foreach (PartVisual part in facilityParts) if (part != null) owner?.RemoveMachineDepthPart(entityId, part.Id);
        owner = null;
        gameObject.SetActive(false);
        if (Application.isPlaying) Destroy(gameObject);
        else DestroyImmediate(gameObject);
    }
    private void OnEnable()
    {
        RegisterInteraction();
        foreach (PartVisual part in parts) if (part != null) Submit(part);
        foreach (PartVisual part in facilityParts) if (part != null) Submit(part);
    }
    private void OnDisable()
    {
        UnregisterInteraction();
        foreach (PartVisual part in parts) if (part != null) owner?.RemoveMachineDepthPart(entityId, part.Id);
        foreach (PartVisual part in facilityParts) if (part != null) owner?.RemoveMachineDepthPart(entityId, part.Id);
    }
    private void OnDestroy()
    {
        UnregisterInteraction();
        owner?.RemoveMachineDepthEntity(entityId);
        owner = null;
    }
    #endregion

    #region 权威交互与网格描边
    private MachineInteractionTarget interactionTarget;
    internal void BindInteraction(MachineInteractionTarget target)
    {
        if (ReferenceEquals(interactionTarget, target)) return;
        UnregisterInteraction(); interactionTarget = target;
        if (isActiveAndEnabled) RegisterInteraction();
    }
    private void RegisterInteraction()
    {
        if (interactionTarget == null || !interactionTarget.IsValid) return;
        interactionTarget.InteractionHighlightChanged -= SetInteractionHighlighted;
        interactionTarget.InteractionHighlightChanged += SetInteractionHighlighted;
        SpatialInteractionRegistry.Register(this, 0f, interactionTarget);
        SetInteractionHighlighted(interactionTarget.IsInteractionHighlighted);
    }
    private void UnregisterInteraction()
    {
        SpatialInteractionRegistry.Unregister(this);
        if (interactionTarget != null) interactionTarget.InteractionHighlightChanged -= SetInteractionHighlighted;
        SetInteractionHighlighted(false);
    }
    private void SetInteractionHighlighted(bool value)
    {
        if (highlighted == value) return;
        highlighted = value;
        foreach (PartVisual part in parts) if (part != null) Submit(part);
        foreach (PartVisual part in facilityParts) if (part != null) Submit(part);
    }

    /// <summary>只在交互查询时计算运动后的图层范围，不以 Renderer 或碰撞体保存机器数据。</summary>
    public bool ContainsInteractionPoint(Vector2 point)
    {
        Vector2 projected = (Vector2)transform.position + WorldTopologyRuntime.ShortestDelta(transform.position, point);
        foreach (PartVisual part in parts) if (Contains(part, projected)) return true;
        foreach (PartVisual part in facilityParts) if (Contains(part, projected)) return true;
        return false;
    }
    private bool Contains(PartVisual part, Vector2 point)
    {
        if (part == null || !part.Visible || part.Sprite == null) return false;
        float phase = part.Animation.z + Time.time * part.Animation.y;
        int mode = Mathf.RoundToInt(part.Animation.x);
        Matrix4x4 motion = Matrix4x4.identity;
        if (mode == 1) motion = Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, phase * Mathf.Rad2Deg));
        else if (mode == 3) motion = Matrix4x4.Translate(Vector3.down * ((1f - Mathf.Cos(phase)) * .5f * part.Animation.w));
        else if (mode == 4) motion = Matrix4x4.Scale(new Vector3(1f, 1f - (1f - Mathf.Cos(phase)) * .5f * Mathf.Clamp01(part.Animation.w), 1f));
        Matrix4x4 matrix = Matrix4x4.TRS(transform.position + part.Offset, part.Rotation, part.Scale) * motion;
        Vector3 local = matrix.inverse.MultiplyPoint3x4(new Vector3(point.x, point.y, transform.position.z));
        Bounds bounds = SharedSpriteMeshCache.GetGeometry(part.Sprite).Bounds;
        return local.x >= bounds.min.x && local.x <= bounds.max.x && local.y >= bounds.min.y && local.y <= bounds.max.y;
    }
    #endregion
}
