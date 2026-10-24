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
        operations.Add("ai/spawner_update_work", EntitySpawnerUpdateWork);
        operations.Add("ai/spawn_monitor", EntitySpawnMonitor);
    }

    // 监控必须只读实际计时，不能为了显示推进时间、补建状态或重启批次。
    private static JObject EntitySpawnMonitor(JObject args)
    {
        SpawnerConfig config = ScriptableObject.CreateInstance<SpawnerConfig>();
        try
        {
            config.PersistentId = "monitor";
            var settings = new SpawnerRuntimeSettings { NaturalSpawnIntervalGameSeconds = 50f };
            var entry = new SpawnerConfig.SpawnEntry { PrefabName = "MonitorSpecies", Probability = 1f };
            var other = new SpawnerConfig.SpawnEntry { PrefabName = "OtherSpecies", Probability = 99f };
            var countdown = new SpawnerCountdownProgress
            {
                RemainingGameSeconds = 7.5d, IntervalGameSeconds = 80d, LastObservedGameSeconds = 500d
            };
            string before = Convert.ToBase64String(MemoryPackSerializer.Serialize(countdown));
            var observed = MonsterSpawnerManager.ReadCountdownMonitor(config, entry, countdown, settings, 2f);
            var paused = MonsterSpawnerManager.ReadCountdownMonitor(config, entry, countdown, settings, 0f);
            var otherRow = MonsterSpawnerManager.ReadCountdownMonitor(config, other, countdown, settings, 1f);
            bool unchanged = before == Convert.ToBase64String(MemoryPackSerializer.Serialize(countdown));
            var missing = MonsterSpawnerManager.ReadCountdownMonitor(config, entry, null, settings, 1f);
            countdown.BeginBatch(3);
            countdown.SelectedConfigId = config.PersistentId;
            countdown.SelectedSpeciesId = entry.PrefabName;
            before = Convert.ToBase64String(MemoryPackSerializer.Serialize(countdown));
            var pending = MonsterSpawnerManager.ReadCountdownMonitor(config, entry, countdown, settings, 1f);
            var otherPending = MonsterSpawnerManager.ReadCountdownMonitor(config, other, countdown, settings, 1f);
            bool batchUnchanged = before == Convert.ToBase64String(MemoryPackSerializer.Serialize(countdown));
            config.SpawnChance = 0f;
            var disabled = MonsterSpawnerManager.ReadCountdownMonitor(config, entry, countdown, settings, 1f);
            return new JObject
            {
                ["remaining"] = observed.RemainingGameSeconds, ["interval"] = observed.IntervalGameSeconds,
                ["initialized"] = observed.CountdownInitialized,
                ["pausedRemaining"] = paused.RemainingGameSeconds, ["pausedInterval"] = paused.IntervalGameSeconds,
                ["unchanged"] = unchanged, ["batchUnchanged"] = batchUnchanged,
                ["lastObserved"] = countdown.LastObservedGameSeconds, ["pending"] = pending.Pending,
                ["pendingRemaining"] = pending.RemainingGameSeconds,
                ["missingInitialized"] = missing.CountdownInitialized, ["missingRemaining"] = missing.RemainingGameSeconds,
                ["missingInterval"] = missing.IntervalGameSeconds,
                ["sameClock"] = otherRow.RemainingGameSeconds == observed.RemainingGameSeconds &&
                    otherRow.IntervalGameSeconds == observed.IntervalGameSeconds,
                ["otherPending"] = otherPending.Pending, ["disabled"] = !disabled.NaturalSpawnEnabled
            };
        }
        finally { UnityEngine.Object.DestroyImmediate(config); }
    }

    // 采正式空转守卫，确保初始化、分帧出生和到期调度都不会被提前跳过。
    private static JObject EntitySpawnerUpdateWork(JObject args)
    {
        return new JObject
        {
            ["initialization"] = MonsterSpawnerManager.RequiresUpdateWorldContext(false, false, false, false, 1f, 2f),
            ["idle"] = MonsterSpawnerManager.RequiresUpdateWorldContext(true, true, true, false, 1f, 2f),
            ["due"] = MonsterSpawnerManager.RequiresUpdateWorldContext(true, true, true, false, 2f, 2f),
            ["overdue"] = MonsterSpawnerManager.RequiresUpdateWorldContext(true, true, true, false, 3f, 2f),
            ["birthPending"] = MonsterSpawnerManager.RequiresUpdateWorldContext(true, true, true, true, 1f, 2f),
            ["birthCompleted"] = MonsterSpawnerManager.RequiresUpdateWorldContext(true, true, true, false, 1f, 2f),
            ["disabled"] = MonsterSpawnerManager.RequiresUpdateWorldContext(true, false, true, true, 3f, 2f),
            ["missingSettings"] = MonsterSpawnerManager.RequiresUpdateWorldContext(true, true, false, true, 3f, 2f),
            ["nanNow"] = MonsterSpawnerManager.RequiresUpdateWorldContext(true, true, true, false, float.NaN, 2f),
            ["nanNext"] = MonsterSpawnerManager.RequiresUpdateWorldContext(true, true, true, false, 1f, float.NaN),
            ["infiniteNow"] = MonsterSpawnerManager.RequiresUpdateWorldContext(true, true, true, false, float.PositiveInfinity, 2f),
            ["infiniteNext"] = MonsterSpawnerManager.RequiresUpdateWorldContext(true, true, true, false, 1f, float.PositiveInfinity),
            ["negativeInfinityNow"] = MonsterSpawnerManager.RequiresUpdateWorldContext(true, true, true, false, float.NegativeInfinity, 2f)
        };
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
        try
        {
            int speciesCount = 0;
            foreach (SpawnerConfigDefinition definition in catalog.Configs)
            {
                configs.Add(definition.CreateRuntimeConfig());
                speciesCount += definition.SpawnEntries.Count;
            }
            SpawnerConfig bird = configs.Find(value => value.PersistentId == "bird_ecology");
            SpawnerConfig wildlife = configs.Find(value => value.PersistentId == "wildlife_resources");
            SpawnerConfig fish = configs.Find(value => value.PersistentId == "small_fish_ecology");
            var entry = bird.SpawnEntries[0];
            double interval = MonsterSpawnerManager.GetCountdownInterval(catalog.Settings, 1f);
            var save = new MonsterSpawnerSaveData();
            var state = new SpawnerProgressSaveData();
            save.ConfigStates.Add(bird.PersistentId, state);
            SpawnerCountdownProgress clock = save.NaturalCountdown;
            bool Tick(double time) => MonsterSpawnerManager.AdvanceCountdownSchedule(clock, time, catalog.Settings, 1f);
            var result = new JObject { ["interval"] = interval, ["batchSize"] = bird.SpawnCount,
                ["speciesCount"] = speciesCount, ["ruleCount"] = configs.Count,
                ["zombieDisabled"] = configs.Find(value => value.PersistentId == "zombie_horde").SpawnChance == 0f };
            Tick(100d);
            result["initial"] = CountdownSample(clock, state);
            result["earlySelectionRejected"] = !MonsterSpawnerManager.TryBeginCountdownBatch(clock, bird, entry, state, 1f, () => 0f);
            Tick(100d + interval - 0.25d);
            result["beforeDue"] = CountdownSample(clock, state);
            var restored = MemoryPackSerializer.Deserialize<MonsterSpawnerSaveData>(MemoryPackSerializer.Serialize(save));
            result["savedRemaining"] = CountdownSample(restored.NaturalCountdown, restored.ConfigStates[bird.PersistentId]);
            restored.NaturalCountdown.LastObservedGameSeconds = -1d;
            MonsterSpawnerManager.AdvanceCountdownSchedule(restored.NaturalCountdown, 999999d, catalog.Settings, 1f);
            result["offlineRestored"] = CountdownSample(restored.NaturalCountdown, restored.ConfigStates[bird.PersistentId]);
            result["exactDue"] = Tick(100d + interval);
            result["notChosenAtExpiry"] = clock.PendingCount == 0 && clock.SelectedSpeciesId == null;
            MonsterSpawnerManager.TryBeginCountdownBatch(clock, bird, entry, state, 1f, () => 0f);
            Tick(100d + interval * 100d);
            result["blockedRound"] = CountdownSample(clock, state);
            restored = MemoryPackSerializer.Deserialize<MonsterSpawnerSaveData>(MemoryPackSerializer.Serialize(save));
            result["savedPending"] = CountdownSample(restored.NaturalCountdown, restored.ConfigStates[bird.PersistentId]);
            result["savedSelection"] = restored.NaturalCountdown.SelectedConfigId == bird.PersistentId &&
                restored.NaturalCountdown.SelectedSpeciesId == entry.PrefabName;
            result["secondSelectionRejected"] = !MonsterSpawnerManager.TryBeginCountdownBatch(clock, wildlife,
                wildlife.SpawnEntries[0], new SpawnerProgressSaveData(), 1f, () => 0f);
            MonsterSpawnerManager.CompleteCountdownSpawnAttempt(clock, state, false, 0, 100d + interval * 100d);
            result["chanceSkipped"] = CountdownSample(clock, state);
            while (clock.PendingCount > 0)
                MonsterSpawnerManager.CompleteCountdownSpawnAttempt(clock, state, true, 0, 100d + interval * 100d);
            result["completed"] = CountdownSample(clock, state);
            result["selectionCleared"] = clock.SelectedConfigId == null && clock.SelectedSpeciesId == null;
            result["nextBeforeDue"] = Tick(100d + interval * 101d - 0.25d);
            result["nextExactDue"] = Tick(100d + interval * 101d);

            var candidates = new List<MonsterSpawnerManager.NaturalSpawnCandidate>
            {
                new(bird, entry, Vector3.zero),
                new(wildlife, wildlife.SpawnEntries[0], Vector3.zero),
                new(wildlife, wildlife.SpawnEntries[1], Vector3.zero)
            };
            var distribution = new int[3];
            for (int i = 0; i < 10000; i++)
                distribution[MonsterSpawnerManager.SelectCountdownCandidate(candidates, (i + 0.5f) / 10000f)]++;
            double totalWeight = entry.Probability + (double)wildlife.SpawnEntries[0].Probability + wildlife.SpawnEntries[1].Probability;
            result["distribution"] = new JArray(distribution);
            result["expectedWeights"] = new JArray(entry.Probability / totalWeight,
                wildlife.SpawnEntries[0].Probability / totalWeight, wildlife.SpawnEntries[1].Probability / totalWeight);
            candidates.Clear();
            result["emptySelection"] = MonsterSpawnerManager.SelectCountdownCandidate(candidates, 0f);
            candidates.Add(new(fish, fish.SpawnEntries[0], Vector3.zero));
            result["filteredSelection"] = candidates[MonsterSpawnerManager.SelectCountdownCandidate(candidates, 1f)].Entry.PrefabName;
            result["weightDoesNotChangeClock"] = MonsterSpawnerManager.GetCountdownInterval(catalog.Settings, 1f) == interval;

            var skipped = new SpawnerCountdownProgress();
            skipped.Advance(0d, interval); skipped.Advance(interval, interval);
            MonsterSpawnerManager.TryBeginCountdownBatch(skipped, wildlife, wildlife.SpawnEntries[0],
                new SpawnerProgressSaveData(), 1f, () => 1f);
            result["roundChanceSkipped"] = skipped.PendingCount == 0 && skipped.RemainingGameSeconds == interval;
            int rolls = 0;
            wildlife.SpawnChance = 1f;
            skipped.Advance(interval * 2d, interval);
            result["certainChance"] = MonsterSpawnerManager.TryBeginCountdownBatch(skipped, wildlife,
                wildlife.SpawnEntries[0], new SpawnerProgressSaveData(), 1f, () => { rolls++; return 1f; });
            skipped.Restart(interval); skipped.Advance(interval * 3d, interval);
            wildlife.SpawnChance = 0f;
            result["zeroChance"] = MonsterSpawnerManager.TryBeginCountdownBatch(skipped, wildlife,
                wildlife.SpawnEntries[0], new SpawnerProgressSaveData(), 1f, () => { rolls++; return 0f; });
            result["boundaryChanceRollCalls"] = rolls;

            var paused = new SpawnerCountdownProgress();
            MonsterSpawnerManager.AdvanceCountdownSchedule(paused, 0d, catalog.Settings, 1f);
            MonsterSpawnerManager.AdvanceCountdownSchedule(paused, interval / 4d, catalog.Settings, 1f);
            double beforePause = paused.RemainingGameSeconds;
            MonsterSpawnerManager.AdvanceCountdownSchedule(paused, 999999d, catalog.Settings, 0f);
            MonsterSpawnerManager.AdvanceCountdownSchedule(paused, 1999999d, catalog.Settings, 1f);
            result["pausePreservesRemaining"] = paused.RemainingGameSeconds == beforePause;
            MonsterSpawnerManager.AdvanceCountdownSchedule(paused, 1999999d, catalog.Settings, 2f);
            result["frequencyScalesRemaining"] = paused.RemainingGameSeconds == beforePause / 2d;
            result["frequencyScalesInterval"] = paused.IntervalGameSeconds == interval / 2d;
            var precise = new SpawnerCountdownProgress();
            precise.Advance(1e12d, 1d);
            result["preciseBeforeDue"] = precise.Advance(1e12d + 0.5d, 1d);
            result["preciseExactDue"] = precise.Advance(1e12d + 1d, 1d);
            int rejected = 0;
            foreach (double bad in new[] { 0d, -1d, double.NaN, double.PositiveInfinity })
                try { new SpawnerCountdownProgress().Advance(0d, bad); }
                catch (ArgumentOutOfRangeException) { rejected++; }
            result["invalidIntervalsRejected"] = rejected;
            var priority = new JArray();
            int cursor = 0;
            for (int i = 0; i < 9; i++) priority.Add(MonsterSpawnerManager.NextLifecycleWork(ref cursor));
            result["busyWorkPriorities"] = priority;

            using var world = new WorldRuntime("spawn_loaded", 1);
            var address = new FlatWorld.WorldModel.WorldAddress("surface", new Int2(0, 0));
            var profile = new ChunkGenerationProfileSnapshot("spawn_loaded", 1, 16, 16);
            var request = world.BeginChunkGeneration(address, 1, profile);
            var chunk = world.Chunks[address];
            result["generatingRejected"] = !MonsterSpawnerManager.IsLoadedNaturalSpawnChunk(chunk, "surface");
            using var terrain = new ChunkTerrainBuffer(16, 16);
            terrain.SetCell(0, 0, new TerrainCell(1, 0, 0, 1, 1, TerrainCellFlags.Walkable));
            using (var committed = new ChunkGenerationResult(request, terrain))
                if (!world.TryCommit(committed, out string reason)) throw new InvalidOperationException(reason);
            result["loadedAccepted"] = MonsterSpawnerManager.IsLoadedNaturalSpawnChunk(chunk, "surface");
            result["otherWorldRejected"] = !MonsterSpawnerManager.IsLoadedNaturalSpawnChunk(chunk, "cave");
            result["missingChunkRejected"] = !MonsterSpawnerManager.IsLoadedNaturalSpawnChunk(null, "surface");
            world.Dispose();
            result["unloadedRejected"] = !MonsterSpawnerManager.IsLoadedNaturalSpawnChunk(chunk, "surface");
            return result;
        }
        finally { foreach (SpawnerConfig config in configs) UnityEngine.Object.DestroyImmediate(config); }
    }

    private static JObject CountdownSample(SpawnerCountdownProgress countdown, SpawnerProgressSaveData state)
    {
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
            double interval = MonsterSpawnerManager.GetCountdownInterval(catalog.Settings, 1f);
            int rolls = 0;
            float Roll() { rolls++; return 0.75f; }
            var state = new SpawnerProgressSaveData();
            var batch = new SpawnerCountdownProgress();
            MonsterSpawnerManager.AdvanceCountdownSchedule(batch, 0d, catalog.Settings, 1f);
            MonsterSpawnerManager.AdvanceCountdownSchedule(batch, interval, catalog.Settings, 1f);
            MonsterSpawnerManager.TryBeginCountdownBatch(batch, config, entry, state, 1f, Roll);
            result["initialBatchCount"] = batch.PendingCount;
            result["initialRequiredRoom"] = MonsterSpawnerManager.GetCountdownBatchRequiredRoom(batch);
            batch.RecordBatchBirth(10.5f, 20.5f, "surface");
            result["continuingRequiredRoom"] = MonsterSpawnerManager.GetCountdownBatchRequiredRoom(batch);
            MonsterSpawnerManager.CompleteCountdownSpawnAttempt(batch, state, true, 0, interval);
            MonsterSpawnerManager.AdvanceCountdownSchedule(batch, interval * 100d, catalog.Settings, 1f);
            result["blockedPending"] = batch.PendingCount;
            result["chanceRollsAfterWait"] = rolls;
            SpawnerCountdownProgress saved = MemoryPackSerializer.Deserialize<SpawnerCountdownProgress>(MemoryPackSerializer.Serialize(batch));
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
                MonsterSpawnerManager.CompleteCountdownSpawnAttempt(batch, state, true, 0, interval);
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
            catalog.Settings.NaturalSpawnIntervalGameSeconds = 0f;
            try { SpawnerConfigCatalogLoader.Validate(catalog); rejected.Add(false); }
            catch (InvalidDataException) { rejected.Add(true); }
            result["invalidConfigurationsRejected"] = rejected;
            config.SpawnCountWeights.Clear();
            int fixedRolls = 0;
            result["fixedCount"] = MonsterSpawnerManager.DetermineCountdownBatchCount(config, 2f, () => { fixedRolls++; return 0f; });
            result["fixedExpectedCount"] = GameRuleMath.ScaleCount(config.SpawnCount, 2f, 1);
            result["fixedCountRolls"] = fixedRolls;
            return result;
        }
        finally { UnityEngine.Object.DestroyImmediate(config); }
    }

    #endregion
}
