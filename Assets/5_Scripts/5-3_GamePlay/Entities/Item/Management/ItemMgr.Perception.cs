using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using FlatWorld.Geometry;

public partial class ItemMgr
{
    // Actor 只持有共享本地几何；旧对象单独持有 Collider Bridge。
    private readonly Dictionary<Item, PerceptionTarget> _perceptionTargets = new();
    private uint _perceptionRevision;

    /// <summary>一个注册代际的感知后端，两个几何权威互斥。</summary>
    private sealed class PerceptionTarget
    {
        // Shapes 与 Bridge 必须恰好选择一个；空形状 Actor 不转回物理查询。
        public PerceptionShape2D[] Shapes;
        public PerceptionColliderBridge Bridge;
        // 每次重新注册或结构变化递增，用于拒绝飞行中的旧结果。
        public uint Revision;
    }

    private const float PerceptionCellSize = 8f;
    private readonly Dictionary<long, HashSet<Item>> _perceptionCells = new();
    private readonly Dictionary<Item, long> _itemPerceptionCells = new();
    private readonly Stack<HashSet<Item>> _perceptionCellPool = new();

    [ShowInInspector, Sirenix.OdinInspector.ReadOnly] private int PerceptionCellCount => _perceptionCells.Count;

    #region 并行感知调度

    private const int PerceptionJobBatchSize = 8;

    private readonly List<PendingDetectorRequest> _pendingDetectorRequests = new(64);
    private readonly HashSet<Mod_ItemDetector> _pendingDetectorSet = new();
    private readonly List<Mod_ItemDetector> _inFlightDetectors = new(64);
    private readonly List<long> _inFlightDetectorVersions = new(64);
    private readonly List<DetectorQuerySnapshot> _perceptionQueryData = new(64);
    private readonly List<Item> _perceptionSnapshotItems = new(256);
    private readonly List<PerceptionItemSnapshot> _perceptionSnapshotData = new(256);
    // 世界空间纯数据形状按目标连续存放，支持一个目标拥有多个不相连的形状。
    private readonly List<PerceptionShape2D> _perceptionShapeData = new(256);
    private readonly HashSet<long> _perceptionSnapshotCells = new();
    private readonly HashSet<Item> _perceptionSnapshotItemSet = new();
    private readonly HashSet<Item> _perceptionResultItemSet = new();
    private readonly List<Item> _detectorApplyBuffer = new(64);

    private NativeList<PerceptionItemSnapshot> _inFlightItemSnapshots;
    private NativeList<PerceptionShape2D> _inFlightShapes;
    private NativeList<DetectorQuerySnapshot> _inFlightQueries;
    private NativeParallelMultiHashMap<long, int> _inFlightSpatialMap;
    private NativeStream _inFlightResults;
    private JobHandle _perceptionJobHandle;
    private bool _perceptionJobScheduled;

    [ShowInInspector, Sirenix.OdinInspector.ReadOnly] private int PendingDetectorQueryCount => _pendingDetectorRequests.Count;
    [ShowInInspector, Sirenix.OdinInspector.ReadOnly] private int InFlightDetectorQueryCount => _perceptionJobScheduled
        ? _inFlightDetectors.Count
        : 0;

    private readonly struct PendingDetectorRequest
    {
        public readonly Mod_ItemDetector Detector;
        public readonly long Version;

        /// <summary>记录检测器和本次申请版本。</summary>
        public PendingDetectorRequest(Mod_ItemDetector detector, long version)
        {
            Detector = detector;
            Version = version;
        }
    }

    private struct PerceptionItemSnapshot
    {
        public long CellKey;
        public int Guid;
        public int InstanceId;
        public int LayerBit;
        public float PerceptionRadiusMultiplier;
        public float2 BoundsCenter;
        public float2 BoundsExtents;
        public int ShapeStart;
        public int ShapeCount;
        public uint Revision;
    }

    private struct DetectorQuerySnapshot
    {
        public float2 Center;
        public float DetectionRadius;
        public float BroadphaseRadius;
        public int LayerMask;
        public int ExcludedInstanceId;
        public WorldTopologyDomain Topology;
    }

    [BurstCompile]
    private struct BatchedPerceptionQueryJob : IJobParallelFor
    {
        [Unity.Collections.ReadOnly] public NativeArray<PerceptionItemSnapshot> Items;
        [Unity.Collections.ReadOnly] public NativeArray<PerceptionShape2D> Shapes;
        [Unity.Collections.ReadOnly] public NativeArray<DetectorQuerySnapshot> Queries;
        [Unity.Collections.ReadOnly] public NativeParallelMultiHashMap<long, int> SpatialMap;
        public NativeStream.Writer Results;

