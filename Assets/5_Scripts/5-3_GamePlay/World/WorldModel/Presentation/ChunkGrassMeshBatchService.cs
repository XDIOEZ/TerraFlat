using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>草按显示区域和实际纹理合并网格；不同 Sprite 只改变顶点 UV，不再占用单独 BRG 批次。</summary>
internal static class ChunkGrassMeshBatchService
{
    #region 全局网格批次

    private const int RegionSize = 128;
    private const int MaxPageVertices = 32768;
    private const MeshUpdateFlags UploadFlags =
        MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;
    private static readonly Dictionary<ChunkTilemapRenderer, Owner> owners = new();
    private static readonly Dictionary<BatchKey, Batch> batches = new();
    private static readonly HashSet<Batch> projectionBatches = new();
    private static bool hooked;

    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex
    {
        public Vector3 Position;
        public Color32 Color;
        public Vector2 Uv;
        public Vector4 Transform0, Transform1, Data0, Data1, Tint, FlowX, FlowY;
    }

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

    private sealed class Owner
    {
        internal readonly ChunkTilemapRenderer Renderer;
        internal readonly Dictionary<int, Entry> Entries = new();
        internal Vector3 Offset;
        internal int RegionX, RegionY;
        internal bool External;
        internal bool Visible;
        internal Matrix4x4 Projection = Matrix4x4.identity;
        internal WorldPresentationSortingDomain Domain;

        internal Owner(ChunkTilemapRenderer renderer)
        {
            Renderer = renderer;
            Offset = renderer.DepthPresentationOffset;
            RegionX = Mathf.FloorToInt(renderer.transform.position.x / RegionSize);
            RegionY = Mathf.FloorToInt(renderer.transform.position.y / RegionSize);
        }
    }

    private sealed class Entry
    {
        internal Owner Owner;
        internal Sprite Sprite;
        internal SharedSpriteMeshCache.Geometry Geometry;
        internal Material Source;
        internal Matrix4x4 Matrix;
        internal Color Tint;
        internal Batch Batch;
    }

    private readonly struct BatchKey : IEquatable<BatchKey>
    {
        internal readonly int Scene, X, Y;
        internal readonly Material Source;
        internal readonly Texture2D Texture;
        internal readonly ChunkTilemapRenderer ProjectedOwner;

        internal BatchKey(Owner owner, Entry entry)
        {
            Scene = owner.Renderer.gameObject.scene.handle;
            Source = entry.Source;
            Texture = entry.Sprite.texture;
            ProjectedOwner = owner.External ? owner.Renderer : null;
            X = owner.External ? 0 : owner.RegionX;
            Y = owner.External ? 0 : owner.RegionY;
        }

        public bool Equals(BatchKey other) => Scene == other.Scene && X == other.X && Y == other.Y &&
            Source == other.Source && Texture == other.Texture && ProjectedOwner == other.ProjectedOwner;
        public override bool Equals(object other) => other is BatchKey key && Equals(key);
        public override int GetHashCode() => HashCode.Combine(Scene, X, Y, Source, Texture, ProjectedOwner);
    }

    static ChunkGrassMeshBatchService()
    {
        SharedSpriteMeshCache.Clearing += ReleaseAll;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        ReleaseAll();
    }

    private static void EnsureHook()
    {
        if (hooked) return;
        RenderPipelineManager.beginContextRendering += FlushBeforeRendering;
        hooked = true;
    }

    private static Owner GetOwner(ChunkTilemapRenderer renderer)
    {
        if (owners.TryGetValue(renderer, out Owner owner)) return owner;
        owners.Add(renderer, owner = new Owner(renderer));
        EnsureHook();
        return owner;
    }

    internal static int GetOwnerVisualCount(ChunkTilemapRenderer renderer)
        => renderer != null && owners.TryGetValue(renderer, out Owner owner) ? owner.Entries.Count : 0;

