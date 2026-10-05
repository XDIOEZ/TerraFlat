using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 从 Sprite 可见轮廓计算世界脚底；太阳投影与接触阴影共用同一落地点规则。
/// 脚点向贴图内预留少量重叠，避免透明底边和旧配置把阴影推离主体。
/// </summary>
public static class ShadowFootprintResolver
{
    #region 可见轮廓缓存

    private static readonly Dictionary<Sprite, Bounds> LocalBounds = new();
    private static readonly List<Vector2> ShapePoints = new();

    static ShadowFootprintResolver() => SharedSpriteMeshCache.Clearing += ClearCache;

    /// <summary>资源会话卸载时释放 Sprite 引用。</summary>
    private static void ClearCache() => LocalBounds.Clear();

    /// <summary>不读取纹理像素，使用导入的物理轮廓和网格边界求可见范围。</summary>
    public static Bounds MeasureVisibleWorldBounds(SpriteRenderer renderer)
    {
        Sprite sprite = renderer.sprite;
        if (renderer.drawMode != SpriteDrawMode.Simple)
            return renderer.bounds;

        return MeasureVisibleWorldBounds(sprite, renderer.transform.localToWorldMatrix,
            renderer.flipX, renderer.flipY);
    }

    /// <summary>数据节点无需创建 SpriteRenderer，也能按相同轮廓得到世界包围盒。</summary>
    public static Bounds MeasureVisibleWorldBounds(Sprite sprite, Matrix4x4 localToWorld,
        bool flipX = false, bool flipY = false)
    {
        if (sprite == null) return default;

        if (!LocalBounds.TryGetValue(sprite, out Bounds local))
        {
            local = MeasureLocalBounds(sprite);
            LocalBounds.Add(sprite, local);
        }

        Bounds world = default;
        for (int corner = 0; corner < 4; corner++)
        {
            Vector3 point = new Vector3((corner & 1) == 0 ? local.min.x : local.max.x,
                corner < 2 ? local.min.y : local.max.y, 0f);
            if (flipX) point.x = -point.x;
            if (flipY) point.y = -point.y;
            point = localToWorld.MultiplyPoint3x4(point);
            if (corner == 0) world = new Bounds(point, Vector3.zero);
            else world.Encapsulate(point);
        }
        return world;
    }

    /// <summary>优先使用 Sprite 导入轮廓，避免不可读纹理退回透明整帧。</summary>
    private static Bounds MeasureLocalBounds(Sprite sprite)
    {
        Bounds local = default;
        bool hasPoint = false;
        for (int shape = 0; shape < sprite.GetPhysicsShapeCount(); shape++)
        {
            ShapePoints.Clear();
            sprite.GetPhysicsShape(shape, ShapePoints);
            for (int point = 0; point < ShapePoints.Count; point++)
            {
                if (!hasPoint) { local = new Bounds(ShapePoints[point], Vector3.zero); hasPoint = true; }
                else local.Encapsulate(ShapePoints[point]);
            }
        }

        if (hasPoint) return local;
        Vector2[] vertices = sprite.vertices;
        for (int i = 0; i < vertices.Length; i++)
        {
            if (!hasPoint) { local = new Bounds(vertices[i], Vector3.zero); hasPoint = true; }
            else local.Encapsulate(vertices[i]);
        }
        return hasPoint ? local : sprite.bounds;
    }

    #endregion

    #region 统一脚点

    /// <summary>允许配置覆盖脚点，但不允许其低于可见底边的内缩位置。</summary>
    public static Vector3 ResolveFoot(Item item, Bounds groundedBounds, Vector2? localFoot,
        float footOffset = 0f, float? footOverlap = null)
    {
        Vector3 foot = localFoot.HasValue
            ? item.transform.TransformPoint(new Vector3(localFoot.Value.x, localFoot.Value.y, 0f))
            : new Vector3(groundedBounds.center.x, groundedBounds.min.y, groundedBounds.center.z);
        return ClampFoot(foot, groundedBounds, footOffset, footOverlap);
    }

    /// <summary>数据机械使用建造矩阵和同一可见底边约束定位落点。</summary>
    public static Vector3 ResolveFoot(Matrix4x4 localToWorld, Bounds groundedBounds,
        Vector2? localFoot, float footOffset = 0f, float? footOverlap = null)
    {
        Vector3 foot = localFoot.HasValue
            ? localToWorld.MultiplyPoint3x4(new Vector3(localFoot.Value.x, localFoot.Value.y, 0f))
            : new Vector3(groundedBounds.center.x, groundedBounds.min.y, groundedBounds.center.z);
        return ClampFoot(foot, groundedBounds, footOffset, footOverlap);
    }

    /// <summary>统一限制自定义脚点，避免投影与主体可见底边脱节。</summary>
    private static Vector3 ClampFoot(Vector3 foot, Bounds groundedBounds, float footOffset,
        float? footOverlap)
    {
        WorldRenderingConfig.ContactShadow settings = WorldRenderingConfigCatalog.Default.shadows.contact;
        float overlap = footOverlap.HasValue
            ? Mathf.Clamp(footOverlap.Value, 0f, groundedBounds.size.y * 0.5f)
            : Mathf.Min(settings.maximumFootOverlap, groundedBounds.size.y * settings.footOverlapRatio);
        float sideInset = Mathf.Min(settings.maximumFootOverlap,
            groundedBounds.size.x * settings.footOverlapRatio);
        foot.x = Mathf.Clamp(foot.x, groundedBounds.min.x + sideInset,
            groundedBounds.max.x - sideInset);
        foot.y = Mathf.Max(foot.y + footOffset, groundedBounds.min.y + overlap);
        return foot;
    }

    #endregion
}
