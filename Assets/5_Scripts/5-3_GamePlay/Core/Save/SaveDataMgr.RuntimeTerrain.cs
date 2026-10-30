using System;
using System.Collections;
using System.Collections.Generic;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

/// <summary>运行时地形差量、基线、同步与分帧捕获。</summary>
public partial class SaveDataMgr
{
    #region 地形差量记录与基线
    private readonly Dictionary<string, RuntimeChunkBaseline> runtimeChunkBaselines = new();
    private readonly HashSet<string> runtimeTerrainDirtyChunks = new(StringComparer.Ordinal);

    private ChunkSaveRecord GetRuntimeChunkRecord(RuntimeWorldAddress address, bool create)
    {
        // 世界上下文已缓存名称；热查询复用坐标键，只在首次创建差量时格式化存档键。
        string planet = GameplayWorldRegistry.ExecutionWorldKey;
        if (string.IsNullOrWhiteSpace(planet)) planet = ResolveRuntimePlanetName();
        return GetRuntimeChunkRecord(planet, address, create);
    }

    private ChunkSaveRecord GetRuntimeChunkRecord(string planet, RuntimeWorldAddress address, bool create)
    {
        var origin = new Vector2Int(address.ChunkOrigin.X, address.ChunkOrigin.Y);
        string key = GetCachedChunkDifferenceKey(planet, origin);
        if (create) MarkChunkDifferenceChanged(key);
        if (chunkDeltas.TryGetValue(key, out var record))
            return record;
        if (!create)
            return null;
        record = CreateRuntimeChunkDelta(planet, address);
        chunkDeltas.Add(key, record);
        return record;
    }

    /// <summary>
    /// 新版 WorldModel 区块生成完成后恢复玩家修改的地表与阻挡地块。
    /// 先记录程序化生成的完整核心地形，再覆盖差量，确保平台下的水不会在重载后恢复。
    /// </summary>
    public void RestoreRuntimeTerrainForChunk(RuntimeWorldAddress address, ChunkRuntime chunk)
    {
        if (SaveData == null || chunk == null ||
            chunk.DataStatus != ChunkDataStatus.Ready || chunk.Terrain == null ||
            chunk.Terrain.IsDisposed)
        {
            return;
        }

        WatchRuntimeFireChunk(chunk);
        string planetName = ResolveRuntimePlanetName();
        string chunkName = ToChunkName(address);
        string key = BuildChunkKey(planetName, chunkName);
        if (!TryEnsureRuntimeChunkBaseline(key, chunk, out RuntimeChunkBaseline baseline) ||
            baseline.PersistenceRestored)
            return;

        if (!chunkDeltas.TryGetValue(key, out ChunkSaveRecord delta) || delta == null)
        {
            RestoreRuntimeFireStates(chunk, (ChunkSaveRecord)null);
            baseline.PersistenceRestored = true;
            return;
        }

        ChunkTerrainData terrain = chunk.Terrain;
        for (int i = 0; i < (delta.RuntimeTileDeltas?.Count ?? 0); i++)
        {
            RuntimeTileCellSaveDelta cell = delta.RuntimeTileDeltas[i];
            int x = cell.LocalPosition.x;
            int y = cell.LocalPosition.y;
            if ((uint)x >= (uint)terrain.Width || (uint)y >= (uint)terrain.Height)
                continue;

            terrain.SetCell(x, y, cell.ToTerrainCell());

            // 同一格的建筑损伤与裸露泥炭采挖互斥；保留后者的部分进度以便读档续挖。
            float accumulatedDamage = cell.BlockingTileId != 0 ||
                GroundTileHarvestSystem.IsHarvestableGround(cell.GroundTileId)
                ? Mathf.Max(0f, cell.AccumulatedDamage) : 0f;
            WriteRuntimeBuildingDamage(terrain, x, y, accumulatedDamage);
        }
        RestoreAgricultureTerrain(chunk, delta);
        foreach (GrassCellSaveDelta grass in delta.GrassDeltas)
        {
            int x = grass.LocalPosition.x, y = grass.LocalPosition.y;
            if ((uint)x < (uint)terrain.Width && (uint)y < (uint)terrain.Height &&
                (grass.State == GrassCellState.Removed || grass.State == GrassCellState.Empty))
                terrain.SetGrass(x, y, 0);
        }
        RestoreSupportTerrain(chunk, delta);
        RestoreContaminationTerrain(chunk, delta);
        RestoreLiquidTerrain(chunk, delta);
        RestoreGroundLayerTerrain(chunk, delta);
        RestoreSnowTerrain(chunk, delta);
        RestoreRuntimeFireStates(chunk, delta);
        baseline.PersistenceRestored = true;
    }