    /// <summary>增量草格只标记受影响的纹理网格，渲染前统一上传。</summary>
    internal static void Set(ChunkTilemapRenderer renderer, int slot, Sprite sprite, Material material,
        Matrix4x4 worldMatrix, Color tint)
    {
        if (SharedSpriteMeshCache.IsSessionEnding || renderer == null || sprite == null || material == null)
            throw new InvalidOperationException("草网格提交时资源会话或 Sprite/材质无效。");

        SharedSpriteMeshCache.Geometry geometry = SharedSpriteMeshCache.GetGeometry(sprite);
        Owner owner = GetOwner(renderer);
        if (!owner.Entries.TryGetValue(slot, out Entry entry))
        {
            entry = new Entry { Owner = owner };
            owner.Entries.Add(slot, entry);
        }
        if (entry.Sprite == sprite && entry.Source == material && entry.Matrix == worldMatrix &&
            entry.Tint.Equals(tint) && entry.Batch != null)
            return;

        Batch previous = entry.Batch;
        entry.Sprite = sprite;
        entry.Geometry = geometry;
        entry.Source = material;
        entry.Matrix = worldMatrix;
        entry.Tint = tint;
        BatchKey key = new(owner, entry);
        if (previous == null || !previous.Key.Equals(key))
        {
            previous?.Remove(entry);
            entry.Batch = GetBatch(owner, key);
            entry.Batch.Add(entry);
            DisposeEmptyBatch(previous);
        }
        else
            previous.Dirty = true;
    }

    internal static void Clear(ChunkTilemapRenderer renderer, int slot)
    {
        if (ReferenceEquals(renderer, null) || !owners.TryGetValue(renderer, out Owner owner) ||
            !owner.Entries.Remove(slot, out Entry entry)) return;
        Batch batch = entry.Batch;
        batch?.Remove(entry);
        DisposeEmptyBatch(batch);
    }

    /// <summary>草取消绑定时清显示数据但保留飞船投影状态；基础 Owner 注销才完全移除。</summary>
    internal static void ClearGrass(ChunkTilemapRenderer renderer)
    {
        if (ReferenceEquals(renderer, null) || !owners.TryGetValue(renderer, out Owner owner)) return;
        foreach (Entry entry in owner.Entries.Values)
            entry.Batch?.Remove(entry);
        owner.Entries.Clear();
        DisposeEmptyBatches();
    }

    internal static void UnregisterOwner(ChunkTilemapRenderer renderer)
    {
        ClearGrass(renderer);
        if (!ReferenceEquals(renderer, null)) owners.Remove(renderer);
    }

    internal static void RefreshOwnerProjection(ChunkTilemapRenderer renderer)
    {
        if (renderer == null || !owners.TryGetValue(renderer, out Owner owner)) return;
        Vector3 offset = renderer.DepthPresentationOffset;
        int regionX = Mathf.FloorToInt(renderer.transform.position.x / RegionSize);
        int regionY = Mathf.FloorToInt(renderer.transform.position.y / RegionSize);
        if (owner.Offset == offset && owner.RegionX == regionX && owner.RegionY == regionY) return;
        owner.Offset = offset;
        owner.RegionX = regionX;
        owner.RegionY = regionY;
        Regroup(owner);
    }

    internal static void SetOwnerExternalProjection(ChunkTilemapRenderer renderer,
        Matrix4x4 projection, bool visible, WorldPresentationSortingDomain domain)
    {
        if (renderer == null) return;
        Owner owner = GetOwner(renderer);
        bool changed = !owner.External || !ReferenceEquals(owner.Domain, domain);
        owner.External = true;
        owner.Visible = visible;
        owner.Projection = projection;
        owner.Domain = domain;
        if (changed) Regroup(owner, false);
        // 一块草可以有上千条目，但投影矩阵每个纹理批次只需设置一次。
        projectionBatches.Clear();
        foreach (Entry entry in owner.Entries.Values)
            if (entry.Batch != null) projectionBatches.Add(entry.Batch);
        foreach (Batch batch in projectionBatches) batch.UpdateProjection();
        projectionBatches.Clear();
    }

    internal static void ClearOwnerExternalProjection(ChunkTilemapRenderer renderer)
    {
        if (renderer == null || !owners.TryGetValue(renderer, out Owner owner)) return;
        if (!owner.External) return;
        owner.External = false;
        owner.Visible = true;
        owner.Projection = Matrix4x4.identity;
        owner.Domain = null;
        owner.RegionX = Mathf.FloorToInt(renderer.transform.position.x / RegionSize);
        owner.RegionY = Mathf.FloorToInt(renderer.transform.position.y / RegionSize);
        Regroup(owner);
    }

