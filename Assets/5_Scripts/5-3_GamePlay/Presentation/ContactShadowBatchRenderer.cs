using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 场景级接触阴影 BRG：每个排序批次的椭圆共用网格、材质和一条绘制命令。
/// 实例数据每只 32 字节，批次内容或数量变化时才上传有效阴影；不为单只物品创建 Renderer。
/// </summary>
internal sealed class ContactShadowBatchRenderer : IDisposable
{
    #region 批次资源

    private const int InitialCapacity = 64;
    private const int InstanceStride = 32;
    private const int ZeroPrefixBytes = 64;
    private const int ShadowRenderQueue = 2995; // 旧实体位于地形 BRG 之后。
    private static readonly ProfilerMarker CullingMarker = new("FlatWorld.Rendering.ContactShadow.OnPerformCulling");
    private static readonly int InstanceDataId = Shader.PropertyToID("_ContactShadowData");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private readonly object syncRoot = new();
    private readonly BatchRendererGroup group;
    private readonly bool useConstantBuffer;
    private readonly int constantBufferPageCapacity;
    private readonly List<ConstantBufferPage> constantPages = new();
    private readonly Mesh mesh;
    private readonly Material material;
    private GraphicsBuffer buffer;
    private BatchID batchId;
    private BatchMeshID meshId;
    private BatchMaterialID materialId;
    private InstanceData[] instances = new InstanceData[InitialCapacity];
    private int[] visibleInstanceIndices = new int[InitialCapacity];
    private int count;
    private int constantBufferCapacity;
    private int uploadedCount;
    private bool dataChanged = true;
    private bool previousFrameComplete;
    private bool disposed;
    private Color batchColor;
    private float globalOpacity = 1f;

    [StructLayout(LayoutKind.Sequential)]
    internal struct InstanceData
    {
        public Vector4 Footprint; // 中心 XY 与半宽、高。
        public Vector4 Appearance; // X 为本只阴影透明度。
    }

    /// <summary>共享材质和单位方片只创建一次；BRG 按实例生成柔边椭圆。</summary>
    public ContactShadowBatchRenderer(int renderQueue = ShadowRenderQueue)
    {
        useConstantBuffer = BatchRendererGroup.BufferTarget == BatchBufferTarget.ConstantBuffer;
        if (useConstantBuffer)
        {
            constantBufferPageCapacity = (BatchRendererGroup.GetConstantBufferMaxWindowSize() - ZeroPrefixBytes) / InstanceStride;
            if (constantBufferPageCapacity < 1)
                throw new InvalidOperationException("当前图形设备的 BRG 常量缓冲窗口无法容纳接触阴影实例。");
        }
        Material template = Resources.Load<Material>("AIECS/ActorBlobShadow");
        if (template == null) throw new InvalidOperationException("缺少 AIECS/ActorBlobShadow 阴影材质。");
        material = new Material(template)
        {
            name = "ContactShadows-BRG",
            hideFlags = HideFlags.HideAndDontSave,
            enableInstancing = true,
            renderQueue = renderQueue
        };
        batchColor = material.HasProperty(ColorId) ? material.GetColor(ColorId) : Color.white;
        mesh = CreateQuad();
        group = new BatchRendererGroup(OnPerformCulling, IntPtr.Zero);
        group.SetGlobalBounds(new Bounds(Vector3.zero, new Vector3(2000000f, 2000000f, 1000f)));
        meshId = group.RegisterMesh(mesh);
        materialId = group.RegisterMaterial(material);
        Resize(InitialCapacity);
    }

    /// <summary>开始收集本帧所有 Item 的接触阴影。</summary>
    public void BeginFrame()
    {
        lock (syncRoot)
        {
            count = 0;
            // 上次收集若被异常中断，CPU 数组不再代表 GPU 已上传内容，必须重新提交。
            dataChanged = !previousFrameComplete;
            previousFrameComplete = false;
        }
    }

