using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>投影世界按区块与素材合并地表实例，船体移动只更新共同变换。</summary>
internal sealed class ChunkNativeSortingDomainBatches : IDisposable
{
    #region 原生域内批次
    private readonly Transform root;
    private readonly WorldPresentationSortingDomain domain;
    private readonly Func<Material, Material> resolveMaterial;
    private readonly Dictionary<(ChunkBatchRendererGroupService.VisualLayer, Sprite, Material), Batch> batches = new();
    private readonly Dictionary<int, Batch> slots = new();
    private bool disposed;

    internal int VisualCount => slots.Count;
    internal int BatchCount => batches.Count;
    internal int MeshRevision { get; private set; }

    internal ChunkNativeSortingDomainBatches(WorldPresentationSortingDomain domain, string ownerName, int layer,
        Func<Material, Material> resolveMaterial)
    {
        this.domain = domain;
        this.resolveMaterial = resolveMaterial;
        var node = new GameObject("NativeChunkProjection_" + ownerName) { hideFlags = HideFlags.DontSave, layer = layer };
        root = node.transform;
        root.SetParent(domain.Root, false);
        ChunkDepthMeshRenderer.EnsureDispatcher();
        ChunkDepthMeshRenderer.BeforeFlush += Flush;
    }

    internal void Synchronize(Matrix4x4 projection, bool visible)
    {
        if (disposed || root == null) return;
        Matrix4x4 local = root.parent != null ? root.parent.worldToLocalMatrix * projection : projection;
        Vector3 position = local.GetColumn(3);
        Quaternion rotation = local.rotation;
        Vector3 scale = local.lossyScale;
        if (root.localPosition != position) root.localPosition = position;
        if (root.localRotation != rotation) root.localRotation = rotation;
        if (root.localScale != scale) root.localScale = scale;
        if (root.gameObject.activeSelf != visible) root.gameObject.SetActive(visible);
        foreach (Batch batch in batches.Values) batch.UpdateSortingKey();
    }

    internal void SetVisual(int slot, ChunkBatchRendererGroupService.VisualLayer layer, Sprite sprite,
        Material sourceMaterial, ChunkBatchRendererGroupService.InstanceData data, int batchOrder)
    {
        var key = (layer, sprite, sourceMaterial);
        if (slots.TryGetValue(slot, out Batch previous) && previous.Key != key)
            RemoveVisual(slot);
        if (!batches.TryGetValue(key, out Batch batch))
        {
            batch = new Batch(root, key, resolveMaterial(sourceMaterial), domain, batchOrder);
            batches.Add(key, batch);
        }
        batch.Set(slot, data);
        slots[slot] = batch;
    }

    internal void RemoveVisual(int slot)
    {
        if (!slots.Remove(slot, out Batch batch)) return;
        batch.Remove(slot);
        if (batch.Count != 0) return;
        batches.Remove(batch.Key);
        batch.Dispose();
        MeshRevision++;
    }

    private void Flush()
    {
        if (disposed) return;
        foreach (Batch batch in batches.Values)
            if (batch.Flush()) MeshRevision++;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        ChunkDepthMeshRenderer.BeforeFlush -= Flush;
        foreach (Batch batch in batches.Values) batch.Dispose();
        batches.Clear(); slots.Clear();
        DestroyObject(root != null ? root.gameObject : null);
    }

    private static void DestroyObject(UnityEngine.Object value)
    {
        if (value == null) return;
        if (value is GameObject node) node.SetActive(false);
        if (Application.isPlaying) UnityEngine.Object.Destroy(value);
        else UnityEngine.Object.DestroyImmediate(value);
    }

    private sealed class Batch : IDisposable
    {
        internal readonly (ChunkBatchRendererGroupService.VisualLayer, Sprite, Material) Key;
        private readonly Dictionary<int, ChunkBatchRendererGroupService.InstanceData> data = new();
        private readonly GameObject node;
        private readonly Mesh mesh;
        private readonly MeshRenderer renderer;
        private readonly WorldPresentationSortingDomain domain;
        private readonly int sortingLayerId, renderQueue, batchOrder;
        private readonly Vector3[] vertices;
        private readonly Vector2[] uv;
        private readonly int[] triangles;
        private readonly List<Vector3> positions = new();
        private readonly List<Vector2> tex = new();
        private readonly List<Color> colors = new();
        private readonly List<Vector4>[] attributes = { new(), new(), new(), new(), new(), new(), new() };
        private readonly List<int> indices = new();
        private readonly float swayPadding;
        private bool dirty;
        internal int Count => data.Count;

