using System;
using System.Collections.Generic;
using FlatWorld.Combat;
using FlatWorld.Gameplay.Events;
using FlatWorld.Gameplay.Progress;
using FlatWorld.Gameplay.Quests;
using Newtonsoft.Json.Linq;
using Unity.Mathematics;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 实体与玩家规则探针注册

    // 仅创建独立数据与未加载的临时物品，业务断言统一留在 Lua。
    private static void RegisterEntityOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("ai/egg_schedule", EntityEggSchedule);
        operations.Add("ai/state_machine", EntityAiStateMachine);
        operations.Add("ai/advance_target", EntityAiAdvance);
        operations.Add("ai/attack_window", EntityAiAttackWindow);
        operations.Add("ai/behavior_memory", EntityAiBehaviorMemory);
        operations.Add("ai/perception_radius", EntityAiPerceptionRadius);
        operations.Add("ai/ecology_config", EntityEcologyConfig);
        operations.Add("ai/fish_current", EntityFishCurrent);
        operations.Add("ai/spawn_chance", EntitySpawnChance);
        operations.Add("ai/spawn_attempt", EntitySpawnAttempt);
        operations.Add("ai/aquatic_traversal", EntityAquaticTraversal);
        operations.Add("ai/hive_resident_schedule", EntityHiveResidentSchedule);
        operations.Add("player/movement_cost", EntityMovementCost);
        operations.Add("player/trauma_modifiers", EntityTraumaModifiers);
        operations.Add("item_runtime/single_sprite_animation", EntitySingleSpriteAnimation);
        operations.Add("item_runtime/single_sprite_shell_assembly", EntitySingleSpriteShellAssembly);
        operations.Add("item_runtime/module_assembly_identity", EntityModuleAssemblyIdentity);
        operations.Add("combat/physical", EntityPhysicalDamage);
        operations.Add("combat/loss_bleeding", EntityLossBleeding);
        operations.Add("combat/identity", EntityCombatIdentity);
        operations.Add("combat/loot_amount", EntityLootAmount);
        operations.Add("combat/tree_leaf_configuration", EntityTreeLeafConfiguration);
        RegisterCombatAudioOperations(operations);
        operations.Add("buff/lifecycle", EntityBuffLifecycle);
        operations.Add("buff/infection_attributes", EntityInfectionAttributes);
        operations.Add("buff/attribute_validation", EntityBuffAttributeValidation);
        operations.Add("buff/icon_catalog", EntityBuffIconCatalog);
        operations.Add("buff/icon_presentation", EntityBuffIconPresentation);
        operations.Add("survival/treatment", EntityTreatment);
        operations.Add("survival/oxygen", EntityOxygen);
        operations.Add("survival/pressure", EntityPressure);
        operations.Add("survival/nutrition", EntityNutrition);
        operations.Add("survival/thermal", EntityTemperature);
        operations.Add("inventory/transactions", EntityInventoryTransactions);
        operations.Add("inventory/capacity_identity", EntityInventoryCapacity);
        operations.Add("inventory/flick_gesture", EntityInventoryFlickGesture);
        operations.Add("inventory/flick_direction", EntityInventoryFlickDirection);
        operations.Add("crafting/allocation", EntityCraftingAllocation);
        operations.Add("crafting/transaction", EntityCraftingTransaction);
        operations.Add("crafting/plank_tool", EntityCraftingPlankTool);
        operations.Add("containers/composition", EntityContainerComposition);
        operations.Add("containers/whole_world_transfer", EntityContainerWholeWorldTransfer);
        operations.Add("containers/whole_batch", EntityContainerWholeBatch);
        operations.Add("quests/signal_progress", EntityQuestSignal);
        operations.Add("quests/reward_plan", EntityQuestRewardPlan);
        operations.Add("quests/inventory_objective", EntityQuestInventoryObjective);
        operations.Add("quests/progress_store", EntityQuestProgressStore);
        operations.Add("game_events/day_schedule", EntityEventDaySchedule);
    }

    #endregion

    #region AI 与移动消耗

    // 读取正式目录并调用正式概率与队列规则，测试不重新计算业务逻辑。
    private static JObject EntityEcologyConfig(JObject args)
    {
        SpawnerConfigCatalog catalog = SpawnerConfigCatalogLoader.LoadBuiltIn();
        SpawnerConfigDefinition config = catalog.Configs.Find(value => value.Id == "wildlife_resources");
        SpawnerConfig.SpawnEntry chicken = config.SpawnEntries.Find(value => value.PrefabName == "Chicken").CreateRuntimeEntry();
        SpawnerConfig.SpawnEntry boar = config.SpawnEntries.Find(value => value.PrefabName == "WildBoar").CreateRuntimeEntry();
        SpawnerConfigDefinition fishSpawns = catalog.Configs.Find(value => value.Id == "small_fish_ecology");
        var fishPackage = JObject.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(
            Application.streamingAssetsPath, "GameConfig/Actors/definitions/fish-actors.json")));
        JObject fish = null;
        foreach (JObject actor in (JArray)fishPackage["actors"])
            if (actor.Value<string>("id") == "SmallFish") fish = actor;
        if (fish == null) throw new InvalidOperationException("正式小鱼配置缺失。");
        JToken temperature = fish["modules"]["temperature"]["parameters"]["Data"];
        return new JObject
        {
            ["chickenChance"] = chicken.SpawnChance, ["chickenWeight"] = chicken.Probability,
            ["boarChance"] = boar.SpawnChance, ["boarWeight"] = boar.Probability,
            ["defaultChance"] = new SpawnerConfig.SpawnEntry().SpawnChance,
            ["fishSpawnsPerDay"] = fishSpawns.SpawnsPerDay, ["fishSpawnCount"] = fishSpawns.SpawnCount,
            ["fishWaterOnly"] = fishSpawns.WaterOnly,
            ["fishAllowedBiomes"] = new JArray(fishSpawns.AllowedBiomeNames),
            ["fishSafeMin"] = temperature["SafeTemperatureMin"], ["fishSafeMax"] = temperature["SafeTemperatureMax"]
        };
    }

    // 独立读取正式水流目录和鱼能力，不发布目录或加载当前世界中的鱼。
    private static JObject EntityFishCurrent(JObject args)
    {
        WaterCurrentPushCatalog currents = WaterCurrentPushConfigLoader.Deserialize(
            StreamingAssetsTextLoader.ReadAllText(WaterCurrentPushConfigLoader.BuiltInConfigPath));
        float weight = (float)Number(args, "weight", 1d);
        return WithEntityProbeItem(receiver =>
        {
            IWaterCurrentExposure exposure = receiver.gameObject.AddComponent<Mod_AI_Fish>();
            float fishSpeed = currents.ResolvePushSpeed("SmallFish", weight);
            Vector2 passiveVelocity = WorldMotionSystem.SampleWaterVelocity(Vector2.zero, fishSpeed);
            return new JObject
            {
                ["receivesCurrent"] = exposure.ReceivesWaterCurrent,
                ["fishSpeed"] = fishSpeed,
                ["passiveVelocityX"] = passiveVelocity.x, ["passiveVelocityY"] = passiveVelocity.y,
                ["chickenSpeed"] = currents.ResolvePushSpeed("Chicken", weight),
                ["wolfSpeed"] = currents.ResolvePushSpeed("Wolf", weight),
                ["defaultSpeed"] = currents.ResolvePushSpeed("test:unlisted_actor", weight)
            };
        });
    }

    private static JObject EntitySpawnChance(JObject args)
    {
        var entry = new SpawnerConfig.SpawnEntry { SpawnChance = (float)Number(args, "chance", .5d) };
        return new JObject { ["accepted"] = entry.PassesSpawnChance((float)Number(args, "sample", 0d)) };
    }

    private static JObject EntitySpawnAttempt(JObject args)
    {
        bool replacement = Flag(args, "replacement", false);
        var state = new SpawnerProgressSaveData
        {
            PendingSpawnCount = replacement ? 0 : 2, PendingReplacementCount = 2,
            LifetimeSpawnCount = 4, AvailableBudget = 7, LastSpawnDay = 10
        };
        state.CompleteSpawnAttempt(Flag(args, "spawned", false), 12);
        return new JObject
        {
            ["pending"] = state.PendingSpawnCount, ["replacement"] = state.PendingReplacementCount,
            ["lifetime"] = state.LifetimeSpawnCount, ["budget"] = state.AvailableBudget,
            ["lastDay"] = state.LastSpawnDay
        };
    }

    private static JObject EntityEggSchedule(JObject args)
    {
        var schedule = new AnimalEggLayingSchedule();
        bool initiallyDue = schedule.IsDue(100d, 2d);
        string invalid = Text(args, "invalid", string.Empty);
        try
        {
            double day = invalid == "day" ? double.NaN : Number(args, "day", 10d);
            double minimum = invalid == "minimum" ? double.PositiveInfinity : Number(args, "minimum", 2d);
            double delay = invalid == "delay" ? double.NaN : Number(args, "delay", 4d);
            double sample = invalid == "sample" ? double.PositiveInfinity : Number(args, "sample", .5d);
            schedule.BeginCycle(day, minimum, delay, sample);
            double checkDay = Number(args, "checkDay", schedule.NextEggDay);
            double updatedMinimum = Number(args, "updatedMinimum", minimum);
            bool due = schedule.IsDue(checkDay, updatedMinimum);
            double oldDeadline = schedule.NextEggDay;
            schedule.BeginCycle(checkDay, minimum, delay, sample);
            return new JObject
            {
                ["initiallyDue"] = initiallyDue, ["rejected"] = false, ["initialized"] = schedule.Initialized,
                ["deadline"] = oldDeadline, ["due"] = due, ["nextDeadline"] = schedule.NextEggDay
            };
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return new JObject { ["initiallyDue"] = initiallyDue, ["rejected"] = true, ["parameter"] = exception.ParamName };
        }
    }

    private static JObject EntityMovementCost(JObject args)
    {
        var rule = new MovementHungerActionDefinition
        {
            enabled = Flag(args, "enabled", true),
            moveNutritionConsumeMultiplier = (float)Number(args, "move", 1.6d),
            runNutritionConsumeMultiplier = (float)Number(args, "run", 2d),
            runWaterConsumeMultiplier = (float)Number(args, "water", .25d)
        };
        rule.ClampValues();
        bool moving = Flag(args, "moving", true), running = Flag(args, "running", false);
        return new JObject
        {
            ["nutrition"] = rule.ResolveMultiplier(moving, running),
            ["water"] = rule.ResolveWaterMultiplier(moving, running),
            ["moveConfiguration"] = rule.moveNutritionConsumeMultiplier,
            ["runConfiguration"] = rule.runNutritionConsumeMultiplier
        };
    }

    private static JObject EntityTraumaModifiers(JObject args)
    {
        return WithEntityProbeItem(first => WithEntityProbeItem(second =>
        {
            var definition = new BuffDefinition { Id = "test:trauma_cache" };
            BuffInstance Make(Item receiver)
            {
                var runtime = new BuffInstance();
                if (!runtime.Initialize(definition, receiver))
                    throw new InvalidOperationException("创伤缓存测试 Buff 初始化失败。");
                return runtime;
            }
            static void Set(BuffInstance runtime, string type, float value, bool stop = false) =>
                BodyTraumaBuffEffects.Apply(new BuffEffectDefinition
                {
                    TypeId = type, Phase = stop ? BuffEffectPhase.Stop : BuffEffectPhase.Start, Value = value
                }, runtime);
            static JObject Snapshot(Item receiver)
            {
                Vector2 input = BodyTraumaBuffEffects.TransformMoveInput(receiver, new Vector2(2f, -3f), 1f);
                return new JObject
                {
                    ["attack"] = BodyTraumaBuffEffects.GetAttackMultiplier(receiver),
                    ["moveX"] = input.x, ["moveY"] = input.y
                };
            }

            BuffInstance one = Make(first), two = Make(first), other = Make(second);
            try
            {
                JObject baseline = Snapshot(first);
                Set(one, BodyTraumaBuffEffects.Attack, 1.5f);
                Set(one, BodyTraumaBuffEffects.Confusion, 2f);
                Set(two, BodyTraumaBuffEffects.Attack, .5f);
                Set(two, BodyTraumaBuffEffects.Confusion, .5f);
                Set(other, BodyTraumaBuffEffects.Attack, 1.8f);
                Set(other, BodyTraumaBuffEffects.Confusion, 1f);
                JObject stacked = Snapshot(first), isolated = Snapshot(second);
                Set(one, BodyTraumaBuffEffects.Confusion, 0f, true);
                JObject oneConfusionRemoved = Snapshot(first);
                Set(two, BodyTraumaBuffEffects.Confusion, 0f, true);
                JObject bothConfusionsRemoved = Snapshot(first);
                Set(one, BodyTraumaBuffEffects.Attack, 0f, true);
                JObject oneAttackRemoved = Snapshot(first);
                Set(two, BodyTraumaBuffEffects.Attack, .8f);
                JObject replacedAttack = Snapshot(first), otherUnchanged = Snapshot(second);
                Set(two, BodyTraumaBuffEffects.Attack, 0f, true);
                Set(other, BodyTraumaBuffEffects.Confusion, 0f, true);
                Set(other, BodyTraumaBuffEffects.Attack, 0f, true);
                return new JObject
                {
                    ["baseline"] = baseline, ["stacked"] = stacked, ["isolated"] = isolated,
                    ["oneConfusionRemoved"] = oneConfusionRemoved,
                    ["bothConfusionsRemoved"] = bothConfusionsRemoved,
                    ["oneAttackRemoved"] = oneAttackRemoved, ["replacedAttack"] = replacedAttack,
                    ["otherUnchanged"] = otherUnchanged, ["cleared"] = Snapshot(first),
                    ["otherCleared"] = Snapshot(second),
                    ["nullAttack"] = BodyTraumaBuffEffects.GetAttackMultiplier(null)
                };
            }
            finally
            {
                foreach (BuffInstance runtime in new[] { one, two, other })
                {
                    Set(runtime, BodyTraumaBuffEffects.Attack, 0f, true);
                    Set(runtime, BodyTraumaBuffEffects.Confusion, 0f, true);
                }
            }
        }));
    }

    private static JObject EntityAiStateMachine(JObject args)
    {
        var events = new JArray();
        var machine = new AIStateMachine<string>();
        machine.Register(new AIStateNode<string>("idle", _ => events.Add("idle.tick"),
            () => events.Add("idle.enter"), () => events.Add("idle.exit"), AIStateAnimationRole.Stopped));
        machine.Register(new AIStateNode<string>("chase", _ => events.Add("chase.tick"),
            () => events.Add("chase.enter"), () => events.Add("chase.exit"), AIStateAnimationRole.Moving));
        bool duplicateRejected = false, missingRejected = false, uninitializedRejected = false;
        try { machine.Register(new AIStateNode<string>("idle", _ => { })); }
        catch (InvalidOperationException) { duplicateRejected = true; }
        try { machine.TransitionTo("chase", null); }
        catch (InvalidOperationException) { uninitializedRejected = true; }
        machine.Initialize("idle");
        machine.Tick(.25f);
        AI_StateMachineRunner.EvaluateAndTick(machine, machine.CurrentState, () => "chase",
            next => machine.TransitionTo(next, () => events.Add("transition")), .25f,
            _ => !Flag(args, "blocked", false));
        string afterEvaluation = machine.CurrentState;
        int repeatedTransitions = 0;
        machine.TransitionTo(machine.CurrentState, () => repeatedTransitions++);
        try { machine.TransitionTo("missing", null); }
        catch (InvalidOperationException) { missingRejected = true; }
        string afterRejected = machine.CurrentState;
        machine.Reset(); machine.Reset();
        bool resetInitialized = machine.IsInitialized;
        machine.Tick(.25f);
        machine.Initialize("idle");
        machine.Tick(.25f);
        return new JObject
        {
            ["events"] = events, ["afterEvaluation"] = afterEvaluation, ["afterRejected"] = afterRejected,
            ["repeatedTransitions"] = repeatedTransitions, ["duplicateRejected"] = duplicateRejected,
            ["missingRejected"] = missingRejected, ["uninitializedRejected"] = uninitializedRejected,
            ["resetInitialized"] = resetInitialized, ["reinitialized"] = machine.IsInitialized,
            ["chaseAnimation"] = machine.GetAnimationRole("chase").ToString()
        };
    }

    private static JObject EntityAiAdvance(JObject args)
    {
        Vector3 destination = new((float)Number(args, "targetX", 3d), 0f, 0f), position = Vector3.zero;
        AIAdvanceTarget target = new(true, destination);
        int movements = 0, stops = 0, arrivals = 0, enters = 0, exits = 0;
        var node = new AIAdvanceStateNode<string>("advance", () => target, () => position,
            () => (float)Number(args, "arrivalDistance", .25d), _ => movements++, () => stops++,
            () => arrivals++, () => enters++, () => exits++);
        node.Enter(); node.Tick(.1f);
        int firstMovements = movements, firstArrivals = arrivals;
        target = Text(args, "invalid", "missing") == "nonfinite"
            ? new AIAdvanceTarget(true, new Vector3(float.NaN, 0f, 0f)) : AIAdvanceTarget.None;
        node.Tick(.1f);
        int invalidMovements = movements, invalidStops = stops;
        target = new AIAdvanceTarget(true, destination);
        position = destination;
        node.Tick(.1f); node.Tick(.1f);
        int repeatedArrivalCount = arrivals;
        position = destination - Vector3.right;
        node.Tick(.1f);
        position = destination;
        node.Tick(.1f);
        int returnArrivalCount = arrivals;
        node.Exit(); node.Enter(); node.Tick(.1f); node.Exit();
        return new JObject
        {
            ["firstMovements"] = firstMovements, ["firstArrivals"] = firstArrivals,
            ["invalidMovements"] = invalidMovements, ["invalidStops"] = invalidStops,
            ["repeatedArrivalCount"] = repeatedArrivalCount, ["returnArrivalCount"] = returnArrivalCount,
            ["arrivals"] = arrivals, ["enters"] = enters, ["exits"] = exits,
            ["animation"] = node.AnimationRole.ToString()
        };
    }

    private static JObject EntityAiAttackWindow(JObject args)
    {
        var controller = new AI_AttackController
        {
            WindupDuration = (float)Number(args, "windup", .25d), DamageWindow = .5f,
            RecoveryDuration = .5f, Cooldown = 1f
        };
        controller.Reset();
        controller.StartWindow(null, string.Empty, Vector2.right);
        bool startActive = controller.IsDamageWindowActive, startLocked = controller.IsAttackLocked;
        controller.Tick(.125f);
        bool firstActive = controller.IsDamageWindowActive;
        controller.StartWindow(null, string.Empty, Vector2.right);
        controller.Tick(.125f);
        bool afterWindupActive = controller.IsDamageWindowActive;
        controller.Tick(.5f);
        bool closedActive = controller.IsDamageWindowActive, recoveryActive = controller.IsRecoveryActive;
        float cooldownAfterClose = controller.CooldownRemaining;
        controller.StartWindow(null, string.Empty, Vector2.right);
        bool recoveryRestartActive = controller.IsDamageWindowActive;
        controller.Tick(.5f);
        bool afterRecoveryLocked = controller.IsAttackLocked, afterRecoveryDone = controller.IsCooldownDone;
        controller.Tick(.5f);
        bool cooldownDone = controller.IsCooldownDone;
        controller.RestoreCooldown((float)Number(args, "restore", 3d));
        float restored = controller.CooldownRemaining;
        controller.StopWindow();
        return new JObject
        {
            ["startActive"] = startActive, ["startLocked"] = startLocked, ["firstActive"] = firstActive,
            ["afterWindupActive"] = afterWindupActive, ["closedActive"] = closedActive,
            ["recoveryActive"] = recoveryActive, ["cooldownAfterClose"] = cooldownAfterClose,
            ["recoveryRestartActive"] = recoveryRestartActive, ["afterRecoveryLocked"] = afterRecoveryLocked,
            ["afterRecoveryDone"] = afterRecoveryDone, ["cooldownDone"] = cooldownDone,
            ["restored"] = restored, ["stoppedLocked"] = controller.IsAttackLocked
        };
    }

    private static JObject EntityAiBehaviorMemory(JObject args)
    {
        var blackboard = new AIBehaviorGraphBlackboard(new Dictionary<string, float>
            { ["ordinary"] = 4f, ["world"] = 10f }, 3f, 1f, new Vector3(-2f, 0f, 0f));
        blackboard.Tick((float)Number(args, "step", 2d), "world");
        float ordinaryAfter = blackboard.GetTimerRemaining("ordinary"), worldAfter = blackboard.GetTimerRemaining("world");
        bool previousThreat = blackboard.HasRecentDamageThreat;
        blackboard.RecordDamage((float)Number(args, "memory", 3d), Flag(args, "hasOrigin", true), new Vector3(7f, 8f, 0f));
        bool damaged = blackboard.DamagedSinceStateEnter;
        bool remembered = blackboard.TryGetRecentDamageOrigin(out Vector3 origin);
        blackboard.BeginState();
        bool newStateDamaged = blackboard.DamagedSinceStateEnter, newStateThreat = blackboard.HasRecentDamageThreat;
        blackboard.TickWorldTimer("world", 5f);
        Dictionary<string, float> exported = blackboard.ExportTimers();
        exported["ordinary"] = 100f;
        float originalAfterExportMutation = blackboard.GetTimerRemaining("ordinary");
        blackboard.Tick(5f, "world");
        return new JObject
        {
            ["ordinaryAfter"] = ordinaryAfter, ["worldAfter"] = worldAfter, ["previousThreat"] = previousThreat,
            ["damaged"] = damaged, ["remembered"] = remembered, ["originX"] = origin.x, ["originY"] = origin.y,
            ["newStateDamaged"] = newStateDamaged, ["newStateThreat"] = newStateThreat,
            ["stateElapsed"] = blackboard.StateElapsed, ["worldAfterWorldTick"] = blackboard.GetTimerRemaining("world"),
            ["originalAfterExportMutation"] = originalAfterExportMutation,
            ["ordinaryElapsed"] = blackboard.IsTimerElapsed("ordinary"), ["finalThreat"] = blackboard.HasRecentDamageThreat
        };
    }

    private static JObject EntityAiPerceptionRadius(JObject args)
    {
        float radius = Flag(args, "invalidRadius", false) ? float.NaN : (float)Number(args, "radius", 4d);
        float multiplier = Flag(args, "invalidMultiplier", false) ? float.PositiveInfinity : (float)Number(args, "multiplier", 2d);
        return new JObject { ["effective"] = Mod_ItemDetector.CalculateEffectiveDetectionRadius(radius, multiplier) };
    }

    #endregion

    #region 战斗结算与身份

    // 采样正式目录与同一组分位点，不改变 Unity 的全局随机状态。
    private static JObject EntityLootAmount(JObject args)
    {
        var entryJson = new JObject
        {
            ["itemId"] = "Leaf", ["probability"] = 1,
            ["minAmount"] = Number(args, "minimum", 1), ["maxAmount"] = Number(args, "maximum", 20),
            ["amountDistribution"] = Text(args, "distribution", "QuadraticPeak"),
            ["peakAmount"] = Number(args, "peak", 10)
        };
        var catalogJson = new JObject { ["schemaVersion"] = 1,
            ["lootTables"] = new JArray(new JObject { ["id"] = "probe", ["entries"] = new JArray(entryJson) }) };
        RuntimeLootTable table;
        try { table = LootTableCatalogLoader.DeserializeCatalog(catalogJson.ToString())[0]; }
        catch (Exception error) when (error is ArgumentException || error is System.IO.InvalidDataException)
        { return new JObject { ["rejected"] = true }; }
        RuntimeLootTableEntry native = table.Entries[0];
        LootEntry go = table.CreateDamageReceiverEntries()[0].ToObject<LootEntry>();
        var counts = new int[native.MaxAmount - native.MinAmount + 1];
        bool equivalent = true;
        for (int i = 0; i < 10000; i++)
        {
            double random = (i + .5d) / 10000d;
            int amount = native.SampleAmount(random);
            counts[amount - native.MinAmount]++;
            equivalent &= amount == go.SampleAmount(random);
        }
        return new JObject { ["rejected"] = false, ["counts"] = new JArray(counts),
            ["minimum"] = native.SampleAmount(0d), ["maximum"] = native.SampleAmount(1d),
            ["goMinimum"] = go.SampleAmount(0d), ["goMaximum"] = go.SampleAmount(1d),
            ["backendEquivalent"] = equivalent };
    }

    private static JObject EntityTreeLeafConfiguration(JObject args)
    {
        var tables = LootTableCatalogLoader.LoadBuiltIn().ToDictionary(table => table.Id, StringComparer.OrdinalIgnoreCase);
        var trees = new JArray();
        foreach (ItemDefinitionDto definition in ItemDefinitionCatalogLoader.LoadBuiltInDefinitions())
        {
            if (definition.Abstract || definition.Tags == null ||
                !definition.Tags.Exists(tag => string.Equals(tag, Tag.Tree, StringComparison.OrdinalIgnoreCase))) continue;
            var leaves = new JArray();
            if (!string.IsNullOrWhiteSpace(definition.LootTableId) && tables.TryGetValue(definition.LootTableId, out var table))
                foreach (RuntimeLootTableEntry entry in table.Entries)
                    if (entry.ItemId == "Leaf") leaves.Add(new JObject { ["probability"] = entry.Probability,
                        ["minimum"] = entry.MinAmount, ["maximum"] = entry.MaxAmount,
                        ["distribution"] = entry.AmountDistribution.ToString(), ["peak"] = entry.PeakAmount });
            trees.Add(new JObject { ["id"] = definition.Id, ["leaves"] = leaves });
        }
        return new JObject { ["trees"] = trees };
    }

    private static JObject EntityPhysicalDamage(JObject args)
    {
        float attack = (float)Number(args, "attack", 10d), defense = (float)Number(args, "defense", 3d);
        float difficulty = (float)Number(args, "difficulty", 1d), received = (float)Number(args, "received", 1d);
        var damage = new float4(attack * .5f, attack * .25f, attack * .25f, 0f);
        float4 resolved = CombatRules.Resolve(damage, new float4(defense), difficulty, received);
        var difficultyRules = new CombatDifficulty
        {
            PlayerAttack = (float)Number(args, "playerAttack", 2d),
            CreatureAttack = (float)Number(args, "creatureAttack", .5d),
            CreatureHealth = (float)Number(args, "creatureHealth", 2d)
        };
        return new JObject
        {
            ["physical"] = CombatRules.ResolvePhysical(attack, defense, difficulty, received),
            ["resolvedTotal"] = math.csum(resolved), ["cut"] = resolved.x, ["pierce"] = resolved.y,
            ["difficulty"] = difficultyRules.Resolve(Flag(args, "sourcePlayer", true), Flag(args, "targetPlayer", false))
        };
    }

    private static JObject EntityLossBleeding(JObject args)
    {
        var resolved = new float4((float)Number(args, "cut", 8d), (float)Number(args, "pierce", 4d),
            (float)Number(args, "chop", 4d), (float)Number(args, "blunt", 4d));
        float4 actual = CombatRules.ActualLoss(resolved, (float)Number(args, "loss", 10d));
        int bleeding = CombatRules.BleedingTier(actual, Flag(args, "blood", true), Flag(args, "alive", true),
            (float)Number(args, "random", 0d));
        return new JObject
        {
            ["actualTotal"] = math.csum(actual), ["cut"] = actual.x, ["pierce"] = actual.y,
            ["blunt"] = actual.w, ["bleeding"] = bleeding
        };
    }

    private static JObject EntityCombatIdentity(JObject args)
    {
        var original = new CombatIdentity { Value = 42, Generation = 1, World = 2, Dimension = 3, Backend = CombatBackend.Entity };
        var changed = original;
        string change = Text(args, "change", "none");
        switch (change)
        {
            case "generation": changed.Generation++; break;
            case "world": changed.World++; break;
            case "dimension": changed.Dimension++; break;
            case "backend": changed.Backend = CombatBackend.External; break;
            case "value": changed.Value++; break;
            case "invalid": changed.Value = 0; break;
        }
        var first = new CombatAttackKey { Source = original, Sequence = 9, Window = 2, Pulse = 3 };
        var second = first;
        second.Source = changed;
        if (Flag(args, "changePulse", false)) second.Pulse++;
        return new JObject
        {
            ["sameIdentity"] = original.Equals(changed), ["sameAttack"] = first.Equals(second),
            ["originalValid"] = original.IsValid, ["changedValid"] = changed.IsValid
        };
    }

    #endregion

    #region Buff 与治疗会话

    private static JObject EntityBuffIconCatalog(JObject args)
    {
        var catalog = new JArray();
        foreach (BuffDefinition definition in BuffCatalogLoader.LoadBuiltInDefinitions())
            catalog.Add(new JObject { ["id"] = definition.Id, ["iconAddress"] = definition.IconAddress });
        BuffDefinition configured = BuffDefinitionFactory.Build(BuffDefinitionFactory.DeserializeDefinition(
            JObject.Parse("{\"id\":\"test:icon\",\"iconAddress\":\"  Assets/Icon.png[Icon]  \",\"effects\":[]}")));
        BuffDefinition missing = BuffDefinitionFactory.Build(new BuffDefinitionDto { Id = "test:no_icon" });
        return new JObject
        {
            ["catalog"] = catalog, ["normalized"] = configured.IconAddress,
            ["missingAddress"] = missing.IconAddress, ["missingSprite"] = missing.Icon == null
        };
    }

    private static JObject EntityBuffIconPresentation(JObject args)
    {
        return WithEntityProbeItem(receiver =>
        {
            const string prefabPath = "Assets/2_Prefabs/2-1_UI/Gameplay/Status/Buff/UI_BuffStatusItem.prefab";
#if UNITY_EDITOR
            GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
#else
            GameObject prefab = GameRes.Instance?.GetPrefab(RuntimeUIPrefabKeys.BuffStatusItem, false);
#endif
            if (prefab == null)
                throw new InvalidOperationException("Buff 图标采样缺少正式行 Prefab。");
            GameObject rowObject = UnityEngine.Object.Instantiate(prefab);
            Texture2D texture = new(1, 1);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(.5f, .5f));
            try
            {
                // 使用正式行 Prefab 检查有图、缺图和复用清理，断言由 Lua 负责。
                BuffStatusRowView row = rowObject.GetComponent<BuffStatusRowView>();
                var icon = rowObject.transform.Find("状态符号底板/状态图标")?.GetComponent<UnityEngine.UI.Image>();
                var initial = rowObject.transform.Find("状态符号底板/占位符文本")?.GetComponent<TMPro.TextMeshProUGUI>();
                Transform badge = rowObject.transform.Find("状态符号底板/层数徽标");
                if (row == null || icon == null || initial == null || badge == null)
                    throw new InvalidOperationException("Buff 行 Prefab 图标节点契约不完整。");
                var withIcon = new BuffInstance();
                withIcon.Initialize(new BuffDefinition { Id = "test:with_icon", DisplayName = "图标状态", Icon = sprite, MaxStacks = 3 }, receiver);
                row.Bind(withIcon);
                var result = new JObject
                {
                    ["iconBound"] = icon.enabled && icon.sprite == sprite,
                    ["iconWhite"] = icon.color == Color.white, ["preserveAspect"] = icon.preserveAspect,
                    ["raycast"] = icon.raycastTarget, ["initialHidden"] = !initial.gameObject.activeSelf,
                    ["stackVisible"] = badge.gameObject.activeSelf
                };
                var withoutIcon = new BuffInstance();
                withoutIcon.Initialize(new BuffDefinition { Id = "test:no_icon", DisplayName = "缺图状态" }, receiver);
                row.Bind(withoutIcon);
                result["missingIconHidden"] = !icon.enabled && icon.sprite == null;
                result["fallbackVisible"] = initial.gameObject.activeSelf;
                result["fallbackInitial"] = initial.text;
                result["plainBadgeHidden"] = !badge.gameObject.activeSelf;
                row.Bind(withIcon);
                result["reboundIcon"] = icon.enabled && icon.sprite == sprite && !initial.gameObject.activeSelf;
                row.Clear();
                result["clearedIcon"] = !icon.enabled && icon.sprite == null;
                result["clearedText"] = string.IsNullOrEmpty(initial.text) && string.IsNullOrEmpty(row.BuffId);
                result["clearedBadge"] = !badge.gameObject.activeSelf;
                return result;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(rowObject);
                UnityEngine.Object.DestroyImmediate(sprite);
                UnityEngine.Object.DestroyImmediate(texture);
            }
        });
    }

    private static JObject EntityBuffLifecycle(JObject args)
    {
        return WithEntityProbeItem(receiver =>
        {
            bool permanent = Flag(args, "permanent", false);
            var definition = BuffDefinitionFactory.Build(new BuffDefinitionDto
            {
                Id = "test:independent_buff", DurationSeconds = permanent ? null : (float?)Number(args, "duration", 2d),
                TickIntervalSeconds = .5f, StackMode = "add_stacks", MaxStacks = (int)Number(args, "maxStacks", 4d),
                DecayStacksOnExpiry = Flag(args, "decay", true) && !permanent,
                VisualBaseScale = 1f, VisualScalePerStack = .25f
            });
            int startCount = 0, stopCount = 0, tickCount = 0;
            var start = new BuffEffectDefinition();
            var tick = new BuffEffectDefinition();
            var stop = new BuffEffectDefinition();
            start.TryCacheHandler((effect, runtime) => startCount++);
            tick.TryCacheHandler((effect, runtime) => tickCount++);
            stop.TryCacheHandler((effect, runtime) => stopCount++);
            definition.SetEffects(new List<BuffEffectDefinition> { start, tick, stop },
                new List<BuffEffectDefinition> { start }, new List<BuffEffectDefinition> { tick }, new List<BuffEffectDefinition> { stop });
            var buff = new BuffInstance();
            bool initialized = buff.Initialize(definition, receiver);
            buff.SetStackCount((int)Number(args, "stacks", 3d));
            int initialStacks = buff.StackCount;
            float initialScale = buff.VisualScale;
            buff.Start(); buff.Start();
            bool expired = buff.Tick((float)Number(args, "elapsed", 2.5d), Flag(args, "exposed", false));
            float remainingBeforeExtend = buff.RemainingDurationSeconds;
            bool extended = buff.ExtendDuration((float)Number(args, "extend", 0d));
            bool invalidDurationAccepted = buff.TrySetRemainingDuration(-1f);
            buff.Stop(); buff.Stop();
            return new JObject
            {
                ["initialized"] = initialized, ["initialStacks"] = initialStacks, ["initialScale"] = initialScale,
                ["stacks"] = buff.StackCount, ["expired"] = expired, ["remaining"] = remainingBeforeExtend,
                ["extended"] = extended, ["afterExtend"] = buff.RemainingDurationSeconds,
                ["invalidDurationAccepted"] = invalidDurationAccepted,
                ["startCount"] = startCount, ["stopCount"] = stopCount, ["tickCount"] = tickCount
            };
        });
    }

    private static JObject EntityTreatment(JObject args)
    {
        return WithEntityProbeItem(consumer =>
        {
            int commits = 0, validations = 0;
            bool valid = Flag(args, "valid", true);
            var session = new BodyPartTreatmentSession((float)Number(args, "duration", 2d),
                (Item actor, BodyPartType part, out string reason) => { validations++; reason = valid ? null : "test condition rejected"; return valid; },
                (actor, part) => { commits++; return Flag(args, "commit", true); });
            bool began = session.TryBegin(consumer, BodyPartType.LeftHand, out string beginReason);
            session.Advance((float)Number(args, "first", 1d));
            string firstState = session.State.ToString();
            float firstProgress = session.Progress;
            bool earlyComplete = session.TryComplete();
            if (Flag(args, "invalidate", false)) valid = false;
            bool cancelled = Flag(args, "cancel", false) && session.Cancel("test cancellation");
            session.Advance((float)Number(args, "second", 1d));
            bool duplicateComplete = session.TryComplete();
            bool lateCancel = session.Cancel("late cancellation");
            return new JObject
            {
                ["began"] = began, ["beginReason"] = beginReason, ["firstState"] = firstState,
                ["firstProgress"] = firstProgress, ["earlyComplete"] = earlyComplete, ["cancelled"] = cancelled,
                ["state"] = session.State.ToString(), ["commits"] = commits, ["validations"] = validations,
                ["duplicateComplete"] = duplicateComplete, ["lateCancel"] = lateCancel
            };
        });
    }

    private static JObject EntityOxygen(JObject args)
    {
        return WithEntityProbeItem(receiver =>
        {
            Mod_Oxygen oxygen = receiver.gameObject.AddComponent<Mod_Oxygen>();
            oxygen.Data.CurrentOxygen = (float)Number(args, "current", 50d);
            oxygen.Data.MaxOxygen = (float)Number(args, "maximum", 100d);
            int notifications = 0;
            oxygen.OxygenValueChanged += _ => notifications++;
            oxygen.AddOxygen((float)Number(args, "add", 70d));
            float afterAdd = oxygen.CurrentValue;
            oxygen.AddOxygen((float)Number(args, "remove", -25d));
            float afterRemove = oxygen.CurrentValue;
            oxygen.AddOxygen(float.NaN);
            return new JObject
            {
                ["afterAdd"] = afterAdd, ["afterRemove"] = afterRemove, ["afterInvalid"] = oxygen.CurrentValue,
                ["ratio"] = oxygen.OxygenRatio, ["notifications"] = notifications,
                ["supplyAmount"] = oxygen.GetOxygenSupplyAmount((float)Number(args, "seconds", .2d)),
                ["invalidSupply"] = oxygen.GetOxygenSupplyAmount(float.PositiveInfinity),
                ["unloadedSupplyAccepted"] = oxygen.SupplyOxygen(20f)
            };
        });
    }

    private static JObject EntityPressure(JObject args)
    {
        return WithEntityProbeItem(receiver =>
        {
            Mod_Pressure pressure = receiver.gameObject.AddComponent<Mod_Pressure>();
            object first = new(), second = new();
            pressure.SetSafePressureRangeModifier(first, (float)Number(args, "low", 20d), (float)Number(args, "high", 30d));
            pressure.SetSafePressureRangeModifier(second, 10f, 20f);
            pressure.GetSafePressureRange(out float minimum, out float maximum);
            pressure.SetSafePressureRangeModifier(first, 5f, 5f);
            pressure.GetSafePressureRange(out float replacedMinimum, out float replacedMaximum);
            pressure.RemoveSafePressureRangeModifier(first);
            pressure.RemoveSafePressureRangeModifier(second);
            pressure.GetSafePressureRange(out float restoredMinimum, out float restoredMaximum);
            bool invalidModifierRejected = false, invalidRangeRejected = false;
            try { pressure.SetSafePressureRangeModifier(first, -1f, 0f); }
            catch (ArgumentException) { invalidModifierRejected = true; }
            try { pressure.ValidateJsonParameters(new JObject { ["SafePressureMinKPa"] = 200f, ["SafePressureMaxKPa"] = 100f }); }
            catch (InvalidOperationException) { invalidRangeRejected = true; }
            pressure.State.RemainingBufferSeconds = 0f;
            pressure.ResetForNewLife();
            return new JObject
            {
                ["minimum"] = minimum, ["maximum"] = maximum,
                ["replacedMinimum"] = replacedMinimum, ["replacedMaximum"] = replacedMaximum,
                ["restoredMinimum"] = restoredMinimum, ["restoredMaximum"] = restoredMaximum,
                ["invalidModifierRejected"] = invalidModifierRejected, ["invalidRangeRejected"] = invalidRangeRejected,
                ["resetBuffer"] = pressure.State.RemainingBufferSeconds
            };
        });
    }

    private static JObject EntityNutrition(JObject args)
    {
        var original = new Nutrition(10f, 5f, 20f, 8f, 4f);
        var intake = new Nutrition((float)Number(args, "carbohydrates", 20d), (float)Number(args, "protein", -2d),
            (float)Number(args, "water", 30d), (float)Number(args, "fat", -3d), (float)Number(args, "vitamins", 10d));
        Nutrition fed = original + intake;
        float beforeEnergy = fed.Carbohydrates + fed.Fat + fed.Protein;
        bool consumed = fed.TryConsumeEnergy((float)Number(args, "energy", 12d));
        float afterEnergy = fed.Carbohydrates + fed.Fat + fed.Protein;
        bool excessive = fed.TryConsumeEnergy(10000f);
        return new JObject
        {
            ["originalCarbohydrates"] = original.Carbohydrates, ["capacity"] = fed.Max_Carbohydrates,
            ["water"] = fed.Water, ["waterCapacity"] = fed.Max_Water, ["vitamins"] = fed.Vitamins,
            ["consumed"] = consumed, ["beforeEnergy"] = beforeEnergy, ["afterEnergy"] = afterEnergy,
            ["carbohydrates"] = fed.Carbohydrates, ["fat"] = fed.Fat, ["protein"] = fed.Protein,
            ["excessiveAccepted"] = excessive, ["afterRejected"] = fed.Carbohydrates + fed.Fat + fed.Protein
        };
    }

    private static JObject EntityTemperature(JObject args)
    {
        return WithEntityProbeItem(receiver =>
        {
            float current = (float)Number(args, "current", 30d), ambient = (float)Number(args, "ambient", 10d);
            float seconds = (float)Number(args, "seconds", 10d);
            Mod_Temperature temperature = receiver.gameObject.AddComponent<Mod_Temperature>();
            temperature.Data.CurrentTemperature = current;
            object first = new(), second = new();
            temperature.SetTemporaryWarming(first, 10f, 35f);
            float firstWarming = temperature.Data.CurrentTemperature;
            temperature.SetTemporaryWarming(second, 20f, 50f);
            float strongestWarming = temperature.Data.CurrentTemperature;
            var snapshot = temperature.Data.CreateSaveSnapshot(current);
            temperature.RemoveTemporaryWarming(first);
            float afterFirstRemoval = temperature.Data.CurrentTemperature;
            temperature.RemoveTemporaryWarming(second);
            float split = ThermalRuntime.AdvanceAmbient(current, ambient, seconds * .5f);
            split = ThermalRuntime.AdvanceAmbient(split, ambient, seconds * .5f);
            return new JObject
            {
                ["ambientNext"] = ThermalRuntime.AdvanceAmbient(current, ambient, seconds), ["splitNext"] = split,
                ["invalidSecondsNext"] = ThermalRuntime.AdvanceAmbient(current, ambient, float.NaN),
                ["firstWarming"] = firstWarming, ["strongestWarming"] = strongestWarming,
                ["afterFirstRemoval"] = afterFirstRemoval, ["restored"] = temperature.Data.CurrentTemperature,
                ["savedTemperature"] = snapshot.CurrentTemperature,
                ["saveIsIndependent"] = !ReferenceEquals(snapshot, temperature.Data)
            };
        });
    }

    #endregion

    #region 独立库存与材料分配

    // Lua 提供轨迹和期望，采样只调用正式甩动识别器。
    private static JObject EntityInventoryFlickDirection(JObject args)
    {
        Vector2 Read(JToken point) => new(point[0].Value<float>(), point[1].Value<float>());
        Vector2 origin = Read(args["origin"]), direction = Read(args["direction"]);
        if (Text(args, "invalid", "") == "nan") direction.x = float.NaN;
        if (Text(args, "invalid", "") == "infinity") origin.y = float.PositiveInfinity;
        JArray targets = args["targets"] as JArray ?? new JArray();
        var positions = new List<Vector2>(targets.Count);
        for (int i = 0; i < targets.Count; i++)
            positions.Add(Read(targets[i]));
        int selected = InventoryFlickTargets.SelectClosestDirection(origin, direction, positions);
        var result = new JObject { ["selected"] = selected,
            ["nullSourceAccepted"] = InventoryFlickTargets.Resolve(null, 0, origin, direction) != null };
        if (args["bounds"] is JArray bounds)
        {
            var rect = new Rect(bounds[0].Value<float>(), bounds[1].Value<float>(), bounds[2].Value<float>(), bounds[3].Value<float>());
            if (Text(args, "invalid", "") == "bounds_nan") rect.x = float.NaN;
            if (Text(args, "invalid", "") == "bounds_infinity") rect.width = float.PositiveInfinity;
            result["angleAccepted"] = InventoryFlickTargets.IsDirectionWithinTarget(origin, direction, rect);
            result["edgeTolerance"] = InventoryFlickTargets.DirectionEdgeToleranceDegrees;
        }
        return result;
    }

    private static JObject EntityInventoryFlickGesture(JObject args)
    {
        var points = (JArray)args["points"];
        var gesture = new InventoryFlickGesture();
        Vector2 Position(JToken point) => new(point[1].Value<float>(), point[2].Value<float>());
        gesture.Begin(Position(points[0]), points[0][0].Value<double>(), (float)Number(args, "scale", 1d));
        for (int i = 1; i < points.Count; i++)
            gesture.Record(Position(points[i]), points[i][0].Value<double>());
        if (Flag(args, "cancel", false)) gesture.Reset();
        JToken last = points[points.Count - 1];
        return new JObject
        {
            ["accepted"] = gesture.IsFlick(Position(last), last[0].Value<double>()),
            ["minimumDistance"] = InventoryFlickGesture.DefaultMinimumDistance,
            ["minimumSpeed"] = InventoryFlickGesture.DefaultMinimumSpeed
        };
    }

    private static JObject EntityInventoryTransactions(JObject args)
    {
        var sourceSlot = new ItemSlot(0) { itemData = EntityProbeData("test:inventory", (float)Number(args, "amount", 5d)) };
        var source = new Inventory_Data(new List<ItemSlot> { sourceSlot }, "independent source");
        var destinationSlot = new ItemSlot(0) { SlotMaxVolume = (float)Number(args, "slotCapacity", 100d) };
        var destination = new Inventory_Data(new List<ItemSlot> { destinationSlot }, "independent destination")
            { IsDepositBlocked = Flag(args, "blocked", false) };
        bool consumed = source.TryConsumeFromSlot(sourceSlot, (int)Number(args, "consume", 2d), out _);
        float afterConsume = sourceSlot.itemData?.Stack.Amount ?? 0f;
        bool excessiveAccepted = source.TryConsumeFromSlot(sourceSlot, 1000, out _);
        bool foreignAccepted = source.TryConsumeFromSlot(destinationSlot, 1, out _);
        int refreshes = 0;
        source.Event_RefreshUI += _ => refreshes++;
        destination.Event_RefreshUI += _ => refreshes++;
        int preflight = source.GetTransferableQuantityTo(sourceSlot, destination, destinationSlot,
            (int)Number(args, "transfer", 3d));
        bool preflightUnchanged = sourceSlot.itemData?.Stack.Amount == afterConsume &&
            destinationSlot.itemData == null && refreshes == 0;
        int foreignPreflight = source.GetTransferableQuantityTo(destinationSlot, destination, sourceSlot, 1);
        bool transferred = source.TransferItemQuantityTo(sourceSlot, destination, destinationSlot, (int)Number(args, "transfer", 3d));
        float sourceAmount = sourceSlot.itemData?.Stack.Amount ?? 0f, targetAmount = destinationSlot.itemData?.Stack.Amount ?? 0f;
        return new JObject
        {
            ["consumed"] = consumed, ["afterConsume"] = afterConsume, ["excessiveAccepted"] = excessiveAccepted,
            ["foreignAccepted"] = foreignAccepted, ["transferred"] = transferred,
            ["preflight"] = preflight, ["preflightUnchanged"] = preflightUnchanged, ["foreignPreflight"] = foreignPreflight,
            ["sourceAmount"] = sourceAmount, ["targetAmount"] = targetAmount,
            ["sourceEmpty"] = sourceSlot.itemData == null,
            ["totalWeight"] = source.CurrentCarryWeight + destination.CurrentCarryWeight
        };
    }

    private static JObject EntityInventoryCapacity(JObject args)
    {
        var inventory = new Inventory_Data(new List<ItemSlot>(), "independent capacity");
        if (Flag(args, "playerBag", false)) inventory.ConfigurePlayerBagCapacity(10f, 10f);
        else inventory.ConfigureStorageContainerCapacity(10f, 10f);
        inventory.SetUnlimitedCarryCapacity(Flag(args, "unlimited", false));
        var item = EntityProbeData("test:capacity", 1f, 2f, 3f);
        var other = EntityProbeData("test:capacity", 1f, 2f, 3f);
        item.CraftedDurabilityMultiplier = (float)Number(args, "quality", 1d);
        other.CraftedDurabilityMultiplier = (float)Number(args, "otherQuality", 1d);
        item.ItemSpecialData = Text(args, "special", string.Empty);
        other.ItemSpecialData = Text(args, "otherSpecial", string.Empty);
        other.Stack.Stackable = Flag(args, "otherStackable", true);
        return new JObject
        {
            ["allowed"] = inventory.GetCapacityLimitedAmount(item, (float)Number(args, "request", 10d),
                (float)Number(args, "currentWeight", 0d), (float)Number(args, "currentVolume", 0d)),
            ["weightLimit"] = inventory.MaxAllowedCarryWeight, ["slots"] = inventory.itemSlots.Count,
            ["sameIdentity"] = item.HasSameStackIdentity(other), ["canStack"] = item.CanStackWith(other),
            ["wholeLarge"] = Inventory_Data.GetWholeStackAmount(float.MaxValue),
            ["wholeNegative"] = Inventory_Data.GetWholeStackAmount(-.5f)
        };
    }

    private static JObject EntityCraftingAllocation(JObject args)
    {
        var slots = new List<ItemSlot>
        {
            new(0) { itemData = EntityProbeData("test:oak", (float)Number(args, "oak", 1d)) },
            new(1) { itemData = EntityProbeData("test:birch", (float)Number(args, "birch", 1d)) }
        };
        slots[0].itemData.Tags.Add("wood"); slots[1].itemData.Tags.Add("wood");
        var ingredients = new List<RuntimeRecipeIngredient>
        {
            new() { matchMode = MatchMode.ByTag, Tag = " WOOD ", amount = (int)Number(args, "tagDemand", 1d) },
            new() { matchMode = MatchMode.ExactItem, ItemName = " TEST:OAK ", amount = (int)Number(args, "exactDemand", 1d) }
        };
        if (Flag(args, "tool", false)) ingredients.Add(new RuntimeRecipeIngredient { ItemName = "test:tool", amount = 0 });
        if (Flag(args, "toolPresent", false)) slots.Add(new ItemSlot(2) { itemData = EntityProbeData("test:tool", 1f) });
        if (Flag(args, "reverse", false)) ingredients.Reverse();
        bool matched = CraftingMaterialAllocator.TryAllocate(slots, ingredients, out List<CraftingConsumption> plan);
        var allocations = new JArray();
        float total = 0f;
        foreach (CraftingConsumption consumption in plan)
        {
            total += consumption.Amount;
            allocations.Add(new JObject { ["slot"] = consumption.SlotIndex, ["amount"] = consumption.Amount });
        }
        return new JObject
        {
            ["matched"] = matched, ["total"] = total, ["plan"] = allocations,
            ["oakAfter"] = slots[0].itemData.Stack.Amount, ["birchAfter"] = slots[1].itemData.Stack.Amount
        };
    }

    private static JObject EntityCraftingTransaction(JObject args)
    {
        var input = new Inventory
        {
            Data = new Inventory_Data(new List<ItemSlot>
                { new(0) { itemData = EntityProbeData("test:material", (float)Number(args, "amount", 5d)) } }, "independent crafting input")
        };
        var outputSlots = new List<ItemSlot>();
        for (int index = 0; index < (int)Number(args, "outputSlots", 2d); index++) outputSlots.Add(new ItemSlot(index));
        var output = new Inventory { Data = new Inventory_Data(outputSlots, "independent crafting output") };
        var outputs = new List<ItemData>();
        for (int index = 0; index < 2; index++)
        {
            Data_GeneralItem produced = EntityProbeData("test:output_" + index, 1f);
            produced.Stack.Stackable = false;
            outputs.Add(produced);
        }
        var match = new CraftingRecipeMatch(new RuntimeRecipe { Id = "test:atomic_recipe" }, false,
            new[] { new CraftingConsumption(0, 2f) });
        bool prepared = CraftingTransaction.TryCreate(input, output, match, outputs, false,
            out CraftingTransaction transaction, out CraftingResult prepareFailure);
        float inputBeforeCommit = EntityInventoryAmount(input.Data), outputBeforeCommit = EntityInventoryAmount(output.Data);
        if (Flag(args, "changeShape", false)) output.Data.itemSlots.Add(new ItemSlot(output.Data.itemSlots.Count));
        bool committed = false, duplicateCommitted = false;
        CraftingResult commitFailure = null;
        if (prepared)
        {
            committed = transaction.Commit(out commitFailure);
            if (committed)
            {
                transaction.Complete();
                duplicateCommitted = transaction.Commit(out _);
            }
        }
        float inputAfterCommit = EntityInventoryAmount(input.Data), outputAfterCommit = EntityInventoryAmount(output.Data);
        if (prepared && Flag(args, "rollback", false)) transaction.Rollback();
        return new JObject
        {
            ["prepared"] = prepared, ["committed"] = committed, ["duplicateCommitted"] = duplicateCommitted,
            ["inputBeforeCommit"] = inputBeforeCommit, ["outputBeforeCommit"] = outputBeforeCommit,
            ["inputAfterCommit"] = inputAfterCommit, ["outputAfterCommit"] = outputAfterCommit,
            ["inputFinal"] = EntityInventoryAmount(input.Data), ["outputFinal"] = EntityInventoryAmount(output.Data),
            ["prepareFailure"] = prepareFailure?.FailureReason.ToString(), ["commitFailure"] = commitFailure?.FailureReason.ToString()
        };
    }

    // 读取正式木板配方并执行匹配、事务和成功动作，采样连续制作时斧头是否原样保留。
    private static JObject EntityCraftingPlankTool(JObject args)
    {
        string recipeRoot = StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath, RecipeCatalogLoader.RelativeRecipeRoot);
        RecipeCatalogDto catalog = RecipeRuntimeFactory.Deserialize(StreamingAssetsTextLoader.ReadAllText(
            RecipeCatalogLoader.ResolvePackagePath(recipeRoot, "crafting/survival.json")));
        RuntimeRecipe recipe = RecipeRuntimeFactory.Build(catalog.Recipes.Find(value => value.Id == "core:木板_切削合成"), _ => true);
        Data_GeneralItem tool = EntityProbeData("test:crafting_axe", 1f);
        tool.Guid = 1701;
        tool.Tags.Add("Axe");
        tool.Stack.Stackable = false;
        tool.Durability = (float)Number(args, "durability", 1d);
        tool.MaxDurability = 1f;
        bool toolPresent = Flag(args, "toolPresent", true);
        var input = new Inventory
        {
            Data = new Inventory_Data(new List<ItemSlot>
            {
                new(0) { itemData = EntityProbeData("Log", 3f) },
                new(1) { itemData = toolPresent ? tool : null }
            }, "independent plank input")
        };
        bool fullOutput = Flag(args, "fullOutput", false);
        var output = new Inventory
        {
            Data = new Inventory_Data(new List<ItemSlot>
            {
                new(0) { itemData = fullOutput ? EntityProbeData("test:blocker", 1f) : null }
            }, "independent plank output")
        };
        var outputs = new List<ItemData>();
        foreach (RuntimeRecipeResult result in recipe.outputs.results)
            outputs.Add(EntityProbeData(result.ItemName, result.amount));
        var attempts = new JArray();
        for (int index = 0; index < 3; index++)
        {
            bool matched = CraftingRecipeMatcher.TryMatchRecipe(input, recipe, new CraftingCapabilities(), out CraftingRecipeMatch match);
            bool prepared = false, committed = false;
            if (matched)
            {
                prepared = CraftingTransaction.TryCreate(input, output, match, outputs, false,
                    out CraftingTransaction transaction, out _);
                if (prepared && transaction.Commit(out _))
                {
                    RecipeActionRunner.Execute(recipe, input);
                    transaction.Complete();
                    committed = true;
                }
            }
            ItemData currentTool = input.Data.itemSlots[1].itemData;
            attempts.Add(new JObject
            {
                ["matched"] = matched, ["prepared"] = prepared, ["committed"] = committed,
                ["logs"] = input.Data.itemSlots[0].itemData?.Stack?.Amount ?? 0f,
                ["outputId"] = output.Data.itemSlots[0].itemData?.IDName,
                ["outputAmount"] = output.Data.itemSlots[0].itemData?.Stack?.Amount ?? 0f,
                ["toolPresent"] = currentTool != null, ["toolGuid"] = currentTool?.Guid ?? 0,
                ["toolAmount"] = currentTool?.Stack?.Amount ?? 0f,
                ["toolDurability"] = currentTool?.Durability ?? 0f,
                ["toolMaxDurability"] = currentTool?.MaxDurability ?? 0f
            });
        }
        return new JObject { ["recipeId"] = recipe.Id, ["attempts"] = attempts };
    }

    private static float EntityInventoryAmount(Inventory_Data data)
    {
        float amount = 0f;
        foreach (ItemSlot slot in data.itemSlots) amount += slot.itemData?.Stack?.Amount ?? 0f;
        return amount;
    }

    #endregion

    #region 容器、任务信号与事件日历

    private static JObject EntityContainerWholeWorldTransfer(JObject args)
    {
        float amount = (float)Number(args, "amount", 8d);
        int capacity = (int)Number(args, "capacity", 8d);
        float retainedFraction = (float)Number(args, "retainedFraction", 0d);
        return new JObject
        {
            ["transfer"] = LiquidVesselOperations.GetWholeWorldServings((float)Number(args, "requested", 1d), amount),
            ["spill"] = LiquidVesselOperations.GetWholeSpillServings(amount, capacity, retainedFraction),
            ["fill"] = LiquidVesselOperations.GetWholeWorldServings(capacity, capacity - amount)
        };
    }

    private static JObject EntityContainerWholeBatch(JObject args)
    {
        var state = new LiquidContainerState { RandomState = 12345, Temperature = 20f };
        state.Composition["test:water"] = (float)Number(args, "water", 2.5d);
        state.Composition["test:oil"] = .5f;
        state.PendingOutputs["test-output"] = new LiquidPendingBatch { LiquidId = "test:oil", Servings = .5f };
        if (Flag(args, "reservedElsewhere", false))
            state.PendingOutputs["other-output"] = new LiquidPendingBatch { LiquidId = "test:water", Servings = 1f };
        MixedLiquidContents.SynchronizeAmounts(state);
        var samples = new JArray();
        for (int index = 0; index < 4; index++)
        {
            bool reserved = MixedLiquidContents.Reserve(state, "test-output", out var batch, 1f);
            bool consumed = reserved && MixedLiquidContents.ConsumeBatch(state, "test-output", batch, 1f);
            samples.Add(new JObject { ["reserved"] = reserved, ["consumed"] = consumed,
                ["liquidId"] = batch.LiquidId, ["servings"] = batch.Servings, ["remaining"] = state.Amount });
        }
        return new JObject { ["samples"] = samples, ["water"] = MixedLiquidContents.GetAmount(state, "test:water"),
            ["oil"] = MixedLiquidContents.GetAmount(state, "test:oil"), ["remaining"] = state.Amount,
            ["otherReserved"] = state.PendingOutputs.TryGetValue("other-output", out var other) ? other.Servings : 0f };
    }

    private static JObject EntityContainerComposition(JObject args)
    {
        var state = new LiquidContainerState { RandomState = 12345, Temperature = 20f };
        state.Composition["test:water"] = (float)Number(args, "water", 3d);
        state.Composition["test:oil"] = (float)Number(args, "oil", 2d);
        MixedLiquidContents.SynchronizeAmounts(state);
        float initial = state.Amount;
        bool reserved = MixedLiquidContents.Reserve(state, "test-output", out LiquidTransferBatch batch);
        uint randomAfterReserve = state.RandomState;
        bool repeatReserved = MixedLiquidContents.Reserve(state, "test-output", out LiquidTransferBatch repeat);
        bool repeatRandomStable = randomAfterReserve == state.RandomState;
        var snapshot = MixedLiquidContents.Clone(state);
        bool cloneIndependent = !ReferenceEquals(snapshot.Composition, state.Composition) &&
            !ReferenceEquals(snapshot.PendingOutputs, state.PendingOutputs);
        bool wrongPort = MixedLiquidContents.ConsumeBatch(state, "wrong-output", batch, .5f);
        bool consumed = MixedLiquidContents.ConsumeBatch(state, "test-output", batch, (float)Number(args, "consume", .5d));
        float afterConsume = state.Amount;
        float pendingAfter = state.PendingOutputs.TryGetValue("test-output", out LiquidPendingBatch pending) ? pending.Servings : 0f;
        bool exceeded = MixedLiquidContents.ConsumeBatch(state, "test-output", batch, 100f);
        float afterRejected = state.Amount;
        MixedLiquidContents.Restore(state, snapshot);
        return new JObject
        {
            ["initial"] = initial, ["reserved"] = reserved, ["repeatReserved"] = repeatReserved,
            ["sameBatch"] = batch.LiquidId == repeat.LiquidId && batch.Servings == repeat.Servings,
            ["reserveRandomStable"] = repeatRandomStable,
            ["wrongPortAccepted"] = wrongPort, ["consumed"] = consumed, ["afterConsume"] = afterConsume,
            ["pendingAfter"] = pendingAfter, ["exceededAccepted"] = exceeded, ["afterRejected"] = afterRejected,
            ["restored"] = state.Amount, ["restoredPending"] = state.PendingOutputs.Count,
            ["restoredWater"] = MixedLiquidContents.GetAmount(state, "test:water"),
            ["restoredOil"] = MixedLiquidContents.GetAmount(state, "test:oil"),
            ["restoredBatchServings"] = state.PendingOutputs.TryGetValue("test-output", out LiquidPendingBatch restoredBatch) ? restoredBatch.Servings : 0f,
            ["cloneIndependent"] = cloneIndependent,
            ["quantized"] = LiquidVesselOperations.Quantize((float)Number(args, "quantize", 2.9d))
        };
    }

    private static JObject EntityQuestSignal(JObject args)
    {
        var definition = new QuestObjectiveDefinition
        {
            Id = "test-objective", Type = QuestBuiltInTypes.SignalCount,
            Required = (float)Number(args, "required", 3d),
            Parameters = new JObject
            {
                ["eventType"] = Text(args, "eventType", "item.picked_up"),
                ["targetId"] = Text(args, "target", "test:ore"),
                ["dimensionId"] = Text(args, "dimension", "surface")
            }
        };
        var handler = new SignalCountObjectiveHandler();
        bool valid = handler.Validate(definition, out string error);
        var signal = new GameplayProgressSignal(null, Text(args, "signalType", "ITEM.PICKED_UP"),
            Text(args, "signalTarget", "TEST:ORE"), (float)Number(args, "amount", 2d),
            new JObject { [GameplayProgressEvents.DimensionIdPayloadKey] = Text(args, "signalDimension", "SURFACE") });
        float first = handler.ApplySignal(definition, (float)Number(args, "current", 0d), signal);
        float second = handler.ApplySignal(definition, first, signal);
        return new JObject { ["valid"] = valid, ["error"] = error, ["first"] = first, ["second"] = second };
    }

    private static JObject EntityQuestRewardPlan(JObject args)
    {
        return EntityWithQuestPlayer(player =>
        {
            var slots = new List<ItemSlot>();
            for (int index = 0; index < (int)Number(args, "slots", 2d); index++) slots.Add(new ItemSlot(index));
            var inventory = new Inventory { Data = new Inventory_Data(slots, "independent quest reward") };
            Mod_Inventory bag = player.gameObject.AddComponent<Mod_Inventory>();
            bag.Data.ModuleId = ModText.Bag;
            bag.Data.StableName = "test-quest-bag";
            bag.InventoryInstances.Add(inventory);
            player.itemMods.AddMod(bag);
            var plan = new QuestRewardPlan();
            for (int index = 0; index < 2; index++)
            {
                Data_GeneralItem item = EntityProbeData("test:quest_reward_" + index, 1f);
                item.Stack.Stackable = false;
                plan.AddItem(item);
            }
            plan.AddSignal(new GameplayProgressSignal(player, "test:deferred_reward", "test:reward", 1f));
            bool committed = plan.TryCommit(player, out IReadOnlyList<GameplayProgressSignal> deferred, out string error);
            int occupied = 0;
            foreach (ItemSlot slot in inventory.Data.itemSlots) if (slot.itemData != null) occupied++;
            return new JObject
            {
                ["committed"] = committed, ["error"] = error, ["amount"] = EntityInventoryAmount(inventory.Data),
                ["occupied"] = occupied, ["deferredCount"] = deferred.Count,
                ["deferredType"] = deferred.Count > 0 ? deferred[0].Type : string.Empty,
                ["firstItem"] = slots.Count > 0 ? slots[0].itemData?.IDName : null
            };
        });
    }

    private static JObject EntityQuestInventoryObjective(JObject args)
    {
        return EntityWithQuestPlayer(player =>
        {
            var first = new Inventory
            {
                Data = new Inventory_Data(new List<ItemSlot>
                    { new(0) { itemData = EntityProbeData("test:objective", 2f) } }, "first objective inventory")
            };
            var second = new Inventory
            {
                Data = new Inventory_Data(new List<ItemSlot>
                    { new(0) { itemData = EntityProbeData("test:objective", 4f) } }, "second objective inventory")
            };
            Mod_Inventory bag = player.gameObject.AddComponent<Mod_Inventory>();
            bag.Data.ModuleId = ModText.Bag;
            bag.Data.StableName = "test-objective-bag";
            bag.InventoryInstances.Add(first);
            bag.InventoryInstances.Add(new Inventory { Data = first.Data });
            bag.InventoryInstances.Add(second);
            player.itemMods.AddMod(bag);
            var definition = new QuestObjectiveDefinition
            {
                Id = "test-state-objective", Type = QuestBuiltInTypes.InventoryOwns,
                Required = (float)Number(args, "required", 5d),
                Parameters = new JObject { ["itemId"] = Text(args, "itemId", "TEST:OBJECTIVE") }
            };
            var handler = new InventoryOwnsObjectiveHandler();
            bool valid = handler.Validate(definition, out string error);
            return new JObject
            {
                ["valid"] = valid, ["error"] = error, ["progress"] = handler.EvaluateState(player, definition),
                ["firstUnchanged"] = EntityInventoryAmount(first.Data), ["secondUnchanged"] = EntityInventoryAmount(second.Data)
            };
        });
    }

    private static JObject EntityQuestProgressStore(JObject args)
    {
        var playerData = new Data_Player { ItemSpecialData = "{\"other\":{\"value\":42}}" };
        var document = new QuestProgressSaveDocument();
        var record = new QuestProgressSaveRecord
        {
            Status = QuestStatus.ReadyToClaim, CurrentStageId = "test-stage", RewardsClaimed = false,
            ObjectiveProgress = new Dictionary<string, float> { ["test-goal"] = 3f }
        };
        document.Quests["test:quest"] = record;
        QuestProgressStore.Save(playerData, document);
        QuestProgressSaveDocument restored = QuestProgressStore.Load(playerData);
        bool caseInsensitive = restored.Quests.TryGetValue("TEST:QUEST", out QuestProgressSaveRecord ready);
        string readyStatus = ready.Status.ToString();
        float readyProgress = ready.ObjectiveProgress["TEST-GOAL"];
        ready.Status = QuestStatus.Completed;
        ready.RewardsClaimed = true;
        ready.CompletionCount = 1;
        QuestProgressStore.Save(playerData, restored);
        QuestProgressSaveRecord completed = QuestProgressStore.Load(playerData).Quests["test:quest"];
        JObject root = JObject.Parse(playerData.ItemSpecialData);
        JObject higherVersion = ItemSpecialDataJsonStore.ReadNamespace(playerData, QuestProgressStore.NamespaceKey);
        higherVersion["version"] = QuestProgressStore.CurrentVersion + 1;
        ItemSpecialDataJsonStore.WriteNamespace(playerData, QuestProgressStore.NamespaceKey, higherVersion);
        string beforeRejectedRead = playerData.ItemSpecialData;
        bool higherVersionRejected = false;
        try { QuestProgressStore.Load(playerData); }
        catch (System.IO.InvalidDataException) { higherVersionRejected = true; }
        return new JObject
        {
            ["caseInsensitive"] = caseInsensitive, ["readyStatus"] = readyStatus, ["readyProgress"] = readyProgress,
            ["completedStatus"] = completed.Status.ToString(), ["rewardsClaimed"] = completed.RewardsClaimed,
            ["completionCount"] = completed.CompletionCount, ["otherValue"] = root["other"]?["value"],
            ["higherVersionRejected"] = higherVersionRejected,
            ["rejectedReadUnchanged"] = beforeRejectedRead == playerData.ItemSpecialData
        };
    }

    private static JObject EntityEventDaySchedule(JObject args)
    {
        var trigger = new DayScheduleGameEventTrigger();
        var parameters = new JObject
        {
            ["minimumDay"] = (int)Number(args, "minimumDay", 2d),
            ["repeatEveryDays"] = (int)Number(args, "repeat", 1d),
            ["timeOfDay"] = (float)Number(args, "timeOfDay", 25d),
            ["chance"] = (float)Number(args, "chance", 1d)
        };
        bool valid = trigger.Validate(parameters, out string error);
        var progress = new GameEventProgressSaveData { LastEvaluatedDayNumber = (int)Number(args, "lastDay", -1d) };
        var results = new List<GameEventOccurrence>();
        var definition = new GameEventDefinition { Id = "test:independent_calendar" };
        var context = new GameEventTriggerContext("test-only", "test-only", (float)Number(args, "old", 0d),
            (float)Number(args, "new", 325d), (float)Number(args, "dayLength", 100d), 12345);
        if (valid) trigger.CollectOccurrences(context, definition, parameters, progress, results);
        var days = new JArray(); var times = new JArray();
        foreach (GameEventOccurrence occurrence in results) { days.Add(occurrence.DayNumber); times.Add(occurrence.TriggeredTotalTime); }
        results.Clear();
        if (valid) trigger.CollectOccurrences(context, definition, parameters, progress, results);
        return new JObject
        {
            ["valid"] = valid, ["error"] = error, ["days"] = days, ["times"] = times,
            ["repeatCount"] = results.Count, ["lastDay"] = progress.LastEvaluatedDayNumber
        };
    }

    #endregion

    #region 独立夹具生命周期

    private static Data_GeneralItem EntityProbeData(string id, float amount, float weight = 1f, float volume = 1f)
    {
        return new Data_GeneralItem { IDName = id, Stack = new ItemStack { Amount = amount, Weight = weight, Volume = volume } };
    }

    private static JObject WithEntityProbeItem(Func<GameItem, JObject> action)
    {
        var root = new GameObject("GameplayTest Independent Entity Probe") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        GameItem item = root.AddComponent<GameItem>();
        try { return action(item); }
        finally
        {
            try { item.PrepareForDespawn(saveData: false); }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }

    private static JObject EntityWithQuestPlayer(Func<Player, JObject> action)
    {
        var root = new GameObject("GameplayTest Independent Quest Player") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        Player player = root.AddComponent<Player>();
        try { return action(player); }
        finally
        {
            try { player.PrepareForDespawn(saveData: false); }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }

    #endregion
}