    internal static void CollectVisualSnapshots(ChunkTilemapRenderer renderer, List<WorldVisualSnapshot> output)
    {
        if (renderer == null || !owners.TryGetValue(renderer, out Owner owner)) return;
        int layer = SortingLayer.GetLayerValueFromID(SortingLayer.NameToID(
            WorldRenderingConfigCatalog.Default.sorting.terrainSortingLayer));
        foreach (Entry entry in owner.Entries.Values)
        {
            Matrix4x4 matrix = entry.Matrix;
            matrix.m03 += owner.Offset.x;
            matrix.m13 += owner.Offset.y;
            matrix.m23 += owner.Offset.z;
            if (owner.External) matrix = owner.Projection * matrix;
            output.Add(new WorldVisualSnapshot(entry.Sprite, matrix, entry.Tint, layer, 2, matrix.m13));
        }
    }

    private static void Regroup(Owner owner, bool offsetChanged = true)
    {
        foreach (Entry entry in owner.Entries.Values)
        {
            Batch old = entry.Batch;
            BatchKey key = new(owner, entry);
            if (old != null && old.Key.Equals(key))
            {
                if (offsetChanged) old.Dirty = true;
                continue;
            }
            old?.Remove(entry);
            entry.Batch = GetBatch(owner, key);
            entry.Batch.Add(entry);
            DisposeEmptyBatch(old);
        }
    }

    private static Batch GetBatch(Owner owner, BatchKey key)
    {
        if (batches.TryGetValue(key, out Batch existing)) return existing;
        var batch = new Batch(key, owner.Renderer.gameObject.scene, owner.Renderer.gameObject.layer);
        batches.Add(key, batch);
        return batch;
    }

    private static void DisposeEmptyBatch(Batch batch)
    {
        if (batch == null || batch.Count != 0) return;
        batches.Remove(batch.Key);
        batch.Dispose();
    }

    private static void DisposeEmptyBatches()
    {
        var empty = new List<Batch>();
        foreach (Batch batch in batches.Values)
            if (batch.Count == 0) empty.Add(batch);
        foreach (Batch batch in empty) DisposeEmptyBatch(batch);
    }

