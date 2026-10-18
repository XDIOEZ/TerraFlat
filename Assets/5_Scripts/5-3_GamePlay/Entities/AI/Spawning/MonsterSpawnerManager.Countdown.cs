using System;
using UnityEngine;

public partial class MonsterSpawnerManager
{
    #region 自然动物持续倒计时

    private static readonly Func<float> CountdownChanceRoll = () => UnityEngine.Random.value;

    public static int NextLifecycleWork(ref int cursor)
    {
        int first = (int)((uint)cursor % 3u);
        cursor = (first + 1) % 3;
        return first;
    }

    private void ProcessCountdowns(SpawnerConfig config, SpawnerProgressSaveData state, TimeData timeData)
    {
        AdvanceCountdownSchedule(config, state, timeData.GetTotalGameTimeSeconds(), timeData.DayLength,
            GameDifficultyService.Current.World.SpawnFrequencyMultiplier, CountdownChanceRoll);
    }

    // 保留原权重对应的稀有程度，各物种独立计时并共用原规则预算和人数上限。
    public static double GetCountdownInterval(SpawnerConfig config, SpawnerConfig.SpawnEntry entry,
        double dayLength, float frequencyMultiplier)
    {
        double totalWeight = 0d;
        foreach (SpawnerConfig.SpawnEntry candidate in config.SpawnEntries)
            totalWeight += candidate.Probability;
        int spawnsPerDay = GameDifficultyService.ScaleCount(config.SpawnsPerDay, frequencyMultiplier, 1);
        return Math.Max(1d, dayLength) * Math.Max(1, config.DaysBetweenSpawns) / spawnsPerDay *
               totalWeight / entry.Probability;
    }

    public static void AdvanceCountdownSchedule(SpawnerConfig config, SpawnerProgressSaveData state,
        double totalGameSeconds, double dayLength, float frequencyMultiplier, Func<float> chanceRoll)
    {
        state.SpeciesCountdowns ??= new();
        foreach (SpawnerConfig.SpawnEntry entry in config.SpawnEntries)
        {
            if (!state.SpeciesCountdowns.TryGetValue(entry.PrefabName, out SpawnerCountdownProgress countdown))
                state.SpeciesCountdowns.Add(entry.PrefabName, countdown = new SpawnerCountdownProgress());
            double interval = GetCountdownInterval(config, entry, dayLength, frequencyMultiplier);
            if (frequencyMultiplier <= 0f)
            {
                countdown.LastObservedGameSeconds = totalGameSeconds;
                continue;
            }
            if (!countdown.Advance(totalGameSeconds, interval)) continue;
            if (config.SpawnChance <= 0f || (config.SpawnChance < 1f && chanceRoll() >= config.SpawnChance))
                countdown.Restart(interval);
            else
                countdown.PendingCount = GameDifficultyService.ScaleCount(config.SpawnCount, frequencyMultiplier, 1);
        }
        RefreshCountdownPendingCount(state);
    }

    // 每次只挑一个已到期物种，地点或预算受阻也轮到其它物种尝试自己的机会。
    public static SpawnerConfig.SpawnEntry SelectPendingCountdownEntry(SpawnerConfig config, SpawnerProgressSaveData state)
    {
        int count = config.SpawnEntries.Count;
        for (int visited = 0; visited < count; visited++)
        {
            int index = (int)((uint)state.CountdownEntryCursor % (uint)count);
            state.CountdownEntryCursor = (index + 1) % count;
            SpawnerConfig.SpawnEntry entry = config.SpawnEntries[index];
            if (state.SpeciesCountdowns.TryGetValue(entry.PrefabName, out SpawnerCountdownProgress countdown) &&
                countdown.PendingCount > 0)
                return entry;
        }
        return null;
    }

    public static void RefreshCountdownPendingCount(SpawnerProgressSaveData state)
    {
        long pending = 0;
        foreach (SpawnerCountdownProgress countdown in state.SpeciesCountdowns.Values)
            pending += Math.Max(0, countdown.PendingCount);
        state.PendingSpawnCount = (int)Math.Min(int.MaxValue, pending);
        state.PendingReplacementCount = 0;
    }

    public static void CompleteCountdownSpawnAttempt(SpawnerProgressSaveData state,
        SpawnerConfig.SpawnEntry entry, bool spawned, int currentDay)
    {
        state.CompleteSpawnAttempt(spawned, currentDay);
        SpawnerCountdownProgress countdown = state.SpeciesCountdowns[entry.PrefabName];
        countdown.CompleteAttempt(countdown.IntervalGameSeconds);
        RefreshCountdownPendingCount(state);
    }

    #endregion
}
