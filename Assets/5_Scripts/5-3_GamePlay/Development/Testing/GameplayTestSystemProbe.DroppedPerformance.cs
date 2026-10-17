using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using Unity.Mathematics;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 掉落物环境采样规则登记

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterDroppedPerformanceRules()
    {
        Register("environment", "wind_read_path", DroppedWindReadPath);
        Register("environment", "thermal_point_path", DroppedThermalPointPath);
    }

    // 独立天气引擎只返回正式单项风采样和完整快照，数值判断交给 Lua。
    private static JObject DroppedWindVector(float3 value) => new()
    {
        ["x"] = value.x, ["y"] = value.y, ["strength"] = value.z
    };

    private static JObject DroppedWindSnapshot(RegionalWeatherEngine engine, Int2 cell)
    {
        bool found = engine.TryGetSnapshot(cell, out RegionalWeatherSnapshot snapshot);
        JObject result = DroppedWindVector(new float3(snapshot.WindX, snapshot.WindY, snapshot.WindStrength));
        result["found"] = found;
        return result;
    }

    private static JObject DroppedWindBlend(RegionalWeatherEngine engine, double2 position)
    {
        bool found = engine.TryGetWindBlend(position, out RegionalWindBlend blend);
        return new JObject
        {
            ["found"] = found, ["origin"] = DroppedWindVector(blend.Origin),
            ["horizontal"] = DroppedWindVector(blend.Horizontal), ["vertical"] = DroppedWindVector(blend.Vertical),
            ["diagonal"] = DroppedWindVector(blend.Diagonal),
            ["horizontalWeight"] = blend.HorizontalWeight, ["verticalWeight"] = blend.VerticalWeight
        };
    }

    private static JObject DroppedWindReadPath(JObject args)
    {
        var settings = new RegionalWeatherSettings();
        var engine = new RegionalWeatherEngine(settings, 17413, default, 16, 16);
        var origin = new Int2(0, 0);
        engine.RegisterChunk(origin, settings.CreateProfile(new ChunkWeatherClimate(.5f, 3f, 4f, 1f, 20f)), 0d);
        engine.ForceElement(origin, 0d, WeatherElementIds.Wind, .9f, 100f);
        var result = new JObject
        {
            ["event"] = DroppedWindBlend(engine, new double2(64, 64)),
            ["eventSnapshot"] = DroppedWindSnapshot(engine, origin)
        };
        engine.ForceElements(origin, 0d, 10d, windStrength: 0f);
        result["forcedZero"] = DroppedWindBlend(engine, new double2(64, 64));
        result["forcedZeroSnapshot"] = DroppedWindSnapshot(engine, origin);
        result["forcedZeroHasPush"] = engine.HasWindStrengthAtLeast(.8f);
        engine.Advance(origin, 10d, false);
        result["restored"] = DroppedWindBlend(engine, new double2(64, 64));
        result["restoredSnapshot"] = DroppedWindSnapshot(engine, origin);
        result["restoredHasPush"] = engine.HasWindStrengthAtLeast(.8f);

        var randomDirection = new RegionalWeatherEngine(settings, 17413, default, 16, 16);
        randomDirection.RegisterChunk(origin, settings.CreateProfile(new ChunkWeatherClimate(.5f, 0f, 0f, 1f, 20f)), 0d);
        randomDirection.ForceElement(origin, 0d, WeatherElementIds.Wind, .8f, 100f);
        result["fallback"] = DroppedWindBlend(randomDirection, new double2(64, 64));
        result["fallbackSnapshot"] = DroppedWindSnapshot(randomDirection, origin);
        result["fallbackRepeated"] = DroppedWindBlend(randomDirection, new double2(64, 64));

        result["fourRegions"] = DroppedWindFourRegions(settings, default, 128);
        result["wrapped"] = DroppedWindFourRegions(settings,
            new WorldTopologyDomain(new int2(-128, -128), new int2(256, 256), true), -128);
        result["missingNeighbor"] = DroppedWindBlend(engine, new double2(127.5, 64));
        result["missingSnapshot"] = DroppedWindSnapshot(engine, new Int2(128, 0));
        result["invalidPosition"] = DroppedWindBlend(engine, new double2(double.NaN, 64));
        return result;
    }

    private static JObject DroppedWindFourRegions(RegionalWeatherSettings settings, WorldTopologyDomain domain,
        int neighbor)
    {
        var authority = new RegionalWeatherEngine(settings, 17413, domain, 16, 16);
        var replica = new RegionalWeatherEngine(settings, 17413, domain, 16, 16);
        Int2[] cells = { new(0, 0), new(neighbor, 0), new(0, neighbor), new(neighbor, neighbor) };
        float2[] directions = { new(1, 0), new(0, 1), new(-1, 0), new(0, -1) };
        float[] strengths = { .8f, .9f, 1f, .85f };
        var snapshots = new JArray();
        for (int i = 0; i < cells.Length; i++)
        {
            authority.RegisterChunk(cells[i], settings.CreateProfile(new ChunkWeatherClimate(.5f,
                directions[i].x, directions[i].y, 1f, 20f)), 0d);
            authority.ForceElement(cells[i], 0d, WeatherElementIds.Wind, strengths[i], 100f);
            authority.ForcePrecipitation(cells[i], 0d, .6f, 80f);
            authority.ForceElement(cells[i], 0d, WeatherElementIds.Thunder, .7f, 60f);
            authority.TryGetSnapshot(cells[i], out RegionalWeatherSnapshot snapshot);
            replica.ApplyReplicatedSnapshot(snapshot);
            snapshots.Add(DroppedWindSnapshot(authority, cells[i]));
        }
        return new JObject
        {
            ["snapshots"] = snapshots,
            ["authority"] = DroppedWindBlend(authority, new double2(127.5, 127.5)),
            ["replica"] = DroppedWindBlend(replica, new double2(127.5, 127.5)),
            ["wrappedEquivalent"] = domain.IsWrapped
                ? DroppedWindBlend(authority, new double2(-128.5, -128.5)) : null
        };
    }

    #endregion

    #region 单格与批量热场正式采样

    private static JObject DroppedLocalTemperature(LocalTemperatureField field, Vector2Int cell)
    {
        const int size = 4;
        float heating = 0f, cooling = 0f;
        field.AccumulateAtPoint(cell, 20f, ref heating, ref cooling);
        float point = ThermalInfluenceRuntime.Compose(20f, heating, cooling);
        Vector2Int origin = new(Mathf.FloorToInt(cell.x / (float)size) * size,
            Mathf.FloorToInt(cell.y / (float)size) * size);
        int index = (cell.y - origin.y) * size + cell.x - origin.x;
        heating = cooling = 0f;
        LocalTemperatureField.Accumulate(field.GetChunkInfluences(origin), index, 20f, ref heating, ref cooling);
        return new JObject { ["point"] = point, ["batch"] = ThermalInfluenceRuntime.Compose(20f, heating, cooling) };
    }

    private static JObject DroppedThermalPointPath(JObject args)
    {
        var field = new LocalTemperatureField(4, 4, default);
        var owner = new object();
        field.SetSource(owner, new Vector2(.5f, .5f), 4f, 100f);
        var local = new JObject { ["initial"] = DroppedLocalTemperature(field, Vector2Int.zero) };
        field.SetSource(owner, new Vector2(.5f, .5f), 4f, 80f);
        local["targetChanged"] = DroppedLocalTemperature(field, Vector2Int.zero);
        field.SetSource(owner, new Vector2(.5f, .5f), 4f, 80f, strength: .5f);
        local["strengthChanged"] = DroppedLocalTemperature(field, Vector2Int.zero);
        field.SetSource(owner, new Vector2(2.5f, .5f), 4f, 80f, strength: .5f);
        local["movedOld"] = DroppedLocalTemperature(field, Vector2Int.zero);
        local["movedNew"] = DroppedLocalTemperature(field, new Vector2Int(2, 0));
        field.SetSource(owner, new Vector2(2.5f, .5f), 1f, 80f, strength: .5f);
        local["radiusChanged"] = DroppedLocalTemperature(field, Vector2Int.zero);
        field.RemoveSource(owner);
        local["removed"] = DroppedLocalTemperature(field, new Vector2Int(2, 0));
        field.SetSource(owner, new Vector2(.5f, .5f), 4f, -20f, cooling: true);
        local["cooling"] = DroppedLocalTemperature(field, Vector2Int.zero);
        field.SetSource(owner, new Vector2(.5f, .5f), 4f, -20f);
        local["heatingOnly"] = DroppedLocalTemperature(field, Vector2Int.zero);

        bool wrappedReady = WorldTopologyBounds.TryCreate(new PlanetData
        { Radius = 8, ChunkSize = new Vector2Int(4, 4), TopologyMode = WorldTopologyMode.Wrapped }, out WorldTopologyBounds bounds);
        var wrappedField = new LocalTemperatureField(4, 4, bounds);
        wrappedField.SetSource(owner, new Vector2(7.5f, .5f), 4f, 100f);
        local["wrapped"] = DroppedLocalTemperature(wrappedField, new Vector2Int(-8, 0));

        return new JObject
        {
            ["localField"] = local, ["wrappedReady"] = wrappedReady,
            ["liquid"] = DroppedLiquidTemperature(bounds)
        };
    }

    private static ChunkTerrainData DroppedThermalTerrain(bool replacement = false)
    {
        using var buffer = new ChunkTerrainBuffer(4, 4);
        for (int y = 0; y < 4; y++)
        for (int x = 0; x < 4; x++)
            buffer.SetCell(x, y, new TerrainCell(1, 0, 0, 0, 1, TerrainCellFlags.Walkable));
        int lava = buffer.LiquidTypes.GetIndex(LiquidTypeCatalog.LavaId);
        buffer.SetLiquid(replacement ? 3 : 0, replacement ? 3 : 0, lava, 6);
        if (!replacement)
        {
            buffer.SetLiquid(2, 0, lava, 6);
            buffer.SetLiquid(0, 3, buffer.LiquidTypes.GetIndex(LiquidTypeCatalog.DirtyWaterId), 6);
        }
        return buffer.Seal();
    }

    private static JObject DroppedLiquidTemperature(WorldTopologyBounds bounds)
    {
        var definitions = new Dictionary<string, LiquidDefinition>(StringComparer.Ordinal)
        {
            [LiquidTypeCatalog.LavaId] = new(LiquidTypeCatalog.LavaId, "probe", "", "", "", Color.white,
                1f, false, 0f, null, null, worldWater: new WorldLiquidSettings
                { RadiantHeatRadius = 4f, RadiantTargetTemperature = 100f }),
            [LiquidTypeCatalog.DirtyWaterId] = new(LiquidTypeCatalog.DirtyWaterId, "probe", "", "", "", Color.white,
                1f, false, 0f, null, null, worldWater: new WorldLiquidSettings { RadiantHeatRadius = 0f })
        };
        using ChunkTerrainData first = DroppedThermalTerrain();
        using ChunkTerrainData replacement = DroppedThermalTerrain(true);
        var source = new LiquidTemperatureField.SourceChunk();
        source.Refresh(first, definitions);
        var result = new JObject
        {
            ["initial"] = DroppedLiquidChunk(source, Vector2Int.zero, Vector2Int.zero, default),
            ["initialRevision"] = first.SurfaceLiquidRevision,
            ["replacementRevision"] = replacement.SurfaceLiquidRevision
        };
        source.Refresh(replacement, definitions);
        result["sameRevisionRebound"] = DroppedLiquidChunk(source, Vector2Int.zero, Vector2Int.zero, default);
        source.Refresh(first, definitions);
        result["sameRevisionRestored"] = DroppedLiquidChunk(source, Vector2Int.zero, Vector2Int.zero, default);
        first.SetEnvironmentValue("moisture", 0, 0, .8f);
        source.Refresh(first, definitions);
        result["soilChanged"] = DroppedLiquidChunk(source, Vector2Int.zero, Vector2Int.zero, default);
        result["soilRevision"] = first.SurfaceLiquidRevision;
        TerrainSupportLayer.Set(first, 0, 0, 90, 2);
        source.Refresh(first, definitions);
        result["covered"] = DroppedLiquidChunk(source, Vector2Int.zero, Vector2Int.zero, default);
        result["coveredRevision"] = first.SurfaceLiquidRevision;
        TerrainSupportLayer.Set(first, 0, 0, 0, 2);
        source.Refresh(first, definitions);
        result["uncovered"] = DroppedLiquidChunk(source, Vector2Int.zero, Vector2Int.zero, default);
        first.SetLiquid(0, 0, 0, 0);
        first.SetLiquid(2, 0, 0, 0);
        source.Refresh(first, definitions);
        result["cleared"] = DroppedLiquidChunk(source, Vector2Int.zero, Vector2Int.zero, default);
        source.Refresh(null, definitions);
        result["unloaded"] = DroppedLiquidChunk(source, Vector2Int.zero, Vector2Int.zero, default);
        source.Refresh(replacement, definitions);
        result["rebound"] = DroppedLiquidChunk(source, Vector2Int.zero, Vector2Int.zero, default);
        result["wrapped"] = DroppedLiquidChunk(source, new Vector2Int(4, 0), new Vector2Int(-8, 0), bounds);
        replacement.Dispose();
        source.Refresh(replacement, definitions);
        result["disposed"] = DroppedLiquidChunk(source, Vector2Int.zero, Vector2Int.zero, default);
        return result;
    }

    // 同一稀疏液体源分别走单格和整块入口，夹具不复制距离或最大权重算法。
    private static JObject DroppedLiquidChunk(LiquidTemperatureField.SourceChunk source, Vector2Int sourceOrigin,
        Vector2Int targetOrigin, WorldTopologyBounds bounds)
    {
        var weights = new Dictionary<float, float[]>();
        source.AccumulateChunk(sourceOrigin, targetOrigin, 4, 4, bounds, weights);
        var influences = new LiquidTemperatureField.Influence[weights.Count];
        int influenceIndex = 0;
        foreach (KeyValuePair<float, float[]> influence in weights)
            influences[influenceIndex++] = new LiquidTemperatureField.Influence(influence.Key, influence.Value);
        var points = new JArray();
        var batch = new JArray();
        for (int y = 0; y < 4; y++)
        for (int x = 0; x < 4; x++)
        {
            float heating = 0f, cooling = 0f;
            source.AccumulateAtPoint(sourceOrigin, new Vector2(targetOrigin.x + x + .5f, targetOrigin.y + y + .5f),
                bounds, 20f, ref heating, ref cooling);
            points.Add(ThermalInfluenceRuntime.Compose(20f, heating, cooling));
            heating = cooling = 0f;
            LiquidTemperatureField.Accumulate(influences, y * 4 + x, 20f, ref heating, ref cooling);
            batch.Add(ThermalInfluenceRuntime.Compose(20f, heating, cooling));
        }
        return new JObject { ["point"] = points, ["batch"] = batch };
    }

    #endregion
}
