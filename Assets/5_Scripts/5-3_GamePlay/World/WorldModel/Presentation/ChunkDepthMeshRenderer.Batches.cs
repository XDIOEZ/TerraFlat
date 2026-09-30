using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

internal sealed partial class ChunkDepthMeshRenderer
{
    #region 顶点与增量网格
    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex
    {
        public Vector3 Position;
        public Color32 Tint;
        public Vector2 Uv;
        public Vector4 LocalClip;
        public Vector4 Basis;
        public Vector4 Origin;
        public Vector4 Animation;
        public Vector4 Region;
        public Vector4 Effects;
        public Vector4 State;
    }

    private const MeshUpdateFlags UploadFlags = MeshUpdateFlags.DontRecalculateBounds |
        MeshUpdateFlags.DontValidateIndices;
    private static readonly VertexAttributeDescriptor[] Layout =
    {
        new(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
        new(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
        new(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
        new(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4),
        new(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 4),
        new(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 4),
        new(VertexAttribute.TexCoord4, VertexAttributeFormat.Float32, 4),
        new(VertexAttribute.TexCoord5, VertexAttributeFormat.Float32, 4),
        new(VertexAttribute.TexCoord6, VertexAttributeFormat.Float32, 4),
        new(VertexAttribute.TexCoord7, VertexAttributeFormat.Float32, 4)
    };

    private sealed class Group : IDisposable
    {
        internal readonly Row Row;
        internal readonly GroupKey Key;
        internal readonly List<Entry> Entries = new();
        internal bool TopologyDirty = true;
        private readonly Transform root;
        private readonly Mesh mesh;
        private readonly MeshRenderer renderer;
        private readonly MaterialLease material;
        private Vertex[] vertices = Array.Empty<Vertex>();
        private uint[] indices = Array.Empty<uint>();
        private int vertexCount, indexCount;
        private bool disposed;

        internal Group(Row row, GroupKey key)
        {
            Row = row; Key = key;
            material = MaterialLease.Acquire(key);
            try
            {
                root = CreateNode("DepthMesh_" + key.Order + "_" + key.Texture.name, row.Root);
                mesh = new Mesh { name = root.name, hideFlags = HideFlags.HideAndDontSave };
                mesh.MarkDynamic();
                root.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                renderer = root.gameObject.AddComponent<MeshRenderer>();
                renderer.sortingLayerID = row.Layer;
                renderer.sortingOrder = key.Order;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                renderer.enabled = false;
                renderer.sharedMaterial = material.Material;
            }
            catch
            {
                DestroyObject(root != null ? root.gameObject : null);
                DestroyObject(mesh);
                material.Dispose();
                throw;
            }
        }

        /// <summary>增删部件才重排索引；换色、成长和受击只合并上传本组的脏顶点区间。</summary>
        internal void Flush()
        {
            if (disposed || renderer == null) return;
            bool rebuild = TopologyDirty;
            if (rebuild)
            {
                Entries.Sort(CompareEntries);
                vertexCount = indexCount = 0;
                foreach (Entry entry in Entries)
                {
                    entry.VertexStart = vertexCount;
                    vertexCount += entry.Geometry.Vertices.Length;
                    indexCount += entry.Geometry.Triangles.Length;
                    entry.Dirty = true;
                }
                if (vertexCount > vertices.Length)
                {
                    Array.Resize(ref vertices, Mathf.NextPowerOfTwo(Mathf.Max(16, vertexCount)));
                    mesh.SetVertexBufferParams(vertices.Length, Layout);
                }
                if (indexCount > indices.Length)
                {
                    Array.Resize(ref indices, Mathf.NextPowerOfTwo(Mathf.Max(24, indexCount)));
                    mesh.SetIndexBufferParams(indices.Length, IndexFormat.UInt32);
                }
                int cursor = 0;
                foreach (Entry entry in Entries)
                    foreach (int triangle in entry.Geometry.Triangles)
                        indices[cursor++] = (uint)(entry.VertexStart + triangle);
                mesh.SetIndexBufferData(indices, 0, 0, indexCount, UploadFlags);
            }
            int first = vertexCount, last = 0;
            Bounds bounds = default;
            bool hasBounds = false;
            foreach (Entry entry in Entries)
            {
                if (entry.Dirty)
                {
                    WriteEntry(entry);
                    first = Mathf.Min(first, entry.VertexStart);
                    last = Mathf.Max(last, entry.VertexStart + entry.Geometry.Vertices.Length);
                    entry.Dirty = false;
                }
                if (!hasBounds) { bounds = entry.Bounds; hasBounds = true; }
                else bounds.Encapsulate(entry.Bounds);
            }
            if (last > first) mesh.SetVertexBufferData(vertices, first, first, last - first, 0, UploadFlags);
            if (rebuild)
            {
                mesh.subMeshCount = 1;
                mesh.SetSubMesh(0, new SubMeshDescriptor(0, indexCount)
                    { vertexCount = vertexCount, bounds = bounds }, UploadFlags);
            }
            mesh.bounds = bounds;
            renderer.enabled = indexCount > 0;
            TopologyDirty = false;
        }

        private static int CompareEntries(Entry left, Entry right)
        {
            int result = left.Visual.Matrix.m03.CompareTo(right.Visual.Matrix.m03);
            if (result != 0) return result;
            result = left.Key.Domain.CompareTo(right.Key.Domain);
            if (result != 0) return result;
            result = left.Key.Entity.CompareTo(right.Key.Entity);
            return result != 0 ? result : left.Key.Part.CompareTo(right.Key.Part);
        }

        private void WriteEntry(Entry entry)
        {
            Visual visual = entry.Visual;
            Matrix4x4 matrix = visual.Matrix;
            Vector4 basis = new(matrix.m00, matrix.m10, matrix.m01, matrix.m11);
            Vector4 origin = new(matrix.m03, matrix.m13, matrix.m23, 0f);
            Bounds spriteBounds = entry.Geometry.Bounds;
            Vector4 effects = new(spriteBounds.min.y, spriteBounds.max.y,
                visual.Occluder ? 1f : 0f, visual.Highlighted ? 1f : 0f);
            Bounds bounds = default;
            for (int i = 0; i < entry.Geometry.Vertices.Length; i++)
            {
                Vector3 local = entry.Geometry.Vertices[i];
                Vector3 position = matrix.MultiplyPoint3x4(local);
                vertices[entry.VertexStart + i] = new Vertex
                {
                    Position = position, Tint = visual.Tint, Uv = entry.Geometry.Uv[i],
                    LocalClip = new Vector4(local.x, local.y, visual.Crop.x, visual.Crop.w),
                    Basis = basis, Origin = origin, Animation = visual.Animation,
                    Region = visual.Region, Effects = effects, State = visual.State
                };
                if (i == 0) bounds = new Bounds(position, Vector3.zero);
                else bounds.Encapsulate(position);
            }
            // GPU 旋转/往复/风摆的最大外扩参与 CPU 裁剪，不能以扁平网格的静止范围剔除。
            int mode = Mathf.RoundToInt(visual.Animation.x);
            if (mode == 1)
            {
                Vector2 reach = new(Mathf.Max(Mathf.Abs(spriteBounds.min.x), Mathf.Abs(spriteBounds.max.x)),
                    Mathf.Max(Mathf.Abs(spriteBounds.min.y), Mathf.Abs(spriteBounds.max.y)));
                float radius = reach.magnitude;
                float x = radius * Mathf.Sqrt(matrix.m00 * matrix.m00 + matrix.m01 * matrix.m01);
                float y = radius * Mathf.Sqrt(matrix.m10 * matrix.m10 + matrix.m11 * matrix.m11);
                bounds = new Bounds(new Vector3(matrix.m03, matrix.m13, matrix.m23), new Vector3(x * 2f, y * 2f, .1f));
            }
            else if (mode == 3)
            {
                Bounds moved = bounds;
                moved.center -= new Vector3(matrix.m01, matrix.m11, 0f) * visual.Animation.w;
                bounds.Encapsulate(moved);
            }
            else if (mode == 4)
            {
                float compressed = 1f - Mathf.Clamp01(visual.Animation.w);
                foreach (Vector3 vertex in entry.Geometry.Vertices)
                    bounds.Encapsulate(matrix.MultiplyPoint3x4(new Vector3(vertex.x, vertex.y * compressed, vertex.z)));
            }
            float matrixScale = Mathf.Max(new Vector2(matrix.m00, matrix.m10).magnitude,
                new Vector2(matrix.m01, matrix.m11).magnitude);
            bounds.Expand(new Vector3(material.SwayMargin * matrixScale * 2f + .05f,
                material.SwayMargin * matrixScale * 2f + .05f, .1f));
            entry.Bounds = bounds;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (renderer != null) renderer.enabled = false;
            DestroyObject(root != null ? root.gameObject : null);
            DestroyObject(mesh);
            material.Dispose();
            Entries.Clear();
        }
    }
    #endregion

    #region 跨行共享材质
    private sealed class MaterialLease : IDisposable
    {
        private static readonly Dictionary<(Material, Texture2D, Texture2D), MaterialLease> cache = new();
        private static GameRes resourceOwner;
        private readonly (Material, Texture2D, Texture2D) key;
        private int references;
        internal readonly Material Material;
        internal float SwayMargin { get; private set; }

        private MaterialLease(GroupKey key)
        {
            this.key = (key.Source, key.Texture, key.Alpha);
            Shader shader = Resources.Load<Shader>("Shaders/ChunkDepthSpriteLit");
            if (shader == null) throw new InvalidOperationException("缺少 Shaders/ChunkDepthSpriteLit 行合批 Shader。");
            Material = new Material(shader) { name = "DepthShared_" + key.Source.name + "_" + key.Texture.name,
                hideFlags = HideFlags.HideAndDontSave };
            try { Refresh(); }
            catch { DestroyObject(Material); throw; }
        }

        private void Refresh()
        {
            Material source = key.Item1;
            Material.CopyPropertiesFromMaterial(source);
            Material.renderQueue = 3000;
            Material.SetTexture("_MainTex", key.Item2);
            Material.SetTexture("_AlphaTex", key.Item3 != null ? key.Item3 : Texture2D.whiteTexture);
            Material.SetFloat("_EnableExternalAlpha", key.Item3 != null ? 1f : 0f);
            Material.SetFloat("_DepthDissolveEnabled", source.IsKeywordEnabled("DISSOLVE_ON") ? 1f : 0f);
            bool emissive = source.shader.name == "Game/2D/Emissive-Sprite-Overlay";
            Material.SetFloat("_DepthUnlit", emissive || source.shader.name == "Sprites/Default" ||
                source.shader.name.IndexOf("Unlit", StringComparison.OrdinalIgnoreCase) >= 0 ? 1f : 0f);
            Material.SetFloat("_DepthEmissive", emissive ? 1f : 0f);
            Material.SetFloat("_DepthSrcBlend", (float)(emissive ? BlendMode.One : BlendMode.SrcAlpha));
            Material.SetFloat("_DepthDstBlend", (float)(emissive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            Material.SetFloat("_DepthSrcBlendAlpha", (float)BlendMode.One);
            Material.SetFloat("_DepthDstBlendAlpha", (float)(emissive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            Material.SetFloat("_DepthBlendOp", (float)(emissive ? BlendOp.Max : BlendOp.Add));
            SwayMargin = Material.GetFloat("_GrassSwayAmplitude") * 3f *
                (1f + Mathf.Abs(Material.GetFloat("_GrassSecondaryStrength")));
        }

        internal static MaterialLease Acquire(GroupKey key)
        {
            GameRes resources = GameRes.ExistingInstance;
            if (resourceOwner != resources)
            {
                DetachResources();
                resourceOwner = resources;
                if (resourceOwner != null) resourceOwner.ResourcesReloaded += RefreshSharedMaterials;
            }
            var identity = (key.Source, key.Texture, key.Alpha);
            if (!cache.TryGetValue(identity, out MaterialLease lease))
                cache.Add(identity, lease = new MaterialLease(key));
            lease.references++;
            return lease;
        }
        public void Dispose()
        {
            if (--references != 0) return;
            cache.Remove(key);
            DestroyObject(Material);
            if (cache.Count == 0) DetachResources();
        }

        /// <summary>F5 原位改材质也同步共享副本；外扩变化只重提对应行，不重建权威实体。</summary>
        private static void RefreshSharedMaterials()
        {
            foreach (MaterialLease lease in cache.Values)
                if (lease.key.Item1 != null && lease.Material != null) lease.Refresh();
            foreach (ChunkDepthMeshRenderer owner in active)
                foreach (Entry entry in owner.entries.Values)
                {
                    entry.Dirty = true;
                    owner.dirtyGroups.Add(entry.Group);
                }
        }

        private static void DetachResources()
        {
            if (resourceOwner != null) resourceOwner.ResourcesReloaded -= RefreshSharedMaterials;
            resourceOwner = null;
        }
    }
    #endregion
}
