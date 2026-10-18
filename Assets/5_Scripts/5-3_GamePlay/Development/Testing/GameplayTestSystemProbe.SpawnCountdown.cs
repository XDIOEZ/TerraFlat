using System;
using System.Collections.Generic;
using System.IO;
using FlatWorld.WorldModel;
using MemoryPack;
using Newtonsoft.Json.Linq;
using Unity.Mathematics;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 正式自然动物倒计时采样

    private static void RegisterSpawnCountdownOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("ai/spawn_countdown", EntitySpawnCountdown);
        operations.Add("ai/spawn_batches", EntitySpawnBatches);
        operations.Add("ai/creature_request", EntityCreatureRequest);
        operations.Add("ai/tree_habitat_batch", EntityTreeHabitatBatch);
    }

    // 采正式入口使用的请求守卫，不创建世界或修改单例。
    private static JObject EntityCreatureRequest(JObject args)
    {
        return new JObject
        {
            ["valid"] = MonsterSpawnerManager.IsValidCreatureRequest("Chicken", Vector3.zero, true, true),
            ["client"] = MonsterSpawnerManager.IsValidCreatureRequest("Chicken", Vector3.zero, false, true),
            ["unready"] = MonsterSpawnerManager.IsValidCreatureRequest("Chicken", Vector3.zero, true, false),
            ["blank"] = MonsterSpawnerManager.IsValidCreatureRequest(" ", Vector3.zero, true, true),
            ["nan"] = MonsterSpawnerManager.IsValidCreatureRequest("Chicken", new Vector3(float.NaN, 0f), true, true),
            ["infinity"] = MonsterSpawnerManager.IsValidCreatureRequest("Chicken", new Vector3(0f, float.PositiveInfinity), true, true)
        };
    }

    /// <summary>树木栖息地批量寻址必须保持群系、接缝及距离边界与旧入口一致。</summary>
    private static JObject EntityTreeHabitatBatch(JObject args)
    {
        var domain = new WorldTopologyDomain(new int2(-16, -16), new int2(32, 32), true);
        var players = new List<Vector3> { new(15.5f, 15.5f, 0f), new(0f, 0f, 0f) };
        bool seamNear = MonsterSpawnerManager.IsHabitatTreeNearPlayers(
            new Vector2(-15.5f, -15.5f), 2f, players, domain);
        bool seamTooFar = MonsterSpawnerManager.IsHabitatTreeNearPlayers(
            new Vector2(-15.5f, -15.5f), 1.4f, players, domain);
        bool flatTooFar = MonsterSpawnerManager.IsHabitatTreeNearPlayers(
            new Vector2(-15.5f, -15.5f), 2f, players, default);
        bool exactBoundary = MonsterSpawnerManager.IsHabitatTreeNearPlayers(
            new Vector2(2f, 0f), 2f, players, default);
        bool withinBoundary = MonsterSpawnerManager.IsHabitatTreeNearPlayers(
            new Vector2(2f, 0f), 2.1f, players, default);
        bool emptyPlayers = MonsterSpawnerManager.IsHabitatTreeNearPlayers(
            Vector2.zero, 5f, Array.Empty<Vector3>(), domain);

        using var world = new WorldRuntime("habitat_batch", 1);
        var profile = new ChunkGenerationProfileSnapshot("habitat_batch", 1, 16, 16);
        var address = new FlatWorld.WorldModel.WorldAddress("surface", new Int2(0, 0));
        ChunkGenerationRequest request = world.BeginChunkGeneration(address, 1, profile);
        using var buffer = new ChunkTerrainBuffer(16, 16);
        buffer.SetCell(2, 3, new TerrainCell(1, 0, 0, (int)SurfaceBiomeKind.Forest,
            1, TerrainCellFlags.Walkable));
        buffer.SetEnvironmentValue(TerrainSupportLayer.TileIdLayer, 2, 3, 90f);
        buffer.SetEnvironmentValue(TerrainSupportLayer.CostLayer, 2, 3, 3f);
        buffer.SetCell(14, 3, new TerrainCell(1, 0, 0, (int)SurfaceBiomeKind.Desert,
            1, TerrainCellFlags.Walkable));
        buffer.SetCell(15, 15, new TerrainCell(1, 0, 0, 999,
            1, TerrainCellFlags.Walkable));
        using (var result = new ChunkGenerationResult(request, buffer))
            if (!world.TryCommit(result, out string reason))
                throw new InvalidOperationException("树木群系夹具提交失败：" + reason);

        var query = new ChunkMgr.RuntimeTerrainPresenceQuery(world, default, "surface", 16, 16);
        var samples = new[]
        {
            new Vector2(2.5f, 3.5f), new Vector2(14.5f, 3.5f),
            new Vector2(15.5f, 15.5f), new Vector2(5.5f, 7.5f),
            new Vector2(16.5f, 3.5f), new Vector2(-1.5f, 3.5f)
        };
        bool matchesOriginal = true;
        int matched = 0, missing = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            bool quick = query.TryGetBiomeName(samples[i], out string quickName);
            bool loaded = query.TrySample(samples[i], out RuntimeTerrainTileSample original);
            string oldName = loaded ? SurfaceBiomeClassifier.GetLegacyName(original.Cell.BiomeId) : string.Empty;
            bool oldSuccess = loaded && !string.IsNullOrWhiteSpace(oldName);
            matchesOriginal &= quick == oldSuccess && string.Equals(quickName, oldName, StringComparison.Ordinal);
            if (quick) matched++;
            else missing++;
        }
        var wrappedQuery = new ChunkMgr.RuntimeTerrainPresenceQuery(world,
            new WorldTopologyDomain(new int2(0, 0), new int2(32, 32), true), "surface", 16, 16);
        bool wrappedBiome = wrappedQuery.TryGetBiomeName(new Vector2(34.5f, 3.5f), out string wrappedName) &&
            wrappedQuery.TrySample(new Vector2(34.5f, 3.5f), out RuntimeTerrainTileSample wrappedSample) &&
            string.Equals(wrappedName, SurfaceBiomeClassifier.GetLegacyName(wrappedSample.Cell.BiomeId),
                StringComparison.Ordinal);

        return new JObject
        {
            ["seamNear"] = seamNear,
            ["seamTooFar"] = seamTooFar,
            ["flatTooFar"] = flatTooFar,
            ["exactBoundary"] = exactBoundary,
            ["withinBoundary"] = withinBoundary,
            ["emptyPlayers"] = emptyPlayers,
            ["matchesOriginal"] = matchesOriginal,
            ["wrappedBiome"] = wrappedBiome,
            ["matched"] = matched,
            ["missing"] = missing
        };
    }

    // 仅创建独立配置与进度，调用生产计时、选择、完成及存档序列化入口。
    private static JObject EntitySpawnCountdown(JObject args)
    {
        SpawnerConfigCatalog catalog = SpawnerConfigCatalogLoader.LoadBuiltIn();
        var configs = new List<SpawnerConfig>();
        var result = new JObject();
        try
        {
            var modes = new JArray();
            SpawnerConfig bird = null, wildlife = null;
            foreach (SpawnerConfigDefinition definition in catalog.Configs)
            {
                SpawnerConfig config = definition.CreateRuntimeConfig();
                configs.Add(config);
                var entries = new JArray();
                foreach (SpawnerConfig.SpawnEntry entry in config.SpawnEntries)
                    entries.Add(new JObject { ["species"] = entry.PrefabName,
                        ["interval"] = MonsterSpawnerManager.GetCountdownInterval(config, entry, 1440d, 1f) });
                modes.Add(new JObject { ["id"] = config.PersistentId,
                    ["mode"] = config.ScheduleMode.ToString(), ["entries"] = entries });
                if (config.PersistentId == "bird_ecology") bird = config;
                if (config.PersistentId == "wildlife_resources") wildlife = config;
            }
            result["catalog"] = modes;
            if (bird == null || wildlife == null) throw new InvalidOperationException("倒计时采样缺少正式鸟或野生动物配置。");
            SpawnerConfig.SpawnEntry birdEntry = bird.SpawnEntries[0];
            double interval = MonsterSpawnerManager.GetCountdownInterval(bird, birdEntry, 1440d, 1f);
            var state = new SpawnerProgressSaveData();
            void Tick(double time) => MonsterSpawnerManager.AdvanceCountdownSchedule(bird, state, time, 1440d, 1f, () => 0f);
            Tick(100d);
            result["initial"] = CountdownSample(state, birdEntry.PrefabName);
            Tick(100d + interval - 0.25d);
            result["beforeDue"] = CountdownSample(state, birdEntry.PrefabName);
            var restored = MemoryPackSerializer.Deserialize<SpawnerProgressSaveData>(MemoryPackSerializer.Serialize(state));
            result["savedRemaining"] = CountdownSample(restored, birdEntry.PrefabName);
            restored.ResetCountdownObservation();
            MonsterSpawnerManager.AdvanceCountdownSchedule(bird, restored, 999999d, 1440d, 1f, () => 0f);
            result["offlineRestored"] = CountdownSample(restored, birdEntry.PrefabName);
            Tick(100d + interval);
            result["exactDue"] = CountdownSample(state, birdEntry.PrefabName);
            Tick(100d + interval * 100d);
            result["blockedRound"] = CountdownSample(state, birdEntry.PrefabName);
            var pendingSave = MemoryPackSerializer.Deserialize<SpawnerProgressSaveData>(MemoryPackSerializer.Serialize(state));
            result["savedPending"] = CountdownSample(pendingSave, birdEntry.PrefabName);
            MonsterSpawnerManager.CompleteCountdownSpawnAttempt(state, birdEntry, false, 0);
            result["chanceSkipped"] = CountdownSample(state, birdEntry.PrefabName);
            while (state.PendingSpawnCount > 0)
                MonsterSpawnerManager.CompleteCountdownSpawnAttempt(state, birdEntry, true, 0);
            result["completed"] = CountdownSample(state, birdEntry.PrefabName);
            Tick(100d + interval * 101d - 0.25d);
            result["nextBeforeDue"] = CountdownSample(state, birdEntry.PrefabName);
            Tick(100d + interval * 101d);
            result["nextExactDue"] = CountdownSample(state, birdEntry.PrefabName);

            var mixed = new SpawnerProgressSaveData();
            SpawnerConfig.SpawnEntry chicken = wildlife.SpawnEntries.Find(entry => entry.PrefabName == "Chicken");
            SpawnerConfig.SpawnEntry boar = wildlife.SpawnEntries.Find(entry => entry.PrefabName == "WildBoar");
            double chickenInterval = MonsterSpawnerManager.GetCountdownInterval(wildlife, chicken, 1440d, 1f);
            double boarInterval = MonsterSpawnerManager.GetCountdownInterval(wildlife, boar, 1440d, 1f);
            MonsterSpawnerManager.AdvanceCountdownSchedule(wildlife, mixed, 0d, 1440d, 1f, () => 0f);
            MonsterSpawnerManager.AdvanceCountdownSchedule(wildlife, mixed, chickenInterval, 1440d, 1f, () => 0f);
            result["independentChicken"] = CountdownSample(mixed, "Chicken");
            result["independentBoar"] = CountdownSample(mixed, "WildBoar");
            MonsterSpawnerManager.AdvanceCountdownSchedule(wildlife, mixed, boarInterval, 1440d, 1f, () => 0f);
            result["bothDueTotal"] = mixed.PendingSpawnCount;
            var selected = new JArray();
            for (int i = 0; i < 4; i++)
                selected.Add(MonsterSpawnerManager.SelectPendingCountdownEntry(wildlife, mixed)?.PrefabName);
            result["blockedSelectionOrder"] = selected;
            MonsterSpawnerManager.CompleteCountdownSpawnAttempt(mixed, chicken, false, 0);
            result["otherSpeciesPendingAfterSkip"] = CountdownSample(mixed, "WildBoar");
            result["weightedIntervalRatio"] = boarInterval / chickenInterval;
            result["originalWeightRatio"] = chicken.Probability / boar.Probability;

            var skipped = new SpawnerProgressSaveData();
            MonsterSpawnerManager.AdvanceCountdownSchedule(wildlife, skipped, 0d, 1440d, 1f, () => 1f);
            MonsterSpawnerManager.AdvanceCountdownSchedule(wildlife, skipped, chickenInterval, 1440d, 1f, () => 1f);
            result["roundChanceSkipped"] = CountdownSample(skipped, "Chicken");
            float configuredChance = wildlife.SpawnChance;
            int chanceCalls = 0;
            wildlife.SpawnChance = 1f;
            var always = new SpawnerProgressSaveData();
            MonsterSpawnerManager.AdvanceCountdownSchedule(wildlife, always, 0d, 1440d, 1f, () => { chanceCalls++; return 1f; });
            MonsterSpawnerManager.AdvanceCountdownSchedule(wildlife, always, chickenInterval, 1440d, 1f, () => { chanceCalls++; return 1f; });
            result["certainChancePending"] = always.SpeciesCountdowns["Chicken"].PendingCount;
            wildlife.SpawnChance = 0f;
            var never = new SpawnerProgressSaveData();
            MonsterSpawnerManager.AdvanceCountdownSchedule(wildlife, never, 0d, 1440d, 1f, () => { chanceCalls++; return 0f; });
            MonsterSpawnerManager.AdvanceCountdownSchedule(wildlife, never, chickenInterval, 1440d, 1f, () => { chanceCalls++; return 0f; });
            result["zeroChancePending"] = never.SpeciesCountdowns["Chicken"].PendingCount;
            result["boundaryChanceRollCalls"] = chanceCalls;
            wildlife.SpawnChance = configuredChance;
            var clock = new SpawnerCountdownProgress();
            clock.Advance(1e12d, 1d);
            result["preciseBeforeDue"] = clock.Advance(1e12d + 0.5d, 1d);
            result["preciseExactDue"] = clock.Advance(1e12d + 1d, 1d);
            int rejected = 0;
            foreach (double bad in new[] { 0d, -1d, double.NaN, double.PositiveInfinity })
                try { new SpawnerCountdownProgress().Advance(0d, bad); }
                catch (ArgumentOutOfRangeException) { rejected++; }
            result["invalidIntervalsRejected"] = rejected;
            var priority = new JArray();
            int cursor = 0;
            for (int i = 0; i < 9; i++) priority.Add(MonsterSpawnerManager.NextLifecycleWork(ref cursor));
            result["busyWorkPriorities"] = priority;
            result["interval"] = interval;
            result["batchSize"] = bird.SpawnCount;
            result["mixedBatchSize"] = wildlife.SpawnCount;
            return result;
        }
        finally
        {
            foreach (SpawnerConfig config in configs) UnityEngine.Object.DestroyImmediate(config);
        }
    }

    private static JObject CountdownSample(SpawnerProgressSaveData state, string species)
    {
        SpawnerCountdownProgress countdown = state.SpeciesCountdowns[species];
        return new JObject { ["remaining"] = countdown.RemainingGameSeconds,
            ["pending"] = countdown.PendingCount, ["totalPending"] = state.PendingSpawnCount,
            ["replacements"] = state.PendingReplacementCount, ["lifetime"] = state.LifetimeSpawnCount,
            ["budget"] = state.AvailableBudget };
    }

    // 采正式数量权重、整批容量需求、同批落点和二进制进度，不创建动物或修改当前世界。
    private static JObject EntitySpawnBatches(JObject args)
    {
        SpawnerConfigCatalog catalog = SpawnerConfigCatalogLoader.LoadBuiltIn();
        SpawnerConfigDefinition definition = catalog.Configs.Find(value => value.Id == "wolf_common_enemy");
        if (definition == null) throw new InvalidOperationException("缺少正式狼生成规则。");
        SpawnerConfig config = definition.CreateRuntimeConfig();
        try
        {
            var distribution = new int[4];
            for (int i = 0; i < 200; i++)
                distribution[MonsterSpawnerManager.DetermineCountdownBatchCount(config, 1f, () => (i + 0.5f) / 200f) - 1]++;
            var multiplierCounts = new JArray();
            foreach (float multiplier in new[] { 0.1f, 0.5f, 1f, 2f, 10f })
                multiplierCounts.Add(MonsterSpawnerManager.DetermineCountdownBatchCount(config, multiplier, () => 0.75f));
            var result = new JObject
            {
                ["distribution"] = new JArray(distribution), ["samples"] = 200,
                ["multiplierCounts"] = multiplierCounts, ["radius"] = config.BatchSpawnRadius,
                ["zeroSample"] = config.DetermineSpawnCount(0f), ["oneSample"] = config.DetermineSpawnCount(1f)
            };

            SpawnerConfig.SpawnEntry entry = config.SpawnEntries[0];
            double interval = MonsterSpawnerManager.GetCountdownInterval(config, entry, 1440d, 1f);
            int rolls = 0;
            float Roll() { rolls++; return 0.75f; }
            var state = new SpawnerProgressSaveData();
            MonsterSpawnerManager.AdvanceCountdownSchedule(config, state, 0d, 1440d, 1f, Roll);
            MonsterSpawnerManager.AdvanceCountdownSchedule(config, state, interval, 1440d, 1f, Roll);
            SpawnerCountdownProgress batch = state.SpeciesCountdowns[entry.PrefabName];
            result["initialBatchCount"] = batch.PendingCount;
            result["initialRequiredRoom"] = MonsterSpawnerManager.GetCountdownBatchRequiredRoom(batch);
            batch.RecordBatchBirth(10.5f, 20.5f, "surface");
            result["continuingRequiredRoom"] = MonsterSpawnerManager.GetCountdownBatchRequiredRoom(batch);
            MonsterSpawnerManager.CompleteCountdownSpawnAttempt(state, entry, true, 0);
            MonsterSpawnerManager.AdvanceCountdownSchedule(config, state, interval * 100d, 1440d, 1f, Roll);
            result["blockedPending"] = batch.PendingCount;
            result["chanceRollsAfterWait"] = rolls;
            var restored = MemoryPackSerializer.Deserialize<SpawnerProgressSaveData>(MemoryPackSerializer.Serialize(state));
            SpawnerCountdownProgress saved = restored.SpeciesCountdowns[entry.PrefabName];
            result["saved"] = new JObject { ["hasAnchor"] = saved.HasBatchAnchor, ["x"] = saved.BatchAnchorX,
                ["y"] = saved.BatchAnchorY, ["scene"] = saved.BatchAnchorScene, ["cells"] = saved.BatchSpawnCells.Count,
                ["pending"] = saved.PendingCount };

            result["duplicateCell"] = MonsterSpawnerManager.IsCountdownBatchCandidate(saved, new Vector3(10.5f, 20.5f), 3f, default);
            result["insideRadius"] = MonsterSpawnerManager.IsCountdownBatchCandidate(saved, new Vector3(12.5f, 21.5f), 3f, default);
            result["exactRadius"] = MonsterSpawnerManager.IsCountdownBatchCandidate(saved, new Vector3(13.5f, 20.5f), 3f, default);
            result["outsideRadius"] = MonsterSpawnerManager.IsCountdownBatchCandidate(saved, new Vector3(13.6f, 20.5f), 3f, default);
            var players = new List<Vector3> { new(0.5f, 20.5f) };
            result["anchorExactRange"] = MonsterSpawnerManager.IsCountdownBatchAnchorUsable(saved, "surface", players, 10f, default);
            result["anchorOutOfRange"] = MonsterSpawnerManager.IsCountdownBatchAnchorUsable(saved, "surface", players, 9.9f, default);
            result["anchorOtherScene"] = MonsterSpawnerManager.IsCountdownBatchAnchorUsable(saved, "cave", players, 50f, default);
            result["anchorNoPlayers"] = MonsterSpawnerManager.IsCountdownBatchAnchorUsable(saved, "surface", Array.Empty<Vector3>(), 50f, default);
            saved.BatchAnchorX = float.NaN;
            result["nanAnchor"] = MonsterSpawnerManager.IsCountdownBatchAnchorUsable(saved, "surface", players, 50f, default);
            saved.BatchAnchorX = float.PositiveInfinity;
            result["infiniteAnchor"] = MonsterSpawnerManager.IsCountdownBatchCandidate(saved, Vector3.zero, 3f, default);
            saved.ClearBatchAnchor();
            result["reanchorRequiredRoom"] = MonsterSpawnerManager.GetCountdownBatchRequiredRoom(saved);
            saved.RecordBatchBirth(15.5f, 0.5f, "surface");
            var wrapped = new WorldTopologyDomain(new int2(-16, -16), new int2(32, 32), true);
            result["wrappedAnchor"] = MonsterSpawnerManager.IsCountdownBatchAnchorUsable(saved, "surface",
                new[] { new Vector3(-15.5f, 0.5f) }, 1f, wrapped);
            result["wrappedCandidate"] = MonsterSpawnerManager.IsCountdownBatchCandidate(saved, new Vector3(-15.5f, 0.5f), 3f, wrapped);
            while (state.PendingSpawnCount > 0)
                MonsterSpawnerManager.CompleteCountdownSpawnAttempt(state, entry, true, 0);
            result["completed"] = new JObject { ["hasAnchor"] = batch.HasBatchAnchor, ["cells"] = batch.BatchSpawnCells.Count,
                ["pending"] = batch.PendingCount, ["remaining"] = batch.RemainingGameSeconds, ["interval"] = interval };
            batch.RecordBatchBirth(-10.5f, -20.5f, "surface");
            batch.BeginBatch(3);
            result["nextBatchCleared"] = !batch.HasBatchAnchor && batch.BatchSpawnCells.Count == 0;

            List<SpawnerBatchSizeWeight> validWeights = definition.SpawnCountWeights;
            float validRadius = definition.BatchSpawnRadius;
            var rejected = new JArray();
            var badWeights = new List<SpawnerBatchSizeWeight>[]
            {
                null, new() { new() { Count = 0, Weight = 1f } },
                new() { new() { Count = 3, Weight = 0f } }, new() { new() { Count = 3, Weight = -1f } },
                new() { new() { Count = 3, Weight = float.NaN } }, new() { new() { Count = 3, Weight = float.PositiveInfinity } },
                new() { new() { Count = 3, Weight = 1f }, new() { Count = 3, Weight = 1f } }
            };
            foreach (List<SpawnerBatchSizeWeight> weights in badWeights)
            {
                definition.SpawnCountWeights = weights;
                try { SpawnerConfigCatalogLoader.Validate(catalog); rejected.Add(false); }
                catch (InvalidDataException) { rejected.Add(true); }
            }
            definition.SpawnCountWeights = validWeights;
            foreach (float radius in new[] { -1f, float.NaN, float.PositiveInfinity })
            {
                definition.BatchSpawnRadius = radius;
                try { SpawnerConfigCatalogLoader.Validate(catalog); rejected.Add(false); }
                catch (InvalidDataException) { rejected.Add(true); }
            }
            definition.BatchSpawnRadius = validRadius;
            definition.ScheduleMode = "timedWindows";
            try { SpawnerConfigCatalogLoader.Validate(catalog); rejected.Add(false); }
            catch (InvalidDataException) { rejected.Add(true); }
            result["invalidConfigurationsRejected"] = rejected;
            config.SpawnCountWeights.Clear();
            int fixedRolls = 0;
            result["fixedCount"] = MonsterSpawnerManager.DetermineCountdownBatchCount(config, 2f, () => { fixedRolls++; return 0f; });
            result["fixedExpectedCount"] = GameDifficultyService.ScaleCount(config.SpawnCount, 2f, 1);
            result["fixedCountRolls"] = fixedRolls;
            return result;
        }
        finally { UnityEngine.Object.DestroyImmediate(config); }
    }

    #endregion
}
