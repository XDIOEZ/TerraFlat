using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace FlatWorld.AIECS
{
    /// <summary>
    /// ECS 主体当前动画帧的太阳投影，4096 只合为一批，固定 16 位索引。
    /// 只读公共 Shader 契约，不反向依赖 GamePlay；关闭/夜晚不采样投影帧、不上传网格。
    /// </summary>
    public sealed class AiecsSunShadowRenderer : IDisposable
    {
        #region 生命周期与批次入口

        private const int MaxSprites = 4096;
        private static readonly int GlobalId = Shader.PropertyToID("_WorldSunShadow");
        private readonly List<Batch> batches = new();
        private readonly Scene scene;
        private readonly Texture atlas;
        private Material material;
        private Vector4 sunlight;
        public int ShadowCount { get; private set; }
        public int BatchCount { get; private set; }
        public bool Active => sunlight.w > 0.001f;
        public float CullingMargin => Active ? sunlight.z : 0f;

        /// <summary>只保存共享资源引用；关闭功能时不创建网格或 Renderer。</summary>
        public AiecsSunShadowRenderer(Scene scene, Texture atlas) { this.scene = scene; this.atlas = atlas; }

        /// <summary>每次 Draw 只读一次太阳全局参数，保持与普通实体同一时间和偏好。</summary>
        public void Begin()
        {
            sunlight = Shader.GetGlobalVector(GlobalId);
            ShadowCount = 0;
            BatchCount = 0;
        }

        /// <summary>追加真实动画矩形和图集 UV；高度倍率为 0 时可按物种禁用。</summary>
        public void Append(AiecsPrototypeActor actor, AiecsActorVisual definition, AiecsAnimationFrame frame,
            AiecsSpriteGeometry sprite, bool mirror, float alpha, float footY, float heightMultiplier)
        {
            if (!Active || alpha <= 0.001f || heightMultiplier <= 0f) return;
            int index = ShadowCount / MaxSprites;
            if (index == batches.Count)
            {
                if (material == null) material = Resources.Load<Material>("SunShadows/SunShadowProjection");
                if (material == null) throw new MissingReferenceException("缺少 SunShadows/SunShadowProjection 材质。");
                batches.Add(new Batch(scene, material, atlas));
            }
            Batch batch = batches[index];
            if (ShadowCount % MaxSprites == 0) { batch.Begin(); BatchCount++; }
            batch.Append(actor, definition, frame, sprite, mirror, alpha, footY, heightMultiplier, sunlight);
            ShadowCount++;
        }

        /// <summary>只上传非空批次；关闭与空帧立即停用上一帧全部 Renderer。</summary>
        public void End()
        {
            for (int i = 0; i < BatchCount; i++) batches[i].Submit();
            for (int i = BatchCount; i < batches.Count; i++) batches[i].Hide();
        }

        /// <summary>相机失效或世界暂时不可见时清空可见状态。</summary>
        public void Hide() { ShadowCount = 0; BatchCount = 0; sunlight = default; End(); }

        /// <summary>释放本表现所有者创建的网格，不销毁共享材质及图集。</summary>
        public void Dispose()
        {
            foreach (Batch batch in batches) batch.Dispose();
            batches.Clear(); ShadowCount = 0; BatchCount = 0;
        }

        #endregion

        #region 固定缓冲与 Shader 顶点契约

        [StructLayout(LayoutKind.Sequential)]
        private struct Vertex
        {
            public Vector3 Position; // 未投影的世界位置。
            public Color32 Color; // 主体淡出透明度。
            public Vector2 UV; // 当前帧图集 UV。
            public Vector4 Caster; // 脚底 Y、高度倍率、最大高度、透明度。
        }

        private sealed class Batch : IDisposable
        {
            private readonly GameObject root;
            private readonly Mesh mesh;
            private readonly MeshRenderer renderer;
            private readonly Vertex[] vertices = new Vertex[MaxSprites * 4];
            private int count;
            private Vector3 min, max;
            private const MeshUpdateFlags Flags = MeshUpdateFlags.DontRecalculateBounds;

            /// <summary>分配固定索引和复用顶点缓冲，所有批次共用普通实体的太阳材质。</summary>
            public Batch(Scene scene, Material material, Texture atlas)
            {
                int layer = LayerMask.NameToLayer("AIECSRuntime");
                if (layer < 0) throw new InvalidOperationException("缺少 AIECSRuntime Layer。");
                root = new GameObject("AIECS 太阳投影批次") { hideFlags = HideFlags.DontSave, layer = layer };
                SceneManager.MoveGameObjectToScene(root, scene);
                mesh = new Mesh { name = "AIECS 太阳投影网格", hideFlags = HideFlags.DontSave };
                mesh.MarkDynamic();
                mesh.SetVertexBufferParams(vertices.Length,
                    new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                    new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
                    new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
                    new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4));
                ushort[] indices = new ushort[MaxSprites * 6];
                for (int i = 0; i < MaxSprites; i++)
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
                renderer.sortingLayerName = "Tilemap";
                renderer.sortingOrder = 3;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                var properties = new MaterialPropertyBlock();
                properties.SetTexture("_MainTex", atlas);
                properties.SetFloat("_SunShadowBatched", 1f);
                renderer.SetPropertyBlock(properties);
                renderer.enabled = false;
            }

            /// <summary>开始复用已有缓冲。</summary>
            public void Begin()
            {
                count = 0;
                min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, 0f);
                max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, 0f);
            }

            /// <summary>复制当前帧的四角，包围盒使用与 Shader 一致的投影公式。</summary>
            public void Append(AiecsPrototypeActor actor, AiecsActorVisual definition, AiecsAnimationFrame frame,
                AiecsSpriteGeometry sprite, bool mirror, float alpha, float footY, float scale, Vector4 sun)
            {
                int offset = count * 4;
                float height = 0.01f;
                Color32 color = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f));
                for (int corner = 0; corner < 4; corner++)
                {
                    bool right = corner == 1 || corner == 2, top = corner >= 2;
                    Vector3 local = new Vector3(right ? sprite.LocalRect.xMax : sprite.LocalRect.xMin,
                        top ? sprite.LocalRect.yMax : sprite.LocalRect.yMin, 0f);
                    Vector3 point = AiecsRenderBatch.TransformPoint(local, actor, definition, frame, mirror);
                    height = Mathf.Max(height, point.y - footY);
                    vertices[offset + corner] = new Vertex { Position = point, Color = color,
                        UV = new Vector2(right ? sprite.AtlasRect.xMax : sprite.AtlasRect.xMin,
                            top ? sprite.AtlasRect.yMax : sprite.AtlasRect.yMin) };
                }
                Vector4 caster = new Vector4(footY, scale, height, 1f);
                Vector2 displacement = new Vector2(sun.x, sun.y) * scale;
                displacement *= Mathf.Min(1f, sun.z / Mathf.Max(0.0001f, displacement.magnitude * height));
                for (int corner = 0; corner < 4; corner++)
                {
                    vertices[offset + corner].Caster = caster;
                    Vector3 point = vertices[offset + corner].Position;
                    Vector2 projected = new Vector2(point.x, footY) + displacement * Mathf.Max(0f, point.y - footY);
                    min = Vector3.Min(min, new Vector3(projected.x, projected.y, 0f));
                    max = Vector3.Max(max, new Vector3(projected.x, projected.y, 0f));
                }
                count++;
            }

            /// <summary>固定索引只初始化一次，本帧只上传已用顶点。</summary>
            public void Submit()
            {
                if (renderer == null) return;
                Bounds bounds = new Bounds((min + max) * 0.5f, max - min + new Vector3(0.01f, 0.01f, 0.1f));
                mesh.SetVertexBufferData(vertices, 0, 0, count * 4, 0, Flags);
                mesh.SetSubMesh(0, new SubMeshDescriptor(0, count * 6, MeshTopology.Triangles)
                    { bounds = bounds, vertexCount = count * 4 }, Flags);
                mesh.bounds = bounds;
                renderer.enabled = count > 0;
            }

            /// <summary>不再上传停用批次。</summary>
            public void Hide() { if (renderer != null && renderer.enabled) renderer.enabled = false; }

            /// <summary>兼容场景已卸载的回收顺序。</summary>
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
