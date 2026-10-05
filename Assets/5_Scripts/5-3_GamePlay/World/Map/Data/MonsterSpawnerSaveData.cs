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
    public float LastProcessedTotalTime = -1f;
    public int LastSpawnDay = -999;

    public int AvailableBudget = -1;
    public int LastBudgetRecoveryDay = -1;
    public int PendingReplacementCount;

    public int ProcessedGrowthMilestones;
    public int PendingSpawnCount;
    public int LifetimeSpawnCount;
}
