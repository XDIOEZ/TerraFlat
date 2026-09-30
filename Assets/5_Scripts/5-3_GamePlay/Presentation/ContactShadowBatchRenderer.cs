using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 场景级接触阴影 BRG：每个排序批次的椭圆共用网格、材质和一条绘制命令。
/// 实例数据每只 32 字节，每帧只上传有效阴影；不为单只物品创建 Renderer。
/// </summary>
internal sealed class ContactShadowBatchRenderer : IDisposable
{
    #region 批次资源

    private const int InitialCapacity = 64;
    private const int InstanceStride = 32;
    private const int ZeroPrefixBytes = 64;
    private const int ShadowRenderQueue = 2995; // 旧实体位于地形 BRG 之后。
    private static readonly int InstanceDataId = Shader.PropertyToID("_ContactShadowData");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private readonly object syncRoot = new();
    private readonly BatchRendererGroup group;
    private readonly Mesh mesh;
    private readonly Material material;
    private GraphicsBuffer buffer;
    private BatchID batchId;
    private BatchMeshID meshId;
    private BatchMaterialID materialId;
    private InstanceData[] instances = new InstanceData[InitialCapacity];
    private int count;
    private bool disposed;
    private Color batchColor;
    private float globalOpacity = 1f;

    [StructLayout(LayoutKind.Sequential)]
    private struct InstanceData
    {
        public Vector4 Footprint; // 中心 XY 与半宽、高。
        public Vector4 Appearance; // X 为本只阴影透明度。
    }

    /// <summary>共享材质和单位方片只创建一次；BRG 按实例生成柔边椭圆。</summary>
    public ContactShadowBatchRenderer(int renderQueue = ShadowRenderQueue)
    {
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
        lock (syncRoot) count = 0;
    }

    /// <summary>追加一个世界空间椭圆；同一材质可汇聚不同尺寸和透明度。</summary>
    public void Append(Vector2 center, float width, float height, float opacity)
    {
        lock (syncRoot)
        {
            if (count == instances.Length)
            {
                Array.Resize(ref instances, instances.Length * 2);
                Resize(instances.Length);
            }
            instances[count++] = new InstanceData
            {
                Footprint = new Vector4(center.x, center.y, width * 0.5f, height * 0.5f),
                Appearance = new Vector4(opacity, 0f, 0f, 0f)
            };
        }
    }

    /// <summary>一次上传本帧实例；空帧由裁剪回调立即停止绘制。</summary>
    public void EndFrame()
    {
        lock (syncRoot)
            if (count > 0) buffer.SetData(instances, 0, ZeroPrefixBytes / InstanceStride, count);
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
        var metadata = new NativeArray<MetadataValue>(1, Allocator.Temp);
        try
        {
            metadata[0] = new MetadataValue { NameID = InstanceDataId, Value = ZeroPrefixBytes };
            batchId = group.AddBatch(metadata, buffer.bufferHandle);
        }
        finally { metadata.Dispose(); }
    }

    #endregion

    #region 视锥裁剪与资源释放

    /// <summary>相机裁剪后对所有可见阴影只发一条 BRG 绘制命令。</summary>
    private unsafe JobHandle OnPerformCulling(BatchRendererGroup rendererGroup,
        BatchCullingContext context, BatchCullingOutput output, IntPtr userContext)
    {
        lock (syncRoot)
        {
            int visible = 0;
            for (int i = 0; i < count; i++)
                if (IsVisible(instances[i].Footprint, context.cullingPlanes)) visible++;

            BatchCullingOutputDrawCommands* commands =
                (BatchCullingOutputDrawCommands*)output.drawCommands.GetUnsafePtr();
            commands->drawCommandPickingInstanceIDs = null;
            commands->instanceSortingPositions = null;
            commands->instanceSortingPositionFloatCount = 0;
            commands->drawCommandCount = visible > 0 ? 1 : 0;
            commands->drawRangeCount = visible > 0 ? 1 : 0;
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
                UnsafeUtility.SizeOf<BatchDrawCommand>(), alignment, Allocator.TempJob);
            commands->drawRanges = (BatchDrawRange*)UnsafeUtility.Malloc(
                UnsafeUtility.SizeOf<BatchDrawRange>(), alignment, Allocator.TempJob);
            commands->visibleInstances = (int*)UnsafeUtility.Malloc(
                sizeof(int) * visible, alignment, Allocator.TempJob);

            BatchDrawCommand* draw = commands->drawCommands;
            draw->visibleOffset = 0;
            draw->visibleCount = (uint)visible;
            draw->batchID = batchId;
            draw->materialID = materialId;
            draw->meshID = meshId;
            draw->submeshIndex = 0;
            draw->splitVisibilityMask = 0xff;
            draw->flags = 0;
            draw->sortingPosition = 0;
            BatchDrawRange* range = commands->drawRanges;
            range->drawCommandsBegin = 0;
            range->drawCommandsCount = 1;
            range->filterSettings = new BatchFilterSettings
            {
                layer = 0,
                renderingLayerMask = uint.MaxValue,
                allDepthSorted = false
            };

            int index = 0;
            for (int i = 0; i < count; i++)
                if (IsVisible(instances[i].Footprint, context.cullingPlanes))
                    commands->visibleInstances[index++] = i;
        }
        return default;
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

    #endregion
}
