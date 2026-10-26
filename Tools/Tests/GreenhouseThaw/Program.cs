using System;
using FlatWorld.WorldModel;

internal static class Program
{
    #region 温室与冰雪生产规则离线回归

    private static int checks;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }

    private static void Near(double actual, double expected, string message, double tolerance = .00001)
        => Check(Math.Abs(actual - expected) <= tolerance, message + $": actual={actual}, expected={expected}");

    private static void Main()
    {
        ValidateGreenhouseOffsetBoundaries();
        ValidateSnowMassAndContinuousThaw();
        ValidateIceProgressAndFrozenWaterIdentity();
        Console.WriteLine($"PASS: {checks} production greenhouse/thaw checks; no Unity runtime or visual claims.");
    }

    private static void ValidateGreenhouseOffsetBoundaries()
    {
        // 直接调用正式分压规则，所有预期值独立固定在验收中。
        Near(AtmosphericGreenhouseTemperatureRules.ResolveTemperatureOffset(0, 0), 0, "vacuum with zero baseline");
        Near(AtmosphericGreenhouseTemperatureRules.ResolveTemperatureOffset(.04, 0), 3, "first pressure-scale warming");
        Near(AtmosphericGreenhouseTemperatureRules.ResolveTemperatureOffset(.12, .04), 3, "effective optical column doubles");
        Near(AtmosphericGreenhouseTemperatureRules.ResolveTemperatureOffset(.04, .04), 0, "unchanged atmosphere baseline");
        Near(AtmosphericGreenhouseTemperatureRules.ResolveTemperatureOffset(0, .04), -3, "removing baseline carbon dioxide cools");
        Near(AtmosphericGreenhouseTemperatureRules.ResolveTemperatureOffset(1e200, 0), 60, "maximum warming clamp");
        Near(AtmosphericGreenhouseTemperatureRules.ResolveTemperatureOffset(0, 1e200), -30, "maximum cooling clamp");
        double previous = double.NegativeInfinity;
        for (int step = 0; step <= 100; step++)
        {
            float offset = AtmosphericGreenhouseTemperatureRules.ResolveTemperatureOffset(step * .001, .04);
            Check(float.IsFinite(offset) && offset >= previous, "warming monotonic with actual carbon dioxide column");
            previous = offset;
        }
        float first = AtmosphericGreenhouseTemperatureRules.ResolveTemperatureOffset(.08, 0);
        for (int step = 0; step < 20; step++)
            Near(AtmosphericGreenhouseTemperatureRules.ResolveTemperatureOffset(.08, 0), first, "repeated reads never accumulate warming");
        Console.WriteLine("PASS: greenhouse baseline, warming/cooling, monotonicity and bounds.");
    }

    private static WorldSurfaceThawRules.SnowStep Snow(float natural, bool edited, float depth,
        float baseline, float seasonal, float weather, float temperature, float seconds, float precipitation = 0f)
        => WorldSurfaceThawRules.AdvanceSnow(natural, edited, depth, baseline, seasonal, weather,
            temperature, 0f, seconds, precipitation, .0003f, .00012f);

    private static float EffectiveSnow(WorldSurfaceThawRules.SnowStep step, float natural, float seasonal)
        => step.Edited ? Math.Max(0f, step.Depth + Math.Clamp(seasonal + step.WeatherDepth, 0f, 1f) - step.SeasonalBaseline)
            : Math.Max(natural, Math.Clamp(seasonal + step.WeatherDepth, 0f, 1f));

    private static void ValidateSnowMassAndContinuousThaw()
    {
        foreach (float cold in new[] { -20f, 0f })
        {
            var frozen = Snow(1f, false, 0f, 0f, 0f, 0f, cold, 100f);
            Check(!frozen.Edited, "freezing natural snow stays unedited");
            Near(frozen.Melted, 0, "freezing natural snow produces no water");
            Near(EffectiveSnow(frozen, 1f, 0f), 1, "freezing natural snow preserved");
        }
        var fullStep = Snow(1f, false, 0f, 0f, 0f, 0f, 10f, 100f);
        Check(fullStep.Edited, "natural thaw becomes saved continuous snow");
        Near(fullStep.Depth, .88, "natural snow warms at configured rate");
        Near(fullStep.Melted, .12, "natural snow melt mass");
        var split = Snow(1f, false, 0f, 0f, 0f, 0f, 10f, 10f);
        for (int step = 1; step < 10; step++)
            split = Snow(1f, split.Edited, split.Depth, split.SeasonalBaseline, 0f, split.WeatherDepth, 10f, 10f);
        Near(split.Depth, fullStep.Depth, "split-step natural thaw preserves continuous mass");
        var tiny = Snow(.0001f, false, 0f, 0f, 0f, 0f, 1f, .1f);
        Check(tiny.Depth > 0 && tiny.Depth < .0001f, "sub-display-layer snow still melts continuously");
        Near(tiny.Depth + tiny.Melted, .0001f, "sub-layer snow mass conserved");
        var melted = Snow(1f, false, 0f, 0f, 0f, 0f, 10f, 1000f);
        Near(melted.Depth, 0, "natural snow fully thaws");
        var recooled = Snow(1f, melted.Edited, melted.Depth, melted.SeasonalBaseline, 0f, melted.WeatherDepth, -10f, 100f);
        Near(EffectiveSnow(recooled, 1f, 0f), 0, "cold does not revive removed natural snow");
        var remelted = Snow(1f, recooled.Edited, recooled.Depth, recooled.SeasonalBaseline, 0f, recooled.WeatherDepth, 10f, 100f);
        Near(remelted.Melted, 0, "already removed natural snow does not create water twice");
        var newSnow = Snow(1f, true, 0f, 0f, 0f, 0f, -10f, 100f, 100f);
        Near(newSnow.WeatherDepth, .03, "new precipitation can snow after thaw");
        var seasonal = Snow(0f, true, 0f, 0f, .5f, 0f, 10f, 100f);
        Near(EffectiveSnow(seasonal, 0f, .5f), .38, "seasonal increment can actually melt");
        Near(seasonal.Melted, .12, "seasonal increment water equals removed mass");
        Near(seasonal.SeasonalBaseline, .5, "warm seasonal snow is rebased into saved state");
        var mixed = Snow(.8f, false, 0f, 0f, .2f, .4f, 10f, 100f);
        Near(EffectiveSnow(mixed, .8f, .2f), .68, "natural and transient overlap is not double-counted");
        var player = Snow(0f, true, 2.3f, 0f, 0f, .2f, 10f, 100f);
        Near(EffectiveSnow(player, 0f, 0f), 2.38, "player snow above ten layers retains actual volume");
        var saturatedTransient = Snow(0f, true, .2f, 0f, .8f, .8f, 10f, 100f);
        Near(EffectiveSnow(saturatedTransient, 0f, .8f), 1.08, "seasonal/weather transient clamp matches surface snow query");
        Near(saturatedTransient.Melted, .12, "overlapping transient snow does not create extra melt water");
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity })
            Near(Snow(1f, true, .4f, 0f, 0f, .2f, invalid, 10f).Depth, .4, "invalid temperature does not change snow");
        Console.WriteLine("PASS: natural/player/seasonal snow thaw, tiny layers, conservation and no revival.");
    }

    private static void ValidateIceProgressAndFrozenWaterIdentity()
    {
        foreach (float cold in new[] { -20f, 0f })
            Near(WorldSurfaceThawRules.AdvanceIceMeltProgress(.3f, cold, 100f, .00002f), .3, "ice freezing boundary");
        float total = WorldSurfaceThawRules.AdvanceIceMeltProgress(.1f, 10f, 500f, .00002f);
        float split = .1f;
        for (int step = 0; step < 50; step++)
            split = WorldSurfaceThawRules.AdvanceIceMeltProgress(split, 10f, 10f, .00002f);
        Near(total, .2, "ice warms progressively");
        Near(split, total, "ice split-step invariance");
        Near(WorldSurfaceThawRules.AdvanceIceMeltProgress(.9f, 100f, 10000f, .00002f), 1, "ice completion clamps to one");
        var types = LiquidTypeCatalog.BuiltIn;
        foreach (string id in new[] { LiquidTypeCatalog.DirtyWaterId, LiquidTypeCatalog.SeaWaterId })
        foreach (int level in new[] { 1, 37, 100 })
        {
            var bed = new TerrainCell(4, 5, 0, 8, 12, TerrainCellFlags.Walkable);
            var source = new FrozenSurfaceWaterSource(bed, 22, types.GetIndex(id), level);
            using var buffer = new ChunkTerrainBuffer(2, 1, types);
            buffer.SetCell(0, 0, new TerrainCell(22, 0, 0, 8, 10, TerrainCellFlags.Walkable));
            FrozenSurfaceWaterLayer.WriteGenerationSource(buffer, 0, source);
            using var terrain = buffer.Seal();
            Check(FrozenSurfaceWaterLayer.TryReadSource(terrain, 0, 0, out var restored), "frozen source survives buffer handoff");
            Check(restored.Bed.Equals(bed) && restored.IceTileId == 22, "all original bed fields restored");
            Check(restored.LiquidLevel == level && types.GetId(restored.LiquidTypeIndex) == id, "salinity and exact liquid volume preserved");
            Check(!FrozenSurfaceWaterLayer.TryReadSource(terrain, 1, 0, out _), "ordinary land has no frozen-water source");
            Check(terrain.GetLiquidBaseline(0, 0).LiquidLevel == 0, "unmelted ice is not a water source");
            terrain.SetEnvironmentValue(FrozenSurfaceWaterLayer.MeltProgressLayer, 0, 0, .99f);
            Check(terrain.GetLiquidBaseline(0, 0).LiquidLevel == 0, "partially melted ice is not a pump source");
            terrain.SetEnvironmentValue(FrozenSurfaceWaterLayer.MeltProgressLayer, 0, 0, 1f);
            terrain.SetCell(0, 0, restored.Bed);
            terrain.SetLiquid(0, 0, restored.LiquidTypeIndex, restored.LiquidLevel);
            Check(terrain.IsLiquidChanged(0, 0), "thawed liquid remains a saved change from frozen generation");
            Check(terrain.GetLiquidBaseline(0, 0).Equals(new LiquidCellValue(restored.LiquidTypeIndex, level)), "thawed natural water participates in replenishment");
            terrain.SetLiquid(0, 0, 0, 0);
            Check(terrain.GetLiquidBaseline(0, 0).LiquidLevel == level, "pumped thawed lake retains its natural baseline");
            Check(!terrain.IsLiquidChanged(0, 0), "pumping back to original frozen liquid state keeps save delta semantics");
        }
        Console.WriteLine("PASS: ice thresholds/progress, salt/fresh water, bed identity and thawed source baseline.");
    }

    #endregion
}
