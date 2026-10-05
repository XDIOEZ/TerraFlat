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

    #region 碰撞源解析
    public static Collider2D Resolve(Collider2D collider)
    {
        if (collider == null)
            return null;
        // 普通碰撞体没有镜像标记时直接返回，避免缺失组件诊断分配。
        return collider.TryGetComponent(out ColliderSource2D source) ? source.SourceCollider : collider;
    }
    #endregion
}