        /// <summary>空间格粗筛后在 Burst 内完成 Actor 圆形/AABB 的最终几何判断。</summary>
        public void Execute(int queryIndex)
        {
            DetectorQuerySnapshot query = Queries[queryIndex];
            NativeStream.Writer writer = Results;
            writer.BeginForEachIndex(queryIndex);

            int minImage = query.Topology.IsWrapped ? -1 : 0;
            int maxImage = query.Topology.IsWrapped ? 1 : 0;
            for (int imageX = minImage; imageX <= maxImage; imageX++)
            {
                for (int imageY = minImage; imageY <= maxImage; imageY++)
                {
                    float2 imageCenter = query.Center + new float2(
                        imageX * query.Topology.Span.x,
                        imageY * query.Topology.Span.y);
                    int minCellX = WorldToCell(imageCenter.x - query.BroadphaseRadius) - 1;
                    int maxCellX = WorldToCell(imageCenter.x + query.BroadphaseRadius) + 1;
                    int minCellY = WorldToCell(imageCenter.y - query.BroadphaseRadius) - 1;
                    int maxCellY = WorldToCell(imageCenter.y + query.BroadphaseRadius) + 1;
                    for (int cellX = minCellX; cellX <= maxCellX; cellX++)
                    {
                        for (int cellY = minCellY; cellY <= maxCellY; cellY++)
                        {
                            long cellKey = PackCell(cellX, cellY);
                            if (!SpatialMap.TryGetFirstValue(cellKey, out int itemIndex, out NativeParallelMultiHashMapIterator<long> iterator))
                                continue;

                            do
                            {
                                PerceptionItemSnapshot candidate = Items[itemIndex];
                                if (candidate.InstanceId == query.ExcludedInstanceId ||
                                    (candidate.LayerBit & query.LayerMask) == 0)
                                {
                                    continue;
                                }

                                float2 distanceToBounds = math.max(
                                    math.abs(candidate.BoundsCenter - imageCenter) - candidate.BoundsExtents,
                                    0f);
                                float effectiveRadius = query.DetectionRadius * candidate.PerceptionRadiusMultiplier;
                                if (math.lengthsq(distanceToBounds) > effectiveRadius * effectiveRadius)
                                    continue;

                                if (candidate.ShapeCount > 0)
                                {
                                    bool intersects = false;
                                    for (int shape = 0; shape < candidate.ShapeCount; shape++)
                                    {
                                        if (!Shapes[candidate.ShapeStart + shape].IntersectsCircle(imageCenter, effectiveRadius)) continue;
                                        intersects = true;
                                        break;
                                    }
                                    if (!intersects) continue;
                                }

                                writer.Write(itemIndex);
                            }
                            while (SpatialMap.TryGetNextValue(out itemIndex, ref iterator));
                        }
                    }
                }
            }

            writer.EndForEachIndex();
        }

        /// <summary>将世界坐标转换成既有感知格。</summary>
        private static int WorldToCell(float coordinate)
        {
            return (int)math.floor(coordinate / PerceptionCellSize);
        }

        /// <summary>无碰撞编码二维感知格坐标。</summary>
        private static long PackCell(int x, int y)
        {
            return ((long)x << 32) ^ (uint)y;
        }
    }

    #endregion

    #region 空间查询

    /// <summary>仅刷新已注册实体的位置索引，不扫描组件或物理形状。</summary>
    public void NotifyItemSpatialIndexChanged(Item item)
    {
        if (item?.itemData == null)
            return;

        if (WorldRunTimeItems.TryGetValue(item.itemData.Guid, out Item registeredItem) && registeredItem == item)
            RefreshItemSpatialIndex(item);
    }

    /// <summary>合并同一检测器的待执行申请，保留最新版本。</summary>
    public void QueueDetectorQuery(Mod_ItemDetector detector, long requestVersion)
    {
        if (detector == null || requestVersion <= 0)
            return;

        if (!_pendingDetectorSet.Add(detector))
        {
            for (int i = 0; i < _pendingDetectorRequests.Count; i++)
            {
                if (_pendingDetectorRequests[i].Detector != detector)
                    continue;

                _pendingDetectorRequests[i] = new PendingDetectorRequest(detector, requestVersion);
                return;
            }
        }

        _pendingDetectorRequests.Add(new PendingDetectorRequest(detector, requestVersion));
    }

