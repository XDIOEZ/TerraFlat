using System;
using System.Collections.Generic;
using MemoryPack;

[MemoryPackable]
[Serializable]
public partial class MonsterSpawnerSaveData
{
    public Dictionary<string, SpawnerProgressSaveData> ConfigStates = new();
}

[MemoryPackable]
[Serializable]
public partial class SpawnerProgressSaveData
{
    public int DataVersion = 1;
    public float LastProcessedTotalTime = -1f;
    public int LastCheckedDay = -1;
    public int LastSpawnDay = -999;
    public List<int> TriggeredWindowIndices = new();

    public int AvailableBudget = -1;
    public int LastBudgetRecoveryDay = -1;
    public int PendingReplacementCount;

    public int ProcessedGrowthMilestones;
    public int PendingSpawnCount;
    public int LifetimeSpawnCount;
}
