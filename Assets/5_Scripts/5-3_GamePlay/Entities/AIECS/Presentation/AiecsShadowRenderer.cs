using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace FlatWorld.AIECS
{
    /// <summary>
    /// 只读脚底假阴影：每只使用四个顶点、两个三角形，每 4096 只共用一个动态网格。
    /// 固定索引只上传一次；顶点缓冲复用，不创建逐实体对象、碰撞体、法线或真实投影。
    /// 使用 Default/-1000 与旧 ActorShadowManager 一致；仅绘制调用方筛选出的可见陆地实体。
    /// </summary>
    public sealed class AiecsShadowRenderer : IDisposable
    {
        #region 共享配置与生命周期
        public const int MaxShadowsPerBatch = 4096; // 16384 顶点，兼容 16 位索引。
        private static readonly ProfilerMarker BuildMarker = new ProfilerMarker("AIECS.BlobShadows.Upload");
        private readonly List<Batch> batches = new List<Batch>();
        private readonly Scene scene;
        private readonly Material material;
        public int ShadowCount { get; private set; } // 本帧真正提交的阴影数。
        public int BatchCount { get; private set; } // 本帧阴影批次，不与主体批次混淆。

        /// <summary>加载构建中保留的共享材质，不实例化每物种材质。</summary>
        public AiecsShadowRenderer(Scene scene)
        {
            this.scene = scene;
            material = Resources.Load<Material>("AIECS/ActorBlobShadow");
            if (material == null || material.shader == null)
                throw new InvalidOperationException("缺少 AIECS/ActorBlobShadow 阴影材质。");
        }

        /// <summary>开始新帧；上一帧未再使用的批次在 End 中关闭。</summary>
        public void Begin() { ShadowCount = 0; BatchCount = 0; }

        /// <summary>追加固定脚底椭圆；尺寸和偏移由物种共享，镜像只翻转横向偏移。</summary>
        public void Append(Vector2 position, Vector4 footprint, bool mirror, float opacity)
        {
            if (opacity <= 0.001f || footprint.z <= 0f || footprint.w <= 0f) return;
            int index = ShadowCount / MaxShadowsPerBatch;
            if (index == batches.Count) batches.Add(new Batch(scene, material));
            Batch batch = batches[index];
            if (ShadowCount % MaxShadowsPerBatch == 0) { batch.Begin(); BatchCount++; }
            position += new Vector2(mirror ? -footprint.x : footprint.x, footprint.y);
            batch.Append(position, footprint.z, footprint.w, Mathf.Clamp01(opacity));
            ShadowCount++;
        }

        /// <summary>每批只上传已用顶点；空帧、夜晚和开关关闭时不留下旧阴影。</summary>
        public void End()
        {
            using (BuildMarker.Auto())
            {
                for (int i = 0; i < BatchCount; i++) batches[i].Submit();
                for (int i = BatchCount; i < batches.Count; i++) batches[i].Hide();
            }
        }

        /// <summary>显式关闭所有显示；相机丢失、世界切换也必须调用。</summary>
        public void Hide() { Begin(); End(); }

        /// <summary>释放本所有者创建的网格和节点，共享材质仍由资源系统管理。</summary>
        public void Dispose()
        {
            foreach (Batch batch in batches) batch.Dispose();
            batches.Clear(); ShadowCount = 0; BatchCount = 0;
        }
        #endregion

        #region 固定脚底几何
        /// <summary>统一昼夜、主体淡出与水态规则；只读 Display，不向模拟回写。</summary>
        public static float ResolveOpacity(float sceneOpacity, float spriteOpacity, float waterDepth, float waterBlend)
        {
            if (waterDepth > 0.001f || waterBlend > 0.001f) return 0f;
            return Mathf.Clamp01(sceneOpacity) * Mathf.Clamp01(spriteOpacity);
        }

        /// <summary>冷路径从待机帧的非透明范围计算固定脚底；不随攻击、奔跑切帧伸缩。</summary>
        public static Vector4 MeasureFootprint(AiecsActorVisual definition, AiecsSpriteGeometry sprite,
            AiecsAnimationFrame frame)
        {
            Rect rect = sprite.VisibleRect.width > 0f && sprite.VisibleRect.height > 0f
                ? sprite.VisibleRect : sprite.LocalRect;
            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (int corner = 0; corner < 4; corner++)
            {
                Vector3 local = new Vector3((corner & 1) == 0 ? rect.xMin : rect.xMax,
                    corner < 2 ? rect.yMin : rect.yMax, 0f);
                Vector2 point = AiecsRenderBatch.TransformPoint(local, default, definition, frame);
                min = Vector2.Min(min, point); max = Vector2.Max(max, point);
            }
            float width = Mathf.Clamp(Mathf.Max((max.x - min.x) * 0.9f, (max.y - min.y) * 0.55f), 0.18f, 2.5f);
            return new Vector4((min.x + max.x) * 0.5f, min.y + 0.02f, width, width * 0.34f);
        }
        #endregion

        #region 复用动态网格
        [StructLayout(LayoutKind.Sequential)]
        private struct Vertex
        {
            public Vector3 Position; // 位置。
            public Color32 Color; // 透明度；UNorm8 避免每顶点传四个 float。
            public Vector2 UV; // 解析椭圆的 [-1,1] 坐标。
        }

        private sealed class Batch : IDisposable
        {
            private readonly GameObject root;
            private readonly Mesh mesh;
            private readonly MeshRenderer renderer;
            private readonly Vertex[] vertices = new Vertex[MaxShadowsPerBatch * 4];
            private int count;
            private Vector2 min, max;
            private const MeshUpdateFlags UpdateFlags = MeshUpdateFlags.DontRecalculateBounds;

            /// <summary>一次分配顶点和固定索引；以后只上传当前使用的顶点区间。</summary>
            public Batch(Scene scene, Material material)
            {
                int layer = LayerMask.NameToLayer("AIECSRuntime");
                if (layer < 0) throw new InvalidOperationException("缺少 AIECSRuntime Layer。");
                root = new GameObject("AIECS 脚底阴影批次") { hideFlags = HideFlags.DontSave, layer = layer };
                SceneManager.MoveGameObjectToScene(root, scene);
                mesh = new Mesh { name = "AIECS 复用脚底阴影", hideFlags = HideFlags.DontSave };
                mesh.MarkDynamic();
                mesh.SetVertexBufferParams(vertices.Length,
                    new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                    new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
                    new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2));
                var indices = new ushort[MaxShadowsPerBatch * 6];
                for (int i = 0; i < MaxShadowsPerBatch; i++)
                {
                    int v = i * 4, t = i * 6;
                    indices[t] = (ushort)v; indices[t + 1] = (ushort)(v + 1); indices[t + 2] = (ushort)(v + 2);
                    indices[t + 3] = (ushort)v; indices[t + 4] = (ushort)(v + 2); indices[t + 5] = (ushort)(v + 3);
                }
                mesh.SetIndexBufferParams(indices.Length, IndexFormat.UInt16);
                mesh.SetIndexBufferData(indices, 0, 0, indices.Length);
                mesh.subMeshCount = 1;
                root.AddComponent<MeshFilter>().sharedMesh = mesh;
                renderer = root.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.sortingLayerName = "Default";
                renderer.sortingOrder = -1000;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                renderer.enabled = false;
            }

            /// <summary>重置使用区间与包围盒，不释放缓冲。</summary>
            public void Begin()
            {
                count = 0;
                min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
                max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            }

            /// <summary>写入一个椭圆的四个顶点，同时增量计算世界包围盒。</summary>
            public void Append(Vector2 center, float width, float height, float opacity)
            {
                Vector2 half = new Vector2(width, height) * 0.5f;
                min = Vector2.Min(min, center - half); max = Vector2.Max(max, center + half);
                Color32 color = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(opacity * 255f));
                for (int corner = 0; corner < 4; corner++)
                {
                    float x = corner == 1 || corner == 2 ? 1f : -1f;
                    float y = corner >= 2 ? 1f : -1f;
                    vertices[count * 4 + corner] = new Vertex
                    {
                        Position = new Vector3(center.x + x * half.x, center.y + y * half.y, 0f),
                        UV = new Vector2(x, y), Color = color
                    };
                }
                count++;
            }

            /// <summary>提交当前区间，不重建索引、不清空网格、不生成法线。</summary>
            public void Submit()
            {
                if (renderer == null) return;
                var bounds = new Bounds((min + max) * 0.5f, new Vector3(max.x - min.x, max.y - min.y, 0.1f));
                mesh.SetVertexBufferData(vertices, 0, 0, count * 4, 0, UpdateFlags);
                mesh.SetSubMesh(0, new SubMeshDescriptor(0, count * 6, MeshTopology.Triangles)
                    { bounds = bounds, vertexCount = count * 4 }, UpdateFlags);
                mesh.bounds = bounds;
                renderer.enabled = count > 0;
            }

            /// <summary>隐藏已分配但本帧不再需要的批次。</summary>
            public void Hide() { if (renderer != null) renderer.enabled = false; }

            /// <summary>兼容世界先卸载节点以及 Editor 非 Play 验证。</summary>
            public void Dispose()
            {
                Hide();
                if (Application.isPlaying) { UnityEngine.Object.Destroy(mesh); UnityEngine.Object.Destroy(root); }
                else { UnityEngine.Object.DestroyImmediate(mesh); UnityEngine.Object.DestroyImmediate(root); }
            }
        }
        #endregion
    }
}