    /// <summary>冻结本批查询、形状和拓扑，随后统一调度 Burst 精筛。</summary>
    private void SchedulePerceptionBatch()
    {
        if (_perceptionJobScheduled || _pendingDetectorRequests.Count == 0)
            return;

        _inFlightDetectors.Clear();
        _inFlightDetectorVersions.Clear();
        for (int i = 0; i < _pendingDetectorRequests.Count; i++)
        {
            PendingDetectorRequest request = _pendingDetectorRequests[i];
            if (request.Detector == null)
                continue;

            _inFlightDetectors.Add(request.Detector);
            _inFlightDetectorVersions.Add(request.Version);
        }

        _pendingDetectorRequests.Clear();
        _pendingDetectorSet.Clear();
        if (_inFlightDetectors.Count == 0)
            return;

        BuildDetectorQuerySnapshot();
        BuildPerceptionItemSnapshot();

        int itemCount = _perceptionSnapshotData.Count;
        int queryCount = _perceptionQueryData.Count;
        PreparePerceptionJobContainers(itemCount, queryCount);

        for (int i = 0; i < itemCount; i++)
        {
            PerceptionItemSnapshot snapshot = _perceptionSnapshotData[i];
            _inFlightItemSnapshots.Add(snapshot);
            _inFlightSpatialMap.Add(snapshot.CellKey, i);
        }

        for (int i = 0; i < queryCount; i++)
            _inFlightQueries.Add(_perceptionQueryData[i]);
        for (int i = 0; i < _perceptionShapeData.Count; i++)
            _inFlightShapes.Add(_perceptionShapeData[i]);

        var queryJob = new BatchedPerceptionQueryJob
        {
            Items = _inFlightItemSnapshots.AsArray(),
            Shapes = _inFlightShapes.AsArray(),
            Queries = _inFlightQueries.AsArray(),
            SpatialMap = _inFlightSpatialMap,
            Results = _inFlightResults.AsWriter()
        };

        _perceptionJobHandle = queryJob.Schedule(queryCount, PerceptionJobBatchSize);
        _perceptionJobScheduled = true;
    }

    /// <summary>在同步边界扩容并复用全部 Native 输入，结果流保留完整结果。</summary>
    private void PreparePerceptionJobContainers(int itemCount, int queryCount)
    {
        int requiredItemCapacity = Mathf.Max(1, itemCount);
        int requiredQueryCapacity = Mathf.Max(1, queryCount);
        int requiredShapeCapacity = Mathf.Max(1, _perceptionShapeData.Count);
        if (!_inFlightShapes.IsCreated)
            _inFlightShapes = new NativeList<PerceptionShape2D>(requiredShapeCapacity, Allocator.Persistent);
        else
        {
            _inFlightShapes.Clear();
            if (_inFlightShapes.Capacity < requiredShapeCapacity) _inFlightShapes.Capacity = requiredShapeCapacity;
        }

        if (!_inFlightItemSnapshots.IsCreated)
            _inFlightItemSnapshots = new NativeList<PerceptionItemSnapshot>(requiredItemCapacity, Allocator.Persistent);
        else
        {
            _inFlightItemSnapshots.Clear();
            if (_inFlightItemSnapshots.Capacity < requiredItemCapacity)
                _inFlightItemSnapshots.Capacity = requiredItemCapacity;
        }

        if (!_inFlightQueries.IsCreated)
            _inFlightQueries = new NativeList<DetectorQuerySnapshot>(requiredQueryCapacity, Allocator.Persistent);
        else
        {
            _inFlightQueries.Clear();
            if (_inFlightQueries.Capacity < requiredQueryCapacity)
                _inFlightQueries.Capacity = requiredQueryCapacity;
        }

        if (!_inFlightSpatialMap.IsCreated)
            _inFlightSpatialMap = new NativeParallelMultiHashMap<long, int>(requiredItemCapacity, Allocator.Persistent);
        else
        {
            _inFlightSpatialMap.Clear();
            if (_inFlightSpatialMap.Capacity < requiredItemCapacity)
                _inFlightSpatialMap.Capacity = requiredItemCapacity;
        }

        DisposePerceptionResultStream();
        _inFlightResults = new NativeStream(queryCount, Allocator.TempJob);
    }

