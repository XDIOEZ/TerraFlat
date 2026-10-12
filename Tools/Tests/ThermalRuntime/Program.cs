using UnityEngine;

internal static class Program
{
    #region 测试入口与断言

    private static int assertions;

    private static void Main()
    {
        AmbientExamples();
        HeatSourceExamples();
        TimePartitioning();
        NumericBoundaries();
        ActorAndItemIntegration();
        WorldContactAndCombustion();
        WorldScheduling();
        Console.WriteLine($"PASS {assertions} checks: ambient heat transfer, distance-weighted heat-source targets, overlap limits, actor/item integration, lava contact and world-item schedulers.");
    }

    private static void Expect(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Near(float actual, float expected, string message, float tolerance = 0.0001f)
        => Expect(float.IsFinite(actual) && Math.Abs(actual - expected) <= tolerance,
            $"{message}: expected {expected:R}, got {actual:R} (tolerance {tolerance:R}).");

    private static ItemData Material(float temperature, string id = "material") => new()
    {
        IDName = id,
        MatterState = new ItemMatterState { Initialized = true, TemperatureCelsius = temperature },
        Stack = new ItemStack { Amount = 5f }
    };

    #endregion

    #region 指数传热与步长

    private static void AmbientExamples()
    {
        Near(ThermalRuntime.AdvanceAmbient(20f, 120f, 1f), 21f, "One second of heating");
        Near(ThermalRuntime.AdvanceAmbient(120f, 20f, 1f), 119f, "One second of cooling");
        Near(ThermalRuntime.AdvanceAmbient(20f, -80f, 1f), 19f, "Negative ambient cooling");
        Near(ThermalRuntime.AdvanceAmbient(20f, 120f, 10f), 29.5617925f, "Ten seconds of heating");
        Near(ThermalRuntime.AdvanceAmbient(120f, 20f, 10f), 110.43821f, "Ten seconds of cooling");
        Near(ThermalRuntime.AdvanceAmbient(20f, 20f, 10f), 20f, "No temperature difference");
        Near(ThermalRuntime.AdvanceAmbient(20f, 120f, 0f), 20f, "Zero time");
        Near(ThermalRuntime.AdvanceAmbient(20f, 120f, -1f), 20f, "Negative time");
        Near(ThermalRuntime.AdvanceAmbient(20f, 120f, 2.5f) - 20f,
            120f - ThermalRuntime.AdvanceAmbient(120f, 20f, 2.5f), "Heating/cooling symmetry");
        Near(ThermalRuntime.AdvanceAmbient(-80f, 20f, 2.5f) + 80f,
            20f - ThermalRuntime.AdvanceAmbient(20f, -80f, 2.5f), "Negative/positive symmetry");
        Near(ThermalRuntime.AdvanceAmbient(1020f, 1120f, 1f), 1021f,
            "Absolute ambient temperature must not control rate");
        Near(ThermalRuntime.AdvanceAmbient(20f, 21f, 1f), 20.01f,
            "Small difference must not use a minimum one degree per second");
    }

    private static void HeatSourceExamples()
    {
        float full = ThermalInfluenceRuntime.DistanceWeight(0f, 36f);
        float middle = ThermalInfluenceRuntime.DistanceWeight(9f, 36f);
        Near(full, 1f, "Heat source center must have full influence");
        Near(middle, 0.5625f, "Heat source distance uses quadratic falloff");
        Near(ThermalInfluenceRuntime.DistanceWeight(36f, 36f), 0f, "Heat source radius boundary");

        foreach ((float ambient, float expected) in new[] { (-50f, 40f), (20f, 40f), (50f, 50f) })
        {
            float heating = 0f, cooling = 0f;
            ThermalInfluenceRuntime.Accumulate(ambient, 40f, full, ref heating, ref cooling);
            Near(ThermalInfluenceRuntime.Compose(ambient, heating, cooling), expected,
                "Campfire must help in cold climates but not heat hotter deserts");
            ThermalInfluenceRuntime.Accumulate(ambient, 40f, full, ref heating, ref cooling);
            Near(ThermalInfluenceRuntime.Compose(ambient, heating, cooling), expected,
                "Identical heat sources must not stack");
        }

        float heat = 0f, cold = 0f;
        ThermalInfluenceRuntime.Accumulate(-50f, 40f, middle, ref heat, ref cold);
        Near(ThermalInfluenceRuntime.Compose(-50f, heat, cold), 0.625f,
            "Snowy area must approach heat source target with distance falloff");
        heat = cold = 0f;
        ThermalInfluenceRuntime.Accumulate(20f, 40f, 1f, ref heat, ref cold);
        ThermalInfluenceRuntime.Accumulate(20f, 60f, 0.25f, ref heat, ref cold);
        Near(ThermalInfluenceRuntime.Compose(20f, heat, cold), 40f,
            "Overlapping sources must choose highest effective heating, not highest nominal target");
        heat = cold = 0f;
        ThermalInfluenceRuntime.Accumulate(-50f, 60f, 0.5f, ref heat, ref cold);
        Near(ThermalInfluenceRuntime.Compose(-50f, heat, cold), 5f,
            "Half-powered heater must apply half of target temperature difference");
        heat = cold = 0f;
        ThermalInfluenceRuntime.Accumulate(50f, 10f, full, ref heat, ref cold, cooling: true);
        Near(ThermalInfluenceRuntime.Compose(50f, heat, cold), 10f,
            "Cooling devices use the same falloff toward a lower target");
        heat = cold = 0f;
        ThermalInfluenceRuntime.Accumulate(-50f, 10f, full, ref heat, ref cold, cooling: true);
        Near(ThermalInfluenceRuntime.Compose(-50f, heat, cold), -50f,
            "Cooling devices must not warm colder surroundings");
        heat = cold = 0f;
        ThermalInfluenceRuntime.Accumulate(-50f, 180f, full, ref heat, ref cold);
        Near(ThermalInfluenceRuntime.Compose(-50f, heat, cold), 180f,
            "Lava surface heats toward ambient target, not a fixed additive offset");
    }

    private static void TimePartitioning()
    {
        foreach (float total in new[] { 10f, 100f })
        foreach ((float current, float ambient) in new[] { (20f, 120f), (120f, 20f), (20f, -80f) })
        foreach (float tick in new[] { 0.016f, 0.25f, 1f, 10f })
        {
            float direct = ThermalRuntime.AdvanceAmbient(current, ambient, total);
            float partitioned = current;
            var item = Material(current);
            // 最后一小步补齐总时长，避免把调度时钟误差算到公式上。
            double elapsed = 0d;
            while (elapsed < total)
            {
                float seconds = (float)Math.Min(tick, total - elapsed);
                partitioned = ThermalRuntime.AdvanceAmbient(partitioned, ambient, seconds);
                ItemMatterRuntime.Advance(item, ambient, 1f, seconds);
                elapsed += seconds;
            }
            Near(partitioned, direct, $"Core partition total={total}, tick={tick}", 0.005f);
            Near(item.MatterState.TemperatureCelsius, direct,
                $"Item partition total={total}, tick={tick}", 0.005f);
        }

        float combined = ThermalRuntime.AdvanceAmbient(20f, 120f, 10f);
        float split = ThermalRuntime.AdvanceAmbient(ThermalRuntime.AdvanceAmbient(20f, 120f, 3f), 120f, 7f);
        Near(split, combined, "Accumulated scheduling intervals must preserve elapsed time");
    }

    #endregion

    #region 数值安全与收敛

    private static void NumericBoundaries()
    {
        float[] values = { float.MinValue, -100000f, -80f, 0f, 20f, 120f, 100000f, float.MaxValue };
        float[] times = { float.Epsilon, 0.016f, 1f, 10f, 100000f, float.MaxValue };
        foreach (float current in values)
        foreach (float ambient in values)
        foreach (float seconds in times)
        {
            float result = ThermalRuntime.AdvanceAmbient(current, ambient, seconds);
            Expect(float.IsFinite(result), "Finite temperatures and time must produce a finite temperature");
            Expect(result >= Math.Min(current, ambient) && result <= Math.Max(current, ambient),
                "Ambient transfer must not overshoot either endpoint");
        }

        foreach (float invalid in new[] { float.NaN, float.NegativeInfinity, float.PositiveInfinity })
        {
            Near(ThermalRuntime.AdvanceAmbient(20f, invalid, 1f), 20f,
                "Invalid ambient temperature must not corrupt finite current state");
            Near(ThermalRuntime.AdvanceAmbient(20f, 120f, invalid), 20f,
                "Invalid time must not corrupt finite current state");
            Expect(float.IsFinite(ThermalRuntime.AdvanceAmbient(invalid, 120f, 1f)),
                "Invalid current temperature must recover to a finite state");
            var item = Material(20f);
            Expect(!ItemMatterRuntime.Advance(item, 120f, 1f, invalid), "Invalid item time must report no change");
            Near(item.MatterState.TemperatureCelsius, 20f, "Invalid item time must preserve temperature");
            Expect(!ItemMatterRuntime.Advance(item, invalid, 1f, 1f), "Invalid item ambient must report no change");
            Near(item.MatterState.TemperatureCelsius, 20f, "Invalid item ambient must preserve temperature");
        }

        Near(ThermalRuntime.AdvanceAmbient(0f, 0.0001f, 1f), 0.0001f,
            "Tiny remaining difference must converge", 0f);
        Near(ThermalRuntime.AdvanceAmbient(0.0001f, 0f, 1f), 0f,
            "Tiny cooling difference must converge", 0f);
        var converged = Material(0f);
        Expect(ItemMatterRuntime.Advance(converged, 0.0001f, 1f, 1f), "Final convergence must report a change");
        Near(converged.MatterState.TemperatureCelsius, 0.0001f, "Item convergence", 0f);
        for (int i = 0; i < 10; i++)
            Expect(!ItemMatterRuntime.Advance(converged, 0.0001f, 1f, 1f),
                "Converged items must not keep producing state-change notifications");

        Near(ThermalRuntime.AdvanceTowards(20f, 120f, 25f, 1f), 45f,
            "Dedicated constant-rate mechanisms must retain their rate");
        Near(ThermalRuntime.AdvanceTowards(20f, 25f, 25f, 1f), 25f,
            "Dedicated constant-rate mechanisms must retain their target clamp");
    }

    #endregion

    #region 角色与物品入口

    private static void ActorAndItemIntegration()
    {
        var manager = new TemperatureMgr();
        foreach ((float current, float ambient) in new[] { (20f, 120f), (120f, 20f), (20f, -80f) })
        foreach (float seconds in new[] { 0.016f, 0.25f, 1f, 10f })
        {
            var actor = new Mod_Temperature.TemperatureData { CurrentTemperature = current, AmbientTemperature = ambient };
            float expected = ThermalRuntime.AdvanceAmbient(current, ambient, seconds);
            Near(manager.EvaluateNextTemperature(actor, seconds), expected, "Actor must delegate to common formula");
            foreach (float exposure in new[] { 0f, 0.01f, 1f, 10f })
            {
                var item = Material(current);
                ItemMatterRuntime.Advance(item, ambient, exposure, seconds);
                Near(item.MatterState.TemperatureCelsius, expected, "Ordinary item exposure must not change transfer rate");
            }
        }

        var buffedActor = new Mod_Temperature.TemperatureData { CurrentTemperature = 80f, AmbientTemperature = 120f };
        Near(manager.EvaluateNextTemperature(buffedActor, 1f, naturalTemperature: 20f), 21f,
            "Temporary Buff temperature must not enter natural-temperature feedback");
        var safeRange = new Mod_Temperature.TemperatureData
        {
            SafeTemperatureMin = 5f, SafeTemperatureMax = 40f,
            DangerTemperatureBufferSeconds = 60f, RemainingTemperatureBufferSeconds = 30f
        };
        manager.NormalizeData(safeRange);
        Near(safeRange.SafeTemperatureMin, 5f, "Safe range minimum must remain intact");
        Near(safeRange.SafeTemperatureMax, 40f, "Safe range maximum must remain intact");
        Near(safeRange.RemainingTemperatureBufferSeconds, 30f, "Temperature danger buffer must remain intact");

        var normal = Material(20f);
        ItemMatterRuntime.Advance(normal, 120f, 1f, 1f, transferPerSecondOverride: 25f);
        Near(normal.MatterState.TemperatureCelsius, 45f, "Explicit device override must retain constant-rate transfer");
        GameRes.ExistingInstance = new GameRes();
        GameRes.ExistingInstance.Definitions["wet"] = new RuntimeItemDefinition
        {
            Matter = ItemMatterReactionCompiler.CompileMatter(new ItemMatterDefinitionDto
            {
                InitialMoisture = 0.5f, WaterAbsorptionRatePerSecond = 0.1f,
                EvaporationRatePerSecond = 0.1f, EvaporationStartTemperature = 0f,
                TemperatureDryingMultiplierPer10C = 0f
            }, Array.Empty<string>(), "wet")
        };
        var drying = Material(20f, "wet");
        drying.MatterState.Moisture = 0.5f;
        ItemMatterRuntime.Advance(drying, 120f, 0f, 1f);
        Near(drying.MatterState.TemperatureCelsius, 21f, "Zero drying exposure must still allow ambient transfer");
        Near(drying.MatterState.Moisture, 0.5f, "Exposure must retain its drying responsibility");
        ItemMatterRuntime.Advance(drying, 120f, 1f, 1f, submerged: true);
        Near(drying.MatterState.Moisture, 0.6f, "Water absorption must remain effective");
    }

    #endregion

    #region 岩浆接触与材料燃烧

    private static void WorldContactAndCombustion()
    {
        TemperatureMgr.Instance = new TemperatureMgr();
        TemperatureMgr.SampleAmbient = 120f;
        WorldLiquidSourceResolver.Settings = new WorldLiquidSettings
        {
            Temperature = 1000f, ContactHeatingPerSecond = 50f, WaterContact = false
        };
        var ordinary = Material(20f);
        WorldItemMatterRules.Advance(ordinary, default, 1f, liquidContact: false, out bool water);
        Near(ordinary.MatterState.TemperatureCelsius, 21f, "World items must default to exponential environment transfer");
        Expect(!water, "Air exposure is not water contact");
        var lava = Material(20f);
        WorldItemMatterRules.Advance(lava, default, 1f, liquidContact: true, out water);
        Near(lava.MatterState.TemperatureCelsius, 70f, "Lava contact must use its dedicated heating rate");
        Expect(!water, "Lava must not extinguish as water");

        var matter = ItemMatterReactionCompiler.CompileMatter(new ItemMatterDefinitionDto { IgnitionTemperature = 100f },
            Array.Empty<string>(), "burnable");
        GameRes.ExistingInstance.Definitions["burnable"] = new RuntimeItemDefinition { Matter = matter };
        var burning = Material(99f, "burnable");
        Expect(!WorldItemMatterRules.CanIgnite(burning), "Below ignition temperature must remain unlit");
        ItemMatterRuntime.AdvanceCombustion(burning, 1f, false, out bool consumed);
        Expect(!burning.MatterState.IsBurning && !consumed, "Ambient change must not bypass ignition threshold");
        burning.MatterState.TemperatureCelsius = 100f;
        Expect(WorldItemMatterRules.CanIgnite(burning), "Ignition boundary must remain inclusive");
        ItemMatterRuntime.AdvanceCombustion(burning, 1f, false, out consumed);
        Expect(burning.MatterState.IsBurning && !consumed, "Material at ignition temperature must burn");
        ItemMatterRuntime.AdvanceCombustion(burning, 11f, false, out consumed);
        Near(burning.Stack.Amount, 4f, "Normal combustion must consume one unit after 12 seconds");
        WorldLiquidSourceResolver.Settings = new WorldLiquidSettings { Temperature = 20f, WaterContact = true };
        WorldItemMatterRules.Advance(burning, default, 1f, true, out water);
        Expect(water, "Actual water contact must be reported to combustion");
        ItemMatterRuntime.AdvanceCombustion(burning, 1f, water, out consumed);
        Expect(!burning.MatterState.IsBurning && burning.MatterState.CombustionElapsedSeconds == 0f && !consumed,
            "Water must extinguish material and clear combustion time");
        Near(burning.Stack.Amount, 4f, "Extinguishing must not consume material");
    }

    #endregion

    #region 完整 Item 与轻量掉落物分帧调度

    private static Item WorldItem(float temperature)
    {
        var data = Material(temperature);
        data.Stack.CanBePickedUp = true;
        return new Item { itemData = data };
    }

    private static void WorldScheduling()
    {
        TemperatureMgr.SampleAmbient = 120f;
        WorldLiquidSourceResolver.Settings = null;
        var fullRuntime = new ItemMgr();
        var full = WorldItem(20f);
        fullRuntime.TestRegister(full);
        var lightRuntime = new DroppedItemRuntime();
        var light = Material(20f);
        lightRuntime.TestAdd(1, light);
        fullRuntime.TestTick(1f);
        lightRuntime.TestTick(1f);
        Near(full.itemData.MatterState.TemperatureCelsius, 21f, "Full item must count time from registration");
        Near(light.MatterState.TemperatureCelsius, 21f, "Lightweight item must count time from addition");
        foreach (float invalid in new[] { 0f, -1f, float.NaN, float.NegativeInfinity, float.PositiveInfinity })
        {
            fullRuntime.TestTick(invalid);
            lightRuntime.TestTick(invalid);
            Expect(fullRuntime.TestClock == 1d, "Invalid full-item frame time must not advance or corrupt clock");
            Expect(lightRuntime.TestClock == 1d, "Invalid lightweight frame time must not advance or corrupt clock");
        }
        fullRuntime.TestTick(9f);
        lightRuntime.TestTick(9f);
        Near(full.itemData.MatterState.TemperatureCelsius, 29.5617925f,
            "Full item must account for an accumulated nine-second scheduling gap");
        Near(light.MatterState.TemperatureCelsius, full.itemData.MatterState.TemperatureCelsius,
            "Both world backends must agree after the same accumulated time");

        var laterFull = WorldItem(20f);
        fullRuntime.TestRegister(laterFull);
        var laterLight = Material(20f);
        lightRuntime.TestAdd(2, laterLight);
        fullRuntime.TestTick(1f);
        lightRuntime.TestTick(1f);
        Near(laterFull.itemData.MatterState.TemperatureCelsius, 21f, "New full item must not inherit earlier world time");
        Near(laterLight.MatterState.TemperatureCelsius, 21f, "New lightweight item must not inherit earlier world time");

        const int batchSize = 192;
        var fullBatch = new ItemMgr();
        var lightBatch = new DroppedItemRuntime();
        var fullItems = new Item[batchSize];
        var lightItems = new ItemData[batchSize];
        for (int i = 0; i < batchSize; i++)
        {
            fullItems[i] = WorldItem(20f);
            fullBatch.TestRegister(fullItems[i]);
            lightItems[i] = Material(20f);
            lightBatch.TestAdd(i, lightItems[i]);
        }
        fullBatch.TestTick(1f);
        lightBatch.TestTick(1f);
        // 使用真正的每步 64 项和时长预算，让末尾物品跨帧处理。
        int steps = 0;
        while ((fullBatch.TestSettledTime(fullItems[^1]) <= 0d || lightBatch.TestSettledTime(batchSize - 1) <= 0d) &&
               steps++ < 1000)
        {
            fullBatch.TestTick(0.016f);
            lightBatch.TestTick(0.016f);
        }
        Expect(steps < 1000, "Both schedulers must eventually process the delayed batch tail");
        Expect(fullBatch.TestSettledTime(fullItems[^1]) > 1d, "Full-item batch tail must be delayed by the 64-item budget");
        Expect(lightBatch.TestSettledTime(batchSize - 1) > 1d, "Lightweight batch tail must be delayed by the 64-item budget");
        for (int i = 0; i < batchSize; i++)
        {
            Near(fullItems[i].itemData.MatterState.TemperatureCelsius,
                ThermalRuntime.AdvanceAmbient(20f, 120f, (float)fullBatch.TestSettledTime(fullItems[i])),
                "Full-item split processing must include all elapsed time");
            Near(lightItems[i].MatterState.TemperatureCelsius,
                ThermalRuntime.AdvanceAmbient(20f, 120f, (float)lightBatch.TestSettledTime(i)),
                "Lightweight split processing must include all elapsed time");
        }

        var stableFull = new ItemMgr();
        var stableItem = WorldItem(120f);
        stableFull.TestRegister(stableItem);
        var stableLight = new DroppedItemRuntime();
        stableLight.TestAdd(1, Material(120f));
        int networkBefore = FlatWorld.Networking.ItemNetworkStateSerialization.Notifications;
        for (int i = 0; i < 5; i++)
        {
            stableFull.TestTick(1f);
            stableLight.TestTick(1f);
        }
        Expect(FlatWorld.Networking.ItemNetworkStateSerialization.Notifications == networkBefore,
            "Converged full world items must not request repetitive network state notifications");
        Expect(stableLight.TestNotifications == 0,
            "Converged lightweight items must not request repetitive presentation changes");
    }

    #endregion
}
