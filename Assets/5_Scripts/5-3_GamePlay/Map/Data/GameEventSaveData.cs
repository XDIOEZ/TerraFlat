using System;
using System.Collections.Generic;
using MemoryPack;

[MemoryPackable]
[Serializable]
public partial class GameEventSaveData
{
    public int DataVersion = 2;
    public Dictionary<string, GameEventProgressSaveData> EventProgress = new();
    public List<ActiveGameEventSaveData> ActiveEvents = new();
}

[MemoryPackable]
[Serializable]
public partial class GameEventProgressSaveData
{
    public int LastEvaluatedDayNumber = -1;
    public int LastTriggeredDayNumber = -1;
    public float LastTriggeredTotalTime = -1f;
    public int TriggerCount;
    public string TriggerRuntimeDataJson = string.Empty;
}

[MemoryPackable]
[Serializable]
public partial class ActiveGameEventSaveData
{
    public string EventId = string.Empty;
    public string SourceWorldKey = string.Empty;
    public float StartedTotalTime;
    public float EndTotalTime;
    public int TriggerDayNumber;
    public List<GameEventActionRuntimeSaveData> ActionStates = new();
    public string TriggerPayloadJson = string.Empty;
}

[MemoryPackable]
[Serializable]
public partial class GameEventActionRuntimeSaveData
{
    public string ActionId = string.Empty;
    public bool Started;
    public bool Completed;
    public string RuntimeDataJson = string.Empty;
    public string LastError = string.Empty;
}