    /// <summary>冻结检测器输入，同时扩大纯数据大体型目标所需的空间格搜索范围。</summary>
    private void BuildDetectorQuerySnapshot()
    {
        _perceptionQueryData.Clear();
        WorldTopologyDomain topology = WorldTopologyRuntime.GetActiveDomain();
        float maximumTargetPerceptionMultiplier = GetMaximumTargetPerceptionMultiplier(out float maximumShapeReach);
        for (int i = 0; i < _inFlightDetectors.Count; i++)
        {
            Mod_ItemDetector detector = _inFlightDetectors[i];
            Item excludedItem = detector.item;
            Vector3 detectorPosition = detector.transform.position;
            float detectionRadius = Mod_ItemDetector.CalculateEffectiveDetectionRadius(
                detector.DetectionRadius,
                1f);
            _perceptionQueryData.Add(new DetectorQuerySnapshot
            {
                Center = new float2(detectorPosition.x, detectorPosition.y),
                DetectionRadius = detectionRadius,
                BroadphaseRadius = detectionRadius * maximumTargetPerceptionMultiplier + maximumShapeReach,
                LayerMask = detector.itemLayer.value,
                ExcludedInstanceId = excludedItem != null ? excludedItem.GetInstanceID() : 0,
                Topology = topology
            });
        }
    }

    /// <summary>复用现有空间格收集候选，每个目标只生成一次冻结快照。</summary>
    private void BuildPerceptionItemSnapshot()
    {
        _perceptionSnapshotItems.Clear();
        _perceptionSnapshotData.Clear();
        _perceptionShapeData.Clear();
        _perceptionSnapshotCells.Clear();
        _perceptionSnapshotItemSet.Clear();

        for (int queryIndex = 0; queryIndex < _perceptionQueryData.Count; queryIndex++)
        {
            DetectorQuerySnapshot query = _perceptionQueryData[queryIndex];
            int minImage = query.Topology.IsWrapped ? -1 : 0;
            int maxImage = query.Topology.IsWrapped ? 1 : 0;
            for (int imageX = minImage; imageX <= maxImage; imageX++)
            {
                for (int imageY = minImage; imageY <= maxImage; imageY++)
                {
                    float centerX = query.Center.x + imageX * query.Topology.Span.x;
                    float centerY = query.Center.y + imageY * query.Topology.Span.y;
                    int minCellX = WorldToPerceptionCell(centerX - query.BroadphaseRadius) - 1;
                    int maxCellX = WorldToPerceptionCell(centerX + query.BroadphaseRadius) + 1;
                    int minCellY = WorldToPerceptionCell(centerY - query.BroadphaseRadius) - 1;
                    int maxCellY = WorldToPerceptionCell(centerY + query.BroadphaseRadius) + 1;
                    for (int cellX = minCellX; cellX <= maxCellX; cellX++)
                    {
                        for (int cellY = minCellY; cellY <= maxCellY; cellY++)
                        {
                            long cellKey = PackPerceptionCell(cellX, cellY);
                            if (!_perceptionSnapshotCells.Add(cellKey) ||
                                !_perceptionCells.TryGetValue(cellKey, out HashSet<Item> cellItems))
                            {
                                continue;
                            }

                            foreach (Item candidate in cellItems)
                                TryAddPerceptionSnapshot(candidate);
                        }
                    }
                }
            }
        }
    }

    /// <summary>Actor 从纯数据形状构建世界包围范围，旧对象才访问 Collider Bridge。</summary>
    private void TryAddPerceptionSnapshot(Item candidate)
    {
        if (candidate == null || candidate.itemData == null ||
            !candidate.gameObject.activeInHierarchy || candidate.DestructionHandled ||
            !_perceptionSnapshotItemSet.Add(candidate))
        {
            return;
        }

        PerceptionTarget target = GetPerceptionTarget(candidate);
        int shapeStart = _perceptionShapeData.Count;
        if (!TryGetPerceptionBounds(candidate, target, out Bounds perceptionBounds)) return;
        if (target.Shapes != null)
        {
            Matrix4x4 matrix = candidate.transform.localToWorldMatrix;
            foreach (PerceptionShape2D local in target.Shapes)
                _perceptionShapeData.Add(TransformPerceptionShape(local, matrix));
        }
        Vector3 position = candidate.transform.position;
        Vector3 boundsCenter = perceptionBounds.center;
        Vector3 boundsExtents = perceptionBounds.extents;
        _perceptionSnapshotItems.Add(candidate);
        _perceptionSnapshotData.Add(new PerceptionItemSnapshot
        {
            CellKey = GetPerceptionCellKey(position),
            Guid = candidate.itemData.Guid,
            InstanceId = candidate.GetInstanceID(),
            LayerBit = 1 << candidate.gameObject.layer,
            PerceptionRadiusMultiplier = candidate.GetPerceptionRadiusMultiplier(),
            BoundsCenter = new float2(boundsCenter.x, boundsCenter.y),
            BoundsExtents = new float2(boundsExtents.x, boundsExtents.y),
            ShapeStart = shapeStart, ShapeCount = _perceptionShapeData.Count - shapeStart,
            Revision = target.Revision
        });
    }

