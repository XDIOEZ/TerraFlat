using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;

namespace FlatWorld.AIECS
{
    /// <summary>正式生物主体共用一个四边形和材质，通过 BRG 实例缓冲提交动画帧。</summary>
    internal sealed class AiecsBatchRendererGroup : IDisposable
    {
        #region 批次数据与资源

        private const int InitialCapacity = 128;
        private const int InstanceStride = 96;
        private const int PrefixBytes = 96;
        private static readonly int InstanceDataId = Shader.PropertyToID("_AiecsInstanceData");

        [StructLayout(LayoutKind.Sequential)]
        private struct InstanceData
        {
            public Vector4 Transform0;
            public Vector4 Transform1;
            public Vector4 Atlas;
            public Vector4 Tint;
            public Vector4 Water;
            public Vector4 Bounds;
        }

        private readonly object syncRoot = new();
        private readonly BatchRendererGroup group;
        private readonly Mesh mesh;
        private readonly Material material;
        private readonly int layer;
        private GraphicsBuffer buffer;
        private BatchID batchId;
        private BatchMeshID meshId;
        private BatchMaterialID materialId;
        private InstanceData[] instances = new InstanceData[InitialCapacity];
        private int count;
        private bool disposed;

        public int Count => count;

        public AiecsBatchRendererGroup(Material source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            layer = LayerMask.NameToLayer("AIECSRuntime");
            if (layer < 0)
                throw new InvalidOperationException("项目缺少 AIECSRuntime Layer。");
            material = new Material(source)
            {
                name = "AIECS-Actor-BRG",
                hideFlags = HideFlags.HideAndDontSave,
                enableInstancing = true
            };
            mesh = CreateQuad();
            group = new BatchRendererGroup(OnPerformCulling, IntPtr.Zero);
            group.SetGlobalBounds(new Bounds(Vector3.zero, new Vector3(2000000f, 2000000f, 1000f)));
            meshId = group.RegisterMesh(mesh);
            materialId = group.RegisterMaterial(material);
            Resize(InitialCapacity);
        }

        public void Begin()
        {
            lock (syncRoot)
                count = 0;
        }

        /// <summary>CPU 只采样动画与写入实例数据，四个顶点交给共享 Shader 变换。</summary>
        public void Append(AiecsPrototypeActor actor, AiecsActorVisual definition,
            AiecsAnimationFrame frame, AiecsSpriteGeometry sprite, float surface, float tint,
            bool mirror, Color color)
        {
            Rect rect = sprite.LocalRect;
            Vector3 p0 = AiecsRenderBatch.TransformPoint(new Vector3(rect.xMin, rect.yMin), actor, definition, frame, mirror);
            Vector3 p1 = AiecsRenderBatch.TransformPoint(new Vector3(rect.xMax, rect.yMin), actor, definition, frame, mirror);
            Vector3 p3 = AiecsRenderBatch.TransformPoint(new Vector3(rect.xMin, rect.yMax), actor, definition, frame, mirror);
            Vector3 p2 = p1 + p3 - p0;
            float xMin = Mathf.Min(p0.x, p1.x, p2.x, p3.x);
            float xMax = Mathf.Max(p0.x, p1.x, p2.x, p3.x);
            float yMin = Mathf.Min(p0.y, p1.y, p2.y, p3.y);
            float yMax = Mathf.Max(p0.y, p1.y, p2.y, p3.y);
            Rect uv = sprite.AtlasRect;
            float height = Mathf.Max(0.0001f, definition.BodyYRange.y - definition.BodyYRange.x);
            var data = new InstanceData
            {
                Transform0 = new Vector4(p1.x - p0.x, p3.x - p0.x, p0.x, 0f),
                Transform1 = new Vector4(p1.y - p0.y, p3.y - p0.y, p0.y, 0f),
                Atlas = new Vector4(uv.xMin, uv.yMin, uv.width, uv.height),
                Tint = color,
                Water = new Vector4(actor.WaterBlend,
                    actor.Position.y + definition.BodyYRange.x + height * surface, height, tint),
                Bounds = new Vector4((xMin + xMax) * 0.5f, (yMin + yMax) * 0.5f,
                    (xMax - xMin) * 0.5f, (yMax - yMin) * 0.5f)
            };
            lock (syncRoot)
            {
                if (count == instances.Length)
                {
                    Array.Resize(ref instances, instances.Length * 2);
                    Resize(instances.Length);
                }
                instances[count++] = data;
            }
        }

        public void Submit()
        {
            lock (syncRoot)
                if (count > 0)
                    buffer.SetData(instances, 0, PrefixBytes / sizeof(int), count);
        }

        public void Hide() => Begin();

        private static Mesh CreateQuad()
        {
            var result = new Mesh { name = "AIECS-BRG-UnitQuad", hideFlags = HideFlags.HideAndDontSave };
            result.vertices = new[] { new Vector3(0f, 0f), new Vector3(1f, 0f),
                new Vector3(1f, 1f), new Vector3(0f, 1f) };
            result.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            result.colors32 = new[] { (Color32)Color.white, (Color32)Color.white,
                (Color32)Color.white, (Color32)Color.white };
            result.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            result.RecalculateBounds();
            return result;
        }

