using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public partial class ItemMgr
{
    private readonly List<Collider2D> _spatialColliderBuffer = new(4);
    private readonly Dictionary<Item, Collider2D[]> _perceptionColliderCache = new();

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
    private readonly HashSet<long> _perceptionSnapshotCells = new();
    private readonly HashSet<Item> _perceptionSnapshotItemSet = new();
    private readonly HashSet<Item> _perceptionResultItemSet = new();
    private readonly List<Item> _detectorApplyBuffer = new(64);

    private NativeList<PerceptionItemSnapshot> _inFlightItemSnapshots;
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
        public float2 BoundsCenter;
        public float2 BoundsExtents;
    }

    private struct DetectorQuerySnapshot
    {
        public float2 Center;
        public float Radius;
        public int LayerMask;
        public int ExcludedInstanceId;
        public WorldTopologyDomain Topology;
    }

    [BurstCompile]
    private struct BatchedPerceptionQueryJob : IJobParallelFor
    {
        [Unity.Collections.ReadOnly] public NativeArray<PerceptionItemSnapshot> Items;
        [Unity.Collections.ReadOnly] public NativeArray<DetectorQuerySnapshot> Queries;
        [Unity.Collections.ReadOnly] public NativeParallelMultiHashMap<long, int> SpatialMap;
        public NativeStream.Writer Results;

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
                    int minCellX = WorldToCell(imageCenter.x - query.Radius) - 1;
                    int maxCellX = WorldToCell(imageCenter.x + query.Radius) + 1;
                    int minCellY = WorldToCell(imageCenter.y - query.Radius) - 1;
                    int maxCellY = WorldToCell(imageCenter.y + query.Radius) + 1;
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
                                if (math.lengthsq(distanceToBounds) > query.Radius * query.Radius)
                                    continue;

                                writer.Write(itemIndex);
                            }
                            while (SpatialMap.TryGetNextValue(out itemIndex, ref iterator));
                        }
                    }
                }
            }

            writer.EndForEachIndex();
        }

        private static int WorldToCell(float coordinate)
        {
            return (int)math.floor(coordinate / PerceptionCellSize);
        }

        private static long PackCell(int x, int y)
        {
            return ((long)x << 32) ^ (uint)y;
        }
    }

    #endregion

    #region 空间查询

    public void NotifyItemSpatialIndexChanged(Item item)
    {
        if (item?.itemData == null)
            return;

        if (WorldRunTimeItems.TryGetValue(item.itemData.Guid, out Item registeredItem) && registeredItem == item)
            RefreshItemSpatialIndex(item);
    }

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

        var queryJob = new BatchedPerceptionQueryJob
        {
            Items = _inFlightItemSnapshots.AsArray(),
            Queries = _inFlightQueries.AsArray(),
            SpatialMap = _inFlightSpatialMap,
            Results = _inFlightResults.AsWriter()
        };

        _perceptionJobHandle = queryJob.Schedule(queryCount, PerceptionJobBatchSize);
        _perceptionJobScheduled = true;
    }

    private void PreparePerceptionJobContainers(int itemCount, int queryCount)
    {
        int requiredItemCapacity = Mathf.Max(1, itemCount);
        int requiredQueryCapacity = Mathf.Max(1, queryCount);

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

    private void BuildDetectorQuerySnapshot()
    {
        _perceptionQueryData.Clear();
        WorldTopologyDomain topology = WorldTopologyRuntime.GetActiveDomain();
        for (int i = 0; i < _inFlightDetectors.Count; i++)
        {
            Mod_ItemDetector detector = _inFlightDetectors[i];
            Item excludedItem = detector.item;
            Vector3 detectorPosition = detector.transform.position;
            _perceptionQueryData.Add(new DetectorQuerySnapshot
            {
                Center = new float2(detectorPosition.x, detectorPosition.y),
                Radius = Mathf.Max(0f, detector.DetectionRadius),
                LayerMask = detector.itemLayer.value,
                ExcludedInstanceId = excludedItem != null ? excludedItem.GetInstanceID() : 0,
                Topology = topology
            });
        }
    }

    private void BuildPerceptionItemSnapshot()
    {
        _perceptionSnapshotItems.Clear();
        _perceptionSnapshotData.Clear();
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
                    int minCellX = WorldToPerceptionCell(centerX - query.Radius) - 1;
                    int maxCellX = WorldToPerceptionCell(centerX + query.Radius) + 1;
                    int minCellY = WorldToPerceptionCell(centerY - query.Radius) - 1;
                    int maxCellY = WorldToPerceptionCell(centerY + query.Radius) + 1;
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

    private void TryAddPerceptionSnapshot(Item candidate)
    {
        if (candidate == null || candidate.itemData == null ||
            !candidate.gameObject.activeInHierarchy || candidate.DestructionHandled ||
            !_perceptionSnapshotItemSet.Add(candidate) ||
            !TryGetPerceptionBounds(candidate, out Bounds perceptionBounds))
        {
            return;
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
            BoundsCenter = new float2(boundsCenter.x, boundsCenter.y),
            BoundsExtents = new float2(boundsExtents.x, boundsExtents.y)
        });
    }

    private void RefreshPerceptionColliderCache(Item item)
    {
        if (item == null)
            return;

        _perceptionColliderCache[item] = item.GetComponents<Collider2D>();
    }

    private bool TryGetPerceptionBounds(Item item, out Bounds combinedBounds)
    {
        combinedBounds = default;
        if (!_perceptionColliderCache.TryGetValue(item, out Collider2D[] colliders))
        {
            RefreshPerceptionColliderCache(item);
            _perceptionColliderCache.TryGetValue(item, out colliders);
        }

        bool hasEnabledCollider = false;
        if (colliders == null)
            return false;

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider2D collider = colliders[i];
            if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy)
                continue;

            if (!hasEnabledCollider)
            {
                combinedBounds = collider.bounds;
                hasEnabledCollider = true;
            }
            else
            {
                combinedBounds.Encapsulate(collider.bounds);
            }
        }

        return hasEnabledCollider;
    }

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
        _perceptionSnapshotCells.Clear();
        _perceptionSnapshotItemSet.Clear();
    }

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

                if (!_perceptionResultItemSet.Add(candidate) || !PassesColliderPerceptionFilter(candidate, query))
                    continue;

                _detectorApplyBuffer.Add(candidate);
            }

            reader.EndForEachIndex();
            if (detector != null)
                detector.ApplyDetectorResults(_inFlightDetectorVersions[queryIndex], _detectorApplyBuffer);
        }
    }

    private bool IsSnapshotItemStillValid(Item candidate, PerceptionItemSnapshot snapshot)
    {
        if (candidate == null || candidate.itemData == null || candidate.DestructionHandled ||
            !candidate.gameObject.activeInHierarchy || candidate.itemData.Guid != snapshot.Guid ||
            candidate.GetInstanceID() != snapshot.InstanceId)
        {
            return false;
        }

        return WorldRunTimeItems.TryGetValue(snapshot.Guid, out Item registeredItem) && registeredItem == candidate;
    }

    private bool PassesColliderPerceptionFilter(Item candidate, DetectorQuerySnapshot query)
    {
        int layerBit = 1 << candidate.gameObject.layer;
        if ((query.LayerMask & layerBit) == 0 || candidate.GetInstanceID() == query.ExcludedInstanceId)
            return false;

        Vector2 center = new Vector2(query.Center.x, query.Center.y);
        float radiusSqr = query.Radius * query.Radius;
        if (!_perceptionColliderCache.TryGetValue(candidate, out Collider2D[] colliders))
            return false;

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider2D collider = colliders[i];
            if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy)
                continue;

            int minImage = query.Topology.IsWrapped ? -1 : 0;
            int maxImage = query.Topology.IsWrapped ? 1 : 0;
            for (int imageX = minImage; imageX <= maxImage; imageX++)
            {
                for (int imageY = minImage; imageY <= maxImage; imageY++)
                {
                    Vector2 imageCenter = center + new Vector2(
                        imageX * query.Topology.Span.x,
                        imageY * query.Topology.Span.y);
                    Vector2 closestPoint = collider.ClosestPoint(imageCenter);
                    if ((closestPoint - imageCenter).sqrMagnitude <= radiusSqr)
                        return true;
                }
            }
        }

        return false;
    }

    private void DisposePerceptionJobData()
    {
        DisposePerceptionResultStream();
        if (_inFlightItemSnapshots.IsCreated)
            _inFlightItemSnapshots.Dispose();
        if (_inFlightQueries.IsCreated)
            _inFlightQueries.Dispose();
        if (_inFlightSpatialMap.IsCreated)
            _inFlightSpatialMap.Dispose();
    }

    private void DisposePerceptionResultStream()
    {
        if (_inFlightResults.IsCreated)
            _inFlightResults.Dispose();
    }

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

        WorldTopologyDomain topology = WorldTopologyRuntime.GetActiveDomain();
        int minImage = topology.IsWrapped ? -1 : 0;
        int maxImage = topology.IsWrapped ? 1 : 0;
        for (int imageX = minImage; imageX <= maxImage; imageX++)
        {
            for (int imageY = minImage; imageY <= maxImage; imageY++)
            {
                Vector2 imageCenter = center + new Vector2(imageX * topology.Span.x, imageY * topology.Span.y);
                int minCellX = WorldToPerceptionCell(imageCenter.x - radius) - 1;
                int maxCellX = WorldToPerceptionCell(imageCenter.x + radius) + 1;
                int minCellY = WorldToPerceptionCell(imageCenter.y - radius) - 1;
                int maxCellY = WorldToPerceptionCell(imageCenter.y + radius) + 1;
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

    private void RemoveItemFromSpatialIndex(Item item)
    {
        if (ReferenceEquals(item, null) || !_itemPerceptionCells.TryGetValue(item, out long cellKey))
            return;

        RemoveItemFromPerceptionCell(item, cellKey);
    }

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

    private void RebuildSpatialIndex()
    {
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
                RefreshItemSpatialIndex(item);
        }
    }

    private static int WorldToPerceptionCell(float coordinate)
    {
        return Mathf.FloorToInt(coordinate / PerceptionCellSize);
    }

    private static long GetPerceptionCellKey(Vector2 position)
    {
        return PackPerceptionCell(
            WorldToPerceptionCell(position.x),
            WorldToPerceptionCell(position.y));
    }

    private static long PackPerceptionCell(int x, int y)
    {
        return ((long)x << 32) ^ (uint)y;
    }

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

        float radiusSqr = radius * radius;
        _spatialColliderBuffer.Clear();
        candidate.GetComponents(_spatialColliderBuffer);
        for (int i = 0; i < _spatialColliderBuffer.Count; i++)
        {
            Collider2D collider = _spatialColliderBuffer[i];
            if (collider == null || !collider.enabled)
                continue;

            Vector2 closestPoint = collider.ClosestPoint(center);
            if ((closestPoint - center).sqrMagnitude > radiusSqr)
                continue;

            if (dedupe.Add(candidate))
                results.Add(candidate);
            return;
        }
    }

    #endregion
}