    /// <summary>在注册和结构变化边界选择唯一后端；Actor 从不保留 Collider 引用。</summary>
    private void RefreshPerceptionTarget(Item item)
    {
        if (item == null)
            return;

        var target = new PerceptionTarget { Revision = ++_perceptionRevision };
        if (GameRes.Instance != null && GameRes.Instance.TryGetItemDefinition(item.itemData?.IDName, out RuntimeItemDefinition definition) && definition.IsActor)
            target.Shapes = definition.ActorPerceptionShapes;
        else if (RuntimeAiEntityUtility.IsAiEntity(item))
            target.Shapes = ActorPerceptionShapeCompiler.Compile(item.gameObject);
        else
            target.Bridge = new PerceptionColliderBridge(item);
        _perceptionTargets[item] = target;
    }

    /// <summary>读取已注册感知目标，兼容已有运行时注入入口。</summary>
    private PerceptionTarget GetPerceptionTarget(Item item)
    {
        if (!_perceptionTargets.TryGetValue(item, out PerceptionTarget target))
        {
            RefreshPerceptionTarget(item);
            target = _perceptionTargets[item];
        }
        return target;
    }

    /// <summary>组件集合变化时重建成员缓存，正在飞行的旧代际结果会被拒绝。</summary>
    private void OnPerceptionStructureChanged(Item item)
    {
        if (item?.itemData != null && WorldRunTimeItems.TryGetValue(item.itemData.Guid, out Item registered) && registered == item)
            RefreshPerceptionTarget(item);
    }

    /// <summary>只读取冻结仿射矩阵，不查询物理同步后的 bounds。</summary>
    private static PerceptionShape2D TransformPerceptionShape(PerceptionShape2D local, Matrix4x4 matrix)
    {
        return local.Transform(new float2(matrix.m03, matrix.m13),
            new float2(matrix.m00, matrix.m10), new float2(matrix.m01, matrix.m11));
    }

    /// <summary>纯数据形状计算包围范围，只有非 Actor 进入主线程物理 Bridge。</summary>
    private static bool TryGetPerceptionBounds(Item item, PerceptionTarget target, out Bounds combinedBounds)
    {
        if (target.Shapes == null) return target.Bridge.TryGetBounds(out combinedBounds);
        combinedBounds = default;
        Matrix4x4 matrix = item.transform.localToWorldMatrix;
        for (int index = 0; index < target.Shapes.Length; index++)
        {
            PerceptionShape2D shape = TransformPerceptionShape(target.Shapes[index], matrix);
            var bounds = new Bounds(new Vector3(shape.Center.x, shape.Center.y, 0f), new Vector3(shape.Extents.x * 2f, shape.Extents.y * 2f, 0f));
            if (index == 0) combinedBounds = bounds;
            else combinedBounds.Encapsulate(bounds);
        }
        return target.Shapes.Length > 0;
    }

    /// <summary>完成飞行中的查询，并在选择丢弃时不向旧场景回写结果。</summary>
    private void CompletePerceptionBatch(bool applyResults = true)
    {
        if (!_perceptionJobScheduled)
            return;

        _perceptionJobHandle.Complete();
        if (applyResults)
            ApplyPerceptionBatchResults();

        _perceptionJobScheduled = false;
        DisposePerceptionResultStream();
        _inFlightDetectors.Clear();
        _inFlightDetectorVersions.Clear();
        _perceptionQueryData.Clear();
        _perceptionSnapshotItems.Clear();
        _perceptionSnapshotData.Clear();
        _perceptionShapeData.Clear();
        _perceptionSnapshotCells.Clear();
        _perceptionSnapshotItemSet.Clear();
    }