    /// <summary>追加一个世界空间椭圆；同一材质可汇聚不同尺寸和透明度。</summary>
    public void Append(Vector2 center, float width, float height, float opacity)
    {
        lock (syncRoot)
        {
            if (count == instances.Length)
            {
                int nextCapacity = instances.Length * 2;
                Array.Resize(ref instances, nextCapacity);
                Array.Resize(ref visibleInstanceIndices, nextCapacity);
                Resize(nextCapacity);
            }
            InstanceData next = new InstanceData
            {
                Footprint = new Vector4(center.x, center.y, width * 0.5f, height * 0.5f),
                Appearance = new Vector4(opacity, 0f, 0f, 0f)
            };
            // 保持每帧完整收集与裁剪，仅当同一槽位内容变化时标记 GPU 上传。
            // Equals 使用精确分量比较，避免近似相等吞掉轻微阴影移动。
            if (!dataChanged && (count >= uploadedCount ||
                !instances[count].Footprint.Equals(next.Footprint) ||
                !instances[count].Appearance.Equals(next.Appearance)))
                dataChanged = true;
            instances[count++] = next;
        }
    }

    /// <summary>内容变化时上传本帧实例；静止帧复用 GPU 数据，空帧由裁剪回调停止绘制。</summary>
    public void EndFrame()
    {
        lock (syncRoot)
        {
            if (count > 0 && (dataChanged || count != uploadedCount))
            {
                if (useConstantBuffer)
                {
                    // 每个 GLES UBO 窗口只上传自己对应的实例区间。
                    for (int start = 0, page = 0; start < count; start += constantBufferPageCapacity, page++)
                        constantPages[page].Buffer.SetData(instances, start, ZeroPrefixBytes / InstanceStride,
                            Mathf.Min(constantBufferPageCapacity, count - start));
                }
                else
                    buffer.SetData(instances, 0, ZeroPrefixBytes / InstanceStride, count);
                uploadedCount = count;
            }
            // 空帧直接由 count 禁止绘制，缓冲区原数据可以在恢复时继续复用。
            // 若此前有未完成的帧写入 CPU 数组，空帧不能错误地宣布 GPU 缓存有效。
            previousFrameComplete = count > 0 || !dataChanged;
        }
    }

    /// <summary>只改整批透明度；实例位置不变时无需重新扫描和上传所有阴影。</summary>
    public void SetGlobalOpacity(float opacity)
    {
        opacity = Mathf.Clamp01(opacity);
        if (Mathf.Approximately(globalOpacity, opacity)) return;
        globalOpacity = opacity;
        batchColor.a = opacity;
        material.SetColor(ColorId, batchColor);
    }

    /// <summary>组件停用或退出世界时清空可见实例。</summary>
    public void Hide()
    {
        lock (syncRoot) count = 0;
    }