    private static void FlushBeforeRendering(ScriptableRenderContext context, List<Camera> cameras)
    {
        foreach (Batch batch in batches.Values)
            if (batch.Dirty) batch.Flush();
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // 回归测试读取真实网格页，而不是仅用输入数量推算 Draw Call。
    internal static void FlushForTests()
    {
        foreach (Batch batch in batches.Values)
            if (batch.Dirty) batch.Flush();
    }

    internal static (int Batches, int Pages, int Vertices, int NativeMaterials) GetStatsForTests()
    {
        int pages = 0, vertices = 0, nativeMaterials = 0;
        foreach (Batch batch in batches.Values)
        {
            pages += batch.VisiblePageCount;
            vertices += batch.VertexCount;
            if (batch.NativeMaterialReady) nativeMaterials++;
        }
        return (batches.Count, pages, vertices, nativeMaterials);
    }
#endif

    private static void ReleaseAll()
    {
        if (hooked)
        {
            RenderPipelineManager.beginContextRendering -= FlushBeforeRendering;
            hooked = false;
        }
        foreach (Batch batch in batches.Values) batch.Dispose();
        batches.Clear();
        owners.Clear();
        projectionBatches.Clear();
    }

    private static void DestroyObject(UnityEngine.Object value)
    {
        if (value == null) return;
        if (value is GameObject gameObject) gameObject.SetActive(false);
        if (Application.isPlaying) UnityEngine.Object.Destroy(value);
        else UnityEngine.Object.DestroyImmediate(value);
    }

    #endregion

    #region 纹理分组及网格页面

    private sealed class Batch : IDisposable
    {
        internal readonly BatchKey Key;
        private readonly HashSet<Entry> entries = new();
        private readonly List<Page> pages = new();
        private readonly List<Vertex> vertices = new();
        private readonly List<int> indices = new();
        private readonly GameObject root;
        private readonly Material material;
        private WorldPresentationSortingDomain attachedDomain;
        private bool disposed;
        internal bool Dirty = true;
        internal int Count => entries.Count;
        internal int VisiblePageCount
        {
            get
            {
                int count = 0;
                foreach (Page page in pages) if (page.Renderer.enabled) count++;
                return count;
            }
        }
        internal int VertexCount
        {
            get
            {
                int count = 0;
                foreach (Page page in pages) if (page.Renderer.enabled) count += page.VertexCount;
                return count;
            }
        }
        internal bool NativeMaterialReady => material.IsKeywordEnabled("_CHUNK_NATIVE_BATCH");
        internal int SortingLayerId => SortingLayer.NameToID(
            WorldRenderingConfigCatalog.Default.sorting.terrainSortingLayer);
        internal int RenderQueue => material.renderQueue;

        internal Batch(BatchKey key, Scene scene, int unityLayer)
        {
            Key = key;
            Material template = Resources.Load<Material>("Config/WorldModel/BRG/ChunkBRG-Sprite-Lit");
            if (template == null)
                throw new InvalidOperationException("缺少草网格共用的 ChunkBRG-Sprite-Lit 材质模板。");
            material = new Material(template)
            {
                name = "GrassMesh_" + key.Source.name + "_" + key.Texture.name,
                hideFlags = HideFlags.HideAndDontSave,
                renderQueue = ChunkBatchRendererGroupService.GetLayerRenderQueue(
                    ChunkBatchRendererGroupService.VisualLayer.Grass)
            };
            try
            {
                material.CopyMatchingPropertiesFromMaterial(key.Source);
                material.shaderKeywords = key.Source.shaderKeywords;
                material.SetTexture("_MainTex", key.Texture);
                material.EnableKeyword("_CHUNK_NATIVE_BATCH");
                material.DisableKeyword("DOTS_INSTANCING_ON");
                if (key.Source.HasProperty("_GrassSwayEnabled") && key.Source.GetFloat("_GrassSwayEnabled") > .5f)
                    material.EnableKeyword("_CHUNK_GRASS_SWAY");
                else
                    material.DisableKeyword("_CHUNK_GRASS_SWAY");
                material.renderQueue = ChunkBatchRendererGroupService.GetLayerRenderQueue(
                    ChunkBatchRendererGroupService.VisualLayer.Grass);
                root = new GameObject("GrassMeshRegion_" + key.X + "_" + key.Y)
                {
                    hideFlags = HideFlags.DontSave, layer = unityLayer
                };
                SceneManager.MoveGameObjectToScene(root, scene);
                UpdateProjection();
            }
            catch
            {
                DestroyObject(root);
                DestroyObject(material);
                throw;
            }
        }

        internal void Add(Entry entry) { entries.Add(entry); Dirty = true; }
        internal void Remove(Entry entry) { if (entries.Remove(entry)) Dirty = true; }

        internal void UpdateProjection()
        {
            if (disposed || root == null || Key.ProjectedOwner == null) return;
            Owner owner = owners[Key.ProjectedOwner];
            WorldPresentationSortingDomain domain = owner.Domain;
            Transform parent = domain?.Root;
            if (root.transform.parent != parent) root.transform.SetParent(parent, true);
            Matrix4x4 local = parent != null
                ? parent.worldToLocalMatrix * owner.Projection : owner.Projection;
            Vector3 position = local.GetColumn(3);
            Quaternion rotation = local.rotation;
            Vector3 scale = local.lossyScale;
            if (root.transform.localPosition != position || root.transform.localRotation != rotation)
                root.transform.SetLocalPositionAndRotation(position, rotation);
            if (root.transform.localScale != scale) root.transform.localScale = scale;
            if (root.activeSelf != owner.Visible) root.SetActive(owner.Visible);
            if (ReferenceEquals(attachedDomain, domain)) return;
            foreach (Page page in pages)
            {
                attachedDomain?.RemoveLocalSortingKey(page.Renderer);
                if (domain != null) domain.ApplyLocalRendererSortingKey(page.Renderer,
                    SortingLayerId, 0, RenderQueue, domain.Root.position.y, 2);
            }
            attachedDomain = domain;
        }

        internal void Flush()
        {
            if (disposed) return;
            vertices.Clear();
            indices.Clear();
            int pageIndex = 0;
            Bounds bounds = default;
            bool hasBounds = false;
            foreach (Entry entry in entries)
            {
                var geometry = entry.Geometry;
                if (vertices.Count > 0 && vertices.Count + geometry.Vertices.Length > MaxPageVertices)
                {
                    Upload(pageIndex++, bounds);
                    vertices.Clear();
                    indices.Clear();
                    hasBounds = false;
                }

                int start = vertices.Count;
                Matrix4x4 matrix = entry.Matrix;
                matrix.m03 += entry.Owner.Offset.x;
                matrix.m13 += entry.Owner.Offset.y;
                matrix.m23 += entry.Owner.Offset.z;
                Vector4 transform0 = new(matrix.m00, matrix.m01, matrix.m03, 0f);
                Vector4 transform1 = new(matrix.m10, matrix.m11, matrix.m13, matrix.m23);
                for (int i = 0; i < geometry.Vertices.Length; i++)
                {
                    Vector3 local = geometry.Vertices[i];
                    vertices.Add(new Vertex
                    {
                        Position = local,
                        Color = new Color32(255, 255, 255, 255),
                        Uv = geometry.Uv[i],
                        Transform0 = transform0,
                        Transform1 = transform1,
                        Tint = entry.Tint
                    });
                    Vector3 world = matrix.MultiplyPoint3x4(local);
                    if (!hasBounds) { bounds = new Bounds(world, Vector3.zero); hasBounds = true; }
                    else bounds.Encapsulate(world);
                }
                foreach (int index in geometry.Triangles) indices.Add(start + index);
            }
            if (vertices.Count > 0) Upload(pageIndex++, bounds);
            for (int i = pageIndex; i < pages.Count; i++) pages[i].Renderer.enabled = false;
            Dirty = false;
        }

        private void Upload(int index, Bounds bounds)
        {
            if (index == pages.Count)
                pages.Add(new Page(root.transform, material, SortingLayerId, attachedDomain));
            float amplitude = material.HasProperty("_GrassSwayAmplitude")
                ? Mathf.Abs(material.GetFloat("_GrassSwayAmplitude")) : 0f;
            float secondary = material.HasProperty("_GrassSecondaryStrength")
                ? Mathf.Abs(material.GetFloat("_GrassSecondaryStrength")) : 0f;
            bounds.Expand(new Vector3(1f, 1f, 0f) * (amplitude * 6f * (1f + secondary) + .1f));
            pages[index].Upload(vertices, indices, bounds);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (Page page in pages)
            {
                attachedDomain?.RemoveLocalSortingKey(page.Renderer);
                page.Dispose();
            }
            pages.Clear();
            entries.Clear();
            DestroyObject(root);
            DestroyObject(material);
        }

        private sealed class Page : IDisposable
        {
            private readonly GameObject node;
            private readonly Mesh mesh;
            internal readonly MeshRenderer Renderer;
            internal int VertexCount => mesh.vertexCount;

            internal Page(Transform parent, Material material, int sortingLayer,
                WorldPresentationSortingDomain domain)
            {
                node = new GameObject("GrassMeshPage") { hideFlags = HideFlags.DontSave, layer = parent.gameObject.layer };
                node.transform.SetParent(parent, false);
                mesh = new Mesh { name = "GrassMergedSprites", hideFlags = HideFlags.HideAndDontSave };
                mesh.MarkDynamic();
                node.AddComponent<MeshFilter>().sharedMesh = mesh;
                Renderer = node.AddComponent<MeshRenderer>();
                Renderer.sharedMaterial = material;
                Renderer.sortingLayerID = sortingLayer;
                Renderer.sortingOrder = 0;
                Renderer.shadowCastingMode = ShadowCastingMode.Off;
                Renderer.receiveShadows = false;
                Renderer.lightProbeUsage = LightProbeUsage.Off;
                Renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                if (domain != null)
                    domain.ApplyLocalRendererSortingKey(Renderer, sortingLayer, 0,
                        material.renderQueue, domain.Root.position.y, 2);
            }

            internal void Upload(List<Vertex> verts, List<int> tris, Bounds bounds)
            {
                mesh.Clear();
                mesh.SetVertexBufferParams(verts.Count, Layout);
                mesh.SetIndexBufferParams(tris.Count, IndexFormat.UInt32);
                mesh.SetVertexBufferData(verts, 0, 0, verts.Count, 0, UploadFlags);
                mesh.SetIndexBufferData(tris, 0, 0, tris.Count, UploadFlags);
                mesh.subMeshCount = 1;
                mesh.SetSubMesh(0, new SubMeshDescriptor(0, tris.Count)
                {
                    vertexCount = verts.Count, bounds = bounds
                }, UploadFlags);
                mesh.bounds = bounds;
                Renderer.enabled = true;
            }

            public void Dispose()
            {
                Renderer.enabled = false;
                DestroyObject(node);
                DestroyObject(mesh);
            }
        }
    }

    #endregion
}