    /// <summary>接受 Burst 的 Actor 几何结果，仅旧目标补充 Bridge 精筛；所有目标继续经过原有格子 LOS。</summary>
    private void ApplyPerceptionBatchResults()
    {
        NativeStream.Reader reader = _inFlightResults.AsReader();
        for (int queryIndex = 0; queryIndex < _inFlightDetectors.Count; queryIndex++)
        {
            Mod_ItemDetector detector = _inFlightDetectors[queryIndex];
            DetectorQuerySnapshot query = _inFlightQueries[queryIndex];
            int candidateCount = reader.BeginForEachIndex(queryIndex);
            _detectorApplyBuffer.Clear();
            _perceptionResultItemSet.Clear();

            for (int candidateIndex = 0; candidateIndex < candidateCount; candidateIndex++)
            {
                int snapshotIndex = reader.Read<int>();
                if ((uint)snapshotIndex >= (uint)_perceptionSnapshotItems.Count)
                    continue;

                Item candidate = _perceptionSnapshotItems[snapshotIndex];
                PerceptionItemSnapshot snapshot = _inFlightItemSnapshots[snapshotIndex];
                if (!IsSnapshotItemStillValid(candidate, snapshot))
                    continue;

                if (!_perceptionResultItemSet.Add(candidate) ||
                    (snapshot.ShapeCount == 0 && !PassesBridgePerceptionFilter(candidate, query, snapshot)) ||
                    detector == null ||
                    !detector.HasLineOfSight(candidate))
                    continue;

                _detectorApplyBuffer.Add(candidate);
            }

            reader.EndForEachIndex();
            if (detector != null)
                detector.ApplyDetectorResults(_inFlightDetectorVersions[queryIndex], _detectorApplyBuffer);
        }
    }

    /// <summary>拒绝回池复用、结构切换或注销后的旧快照。</summary>
    private bool IsSnapshotItemStillValid(Item candidate, PerceptionItemSnapshot snapshot)
    {
        if (candidate == null || candidate.itemData == null || candidate.DestructionHandled ||
            !candidate.gameObject.activeInHierarchy || candidate.itemData.Guid != snapshot.Guid ||
            candidate.GetInstanceID() != snapshot.InstanceId ||
            !_perceptionTargets.TryGetValue(candidate, out PerceptionTarget target) || target.Revision != snapshot.Revision ||
            ((1 << candidate.gameObject.layer) & snapshot.LayerBit) == 0)
        {
            return false;
        }

        return WorldRunTimeItems.TryGetValue(snapshot.Guid, out Item registeredItem) && registeredItem == candidate;
    }

    /// <summary>只为玩家和其它旧对象保留主线程 Collider 精筛。</summary>
    private bool PassesBridgePerceptionFilter(
        Item candidate,
        DetectorQuerySnapshot query,
        PerceptionItemSnapshot candidateSnapshot)
    {
        int layerBit = 1 << candidate.gameObject.layer;
        if ((query.LayerMask & layerBit) == 0 || candidate.GetInstanceID() == query.ExcludedInstanceId)
            return false;

        Vector2 center = new Vector2(query.Center.x, query.Center.y);
        float effectiveRadius = Mod_ItemDetector.CalculateEffectiveDetectionRadius(
            query.DetectionRadius,
            candidateSnapshot.PerceptionRadiusMultiplier);
        if (!_perceptionTargets.TryGetValue(candidate, out PerceptionTarget target) || target.Bridge == null)
            return false;

        int minImage = query.Topology.IsWrapped ? -1 : 0;
        int maxImage = query.Topology.IsWrapped ? 1 : 0;
        for (int imageX = minImage; imageX <= maxImage; imageX++)
        {
            for (int imageY = minImage; imageY <= maxImage; imageY++)
            {
                Vector2 imageCenter = center + new Vector2(
                    imageX * query.Topology.Span.x,
                    imageY * query.Topology.Span.y);
                if (target.Bridge.IntersectsCircle(new float2(imageCenter.x, imageCenter.y), effectiveRadius))
                    return true;
            }
        }
        return false;
    }

    /// <summary>完成任务后释放形状、查询和索引的唯一 Native 所有权。</summary>
    private void DisposePerceptionJobData()
    {
        DisposePerceptionResultStream();
        if (_inFlightItemSnapshots.IsCreated)
            _inFlightItemSnapshots.Dispose();
        if (_inFlightShapes.IsCreated) _inFlightShapes.Dispose();
        _perceptionTargets.Clear();
        _perceptionShapeData.Clear();
        if (_inFlightQueries.IsCreated)
            _inFlightQueries.Dispose();
        if (_inFlightSpatialMap.IsCreated)
            _inFlightSpatialMap.Dispose();
    }

    /// <summary>在依赖任务已完成后释放本批结果流。</summary>
    private void DisposePerceptionResultStream()
    {
        if (_inFlightResults.IsCreated)
            _inFlightResults.Dispose();
    }

