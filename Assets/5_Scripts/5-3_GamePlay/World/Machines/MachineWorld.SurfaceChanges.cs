using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class MachineWorld
{
    #region 地表批量提交通知
    // 同步借用格列表，表现消费者在整批数据就位后只处理一次最终状态。
    public static event Action<Vector2Int> SurfacePresentationCellChanged;
    public static event Action<IReadOnlyList<Vector2Int>> SurfacePresentationCellsChanged;

    private sealed class SurfaceChanges
    {
        internal readonly HashSet<Vector2Int> Mechanical = new();
        internal readonly HashSet<Vector2Int> Visual = new();
    }

    private sealed class SurfaceChangeBatch : IDisposable
    {
        internal readonly Dictionary<string, SurfaceChanges> Worlds = new(StringComparer.Ordinal);
        internal int Depth;
        private bool disposed;
        private readonly SurfaceChangeBatch owner;

        internal SurfaceChangeBatch(SurfaceChangeBatch batch)
        { owner = batch ?? this; owner.Depth++; }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (--owner.Depth != 0) return;
            if (!ReferenceEquals(surfaceChangeBatch, owner)) return;
            surfaceChangeBatch = null;
            foreach (KeyValuePair<string, SurfaceChanges> entry in owner.Worlds)
                FlushSurfaceChanges(entry.Key, entry.Value);
            owner.Worlds.Clear();
        }
    }

    private static SurfaceChangeBatch surfaceChangeBatch;

    private static void FlushSurfaceChanges(string key, SurfaceChanges changes)
    {
        if (key != SceneManager.GetActiveScene().name) return;
        try
        {
            using var world = UseScope(key);
            foreach (Vector2Int cell in changes.Mechanical)
                NotifyScopeTransferChange(() => BuildingOccupancyRegistry.NotifyMechanicalChanged(cell));
            var cells = new List<Vector2Int>(changes.Visual);
            Delegate[] cellHandlers = CellChanged?.GetInvocationList();
            foreach (Vector2Int cell in cells)
                if (cellHandlers != null)
                    foreach (Action<Vector2Int> handler in cellHandlers)
                        NotifyScopeTransferChange(() => handler(cell));
            if (cells.Count > 0 && SurfacePresentationCellsChanged != null)
                foreach (Action<IReadOnlyList<Vector2Int>> handler in SurfacePresentationCellsChanged.GetInvocationList())
                    NotifyScopeTransferChange(() => handler(cells));
        }
        catch (Exception error) { Debug.LogException(error); }
    }

    /// <summary>只合并当前同步事务内的通知；异常回滚后仍投影最终真实状态。</summary>
    public static IDisposable BeginSurfaceChangeBatch()
    {
        if (surfaceChangeBatch != null) return new SurfaceChangeBatch(surfaceChangeBatch);
        return surfaceChangeBatch = new SurfaceChangeBatch(null);
    }

    private static SurfaceChanges CurrentSurfaceChanges()
    {
        if (!surfaceChangeBatch.Worlds.TryGetValue(worldKey, out SurfaceChanges changes))
            surfaceChangeBatch.Worlds.Add(worldKey, changes = new SurfaceChanges());
        return changes;
    }

    private static void PublishSurfaceMechanicalChange(Vector2Int cell)
    {
        if (surfaceChangeBatch != null) CurrentSurfaceChanges().Mechanical.Add(WorldTopologyRuntime.NormalizeCell(cell));
        else BuildingOccupancyRegistry.NotifyMechanicalChanged(cell);
    }

    private static void PublishSurfaceCellChange(Vector2Int cell)
    {
        if (surfaceChangeBatch != null) CurrentSurfaceChanges().Visual.Add(WorldTopologyRuntime.NormalizeCell(cell));
        else
        {
            CellChanged?.Invoke(cell);
            SurfacePresentationCellChanged?.Invoke(cell);
        }
    }

    private static void ResetSurfaceChangeBatch()
    {
        surfaceChangeBatch = null;
        SurfacePresentationCellChanged = null;
        SurfacePresentationCellsChanged = null;
    }
    #endregion
}
