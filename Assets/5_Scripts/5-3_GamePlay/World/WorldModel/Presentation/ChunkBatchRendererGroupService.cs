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

    /// <summary>Editor 与 Development Build 只记录 BRG 异常与手动诊断，正式非开发包保持静默。</summary>
    private static bool RenderDebugEnabled => Application.isEditor || Debug.isDebugBuild;

    internal enum VisualLayer : byte
    {
        Back,
        Ground,
        Water,
        Support,
        Environment,
        Snow,
        Blocking,
        SnowWall,
        Grass,
        GroundCover,
        MechanicalLowerBase,
        MechanicalLowerMotionA,
        MechanicalLowerMotionB,
        MechanicalLowerFront,
        MechanicalUpperBase,
        MechanicalUpperMotionA,
        MechanicalUpperMotionB,
        MechanicalUpperFront
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct InstanceData
    {
        public Vector4 Transform0;
        public Vector4 Transform1;
        public Vector4 Data0;
        public Vector4 Data1;
        public Vector4 Tint;
        public Vector4 FlowX; // 河流为四格角速度，海洋为当前格风场单位方向 X。
        public Vector4 FlowY; // 河流为四格角速度，海洋为当前格风场单位方向 Y。

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
            : this(layer, sprite, sourceMaterial, data, false, default)
        {
        }

        /// <summary>需要透明深度排序的实例额外携带稳定世界锚点；机械建筑用它按 Y 轴排序整组子图层。</summary>
        public Visual(VisualLayer layer, Sprite sprite, Material sourceMaterial, InstanceData data,
            Vector3 sortingPosition)
            : this(layer, sprite, sourceMaterial, data, true, sortingPosition)
        {
        }

        private Visual(VisualLayer layer, Sprite sprite, Material sourceMaterial, InstanceData data,
            bool depthSorted, Vector3 sortingPosition)
        {
            Layer = layer;
            Sprite = sprite;
            SourceMaterial = sourceMaterial;
            Data = data;
            DepthSorted = depthSorted;
            SortingPosition = sortingPosition;
        }

        public VisualLayer Layer { get; }
        public Sprite Sprite { get; }
        public Material SourceMaterial { get; }
        public InstanceData Data { get; }
        public bool DepthSorted { get; }
        public Vector3 SortingPosition { get; }
    }

    private static Backend backend;

    static ChunkBatchRendererGroupService()
    {
        // 资源会话结束必须先注销 BRG；共享缓存本身不依赖地形后端。
        SharedSpriteMeshCache.Clearing += ResetStatics;
    }

    internal static void RegisterOwner(ChunkTilemapRenderer owner, Bounds worldBounds)
    {
        if (owner == null)
            return;
        EnsureBackend().RegisterOwner(owner, worldBounds);
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
        // 只有 RegisterOwner 能创建后端。增量提交不能借 SetVisual 偷偷复活后端，
        // 否则一次脏格刷新就可能制造“只登记了局部实例”的伪完整 Owner。
        backend?.SetVisual(owner, slotKey, visual);
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

    /// <summary>只统计基础地形图层，避免同 Owner 的草花扩展实例污染地形对账。</summary>
    internal static int GetOwnerTerrainVisualCount(ChunkTilemapRenderer owner)
    {
        return owner != null && backend != null ? backend.GetOwnerTerrainVisualCount(owner) : 0;
    }

    /// <summary>构造当前 BRG 后端快照，供运行时日志和 Inspector ContextMenu 定向排查。</summary>
    internal static string BuildDebugSummary(ChunkTilemapRenderer owner = null)
    {
        return backend != null
            ? backend.BuildDebugSummary(owner)
            : "[ChunkRenderDebug] backend=null owners=0 batches=0 instances=0 culls=0";
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        if (RenderDebugEnabled && backend != null)
            Debug.LogWarning("[ChunkRenderDebug] BRG backend 正在 Reset；现有 ChunkView 的基础地块表现需要重新登记。 " +
                             backend.BuildDebugSummary());
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
        private const string MechanicalKeyword = "_CHUNK_MECHANICAL";
        private static readonly int InstanceDataId = Shader.PropertyToID("_ChunkInstanceData");
        private static readonly int ElevationStrengthId = Shader.PropertyToID("_ElevationStrength");
        private static readonly int ElevationEdgeWidthId = Shader.PropertyToID("_ElevationEdgeWidth");
        private static readonly int GrassSwayEnabledId = Shader.PropertyToID("_GrassSwayEnabled");
        private const string GrassSwayKeyword = "_CHUNK_GRASS_SWAY";

        private readonly object syncRoot = new();
        private readonly BatchRendererGroup rendererGroup;
        private readonly Dictionary<ChunkTilemapRenderer, Dictionary<int, InstanceHandle>> ownerHandles = new();
        /// <summary>注册时建立区块可见状态，剔除回调不再逐实例查字典。</summary>
        private readonly Dictionary<int, OwnerCullingState> ownerCullingById = new();
        private readonly List<OwnerCullingState> activeOwnerCullingStates = new();
        /// <summary>每个批次复用本次剔除的可见实例索引。</summary>
        private readonly List<List<int>> batchVisibleInstances = new();
        /// <summary>实例或批次变化后，下次局部剔除重新收集可见索引。</summary>
        private bool visibleInstanceListsDirty = true;
        private readonly Dictionary<VisualKey, TileBatch> batches = new();
        private readonly List<TileBatch> orderedBatches = new();
        private readonly HashSet<TileBatch> ownerRemovalBatches = new();
        private readonly Dictionary<int, MeshRegistration> meshes = new();
        private readonly Dictionary<MaterialKey, MaterialRegistration> materials = new();
        private readonly HashSet<int> warnedMissingOwnerIds = new();
        private readonly Material spriteTemplate;
        private readonly Material contactTemplate;
        private readonly Material waterTemplate;
        private long cullingCallbackCount;
        private long lastZeroVisibleWarningCull;
        private int lastCullingCommandCount;
        private int lastCullingVisibleCount;
        private bool disposed;

        public Backend()
        {
            spriteTemplate = LoadTemplate("Config/WorldModel/BRG/ChunkBRG-Sprite-Lit");
            contactTemplate = LoadTemplate("Config/WorldModel/BRG/ChunkBRG-Contact-Lit");
            waterTemplate = LoadTemplate("Config/WorldModel/BRG/ChunkBRG-Water-Lit");
            rendererGroup = new BatchRendererGroup(OnPerformCulling, IntPtr.Zero);
            GroundElevationShadowSettings.Changed += ApplyGroundElevationPreference;
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

        public void RegisterOwner(ChunkTilemapRenderer owner, Bounds worldBounds)
        {
            lock (syncRoot)
            {
                if (!ownerHandles.ContainsKey(owner))
                    ownerHandles.Add(owner, new Dictionary<int, InstanceHandle>());
                int ownerId = owner.GetInstanceID();
                RegisterOwnerCullingState(ownerId, worldBounds);
                warnedMissingOwnerIds.Remove(ownerId);
            }
        }

        /// <summary>复用区块状态，使已有实例自动读取最新边界。</summary>
        private void RegisterOwnerCullingState(int ownerId, Bounds worldBounds)
        {
            if (ownerCullingById.TryGetValue(ownerId, out OwnerCullingState state))
            {
                state.Bounds = worldBounds;
                return;
            }

            state = new OwnerCullingState(worldBounds, activeOwnerCullingStates.Count);
            ownerCullingById.Add(ownerId, state);
            activeOwnerCullingStates.Add(state);
            visibleInstanceListsDirty = true;
        }

        public bool IsOwnerRegistered(ChunkTilemapRenderer owner)
        {
            lock (syncRoot)
                return owner != null && ownerHandles.ContainsKey(owner);
        }

        public int GetOwnerTerrainVisualCount(ChunkTilemapRenderer owner)
        {
            lock (syncRoot)
            {
                if (owner == null || !ownerHandles.TryGetValue(owner, out Dictionary<int, InstanceHandle> handles))
                    return 0;

                int count = 0;
                foreach (InstanceHandle handle in handles.Values)
                {
                    VisualLayer layer = handle.Batch.Key.Layer;
                    if (layer is VisualLayer.Back or VisualLayer.Ground or VisualLayer.Water or VisualLayer.Blocking)
                        count++;
                }
                return count;
            }
        }

        public string BuildDebugSummary(ChunkTilemapRenderer owner = null)
        {
            lock (syncRoot)
            {
                int totalInstances = 0;
                int activeBatches = 0;
                for (int i = 0; i < orderedBatches.Count; i++)
                {
                    int count = orderedBatches[i].Count;
                    totalInstances += count;
                    if (count > 0)
                        activeBatches++;
                }

                int ownerInstances = owner != null && ownerHandles.TryGetValue(owner,
                    out Dictionary<int, InstanceHandle> handles)
                    ? handles.Count
                    : -1;
                string ownerPart = owner != null
                    ? $" owner={owner.name} ownerRegistered={ownerInstances >= 0} ownerInstances={Mathf.Max(0, ownerInstances)}"
                    : string.Empty;
                return $"[ChunkRenderDebug] ownerCulling={WorldStreamingPreferences.OwnerCullingEnabled} " +
                       $"owners={ownerHandles.Count} batches={batches.Count} activeBatches={activeBatches} " +
                       $"instances={totalInstances} meshes={meshes.Count} materials={materials.Count} " +
                       $"culls={cullingCallbackCount} lastCullCommands={lastCullingCommandCount} " +
                       $"lastCullVisible={lastCullingVisibleCount}{ownerPart}";
            }
        }

        public void UnregisterOwner(ChunkTilemapRenderer owner)
        {
            if (owner == null)
                return;
            lock (syncRoot)
            {
                if (!ownerHandles.TryGetValue(owner, out Dictionary<int, InstanceHandle> handles))
                    return;

                ownerRemovalBatches.Clear();
                foreach (InstanceHandle handle in handles.Values)
                {
                    if (ownerRemovalBatches.Add(handle.Batch))
                        handle.Batch.BeginBulkRemoval();
                }

                try
                {
                    while (handles.Count > 0)
                    {
                        using Dictionary<int, InstanceHandle>.Enumerator enumerator = handles.GetEnumerator();
                        enumerator.MoveNext();
                        RemoveHandle(owner, enumerator.Current.Key, enumerator.Current.Value, handles);
                    }
                }
                finally
                {
                    foreach (TileBatch batch in ownerRemovalBatches)
                        batch.EndBulkRemoval();
                    ownerRemovalBatches.Clear();
                }

                ownerHandles.Remove(owner);
                UnregisterOwnerCullingState(owner.GetInstanceID());
            }
        }

        /// <summary>交换删除区块状态，避免大量区块卸载时线性搬移列表。</summary>
        private void UnregisterOwnerCullingState(int ownerId)
        {
            if (!ownerCullingById.TryGetValue(ownerId, out OwnerCullingState state))
                return;

            int lastIndex = activeOwnerCullingStates.Count - 1;
            if (state.Index != lastIndex)
            {
                OwnerCullingState moved = activeOwnerCullingStates[lastIndex];
                activeOwnerCullingStates[state.Index] = moved;
                moved.Index = state.Index;
            }
            activeOwnerCullingStates.RemoveAt(lastIndex);
            ownerCullingById.Remove(ownerId);
            visibleInstanceListsDirty = true;
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
                    if (RenderDebugEnabled && warnedMissingOwnerIds.Add(owner.GetInstanceID()))
                    {
                        Debug.LogWarning($"[ChunkRenderDebug] SetVisualRejected owner={owner.name} slot={slotKey} " +
                                         $"layer={visual.Layer} reason=owner-not-registered. " +
                                         "禁止通过单格增量提交隐式创建 Owner，避免只恢复局部地块。");
                    }
                    return;
                }

                VisualKey key = new(visual.Layer, visual.Sprite, visual.SourceMaterial);
                if (handles.TryGetValue(slotKey, out InstanceHandle current))
                {
                    if (current.Batch.Key.Equals(key))
                    {
                        current.Batch.Update(current.Index, visual.Data, visual.SortingPosition);
                        return;
                    }

                    RemoveHandle(owner, slotKey, current, handles);
                }

                TileBatch batch = GetOrCreateBatch(key, visual);
                OwnerCullingState cullingState = ownerCullingById[owner.GetInstanceID()];
                int index = batch.Add(owner, slotKey, cullingState, visual.Data, visual.SortingPosition);
                handles[slotKey] = new InstanceHandle(batch, index);
                visibleInstanceListsDirty = true;
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
            visibleInstanceListsDirty = true;
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
                visual.DepthSorted, InitialCapacity);
            batches.Add(key, batch);
            orderedBatches.Add(batch);
            orderedBatches.Sort(CompareBatchOrder);
            return batch;
        }

        /// <summary>保持同队列的草与花按图层稳定排序，花批次始终绘制在草批次上方。</summary>
        private static int CompareBatchOrder(TileBatch left, TileBatch right)
        {
            int priority = left.Priority.CompareTo(right.Priority);
            return priority != 0 ? priority : left.Key.Layer.CompareTo(right.Key.Layer);
        }

        private MeshRegistration GetOrCreateMesh(Sprite sprite)
        {
            int key = sprite.GetInstanceID();
            if (meshes.TryGetValue(key, out MeshRegistration registration))
                return registration;

            // Mesh 属于资源会话；注册 ID 只保存在当前 Backend，重建 BRG 后重新注册。
            Mesh mesh = SharedSpriteMeshCache.GetOrCreate(sprite);
            BatchMeshID id = rendererGroup.RegisterMesh(mesh);
            registration = new MeshRegistration(id);
            meshes.Add(key, registration);
            return registration;
        }

        private MaterialRegistration GetOrCreateMaterial(VisualLayer layer, Texture texture,
            Material sourceMaterial)
        {
            Material template = layer switch
            {
                VisualLayer.Ground or VisualLayer.Support => contactTemplate,
                VisualLayer.Water => waterTemplate,
                _ => spriteTemplate
            };
            int queuePriority = ResolveRenderQueuePriority(layer);
            var key = new MaterialKey(template.GetInstanceID(), sourceMaterial.GetInstanceID(),
                texture != null ? texture.GetInstanceID() : 0, queuePriority);
            if (materials.TryGetValue(key, out MaterialRegistration registration))
                return registration;

            var runtimeMaterial = new Material(template)
            {
                name = $"ChunkBRG_{layer}_{sourceMaterial.name}_{texture?.name}",
                hideFlags = HideFlags.HideAndDontSave,
                enableInstancing = true,
                // BRG 没有 SpriteRenderer/TilemapRenderer 的 Sorting Layer 字段；草花共用 Default/0 与 2993 队列。
                renderQueue = TerrainQueueBase + queuePriority
            };
            runtimeMaterial.CopyPropertiesFromMaterial(sourceMaterial);
            runtimeMaterial.shaderKeywords = sourceMaterial.shaderKeywords;
            runtimeMaterial.SetTexture("_MainTex", texture);
            runtimeMaterial.renderQueue = TerrainQueueBase + queuePriority;
            if (sourceMaterial.HasProperty(GrassSwayEnabledId) &&
                sourceMaterial.GetFloat(GrassSwayEnabledId) > 0.5f)
                runtimeMaterial.EnableKeyword(GrassSwayKeyword);
            else
                runtimeMaterial.DisableKeyword(GrassSwayKeyword);
            if (IsMechanicalLayer(layer)) runtimeMaterial.EnableKeyword(MechanicalKeyword);
            else runtimeMaterial.DisableKeyword(MechanicalKeyword);
            if (layer == VisualLayer.Ground)
                ApplyGroundElevationPreference(runtimeMaterial);
            BatchMaterialID id = rendererGroup.RegisterMaterial(runtimeMaterial);
            registration = new MaterialRegistration(runtimeMaterial, id, layer);
            materials.Add(key, registration);
            return registration;
        }

        /// <summary>立即刷新已有地面批次，宽度调整不需要重新上传地块实例。</summary>
        private void ApplyGroundElevationPreference()
        {
            lock (syncRoot)
            {
                foreach (MaterialRegistration registration in materials.Values)
                {
                    if (registration.Layer == VisualLayer.Ground)
                        ApplyGroundElevationPreference(registration.Material);
                }
            }
        }

        /// <summary>只覆盖高度着色参数，保持源材质的墙脚接触阴影配置。</summary>
        private static void ApplyGroundElevationPreference(Material material)
        {
            material.SetFloat(ElevationStrengthId, GroundElevationShadowSettings.Enabled ? 1f : 0f);
            material.SetFloat(ElevationEdgeWidthId, GroundElevationShadowSettings.Width);
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
                cullingCallbackCount++;
                int commandCount = 0;
                int visibleCount = 0;
                int sortedVisibleCount = 0;
                int submittedCount = 0;
                bool ownerVisibilityChanged = false;
                bool allOwnersVisible = !WorldStreamingPreferences.OwnerCullingEnabled ||
                    RefreshOwnerVisibility(context.cullingPlanes, out ownerVisibilityChanged);
                if (!allOwnersVisible && (visibleInstanceListsDirty || ownerVisibilityChanged))
                    RebuildVisibleInstanceLists();
                for (int i = 0; i < orderedBatches.Count; i++)
                {
                    TileBatch batch = orderedBatches[i];
                    int batchCount = batch.Count;
                    submittedCount += batchCount;
                    int batchVisible = allOwnersVisible ? batchCount : batchVisibleInstances[i].Count;
                    if (batchVisible <= 0)
                        continue;
                    commandCount += batch.DepthSorted ? batchVisible : 1;
                    if (batch.DepthSorted) sortedVisibleCount += batchVisible;
                    visibleCount += batchVisible;
                }
                lastCullingCommandCount = commandCount;
                lastCullingVisibleCount = visibleCount;

                if (RenderDebugEnabled && ownerHandles.Count > 0 && submittedCount == 0 &&
                    cullingCallbackCount - lastZeroVisibleWarningCull >= 180)
                {
                    lastZeroVisibleWarningCull = cullingCallbackCount;
                    Debug.LogWarning($"[ChunkRenderDebug] BRG 有 {ownerHandles.Count} 个 Owner，但没有提交可绘制实例。 " +
                                     BuildDebugSummary());
                }

                BatchCullingOutputDrawCommands* commands =
                    (BatchCullingOutputDrawCommands*)output.drawCommands.GetUnsafePtr();
                commands->drawCommandPickingInstanceIDs = null;
                commands->instanceSortingPositions = sortedVisibleCount > 0
                    ? (float*)UnsafeUtility.Malloc(sizeof(float) * sortedVisibleCount * 3,
                        UnsafeUtility.AlignOf<float>(), Allocator.TempJob)
                    : null;
                commands->instanceSortingPositionFloatCount = sortedVisibleCount * 3;
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
                int sortingFloatOffset = 0;
                for (int i = 0; i < orderedBatches.Count; i++)
                {
                    TileBatch batch = orderedBatches[i];
                    List<int> visibleInstances = allOwnersVisible ? null : batchVisibleInstances[i];
                    int batchVisible = allOwnersVisible ? batch.Count : visibleInstances.Count;
                    if (batchVisible <= 0)
                        continue;

                    if (batch.DepthSorted)
                    {
                        for (int visibleIndex = 0; visibleIndex < batchVisible; visibleIndex++)
                        {
                            int instance = allOwnersVisible ? visibleIndex : visibleInstances[visibleIndex];

                            BatchDrawCommand* sortedDraw = commands->drawCommands + commandIndex;
                            sortedDraw->visibleOffset = (uint)visibleOffset;
                            sortedDraw->visibleCount = 1;
                            sortedDraw->batchID = batch.BatchId;
                            sortedDraw->materialID = batch.MaterialId;
                            sortedDraw->meshID = batch.MeshId;
                            sortedDraw->submeshIndex = 0;
                            sortedDraw->splitVisibilityMask = 0xff;
                            sortedDraw->flags = BatchDrawCommandFlags.HasSortingPosition;
                            sortedDraw->sortingPosition = sortingFloatOffset;

                            Vector3 position = batch.GetSortingPosition(instance);
                            commands->instanceSortingPositions[sortingFloatOffset] = position.x;
                            commands->instanceSortingPositions[sortingFloatOffset + 1] = position.y;
                            commands->instanceSortingPositions[sortingFloatOffset + 2] = position.z;
                            commands->visibleInstances[visibleOffset] = instance;

                            BatchDrawRange* sortedRange = commands->drawRanges + commandIndex;
                            sortedRange->drawCommandsBegin = (uint)commandIndex;
                            sortedRange->drawCommandsCount = 1;
                            sortedRange->filterSettings = new BatchFilterSettings
                            {
                                renderingLayerMask = uint.MaxValue,
                                allDepthSorted = true
                            };

                            sortingFloatOffset += 3;
                            visibleOffset++;
                            commandIndex++;
                        }
                        continue;
                    }

                    BatchDrawCommand* draw = commands->drawCommands + commandIndex;
                    draw->visibleOffset = (uint)visibleOffset;
                    draw->visibleCount = (uint)batchVisible;
                    draw->batchID = batch.BatchId;
                    draw->materialID = batch.MaterialId;
                    draw->meshID = batch.MeshId;
                    draw->submeshIndex = 0;
                    draw->splitVisibilityMask = 0xff;
                    draw->flags = 0;
                    draw->sortingPosition = 0;

                    BatchDrawRange* range = commands->drawRanges + commandIndex;
                    range->drawCommandsBegin = (uint)commandIndex;
                    range->drawCommandsCount = 1;
                    range->filterSettings = new BatchFilterSettings
                    {
                        renderingLayerMask = uint.MaxValue,
                        allDepthSorted = false
                    };

                    for (int visibleIndex = 0; visibleIndex < batchVisible; visibleIndex++)
                        commands->visibleInstances[visibleOffset + visibleIndex] =
                            allOwnersVisible ? visibleIndex : visibleInstances[visibleIndex];

                    visibleOffset += batchVisible;
                    commandIndex++;
                }
            }

            return default;
        }

        /// <summary>只在区块可见状态变化时通知重建实例索引，镜头在区块内移动可复用旧结果。</summary>
        private bool RefreshOwnerVisibility(NativeArray<Plane> planes, out bool visibilityChanged)
        {
            bool allVisible = true;
            visibilityChanged = false;
            for (int i = 0; i < activeOwnerCullingStates.Count; i++)
            {
                OwnerCullingState state = activeOwnerCullingStates[i];
                bool visible = IsOwnerVisible(state.Bounds, planes);
                if (state.Visible != visible)
                {
                    state.Visible = visible;
                    visibilityChanged = true;
                }
                if (!visible)
                    allVisible = false;
            }
            return allVisible;
        }

        /// <summary>批次顺序或区块可见集合改变后，才扫描实例生成可见索引。</summary>
        private void RebuildVisibleInstanceLists()
        {
            for (int i = 0; i < orderedBatches.Count; i++)
            {
                TileBatch batch = orderedBatches[i];
                if (i == batchVisibleInstances.Count)
                    batchVisibleInstances.Add(new List<int>(Mathf.Min(batch.Count, 64)));
                batch.CollectVisibleInstances(batchVisibleInstances[i]);
            }
            visibleInstanceListsDirty = false;
        }

        /// <summary>按区块边界与相机裁剪面判断，预加载区块靠近镜头时直接显示。</summary>
        private static bool IsOwnerVisible(Bounds bounds, NativeArray<Plane> planes)
        {
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;
            for (int i = 0; i < planes.Length; i++)
            {
                Plane plane = planes[i];
                Vector3 normal = plane.normal;
                float radius = Mathf.Abs(normal.x) * extents.x +
                               Mathf.Abs(normal.y) * extents.y +
                               Mathf.Abs(normal.z) * extents.z;
                if (Vector3.Dot(normal, center) + plane.distance + radius >= 0f)
                    continue;
                return false;
            }
            return true;
        }

        public void Dispose()
        {
            lock (syncRoot)
            {
                if (disposed)
                    return;
                disposed = true;
                GroundElevationShadowSettings.Changed -= ApplyGroundElevationPreference;

                for (int i = 0; i < orderedBatches.Count; i++)
                    orderedBatches[i].Dispose();
                orderedBatches.Clear();
                batches.Clear();
                ownerHandles.Clear();
                ownerCullingById.Clear();
                activeOwnerCullingStates.Clear();
                batchVisibleInstances.Clear();

                foreach (MaterialRegistration registration in materials.Values)
                {
                    rendererGroup.UnregisterMaterial(registration.Id);
                    DestroyRuntimeObject(registration.Material);
                }
                materials.Clear();

                foreach (MeshRegistration registration in meshes.Values)
                {
                    rendererGroup.UnregisterMesh(registration.Id);
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
            VisualLayer.Support or VisualLayer.Environment => 2,
            VisualLayer.Snow => 3,
            VisualLayer.Blocking => 4,
            VisualLayer.SnowWall => 5,
            VisualLayer.Grass => 5,
            VisualLayer.GroundCover => 6,
            VisualLayer.MechanicalLowerBase => 7,
            VisualLayer.MechanicalLowerMotionA => 8,
            VisualLayer.MechanicalLowerMotionB => 9,
            VisualLayer.MechanicalLowerFront => 10,
            VisualLayer.MechanicalUpperBase => 11,
            VisualLayer.MechanicalUpperMotionA => 12,
            VisualLayer.MechanicalUpperMotionB => 13,
            VisualLayer.MechanicalUpperFront => 14,
            _ => 0
        };

        /// <summary>机械所有子层共享一个透明队列，真正的前后关系交给节点 Y 锚点；草花仍保持既有固定队列。</summary>
        private static int ResolveRenderQueuePriority(VisualLayer layer) => layer switch
        {
            VisualLayer.SnowWall or VisualLayer.Grass or VisualLayer.GroundCover => 5,
            VisualLayer.MechanicalLowerBase or VisualLayer.MechanicalLowerMotionA or
                VisualLayer.MechanicalLowerMotionB or VisualLayer.MechanicalLowerFront or
                VisualLayer.MechanicalUpperBase or VisualLayer.MechanicalUpperMotionA or
                VisualLayer.MechanicalUpperMotionB or VisualLayer.MechanicalUpperFront => 6,
            _ => ResolvePriority(layer)
        };

        /// <summary>机械实例使用同一 BRG 后端，但启用专属 GPU 动画变体。</summary>
        private static bool IsMechanicalLayer(VisualLayer layer)
            => layer >= VisualLayer.MechanicalLowerBase && layer <= VisualLayer.MechanicalUpperFront;

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
            private readonly List<Vector3> sortingPositions = new();
            private readonly InstanceData[] scratch = new InstanceData[1];
            private GraphicsBuffer buffer;
            private int capacity;
            private bool bulkRemoving;
            private readonly List<int> bulkDirtyIndices = new();

            public TileBatch(Backend backend, VisualKey key, BatchMeshID meshId,
                BatchMaterialID materialId, int priority, bool depthSorted, int capacity)
            {
                this.backend = backend;
                Key = key;
                MeshId = meshId;
                MaterialId = materialId;
                Priority = priority;
                DepthSorted = depthSorted;
                Resize(capacity);
            }

            public VisualKey Key { get; }
            public BatchMeshID MeshId { get; }
            public BatchMaterialID MaterialId { get; }
            public int Priority { get; }
            public bool DepthSorted { get; }
            public BatchID BatchId { get; private set; }
            public int Count => gpuData.Count;
            public Vector3 GetSortingPosition(int index) => sortingPositions[index];

            /// <summary>只在可见区块集合变化时扫描本批次，平常复用上次的索引。</summary>
            public void CollectVisibleInstances(List<int> visibleInstances)
            {
                visibleInstances.Clear();
                int count = owners.Count;
                for (int index = 0; index < count; index++)
                {
                    if (owners[index].CullingState.Visible)
                        visibleInstances.Add(index);
                }
            }

            public int Add(ChunkTilemapRenderer owner, int slotKey, OwnerCullingState cullingState,
                InstanceData data, Vector3 sortingPosition)
            {
                if (gpuData.Count >= capacity)
                    Resize(capacity * 2);
                int index = gpuData.Count;
                gpuData.Add(data);
                owners.Add(new InstanceOwner(owner, slotKey, cullingState));
                sortingPositions.Add(sortingPosition);
                Upload(index, data);
                return index;
            }

            public void Update(int index, InstanceData data, Vector3 sortingPosition)
            {
                if ((uint)index >= (uint)gpuData.Count)
                    return;
                gpuData[index] = data;
                sortingPositions[index] = sortingPosition;
                Upload(index, data);
            }

            /// <summary>同一区块的实例先在 CPU 上交换删除，结束时合并写回 GPU。</summary>
            public void BeginBulkRemoval()
            {
                bulkRemoving = true;
                bulkDirtyIndices.Clear();
            }

            public void EndBulkRemoval()
            {
                bulkRemoving = false;
                bulkDirtyIndices.Sort();
                int rangeStart = -1;
                int rangeEnd = -1;
                for (int i = 0; i < bulkDirtyIndices.Count; i++)
                {
                    int index = bulkDirtyIndices[i];
                    if (index >= gpuData.Count)
                        break;
                    if (rangeStart >= 0 && index <= rangeEnd + 1)
                    {
                        rangeEnd = Mathf.Max(rangeEnd, index);
                        continue;
                    }
                    UploadRange(rangeStart, rangeEnd);
                    rangeStart = rangeEnd = index;
                }
                UploadRange(rangeStart, rangeEnd);
                bulkDirtyIndices.Clear();
            }

            private void UploadRange(int start, int end)
            {
                if (start >= 0)
                    buffer.SetData(gpuData, start, start + 1, end - start + 1);
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
                    sortingPositions[index] = sortingPositions[last];
                    movedOwner = owners[index];
                    if (bulkRemoving)
                        bulkDirtyIndices.Add(index);
                    else
                        Upload(index, gpuData[index]);
                }
                gpuData.RemoveAt(last);
                owners.RemoveAt(last);
                sortingPositions.RemoveAt(last);
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
                sortingPositions.Clear();
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
            public InstanceOwner(ChunkTilemapRenderer owner, int slotKey, OwnerCullingState cullingState)
            {
                Owner = owner;
                SlotKey = slotKey;
                CullingState = cullingState;
            }

            public ChunkTilemapRenderer Owner { get; }
            public int SlotKey { get; }
            /// <summary>区块共享的本次相机可见状态。</summary>
            public OwnerCullingState CullingState { get; }
        }

        /// <summary>一个区块只保存一份边界和本次相机可见结果。</summary>
        private sealed class OwnerCullingState
        {
            public OwnerCullingState(Bounds bounds, int index)
            {
                Bounds = bounds;
                Index = index;
            }

            public Bounds Bounds { get; set; }
            public int Index { get; set; }
            public bool Visible { get; set; }
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
            public MeshRegistration(BatchMeshID id)
            {
                Id = id;
            }

            public BatchMeshID Id { get; }
        }

        private readonly struct MaterialRegistration
        {
            public MaterialRegistration(Material material, BatchMaterialID id, VisualLayer layer)
            {
                Material = material;
                Id = id;
                Layer = layer;
            }

            public Material Material { get; }
            public BatchMaterialID Id { get; }
            public VisualLayer Layer { get; }
        }
    }

    #endregion
}