    /// <summary>同步收集圆形范围内的目标，复用调用方缓冲并对循环镜像去重。</summary>
    public void QueryItemsInCircleNonAlloc(
        Vector2 center,
        float radius,
        LayerMask layerMask,
        Item excludedItem,
        List<Item> results,
        HashSet<Item> dedupe)
    {
        if (results == null)
            throw new ArgumentNullException(nameof(results));
        if (dedupe == null)
            throw new ArgumentNullException(nameof(dedupe));

        results.Clear();
        dedupe.Clear();
        if (radius < 0f)
            return;

        float multiplier = GetMaximumTargetPerceptionMultiplier(out float maximumShapeReach);
        float broadphaseRadius = Mod_ItemDetector.CalculateEffectiveDetectionRadius(radius, multiplier) + maximumShapeReach;
        WorldTopologyDomain topology = WorldTopologyRuntime.GetActiveDomain();
        int minImage = topology.IsWrapped ? -1 : 0;
        int maxImage = topology.IsWrapped ? 1 : 0;
        for (int imageX = minImage; imageX <= maxImage; imageX++)
        {
            for (int imageY = minImage; imageY <= maxImage; imageY++)
            {
                Vector2 imageCenter = center + new Vector2(imageX * topology.Span.x, imageY * topology.Span.y);
                int minCellX = WorldToPerceptionCell(imageCenter.x - broadphaseRadius) - 1;
                int maxCellX = WorldToPerceptionCell(imageCenter.x + broadphaseRadius) + 1;
                int minCellY = WorldToPerceptionCell(imageCenter.y - broadphaseRadius) - 1;
                int maxCellY = WorldToPerceptionCell(imageCenter.y + broadphaseRadius) + 1;
                for (int cellX = minCellX; cellX <= maxCellX; cellX++)
                {
                    for (int cellY = minCellY; cellY <= maxCellY; cellY++)
                    {
                        long cellKey = PackPerceptionCell(cellX, cellY);
                        if (!_perceptionCells.TryGetValue(cellKey, out HashSet<Item> cellItems))
                            continue;

                        foreach (Item candidate in cellItems)
                            TryAddSpatialCandidate(candidate, imageCenter, radius, layerMask, excludedItem, results, dedupe);
                    }
                }
            }
        }
    }

    /// <summary>实体跨格时更新成员关系，同格移动无需更改集合。</summary>
    private void RefreshItemSpatialIndex(Item item)
    {
        if (item == null || item.itemData == null)
            return;

        long newCellKey = GetPerceptionCellKey(item.transform.position);
        if (_itemPerceptionCells.TryGetValue(item, out long currentCellKey))
        {
            if (currentCellKey == newCellKey)
                return;

            RemoveItemFromPerceptionCell(item, currentCellKey);
        }

        if (!_perceptionCells.TryGetValue(newCellKey, out HashSet<Item> targetCell))
        {
            targetCell = _perceptionCellPool.Count > 0
                ? _perceptionCellPool.Pop()
                : new HashSet<Item>();
            _perceptionCells[newCellKey] = targetCell;
        }

        targetCell.Add(item);
        _itemPerceptionCells[item] = newCellKey;
    }

    /// <summary>从已登记的感知格中注销实体。</summary>
    private void RemoveItemFromSpatialIndex(Item item)
    {
        if (ReferenceEquals(item, null) || !_itemPerceptionCells.TryGetValue(item, out long cellKey))
            return;

        RemoveItemFromPerceptionCell(item, cellKey);
    }

    /// <summary>移除格内成员并回收空格集合。</summary>
    private void RemoveItemFromPerceptionCell(Item item, long cellKey)
    {
        _itemPerceptionCells.Remove(item);
        if (!_perceptionCells.TryGetValue(cellKey, out HashSet<Item> cellItems))
            return;

        cellItems.Remove(item);
        if (cellItems.Count > 0)
            return;

        _perceptionCells.Remove(cellKey);
        cellItems.Clear();
        _perceptionCellPool.Push(cellItems);
    }

    /// <summary>在维护边界同时重建空间与感知后端，清除已销毁对象的 Bridge/几何引用。</summary>
    private void RebuildSpatialIndex()
    {
        CompletePerceptionBatch(false);
        _perceptionTargets.Clear();
        foreach (HashSet<Item> cellItems in _perceptionCells.Values)
        {
            cellItems.Clear();
            _perceptionCellPool.Push(cellItems);
        }

        _perceptionCells.Clear();
        _itemPerceptionCells.Clear();

        for (int i = 0; i < RuntimeItems.Count; i++)
        {
            Item item = RuntimeItems[i];
            if (item != null)
            {
                RefreshItemSpatialIndex(item);
                RefreshPerceptionTarget(item);
            }
        }
    }

