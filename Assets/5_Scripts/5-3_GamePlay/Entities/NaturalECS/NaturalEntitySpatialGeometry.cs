using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FlatWorld.NaturalEntities
{
    #region 自然物几何冷编译

    /// <summary>定义只编译一次受击框与阻挡框，运行时只变换值，不重复解析 JSON。</summary>
    internal readonly struct NaturalEntitySpatialGeometry
    {
        private readonly Vector2 bodyOffset, bodySize, hitOffset, hitSize;
        private readonly Matrix4x4 hitTransform;
        private readonly bool tree;
        internal readonly bool BlocksMovement, HitColliderEnabled;

        private NaturalEntitySpatialGeometry(Vector2 bodyOffset, Vector2 bodySize, Vector2 hitOffset,
            Vector2 hitSize, Matrix4x4 hitTransform, bool blocksMovement, bool hitColliderEnabled, bool tree)
        {
            this.bodyOffset = bodyOffset; this.bodySize = bodySize;
            this.hitOffset = hitOffset; this.hitSize = hitSize; this.hitTransform = hitTransform;
            BlocksMovement = blocksMovement; HitColliderEnabled = hitColliderEnabled; this.tree = tree;
        }

        internal static NaturalEntitySpatialGeometry Compile(ItemColliderDefinitionDto collider, JObject health, bool tree)
        {
            JObject hit = health?["$collider2D"] as JObject;
            JObject transform = health?["$transform"] as JObject;
            return new NaturalEntitySpatialGeometry(collider?.Offset ?? Vector2.zero, collider?.Size ?? Vector2.one,
                hit?["offset"]?.ToObject<Vector2>() ?? collider?.Offset ?? Vector2.zero,
                hit?["size"]?.ToObject<Vector2>() ?? collider?.Size ?? Vector2.one,
                Matrix4x4.TRS(transform?["localPosition"]?.ToObject<Vector3>() ?? Vector3.zero,
                    Quaternion.Euler(transform?["localEulerAngles"]?.ToObject<Vector3>() ?? Vector3.zero),
                    transform?["localScale"]?.ToObject<Vector3>() ?? Vector3.one),
                collider?.Enabled != false && collider?.IsTrigger == false,
                hit?["enabled"]?.Value<bool>() != false, tree);
        }

        internal void Resolve(Matrix4x4 root, out Bounds body, out Bounds hit)
        {
            body = NaturalEntityEcsService.TransformBounds(root, bodyOffset, bodySize);
            hit = NaturalEntityEcsService.TransformBounds(root * hitTransform, hitOffset, hitSize);
            if (tree && BlocksMovement && HitColliderEnabled) hit.Encapsulate(body);
        }
    }

    #endregion
}
