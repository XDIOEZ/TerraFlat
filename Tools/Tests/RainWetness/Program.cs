using FlatWorld.Networking;

internal static class Program
{
    private static int assertions;
    private static BuffDefinition wet;

    private static void Expect(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new Exception(message);
    }

    private static Mod_BuffManager Actor(bool player = false, string id = "Animal")
    {
        Item item = player ? new Player() : new Item();
        item.itemData.IDName = id;
        item.itemMods.Modules[ModText.Hp] = new Mod_DamageReceiver();
        var manager = new Mod_BuffManager { item = item, ModData = new() };
        manager.Awake();
        return manager;
    }

    private static int Stacks(Mod_BuffManager manager) => manager.GetBuffStacks(WetBuffIds.Wet);
    private static void ResetWorld()
    {
        GameNetwork.HasStateAuthority = true;
        WeatherMgr.ExistingInstance = new();
        GameRes.Instance.Buffs[WetBuffIds.Wet] = wet;
    }

    private static void Main(string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("Pass attribute_modifiers.json.");
        var catalog = BuffDefinitionFactory.Deserialize(File.ReadAllText(args[0]));
        foreach (var definition in BuffDefinitionFactory.BuildCatalog(catalog))
            GameRes.Instance.Buffs[definition.Id] = definition;
        wet = GameRes.Instance.GetBuffDefinition(WetBuffIds.Wet);
        Expect(wet.RainMaxStacks == 5 && wet.RainStackIntervalSeconds == 60f && wet.RainReferenceIntensity == 0.65f,
            "Production JSON must configure 60 seconds / 5 layers / 0.65 reference rain.");

        var animal = Actor();
        animal.Tick(59f);
        Expect(Stacks(animal) == 0, "Rain must not instantly grant a layer.");
        animal.Tick(1f);
        Expect(Stacks(animal) == 1, "First layer must appear at 60 seconds.");
        for (int layer = 2; layer <= 5; layer++)
        {
            animal.Tick(60f);
            Expect(Stacks(animal) == layer, $"Normal rain must reach layer {layer} at {layer * 60}s.");
        }
        animal.Tick(3600f);
        Expect(Stacks(animal) == 5, "Long rain must stay capped at 5 without decay.");

        var player = Actor(player: true, id: "Player");
        player.Tick(60f);
        Expect(Stacks(player) == 1, "Players and AI must use the same rain mechanic.");
        var lateAnimal = Actor();
        lateAnimal.Tick(30f);
        Expect(Stacks(lateAnimal) == 0, "New animals must not inherit another actor's clock.");
        lateAnimal.Tick(30f);
        Expect(Stacks(lateAnimal) == 1, "Each animal needs its own 60 seconds.");
        var building = Actor(id: "Building");
        building.Tick(600f);
        Expect(Stacks(building) == 0, "Non-actor items with HP/Buffs must not receive rain wetness.");

        var smallTicks = Actor();
        for (int i = 0; i < 599; i++) smallTicks.Tick(0.1f);
        Expect(Stacks(smallTicks) == 0, "0.1 second scheduling must not grant an early layer.");
        smallTicks.Tick(0.1f);
        Expect(Stacks(smallTicks) == 1, "Fixed interval float accumulation must reach 60 seconds.");
        for (int i = 0; i < 2400; i++) smallTicks.Tick(0.1f);
        Expect(Stacks(smallTicks) == 5, "Small ticks must agree with large ticks over 300 seconds.");

        ResetWorld();
        WeatherMgr.ExistingInstance.Intensity = 0.325f;
        var light = Actor();
        light.Tick(119f);
        Expect(Stacks(light) == 0, "Half-strength rain needs 120 seconds.");
        light.Tick(1f);
        Expect(Stacks(light) == 1, "Half-strength rain must reach a layer at 120 seconds.");
        WeatherMgr.ExistingInstance.Intensity = 1f;
        var heavy = Actor();
        heavy.Tick(38f);
        Expect(Stacks(heavy) == 0, "Heavy rain must not grant a layer before 39 seconds.");
        heavy.Tick(1f);
        Expect(Stacks(heavy) == 1, "Heavy rain must reach a layer at 39 seconds.");
        var mixed = Actor();
        WeatherMgr.ExistingInstance.Intensity = 0.325f;
        mixed.Tick(60f);
        WeatherMgr.ExistingInstance.Intensity = 1f;
        mixed.Tick(19.5f);
        Expect(Stacks(mixed) == 1, "Intensity changes must retain fractional exposure progress.");

        ResetWorld();
        var stopped = Actor();
        stopped.Tick(59f);
        WeatherMgr.ExistingInstance.Raining = false;
        stopped.Tick(1f);
        WeatherMgr.ExistingInstance.Raining = true;
        stopped.Tick(1f);
        Expect(Stacks(stopped) == 0, "A dry interval must reset incomplete rain exposure.");
        stopped.Tick(59f);
        Expect(Stacks(stopped) == 1, "Resumed rain must start a new exposure interval.");
        stopped.Tick(240f);
        WeatherMgr.ExistingInstance.Raining = false;
        stopped.Tick(30f);
        Expect(Stacks(stopped) == 4, "Rain stopping must resume one-layer-per-30s drying.");
        stopped.Tick(120f);
        Expect(Stacks(stopped) == 0, "The last wet layer must expire normally.");

        ResetWorld();
        var cleared = Actor();
        cleared.Tick(59f);
        cleared.ClearAllBuffs();
        cleared.Tick(1f);
        Expect(Stacks(cleared) == 0, "Respawn/clear must reset a clock even with zero active buffs.");
        cleared.Tick(58f);
        cleared.Unload();
        cleared.Tick(1f);
        Expect(Stacks(cleared) == 0, "Pool unload must reset incomplete rain exposure.");
        var dead = Actor();
        dead.Tick(59f);
        var health = dead.item.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
        health.Hp = 0f;
        dead.Tick(100f);
        health.Hp = 100f;
        dead.Tick(1f);
        Expect(Stacks(dead) == 0, "Dead actors must not accumulate or preserve exposure debt.");

        var client = Actor();
        GameNetwork.HasStateAuthority = false;
        client.Tick(600f);
        Expect(Stacks(client) == 0, "A network client must never apply rain stacks.");
        GameNetwork.HasStateAuthority = true;
        client.Tick(59f);
        Expect(Stacks(client) == 0, "Authority changes must not replay client time.");
        client.Tick(1f);
        Expect(Stacks(client) == 1, "Authority must enable ordinary actor-local exposure.");

        ResetWorld();
        var underground = Actor();
        WeatherMgr.ExistingInstance.Suppressed = true;
        underground.Tick(600f);
        Expect(Stacks(underground) == 0, "Weather-suppressed dimensions must not accumulate rain.");
        WeatherMgr.ExistingInstance.Suppressed = false;
        underground.Tick(1f);
        Expect(Stacks(underground) == 0, "Leaving a suppressed dimension must not replay rain time.");
        var snowy = Actor();
        snowy.item.transform.position.x = -10f;
        snowy.Tick(600f);
        var warm = Actor();
        warm.Tick(60f);
        Expect(Stacks(snowy) == 0 && Stacks(warm) == 1, "Rain/snow must use each actor's location, not a camera.");

        var invalid = Actor();
        invalid.Tick(59f);
        foreach (float dt in new[] { float.NaN, float.PositiveInfinity, -1f, 0f }) invalid.Tick(dt);
        Expect(Stacks(invalid) == 0, "Invalid elapsed time must not grant layers.");
        invalid.Tick(1f);
        Expect(Stacks(invalid) == 1, "Invalid ticks must not poison a valid clock.");

        var swimmer = Actor();
        swimmer.SetWaterStackExposure(true);
        swimmer.AdvanceWaterWetness(1f, 10f);
        Expect(Stacks(swimmer) == 10, "Water depth stacking must retain the existing ten-layer cap.");
        swimmer.SetWaterStackExposure(false);
        swimmer.Tick(300f);
        Expect(Stacks(swimmer) == 10, "Rain must not clip stronger pre-existing water stacks to five.");
        WeatherMgr.ExistingInstance.Raining = false;
        swimmer.Tick(30f);
        Expect(Stacks(swimmer) == 9, "Stronger water stacks must still decay normally when dry.");

        ResetWorld();
        GameRes.Instance.Buffs[BurningBuffIds.Burning] = BuffDefinitionFactory.Build(new BuffDefinitionDto
        {
            Id = BurningBuffIds.Burning, DurationSeconds = 10000f, StackMode = "add_stacks", MaxStacks = 10
        });
        var burning = Actor();
        burning.AddBuff(BurningBuffIds.Burning, 3);
        burning.Tick(120f);
        Expect(Stacks(burning) == 2 && burning.HasBuff(BurningBuffIds.Burning), "Weak rain wetness must keep accumulating against stronger fire.");
        burning.Tick(60f);
        Expect(Stacks(burning) == 3 && !burning.HasBuff(BurningBuffIds.Burning), "Equal wet/fire layers must extinguish burning.");

        var tickingDto = catalog.Buffs.Single(b => b.Id == WetBuffIds.Wet);
        tickingDto.TickIntervalSeconds = 1f;
        tickingDto.Effects.Add(new BuffEffectDto { Phase = "tick", TypeId = "core:true_damage", Value = 1f });
        GameRes.Instance.Buffs[WetBuffIds.Wet] = BuffDefinitionFactory.Build(tickingDto);
        BuffEffectDispatcher.Executions.Clear();
        var ticking = Actor();
        ticking.AddBuff(WetBuffIds.Wet);
        ticking.Tick(2f);
        Expect(BuffEffectDispatcher.Executions.GetValueOrDefault((WetBuffIds.Wet, BuffEffectPhase.Start)) == 1,
            "Sustaining rain wetness must not replay Start.");
        Expect(BuffEffectDispatcher.Executions.GetValueOrDefault((WetBuffIds.Wet, BuffEffectPhase.Tick)) == 2,
            "Sustaining a duration must not pause periodic effects.");
        Expect(ticking.ActiveBuffs[WetBuffIds.Wet].RemainingDurationSeconds == 30f, "Sustained wetness must keep a full drying interval.");

        GameRes.Instance.Buffs["poison"] = BuffDefinitionFactory.Build(new BuffDefinitionDto
        {
            Id = "poison", DurationSeconds = 2f, TickIntervalSeconds = 1f,
            Effects = new() { new() { Phase = "tick", TypeId = "core:true_damage", Value = 1f } }
        });
        ticking.AddBuff("poison");
        ticking.Tick(2f);
        Expect(!ticking.HasBuff("poison") && BuffEffectDispatcher.Executions.GetValueOrDefault(("poison", BuffEffectPhase.Tick)) == 2,
            "Other buffs must tick and expire normally during rain.");

        foreach (Action<BuffDefinitionDto> mutate in new Action<BuffDefinitionDto>[]
        {
            d => d.RainStackIntervalSeconds = -1f,
            d => d.RainStackIntervalSeconds = float.NaN,
            d => d.RainStackIntervalSeconds = float.PositiveInfinity,
            d => d.RainMaxStacks = -1,
            d => d.RainMaxStacks = 11,
            d => d.RainReferenceIntensity = 0f,
            d => d.RainReferenceIntensity = 1.1f,
            d => d.RainReferenceIntensity = float.NaN,
            d => d.Id = "not-wet",
            d => d.StackMode = "ignore"
        })
        {
            var dto = BuffDefinitionFactory.Deserialize(File.ReadAllText(args[0])).Buffs.Single(b => b.Id == WetBuffIds.Wet);
            mutate(dto);
            bool rejected = false;
            try { BuffDefinitionFactory.Build(dto); } catch (InvalidDataException) { rejected = true; }
            Expect(rejected, "Invalid rain schema must fail during C# definition construction.");
        }
        Console.WriteLine($"PASS: {assertions} rain/buff assertions against production C# source (engine/world inputs stubbed).");
    }
}
