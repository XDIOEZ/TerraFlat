using System.Collections.Generic;
using UnityEngine;

public sealed partial class Mod_Roll
{
    #region 贴图旋转与碰撞形状快照
    private Quaternion originalVisualRotation;
    private bool presentationApplied;
    private readonly List<RollColliderShape> colliderShapes = new();

    private bool CanUsePresentation()
    {
        if (visualRoot == null || item == null || visualRoot == item.transform || !visualRoot.IsChildOf(item.transform) ||
            visualRoot.GetComponentInChildren<Collider2D>(true) != null ||
            visualRoot.GetComponentInChildren<Rigidbody2D>(true) != null) return false;
        if (bodyColliders == null) return false;
        foreach (Collider2D collider in bodyColliders)
            if (collider != null && !collider.isTrigger && collider.GetComponentInParent<Item>() == item &&
                collider is BoxCollider2D or CapsuleCollider2D or CircleCollider2D) return true;
        return false;
    }

    private void BeginPresentation()
    {
        if (presentationApplied) return;
        originalVisualRotation = visualRoot.localRotation;
        colliderShapes.Clear();
        var captured = new HashSet<Collider2D>();
        foreach (Collider2D collider in bodyColliders)
        {
            if (collider == null || collider.isTrigger || collider.GetComponentInParent<Item>() != item || !captured.Add(collider)) continue;
            RollColliderShape shape = RollColliderShape.Capture(collider);
            if (shape == null) continue;
            colliderShapes.Add(shape);
            shape.Apply(Mathf.Clamp(ColliderScale, .01f, 1f));
        }
        presentationApplied = true;
    }
    private void UpdatePresentation(float progress)
    {
        if (!presentationApplied || visualRoot == null) return;
        float sign = Direction.x < 0f ? 1f : -1f;
        visualRoot.localRotation = originalVisualRotation * Quaternion.Euler(0f, 0f, sign * 360f * Mathf.Clamp01(progress));
    }
    private void RestorePresentation()
    {
        if (!presentationApplied) return;
        foreach (RollColliderShape shape in colliderShapes) shape.Apply(1f);
        colliderShapes.Clear();
        if (visualRoot != null) visualRoot.localRotation = originalVisualRotation;
        presentationApplied = false;
    }

    private sealed class RollColliderShape
    {
        private Collider2D collider;
        private Vector2 size;
        private float radius;
        public static RollColliderShape Capture(Collider2D collider)
        {
            var shape = new RollColliderShape { collider = collider };
            switch (collider)
            {
                case BoxCollider2D box: shape.size = box.size; shape.radius = box.edgeRadius; break;
                case CapsuleCollider2D capsule: shape.size = capsule.size; break;
                case CircleCollider2D circle: shape.radius = circle.radius; break;
                default: return null;
            }
            return shape;
        }
        public void Apply(float multiplier)
        {
            switch (collider)
            {
                case BoxCollider2D box when box != null: box.size = size * multiplier; box.edgeRadius = radius * multiplier; break;
                case CapsuleCollider2D capsule when capsule != null: capsule.size = size * multiplier; break;
                case CircleCollider2D circle when circle != null: circle.radius = radius * multiplier; break;
            }
        }
    }
    #endregion
}
