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