    /// <summary>单位顶点与 [-1,1] UV 由 Shader 转换为椭圆。</summary>
    private static Mesh CreateQuad()
    {
        var result = new Mesh { name = "ContactShadow-UnitQuad", hideFlags = HideFlags.HideAndDontSave };
        result.vertices = new[]
        {
            new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f),
            new Vector3(1f, 1f, 0f), new Vector3(-1f, 1f, 0f)
        };
        result.uv = new[]
        {
            new Vector2(-1f, -1f), new Vector2(1f, -1f),
            new Vector2(1f, 1f), new Vector2(-1f, 1f)
        };
        result.colors32 = new[]
        {
            new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255),
            new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255)
        };
        result.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        result.RecalculateBounds();
        return result;
    }

    /// <summary>扩容时重新登记批次，保持同一网格和材质注册。</summary>
    private void Resize(int capacity)
    {
        if (useConstantBuffer)
        {
            while (constantBufferCapacity < capacity)
            {
                int pageBytes = ZeroPrefixBytes + constantBufferPageCapacity * InstanceStride;
                var pageBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Constant, pageBytes / 16, 16)
                {
                    name = $"ContactShadows-BRG-Page{constantPages.Count}"
                };
                try
                {
                    pageBuffer.SetData(new InstanceData[ZeroPrefixBytes / InstanceStride], 0, 0,
                        ZeroPrefixBytes / InstanceStride);
                    BatchID pageId = AddBatch(pageBuffer, pageBytes);
                    constantPages.Add(new ConstantBufferPage(pageBuffer, pageId));
                    constantBufferCapacity += constantBufferPageCapacity;
                }
                catch { pageBuffer.Dispose(); throw; }
            }
            uploadedCount = 0;
            dataChanged = true;
            previousFrameComplete = false;
            return;
        }
        if (buffer != null)
        {
            group.RemoveBatch(batchId);
            buffer.Dispose();
        }
        int bytes = ZeroPrefixBytes + capacity * InstanceStride;
        buffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, bytes / sizeof(int), sizeof(int))
        {
            name = "ContactShadows-BRG-Instances"
        };
        buffer.SetData(new InstanceData[ZeroPrefixBytes / InstanceStride], 0, 0,
            ZeroPrefixBytes / InstanceStride);
        batchId = AddBatch(buffer, bytes);
        // 新 GraphicsBuffer 只有元数据前缀，下一次非空 EndFrame 必须完整上传实例。
        uploadedCount = 0;
        dataChanged = true;
        previousFrameComplete = false;
    }

    private BatchID AddBatch(GraphicsBuffer target, int bytes)
    {
        var metadata = new NativeArray<MetadataValue>(1, Allocator.Temp);
        try
        {
            metadata[0] = new MetadataValue { NameID = InstanceDataId, Value = ZeroPrefixBytes };
            return useConstantBuffer
                ? group.AddBatch(metadata, target.bufferHandle, 0, (uint)bytes)
                : group.AddBatch(metadata, target.bufferHandle);
        }
        finally { metadata.Dispose(); }
    }

    private readonly struct ConstantBufferPage
    {
        public ConstantBufferPage(GraphicsBuffer buffer, BatchID batchId)
        {
            Buffer = buffer;
            BatchId = batchId;
        }

        public GraphicsBuffer Buffer { get; }
        public BatchID BatchId { get; }
    }

    #endregion

    #region 视锥裁剪与资源释放

    /// <summary>相机裁剪后对所有可见阴影只发一条 BRG 绘制命令。</summary>
    private unsafe JobHandle OnPerformCulling(BatchRendererGroup rendererGroup,
        BatchCullingContext context, BatchCullingOutput output, IntPtr userContext)
    {
        using var profilerScope = CullingMarker.Auto();
        lock (syncRoot)
        {
            int visible = CollectVisibleInstanceIndices(instances, count,
                context.cullingPlanes, visibleInstanceIndices);

            BatchCullingOutputDrawCommands* commands =
                (BatchCullingOutputDrawCommands*)output.drawCommands.GetUnsafePtr();
            commands->drawCommandPickingInstanceIDs = null;
            commands->instanceSortingPositions = null;
            commands->instanceSortingPositionFloatCount = 0;
            int drawCount = visible > 0 ? 1 : 0;
            if (useConstantBuffer && visible > 0)
            {
                int lastPage = -1;
                drawCount = 0;
                for (int i = 0; i < visible; i++)
                {
                    int page = visibleInstanceIndices[i] / constantBufferPageCapacity;
                    if (page == lastPage) continue;
                    lastPage = page;
                    drawCount++;
                }
            }
            commands->drawCommandCount = drawCount;
            commands->drawRangeCount = drawCount;
            commands->visibleInstanceCount = visible;
            if (visible == 0)
            {
                commands->drawCommands = null;
                commands->drawRanges = null;
                commands->visibleInstances = null;
                return default;
            }

            int alignment = UnsafeUtility.AlignOf<long>();
            commands->drawCommands = (BatchDrawCommand*)UnsafeUtility.Malloc(
                UnsafeUtility.SizeOf<BatchDrawCommand>() * drawCount, alignment, Allocator.TempJob);
            commands->drawRanges = (BatchDrawRange*)UnsafeUtility.Malloc(
                UnsafeUtility.SizeOf<BatchDrawRange>() * drawCount, alignment, Allocator.TempJob);
            commands->visibleInstances = (int*)UnsafeUtility.Malloc(
                sizeof(int) * visible, alignment, Allocator.TempJob);
            // 每个 DrawCommand 的索引都相对它所属的 BRG Batch/UBO 页。
            for (int start = 0, commandIndex = 0; start < visible; commandIndex++)
            {
                int page = useConstantBuffer ? visibleInstanceIndices[start] / constantBufferPageCapacity : 0;
                int end = start + 1;
                while (end < visible && (!useConstantBuffer ||
                       visibleInstanceIndices[end] / constantBufferPageCapacity == page))
                    end++;

                BatchDrawCommand* draw = commands->drawCommands + commandIndex;
                draw->visibleOffset = (uint)start;
                draw->visibleCount = (uint)(end - start);
                draw->batchID = useConstantBuffer ? constantPages[page].BatchId : batchId;
                draw->materialID = materialId;
                draw->meshID = meshId;
                draw->submeshIndex = 0;
                draw->splitVisibilityMask = 0xff;
                draw->flags = 0;
                draw->sortingPosition = 0;
                BatchDrawRange* range = commands->drawRanges + commandIndex;
                range->drawCommandsBegin = (uint)commandIndex;
                range->drawCommandsCount = 1;
                range->filterSettings = new BatchFilterSettings
                {
                    layer = 0,
                    renderingLayerMask = uint.MaxValue,
                    allDepthSorted = false
                };

                for (int i = start; i < end; i++)
                    commands->visibleInstances[i] = useConstantBuffer
                        ? visibleInstanceIndices[i] % constantBufferPageCapacity
                        : visibleInstanceIndices[i];
                start = end;
            }
        }
        return default;
    }

    /// <summary>按输入顺序收集可见索引，供 BRG 输出与规则回归共用。</summary>
    internal static int CollectVisibleInstanceIndices(InstanceData[] source, int sourceCount,
        NativeArray<Plane> planes, int[] destination)
    {
        int visible = 0;
        for (int i = 0; i < sourceCount; i++)
            if (IsVisible(source[i].Footprint, planes))
                destination[visible++] = i;
        return visible;
    }

    /// <summary>按椭圆外接矩形检测所有裁剪面。</summary>
    private static bool IsVisible(Vector4 footprint, NativeArray<Plane> planes)
    {
        Vector3 center = new Vector3(footprint.x, footprint.y, 0f);
        for (int i = 0; i < planes.Length; i++)
        {
            Vector3 normal = planes[i].normal;
            float radius = Mathf.Abs(normal.x) * footprint.z + Mathf.Abs(normal.y) * footprint.w;
            if (planes[i].GetDistanceToPoint(center) < -radius) return false;
        }
        return true;
    }

    /// <summary>同步注销原生批次、缓冲、网格和材质。</summary>
    public void Dispose()
    {
        lock (syncRoot)
        {
            if (disposed) return;
            disposed = true;
            // 先销毁整个 BRG 结束全部注册，再独立释放自有缓冲与资源。
            try { group.Dispose(); }
            finally
            {
                GraphicsBuffer previous = buffer;
                buffer = null;
                try { previous?.Dispose(); }
                finally
                {
                    try
                    {
                        foreach (ConstantBufferPage page in constantPages)
                            page.Buffer.Dispose();
                        constantPages.Clear();
                    }
                    finally
                    {
                        if (Application.isPlaying)
                        {
                            UnityEngine.Object.Destroy(mesh);
                            UnityEngine.Object.Destroy(material);
                        }
                        else
                        {
                            UnityEngine.Object.DestroyImmediate(mesh);
                            UnityEngine.Object.DestroyImmediate(material);
                        }
                    }
                }
            }
        }
    }

    #endregion
}
