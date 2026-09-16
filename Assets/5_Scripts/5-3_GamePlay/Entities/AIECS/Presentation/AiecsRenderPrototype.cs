using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace FlatWorld.AIECS
{
    /// <summary>
    /// P1 可独立运行的渲染切片：真实 Actor 帧表 → 独立 ECS 位置 → 精确排序 → 原生 Lit 批次。
    /// 只用于独立原型场景，不注册正式生态、存档或网络；数量默认 48，上限 20000，绝不自动开启压力验证。
    /// 所有旧颜色显示项须位于 LegacyRoot，原型停用会释放 World/Native/网格并恢复原排序。
    /// </summary>
    public sealed class AiecsRenderPrototype : MonoBehaviour
    {
        #region 原型输入
        // 编辑器导出的当前内容目录。
        public AiecsAnimationCatalog Catalog;
        // 唯一原型观察相机与全部旧 Sprite 显示范围。
        public Camera TargetCamera;
        public Transform LegacyRoot;
        // 显式由用户设置规模、布阵及穿行范围。
        [Range(1, 20000)] public int EntityCount = 48;
        public Vector2 LayoutSize = new Vector2(14f, 8f);
        [Min(0f)] public float Travel = 1.5f;
        // 视觉入水输入，不读取或修改正式地形与体力。
        public float WaterBoundaryY = 0f;
        [Range(0f, 1f)] public float WaterSurface = 0.6f;
        [Range(0f, 1f)] public float WaterTintStrength = 0.45f;
        [Min(0.001f)] public float WaterTransitionSeconds = 0.18f;
        #endregion

        #region 只读统计
        // 这些值只统计已创建、更新、相机内和提交的真实对象；不代表生命或战斗计数。
        public int CreatedEntities { get; private set; }
        public int UpdatedPositions { get; private set; }
        public int VisibleEntities { get; private set; }
        public int SubmittedSprites { get; private set; }
        public int ColorBatches { get; private set; }
        public int UploadedBytes { get; private set; }
        public int LegacyDisplayItems { get; private set; }
        #endregion

        #region 所有权与缓存
        // 本原型独占资源，不使用默认 ECS World 或正式 Item 生命周期。
        private World world;
        private AiecsPrototypeJobSchedulerSystem scheduler;
        private EntityQuery query;
        private JobHandle pending;
        private NativeArray<AiecsPrototypeActor> snapshot;
        private AiecsLegacySortScope legacy;
        private readonly List<AiecsRenderBatch> batches = new();
        private readonly List<DisplayItem> display = new();
        private readonly Plane[] planes = new Plane[6];
        private static readonly DisplayComparer Comparer = new();
        #endregion

        #region 生命周期
        /// <summary>仅在用户开启 Play Mode 后创建独立原型 World。</summary>
        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            try
            {
                ValidateInputs();
                legacy = new AiecsLegacySortScope(LegacyRoot);
                world = new World("AIECS P1 渲染原型");
                scheduler = world.GetOrCreateSystemManaged<AiecsPrototypeJobSchedulerSystem>();
                EntityManager manager = world.EntityManager;
                query = manager.CreateEntityQuery(ComponentType.ReadWrite<AiecsPrototypeActor>());
                snapshot = new NativeArray<AiecsPrototypeActor>(EntityCount, Allocator.Persistent);
                using var entities = new NativeArray<Entity>(EntityCount, Allocator.Temp);
                manager.CreateEntity(manager.CreateArchetype(typeof(AiecsPrototypeActor)), entities);
                int columns = Mathf.CeilToInt(Mathf.Sqrt(EntityCount * LayoutSize.x / LayoutSize.y));
                int rows = Mathf.CeilToInt((float)EntityCount / columns);
                for (int index = 0; index < entities.Length; index++)
                {
                    int definition = index % Catalog.Actors.Length;
                    Vector2 origin = new Vector2(((index % columns + 0.5f) / columns - 0.5f) * LayoutSize.x,
                        ((index / columns + 0.5f) / rows - 0.5f) * LayoutSize.y);
                    var actor = new AiecsPrototypeActor
                    {
                        Slot = index, Definition = definition,
                        Action = index / Catalog.Actors.Length % Catalog.Actors[definition].Clips.Length,
                        Origin = new float2(origin.x, origin.y), Position = new float2(origin.x, origin.y),
                        Phase = index * 2.39996323f, Elapsed = (index % 31) * 0.137f,
                        Speed = 0.8f + (index % 7) * 0.07f
                    };
                    manager.SetComponentData(entities[index], actor);
                }
                CreatedEntities = query.CalculateEntityCount();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                enabled = false;
            }
        }

        /// <summary>冻结输入、批量更新位置、完成一个同步点，再提交本帧有序表现。</summary>
        private void LateUpdate()
        {
            if (world == null || !world.IsCreated) return;
            try
            {
                pending = scheduler.ScheduleParallel(new AiecsPrototypeMotion
                {
                    DeltaTime = Time.deltaTime, Travel = Travel,
                    WaterBoundaryY = WaterBoundaryY, TransitionSeconds = WaterTransitionSeconds,
                    Snapshot = snapshot
                }, query, default);
                pending.Complete();
                UpdatedPositions = snapshot.Length;
                CollectDisplayItems();
                SubmitDisplayItems();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                enabled = false;
            }
        }

        /// <summary>先完成任务，再释放原型独占资源和旧排序租约。</summary>
        private void OnDisable()
        {
            pending.Complete();
            foreach (AiecsRenderBatch batch in batches) batch.Dispose();
            batches.Clear();
            legacy?.Dispose();
            legacy = null;
            if (snapshot.IsCreated) snapshot.Dispose();
            if (world != null && world.IsCreated) world.Dispose();
            world = null; scheduler = null;
            display.Clear();
            CreatedEntities = UpdatedPositions = VisibleEntities = SubmittedSprites = 0;
            ColorBatches = UploadedBytes = LegacyDisplayItems = 0;
        }
        #endregion

        #region 排序与绘制
        /// <summary>合并旧显示项与相机内实体；稳定 Slot 仅用于本原型同位置平局。</summary>
        private void CollectDisplayItems()
        {
            display.Clear();
            GeometryUtility.CalculateFrustumPlanes(TargetCamera, planes);
            for (int index = 0; index < legacy.Entries.Count; index++)
            {
                AiecsLegacySortScope.Entry entry = legacy.Entries[index];
                if (entry.TryGetY(out float y))
                    display.Add(new DisplayItem(entry.Layer, entry.OriginalOrder, entry.Queue, y,
                        index, true, default));
            }
            LegacyDisplayItems = display.Count;
            VisibleEntities = 0;
            for (int index = 0; index < snapshot.Length; index++)
            {
                AiecsPrototypeActor actor = snapshot[index];
                AiecsActorVisual definition = Catalog.Actors[actor.Definition];
                AiecsAnimationFrame frame = definition.Clips[actor.Action].Sample(actor.Elapsed);
                Rect rect = Catalog.Sprites[frame.Sprite].LocalRect;
                Vector3 first = AiecsRenderBatch.TransformPoint(new Vector3(rect.xMin, rect.yMin), actor, definition, frame);
                var bounds = new Bounds(first, Vector3.zero);
                bounds.Encapsulate(AiecsRenderBatch.TransformPoint(new Vector3(rect.xMax, rect.yMin), actor, definition, frame));
                bounds.Encapsulate(AiecsRenderBatch.TransformPoint(new Vector3(rect.xMax, rect.yMax), actor, definition, frame));
                bounds.Encapsulate(AiecsRenderBatch.TransformPoint(new Vector3(rect.xMin, rect.yMax), actor, definition, frame));
                if (!GeometryUtility.TestPlanesAABB(planes, bounds)) continue;
                VisibleEntities++;
                display.Add(new DisplayItem(definition.SortingLayerId, definition.SortingOrder,
                    Catalog.Material.renderQueue, actor.Position.y + frame.Position.y, index, false, frame));
            }
            display.Sort(Comparer);
        }

        /// <summary>按最终透明顺序仅合并连续生物；旧颜色仍由各自原生 Renderer 绘制一次。</summary>
        private void SubmitDisplayItems()
        {
            if (display.Count > ushort.MaxValue)
                throw new InvalidOperationException("原型显示序号超过 Unity Order 范围，不能截断显示项。");
            ColorBatches = SubmittedSprites = UploadedBytes = 0;
            int cursor = 0;
            int order = short.MinValue;
            int layer = int.MinValue;
            while (cursor < display.Count)
            {
                DisplayItem item = display[cursor];
                if (layer != item.Layer) { layer = item.Layer; order = short.MinValue; }
                if (item.Legacy)
                {
                    legacy.Entries[item.Index].Apply(order++);
                    cursor++;
                    continue;
                }
                if (batches.Count <= ColorBatches)
                    batches.Add(new AiecsRenderBatch(gameObject.scene, Catalog.Material));
                AiecsRenderBatch batch = batches[ColorBatches];
                batch.Begin();
                int count = 0;
                while (cursor < display.Count && !display[cursor].Legacy &&
                    display[cursor].Layer == layer && count < AiecsRenderBatch.MaxSprites)
                {
                    item = display[cursor++];
                    AiecsPrototypeActor actor = snapshot[item.Index];
                    batch.Append(actor, Catalog.Actors[actor.Definition], item.Frame,
                        Catalog.Sprites[item.Frame.Sprite], WaterSurface, WaterTintStrength);
                    count++;
                }
                UploadedBytes += batch.Submit(layer, order++);
                SubmittedSprites += count;
                ColorBatches++;
            }
            for (int index = ColorBatches; index < batches.Count; index++) batches[index].Hide();
        }

        /// <summary>拒绝尚不支持的原型输入，避免静默套入正式多相机或不完整内容。</summary>
        private void ValidateInputs()
        {
            if (Catalog == null || Catalog.Material == null || Catalog.Actors == null || Catalog.Actors.Length == 0 ||
                TargetCamera == null || LegacyRoot == null)
                throw new InvalidOperationException("请先从 AIECS 菜单生成完整渲染原型资源和场景。");
            if (!TargetCamera.orthographic || EntityCount < 1 || EntityCount > 20000 ||
                LayoutSize.x <= 0f || LayoutSize.y <= 0f)
                throw new InvalidOperationException("P1 需要正交相机、正数布局和 1～20000 个实体。");
            if (transform.position != Vector3.zero || transform.rotation != Quaternion.identity || transform.lossyScale != Vector3.one)
                throw new InvalidOperationException("P1 原型根节点必须使用单位变换，世界拓扑适配尚未接入。");
            if (LegacyRoot.gameObject.scene != gameObject.scene || TargetCamera.gameObject.scene != gameObject.scene ||
                (TargetCamera.cullingMask & 1) == 0)
                throw new InvalidOperationException("原型显示范围与相机必须属于同一独立场景，并能绘制 Default Layer。");
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            {
                foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
                    if (camera != TargetCamera && camera.isActiveAndEnabled)
                        throw new InvalidOperationException("P1 排序租约只支持单一原型相机，尚未支持相机栈。");
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                    if (!renderer.transform.IsChildOf(LegacyRoot))
                        throw new InvalidOperationException("独立场景存在未登记的显示项，请先纳入旧显示范围。");
            }
            foreach (AiecsActorVisual actor in Catalog.Actors)
            {
                if (actor.Clips == null || actor.Clips.Length == 0)
                    throw new InvalidOperationException($"Actor {actor.Id} 没有可显示动作，不能忽略该物种。");
            }
        }
        #endregion

        #region 显示记录
        /// <summary>单一外部显示单元及原始排序键。</summary>
        private readonly struct DisplayItem
        {
            internal readonly int Layer, LayerValue, Order, Queue, Index;
            internal readonly float Y;
            internal readonly bool Legacy;
            internal readonly AiecsAnimationFrame Frame;

            /// <summary>缓存图层排序值，禁止把 SortingLayer ID 当顺序。</summary>
            internal DisplayItem(int layer, int order, int queue, float y, int index, bool legacy,
                AiecsAnimationFrame frame)
            {
                Layer = layer; LayerValue = SortingLayer.GetLayerValueFromID(layer); Order = order;
                Queue = queue; Y = y; Index = index; Legacy = legacy; Frame = frame;
            }
        }

        /// <summary>先保留原图层、Order 和队列，再按 Y 从后往前排列，同位置使用稳定顺序。</summary>
        private sealed class DisplayComparer : IComparer<DisplayItem>
        {
            /// <summary>按完整透明排序键比较显示项，原型内的旧显示项在平局时先绘制。</summary>
            public int Compare(DisplayItem x, DisplayItem y)
            {
                int result = x.LayerValue.CompareTo(y.LayerValue);
                if (result == 0) result = x.Order.CompareTo(y.Order);
                if (result == 0) result = x.Queue.CompareTo(y.Queue);
                if (result == 0) result = y.Y.CompareTo(x.Y);
                if (result == 0) result = y.Legacy.CompareTo(x.Legacy);
                return result != 0 ? result : x.Index.CompareTo(y.Index);
            }
        }
        #endregion
    }
}
