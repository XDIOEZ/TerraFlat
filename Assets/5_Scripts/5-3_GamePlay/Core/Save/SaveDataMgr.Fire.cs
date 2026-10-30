using System;
using System.Collections;
using System.Collections.Generic;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using MemoryPack;
using UnityEngine;
using UnityEngine.Pool;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

/// <summary>火场稀疏差量、运行态观察与恢复。</summary>
public partial class SaveDataMgr
{
    #region 火场差量与观察
    private sealed class RuntimeFireChunkWatch
    {
        public WorldRuntime World;
        public string WorldKey;
        public ChunkRuntime Chunk;
        public ChunkTerrainData Terrain;
        public Action<ChunkTerrainChanged> ChangedHandler;
    }

    private readonly Dictionary<(WorldRuntime World, RuntimeWorldAddress Address), RuntimeFireChunkWatch> runtimeFireChunkWatches = new();
    private readonly Dictionary<WorldRuntime, IDisposable> runtimeFireWorldSubscriptions = new();
    private readonly HashSet<RuntimeFireChunkWatch> runtimeFireDirtyChunks = new();
    private readonly List<RuntimeFireChunkWatch> runtimeFireCaptureBuffer = new();
    private readonly List<RuntimeFireChunkWatch> runtimeFirePruneBuffer = new();
    private bool restoringRuntimeFireStates;

    public static event Action<string, ChunkRuntime, ChunkTerrainChanged> AuthoritativeTerrainFireChanged;

    private void WatchRuntimeFireChunk(ChunkRuntime chunk, WorldRuntime expectedWorld = null)
    {
        ChunkTerrainData terrain = chunk?.Terrain;
        if (terrain == null || terrain.IsDisposed) return;
        WorldRuntime world = RuntimeFireWorldOwnsChunk(expectedWorld, chunk)
            ? expectedWorld : GameplayWorldRegistry.ExecutionChunks?.WorldRuntime;
        if (!RuntimeFireWorldOwnsChunk(world, chunk) &&
            GameplayWorldRegistry.TryGet(chunk.Address.DimensionId, out GameplayWorldRuntime context))
            world = context.Chunks?.WorldRuntime;
        if (!RuntimeFireWorldOwnsChunk(world, chunk)) return;

        EnsureRuntimeFireWorldSubscription(world);
        var key = (world, chunk.Address);
        if (runtimeFireChunkWatches.TryGetValue(key, out RuntimeFireChunkWatch existing))
        {
            if (ReferenceEquals(existing.Chunk, chunk) && ReferenceEquals(existing.Terrain, terrain)) return;
            StopWatchingRuntimeFireChunk(existing);
        }

        var watch = new RuntimeFireChunkWatch
        {
            World = world,
            WorldKey = world.WorldId,
            Chunk = chunk,
            Terrain = terrain
        };
        watch.ChangedHandler = changed => HandleRuntimeFireTerrainChanged(watch, changed);
        terrain.Changed += watch.ChangedHandler;
        runtimeFireChunkWatches[key] = watch;
    }

    private static bool RuntimeFireWorldOwnsChunk(WorldRuntime world, ChunkRuntime chunk)
        => world != null && chunk != null && world.Chunks.TryGetValue(chunk.Address, out ChunkRuntime registered) &&
           ReferenceEquals(registered, chunk);

    private void EnsureRuntimeFireWorldSubscription(WorldRuntime world)
    {
        if (runtimeFireWorldSubscriptions.ContainsKey(world)) return;
        runtimeFireWorldSubscriptions.Add(world, world.Events.Subscribe<ChunkDataStatusChanged>(
            changed => HandleRuntimeFireChunkStatusChanged(world, changed)));
    }

    private void HandleRuntimeFireTerrainChanged(RuntimeFireChunkWatch watch, ChunkTerrainChanged changed)
    {
        if (restoringRuntimeFireStates || changed.Kind != TerrainChangeKind.Fire || !GameNetwork.HasStateAuthority)
            return;
        runtimeFireDirtyChunks.Add(watch);
        AuthoritativeTerrainFireChanged?.Invoke(watch.WorldKey, watch.Chunk, changed);
    }

