using System;
using System.Collections.Generic;
using FlatWorld.Navigation;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;

/// <summary>普通长距离请求批量借用共享流场，Job 完成后还原原有 Waypoints 契约。</summary>
public sealed partial class WorldNavigationManager
{
    #region GameObject 批量寻路所有权
    private const int PortalPathDistance = 48;
    private const int MaximumCachedPortalPathGoals = 12;

    [Header("GameObject 批量寻路")]
    [SerializeField, Min(1)] private int maxPortalPathsPerBatch = 64;

    private static readonly ProfilerMarker PortalScheduleMarker = new("FlatWorld.Navigation.Portal.Schedule");
    private static readonly ProfilerMarker PortalCollectMarker = new("FlatWorld.Navigation.Portal.Collect");
    private readonly Queue<PortalPathWork> pendingPortalPaths = new();
    private readonly List<PortalPathWork> portalPathBatch = new(64);
    private readonly HashSet<Vector2Int> portalBatchGoals = new();
    private readonly List<Vector2Int> portalRawCells = new(64);
    private readonly List<Vector2> portalWaypoints = new(64);
    private NativeArray<FlowPathRequest> portalPathInputs;
    private NativeArray<FlowPathResult> portalPathResults;
    private NativeArray<int2> portalPathCells;
    private JobHandle portalPathJob;
    private bool portalBatchActive, portalJobScheduled;
    private int portalResultIndex, portalPathCellLimit;

    public int PendingPortalPathCount => pendingPortalPaths.Count + portalPathBatch.Count - portalResultIndex;
    public long PortalPathJobBatchCount { get; private set; }
    public long PortalPathJobRequestCount { get; private set; }

    /// <summary>冻结请求身份与起终点，防止取消、重排或新世界复用旧结果。</summary>
    private readonly struct PortalPathWork
    {
        internal readonly PathRequest Request;
        internal readonly uint Generation;
        internal readonly int GridRevision;
        internal readonly int PathCostRevision, PathInvalidationRevision;
        internal readonly Vector2Int Start, Goal;
        internal readonly Vector2 Destination;

        internal PortalPathWork(PathRequest request, WorldNavigationGrid grid)
        {
            Request = request;
            Generation = request.PortalGeneration;
            GridRevision = request.PreparedRevision;
            PathCostRevision = grid.PathCostRevision;
            PathInvalidationRevision = grid.PathInvalidationRevision;
            Start = request.StartCell;
            Goal = request.GoalCell;
            Destination = Goal == WorldNavigationGrid.WorldToCell(request.Destination)
                ? WorldTopologyRuntime.NormalizePosition(request.Destination)
                : WorldNavigationGrid.CellCenter(Goal);
        }
    }

    /// <summary>停用时结束借用并释放输出；恢复后让仍有效的请求重新排队。</summary>
    private void OnDisable() => DisposePortalPathJobs(requeue: true);
    #endregion

