using System;
using System.Collections.Generic;
using MemoryPack;

[MemoryPack.MemoryPackable(MemoryPack.GenerateType.VersionTolerant)]
[Serializable]
public partial class GameEventSaveData
{
    [MemoryPack.MemoryPackOrder(1)] public Dictionary<string, GameEventProgressSaveData> EventProgress = new();
    [MemoryPack.MemoryPackOrder(0)] public List<ActiveGameEventSaveData> ActiveEvents = new();
}

[MemoryPack.MemoryPackable(MemoryPack.GenerateType.VersionTolerant)]
[Serializable]
public partial class GameEventProgressSaveData
{
    [MemoryPack.MemoryPackOrder(0)] public int LastEvaluatedDayNumber = -1;
    [MemoryPack.MemoryPackOrder(1)] public int LastTriggeredDayNumber = -1;
    [MemoryPack.MemoryPackOrder(2)] public float LastTriggeredTotalTime = -1f;
    [MemoryPack.MemoryPackOrder(3)] public int TriggerCount;
    [MemoryPack.MemoryPackOrder(4)] public string TriggerRuntimeDataJson = string.Empty;
}

[MemoryPack.MemoryPackable(MemoryPack.GenerateType.VersionTolerant)]
[Serializable]
public partial class ActiveGameEventSaveData
{
    [MemoryPack.MemoryPackOrder(2)] public string EventId = string.Empty;
    [MemoryPack.MemoryPackOrder(3)] public string SourceWorldKey = string.Empty;
    [MemoryPack.MemoryPackOrder(4)] public float StartedTotalTime;
    [MemoryPack.MemoryPackOrder(1)] public float EndTotalTime;
    [MemoryPack.MemoryPackOrder(5)] public int TriggerDayNumber;
    [MemoryPack.MemoryPackOrder(0)] public List<GameEventActionRuntimeSaveData> ActionStates = new();
    [MemoryPack.MemoryPackOrder(6)] public string TriggerPayloadJson = string.Empty;
}

[MemoryPack.MemoryPackable(MemoryPack.GenerateType.VersionTolerant)]
[Serializable]
public partial class GameEventActionRuntimeSaveData
{
    [MemoryPack.MemoryPackOrder(0)] public string ActionId = string.Empty;
    [MemoryPack.MemoryPackOrder(4)] public bool Started;
    [MemoryPack.MemoryPackOrder(1)] public bool Completed;
    [MemoryPack.MemoryPackOrder(3)] public string RuntimeDataJson = string.Empty;
    [MemoryPack.MemoryPackOrder(2)] public string LastError = string.Empty;
}