    /// <summary>放置、受损或拆除运行时格子建筑后立即更新内存差量，避免区块回收时丢失状态。</summary>
    public void RecordRuntimeTerrainChange(TileBuildingCell placedCell)
    {
        if (!GameNetwork.HasStateAuthority || SaveData == null ||
            !placedCell.UsesRuntimeTerrain)
        {
            return;
        }

        ChunkRuntime chunk = placedCell.RuntimeChunk;
        ChunkTerrainData terrain = chunk?.Terrain;
        if (chunk == null || chunk.DataStatus != ChunkDataStatus.Ready ||
            terrain == null || terrain.IsDisposed)
        {
            return;
        }

        string planetName = ResolveRuntimePlanetName();
        string chunkName = ToChunkName(chunk.Address);
        string key = BuildChunkKey(planetName, chunkName);
        if (!TryEnsureRuntimeChunkBaseline(key, chunk, out RuntimeChunkBaseline baseline))
            return;

        int x = placedCell.LocalPosition.x;
        int y = placedCell.LocalPosition.y;
        if ((uint)x >= (uint)terrain.Width || (uint)y >= (uint)terrain.Height)
            return;

        TerrainCell currentCell = terrain.GetCell(x, y);
        float accumulatedDamage = ReadRuntimeBuildingDamage(terrain, x, y);
        SetRuntimeTileDelta(key, planetName, chunk.Address, placedCell.LocalPosition,
            currentCell, accumulatedDamage, baseline.GetCell(x, y),
            baseline.GetAccumulatedDamage(x, y));
        runtimeTerrainDirtyChunks.Add(key);
        NotifyAuthoritativeTerrainCellChanged(chunk, placedCell.LocalPosition);
    }

    // 场景表现解绑会结清天气批处理，世界模型交接前再次保存实际地形变化。
    internal bool CaptureRuntimeTerrainAfterSceneViewUnbinding(string worldKey)
    {
        if (string.IsNullOrEmpty(worldKey) ||
            !string.Equals(worldKey, GameplayWorldRegistry.ExecutionWorldKey, StringComparison.Ordinal)) return false;
        try
        {
            if (!CaptureRuntimeTerrainStates()) return false;
            CaptureRuntimeFireWorldsForSceneUnbinding(worldKey);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError($"[SaveDataMgr] 场景解绑后采集地形失败：{worldKey}，{exception}");
            return false;
        }
    }

