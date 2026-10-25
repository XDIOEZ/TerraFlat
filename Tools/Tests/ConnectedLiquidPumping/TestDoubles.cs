using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlatWorld.WorldModel;
using UnityEngine;

namespace UnityEngine
{
    public static class Time { public static double unscaledTimeAsDouble; }
    public static class Debug { public static void LogError(object value) => Console.WriteLine(value); }
    public readonly record struct Vector2(float x, float y)
    {
        public static Vector2 one => new(1, 1);
        public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.x + b.x, a.y + b.y);
        public static Vector2 operator *(Vector2 a, float b) => new(a.x * b, a.y * b);
    }
    public readonly record struct Vector2Int(int x, int y)
    {
        public static Vector2Int left => new(-1, 0);
        public static Vector2Int right => new(1, 0);
        public static Vector2Int down => new(0, -1);
        public static Vector2Int up => new(0, 1);
        public static Vector2Int FloorToInt(Vector2 p) => new((int)Math.Floor(p.x), (int)Math.Floor(p.y));
        public static Vector2Int operator +(Vector2Int a, Vector2Int b) => new(a.x + b.x, a.y + b.y);
        public static implicit operator Vector2(Vector2Int a) => new(a.x, a.y);
    }
}
namespace FlatWorld.Networking { public static class GameNetwork { public static bool HasStateAuthority = true; } }
namespace FlatWorld.WorldModel
{
    public readonly record struct Int2(int X, int Y);
    public readonly record struct WorldAddress(string DimensionId, Int2 ChunkOrigin) : IComparable<WorldAddress>
    {
        public int CompareTo(WorldAddress other)
        {
            int x = ChunkOrigin.X.CompareTo(other.ChunkOrigin.X);
            return x != 0 ? x : ChunkOrigin.Y.CompareTo(other.ChunkOrigin.Y);
        }
    }
    public readonly record struct ChunkEvicted(WorldAddress Address);
    public enum ChunkDataStatus { Ready }
    public enum ChunkLeaseKind { Simulation }
    public sealed class ChunkLease(Action release) : IDisposable
    {
        private bool disposed;
        public void Dispose() { if (disposed) return; disposed = true; release(); }
    }
    [Flags] public enum TerrainCellFlags { None = 0, Blocking = 1 }
    public readonly record struct TerrainCell(int BlockingTileId, TerrainCellFlags Flags);
    public readonly record struct LiquidCellValue(int LiquidTypeIndex, int LiquidLevel)
    { public const int MaximumLevel = 10; }
    public enum TerrainChangeKind { Cell, TileStack, Liquid, Environment }
    public sealed class ChunkTerrainChanged
    {
        public TerrainChangeKind Kind;
        public string Layer;
        public int Index;
        public bool AffectsEnvironmentLayer(string id) => Layer == id;
        public IEnumerable<int> GetCellIndices(int width) { yield return Index; }
    }
    public readonly record struct ChunkLiquidBatchChanged(ReadOnlyMemory<int> CellIndices);
    public sealed class LiquidTypes
    {
        public string GetId(int index) => index == 1 ? "water" : index == 2 ? "lava" : null;
        public int GetIndex(string id) => id == "water" ? 1 : id == "lava" ? 2 : 0;
    }
    public sealed class ChunkTerrainData
    {
        public const int Size = 16;
        public int Width => Size;
        public int Height => Size;
        public int CellCount => Size * Size;
        public bool IsDisposed;
        public readonly int[] Levels = new int[Size * Size], Types = new int[Size * Size];
        public readonly LiquidCellValue[] Baseline = new LiquidCellValue[Size * Size];
        public readonly TerrainCell[] Ground = new TerrainCell[Size * Size];
        public readonly float[] Excavated = new float[Size * Size];
        public LiquidTypes LiquidTypes = new();
        public ReadOnlyMemory<int> LiquidLevel => Levels;
        public event Action<ChunkTerrainChanged> Changed;
        public event Action<ChunkLiquidBatchChanged> LiquidBatchChanged;
        public TerrainCell GetCell(int x, int y) => Ground[y * Size + x];
        public LiquidCellValue GetLiquidBaseline(int x, int y) => Baseline[y * Size + x];
        public int GetLiquidLevel(int x, int y) => Levels[y * Size + x];
        public int GetLiquidTypeIndex(int x, int y) => Types[y * Size + x];
        public bool IsLiquidChanged(int x, int y) => GetLiquidLevel(x, y) != GetLiquidBaseline(x, y).LiquidLevel;
        public bool TryGetEnvironmentValue(string id, int x, int y, out float value)
        { value = id == "excavated" ? Excavated[y * Size + x] : 0; return true; }
        public void Set(int x, int y, int type, int level)
        {
            int i = y * Size + x;
            Types[i] = level == 0 ? 0 : type;
            Levels[i] = level;
            Changed?.Invoke(new() { Kind = TerrainChangeKind.Liquid, Index = i });
        }
        public void Wall(int x, int y, bool blocked)
        {
            int i = y * Size + x;
            Ground[i] = new(blocked ? 1 : 0, TerrainCellFlags.None);
            Changed?.Invoke(new() { Kind = TerrainChangeKind.Cell, Index = i });
        }
        public void Batch(int[] indices) => LiquidBatchChanged?.Invoke(new(indices));
    }
    public sealed class ChunkRuntime
    {
        public WorldAddress Address;
        public ChunkTerrainData Terrain = new();
        public ChunkDataStatus DataStatus => ChunkDataStatus.Ready;
    }
    public sealed class EventBus
    {
        private Action<ChunkEvicted> evicted;
        private sealed class Subscription(Action dispose) : IDisposable { public void Dispose() => dispose(); }
        public IDisposable Subscribe<T>(Action<T> callback)
        {
            Action<ChunkEvicted> handler = e => callback((T)(object)e);
            evicted += handler;
            return new Subscription(() => evicted -= handler);
        }
        public void Evict(WorldAddress address) => evicted?.Invoke(new(address));
    }
    public sealed class WorldRuntime { public long Epoch = 1; public string WorldId = "test"; public EventBus Events = new(); }
}
public readonly record struct RuntimeTerrainTileSample(WorldAddress Address, ChunkTerrainData Terrain,
    Vector2Int WorldCell, Vector2Int LocalCell, TerrainCell Cell, int Unused);
