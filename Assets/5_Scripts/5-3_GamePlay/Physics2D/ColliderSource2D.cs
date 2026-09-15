using UnityEngine;

/// <summary>
/// 碰撞镜像到真实 Collider 的物理归属标记；不依赖 Item、地图或活动世界。
/// 镜像不是独立实体，源被销毁后不能把镜像当成新的目标。
/// </summary>
[DisallowMultipleComponent]
public sealed class ColliderSource2D : MonoBehaviour
{
    public Collider2D SourceCollider { get; private set; }
    public Vector2 ImageOffset { get; private set; }

    internal void Bind(Collider2D source, Vector2 imageOffset)
    {
        SourceCollider = source;
        ImageOffset = imageOffset;
    }

    public static Collider2D Resolve(Collider2D collider)
    {
        if (collider == null)
            return null;
        ColliderSource2D source = collider.GetComponent<ColliderSource2D>();
        return source != null ? source.SourceCollider : collider;
    }
}
