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
    public int LastCheckedDay = -1;
    public int LastSpawnDay = -999;
    public List<int> TriggeredWindowIndices = new();

    public int ProcessedGrowthMilestones;
    public int PendingSpawnCount;
    public int LifetimeSpawnCount;
}