    /// <summary>将正负世界坐标向下取整为感知格坐标。</summary>
    private static int WorldToPerceptionCell(float coordinate)
    {
        return Mathf.FloorToInt(coordinate / PerceptionCellSize);
    }

    /// <summary>取得实体中心所在的感知格键。</summary>
    private static long GetPerceptionCellKey(Vector2 position)
    {
        return PackPerceptionCell(
            WorldToPerceptionCell(position.x),
            WorldToPerceptionCell(position.y));
    }

    /// <summary>无碰撞编码两个有符号格坐标。</summary>
    private static long PackPerceptionCell(int x, int y)
    {
        return ((long)x << 32) ^ (uint)y;
    }

    /// <summary>一次扫描取得最大目标倍率和纯数据几何跨度，保证中心格登记的大体型目标不漏筛。</summary>
    private float GetMaximumTargetPerceptionMultiplier(out float maximumShapeReach)
    {
        float maximumMultiplier = 1f;
        maximumShapeReach = 0f;
        for (int i = 0; i < RuntimeItems.Count; i++)
        {
            Item candidate = RuntimeItems[i];
            if (candidate == null || candidate.itemData == null ||
                !candidate.gameObject.activeInHierarchy || candidate.DestructionHandled)
            {
                continue;
            }

            maximumMultiplier = Mathf.Max(
                maximumMultiplier,
                candidate.GetPerceptionRadiusMultiplier());
            PerceptionTarget target = GetPerceptionTarget(candidate);
            if (target.Shapes != null && TryGetPerceptionBounds(candidate, target, out Bounds bounds))
            {
                Vector2 reach = (Vector2)(bounds.center - candidate.transform.position);
                maximumShapeReach = Mathf.Max(maximumShapeReach,
                    Mathf.Max(Mathf.Abs(reach.x) + bounds.extents.x, Mathf.Abs(reach.y) + bounds.extents.y));
            }
        }

        return maximumMultiplier;
    }

    /// <summary>同步空间查询对 Actor 也使用纯数据几何，旧对象保留 Bridge 语义。</summary>
    private void TryAddSpatialCandidate(
        Item candidate,
        Vector2 center,
        float radius,
        LayerMask layerMask,
        Item excludedItem,
        List<Item> results,
        HashSet<Item> dedupe)
    {
        if (candidate == null || candidate == excludedItem || candidate.itemData == null ||
            !candidate.gameObject.activeInHierarchy || candidate.DestructionHandled || dedupe.Contains(candidate))
        {
            return;
        }

        int layerBit = 1 << candidate.gameObject.layer;
        if ((layerMask.value & layerBit) == 0)
            return;

        float effectiveRadius = Mod_ItemDetector.CalculateEffectiveDetectionRadius(
            radius,
            candidate.GetPerceptionRadiusMultiplier());
        PerceptionTarget target = GetPerceptionTarget(candidate);
        if (IntersectsPerceptionTarget(candidate, target, new float2(center.x, center.y), effectiveRadius) && dedupe.Add(candidate))
            results.Add(candidate);
    }

    /// <summary>在同步查询与持续锁定中共享同一纯数据形状，不让大体型目标被中心点判断提前丢失。</summary>
    public bool TryIsWithinDataPerceptionRange(Vector2 center, Item targetItem, float radius, out bool within)
    {
        within = false;
        if (targetItem == null || !_perceptionTargets.TryGetValue(targetItem, out PerceptionTarget target) || target.Shapes == null) return false;
        WorldTopologyDomain topology = WorldTopologyRuntime.GetActiveDomain();
        int min = topology.IsWrapped ? -1 : 0;
        int max = topology.IsWrapped ? 1 : 0;
        for (int x = min; x <= max; x++)
        for (int y = min; y <= max; y++)
        {
            if (!IntersectsPerceptionTarget(targetItem, target,
                new float2(center.x + x * topology.Span.x, center.y + y * topology.Span.y), radius)) continue;
            within = true;
            return true;
        }
        return true;
    }

    /// <summary>同步路径按目标的唯一后端完成几何判断。</summary>
    private static bool IntersectsPerceptionTarget(Item item, PerceptionTarget target, float2 center, float radius)
    {
        if (target.Shapes == null) return target.Bridge.IntersectsCircle(center, radius);
        Matrix4x4 matrix = item.transform.localToWorldMatrix;
        foreach (PerceptionShape2D shape in target.Shapes)
            if (TransformPerceptionShape(shape, matrix).IntersectsCircle(center, radius)) return true;
        return false;
    }

    #endregion
}