    private void HandleRuntimeFireChunkStatusChanged(WorldRuntime world, ChunkDataStatusChanged changed)
    {
        if (!runtimeFireChunkWatches.TryGetValue((world, changed.Address), out RuntimeFireChunkWatch watch))
            return;

        if (changed.Current == ChunkDataStatus.Requested)
        {
            if (GameNetwork.HasStateAuthority && runtimeFireDirtyChunks.Contains(watch) && !CaptureRuntimeFireChunk(watch))
                throw new InvalidOperationException("火场状态保存失败，不能替换区块地形。");
            return;
        }

        if (changed.Current == ChunkDataStatus.Ready &&
            world.Chunks.TryGetValue(changed.Address, out ChunkRuntime readyChunk) &&
            ReferenceEquals(readyChunk, watch.Chunk) && !ReferenceEquals(watch.Terrain, readyChunk.Terrain))
        {
            // 同一 ChunkRuntime 可在重新生成时更换 Terrain；先冻结旧格，再接管新地形。
            WorldFireCellState[] currentStates = watch.Terrain.CopyFireStates();
            bool retryPersistence = GameNetwork.HasStateAuthority && runtimeFireDirtyChunks.Contains(watch) &&
                !CaptureRuntimeFireChunk(watch);
            StopWatchingRuntimeFireChunk(watch);
            WatchRuntimeFireChunk(readyChunk, world);
            RestoreRuntimeFireStates(readyChunk, currentStates);
            if (retryPersistence && runtimeFireChunkWatches.TryGetValue((world, readyChunk.Address), out RuntimeFireChunkWatch replacement))
                runtimeFireDirtyChunks.Add(replacement);
            return;
        }

        if (changed.Current != ChunkDataStatus.Evicting) return;
        if (GameNetwork.HasStateAuthority && runtimeFireDirtyChunks.Contains(watch) && !CaptureRuntimeFireChunk(watch))
            throw new InvalidOperationException("火场状态保存失败，不能卸载区块。");
        StopWatchingRuntimeFireChunk(watch);
    }

    private void StopWatchingRuntimeFireChunk(RuntimeFireChunkWatch watch)
    {
        if (watch == null) return;
        if (watch.Terrain != null && watch.ChangedHandler != null)
            watch.Terrain.Changed -= watch.ChangedHandler;
        runtimeFireChunkWatches.Remove((watch.World, watch.Chunk.Address));
        runtimeFireDirtyChunks.Remove(watch);
    }

    private void CaptureRuntimeFireWorldsForSceneUnbinding(string worldKey)
    {
        if (!CaptureRuntimeFireStates())
            throw new InvalidOperationException("火场快照采集失败，不能解绑世界：" + worldKey);

        runtimeFirePruneBuffer.Clear();
        foreach (RuntimeFireChunkWatch watch in runtimeFireChunkWatches.Values)
            if (string.Equals(watch.WorldKey, worldKey, StringComparison.Ordinal))
                runtimeFirePruneBuffer.Add(watch);
        foreach (RuntimeFireChunkWatch watch in runtimeFirePruneBuffer)
            StopWatchingRuntimeFireChunk(watch);
        runtimeFirePruneBuffer.Clear();

        foreach (var pair in runtimeFireWorldSubscriptions)
            if (string.Equals(pair.Key.WorldId, worldKey, StringComparison.Ordinal))
                pair.Value.Dispose();
        using var scratch = ListPool<WorldRuntime>.Get(out var worldsToRemove);
        foreach (WorldRuntime world in runtimeFireWorldSubscriptions.Keys)
            if (string.Equals(world.WorldId, worldKey, StringComparison.Ordinal))
                worldsToRemove.Add(world);
        foreach (WorldRuntime world in worldsToRemove)
            runtimeFireWorldSubscriptions.Remove(world);
    }

    private void ClearRuntimeFireTracking()
    {
        foreach (RuntimeFireChunkWatch watch in runtimeFireChunkWatches.Values)
            if (watch.Terrain != null && watch.ChangedHandler != null)
                watch.Terrain.Changed -= watch.ChangedHandler;
        runtimeFireChunkWatches.Clear();
        runtimeFireDirtyChunks.Clear();
        foreach (IDisposable subscription in runtimeFireWorldSubscriptions.Values)
            subscription?.Dispose();
        runtimeFireWorldSubscriptions.Clear();
        runtimeFireCaptureBuffer.Clear();
        runtimeFirePruneBuffer.Clear();
    }
    #endregion

    #region 火场存档采集与恢复
    private bool CaptureRuntimeFireStates()
    {
        if (!GameNetwork.HasStateAuthority || runtimeFireDirtyChunks.Count == 0) return true;
        if (SaveData == null) return false;

        runtimeFireCaptureBuffer.Clear();
        runtimeFireCaptureBuffer.AddRange(runtimeFireDirtyChunks);
        bool succeeded = true;
        foreach (RuntimeFireChunkWatch watch in runtimeFireCaptureBuffer)
            if (runtimeFireDirtyChunks.Contains(watch) && !CaptureRuntimeFireChunk(watch))
                succeeded = false;
        runtimeFireCaptureBuffer.Clear();
        return succeeded;
    }

    private bool CaptureRuntimeFireChunk(RuntimeFireChunkWatch watch)
    {
        if (watch?.Terrain == null || watch.Terrain.IsDisposed || SaveData == null)
            return false;
        try
        {
            StoreRuntimeFireSnapshot(watch);
            runtimeFireDirtyChunks.Remove(watch);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError($"[SaveDataMgr] 采集火场失败：{watch.WorldKey}/{watch.Chunk.Address}，{exception}");
            runtimeFireDirtyChunks.Add(watch);
            return false;
        }
    }