        internal Batch(Transform parent, (ChunkBatchRendererGroupService.VisualLayer, Sprite, Material) key,
            Material material, WorldPresentationSortingDomain domain, int batchOrder)
        {
            Key = key;
            this.domain = domain;
            this.batchOrder = batchOrder;
            renderQueue = material.renderQueue;
            swayPadding = material.HasProperty("_GrassSwayAmplitude")
                ? Mathf.Abs(material.GetFloat("_GrassSwayAmplitude")) * 3f *
                  (1f + Mathf.Abs(material.GetFloat("_GrassSecondaryStrength"))) : 0f;
            Mesh source = key.Item1 == ChunkBatchRendererGroupService.VisualLayer.NaturalShadow
                ? SharedSpriteMeshCache.GetSunShadowGeometry(key.Item2).Mesh
                : SharedSpriteMeshCache.GetOrCreate(key.Item2);
            vertices = source.vertices; uv = source.uv; triangles = source.triangles;
            node = new GameObject("NativeChunkBatch_" + key.Item1) { hideFlags = HideFlags.DontSave, layer = parent.gameObject.layer };
            node.transform.SetParent(parent, false);
            mesh = new Mesh { name = node.name, hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
            mesh.MarkDynamic();
            node.AddComponent<MeshFilter>().sharedMesh = mesh;
            renderer = node.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            sortingLayerId = SortingLayer.NameToID(WorldRenderingConfigCatalog.Default.sorting.terrainSortingLayer);
            renderer.sortingLayerID = sortingLayerId;
            renderer.sortingOrder = 0;
            renderer.renderingLayerMask = uint.MaxValue;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            UpdateSortingKey();
        }

        internal void UpdateSortingKey()
        {
            if (renderer == null || domain.IsDisposed) return;
            domain.ApplyLocalRendererSortingKey(renderer, sortingLayerId, 0, renderQueue,
                domain.Root.position.y, batchOrder);
        }

        internal void Set(int slot, ChunkBatchRendererGroupService.InstanceData value)
        {
            if (data.TryGetValue(slot, out var old) && Same(old, value)) return;
            data[slot] = value;
            dirty = true;
        }

        internal void Remove(int slot) { if (data.Remove(slot)) dirty = true; }

        private static bool Same(ChunkBatchRendererGroupService.InstanceData a, ChunkBatchRendererGroupService.InstanceData b)
            => a.Transform0.Equals(b.Transform0) && a.Transform1.Equals(b.Transform1) && a.Data0.Equals(b.Data0) &&
               a.Data1.Equals(b.Data1) && a.Tint.Equals(b.Tint) && a.FlowX.Equals(b.FlowX) && a.FlowY.Equals(b.FlowY);

        internal bool Flush()
        {
            if (!dirty) return false;
            int count = data.Count * vertices.Length;
            Prepare(positions, count); Prepare(tex, count); Prepare(colors, count);
            foreach (List<Vector4> channel in attributes) Prepare(channel, count);
            Prepare(indices, data.Count * triangles.Length);
            Bounds bounds = default;
            bool hasBounds = false;
            foreach (var instance in data.Values)
            {
                int start = positions.Count;
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 v = vertices[i];
                    positions.Add(v); tex.Add(uv[i]); colors.Add(Color.white);
                    attributes[0].Add(instance.Transform0); attributes[1].Add(instance.Transform1);
                    attributes[2].Add(instance.Data0); attributes[3].Add(instance.Data1);
                    attributes[4].Add(instance.Tint); attributes[5].Add(instance.FlowX); attributes[6].Add(instance.FlowY);
                    Vector3 point = new(instance.Transform0.x * v.x + instance.Transform0.y * v.y + instance.Transform0.z,
                        instance.Transform1.x * v.x + instance.Transform1.y * v.y + instance.Transform1.z,
                        instance.Transform1.w + v.z);
                    if (!hasBounds) { bounds = new Bounds(point, Vector3.zero); hasBounds = true; }
                    else bounds.Encapsulate(point);
                }
                foreach (int index in triangles) indices.Add(start + index);
            }
            if (Key.Item1 == ChunkBatchRendererGroupService.VisualLayer.NaturalShadow)
                bounds.Expand(WorldRenderingConfigCatalog.Default.shadows.maximumDistance * 2f);
            else if (swayPadding > 0f) bounds.Expand(swayPadding * 2f);
            mesh.Clear();
            mesh.indexFormat = count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(positions); mesh.SetUVs(0, tex); mesh.SetColors(colors);
            for (int i = 0; i < attributes.Length; i++) mesh.SetUVs(i + 1, attributes[i]);
            mesh.SetTriangles(indices, 0, false);
            mesh.bounds = bounds;
            dirty = false;
            return true;
        }

        private static void Prepare<T>(List<T> values, int capacity)
        {
            values.Clear();
            if (values.Capacity < capacity) values.Capacity = capacity;
        }

        public void Dispose() { domain.RemoveLocalSortingKey(renderer); DestroyObject(node); DestroyObject(mesh); }
    }
    #endregion
}