    /// <summary>保存前复核本次变脏的运行时区块，避免只依赖单格通知留下半成品差量。</summary>
    private bool CaptureRuntimeTerrainStates()
    {
        if (!GameNetwork.HasStateAuthority) return true;
        bool fireCaptured = CaptureRuntimeFireStatesForSave();
        if (runtimeTerrainDirtyChunks.Count == 0) return fireCaptured;
        string planetName = GameplayWorldRegistry.ExecutionWorldKey;
        if (string.IsNullOrWhiteSpace(planetName)) return false;
        string prefix = planetName + "\u001f";
        bool hasWorldChanges = false;
        foreach (string dirtyKey in runtimeTerrainDirtyChunks)
            if (dirtyKey.StartsWith(prefix, StringComparison.Ordinal)) { hasWorldChanges = true; break; }
        if (!hasWorldChanges) return fireCaptured;
        ChunkMgr manager = ChunkMgr.ExistingInstance;
        if (SaveData == null || manager?.WorldRuntime == null || manager.WorldRuntime.WorldId != planetName)
            return false;

        bool succeeded = true;
        IReadOnlyDictionary<RuntimeWorldAddress, ChunkRuntime> chunks = manager.Chunks;
        foreach (KeyValuePair<RuntimeWorldAddress, ChunkRuntime> pair in chunks)
        {
            string key = BuildChunkKey(planetName, ToChunkName(pair.Key));
            if (!runtimeTerrainDirtyChunks.Contains(key)) continue;
            ChunkRuntime chunk = pair.Value;
            if (chunk == null || chunk.DataStatus != ChunkDataStatus.Ready ||
                chunk.Terrain == null || chunk.Terrain.IsDisposed)
            {
                succeeded = false;
                continue;
            }

            if (!TryEnsureRuntimeChunkBaseline(key, chunk, out RuntimeChunkBaseline baseline))
            {
                succeeded = false;
                continue;
            }

            // 采集前撤标记；采集期间新变化会重新标脏，失败则恢复标记。
            runtimeTerrainDirtyChunks.Remove(key);
            try
            {
                RefreshRuntimeChunkDelta(key, planetName, pair.Key, chunk, baseline);
                if (runtimeTerrainDirtyChunks.Contains(key)) succeeded = false;
            }
            catch (Exception exception)
            {
                runtimeTerrainDirtyChunks.Add(key);
                succeeded = false;
                Debug.LogError($"[SaveDataMgr] 采集世界地形失败：{key}，{exception}");
            }
        }
        return succeeded && fireCaptured;
    }

    /// <summary>为当前运行时区块建立完整地形与互斥的建筑损伤、地表采挖进度基线。</summary>
    private bool TryEnsureRuntimeChunkBaseline(
        string key,
        ChunkRuntime chunk,
        out RuntimeChunkBaseline baseline)
    {
        baseline = null;
        if (chunk?.Terrain == null || chunk.Terrain.IsDisposed)
            return false;

        if (runtimeChunkBaselines.TryGetValue(key, out RuntimeChunkBaseline existing) &&
            existing.ChunkReference.IsAlive &&
            ReferenceEquals(existing.ChunkReference.Target, chunk))
        {
            baseline = existing;
            return true;
        }

        ChunkTerrainData terrain = chunk.Terrain;
        TerrainCell[] cells = new TerrainCell[terrain.CellCount];
        for (int x = 0; x < terrain.Width; x++)
        {
            for (int y = 0; y < terrain.Height; y++)
                cells[y * terrain.Width + x] = terrain.GetCell(x, y);
        }

        if (!terrain.TryCopyEnvironmentLayer(
                TileBuildingSystem.RuntimeDamageLayerId,
                out float[] accumulatedDamage))
        {
            accumulatedDamage = Array.Empty<float>();
        }

        baseline = new RuntimeChunkBaseline(
            chunk,
            terrain.Width,
            terrain.Height,
            cells,
            accumulatedDamage);
        runtimeChunkBaselines[key] = baseline;
        return true;
    }

    /// <summary>更新一格完整地形及其累计工作量的最小差量。</summary>
    private void SetRuntimeTileDelta(
        string key,
        string planetName,
        RuntimeWorldAddress address,
        Vector2Int localPosition,
        TerrainCell currentCell,
        float accumulatedDamage,
        TerrainCell baselineCell,
        float baselineAccumulatedDamage)
    {
        MarkChunkDifferenceChanged(key);
        if (!chunkDeltas.TryGetValue(key, out ChunkSaveRecord delta) || delta == null)
            delta = CreateRuntimeChunkDelta(planetName, address);

        delta.RuntimeTileDeltas ??= new List<RuntimeTileCellSaveDelta>();
        delta.RuntimeTileDeltas.RemoveAll(cell => cell.LocalPosition == localPosition);
        if (!currentCell.Equals(baselineCell) ||
            !Mathf.Approximately(accumulatedDamage, baselineAccumulatedDamage))
        {
            delta.RuntimeTileDeltas.Add(RuntimeTileCellSaveDelta.Capture(
                localPosition, currentCell, accumulatedDamage));
            delta.RuntimeTileDeltas.Sort(CompareRuntimeTileDelta);
            chunkDeltas[key] = delta;
            return;
        }

        if (!delta.HasChanges)
            chunkDeltas.Remove(key);
    }