public sealed class ChunkMgr
{
    public WorldRuntime WorldRuntime = new();
    public object LiquidFlowExperiment;
    public int Leases, Requests;
    public readonly Dictionary<WorldAddress, ChunkRuntime> Remote = new();
    public ChunkLease AcquireChunkLease(WorldAddress address, ChunkLeaseKind kind)
    { Leases++; return new(() => Leases--); }
    public Task<ChunkRuntime> RequestLiquidFlowChunkAsync(WorldAddress address, CancellationToken cancellation)
    {
        Requests++;
        return Task.FromResult(Remote.TryGetValue(address, out var chunk) ? chunk : new ChunkRuntime { Address = address });
    }
    public WorldAddress ResolveWorldAddress(Vector2 p, string dimension) => new(dimension,
        new((int)Math.Floor(p.x / 16) * 16, (int)Math.Floor(p.y / 16) * 16));
}
public sealed class SaveDataMgr
{
    public static SaveDataMgr Instance = new();
    public int RestoredTerrain, RestoredBuildings;
    public void RestoreRuntimeTerrainForChunk(WorldAddress address, ChunkRuntime chunk) => RestoredTerrain++;
    public void RestoreRuntimeBuildingsForChunk(WorldAddress address) => RestoredBuildings++;
}
public static class WorldTopologyRuntime
{
    public static int WrapWidth;
    public static Vector2Int NormalizeCell(Vector2Int p) => WrapWidth > 0
        ? new((p.x % WrapWidth + WrapWidth) % WrapWidth, p.y) : p;
}
public sealed class LiquidDefinition { public object WorldWater = new(); }
public sealed class GameRes
{
    public static GameRes ExistingInstance = new();
    public bool TryGetLiquidDefinition(string id, out LiquidDefinition value)
    { value = id == null ? null : new(); return value != null; }
}
public static class GameplayWorldRegistry { public static string ExecutionWorldKey = "test"; }
public static class TerrainSurfaceHeightLayer
{
    public const string ExcavatedLevelsLayerId = "excavated", CoverLevelsLayerId = "cover";
    public static int GetExcavatedLevels(ChunkTerrainData t, int x, int y) => (int)t.Excavated[y * 16 + x];
}
public static class WorldLiquidFlowObstacles
{
    private static readonly HashSet<Vector2Int> cells = new();
    public static event Action<Vector2Int> Changed;
    public static bool IsBlocked(Vector2Int cell) => cells.Contains(cell);
    public static void Set(Vector2Int cell, bool blocked)
    { if (blocked) cells.Add(cell); else cells.Remove(cell); Changed?.Invoke(cell); }
}
public static class WorldLiquidReplenishment
{
    public static bool TryTransfer(RuntimeTerrainTileSample a, RuntimeTerrainTileSample b, bool canal)
    {
        int left = a.Terrain.GetLiquidLevel(a.LocalCell.x, a.LocalCell.y);
        int right = b.Terrain.GetLiquidLevel(b.LocalCell.x, b.LocalCell.y);
        int amount = Math.Max(0, (left - right) / 2);
        if (amount == 0) return false;
        int type = a.Terrain.GetLiquidTypeIndex(a.LocalCell.x, a.LocalCell.y);
        a.Terrain.Set(a.LocalCell.x, a.LocalCell.y, type, left - amount);
        b.Terrain.Set(b.LocalCell.x, b.LocalCell.y, type, right + amount);
        return true;
    }
}
