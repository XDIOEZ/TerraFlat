using Sirenix.OdinInspector;

public partial class GameSaveData
{
    [ShowInInspector]
    [MemoryPack.MemoryPackOrder(19)] public GameEventSaveData GameEventData = new();
}
