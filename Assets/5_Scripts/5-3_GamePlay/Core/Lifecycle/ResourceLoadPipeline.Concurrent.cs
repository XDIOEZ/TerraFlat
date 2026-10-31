using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Unity.Profiling;
using UnityEngine;

internal sealed partial class ResourceLoadPipeline
{
    #region 依赖就绪阶段协作调度

    private sealed class ConcurrentExecution
    {
        internal Step Step;
        internal ConcurrentExecution[] Dependencies;
        internal readonly Stack<IEnumerator> Routines = new();
        internal readonly Stopwatch Timer = new();
        internal ProfilerMarker Marker;
        internal AsyncOperation Waiting;
        internal int YieldedFrame = -1;
        internal bool Started, Finished;
        internal float Progress;
        internal long WorkTicks;
        internal double LongestMoveMilliseconds;
    }

    private ConcurrentExecution[] concurrentExecutions;
    private ConcurrentExecution concurrentExecution;
    private readonly List<ConcurrentExecution> startedConcurrentExecutions = new();

    /// <summary>主线程按依赖交错推进资源阶段；等待某个请求时继续其它阶段，所有工作共用同帧预算。</summary>
    private IEnumerator RunConcurrent()
    {
        try
        {
            List<Step> ordered = OrderSteps();
            totalWeight = ordered.Sum(step => step.Weight);
            concurrentExecutions = ordered.Select(step => new ConcurrentExecution { Step = step }).ToArray();
            var byId = concurrentExecutions.ToDictionary(entry => entry.Step.Id, StringComparer.Ordinal);
            foreach (ConcurrentExecution entry in concurrentExecutions)
                entry.Dependencies = entry.Step.Dependencies.Select(id => byId[id]).ToArray();
            // 依赖图通过校验后先发出独立请求，回调交接仍遵守主线程预算和消费阶段的错误边界。
            foreach (IPrefetchedResourceData input in prefetched) input.StartRequest();
        }
        catch (Exception exception) { Failure = exception; Dispose(); yield break; }

        int remaining = concurrentExecutions.Length, next = 0;
        try
        {
            while (!disposed && remaining > 0)
            {
                bool advanced = false;
                int frame = frameNumber();
                for (int examined = 0; examined < concurrentExecutions.Length && !ShouldYield(); examined++)
                {
                    ConcurrentExecution entry = concurrentExecutions[next];
                    next = (next + 1) % concurrentExecutions.Length;
                    if (entry.Finished || entry.Dependencies.Any(dependency => !dependency.Finished)) continue;
                    concurrentExecution = entry;
                    currentStep = entry.Step;
                    stepProgress = entry.Progress;
                    try
                    {
                        if (entry.Started && entry.Timer.Elapsed.TotalSeconds > StageTimeoutSeconds)
                            throw new TimeoutException($"资源阶段 {entry.Step.Id} 超过 {StageTimeoutSeconds} 秒仍未完成。");
                        if (entry.YieldedFrame == frame || entry.Waiting != null && !entry.Waiting.isDone) continue;
                        entry.Waiting = null;
                        if (!entry.Started)
                        {
                            entry.Started = true;
                            entry.Timer.Start();
                            entry.Marker = new ProfilerMarker("FlatWorld.Resources." + entry.Step.Id);
                            startedConcurrentExecutions.Add(entry);
                            Report(0);
                            entry.Routines.Push(entry.Step.Load() ??
                                throw new InvalidOperationException($"阶段 {entry.Step.Id} 没有返回加载流程。"));
                        }
                        AdvanceConcurrentStep(entry, frame);
                        advanced = true;
                        if (entry.Routines.Count == 0)
                        {
                            Report(1);
                            entry.Finished = true;
                            remaining--;
                            UnityEngine.Debug.Log($"[GameRes] 并行阶段完成：{entry.Step.Id}，{entry.Timer.ElapsedMilliseconds} ms，" +
                                $"阶段工作 {entry.WorkTicks * 1000.0 / Stopwatch.Frequency:F2} ms，" +
                                $"主线程最慢单步 {entry.LongestMoveMilliseconds:F2} ms（阶段墙钟可能重叠）");
                        }
                    }
                    catch (Exception exception) { Failure = exception; yield break; }
                    AdvanceAndMeasurePrefetchedResourceData();
                    if (disposed) yield break;
                }
                if (remaining == 0) yield break;
                if (!advanced || ShouldYield())
                {
                    AdvanceAndMeasurePrefetchedResourceData();
                    yield return null;
                }
            }
        }
        finally { Dispose(); }
    }

    private void AdvanceConcurrentStep(ConcurrentExecution entry, int frame)
    {
        while (entry.Routines.Count > 0 && !ShouldYield())
        {
            IEnumerator routine = entry.Routines.Peek();
            long started = Stopwatch.GetTimestamp();
            bool moved;
            using (entry.Marker.Auto()) moved = routine.MoveNext();
            long ticks = Stopwatch.GetTimestamp() - started;
            entry.WorkTicks += ticks;
            entry.LongestMoveMilliseconds = Math.Max(entry.LongestMoveMilliseconds, ticks * 1000d / Stopwatch.Frequency);
            if (!moved)
            {
                entry.Routines.Pop();
                (routine as IDisposable)?.Dispose();
                continue;
            }
            object yielded = routine.Current;
            if (yielded is IEnumerator nested) { entry.Routines.Push(nested); continue; }
            if (yielded != null && yielded is not AsyncOperation)
                throw new InvalidOperationException($"并行资源阶段 {entry.Step.Id} 不支持等待类型 {yielded.GetType().Name}；请使用异步操作或嵌套 IEnumerator。");
            entry.Waiting = yielded as AsyncOperation;
            entry.YieldedFrame = frame;
            return;
        }
    }

    private float GetConcurrentProgress()
    {
        float weight = 0;
        foreach (ConcurrentExecution entry in concurrentExecutions) weight += entry.Step.Weight * entry.Progress;
        return totalWeight > 0 ? weight / totalWeight : 0;
    }

    private void DisposeConcurrentSteps()
    {
        for (int i = startedConcurrentExecutions.Count - 1; i >= 0; i--)
        {
            ConcurrentExecution entry = startedConcurrentExecutions[i];
            while (entry.Routines.Count > 0)
            {
                try { (entry.Routines.Pop() as IDisposable)?.Dispose(); }
                catch (Exception exception) { Failure ??= exception; }
            }
            entry.Waiting = null;
        }
        startedConcurrentExecutions.Clear();
        concurrentExecution = null;
        concurrentExecutions = null;
    }

    #endregion
}
