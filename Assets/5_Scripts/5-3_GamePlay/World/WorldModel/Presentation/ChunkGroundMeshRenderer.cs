using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>每层每种图集纹理为一个 Chunk 网格，单格变化只上传该格的四个顶点。</summary>
internal sealed class ChunkGroundMeshRenderer : IDisposable
{
    #region 网格数据
    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex
    {
        public Vector3 Position;
        public Color32 Tint;
        public Vector2 Uv;
        public Vector4 Contact;
        public Vector4 NeighbourHeight;
        public float Height;
        public Vector4 FlowX;
        public Vector4 FlowY;
        public float WaterKind;
    }

    private const MeshUpdateFlags UploadFlags = MeshUpdateFlags.DontRecalculateBounds |
        MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers;
    private readonly Transform parent;
    private readonly Material sourceMaterial;
    private readonly ChunkBatchRendererGroupService.VisualLayer layer;
    private readonly int width;
    private readonly int height;
    private readonly Group[] cellGroups;
    private readonly Dictionary<(Texture, Material), Group> groups = new();
    private readonly Dictionary<Sprite, Quad> quads = new();
    private readonly Shader shader;
    private bool bulkUpdating;

    public ChunkGroundMeshRenderer(Transform parent, int width, int height, Material sourceMaterial,
        ChunkBatchRendererGroupService.VisualLayer layer)
    {
        this.parent = parent;
        this.width = width;
        this.height = height;
        this.sourceMaterial = sourceMaterial;
        this.layer = layer;
        shader = Shader.Find(layer == ChunkBatchRendererGroupService.VisualLayer.Water
            ? "FlatWorld/2D/Chunk Mesh Water Lit"
            : "FlatWorld/2D/Chunk Mesh Contact Lit");
        cellGroups = new Group[width * height];
        if (layer == ChunkBatchRendererGroupService.VisualLayer.Ground)
            GroundElevationShadowSettings.Changed += RefreshElevationMaterials;
    }

    public bool IsAvailable => shader != null &&
        (sourceMaterial != null || layer == ChunkBatchRendererGroupService.VisualLayer.Water);
    public int ActiveCellCount { get; private set; }

    public void BeginBulkUpdate() => bulkUpdating = true;

    public void EndBulkUpdate()
    {
        if (!bulkUpdating) return;
        bulkUpdating = false;
        foreach (Group group in groups.Values)
            if (group.Dirty) group.UploadAll();
    }

    public bool TrySetCell(int x, int y, Sprite sprite, Color tint, Matrix4x4 tileTransform,
        Vector4 contact, float elevation, Vector4 neighbours)
        => TrySetCore(x, y, sprite, sourceMaterial, tint, tileTransform,
            contact, elevation, neighbours, Vector4.zero, Vector4.zero, -1f);

    public bool TrySetWaterCell(int x, int y, Sprite sprite, Material material,
        ChunkBatchRendererGroupService.InstanceData data)
        => TrySetCore(x, y, sprite, material, Color.white, Matrix4x4.identity,
            data.Data0, -1f, data.Data1, data.FlowX, data.FlowY, data.Transform0.w);

    private bool TrySetCore(int x, int y, Sprite sprite, Material material,
        Color tint, Matrix4x4 tileTransform, Vector4 contact, float elevation,
        Vector4 neighbours, Vector4 flowX, Vector4 flowY, float waterKind)
    {
        if (!IsAvailable || material == null || sprite == null || sprite.texture == null ||
            !TryReadQuad(sprite, out Quad quad))
        {
            ClearCell(x, y);
            return false;
        }

        int index = y * width + x;
        Group group = GetOrCreateGroup(sprite.texture, material);
        Group previous = cellGroups[index];
        if (previous != null && previous != group)
            previous.Clear(index, bulkUpdating);
        if (previous == null) ActiveCellCount++;
        cellGroups[index] = group;
        group.Write(index, x, y, quad.Corners, quad.Uvs, tint, tileTransform,
            contact, elevation, neighbours, flowX, flowY, waterKind, bulkUpdating);
        return true;
    }

