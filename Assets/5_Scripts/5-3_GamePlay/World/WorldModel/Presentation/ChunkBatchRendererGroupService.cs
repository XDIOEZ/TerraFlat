using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 全局区块 BatchRendererGroup 后端。
///
/// 每种 Sprite + 源材质 + 地图层共享一个 BRG Batch；所有 ChunkView 只提交格子实例数据，
/// 不再为每个区块创建地形 MeshRenderer。实例采用 112 字节 AoS，单格变化只上传一个连续块。
/// BRG 只负责地图视觉，Blocking TilemapCollider2D 仍由兼容层维护。
/// </summary>
internal static class ChunkBatchRendererGroupService
{
    #region 公共契约

    internal enum VisualLayer : byte
    {
        Back,
        Ground,
        Water,
        Blocking
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct InstanceData
    {
        public Vector4 Transform0;
        public Vector4 Transform1;
        public Vector4 Data0;
        public Vector4 Data1;
        public Vector4 Tint;
        public Vector4 FlowX; // 四个共享格角的下游速度 X
        public Vector4 FlowY; // 四个共享格角的下游速度 Y

        public static InstanceData Create(Matrix4x4 localToWorld, Vector4 data0, Vector4 data1, Color tint)
        {
            return new InstanceData
            {
                Transform0 = new Vector4(localToWorld.m00, localToWorld.m01, localToWorld.m03, 0f),
                Transform1 = new Vector4(localToWorld.m10, localToWorld.m11, localToWorld.m13, localToWorld.m23),
                Data0 = data0,
                Data1 = data1,
                Tint = tint
            };
        }
    }

    internal readonly struct Visual
    {
        public Visual(VisualLayer layer, Sprite sprite, Material sourceMaterial, InstanceData data)
        {
            Layer = layer;
            Sprite = sprite;
            SourceMaterial = sourceMaterial;
            Data = data;
        }

        public VisualLayer Layer { get; }
        public Sprite Sprite { get; }
        public Material SourceMaterial { get; }
        public InstanceData Data { get; }
    }

    private static Backend backend;

    internal static void RegisterOwner(ChunkTilemapRenderer owner)
    {
        if (owner == null)
            return;
        EnsureBackend().RegisterOwner(owner);
    }

    internal static void UnregisterOwner(ChunkTilemapRenderer owner)
    {
        backend?.UnregisterOwner(owner);
    }

    /// <summary>
    /// 世界窗口彻底关闭后再释放空闲后端。
    /// 普通区块流送期间即使短暂没有 Owner，也保留 BRG，避免反复销毁/重建共享批次。
    /// </summary>
    internal static void ReleaseUnusedBackend()
    {
        if (backend == null || backend.OwnerCount != 0)
            return;
        backend.Dispose();
        backend = null;
    }

    internal static void SetVisual(ChunkTilemapRenderer owner, int slotKey, Visual visual)
    {
        EnsureBackend().SetVisual(owner, slotKey, visual);
    }

    internal static void ClearVisual(ChunkTilemapRenderer owner, int slotKey)
    {
        backend?.ClearVisual(owner, slotKey);
    }

    /// <summary>检查某个区块地形表现是否仍登记在当前 BRG 后端。</summary>
    internal static bool IsOwnerRegistered(ChunkTilemapRenderer owner)
    {
        return owner != null && backend?.IsOwnerRegistered(owner) == true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        backend?.Dispose();
        backend = null;
    }

    private static Backend EnsureBackend() => backend ??= new Backend();

    #endregion

    #region BRG 后端

    private sealed class Backend : IDisposable
    {
        private const int InitialCapacity = 64;
        private const int InstanceStride = 112;
        private const int ZeroPrefixBytes = InstanceStride;
        private const uint InstanceMetadataAddress = ZeroPrefixBytes;
        private const int TerrainQueueBase = 2988;
        private static readonly int InstanceDataId = Shader.PropertyToID("_ChunkInstanceData");

        private readonly object syncRoot = new();
        private readonly BatchRendererGroup rendererGroup;
        private readonly Dictionary<ChunkTilemapRenderer, Dictionary<int, InstanceHandle>> ownerHandles = new();
        private readonly Dictionary<VisualKey, TileBatch> batches = new();
        private readonly List<TileBatch> orderedBatches = new();
        private readonly Dictionary<int, MeshRegistration> meshes = new();
        private readonly Dictionary<MaterialKey, MaterialRegistration> materials = new();
        private readonly Material spriteTemplate;
        private readonly Material contactTemplate;
        private readonly Material waterTemplate;
        private bool disposed;

