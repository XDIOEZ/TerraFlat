using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using FlatWorld.Geometry;
using FlatWorld.Diagnostics;

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

    /// <summary>感知格持有成员和批次访问戳，避免每批额外维护已访问 HashSet。</summary>
    private sealed class PerceptionCell
    {
        public readonly HashSet<Item> Items = new();
        public uint LastVisitedBatch;

        public void Reset()
        {
            Items.Clear();
            LastVisitedBatch = 0;
        }
    }

    private const float PerceptionCellSize = 8f;
    // 不同逻辑世界的同坐标使用独立桶，避免混扫其他世界的实体。
    internal readonly struct PerceptionCellKey : IEquatable<PerceptionCellKey>
    {
        public readonly int WorldId;
        public readonly long PositionKey;
        public PerceptionCellKey(int worldId, long positionKey) { WorldId = worldId; PositionKey = positionKey; }
        public bool Equals(PerceptionCellKey other) => WorldId == other.WorldId && PositionKey == other.PositionKey;
        public override bool Equals(object other) => other is PerceptionCellKey key && Equals(key);
        public override int GetHashCode() => unchecked(((WorldId * 397) ^ (int)PositionKey) * 397 ^ (int)(PositionKey >> 32));
    }
    private readonly Dictionary<PerceptionCellKey, PerceptionCell> _perceptionCells = new();
    private readonly Dictionary<Item, PerceptionCellKey> _itemPerceptionCells = new();
    private readonly Stack<PerceptionCell> _perceptionCellPool = new();
    private uint _perceptionVisitBatch;

    [ShowInInspector, Sirenix.OdinInspector.ReadOnly] private int PerceptionCellCount => _perceptionCells.Count;

    #region 并行感知调度

    // 按批次和检测器划分采样，避免每个候选都打点干扰实际成本。
    private static readonly PerformanceDebugPoint PerceptionJobCompleteMarker = new("ItemMgr.Perception.JobComplete");
    private static readonly PerformanceDebugPoint PerceptionApplyMarker = new("ItemMgr.Perception.Apply");
    private static readonly PerformanceDebugPoint PerceptionFilterCandidatesMarker = new("ItemMgr.Perception.FilterCandidates");
    private static readonly PerformanceDebugPoint PerceptionDetectorApplyMarker = new("ItemMgr.Perception.DetectorApply");
    private static readonly PerformanceDebugPoint PerceptionClearMarker = new("ItemMgr.Perception.Clear");
    // 批次末汇总工作量，验证与 Bridge 仍保留在原 Filter 计时内。
    private static readonly PerformanceDebugPoint PerceptionRawCandidatesDebug = new("FlatWorld.Perception.RawCandidates");
    private static readonly PerformanceDebugPoint PerceptionValidCandidatesDebug = new("FlatWorld.Perception.ValidCandidates");
    private static readonly PerformanceDebugPoint PerceptionInvalidCandidatesDebug = new("FlatWorld.Perception.InvalidCandidates");
    private static readonly PerformanceDebugPoint PerceptionDuplicateCandidatesDebug = new("FlatWorld.Perception.DuplicateCandidates");
    private static readonly PerformanceDebugPoint PerceptionBridgeChecksDebug = new("FlatWorld.Perception.BridgeChecks");
    private static readonly PerformanceDebugPoint PerceptionBridgeRejectedDebug = new("FlatWorld.Perception.BridgeRejected");
    private static readonly PerformanceDebugPoint PerceptionLosChecksDebug = new("FlatWorld.Perception.LOSChecks");
    private static readonly PerformanceDebugPoint PerceptionLosRejectedDebug = new("FlatWorld.Perception.LOSRejected");
    private static readonly PerformanceDebugPoint PerceptionFilteredResultsDebug = new("FlatWorld.Perception.FilteredResults");
    private static readonly PerformanceDebugPoint PerceptionMissingDetectorDebug = new("FlatWorld.Perception.MissingDetectorCandidates");
    private static readonly PerformanceDebugPoint PerceptionStaleDetectorDebug = new("FlatWorld.Perception.StaleDetectorBatches");

    private const int PerceptionJobBatchSize = 8;

    private readonly List<PendingDetectorRequest> _pendingDetectorRequests = new(64);
    private readonly Dictionary<Mod_ItemDetector, int> _pendingDetectorIndices = new(64);
    private readonly List<Mod_ItemDetector> _inFlightDetectors = new(64);
    private readonly List<long> _inFlightDetectorVersions = new(64);
    private readonly List<DetectorQuerySnapshot> _perceptionQueryData = new(64);
    private readonly List<Item> _perceptionSnapshotItems = new(256);
    private readonly List<PerceptionItemSnapshot> _perceptionSnapshotData = new(256);
    // 世界空间纯数据形状按目标连续存放，支持一个目标拥有多个不相连的形状。
    private readonly List<PerceptionShape2D> _perceptionShapeData = new(256);
    private readonly HashSet<Item> _perceptionSnapshotItemSet = new();
    private readonly HashSet<Item> _perceptionResultItemSet = new();
    private readonly List<Item> _detectorApplyBuffer = new(64);

    private NativeList<PerceptionItemSnapshot> _inFlightItemSnapshots;
    private NativeList<PerceptionShape2D> _inFlightShapes;
    private NativeList<DetectorQuerySnapshot> _inFlightQueries;
    private NativeParallelMultiHashMap<PerceptionCellKey, int> _inFlightSpatialMap;
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

    internal struct PerceptionItemSnapshot
    {
        public PerceptionCellKey CellKey;
        public int Guid;
        public int InstanceId;
        public int WorldId;
        public ulong WorldRevision;
        public int LayerBit;
        public float PerceptionRadiusMultiplier;
        public float2 BoundsCenter;
        public float2 BoundsExtents;
        public int ShapeStart;
        public int ShapeCount;
        public uint Revision;
    }

    internal struct DetectorQuerySnapshot
    {
        public float2 Center;
        public float DetectionRadius;
        public float MaximumDetectionRadius;
        public float BroadphaseRadius;
        public int LayerMask;
        public int ExcludedInstanceId;
        public int WorldId;
        public ulong WorldRevision;
        public WorldTopologyDomain Topology;
    }

    [BurstCompile]
    internal struct BatchedPerceptionQueryJob : IJobParallelFor
    {
        [Unity.Collections.ReadOnly] public NativeArray<PerceptionItemSnapshot> Items;
        [Unity.Collections.ReadOnly] public NativeArray<PerceptionShape2D> Shapes;
        [Unity.Collections.ReadOnly] public NativeArray<DetectorQuerySnapshot> Queries;
        [Unity.Collections.ReadOnly] public NativeParallelMultiHashMap<PerceptionCellKey, int> SpatialMap;
        public NativeStream.Writer Results;

        /// <summary>空间格粗筛后在 Burst 内完成 Actor 圆形/AABB 的最终几何判断。</summary>
        public void Execute(int queryIndex)
        {
            DetectorQuerySnapshot query = Queries[queryIndex];
            NativeStream.Writer writer = Results;
            writer.BeginForEachIndex(queryIndex);
            if (query.WorldId == 0) { writer.EndForEachIndex(); return; }

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
                            PerceptionCellKey cellKey = new(query.WorldId, PackCell(cellX, cellY));
                            if (!SpatialMap.TryGetFirstValue(cellKey, out int itemIndex, out NativeParallelMultiHashMapIterator<PerceptionCellKey> iterator))
                                continue;

                            do
                            {
                                PerceptionItemSnapshot candidate = Items[itemIndex];
                                if (candidate.WorldId != query.WorldId || candidate.InstanceId == query.ExcludedInstanceId ||
                                    (candidate.LayerBit & query.LayerMask) == 0)
                                {
                                    continue;
                                }

                                float2 distanceToBounds = math.max(
                                    math.abs(candidate.BoundsCenter - imageCenter) - candidate.BoundsExtents,
                                    0f);
                                float effectiveRadius = math.min(query.MaximumDetectionRadius,
                                    query.DetectionRadius * candidate.PerceptionRadiusMultiplier);
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

        // 首次排队只查一次字典，重复申请则直接覆盖原槽位的最新版本。
        if (_pendingDetectorIndices.TryAdd(detector, _pendingDetectorRequests.Count))
        {
            _pendingDetectorRequests.Add(new PendingDetectorRequest(detector, requestVersion));
            return;
        }

        int index = _pendingDetectorIndices[detector];
        _pendingDetectorRequests[index] = new PendingDetectorRequest(detector, requestVersion);
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
            Item detectorItem = request.Detector.item;
            if ((detectorItem != null ? GameplayWorldRegistry.GetItemWorldIdentity(detectorItem) : GameplayWorldRegistry.ExecutionWorldIdentity) == 0)
            {
                request.Detector.ApplyDetectorResults(request.Version, null);
                continue;
            }

            _inFlightDetectors.Add(request.Detector);
            _inFlightDetectorVersions.Add(request.Version);
        }

        _pendingDetectorRequests.Clear();
        _pendingDetectorIndices.Clear();
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
            _inFlightSpatialMap = new NativeParallelMultiHashMap<PerceptionCellKey, int>(requiredItemCapacity, Allocator.Persistent);
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
            string worldKey = excludedItem != null ? GameplayWorldRegistry.GetItemWorldKey(excludedItem) : GameplayWorldRegistry.ExecutionWorldKey;
            if (GameplayWorldRegistry.TryGet(worldKey, out _))
                using (GameplayWorldRegistry.UseWorld(worldKey)) topology = WorldTopologyRuntime.GetActiveDomain();
            else topology = string.Equals(worldKey, GameplayWorldRegistry.ExecutionWorldKey, StringComparison.Ordinal)
                ? WorldTopologyRuntime.GetActiveDomain() : default;
            float detectionRadius = Mod_ItemDetector.CalculateEffectiveDetectionRadius(
                detector.DetectionRadius,
                1f);
            _perceptionQueryData.Add(new DetectorQuerySnapshot
            {
                Center = new float2(detectorPosition.x, detectorPosition.y),
                DetectionRadius = detectionRadius,
                MaximumDetectionRadius = detector.MaximumDetectionRadius,
                BroadphaseRadius = Mathf.Min(detector.MaximumDetectionRadius,
                    detectionRadius * maximumTargetPerceptionMultiplier) + maximumShapeReach,
                LayerMask = detector.itemLayer.value,
                ExcludedInstanceId = excludedItem != null ? excludedItem.GetInstanceID() : 0,
                WorldId = excludedItem != null ? GameplayWorldRegistry.GetItemWorldIdentity(excludedItem) : GameplayWorldRegistry.ExecutionWorldIdentity,
                WorldRevision = excludedItem != null ? GameplayWorldRegistry.GetItemWorldRevision(excludedItem) : 0UL,
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
        _perceptionSnapshotItemSet.Clear();
        uint visitBatch = BeginPerceptionVisitBatch();

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
                            PerceptionCellKey cellKey = new(query.WorldId, PackPerceptionCell(cellX, cellY));
                            if (!_perceptionCells.TryGetValue(cellKey, out PerceptionCell cell) ||
                                cell.LastVisitedBatch == visitBatch)
                            {
                                continue;
                            }

                            cell.LastVisitedBatch = visitBatch;
                            foreach (Item candidate in cell.Items)
                                TryAddPerceptionSnapshot(candidate);
                        }
                    }
                }
            }
        }
    }

    /// <summary>推进访问批次；极低频溢出时重置所有格子的访问戳。</summary>
    private uint BeginPerceptionVisitBatch()
    {
        _perceptionVisitBatch++;
        if (_perceptionVisitBatch != 0)
            return _perceptionVisitBatch;

        foreach (PerceptionCell cell in _perceptionCells.Values)
            cell.LastVisitedBatch = 0;

        _perceptionVisitBatch = 1;
        return _perceptionVisitBatch;
    }

    /// <summary>Actor 从纯数据形状构建世界包围范围，旧对象才访问 Collider Bridge。</summary>
    private void TryAddPerceptionSnapshot(Item candidate)
    {
        if (candidate == null || candidate.itemData == null ||
            !GameplayWorldRegistry.TryGetItemWorldOwnership(candidate, out var world) ||
            !candidate.gameObject.activeInHierarchy || candidate.DestructionHandled ||
            !_perceptionSnapshotItemSet.Add(candidate))
        {
            return;
        }

        PerceptionTarget target = GetPerceptionTarget(candidate);
        int shapeStart = _perceptionShapeData.Count;
        if (!TryGetPerceptionBounds(candidate, target, out Bounds perceptionBounds,
                target.Shapes != null ? _perceptionShapeData : null)) return;
        Vector3 position = candidate.transform.position;
        Vector3 boundsCenter = perceptionBounds.center;
        Vector3 boundsExtents = perceptionBounds.extents;
        _perceptionSnapshotItems.Add(candidate);
        _perceptionSnapshotData.Add(new PerceptionItemSnapshot
        {
            CellKey = GetPerceptionCellKey(world.WorldIdentity, position),
            Guid = candidate.itemData.Guid,
            InstanceId = candidate.GetInstanceID(),
            WorldId = world.WorldIdentity,
            WorldRevision = world.Revision,
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
    private static bool TryGetPerceptionBounds(Item item, PerceptionTarget target, out Bounds combinedBounds,
        List<PerceptionShape2D> worldShapes = null)
    {
        if (target.Shapes == null) return target.Bridge.TryGetBounds(out combinedBounds);
        return TryBuildPerceptionShapes(target.Shapes, item.transform.localToWorldMatrix, worldShapes,
            out combinedBounds);
    }

    // 同一次世界变换兼得粗筛包围盒和 Burst 形状，避免对每个 Actor 重算两遍。
    internal static bool TryBuildPerceptionShapes(PerceptionShape2D[] localShapes, Matrix4x4 matrix,
        List<PerceptionShape2D> worldShapes, out Bounds combinedBounds)
    {
        combinedBounds = default;
        if (localShapes == null || localShapes.Length == 0)
            return false;
        float2 translation = new(matrix.m03, matrix.m13);
        float2 axisX = new(matrix.m00, matrix.m10);
        float2 axisY = new(matrix.m01, matrix.m11);
        for (int index = 0; index < localShapes.Length; index++)
        {
            PerceptionShape2D shape = localShapes[index].Transform(translation, axisX, axisY);
            worldShapes?.Add(shape);
            var bounds = new Bounds(new Vector3(shape.Center.x, shape.Center.y, 0f), new Vector3(shape.Extents.x * 2f, shape.Extents.y * 2f, 0f));
            if (index == 0) combinedBounds = bounds;
            else combinedBounds.Encapsulate(bounds);
        }
        return true;
    }

    /// <summary>完成飞行中的查询，并在选择丢弃时不向旧场景回写结果。</summary>
    private void CompletePerceptionBatch(bool applyResults = true)
    {
        if (!_perceptionJobScheduled)
            return;

        using (PerceptionJobCompleteMarker.Measure())
            _perceptionJobHandle.Complete();
        if (applyResults)
        {
            using (PerceptionApplyMarker.Measure(_inFlightDetectors.Count))
                ApplyPerceptionBatchResults();
        }

        using (PerceptionClearMarker.Measure())
        {
            _perceptionJobScheduled = false;
            DisposePerceptionResultStream();
            _inFlightDetectors.Clear();
            _inFlightDetectorVersions.Clear();
            _perceptionQueryData.Clear();
            _perceptionSnapshotItems.Clear();
            _perceptionSnapshotData.Clear();
            _perceptionShapeData.Clear();
            _perceptionSnapshotItemSet.Clear();
        }
    }

    /// <summary>接受 Burst 的 Actor 几何结果，仅旧目标补充 Bridge 精筛；所有目标继续经过原有格子 LOS。</summary>
    private void ApplyPerceptionBatchResults()
    {
        NativeStream.Reader reader = _inFlightResults.AsReader();
        long raw = 0, valid = 0, invalid = 0, duplicate = 0, bridgeChecks = 0, bridgeRejected = 0;
        long losChecks = 0, losRejected = 0, filteredResults = 0, missingDetector = 0, staleDetector = 0;
        try
        {
            for (int queryIndex = 0; queryIndex < _inFlightDetectors.Count; queryIndex++)
            {
                Mod_ItemDetector detector = _inFlightDetectors[queryIndex];
                int candidateCount = reader.BeginForEachIndex(queryIndex);
                raw += candidateCount;
                _detectorApplyBuffer.Clear();
                _perceptionResultItemSet.Clear();

                if (IsPerceptionDetectorRequestStale(detector, _inFlightDetectorVersions[queryIndex]) ||
                    detector != null && !IsPerceptionDetectorWorldCurrent(detector, _inFlightQueries[queryIndex]))
                {
                    // 过期结果不能回写，但 NativeStream 必须读完本组后才能继续下一组。
                    DiscardPerceptionCandidateIndices(ref reader, candidateCount);
                    reader.EndForEachIndex();
                    staleDetector++;
                    continue;
                }

                DetectorQuerySnapshot query = _inFlightQueries[queryIndex];
                string worldKey = detector?.item != null ? GameplayWorldRegistry.GetItemWorldKey(detector.item) : GameplayWorldRegistry.ExecutionWorldKey;
                using var worldScope = GameplayWorldRegistry.TryGet(worldKey, out _)
                    ? GameplayWorldRegistry.UseWorld(worldKey) : default;
                using (PerceptionFilterCandidatesMarker.Measure(candidateCount))
                {
                    using GridLineOfSightQuery lineOfSight = candidateCount > 0 && detector != null
                        ? detector.BeginLineOfSightQuery() : null;
                    for (int candidateIndex = 0; candidateIndex < candidateCount; candidateIndex++)
                    {
                        int snapshotIndex = reader.Read<int>();
                        if ((uint)snapshotIndex >= (uint)_perceptionSnapshotItems.Count)
                        {
                            invalid++;
                            continue;
                        }

                        Item candidate = _perceptionSnapshotItems[snapshotIndex];
                        PerceptionItemSnapshot snapshot = _inFlightItemSnapshots[snapshotIndex];
                        if (!IsSnapshotItemStillValid(candidate, snapshot))
                        {
                            invalid++;
                            continue;
                        }
                        valid++;

                        if (!_perceptionResultItemSet.Add(candidate))
                        {
                            duplicate++;
                            continue;
                        }
                        if (snapshot.ShapeCount == 0)
                        {
                            bridgeChecks++;
                            if (!PassesBridgePerceptionFilter(candidate, query, snapshot))
                            {
                                bridgeRejected++;
                                continue;
                            }
                        }
                        if (detector == null)
                        {
                            missingDetector++;
                            continue;
                        }
                        losChecks++;
                        if (!detector.HasLineOfSight(candidate, lineOfSight))
                        {
                            losRejected++;
                            continue;
                        }

                        _detectorApplyBuffer.Add(candidate);
                        filteredResults++;
                    }
                }

                reader.EndForEachIndex();
                if (detector != null && IsPerceptionDetectorWorldCurrent(detector, query))
                {
                    if (detector.RequestedVersion != _inFlightDetectorVersions[queryIndex]) staleDetector++;
                    using (PerceptionDetectorApplyMarker.Measure(_detectorApplyBuffer.Count))
                        detector.ApplyDetectorResults(_inFlightDetectorVersions[queryIndex], _detectorApplyBuffer);
                }
                else if (detector != null) staleDetector++;
            }
        }
        finally
        {
            PerceptionRawCandidatesDebug.AddWork(raw);
            PerceptionValidCandidatesDebug.AddWork(valid);
            PerceptionInvalidCandidatesDebug.AddWork(invalid);
            PerceptionDuplicateCandidatesDebug.AddWork(duplicate);
            PerceptionBridgeChecksDebug.AddWork(bridgeChecks);
            PerceptionBridgeRejectedDebug.AddWork(bridgeRejected);
            PerceptionLosChecksDebug.AddWork(losChecks);
            PerceptionLosRejectedDebug.AddWork(losRejected);
            PerceptionFilteredResultsDebug.AddWork(filteredResults);
            PerceptionMissingDetectorDebug.AddWork(missingDetector);
            PerceptionStaleDetectorDebug.AddWork(staleDetector);
        }
    }

    #region 过期感知批次快速丢弃

    /// <summary>版本已更新的检测器不能消费旧结果；已销毁检测器沿用原缺失候选统计流程。</summary>
    internal static bool IsPerceptionDetectorRequestStale(Mod_ItemDetector detector, long submittedVersion)
        => detector != null && detector.RequestedVersion != submittedVersion;

    // 同一实例在不同世界之间往返后仍必须拒绝迁移前的异步感知结果。
    internal static bool IsPerceptionItemWorldCurrent(Item item, int worldId, ulong worldRevision)
        => worldId != 0 && (item != null
            ? GameplayWorldRegistry.TryGetItemWorldOwnership(item, out var owner) && owner.WorldIdentity == worldId && owner.Revision == worldRevision
            : GameplayWorldRegistry.ExecutionWorldIdentity == worldId && worldRevision == 0UL);

    private static bool IsPerceptionDetectorWorldCurrent(Mod_ItemDetector detector, DetectorQuerySnapshot query)
    {
        Item owner = detector.item;
        return (owner != null ? owner.GetInstanceID() : 0) == query.ExcludedInstanceId &&
            IsPerceptionItemWorldCurrent(owner, query.WorldId, query.WorldRevision);
    }

    /// <summary>逐项消费本组原始索引，避免后续结果流发生未读数据残留。</summary>
    internal static void DiscardPerceptionCandidateIndices(ref NativeStream.Reader reader, int count)
    {
        for (int i = 0; i < count; i++)
            reader.Read<int>();
    }

    #endregion

    /// <summary>拒绝回池复用、结构切换或注销后的旧快照。</summary>
    private bool IsSnapshotItemStillValid(Item candidate, PerceptionItemSnapshot snapshot)
    {
        if (candidate == null || candidate.itemData == null || candidate.DestructionHandled ||
            !candidate.gameObject.activeInHierarchy || candidate.itemData.Guid != snapshot.Guid ||
            candidate.GetInstanceID() != snapshot.InstanceId ||
            !IsPerceptionItemWorldCurrent(candidate, snapshot.WorldId, snapshot.WorldRevision) ||
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
        float effectiveRadius = Mathf.Min(query.MaximumDetectionRadius, Mod_ItemDetector.CalculateEffectiveDetectionRadius(
            query.DetectionRadius,
            candidateSnapshot.PerceptionRadiusMultiplier));
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
        => CollectItemsInCircle(center, radius, layerMask, excludedItem, results, dedupe, true);

    /// <summary>掉落物按中心距离查附近空间桶，不为拾取或吸燃料扫描全世界感知几何。</summary>
    public void QueryItemCentersInCircleNonAlloc(
        Vector2 center, float radius, LayerMask layerMask, Item excludedItem,
        List<Item> results, HashSet<Item> dedupe)
        => CollectItemsInCircle(center, radius, layerMask, excludedItem, results, dedupe, false);

    private void CollectItemsInCircle(
        Vector2 center, float radius, LayerMask layerMask, Item excludedItem,
        List<Item> results, HashSet<Item> dedupe, bool useTargetGeometry)
    {
        if (results == null)
            throw new ArgumentNullException(nameof(results));
        if (dedupe == null)
            throw new ArgumentNullException(nameof(dedupe));
        results.Clear();
        dedupe.Clear();
        string world = excludedItem != null ? GameplayWorldRegistry.GetItemWorldKey(excludedItem) : GameplayWorldRegistry.ExecutionWorldKey;
        int worldId = excludedItem != null ? GameplayWorldRegistry.GetItemWorldIdentity(excludedItem) : GameplayWorldRegistry.ExecutionWorldIdentity;
        if (worldId == 0) return;
        bool hasWorld = GameplayWorldRegistry.TryGet(world, out _);
        using var worldScope = hasWorld ? GameplayWorldRegistry.UseWorld(world) : default;
        if (!float.IsFinite(radius) || radius < 0f || !float.IsFinite(center.x) || !float.IsFinite(center.y))
            return;

        float broadphaseRadius = radius;
        if (useTargetGeometry)
        {
            float multiplier = GetMaximumTargetPerceptionMultiplier(out float maximumShapeReach);
            broadphaseRadius = Mod_ItemDetector.CalculateEffectiveDetectionRadius(radius, multiplier) + maximumShapeReach;
        }
        WorldTopologyDomain topology = hasWorld || string.Equals(world, GameplayWorldRegistry.ExecutionWorldKey, StringComparison.Ordinal)
            ? WorldTopologyRuntime.GetActiveDomain() : default;
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
                        PerceptionCellKey cellKey = new(worldId, PackPerceptionCell(cellX, cellY));
                        if (!_perceptionCells.TryGetValue(cellKey, out PerceptionCell cell))
                            continue;

                        foreach (Item candidate in cell.Items)
                        {
                            if (candidate == null || GameplayWorldRegistry.GetItemWorldIdentity(candidate) != worldId) continue;
                            if (useTargetGeometry)
                                TryAddSpatialCandidate(candidate, imageCenter, radius, layerMask, excludedItem, results, dedupe);
                            else
                                TryAddCenterCandidate(candidate, imageCenter, radius, layerMask, excludedItem, results, dedupe);
                        }
                    }
                }
            }
        }
    }

    private static void TryAddCenterCandidate(
        Item candidate, Vector2 center, float radius, LayerMask layerMask, Item excludedItem,
        List<Item> results, HashSet<Item> dedupe)
    {
        if (candidate == null || candidate == excludedItem || candidate.itemData == null ||
            !candidate.gameObject.activeInHierarchy || candidate.DestructionHandled || dedupe.Contains(candidate) ||
            (layerMask.value & 1 << candidate.gameObject.layer) == 0)
            return;
        if (((Vector2)candidate.transform.position - center).sqrMagnitude <= radius * radius && dedupe.Add(candidate))
            results.Add(candidate);
    }

    /// <summary>实体跨格时更新成员关系，同格移动无需更改集合。</summary>
    private void RefreshItemSpatialIndex(Item item)
    {
        if (item == null || item.itemData == null)
            return;

        int worldIdentity = GameplayWorldRegistry.GetItemWorldIdentity(item);
        if (worldIdentity == 0) { RemoveItemFromSpatialIndex(item); return; }
        PerceptionCellKey newCellKey = GetPerceptionCellKey(worldIdentity, item.transform.position);
        if (_itemPerceptionCells.TryGetValue(item, out PerceptionCellKey currentCellKey))
        {
            if (currentCellKey.Equals(newCellKey))
                return;

            RemoveItemFromPerceptionCell(item, currentCellKey);
        }

        if (!_perceptionCells.TryGetValue(newCellKey, out PerceptionCell targetCell))
        {
            targetCell = _perceptionCellPool.Count > 0
                ? _perceptionCellPool.Pop()
                : new PerceptionCell();
            targetCell.Reset();
            _perceptionCells[newCellKey] = targetCell;
        }

        targetCell.Items.Add(item);
        _itemPerceptionCells[item] = newCellKey;
    }

    /// <summary>从已登记的感知格中注销实体。</summary>
    private void RemoveItemFromSpatialIndex(Item item)
    {
        if (ReferenceEquals(item, null) || !_itemPerceptionCells.TryGetValue(item, out PerceptionCellKey cellKey))
            return;

        RemoveItemFromPerceptionCell(item, cellKey);
    }

    /// <summary>移除格内成员并回收空格集合。</summary>
    private void RemoveItemFromPerceptionCell(Item item, PerceptionCellKey cellKey)
    {
        _itemPerceptionCells.Remove(item);
        if (!_perceptionCells.TryGetValue(cellKey, out PerceptionCell cell))
            return;

        cell.Items.Remove(item);
        if (cell.Items.Count > 0)
            return;

        _perceptionCells.Remove(cellKey);
        cell.Reset();
        _perceptionCellPool.Push(cell);
    }

    /// <summary>在维护边界同时重建空间与感知后端，清除已销毁对象的 Bridge/几何引用。</summary>
    private void RebuildSpatialIndex()
    {
        CompletePerceptionBatch(false);
        _perceptionTargets.Clear();
        foreach (PerceptionCell cell in _perceptionCells.Values)
        {
            cell.Reset();
            _perceptionCellPool.Push(cell);
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
    private static PerceptionCellKey GetPerceptionCellKey(int worldId, Vector2 position)
    {
        return new PerceptionCellKey(worldId, PackPerceptionCell(
            WorldToPerceptionCell(position.x),
            WorldToPerceptionCell(position.y)));
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
            if (candidate == null || candidate.itemData == null || GameplayWorldRegistry.GetItemWorldIdentity(candidate) == 0 ||
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
