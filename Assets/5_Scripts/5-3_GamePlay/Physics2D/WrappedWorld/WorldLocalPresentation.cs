using UnityEngine;

/// <summary>
/// 当前客户端的局部平面投影。逻辑坐标始终保持规范化；Transform/Physics2D 只选择离本地玩家最近的世界镜像。
/// </summary>
public static class WorldLocalPresentation
{
    private static Transform anchor;
    private static Rigidbody2D anchorBody;

    public static bool HasAnchor => anchor != null;

    public static Vector2 AnchorPosition => anchorBody != null
        ? anchorBody.position
        : anchor != null ? (Vector2)anchor.position : Vector2.zero;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        anchor = null;
        anchorBody = null;
    }

    /// <summary>本地玩家是唯一表现锚点；重复绑定同一 Transform 不产生额外工作。</summary>
    public static void SetAnchor(Transform target)
    {
        if (target == null || anchor == target)
            return;

        anchor = target;
        anchorBody = target.GetComponent<Rigidbody2D>();
    }

    public static void ClearAnchor(Transform target)
    {
        if (target == null || anchor != target)
            return;

        anchor = null;
        anchorBody = null;
    }

    /// <summary>把任意表现坐标还原成协议、存档和 WorldModel 使用的规范逻辑坐标。</summary>
    public static Vector2 ToLogical(Vector2 presentationPosition)
        => WorldTopologyRuntime.NormalizePosition(presentationPosition);

    public static Vector3 ToLogical(Vector3 presentationPosition)
        => WorldTopologyRuntime.NormalizePosition(presentationPosition);

    /// <summary>把规范逻辑坐标投影到本机锚点附近；无锚点时直接返回规范坐标。</summary>
    public static Vector2 ProjectPosition(Vector2 logicalPosition)
    {
        logicalPosition = WorldTopologyRuntime.NormalizePosition(logicalPosition);
        if (!HasAnchor || !WorldTopologyRuntime.TryGetActiveBounds(out WorldTopologyBounds bounds))
            return logicalPosition;

        return bounds.NearestImagePosition(AnchorPosition, logicalPosition);
    }

    public static Vector3 ProjectPosition(Vector3 logicalPosition)
    {
        Vector2 projected = ProjectPosition((Vector2)logicalPosition);
        return new Vector3(projected.x, projected.y, logicalPosition.z);
    }

    /// <summary>显式锚点版本供远端插值和测试使用，不依赖全局本地玩家引用。</summary>
    public static Vector2 ProjectPosition(Vector2 logicalPosition, Vector2 presentationAnchor)
    {
        logicalPosition = WorldTopologyRuntime.NormalizePosition(logicalPosition);
        return WorldTopologyRuntime.TryGetActiveBounds(out WorldTopologyBounds bounds)
            ? bounds.NearestImagePosition(presentationAnchor, logicalPosition)
            : logicalPosition;
    }
}
