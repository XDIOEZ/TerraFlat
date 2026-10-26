using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using MemoryPack;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 地表冰雪融化正式规则采样
    private static void RegisterSurfaceThawOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("environment/surface_thaw_snow", SurfaceThawSnow);
        operations.Add("environment/surface_thaw_ice", SurfaceThawIce);
        operations.Add("environment/surface_thaw_snapshots", SurfaceThawSnapshots);
    }

    // 探针只调用生产积分和真实地形快照，业务期望由 Lua 断言。
    private static JObject SurfaceThawSnow(JObject args)
    {
        using ChunkTerrainData terrain = EnvironmentBatchFixture(1, 1);
        terrain.SetEnvironmentValue(SnowDepthLayer.LayerId, 0, 0, 1f);
        int naturalCandidates = WorldWeatherSurfaceSystem.GetSnowCellIndices(terrain).Count;
        terrain.SetEnvironmentValue(TerrainSupportLayer.TileIdLayer, 0, 0, 91f);
        int coveredCandidates = WorldWeatherSurfaceSystem.GetSnowCellIndices(terrain).Count;
        WorldSurfaceThawRules.SnowStep covered = WorldSurfaceThawRules.AdvanceSnow(
            WorldSnowSystem.GetExposedNaturalDepth(terrain, 0, 0), false, 0f, 0f,
            0f, 0f, 10f, 0f, 100f, 0f, .003f, .00012f);
        terrain.SetEnvironmentValue(WorldSnowSystem.WeatherDepthLayer, 0, 0, .2f);
        int platformWeatherCandidates = WorldWeatherSurfaceSystem.GetSnowCellIndices(terrain).Count;
        WorldSurfaceThawRules.SnowStep platformWeather = WorldSurfaceThawRules.AdvanceSnow(
            WorldSnowSystem.GetExposedNaturalDepth(terrain, 0, 0), false, 0f, 0f,
            0f, .2f, 10f, 0f, 100f, 0f, .003f, .00012f);
        terrain.SetEnvironmentValue(WorldSnowSystem.WeatherDepthLayer, 0, 0, 0f);
        terrain.SetEnvironmentValue(TerrainSupportLayer.TileIdLayer, 0, 0, 0f);
        int uncoveredCandidates = WorldWeatherSurfaceSystem.GetSnowCellIndices(terrain).Count;
        WorldSurfaceThawRules.SnowStep first = WorldSurfaceThawRules.AdvanceSnow(1f, false, 0f, 0f,
            0f, 0f, 10f, 0f, 100f, 0f, .003f, .00012f);
        var saved = new SnowCellSaveData { LocalPosition = Vector2Int.zero, Edited = first.Edited,
            Depth = first.Depth, SeasonalDepth = first.SeasonalBaseline, WeatherDepth = first.WeatherDepth };
        saved = MemoryPackSerializer.Deserialize<SnowCellSaveData>(MemoryPackSerializer.Serialize(saved));
        WorldSnowInteraction.Restore(terrain, saved);
        float visibleAfterRestore = WorldSnowSystem.GetSurfaceDepth(terrain, 0, 0, null, 0f);
        int partialCandidates = WorldWeatherSurfaceSystem.GetSnowCellIndices(terrain).Count;
        WorldSurfaceThawRules.SnowStep last = WorldSurfaceThawRules.AdvanceSnow(1f, saved.Edited, saved.Depth,
            saved.SeasonalDepth, 0f, saved.WeatherDepth, 10f, 0f, 1000f, 0f, .003f, .00012f);
        WorldSnowInteraction.Restore(terrain, new SnowCellSaveData { LocalPosition = Vector2Int.zero,
            Edited = last.Edited, Depth = last.Depth, SeasonalDepth = last.SeasonalBaseline, WeatherDepth = last.WeatherDepth });
        float afterMelt = WorldSnowSystem.GetSurfaceDepth(terrain, 0, 0, null, 0f);
        int emptyCandidates = WorldWeatherSurfaceSystem.GetSnowCellIndices(terrain).Count;
        WorldSurfaceThawRules.SnowStep cold = WorldSurfaceThawRules.AdvanceSnow(1f, last.Edited, last.Depth,
            last.SeasonalBaseline, 0f, last.WeatherDepth, -20f, 0f, 100f, 0f, .003f, .00012f);
        WorldSurfaceThawRules.SnowStep newSnow = WorldSurfaceThawRules.AdvanceSnow(1f, cold.Edited, cold.Depth,
            cold.SeasonalBaseline, 0f, cold.WeatherDepth, -20f, 0f, 10f, 10f, .003f, .00012f);
        WorldSurfaceThawRules.SnowStep tiny = WorldSurfaceThawRules.AdvanceSnow(1f, true, .04f, 0f,
            0f, 0f, 10f, 0f, 10f, 0f, .003f, .00012f);
        WorldSnowInteraction.Restore(terrain, new SnowCellSaveData { LocalPosition = Vector2Int.zero,
            Edited = true, Depth = tiny.Depth });
        int tinyCandidates = WorldWeatherSurfaceSystem.GetSnowCellIndices(terrain).Count;
        WorldSurfaceThawRules.SnowStep seasonal = WorldSurfaceThawRules.AdvanceSnow(1f, true, 0f, 0f,
            .5f, 0f, 10f, 0f, 100f, 0f, .003f, .00012f);
        WorldSurfaceThawRules.SnowStep zero = WorldSurfaceThawRules.AdvanceSnow(1f, false, 0f, 0f,
            0f, 0f, 0f, 0f, 100f, 0f, .003f, .00012f);
        return new JObject { ["naturalCandidates"] = naturalCandidates, ["partialCandidates"] = partialCandidates,
            ["coveredCandidates"] = coveredCandidates, ["uncoveredCandidates"] = uncoveredCandidates,
            ["coveredEdited"] = covered.Edited, ["coveredDepth"] = covered.Depth, ["coveredMelted"] = covered.Melted,
            ["platformWeatherCandidates"] = platformWeatherCandidates,
            ["platformWeatherDepth"] = platformWeather.WeatherDepth, ["platformWeatherMelted"] = platformWeather.Melted,
            ["firstEdited"] = first.Edited, ["firstDepth"] = first.Depth, ["firstMelted"] = first.Melted,
            ["visibleAfterRestore"] = visibleAfterRestore, ["afterMelt"] = afterMelt,
            ["emptyCandidates"] = emptyCandidates, ["coldDepth"] = cold.Depth, ["newWeatherSnow"] = newSnow.WeatherDepth,
            ["tinyDepth"] = tiny.Depth, ["tinyCandidates"] = tinyCandidates,
            ["seasonalDepth"] = seasonal.Depth, ["seasonalBaseline"] = seasonal.SeasonalBaseline,
            ["seasonalMelted"] = seasonal.Melted, ["zeroEdited"] = zero.Edited, ["zeroMelted"] = zero.Melted };
    }

    private static JObject SurfaceThawIce(JObject args)
    {
        using var buffer = new ChunkTerrainBuffer(2, 1);
        var seaBed = new TerrainCell(29, 0, 0, (int)SurfaceBiomeKind.Ocean, 3, TerrainCellFlags.Walkable);
        var riverBed = new TerrainCell(30, 0, 0, (int)SurfaceBiomeKind.River, 4, TerrainCellFlags.Walkable);
        var sea = new FrozenSurfaceWaterSource(seaBed, 40, buffer.LiquidTypes.GetIndex(LiquidTypeCatalog.SeaWaterId), 80);
        var river = new FrozenSurfaceWaterSource(riverBed, 40, buffer.LiquidTypes.GetIndex(LiquidTypeCatalog.DirtyWaterId), 37);
        buffer.SetCell(0, 0, new TerrainCell(40, 0, 0, seaBed.BiomeId, 4, TerrainCellFlags.Walkable));
        buffer.SetCell(1, 0, new TerrainCell(40, 0, 0, riverBed.BiomeId, 5, TerrainCellFlags.Walkable));
        FrozenSurfaceWaterLayer.WriteGenerationSource(buffer, 0, sea);
        FrozenSurfaceWaterLayer.WriteGenerationSource(buffer, 1, river);
        using ChunkTerrainData terrain = buffer.Seal();
        FrozenSurfaceWaterLayer.TryReadSource(terrain, 0, 0, out FrozenSurfaceWaterSource seaRead);
        FrozenSurfaceWaterLayer.TryReadSource(terrain, 1, 0, out FrozenSurfaceWaterSource riverRead);
        bool candidate = WorldFrozenSurfaceWaterThawSystem.TryGetThawableSource(terrain, 0, 0, out _);
        terrain.SetEnvironmentValue(TerrainSupportLayer.TileIdLayer, 0, 0, 91f);
        bool platformProtected = !WorldFrozenSurfaceWaterThawSystem.TryGetThawableSource(terrain, 0, 0, out _);
        terrain.SetEnvironmentValue(TerrainSupportLayer.TileIdLayer, 0, 0, 0f);
        terrain.SetCell(0, 0, new TerrainCell(40, 93, 0, seaBed.BiomeId, 4, TerrainCellFlags.Walkable));
        bool backProtected = !WorldFrozenSurfaceWaterThawSystem.TryGetThawableSource(terrain, 0, 0, out _);
        terrain.SetCell(0, 0, new TerrainCell(29, 0, 0, seaBed.BiomeId, 3, TerrainCellFlags.Walkable));
        bool changedGroundProtected = !WorldFrozenSurfaceWaterThawSystem.TryGetThawableSource(terrain, 0, 0, out _);
        float partial = WorldSurfaceThawRules.AdvanceIceMeltProgress(0f, 10f, 100f, .00003f);
        terrain.SetEnvironmentValue(FrozenSurfaceWaterLayer.MeltProgressLayer, 0, 0, partial);
        int frozenBaseline = terrain.GetLiquidBaseline(0, 0).LiquidLevel;
        float complete = WorldSurfaceThawRules.AdvanceIceMeltProgress(partial, 10f, 4000f, .00003f);
        terrain.SetEnvironmentValue(FrozenSurfaceWaterLayer.MeltProgressLayer, 0, 0, complete);
        terrain.SetLiquid(0, 0, seaRead.LiquidTypeIndex, seaRead.LiquidLevel);
        int thawedBaseline = terrain.GetLiquidBaseline(0, 0).LiquidLevel;
        return new JObject { ["candidate"] = candidate, ["platformProtected"] = platformProtected,
            ["backProtected"] = backProtected, ["changedGroundProtected"] = changedGroundProtected,
            ["seaBed"] = seaRead.Bed.GroundTileId, ["seaLevel"] = seaRead.LiquidLevel,
            ["seaId"] = terrain.LiquidTypes.GetId(seaRead.LiquidTypeIndex),
            ["riverBed"] = riverRead.Bed.GroundTileId, ["riverLevel"] = riverRead.LiquidLevel,
            ["riverId"] = terrain.LiquidTypes.GetId(riverRead.LiquidTypeIndex),
            ["partial"] = partial, ["complete"] = complete, ["frozenBaseline"] = frozenBaseline,
            ["thawedBaseline"] = thawedBaseline, ["liquidPersisted"] = terrain.IsLiquidChanged(0, 0),
            ["zeroProgress"] = WorldSurfaceThawRules.AdvanceIceMeltProgress(partial, 0f, 100f, .00003f),
            ["coldProgress"] = WorldSurfaceThawRules.AdvanceIceMeltProgress(partial, -20f, 100f, .00003f) };
    }

    private static JObject SurfaceThawSnapshots(JObject args)
    {
        var state = new SnowCellSaveData { LocalPosition = Vector2Int.zero, Edited = true,
            Depth = .23f, SeasonalDepth = .1f, WeatherDepth = .04f, IceMeltProgress = .37f };
        SnowCellSaveData restored = MemoryPackSerializer.Deserialize<SnowCellSaveData>(MemoryPackSerializer.Serialize(state));
        using ChunkTerrainData terrain = EnvironmentBatchFixture(1, 1);
        WorldSnowInteraction.Restore(terrain, restored);
        SnowCellSaveData captured = WorldSnowInteraction.Capture(terrain, Vector2Int.zero);
        var payload = new WeatherSurfaceDelta();
        payload.Cells.Add(new WeatherSurfaceCellDelta { LocalPosition = Vector2Int.zero, SnowChanged = true,
            Edited = captured.Edited, Depth = captured.Depth, SeasonalDepth = captured.SeasonalDepth,
            WeatherDepth = captured.WeatherDepth, IceMeltProgress = captured.IceMeltProgress });
        payload = MemoryPackSerializer.Deserialize<WeatherSurfaceDelta>(MemoryPackSerializer.Serialize(payload));
        payload.Validate(1, 1);
        float wireProgress = payload.Cells[0].IceMeltProgress;
        payload.Cells[0].IceMeltProgress = float.NaN;
        bool badWireRejected = false;
        try { payload.Validate(1, 1); } catch (InvalidOperationException) { badWireRejected = true; }
        restored.IceMeltProgress = 1.1f;
        bool badSaveRejected = false;
        try { WorldSnowInteraction.Restore(terrain, restored); } catch (InvalidOperationException) { badSaveRejected = true; }
        return new JObject { ["saveProgress"] = captured.IceMeltProgress, ["saveDepth"] = captured.Depth,
            ["saveEdited"] = captured.Edited, ["wireProgress"] = wireProgress,
            ["badWireRejected"] = badWireRejected, ["badSaveRejected"] = badSaveRejected };
    }
    #endregion
}