        public Backend()
        {
            spriteTemplate = LoadTemplate("Config/WorldModel/BRG/ChunkBRG-Sprite-Lit");
            contactTemplate = LoadTemplate("Config/WorldModel/BRG/ChunkBRG-Contact-Lit");
            waterTemplate = LoadTemplate("Config/WorldModel/BRG/ChunkBRG-Water-Lit");
            rendererGroup = new BatchRendererGroup(OnPerformCulling, IntPtr.Zero);
            // Chunk 流送本身已经限制活动窗口；这里保留宽全局 Bounds，避免 BRG 在回调前误裁掉循环世界区块。
            rendererGroup.SetGlobalBounds(new Bounds(Vector3.zero, new Vector3(2000000f, 2000000f, 1000f)));
        }

        public int OwnerCount
        {
            get
            {
                lock (syncRoot)
                    return ownerHandles.Count;
            }
        }

        public void RegisterOwner(ChunkTilemapRenderer owner)
        {
            lock (syncRoot)
            {
                if (!ownerHandles.ContainsKey(owner))
                    ownerHandles.Add(owner, new Dictionary<int, InstanceHandle>());
            }
        }

        public bool IsOwnerRegistered(ChunkTilemapRenderer owner)
        {
            lock (syncRoot)
                return owner != null && ownerHandles.ContainsKey(owner);
        }

        public void UnregisterOwner(ChunkTilemapRenderer owner)
        {
            if (owner == null)
                return;
            lock (syncRoot)
            {
                if (!ownerHandles.TryGetValue(owner, out Dictionary<int, InstanceHandle> handles))
                    return;

                while (handles.Count > 0)
                {
                    using Dictionary<int, InstanceHandle>.Enumerator enumerator = handles.GetEnumerator();
                    enumerator.MoveNext();
                    RemoveHandle(owner, enumerator.Current.Key, enumerator.Current.Value, handles);
                }

                ownerHandles.Remove(owner);
            }
        }

        public void SetVisual(ChunkTilemapRenderer owner, int slotKey, Visual visual)
        {
            if (owner == null || visual.Sprite == null || visual.SourceMaterial == null)
            {
                ClearVisual(owner, slotKey);
                return;
            }

            lock (syncRoot)
            {
                if (!ownerHandles.TryGetValue(owner, out Dictionary<int, InstanceHandle> handles))
                {
                    handles = new Dictionary<int, InstanceHandle>();
                    ownerHandles.Add(owner, handles);
                }

                VisualKey key = new(visual.Layer, visual.Sprite, visual.SourceMaterial);
                if (handles.TryGetValue(slotKey, out InstanceHandle current))
                {
                    if (current.Batch.Key.Equals(key))
                    {
                        current.Batch.Update(current.Index, visual.Data);
                        return;
                    }

                    RemoveHandle(owner, slotKey, current, handles);
                }

                TileBatch batch = GetOrCreateBatch(key, visual);
                int index = batch.Add(owner, slotKey, visual.Data);
                handles[slotKey] = new InstanceHandle(batch, index);
            }
        }

        public void ClearVisual(ChunkTilemapRenderer owner, int slotKey)
        {
            if (owner == null)
                return;
            lock (syncRoot)
            {
                if (!ownerHandles.TryGetValue(owner, out Dictionary<int, InstanceHandle> handles) ||
                    !handles.TryGetValue(slotKey, out InstanceHandle handle))
                {
                    return;
                }

                RemoveHandle(owner, slotKey, handle, handles);
            }
        }

        private void RemoveHandle(ChunkTilemapRenderer owner, int slotKey, InstanceHandle handle,
            Dictionary<int, InstanceHandle> handles)
        {
            handles.Remove(slotKey);
            if (handle.Batch.Remove(handle.Index, out InstanceOwner movedOwner))
            {
                if (ownerHandles.TryGetValue(movedOwner.Owner, out Dictionary<int, InstanceHandle> movedHandles))
                    movedHandles[movedOwner.SlotKey] = new InstanceHandle(handle.Batch, handle.Index);
            }
        }

        private TileBatch GetOrCreateBatch(VisualKey key, Visual visual)
        {
            if (batches.TryGetValue(key, out TileBatch existing))
                return existing;

            MeshRegistration mesh = GetOrCreateMesh(visual.Sprite);
            MaterialRegistration material = GetOrCreateMaterial(visual.Layer, visual.Sprite.texture,
                visual.SourceMaterial);
            var batch = new TileBatch(this, key, mesh.Id, material.Id, ResolvePriority(visual.Layer),
                InitialCapacity);
            batches.Add(key, batch);
            orderedBatches.Add(batch);
            orderedBatches.Sort((left, right) => left.Priority.CompareTo(right.Priority));
            return batch;
        }

