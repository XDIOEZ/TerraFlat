using UnityEngine;

/// <summary>
/// 碰撞镜像到真实 Collider 的物理归属标记；不依赖 Item、地图或活动世界。
/// 镜像不是独立实体，源被销毁后不能把镜像当成新的目标。
/// </summary>
[DisallowMultipleComponent]
public sealed class ColliderSource2D : MonoBehaviour
{
    #region 镜像来源绑定
    public Collider2D SourceCollider { get; private set; }
    public Vector2 ImageOffset { get; private set; }

    internal void Bind(Collider2D source, Vector2 imageOffset)
    {
        SourceCollider = source;
        ImageOffset = imageOffset;
        enabled = true;
    }

    internal void ClearBinding()
    {
        SourceCollider = null;
        ImageOffset = Vector2.zero;
        enabled = false;
    }
    #endregion

    #region 碰撞源解析
    public static Collider2D Resolve(Collider2D collider)
    {
        Collider2D slow = collider;
        bool advanceSlow = false;
        // 多层镜像逐级还原，已释放的源和循环引用都不能变成新的命中目标。
        while (collider != null && collider.TryGetComponent(out ColliderSource2D source) && source.enabled)
        {
            collider = source.SourceCollider;
            if (advanceSlow && slow != null && slow.TryGetComponent(out ColliderSource2D slowSource) && slowSource.enabled)
                slow = slowSource.SourceCollider;
            advanceSlow = !advanceSlow;
            if (collider != null && collider == slow) return null;
        }
        return collider;
    }
    #endregion
}
