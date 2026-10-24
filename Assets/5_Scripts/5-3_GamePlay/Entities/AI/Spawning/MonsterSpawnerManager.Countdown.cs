using System;
using System.Collections.Generic;
using UnityEngine;

public partial class MonsterSpawnerManager
{
    #region 世界统一自然刷新倒计时

    private static readonly Func<float> CountdownChanceRoll = () => UnityEngine.Random.value;
    private SpawnerCountdownProgress _naturalCountdown = new();

    public static int NextLifecycleWork(ref int cursor)
    {
        int first = (int)((uint)cursor % 3u);
        cursor = (first + 1) % 3;
        return first;
    }

    // 频率只调整世界时钟，物种数量、权重和所在区块都不改变倒计时。
    public static double GetCountdownInterval(SpawnerRuntimeSettings settings, float frequencyMultiplier)
    {
        double interval = settings?.NaturalSpawnIntervalGameSeconds ?? 0d;
        if (double.IsNaN(interval) || double.IsInfinity(interval) || interval <= 0d ||
            float.IsNaN(frequencyMultiplier) || float.IsInfinity(frequencyMultiplier) || frequencyMultiplier < 0f)
            throw new ArgumentOutOfRangeException(nameof(frequencyMultiplier), "自然刷新间隔和频率必须是有效数值。");
        return frequencyMultiplier > 0f ? interval / frequencyMultiplier : interval;
    }

    public static bool AdvanceCountdownSchedule(SpawnerCountdownProgress countdown, double totalGameSeconds,
        SpawnerRuntimeSettings settings, float frequencyMultiplier)
    {
        double interval = GetCountdownInterval(settings, frequencyMultiplier);
        if (frequencyMultiplier <= 0f)
        {
            countdown.LastObservedGameSeconds = -1d;
            return false;
        }
        return countdown.Advance(totalGameSeconds, interval);
    }

    public static int DetermineCountdownBatchCount(SpawnerConfig config, float frequencyMultiplier, Func<float> chanceRoll)
    {
        return config.SpawnCountWeights != null && config.SpawnCountWeights.Count > 0
            ? config.DetermineSpawnCount(chanceRoll())
            : GameRuleMath.ScaleCount(config.SpawnCount, frequencyMultiplier, 1);
    }

    // 候选已经通过真实落点筛选，再消费一次规则概率，不能失败后重抽到成功。
    internal static bool TryBeginCountdownBatch(SpawnerCountdownProgress countdown, SpawnerConfig config,
        SpawnerConfig.SpawnEntry entry, SpawnerProgressSaveData state, float frequencyMultiplier, Func<float> chanceRoll)
    {
        if (countdown.RemainingGameSeconds != 0d || countdown.PendingCount > 0 || frequencyMultiplier <= 0f)
            return false;
        if (config.SpawnChance <= 0f || (config.SpawnChance < 1f && chanceRoll() >= config.SpawnChance))
        {
            countdown.Restart(countdown.IntervalGameSeconds);
            return false;
        }
        countdown.BeginBatch(DetermineCountdownBatchCount(config, frequencyMultiplier, chanceRoll));
        countdown.SelectedConfigId = config.PersistentId;
        countdown.SelectedSpeciesId = entry.PrefabName;
        state.PendingSpawnCount = countdown.PendingCount;
        state.PendingReplacementCount = 0;
        return true;
    }

    internal static bool IsCountdownBatchFor(SpawnerCountdownProgress countdown, string configId, string speciesId) =>
        countdown != null && countdown.PendingCount > 0 &&
        string.Equals(countdown.SelectedConfigId, configId, StringComparison.Ordinal) &&
        string.Equals(countdown.SelectedSpeciesId, speciesId, StringComparison.Ordinal);

    public static void CompleteCountdownSpawnAttempt(SpawnerCountdownProgress countdown,
        SpawnerProgressSaveData state, bool spawned, int currentDay, double totalGameSeconds)
    {
        if (countdown.PendingCount <= 0) return;
        state.CompleteSpawnAttempt(spawned, currentDay);
        countdown.LastObservedGameSeconds = totalGameSeconds;
        countdown.CompleteAttempt(countdown.IntervalGameSeconds);
        state.PendingSpawnCount = countdown.PendingCount;
    }

    internal readonly struct NaturalSpawnCandidate
    {
        public readonly SpawnerConfig Config;
        public readonly SpawnerConfig.SpawnEntry Entry;
        public readonly Vector3 Position;

        public NaturalSpawnCandidate(SpawnerConfig config, SpawnerConfig.SpawnEntry entry, Vector3 position)
        {
            Config = config;
            Entry = entry;
            Position = position;
        }
    }

    // 所有规则的合格物种放在同一奖池，权重只决定本轮抽中谁。
    internal static int SelectCountdownCandidate(IReadOnlyList<NaturalSpawnCandidate> candidates, float sample)
    {
        if (float.IsNaN(sample) || float.IsInfinity(sample) || sample < 0f || sample > 1f)
            throw new ArgumentOutOfRangeException(nameof(sample));
        double total = 0d;
        for (int i = 0; i < candidates.Count; i++) total += candidates[i].Entry.Probability;
        if (total <= 0d) return -1;
        double target = sample * total;
        for (int i = 0; i < candidates.Count; i++)
        {
            target -= candidates[i].Entry.Probability;
            if (target < 0d) return i;
        }
        return candidates.Count - 1;
    }

    #endregion
}