    #region 批量请求与共享目标
    /// <summary>长路线只排队，每个世界统一调度一批，不为每个 AI 创建 Job。</summary>
    private bool TryQueuePortalPath(PathRequest request)
    {
        Vector2Int delta = WorldTopologyRuntime.ShortestDelta(request.StartCell, request.GoalCell);
        if (request.SkipPortalPath || Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y)) < PortalPathDistance)
            return false;
        if (PendingPortalPathCount >= Mathf.Max(16, maxBufferedCompletions))
        {
            QueueForAdmission(request);
            return true;
        }
        request.PortalPending = true;
        request.PortalGeneration++;
        pendingPortalPaths.Enqueue(new PortalPathWork(request, grid));
        return true;
    }

    /// <summary>按目标格复用句柄；本批已引用的目标不能在收集其它请求时被淘汰。</summary>
    private FlowGoalHandle GetPortalPathGoal(FlowNavigationCache cache,
        Vector2Int goalCell, Vector2 position)
    {
        float2 target = new(position.x, position.y);
        if (portalPathGoals.TryGetValue(goalCell, out FlowGoalHandle cached))
        {
            if (cache.IsValid(cached))
            {
                cache.UpdateGoal(cached, target);
                return cached;
            }
            portalPathGoals.Remove(goalCell);
        }
        int candidates = portalPathGoalOrder.Count;
        while (portalPathGoals.Count >= MaximumCachedPortalPathGoals && candidates-- > 0)
        {
            Vector2Int oldest = portalPathGoalOrder.Dequeue();
            if (portalBatchGoals.Contains(oldest))
            {
                portalPathGoalOrder.Enqueue(oldest);
                continue;
            }
            if (!portalPathGoals.Remove(oldest, out FlowGoalHandle oldHandle)) continue;
            cache.RemoveGoal(oldHandle);
        }
        FlowGoalHandle handle = cache.CreateGoal(target);
        portalPathGoals[goalCell] = handle;
        portalPathGoalOrder.Enqueue(goalCell);
        return handle;
    }

    /// <summary>同批先更新全部目标，只发布一次流场，然后把路径提取交给工作线程。</summary>
    private void SchedulePortalPathBatch()
    {
        if (portalBatchActive || pendingPortalPaths.Count == 0) return;
        int available = Mathf.Max(16, maxBufferedCompletions) - completions.Count;
        int count = Mathf.Min(Mathf.Max(1, maxPortalPathsPerBatch), available);
        if (count <= 0) return;
        using var marker = PortalScheduleMarker.Auto();
        portalBatchGoals.Clear();
        while (portalPathBatch.Count < count && pendingPortalPaths.Count > 0)
        {
            PortalPathWork work = pendingPortalPaths.Peek();
            if (!IsCurrentPortalWork(work))
            {
                pendingPortalPaths.Dequeue();
                continue;
            }
            if (work.GridRevision != grid.Revision)
            {
                pendingPortalPaths.Dequeue();
                RequeuePortalWork(work, skipPortal: false);
                continue;
            }
            if (!portalBatchGoals.Contains(work.Goal) && portalBatchGoals.Count >= MaximumCachedPortalPathGoals)
                break;
            pendingPortalPaths.Dequeue();
            portalBatchGoals.Add(work.Goal);
            portalPathBatch.Add(work);
        }
        if (portalPathBatch.Count == 0) return;

        portalPathCellLimit = Mathf.Max(2, maxPathCellsPerResult);
        EnsurePortalStorage(ref portalPathInputs, portalPathBatch.Count);
        EnsurePortalStorage(ref portalPathResults, portalPathBatch.Count);
        EnsurePortalStorage(ref portalPathCells, checked(portalPathBatch.Count * portalPathCellLimit));
        FlowNavigationCache cache = GetSharedNavigation();
        int written = 0;
        for (int index = 0; index < portalPathBatch.Count; index++)
        {
            PortalPathWork work = portalPathBatch[index];
            FlowGoalHandle handle;
            try
            {
                handle = GetPortalPathGoal(cache, work.Goal, work.Destination);
            }
            catch (InvalidOperationException)
            {
                RequeuePortalWork(work, skipPortal: true);
                continue;
            }
            portalPathInputs[written] = new FlowPathRequest
            {
                Start = new int2(work.Start.x, work.Start.y),
                Destination = new int2(work.Goal.x, work.Goal.y),
                Goal = handle
            };
            portalPathBatch[written++] = work;
        }
        portalPathBatch.RemoveRange(written, portalPathBatch.Count - written);
        if (written == 0) return;

        FlowNavigationSnapshot snapshot = cache.Read();
        portalPathJob = new FlowPathBuildJob
        {
            Navigation = snapshot,
            Requests = portalPathInputs.GetSubArray(0, written),
            Results = portalPathResults.GetSubArray(0, written),
            PathCells = portalPathCells.GetSubArray(0, checked(written * portalPathCellLimit)),
            MaximumCells = portalPathCellLimit
        }.Schedule(written, 1);
        cache.RegisterReader(portalPathJob);
        portalResultIndex = 0;
        portalBatchActive = portalJobScheduled = true;
        PortalPathJobBatchCount++;
        PortalPathJobRequestCount += written;
        JobHandle.ScheduleBatchedJobs();
    }

    /// <summary>请求仍在当前管理器中且代际一致时，才允许处理其异步结果。</summary>
    private bool IsCurrentPortalWork(PortalPathWork work) =>
        work.Request.PortalPending && work.Request.PortalGeneration == work.Generation &&
        requests.TryGetValue(work.Request.Id, out PathRequest current) && ReferenceEquals(current, work.Request);

    /// <summary>地图变化重新准备起终点；流场不可用则只回退到原有目标场一次。</summary>
    private void RequeuePortalWork(PortalPathWork work, bool skipPortal)
    {
        if (!IsCurrentPortalWork(work)) return;
        work.Request.PortalPending = false;
        work.Request.SkipPortalPath = skipPortal;
        QueueForAdmission(work.Request);
    }
    #endregion

    #region 异步结果与路点适配
    /// <summary>只接收已经完成的批次，结果分帧处理并沿用原有回调预算。</summary>
    private void CollectPortalPathResults()
    {
        if (!portalBatchActive) return;
        if (portalJobScheduled)
        {
            if (!portalPathJob.IsCompleted) return;
            portalPathJob.Complete();
            portalJobScheduled = false;
        }
        using var marker = PortalCollectMarker.Auto();
        int processed = 0;
        while (portalResultIndex < portalPathBatch.Count &&
               completions.Count < Mathf.Max(16, maxBufferedCompletions))
        {
            if (processed > 0 && pathStopwatch.Elapsed.TotalMilliseconds >= Mathf.Max(0.1f, maxPathMillisecondsPerFrame))
                break;
            int index = portalResultIndex++;
            processed++;
            PortalPathWork work = portalPathBatch[index];
            if (!IsCurrentPortalWork(work)) continue;
            if (work.PathCostRevision != grid.PathCostRevision)
            {
                RequeuePortalWork(work, skipPortal: false);
                continue;
            }
            FlowPathResult result = portalPathResults[index];
            if (!result.Success)
            {
                RequeuePortalWork(work, skipPortal: true);
                continue;
            }
            if (!TryBuildPortalWaypoints(index, work, result, out Vector2[] waypoints))
            {
                RequeuePortalWork(work, skipPortal: false);
                continue;
            }
            work.Request.PortalPending = false;
            ScheduleSuccess(work.Request, work.Destination, waypoints,
                result.ReachesDestination, result.TotalCost);
        }
        if (portalResultIndex == portalPathBatch.Count)
        {
            portalPathBatch.Clear();
            portalResultIndex = 0;
            portalBatchActive = false;
            portalPathJob = default;
        }
    }

    /// <summary>复用临时列表，最终路点仍独立分配并沿用权重安全的平滑规则。</summary>
    private bool TryBuildPortalWaypoints(int index, PortalPathWork work, FlowPathResult result, out Vector2[] waypoints)
    {
        waypoints = null;
        portalRawCells.Clear();
        portalWaypoints.Clear();
        int start = index * portalPathCellLimit;
        for (int cell = 0; cell < result.CellCount; cell++)
        {
            int2 value = portalPathCells[start + cell];
            portalRawCells.Add(new Vector2Int(value.x, value.y));
        }
        if (work.PathInvalidationRevision != grid.PathInvalidationRevision)
        {
            if (!grid.IsWalkable(portalRawCells[0])) return false;
            for (int cell = 1; cell < portalRawCells.Count; cell++)
                if (!grid.CanTraverse(portalRawCells[cell - 1], portalRawCells[cell], out _)) return false;
        }
        if (work.Start != WorldNavigationGrid.WorldToCell(work.Request.Start))
            portalWaypoints.Add(WorldNavigationGrid.CellCenter(work.Start));
        int anchor = 0;
        while (anchor < portalRawCells.Count - 1)
        {
            int furthest = Mathf.Min(portalRawCells.Count - 1, anchor + 16);
            while (furthest > anchor + 1 && !grid.CanSmoothPathSegment(portalRawCells, anchor, furthest))
                furthest--;
            portalWaypoints.Add(WorldNavigationGrid.CellCenter(portalRawCells[furthest]));
            anchor = furthest;
        }
        Vector2 end = result.ReachesDestination ? work.Destination : WorldNavigationGrid.CellCenter(portalRawCells[^1]);
        if (portalWaypoints.Count == 0 || WorldTopologyRuntime.SqrDistance(portalWaypoints[^1], end) > 0.0001f)
            portalWaypoints.Add(end);
        else
            portalWaypoints[^1] = end;
        waypoints = portalWaypoints.ToArray();
        return true;
    }
    #endregion

    #region 批量工作区与清理
    /// <summary>只有上一批全部消费后才扩容，持久缓冲避免每帧 Native 分配。</summary>
    private static void EnsurePortalStorage<T>(ref NativeArray<T> array, int count) where T : unmanaged
    {
        if (array.IsCreated && array.Length >= count) return;
        if (array.IsCreated) array.Dispose();
        array = new NativeArray<T>(math.ceilpow2(math.max(1, count)), Allocator.Persistent,
            NativeArrayOptions.UninitializedMemory);
    }

    /// <summary>只有停用、切换世界或销毁需要等待任务结束，再释放自有输出。</summary>
    private void DisposePortalPathJobs(bool requeue)
    {
        if (portalJobScheduled) portalPathJob.Complete();
        foreach (PortalPathWork work in pendingPortalPaths)
        {
            if (requeue) RequeuePortalWork(work, skipPortal: false);
            else if (IsCurrentPortalWork(work)) work.Request.PortalPending = false;
        }
        for (int index = portalResultIndex; index < portalPathBatch.Count; index++)
        {
            PortalPathWork work = portalPathBatch[index];
            if (requeue) RequeuePortalWork(work, skipPortal: false);
            else if (IsCurrentPortalWork(work)) work.Request.PortalPending = false;
        }
        pendingPortalPaths.Clear();
        portalPathBatch.Clear();
        portalBatchGoals.Clear();
        portalRawCells.Clear();
        portalWaypoints.Clear();
        if (portalPathInputs.IsCreated) portalPathInputs.Dispose();
        if (portalPathResults.IsCreated) portalPathResults.Dispose();
        if (portalPathCells.IsCreated) portalPathCells.Dispose();
        portalPathJob = default;
        portalResultIndex = 0;
        portalBatchActive = portalJobScheduled = false;
    }
    #endregion
}