        private MeshRegistration GetOrCreateMesh(Sprite sprite)
        {
            int key = sprite.GetInstanceID();
            if (meshes.TryGetValue(key, out MeshRegistration registration))
                return registration;

            Vector2[] spriteVertices = sprite.vertices;
            Vector2[] spriteUv = sprite.uv;
            ushort[] spriteTriangles = sprite.triangles;
            var vertices = new Vector3[spriteVertices.Length];
            var triangles = new int[spriteTriangles.Length];
            for (int i = 0; i < spriteVertices.Length; i++)
                vertices[i] = spriteVertices[i];
            for (int i = 0; i < spriteTriangles.Length; i++)
                triangles[i] = spriteTriangles[i];

            var mesh = new Mesh
            {
                name = $"ChunkBRG_{sprite.name}",
                hideFlags = HideFlags.HideAndDontSave
            };
            mesh.vertices = vertices;
            mesh.uv = spriteUv;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            BatchMeshID id = rendererGroup.RegisterMesh(mesh);
            registration = new MeshRegistration(mesh, id);
            meshes.Add(key, registration);
            return registration;
        }

        private MaterialRegistration GetOrCreateMaterial(VisualLayer layer, Texture texture,
            Material sourceMaterial)
        {
            Material template = layer switch
            {
                VisualLayer.Ground => contactTemplate,
                VisualLayer.Water => waterTemplate,
                _ => spriteTemplate
            };
            int priority = ResolvePriority(layer);
            var key = new MaterialKey(template.GetInstanceID(), sourceMaterial.GetInstanceID(),
                texture != null ? texture.GetInstanceID() : 0, priority);
            if (materials.TryGetValue(key, out MaterialRegistration registration))
                return registration;

            var runtimeMaterial = new Material(template)
            {
                name = $"ChunkBRG_{layer}_{sourceMaterial.name}_{texture?.name}",
                hideFlags = HideFlags.HideAndDontSave,
                enableInstancing = true,
                // BRG 没有 SpriteRenderer/TilemapRenderer 的 Sorting Layer 字段，实际会与普通世界渲染共用默认排序域。
                // 地形批次固定占用 2987~2992；草等 BRG 上层表现需使用 Default Sorting Layer，
                // 并把 Render Queue 放在 2992 之后、普通世界 Sprite 3000 之前。
                renderQueue = TerrainQueueBase + priority
            };
            runtimeMaterial.CopyPropertiesFromMaterial(sourceMaterial);
            runtimeMaterial.shaderKeywords = sourceMaterial.shaderKeywords;
            runtimeMaterial.SetTexture("_MainTex", texture);
            runtimeMaterial.renderQueue = TerrainQueueBase + priority;
            BatchMaterialID id = rendererGroup.RegisterMaterial(runtimeMaterial);
            registration = new MaterialRegistration(runtimeMaterial, id);
            materials.Add(key, registration);
            return registration;
        }

        private BatchID CreateBatch(GraphicsBuffer buffer)
        {
            var metadata = new NativeArray<MetadataValue>(1, Allocator.Temp);
            try
            {
                metadata[0] = new MetadataValue
                {
                    NameID = InstanceDataId,
                    // 自定义 AoS 加载：这里保存实例 0 的字节地址，不使用最高位数组协议。
                    Value = InstanceMetadataAddress
                };
                return rendererGroup.AddBatch(metadata, buffer.bufferHandle);
            }
            finally
            {
                metadata.Dispose();
            }
        }

