using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace FlatWorld.AIECS
{
    /// <summary>
    /// 一个透明顺序连续区间的复用网格；不跨旧 Renderer 合批，不为每只生物创建 GameObject。
    /// P1 先验证原生 Renderer2D 的排序和光照接入，CPU 顶点上传成本仍需后续测量。
    /// </summary>
    internal sealed class AiecsRenderBatch : IDisposable
    {
        // 每批最多 4096 张图片，控制单次上传和包围盒大小。
        internal const int MaxSprites = 4096;
        // 复用的绘制资源与暂存列表。
        private readonly GameObject root;
        private readonly Mesh mesh;
        private readonly MeshRenderer renderer;
        private readonly List<Vector3> positions = new();
        private readonly List<Vector2> uvs = new();
        private readonly List<Color> colors = new();
        private readonly List<Vector4> water = new();
        private readonly List<int> indices = new();

        /// <summary>创建本原型拥有的原生 Lit Renderer，并显式关联原型所属场景。</summary>
        internal AiecsRenderBatch(Scene scene, Material material)
        {
            root = new GameObject("AIECS 连续绘制批次") { hideFlags = HideFlags.DontSave };
            SceneManager.MoveGameObjectToScene(root, scene);
            mesh = new Mesh { name = "AIECS 复用精灵网格", indexFormat = IndexFormat.UInt32 };
            mesh.MarkDynamic();
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        /// <summary>清空本批顶点列表并保留容量。</summary>
        internal void Begin()
        {
            positions.Clear();
            uvs.Clear();
            colors.Clear();
            water.Clear();
            indices.Clear();
        }

        /// <summary>追加一只生物的四边形，保持帧尺寸、Pivot、镜像与原始局部姿态。</summary>
        internal void Append(AiecsPrototypeActor actor, AiecsActorVisual definition,
            AiecsAnimationFrame frame, AiecsSpriteGeometry sprite, float surface, float tint, bool mirror = false, Color? color = null)
        {
            int offset = positions.Count;
            Rect rect = sprite.LocalRect;
            Rect uv = sprite.AtlasRect;
            float height = Mathf.Max(0.0001f, definition.BodyYRange.y - definition.BodyYRange.x);
            var parameters = new Vector4(actor.WaterBlend,
                actor.Position.y + definition.BodyYRange.x + height * surface, height, tint);
            for (int corner = 0; corner < 4; corner++)
            {
                bool right = corner == 1 || corner == 2;
                bool top = corner >= 2;
                Vector3 local = new Vector3(right ? rect.xMax : rect.xMin, top ? rect.yMax : rect.yMin, 0f);
                positions.Add(TransformPoint(local, actor, definition, frame, mirror));
                uvs.Add(new Vector2(right ? uv.xMax : uv.xMin, top ? uv.yMax : uv.yMin));
                colors.Add(color ?? definition.Color);
                water.Add(parameters);
            }
            indices.Add(offset); indices.Add(offset + 1); indices.Add(offset + 2);
            indices.Add(offset); indices.Add(offset + 2); indices.Add(offset + 3);
        }

        /// <summary>将 Sprite 局部顶点变换到世界，镜像只作用于图片自身。</summary>
        internal static Vector3 TransformPoint(Vector3 point, AiecsPrototypeActor actor,
            AiecsActorVisual definition, AiecsAnimationFrame frame, bool mirror = false)
        {
            point.x *= definition.FlipX ? -1f : 1f;
            point.y *= definition.FlipY ? -1f : 1f;
            point = Quaternion.Euler(0f, 0f, frame.Rotation) * Vector3.Scale(point, frame.Scale);
            point += frame.Position;
            if (mirror) point.x = -point.x;
            return point + new Vector3(actor.Position.x, actor.Position.y, 0f);
        }

        /// <summary>提交同一图层内的精确序号，保留原生 Renderer2D 的颜色及法线绘制上下文。</summary>
        internal int Submit(int layer, int order)
        {
            mesh.Clear();
            mesh.SetVertices(positions);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.SetUVs(1, water);
            mesh.SetTriangles(indices, 0, true);
            renderer.sortingLayerID = layer;
            renderer.sortingOrder = order;
            renderer.enabled = positions.Count != 0;
            return positions.Count * (12 + 8 + 16 + 16) + indices.Count * 4;
        }

        /// <summary>隐藏本帧没有使用的池化批次。</summary>
        internal void Hide()
        {
            renderer.enabled = false;
        }

        /// <summary>释放本原型创建的网格与节点，不修改共享材质。</summary>
        public void Dispose()
        {
            // 世界场景可能先卸载批次节点，网格仍由本所有者释放。
            if (renderer != null) renderer.enabled = false;
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(mesh);
                UnityEngine.Object.Destroy(root);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