        private void Resize(int capacity)
        {
            if (buffer != null)
            {
                group.RemoveBatch(batchId);
                buffer.Dispose();
            }
            int bytes = PrefixBytes + capacity * InstanceStride;
            buffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, bytes / sizeof(int), sizeof(int))
            {
                name = "AIECS-BRG-Instances"
            };
            buffer.SetData(new InstanceData[PrefixBytes / InstanceStride], 0, 0,
                PrefixBytes / InstanceStride);
            var metadata = new NativeArray<MetadataValue>(1, Allocator.Temp);
            try
            {
                metadata[0] = new MetadataValue { NameID = InstanceDataId, Value = PrefixBytes };
                batchId = group.AddBatch(metadata, buffer.bufferHandle);
            }
            finally { metadata.Dispose(); }
        }

        #endregion

        #region 裁剪与释放

        /// <summary>同材质可见生物合为一条命令，并向 Unity 提供逐实例的透明排序位置。</summary>
        private unsafe JobHandle OnPerformCulling(BatchRendererGroup rendererGroup,
            BatchCullingContext context, BatchCullingOutput output, IntPtr userContext)
        {
            lock (syncRoot)
            {
                int visible = 0;
                for (int i = 0; i < count; i++)
                    if (IsVisible(instances[i].Bounds, context.cullingPlanes)) visible++;

                var commands = (BatchCullingOutputDrawCommands*)output.drawCommands.GetUnsafePtr();
                commands->drawCommandPickingInstanceIDs = null;
                commands->drawCommandCount = visible > 0 ? 1 : 0;
                commands->drawRangeCount = visible > 0 ? 1 : 0;
                commands->visibleInstanceCount = visible;
                commands->instanceSortingPositionFloatCount = visible * 3;
                if (visible == 0)
                {
                    commands->drawCommands = null;
                    commands->drawRanges = null;
                    commands->visibleInstances = null;
                    commands->instanceSortingPositions = null;
                    return default;
                }

                int alignment = UnsafeUtility.AlignOf<long>();
                commands->drawCommands = (BatchDrawCommand*)UnsafeUtility.Malloc(
                    UnsafeUtility.SizeOf<BatchDrawCommand>(), alignment, Allocator.TempJob);
                commands->drawRanges = (BatchDrawRange*)UnsafeUtility.Malloc(
                    UnsafeUtility.SizeOf<BatchDrawRange>(), alignment, Allocator.TempJob);
                commands->visibleInstances = (int*)UnsafeUtility.Malloc(
                    sizeof(int) * visible, alignment, Allocator.TempJob);
                commands->instanceSortingPositions = (float*)UnsafeUtility.Malloc(
                    sizeof(float) * visible * 3, alignment, Allocator.TempJob);

                BatchDrawCommand* draw = commands->drawCommands;
                draw->visibleOffset = 0;
                draw->visibleCount = (uint)visible;
                draw->batchID = batchId;
                draw->materialID = materialId;
                draw->meshID = meshId;
                draw->submeshIndex = 0;
                draw->splitVisibilityMask = 0xff;
                draw->flags = BatchDrawCommandFlags.HasSortingPosition;
                draw->sortingPosition = 0;
                BatchDrawRange* range = commands->drawRanges;
                range->drawCommandsBegin = 0;
                range->drawCommandsCount = 1;
                range->filterSettings = new BatchFilterSettings
                {
                    layer = (byte)layer,
                    renderingLayerMask = uint.MaxValue,
                    allDepthSorted = true
                };

                int index = 0;
                for (int i = 0; i < count; i++)
                {
                    if (!IsVisible(instances[i].Bounds, context.cullingPlanes)) continue;
                    commands->visibleInstances[index] = i;
                    commands->instanceSortingPositions[index * 3] = instances[i].Bounds.x;
                    commands->instanceSortingPositions[index * 3 + 1] = instances[i].Bounds.y;
                    commands->instanceSortingPositions[index * 3 + 2] = 0f;
                    index++;
                }
            }
            return default;
        }

        private static bool IsVisible(Vector4 bounds, NativeArray<Plane> planes)
        {
            Vector3 center = new Vector3(bounds.x, bounds.y, 0f);
            for (int i = 0; i < planes.Length; i++)
            {
                Vector3 normal = planes[i].normal;
                float radius = Mathf.Abs(normal.x) * bounds.z + Mathf.Abs(normal.y) * bounds.w;
                if (planes[i].GetDistanceToPoint(center) < -radius) return false;
            }
            return true;
        }

        public void Dispose()
        {
            lock (syncRoot)
            {
                if (disposed) return;
                disposed = true;
                group.RemoveBatch(batchId);
                buffer.Dispose();
                group.UnregisterMesh(meshId);
                group.UnregisterMaterial(materialId);
                group.Dispose();
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

        #endregion
    }
}