        private unsafe JobHandle OnPerformCulling(BatchRendererGroup group,
            BatchCullingContext context, BatchCullingOutput output, IntPtr userContext)
        {
            lock (syncRoot)
            {
                int commandCount = 0;
                int visibleCount = 0;
                for (int i = 0; i < orderedBatches.Count; i++)
                {
                    if (orderedBatches[i].Count <= 0)
                        continue;
                    commandCount++;
                    visibleCount += orderedBatches[i].Count;
                }

                BatchCullingOutputDrawCommands* commands =
                    (BatchCullingOutputDrawCommands*)output.drawCommands.GetUnsafePtr();
                commands->drawCommandPickingInstanceIDs = null;
                commands->instanceSortingPositions = null;
                commands->instanceSortingPositionFloatCount = 0;
                commands->drawCommandCount = commandCount;
                commands->drawRangeCount = commandCount;
                commands->visibleInstanceCount = visibleCount;

                if (commandCount == 0)
                {
                    commands->drawCommands = null;
                    commands->drawRanges = null;
                    commands->visibleInstances = null;
                    return default;
                }

                int alignment = UnsafeUtility.AlignOf<long>();
                commands->drawCommands = (BatchDrawCommand*)UnsafeUtility.Malloc(
                    UnsafeUtility.SizeOf<BatchDrawCommand>() * commandCount, alignment, Allocator.TempJob);
                commands->drawRanges = (BatchDrawRange*)UnsafeUtility.Malloc(
                    UnsafeUtility.SizeOf<BatchDrawRange>() * commandCount, alignment, Allocator.TempJob);
                commands->visibleInstances = (int*)UnsafeUtility.Malloc(
                    sizeof(int) * visibleCount, alignment, Allocator.TempJob);

                int commandIndex = 0;
                int visibleOffset = 0;
                for (int i = 0; i < orderedBatches.Count; i++)
                {
                    TileBatch batch = orderedBatches[i];
                    if (batch.Count <= 0)
                        continue;

                    BatchDrawCommand* draw = commands->drawCommands + commandIndex;
                    draw->visibleOffset = (uint)visibleOffset;
                    draw->visibleCount = (uint)batch.Count;
                    draw->batchID = batch.BatchId;
                    draw->materialID = batch.MaterialId;
                    draw->meshID = batch.MeshId;
                    draw->submeshIndex = 0;
                    draw->splitVisibilityMask = 0xff;
                    draw->flags = 0;
                    draw->sortingPosition = batch.Priority;

                    BatchDrawRange* range = commands->drawRanges + commandIndex;
                    range->drawCommandsBegin = (uint)commandIndex;
                    range->drawCommandsCount = 1;
                    range->filterSettings = new BatchFilterSettings
                    {
                        renderingLayerMask = uint.MaxValue
                    };

                    for (int instance = 0; instance < batch.Count; instance++)
                        commands->visibleInstances[visibleOffset + instance] = instance;

                    visibleOffset += batch.Count;
                    commandIndex++;
                }
            }

            return default;
        }

        public void Dispose()
        {
            lock (syncRoot)
            {
                if (disposed)
                    return;
                disposed = true;

                for (int i = 0; i < orderedBatches.Count; i++)
                    orderedBatches[i].Dispose();
                orderedBatches.Clear();
                batches.Clear();
                ownerHandles.Clear();

                foreach (MaterialRegistration registration in materials.Values)
                {
                    rendererGroup.UnregisterMaterial(registration.Id);
                    DestroyRuntimeObject(registration.Material);
                }
                materials.Clear();

                foreach (MeshRegistration registration in meshes.Values)
                {
                    rendererGroup.UnregisterMesh(registration.Id);
                    DestroyRuntimeObject(registration.Mesh);
                }
                meshes.Clear();
                rendererGroup.Dispose();
            }
        }

        private static Material LoadTemplate(string resourcePath)
        {
            Material template = Resources.Load<Material>(resourcePath);
            if (template == null)
                throw new InvalidOperationException($"BRG 地图材质模板缺失：Resources/{resourcePath}");
            return template;
        }

        private static int ResolvePriority(VisualLayer layer) => layer switch
        {
            VisualLayer.Back => -1,
            VisualLayer.Ground => 0,
            VisualLayer.Water => 1,
            VisualLayer.Blocking => 4,
            _ => 0
        };

        private static void DestroyRuntimeObject(UnityEngine.Object target)
        {
            if (target == null)
                return;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(target);
            else
                UnityEngine.Object.DestroyImmediate(target);
        }

        private sealed class TileBatch : IDisposable
        {
            private readonly Backend backend;
            private readonly List<InstanceData> gpuData = new();
            private readonly List<InstanceOwner> owners = new();
            private readonly InstanceData[] scratch = new InstanceData[1];
            private GraphicsBuffer buffer;
            private int capacity;

            public TileBatch(Backend backend, VisualKey key, BatchMeshID meshId,
                BatchMaterialID materialId, int priority, int capacity)
            {
                this.backend = backend;
                Key = key;
                MeshId = meshId;
                MaterialId = materialId;
                Priority = priority;
                Resize(capacity);
            }

            public VisualKey Key { get; }
            public BatchMeshID MeshId { get; }
            public BatchMaterialID MaterialId { get; }
            public int Priority { get; }
            public BatchID BatchId { get; private set; }
            public int Count => gpuData.Count;

            public int Add(ChunkTilemapRenderer owner, int slotKey, InstanceData data)
            {
                if (gpuData.Count >= capacity)
                    Resize(capacity * 2);
                int index = gpuData.Count;
                gpuData.Add(data);
                owners.Add(new InstanceOwner(owner, slotKey));
                Upload(index, data);
                return index;
            }