    public void ClearCell(int x, int y)
    {
        int index = y * width + x;
        Group previous = cellGroups[index];
        if (previous == null) return;
        previous.Clear(index, bulkUpdating);
        cellGroups[index] = null;
        ActiveCellCount--;
    }

    private Group GetOrCreateGroup(Texture texture, Material material)
    {
        var key = (texture, material);
        if (groups.TryGetValue(key, out Group existing)) return existing;
        var group = new Group(parent, width, height, shader, material, texture, layer);
        groups.Add(key, group);
        if (layer == ChunkBatchRendererGroupService.VisualLayer.Ground)
            ApplyElevationMaterial(group.Material);
        return group;
    }

    private bool TryReadQuad(Sprite sprite, out Quad quad)
    {
        if (quads.TryGetValue(sprite, out quad)) return quad.Valid;
        Vector2[] source = sprite.vertices;
        Vector2[] sourceUv = sprite.uv;
        if (source.Length != 4 || sourceUv.Length != 4)
        {
            quads.Add(sprite, default);
            return false;
        }
        float minX = Mathf.Min(source[0].x, source[1].x, source[2].x, source[3].x);
        float maxX = Mathf.Max(source[0].x, source[1].x, source[2].x, source[3].x);
        float minY = Mathf.Min(source[0].y, source[1].y, source[2].y, source[3].y);
        float maxY = Mathf.Max(source[0].y, source[1].y, source[2].y, source[3].y);
        if (maxX <= minX || maxY <= minY)
        {
            quads.Add(sprite, default);
            return false;
        }
        Vector2[] corners = new Vector2[4];
        Vector2[] uvs = new Vector2[4];
        int seen = 0;
        float middleX = (minX + maxX) * 0.5f;
        float middleY = (minY + maxY) * 0.5f;
        for (int i = 0; i < 4; i++)
        {
            int corner = (source[i].x > middleX ? 1 : 0) + (source[i].y > middleY ? 2 : 0);
            if ((seen & (1 << corner)) != 0)
            {
                quads.Add(sprite, default);
                return false;
            }
            seen |= 1 << corner;
            corners[corner] = source[i];
            uvs[corner] = sourceUv[i];
        }
        quad = seen == 15 ? new Quad(corners, uvs) : default;
        quads.Add(sprite, quad);
        return quad.Valid;
    }

    private readonly struct Quad
    {
        public Quad(Vector2[] corners, Vector2[] uvs)
        { Corners = corners; Uvs = uvs; }
        public Vector2[] Corners { get; }
        public Vector2[] Uvs { get; }
        public bool Valid => Corners != null && Uvs != null;
    }

    private void RefreshElevationMaterials()
    {
        foreach (Group group in groups.Values)
            ApplyElevationMaterial(group.Material);
    }

    private static void ApplyElevationMaterial(Material material)
    {
        material.SetFloat("_ElevationStrength", GroundElevationShadowSettings.Enabled ? 1f : 0f);
        material.SetFloat("_ElevationEdgeWidth", GroundElevationShadowSettings.Width);
    }

    public void Dispose()
    {
        GroundElevationShadowSettings.Changed -= RefreshElevationMaterials;
        foreach (Group group in groups.Values) group.Dispose();
        groups.Clear();
        quads.Clear();
        Array.Clear(cellGroups, 0, cellGroups.Length);
        ActiveCellCount = 0;
    }
    #endregion

    #region 纹理批次
    private sealed class Group : IDisposable
    {
        private readonly GameObject root;
        private readonly Mesh mesh;
        private readonly Vertex[] vertices;
        public Material Material { get; }
        public bool Dirty { get; private set; }

