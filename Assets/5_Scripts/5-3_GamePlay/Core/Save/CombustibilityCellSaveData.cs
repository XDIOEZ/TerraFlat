using System;
using System.Collections.Generic;
using FlatWorld.Persistence;
using FlatWorld.WorldModel;
using MemoryPack;

[MemoryPackable(GenerateType.VersionTolerant)]
[Serializable]
public partial class CombustibilityCellSaveData
{
    #region 可燃性固定字段存档
    [MemoryPackOrder(0)] public int CellIndex;
    [MemoryPackOrder(1)] public bool OverridesBase;
    [MemoryPackOrder(2)] public float IgnitionProbability;
    [MemoryPackOrder(3)] public float FuelSeconds;
    [MemoryPackOrder(4)] public float ConsumedFuelSeconds;

    public static CombustibilityCellSaveData Capture(WorldCombustibilityCellState state) => new()
    {
        CellIndex = state.CellIndex, OverridesBase = state.OverridesBase, IgnitionProbability = state.IgnitionProbability,
        FuelSeconds = state.FuelSeconds, ConsumedFuelSeconds = state.ConsumedFuelSeconds
    };
    public WorldCombustibilityCellState ToRuntime() => new()
    {
        CellIndex = CellIndex, OverridesBase = OverridesBase, IgnitionProbability = IgnitionProbability,
        FuelSeconds = FuelSeconds, ConsumedFuelSeconds = ConsumedFuelSeconds
    };
    public static bool Matches(List<CombustibilityCellSaveData> saved, WorldCombustibilityCellState[] states)
    {
        if ((saved?.Count ?? 0) != states.Length) return false;
        for (int i = 0; i < states.Length; i++)
        {
            var cell = saved[i]; var state = states[i];
            if (cell.CellIndex != state.CellIndex || cell.OverridesBase != state.OverridesBase ||
                cell.IgnitionProbability != state.IgnitionProbability || cell.FuelSeconds != state.FuelSeconds ||
                cell.ConsumedFuelSeconds != state.ConsumedFuelSeconds) return false;
        }
        return true;
    }
    #endregion
}

/// <summary>SQLite chunk v1 的累计燃烧秒数迁入新层，原固定字段保持原义。</summary>
internal sealed class WorldCombustibilitySaveMigration : ISaveArchivePayloadMigration
{
    #region 可燃性载荷迁移
    public string SchemaId => "flatworld.chunk";
    public int FromVersion => 1;
    public int ToVersion => 2;
    public byte[] Migrate(byte[] payload)
    {
        if (payload == null) return null;
        var snapshot = MemoryPackSerializer.Deserialize<SqliteChunkSnapshot>(payload);
        if (snapshot?.Terrain == null) return payload;
        var terrain = MemoryPackSerializer.Deserialize<ChunkSaveRecord>(snapshot.Terrain);
        terrain.CombustibilityCells ??= new();
        foreach (var fire in terrain.FireCells)
            if (fire.SurfaceFuelBurnSeconds > 0f)
                terrain.CombustibilityCells.Add(new CombustibilityCellSaveData
                { CellIndex = fire.CellIndex, ConsumedFuelSeconds = fire.SurfaceFuelBurnSeconds });
        snapshot.Terrain = MemoryPackSerializer.Serialize(terrain);
        return MemoryPackSerializer.Serialize(snapshot);
    }
    #endregion
}