            public void Update(int index, InstanceData data)
            {
                if ((uint)index >= (uint)gpuData.Count)
                    return;
                gpuData[index] = data;
                Upload(index, data);
            }

            public bool Remove(int index, out InstanceOwner movedOwner)
            {
                movedOwner = default;
                int last = gpuData.Count - 1;
                if ((uint)index > (uint)last)
                    return false;

                bool moved = index != last;
                if (moved)
                {
                    gpuData[index] = gpuData[last];
                    owners[index] = owners[last];
                    movedOwner = owners[index];
                    Upload(index, gpuData[index]);
                }
                gpuData.RemoveAt(last);
                owners.RemoveAt(last);
                return moved;
            }

            private void Resize(int newCapacity)
            {
                newCapacity = Mathf.Max(InitialCapacity, newCapacity);
                if (buffer != null)
                {
                    backend.rendererGroup.RemoveBatch(BatchId);
                    buffer.Dispose();
                }

                capacity = newCapacity;
                int byteCount = ZeroPrefixBytes + capacity * InstanceStride;
                buffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, byteCount / sizeof(int), sizeof(int))
                {
                    name = $"ChunkBRG_{Key.Layer}_{Key.SpriteId}"
                };
                // 第一份 112 字节零实例覆盖官方建议的 64 字节零前缀。
                scratch[0] = default;
                buffer.SetData(scratch, 0, 0, 1);
                BatchId = backend.CreateBatch(buffer);
                if (gpuData.Count > 0)
                {
                    InstanceData[] all = gpuData.ToArray();
                    buffer.SetData(all, 0, 1, all.Length);
                }
            }

            private void Upload(int index, InstanceData data)
            {
                scratch[0] = data;
                buffer.SetData(scratch, 0, index + 1, 1);
            }

            public void Dispose()
            {
                if (buffer == null)
                    return;
                backend.rendererGroup.RemoveBatch(BatchId);
                buffer.Dispose();
                buffer = null;
                gpuData.Clear();
                owners.Clear();
            }
        }

        private readonly struct InstanceHandle
        {
            public InstanceHandle(TileBatch batch, int index)
            {
                Batch = batch;
                Index = index;
            }

            public TileBatch Batch { get; }
            public int Index { get; }
        }

        private readonly struct InstanceOwner
        {
            public InstanceOwner(ChunkTilemapRenderer owner, int slotKey)
            {
                Owner = owner;
                SlotKey = slotKey;
            }

            public ChunkTilemapRenderer Owner { get; }
            public int SlotKey { get; }
        }

        private readonly struct VisualKey : IEquatable<VisualKey>
        {
            public VisualKey(VisualLayer layer, Sprite sprite, Material sourceMaterial)
            {
                Layer = layer;
                SpriteId = sprite.GetInstanceID();
                SourceMaterialId = sourceMaterial.GetInstanceID();
            }

            public VisualLayer Layer { get; }
            public int SpriteId { get; }
            public int SourceMaterialId { get; }

            public bool Equals(VisualKey other) => Layer == other.Layer && SpriteId == other.SpriteId &&
                                                   SourceMaterialId == other.SourceMaterialId;
            public override bool Equals(object obj) => obj is VisualKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine((int)Layer, SpriteId, SourceMaterialId);
        }

        private readonly struct MaterialKey : IEquatable<MaterialKey>
        {
            public MaterialKey(int templateId, int sourceId, int textureId, int priority)
            {
                TemplateId = templateId;
                SourceId = sourceId;
                TextureId = textureId;
                Priority = priority;
            }

            private int TemplateId { get; }
            private int SourceId { get; }
            private int TextureId { get; }
            private int Priority { get; }

            public bool Equals(MaterialKey other) => TemplateId == other.TemplateId && SourceId == other.SourceId &&
                                                     TextureId == other.TextureId && Priority == other.Priority;
            public override bool Equals(object obj) => obj is MaterialKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(TemplateId, SourceId, TextureId, Priority);
        }

        private readonly struct MeshRegistration
        {
            public MeshRegistration(Mesh mesh, BatchMeshID id)
            {
                Mesh = mesh;
                Id = id;
            }

            public Mesh Mesh { get; }
            public BatchMeshID Id { get; }
        }

        private readonly struct MaterialRegistration
        {
            public MaterialRegistration(Material material, BatchMaterialID id)
            {
                Material = material;
                Id = id;
            }

            public Material Material { get; }
            public BatchMaterialID Id { get; }
        }
    }

    #endregion
}