        public Group(Transform parent, int width, int height, Shader shader,
            Material sourceMaterial, Texture texture,
            ChunkBatchRendererGroupService.VisualLayer layer)
        {
            int cellCount = width * height;
            vertices = new Vertex[cellCount * 4];
            mesh = new Mesh { name = $"Chunk_{layer}_{texture.name}", hideFlags = HideFlags.HideAndDontSave };
            mesh.MarkDynamic();
            mesh.SetVertexBufferParams(vertices.Length,
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 1),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord4, VertexAttributeFormat.Float32, 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord5, VertexAttributeFormat.Float32, 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord6, VertexAttributeFormat.Float32, 1));
            int indexCount = cellCount * 6;
            if (vertices.Length <= ushort.MaxValue)
            {
                var indices = new ushort[indexCount];
                for (int cell = 0; cell < cellCount; cell++)
                {
                    int vertex = cell * 4, target = cell * 6;
                    indices[target] = (ushort)vertex;
                    indices[target + 1] = (ushort)(vertex + 2);
                    indices[target + 2] = (ushort)(vertex + 1);
                    indices[target + 3] = (ushort)(vertex + 1);
                    indices[target + 4] = (ushort)(vertex + 2);
                    indices[target + 5] = (ushort)(vertex + 3);
                }
                mesh.SetIndexBufferParams(indexCount, IndexFormat.UInt16);
                mesh.SetIndexBufferData(indices, 0, 0, indexCount, UploadFlags);
            }
            else
            {
                var indices = new uint[indexCount];
                for (int cell = 0; cell < cellCount; cell++)
                {
                    uint vertex = (uint)(cell * 4);
                    int target = cell * 6;
                    indices[target] = vertex;
                    indices[target + 1] = vertex + 2;
                    indices[target + 2] = vertex + 1;
                    indices[target + 3] = vertex + 1;
                    indices[target + 4] = vertex + 2;
                    indices[target + 5] = vertex + 3;
                }
                mesh.SetIndexBufferParams(indexCount, IndexFormat.UInt32);
                mesh.SetIndexBufferData(indices, 0, 0, indexCount, UploadFlags);
            }
            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, indexCount), UploadFlags);
            mesh.bounds = new Bounds(new Vector3(width * 0.5f, height * 0.5f),
                new Vector3(width + 6f, height + 6f, 2f));
            int renderPriority = layer switch
            {
                ChunkBatchRendererGroupService.VisualLayer.Back => -1,
                ChunkBatchRendererGroupService.VisualLayer.Water => 1,
                ChunkBatchRendererGroupService.VisualLayer.Blocking => 4,
                _ => 0
            };
            Material = new Material(shader) { name = $"Chunk_{layer}_{texture.name}",
                hideFlags = HideFlags.HideAndDontSave, renderQueue = 2988 + renderPriority };
            Material.CopyPropertiesFromMaterial(sourceMaterial);
            Material.shaderKeywords = sourceMaterial.shaderKeywords;
            Material.SetTexture("_MainTex", texture);
            Material.renderQueue = 2988 + renderPriority;
            root = new GameObject($"{layer}Mesh_{texture.name}");
            root.transform.SetParent(parent, false);
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        public void Write(int index, int x, int y, Vector2[] corners, Vector2[] uvs,
            Color tint, Matrix4x4 transform, Vector4 contact, float elevation,
            Vector4 neighbours, Vector4 flowX, Vector4 flowY, float waterKind, bool bulk)
        {
            int start = index * 4;
            for (int corner = 0; corner < 4; corner++)
            {
                Vector3 local = transform.MultiplyPoint3x4(corners[corner]);
                vertices[start + corner] = new Vertex
                {
                    Position = new Vector3(x + 0.5f + local.x, y + 0.5f + local.y, local.z),
                    Tint = tint,
                    Uv = uvs[corner],
                    Contact = contact,
                    Height = elevation,
                    NeighbourHeight = neighbours,
                    FlowX = flowX,
                    FlowY = flowY,
                    WaterKind = waterKind
                };
            }
            Upload(index, bulk);
        }

        public void Clear(int index, bool bulk)
        {
            Array.Clear(vertices, index * 4, 4);
            Upload(index, bulk);
        }

        private void Upload(int index, bool bulk)
        {
            if (bulk) { Dirty = true; return; }
            mesh.SetVertexBufferData(vertices, index * 4, index * 4, 4, 0, UploadFlags);
        }

        public void UploadAll()
        {
            mesh.SetVertexBufferData(vertices, 0, 0, vertices.Length, 0, UploadFlags);
            Dirty = false;
        }

        public void Dispose()
        {
            root.SetActive(false);
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(root);
                UnityEngine.Object.Destroy(mesh);
                UnityEngine.Object.Destroy(Material);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(Material);
            }
        }
    }
    #endregion
}
