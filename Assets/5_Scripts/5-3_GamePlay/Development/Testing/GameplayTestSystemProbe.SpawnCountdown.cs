using System;
using System.Collections.Generic;
using MemoryPack;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 正式自然动物倒计时采样

    private static void RegisterSpawnCountdownOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("ai/spawn_countdown", EntitySpawnCountdown);
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

    #endregion
}