    /// <summary>保存前按完整区块状态重建运行时地形差量。</summary>
    private void RefreshRuntimeChunkDelta(
        string key,
        string planetName,
        RuntimeWorldAddress address,
        ChunkRuntime chunk,
        RuntimeChunkBaseline baseline)
    {
        MarkChunkDifferenceChanged(key);
        ChunkTerrainData terrain = chunk.Terrain;
        chunkDeltas.TryGetValue(key, out ChunkSaveRecord delta);

        List<RuntimeTileCellSaveDelta> changes = new List<RuntimeTileCellSaveDelta>();
        for (int x = 0; x < terrain.Width; x++)
        {
            for (int y = 0; y < terrain.Height; y++)
            {
                TerrainCell currentCell = terrain.GetCell(x, y);
                float currentAccumulatedDamage = ReadRuntimeBuildingDamage(terrain, x, y);
                if (currentCell.Equals(baseline.GetCell(x, y)) &&
                    Mathf.Approximately(
                        currentAccumulatedDamage,
                        baseline.GetAccumulatedDamage(x, y)))
                {
                    continue;
                }

                changes.Add(RuntimeTileCellSaveDelta.Capture(
                    new Vector2Int(x, y), currentCell, currentAccumulatedDamage));
            }
        }

        if (changes.Count == 0)
        {
            delta?.RuntimeTileDeltas?.Clear();
            if (delta != null && !delta.HasChanges)
                chunkDeltas.Remove(key);
            return;
        }

        delta ??= CreateRuntimeChunkDelta(planetName, address);
        delta.RuntimeTileDeltas = changes;
        chunkDeltas[key] = delta;
    }

    /// <summary>读取一格建筑损伤或裸露资源采挖进度；两者在同一格互斥。</summary>
    private static float ReadRuntimeBuildingDamage(ChunkTerrainData terrain, int x, int y)
    {
        return terrain != null &&
               terrain.TryGetEnvironmentValue(
                   TileBuildingSystem.RuntimeDamageLayerId,
                   x,
                   y,
                   out float accumulatedDamage)
            ? Mathf.Max(0f, accumulatedDamage)
            : 0f;
    }

    /// <summary>恢复一格累计工作量，默认零值不额外创建整张环境层数组。</summary>
    private static void WriteRuntimeBuildingDamage(
        ChunkTerrainData terrain,
        int x,
        int y,
        float accumulatedDamage)
    {
        if (terrain == null)
            return;

        float safeDamage = Mathf.Max(0f, accumulatedDamage);
        bool hasLayer = terrain.TryGetEnvironmentValue(
            TileBuildingSystem.RuntimeDamageLayerId,
            x,
            y,
            out float currentDamage);
        if ((!hasLayer && Mathf.Approximately(safeDamage, 0f)) ||
            (hasLayer && Mathf.Approximately(currentDamage, safeDamage)))
        {
            return;
        }

        terrain.SetEnvironmentValue(
            TileBuildingSystem.RuntimeDamageLayerId,
            x,
            y,
            safeDamage);
    }

    private static ChunkSaveRecord CreateRuntimeChunkDelta(
        string planetName,
        RuntimeWorldAddress address)
    {
        return new ChunkSaveRecord
        {
            PlanetName = planetName,
            ChunkName = ToChunkName(address),
            ChunkPosition = new Vector2Int(address.ChunkOrigin.X, address.ChunkOrigin.Y)
        };
    }

    private static int CompareRuntimeTileDelta(
        RuntimeTileCellSaveDelta left,
        RuntimeTileCellSaveDelta right)
    {
        int x = left.LocalPosition.x.CompareTo(right.LocalPosition.x);
        return x != 0 ? x : left.LocalPosition.y.CompareTo(right.LocalPosition.y);
    }

    private static string ResolveRuntimePlanetName()
    {
        string worldKey = GameplayWorldRegistry.ExecutionWorldKey;
        return string.IsNullOrWhiteSpace(worldKey) ? "world" : worldKey;
    }

