using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

// 只把正在模拟的权威地形转成通用阻挡声明，不创建任何 Unity 物理组件。
internal sealed class WorldTerrainPhysicsSource2D : IDisposable
{
    #region 地形数据接入
    private sealed class Binding : IDisposable
    {
        internal readonly ChunkRuntime Chunk;
        internal readonly ChunkTerrainData Terrain;
        internal readonly WorldPhysicsFixedRectangleSource2D Source;

        internal Binding(WorldPhysicsService2D physics, ChunkRuntime chunk)
        {
            Chunk = chunk; Terrain = chunk.Terrain;
            Source = physics.CreateFixedRectangleSource(chunk,
                new Vector2(chunk.Address.ChunkOrigin.X, chunk.Address.ChunkOrigin.Y));
            try
            {
                Terrain.Changed += Changed;
                for (int y = 0; y < Terrain.Height; y++)
                    for (int x = 0; x < Terrain.Width; x++) WriteCell(x, y);
            }
            catch { Dispose(); throw; }
        }

        private void Changed(ChunkTerrainChanged change)
        {
            if (change.Kind == TerrainChangeKind.Cell || change.Kind == TerrainChangeKind.TileStack)
                WriteCell(change.LocalCell.X, change.LocalCell.Y);
        }

        private void WriteCell(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Terrain.Width || y >= Terrain.Height) return;
            TerrainCell cell = Terrain.GetCell(x, y);
            int id = y * Terrain.Width + x;
            if (cell.BlockingTileId != 0 && (cell.Flags & TerrainCellFlags.Blocking) != 0)
                Source.SetRectangle(id, new Vector2(x + .5f, y + .5f), Vector2.one);
            else Source.RemoveRectangle(id);
        }

        public void Dispose() { Terrain.Changed -= Changed; Source.Dispose(); }
    }

    private readonly WorldPhysicsService2D physics;
    private readonly Dictionary<RuntimeWorldAddress, Binding> bindings = new();
    private WorldRuntime world;
    private IDisposable committed, simulation, evicted;
    private bool disposed;
    internal int BoundTerrainCount => bindings.Count;

    internal WorldTerrainPhysicsSource2D(WorldPhysicsService2D owner)
    { physics = owner ?? throw new ArgumentNullException(nameof(owner)); physics.Disposing += Dispose; }

    internal bool IsBound(ChunkRuntime chunk) => chunk != null && bindings.TryGetValue(chunk.Address, out var binding) &&
        ReferenceEquals(binding.Chunk, chunk) && ReferenceEquals(binding.Terrain, chunk.Terrain) && binding.Source.IsValid;

    internal bool TryGetSource(RuntimeWorldAddress address, out WorldPhysicsFixedRectangleSource2D source)
    {
        source = bindings.TryGetValue(address, out var binding) ? binding.Source : null;
        return source?.IsValid == true;
    }

    internal void Bind(WorldRuntime model)
    {
        if (disposed || !physics.IsValid) throw new ObjectDisposedException(nameof(WorldTerrainPhysicsSource2D));
        if (model == null || model.WorldId != physics.WorldKey) throw new ArgumentException("地形来源与物理世界不一致。", nameof(model));
        if (ReferenceEquals(world, model)) return;
        Unbind(world);
        world = model;
        committed = model.Events.Subscribe<ChunkCommitted>(change => Reconcile(change.Address));
        simulation = model.Events.Subscribe<ChunkSimulationStatusChanged>(change => Reconcile(change.Address));
        evicted = model.Events.Subscribe<ChunkEvicted>(change => Release(change.Address));
        try { foreach (ChunkRuntime chunk in model.Chunks.Values) Reconcile(chunk.Address); }
        catch { Unbind(model); throw; }
    }

    private void Reconcile(RuntimeWorldAddress address)
    {
        if (disposed || !physics.IsValid || world == null) return;
        if (!world.TryGetChunk(address, out var chunk) || chunk.DataStatus != ChunkDataStatus.Ready ||
            chunk.Terrain == null || chunk.SimulationStatus != ChunkSimulationStatus.Active)
        { Release(address); return; }
        if (IsBound(chunk)) return;
        Release(address);
        bindings.Add(address, new Binding(physics, chunk));
    }

    private void Release(RuntimeWorldAddress address)
    { if (bindings.Remove(address, out var binding)) binding.Dispose(); }

    internal void Unbind(WorldRuntime expected)
    {
        if (!ReferenceEquals(world, expected)) return;
        committed?.Dispose(); simulation?.Dispose(); evicted?.Dispose();
        committed = simulation = evicted = null;
        world = null;
        foreach (var binding in bindings.Values) binding.Dispose();
        bindings.Clear();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        physics.Disposing -= Dispose;
        Unbind(world);
    }
    #endregion
}
