using System;
using System.Collections.Generic;
using MemoryPack;

[MemoryPackable]
[Serializable]
public partial class MonsterSpawnerSaveData
{
    public Dictionary<string, SpawnerProgressSaveData> ConfigStates = new();
    public List<AiecsResidentSaveData> EntitiesResidents = new();
}

/// <summary>正式 ECS 居民的持久玩法值；蜂巢成员仍由蜂巢存档持有。</summary>
[MemoryPackable]
[Serializable]
public partial class AiecsResidentSaveData
{
    public int Guid;
    public string SpeciesId;
    public float X, Y, Hp;
    public List<float> BodyPartHp = new();
    public float Nutrition = -1f;
    public float FlightStamina = -1f;
    public bool FlightRecovering;
    public double NextBirthTime;
    public bool Orphaned;
    public float HiveHomeX, HiveHomeY;
    public int AdvanceTargetItemGuid;
    public float AdvanceTargetX, AdvanceTargetY, AdvanceArrivalDistance;
    public bool AdvanceAttackActorsOnRoute;
}

[MemoryPackable]
[Serializable]
public partial class SpawnerProgressSaveData
{
    #region 生成队列进度

    public float LastProcessedTotalTime = -1f;
    public int LastSpawnDay = -999;

    public int AvailableBudget = -1;
    public int LastBudgetRecoveryDay = -1;
    public int PendingReplacementCount;

    public int ProcessedGrowthMilestones;
    public int PendingSpawnCount;
    public int LifetimeSpawnCount;
    public Dictionary<string, SpawnerCountdownProgress> SpeciesCountdowns = new();
    public int CountdownEntryCursor;

    public void ResetCountdownObservation()
    {
        if (SpeciesCountdowns == null) return;
        foreach (SpawnerCountdownProgress countdown in SpeciesCountdowns.Values)
            if (countdown != null) countdown.LastObservedGameSeconds = -1d;
    }

    /// <summary>独立概率落空也消耗一次排队机会，只有真正出生才累计出生数量。</summary>
    public void CompleteSpawnAttempt(bool spawned, int currentDay)
    {
        if (PendingSpawnCount > 0)
        {
            PendingSpawnCount--;
            if (spawned) LifetimeSpawnCount++;
        }
        else
        {
            PendingReplacementCount = Math.Max(0, PendingReplacementCount - 1);
        }
        LastSpawnDay = Math.Max(LastSpawnDay, currentDay);
    }

    #endregion
}

/// <summary>每个物种只保留当前倒计时或一轮待出生，不绑定玩家位置和区块。</summary>
[MemoryPackable]
[Serializable]
public partial class SpawnerCountdownProgress
{
    #region 持续倒计时

    public double RemainingGameSeconds = -1d;
    public double LastObservedGameSeconds = -1d;
    public double IntervalGameSeconds;
    public int PendingCount;

    // 只推进在线观察到的游戏时间，等待落点时不累计第二轮。
    public bool Advance(double totalGameSeconds, double intervalGameSeconds)
    {
        if (double.IsNaN(totalGameSeconds) || double.IsInfinity(totalGameSeconds) || totalGameSeconds < 0d ||
            double.IsNaN(intervalGameSeconds) || double.IsInfinity(intervalGameSeconds) || intervalGameSeconds <= 0d)
            throw new ArgumentOutOfRangeException(nameof(intervalGameSeconds), "倒计时需要有限的游戏时间和正间隔。");
        IntervalGameSeconds = intervalGameSeconds;
        if (RemainingGameSeconds < 0d) RemainingGameSeconds = intervalGameSeconds;
        double elapsed = LastObservedGameSeconds < 0d ? 0d : Math.Max(0d, totalGameSeconds - LastObservedGameSeconds);
        LastObservedGameSeconds = totalGameSeconds;
        if (PendingCount > 0) return false;
        RemainingGameSeconds = Math.Max(0d, RemainingGameSeconds - elapsed);
        return RemainingGameSeconds <= 0d;
    }

    public void Restart(double intervalGameSeconds)
    {
        if (double.IsNaN(intervalGameSeconds) || double.IsInfinity(intervalGameSeconds) || intervalGameSeconds <= 0d)
            throw new ArgumentOutOfRangeException(nameof(intervalGameSeconds));
        PendingCount = 0;
        RemainingGameSeconds = intervalGameSeconds;
    }

    public void CompleteAttempt(double intervalGameSeconds)
    {
        PendingCount = Math.Max(0, PendingCount - 1);
        if (PendingCount == 0) Restart(intervalGameSeconds);
    }

    #endregion
}