    private IEnumerator CaptureRuntimeTerrainStatesCoroutine(AutoSaveWorkBudget budget)
    {
        if (!GameNetwork.HasStateAuthority || SaveData == null)
            yield break;
        IEnumerator fireRoutine = CaptureRuntimeFireStatesCoroutine(budget);
        while (fireRoutine.MoveNext())
            yield return fireRoutine.Current;

        ChunkMgr manager = ChunkMgr.ExistingInstance;
        if (runtimeTerrainDirtyChunks.Count == 0 || manager?.Chunks == null)
            yield break;

        var chunks = new List<KeyValuePair<RuntimeWorldAddress, ChunkRuntime>>(manager.Chunks);
        string planetName = ResolveRuntimePlanetName();
        foreach (var pair in chunks)
        {
            ChunkRuntime chunk = pair.Value;
            string key = BuildChunkKey(planetName, ToChunkName(pair.Key));
            if (chunk != null && chunk.DataStatus == ChunkDataStatus.Ready &&
                chunk.Terrain != null && !chunk.Terrain.IsDisposed &&
                runtimeTerrainDirtyChunks.Contains(key) &&
                TryEnsureRuntimeChunkBaseline(key, chunk, out RuntimeChunkBaseline baseline))
            {
                // 每区块原子采集，随后发生的新地形修改继续保留脏标记。
                runtimeTerrainDirtyChunks.Remove(key);
                try { RefreshRuntimeChunkDelta(key, planetName, pair.Key, chunk, baseline); }
                catch { runtimeTerrainDirtyChunks.Add(key); throw; }
            }
            if (budget.ShouldYield())
                yield return budget.WaitForNextSlice();
        }
    }

    /// <summary>新版区块的程序化阻挡层基线；用弱引用避免玩家探索过多区块后长期持有运行时对象。</summary>
    private sealed class RuntimeChunkBaseline
    {
        /// <summary>复制程序化区块基线，避免后续运行时修改污染比较源。</summary>
        public RuntimeChunkBaseline(
            ChunkRuntime chunk,
            int width,
            int height,
            TerrainCell[] cells,
            float[] accumulatedDamage)
        {
            ChunkReference = new WeakReference(chunk);
            Width = width;
            Height = height;
            Cells = cells;
            AccumulatedDamage = accumulatedDamage ?? Array.Empty<float>();
        }

        public WeakReference ChunkReference { get; }
        /// <summary>同一份区块数据的存档差量只恢复一次，防止缓存区块往返视野时覆盖运行中改动。</summary>
        public bool PersistenceRestored { get; set; }
        public int Width { get; }
        public int Height { get; }
        public TerrainCell[] Cells { get; }
        /// <summary>程序化基线中每格建筑已经累计的损伤；空数组表示全为零。</summary>
        public float[] AccumulatedDamage { get; }

        /// <summary>读取程序化基线中的完整格子属性。</summary>
        public TerrainCell GetCell(int x, int y)
        {
            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
                throw new ArgumentOutOfRangeException(nameof(x));
            return Cells[y * Width + x];
        }

        /// <summary>读取程序化基线中一格建筑的累计损伤。</summary>
        public float GetAccumulatedDamage(int x, int y)
        {
            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
                return 0f;
            int index = y * Width + x;
            return (uint)index < (uint)AccumulatedDamage.Length
                ? AccumulatedDamage[index]
                : 0f;
        }
    }
    #endregion

    #region 权威地形变更通知
    public static event Action<string, ChunkRuntime, Vector2Int> AuthoritativeTerrainCellChanged;

    // 网络层只记脏格，事务及其回滚结束后再捕获最终状态，避免广播未提交的候选。
    private static void NotifyAuthoritativeTerrainCellChanged(ChunkRuntime chunk, Vector2Int local)
    {
        if (!GameNetwork.HasStateAuthority || chunk?.Terrain == null || chunk.Terrain.IsDisposed)
            return;
        AuthoritativeTerrainCellChanged?.Invoke(GameplayWorldRegistry.ExecutionWorldKey, chunk, local);
    }
    #endregion
}