    private void StoreRuntimeFireSnapshot(RuntimeFireChunkWatch watch)
    {
        WorldFireCellState[] states = watch.Terrain.CopyFireStates();
        Array.Sort(states, (left, right) => left.CellIndex.CompareTo(right.CellIndex));
        string chunkName = ToChunkName(watch.Chunk.Address);
        string key = BuildChunkKey(watch.WorldKey, chunkName);
        chunkDeltas.TryGetValue(key, out ChunkSaveRecord record);
        if (HasSameRuntimeFireStates(record?.FireCells, states)) return;

        if (record == null)
        {
            if (states.Length == 0) return;
            record = CreateRuntimeChunkDelta(watch.WorldKey, watch.Chunk.Address);
        }

        record.FireCells = new List<FireCellSaveData>(states.Length);
        foreach (WorldFireCellState state in states)
        {
            if (!FireCellSaveData.IsValid(state.CellIndex, state.Intensity, state.SurfaceFuelBurnSeconds,
                    watch.Terrain.CellCount))
                throw new InvalidOperationException("火场状态越过当前区块或数值无效。");
            record.FireCells.Add(FireCellSaveData.Capture(state));
        }

        if (record.HasChanges) chunkDeltas[key] = record;
        else chunkDeltas.Remove(key);
        MarkChunkDifferenceChanged(key);
    }

    private static bool HasSameRuntimeFireStates(List<FireCellSaveData> saved, WorldFireCellState[] current)
    {
        if ((saved?.Count ?? 0) != current.Length) return false;
        for (int i = 0; i < current.Length; i++)
        {
            FireCellSaveData cell = saved[i];
            WorldFireCellState state = current[i];
            if (cell == null || cell.CellIndex != state.CellIndex || cell.Intensity != state.Intensity ||
                cell.SurfaceFuelBurnSeconds != state.SurfaceFuelBurnSeconds)
                return false;
        }
        return true;
    }

    private void RestoreRuntimeFireStates(ChunkRuntime chunk, ChunkSaveRecord record)
    {
        ChunkTerrainData terrain = chunk?.Terrain;
        if (terrain == null || terrain.IsDisposed) return;
        WatchRuntimeFireChunk(chunk);
        List<FireCellSaveData> saved = record?.FireCells;
        var states = saved == null || saved.Count == 0
            ? Array.Empty<WorldFireCellState>()
            : new WorldFireCellState[saved.Count];
        for (int i = 0; i < states.Length; i++)
        {
            FireCellSaveData cell = saved[i];
            if (cell == null || !FireCellSaveData.IsValid(cell.CellIndex, cell.Intensity, cell.SurfaceFuelBurnSeconds,
                    terrain.CellCount))
                throw new InvalidOperationException("存档中的火场状态无效。");
            states[i] = cell.ToWorldFireCellState();
        }

        restoringRuntimeFireStates = true;
        try { terrain.RestoreFireStates(states); }
        finally { restoringRuntimeFireStates = false; }
    }

    private void RestoreRuntimeFireStates(ChunkRuntime chunk, ReadOnlySpan<WorldFireCellState> states)
    {
        ChunkTerrainData terrain = chunk?.Terrain;
        if (terrain == null || terrain.IsDisposed) return;
        WatchRuntimeFireChunk(chunk);
        restoringRuntimeFireStates = true;
        try { terrain.RestoreFireStates(states); }
        finally { restoringRuntimeFireStates = false; }
    }

    private bool CaptureRuntimeFireStatesForSave()
        => CaptureRuntimeFireStates();

    private IEnumerator CaptureRuntimeFireStatesCoroutine(AutoSaveWorkBudget budget)
    {
        if (!GameNetwork.HasStateAuthority || SaveData == null || runtimeFireDirtyChunks.Count == 0)
            yield break;
        var captureBuffer = new List<RuntimeFireChunkWatch>(runtimeFireDirtyChunks);
        for (int i = 0; i < captureBuffer.Count; i++)
        {
            RuntimeFireChunkWatch watch = captureBuffer[i];
            if (runtimeFireDirtyChunks.Contains(watch) && !CaptureRuntimeFireChunk(watch))
                throw new InvalidOperationException("自动保存的火场快照采集失败。");
            if (budget.ShouldYield())
                yield return budget.WaitForNextSlice();
        }
    }
    #endregion
}

[MemoryPackable(GenerateType.VersionTolerant)]
[Serializable]
public partial class FireCellSaveData
{
    #region 火场稀疏存档记录
    [MemoryPackOrder(0)] public int CellIndex;
    [MemoryPackOrder(1)] public float Intensity;
    [MemoryPackOrder(2)] public float SurfaceFuelBurnSeconds;

    public static FireCellSaveData Capture(WorldFireCellState state) => new()
    {
        CellIndex = state.CellIndex,
        Intensity = state.Intensity,
        SurfaceFuelBurnSeconds = state.SurfaceFuelBurnSeconds
    };

    public static bool IsValid(int cellIndex, float intensity, float surfaceFuelBurnSeconds, int cellCount) =>
        (uint)cellIndex < (uint)cellCount && float.IsFinite(intensity) && intensity > 0f && intensity <= 1f &&
        float.IsFinite(surfaceFuelBurnSeconds) && surfaceFuelBurnSeconds >= 0f;

    public WorldFireCellState ToWorldFireCellState() => new()
    {
        CellIndex = CellIndex,
        Intensity = Intensity,
        SurfaceFuelBurnSeconds = SurfaceFuelBurnSeconds
    };
    #endregion
}
