using System;
using System.Collections;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.Pool;
using Stopwatch = System.Diagnostics.Stopwatch;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

/// <summary>按数据区块持有生态运行态，独立推进生成并在数据逐出或世界退出时保存释放。</summary>
internal sealed class WorldNaturalItemRuntimeManager : IDisposable
{
    #region 世界账本与初始化队列
    private sealed class Entry
    {
        internal ChunkRuntime Chunk;
        internal ChunkNaturalItemRuntime Runtime;
        internal IEnumerator Initialization;
        internal Exception Failure;
    }

    private readonly ChunkMgr owner;
    private readonly WorldRuntime world;
    private readonly Dictionary<RuntimeWorldAddress, Entry> entries = new();
    private readonly Queue<Entry> pending = new();
    private readonly IDisposable evictionSubscription;
    private bool disposed, quitting;

    internal int Count => entries.Count;
    internal int PendingCount => pending.Count;

    internal WorldNaturalItemRuntimeManager(ChunkMgr owner, WorldRuntime world)
    {
        this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        evictionSubscription = world.Events.Subscribe<ChunkEvicting>(change => Release(change.Address));
        Application.quitting += HandleQuitting;
    }

    private void HandleQuitting() => quitting = true;

    private GameplayWorldRegistry.WorldExecutionScope EnterWorld() => GameplayWorldRegistry.TryGet(world.WorldId, out _)
        ? GameplayWorldRegistry.UseWorld(world.WorldId) : default;

    internal ChunkNaturalItemRuntime Ensure(ChunkRuntime chunk)
    {
        if (disposed) throw new ObjectDisposedException(nameof(WorldNaturalItemRuntimeManager));
        if (chunk == null || chunk.DataStatus != ChunkDataStatus.Ready || chunk.Terrain == null)
            throw new InvalidOperationException("生态运行态必须依附已就绪的数据区块。");
        if (entries.TryGetValue(chunk.Address, out Entry existing))
        {
            if (ReferenceEquals(existing.Chunk, chunk)) return existing.Runtime;
            Release(chunk.Address);
        }
        var runtime = new ChunkNaturalItemRuntime(owner);
        var entry = new Entry { Chunk = chunk, Runtime = runtime, Initialization = runtime.BindIncremental(chunk) };
        entries.Add(chunk.Address, entry);
        pending.Enqueue(entry);
        return runtime;
    }

    internal bool TryGet(RuntimeWorldAddress address, out ChunkNaturalItemRuntime runtime)
    {
        runtime = entries.TryGetValue(address, out Entry entry) ? entry.Runtime : null;
        return runtime != null && !runtime.IsDisposed && runtime.Chunk != null;
    }

    internal bool IsInitialized(ChunkRuntime chunk)
    {
        if (!entries.TryGetValue(chunk.Address, out Entry entry) || !ReferenceEquals(entry.Chunk, chunk)) return false;
        if (entry.Failure != null) throw new InvalidOperationException($"区块生态初始化失败：{chunk.Address}", entry.Failure);
        return entry.Runtime.IsInitialized;
    }

    internal void CompleteInitialization(ChunkRuntime chunk)
    {
        Ensure(chunk);
        Entry entry = entries[chunk.Address];
        while (!entry.Runtime.IsInitialized && entry.Failure == null) Advance(entry);
        IsInitialized(chunk);
    }

    // 每块每帧最多推进一步，View 停用或取消绑定不会取消逻辑初始化。
    internal void AdvanceFrame(int stepBudget, float milliseconds)
    {
        if (disposed) return;
        using var scope = EnterWorld();
        long deadline = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * Math.Max(.1f, milliseconds) / 1000d);
        int count = Math.Min(pending.Count, Math.Max(1, stepBudget));
        for (int i = 0; i < count; i++)
        {
            Entry entry = pending.Dequeue();
            if (!IsCurrent(entry) || entry.Initialization == null) continue;
            Advance(entry);
            if (IsCurrent(entry) && entry.Initialization != null) pending.Enqueue(entry);
            if (Stopwatch.GetTimestamp() >= deadline) break;
        }
        using var scratch = ListPool<Entry>.Get(out var active);
        active.AddRange(entries.Values);
        foreach (Entry entry in active)
            if (IsCurrent(entry) && entry.Runtime.IsInitialized) entry.Runtime.Tick();
    }

    private bool IsCurrent(Entry entry) => !disposed && entries.TryGetValue(entry.Chunk.Address, out Entry current) &&
        ReferenceEquals(current, entry) && !entry.Runtime.IsDisposed;

    private void Advance(Entry entry)
    {
        using var scope = EnterWorld();
        try
        {
            using (world.StreamingDiagnostics.Measure("entities.natural.initialize"))
                if (entry.Initialization.MoveNext()) return;
            (entry.Initialization as IDisposable)?.Dispose();
            entry.Initialization = null;
            entry.Runtime.IsInitialized = true;
        }
        catch (Exception error)
        {
            entry.Failure = error;
            try { (entry.Initialization as IDisposable)?.Dispose(); }
            finally { entry.Initialization = null; }
            Debug.LogError($"[WorldNaturalItemRuntimeManager] {entry.Chunk.Address}：{error}", owner);
        }
    }
    #endregion

    #region 快照与释放
    internal void CaptureAll()
    {
        using var scope = EnterWorld();
        using var scratch = ListPool<Entry>.Get(out var active);
        active.AddRange(entries.Values);
        foreach (Entry entry in active) if (IsCurrent(entry)) entry.Runtime.CaptureState();
    }

    internal IEnumerator CaptureAllCoroutine(AutoSaveWorkBudget budget)
    {
        using var scratch = ListPool<Entry>.Get(out var active);
        active.AddRange(entries.Values);
        foreach (Entry entry in active)
        {
            if (!IsCurrent(entry)) continue;
            IEnumerator capture = entry.Runtime.CaptureStateCoroutine(budget);
            try
            {
                while (IsCurrent(entry))
                {
                    bool hasNext;
                    using (EnterWorld()) hasNext = capture.MoveNext();
                    if (!hasNext) break;
                    yield return capture.Current;
                }
            }
            finally { (capture as IDisposable)?.Dispose(); }
            if (budget.ShouldYield()) yield return budget.WaitForNextSlice();
        }
    }

    private void Release(RuntimeWorldAddress address)
    {
        if (!entries.Remove(address, out Entry entry)) return;
        using var scope = EnterWorld();
        try { (entry.Initialization as IDisposable)?.Dispose(); }
        finally { entry.Runtime.Release(capture: !quitting); }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        evictionSubscription.Dispose();
        Application.quitting -= HandleQuitting;
        pending.Clear();
        using var scratch = ListPool<RuntimeWorldAddress>.Get(out var addresses);
        addresses.AddRange(entries.Keys);
        using var errorsScope = ListPool<Exception>.Get(out var errors);
        foreach (RuntimeWorldAddress address in addresses)
        {
            try { Release(address); }
            catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count > 0) throw new AggregateException("部分区块生态释放失败，其余区块已继续清理。", errors);
    }
    #endregion
}
