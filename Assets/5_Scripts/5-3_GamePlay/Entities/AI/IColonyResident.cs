using UnityEngine;

#region 群落协作接口

/// <summary>群落与成员通过能力接口交换家园、警报和持久状态。</summary>
public interface IColonyHome
{
    Item ColonyItem { get; }
    Vector2 HomePosition { get; }
    int Honey { get; }
    int HoneyCapacity { get; }
    bool ContainsTerritoryPosition(Vector2 position);
    Vector2 GetRandomTerritoryPatrolPosition();
    void ReceiveHoney();
    bool TryConsumeHoney();
}

public interface IColonyResident
{
    bool IsAlive { get; }
    IColonyHome Colony { get; }
    bool CanRespondToColonyHelp { get; }
    bool HasActiveAngryPursuit { get; }
    float SatietyDrainRate { get; }
    float HomeArrivalRadius { get; }
    void BindColony(IColonyHome owner, ColonyResidentState saved);
    ColonyResidentState CaptureState();
    void SetNightSleepRequested(bool requested);
    void SetColonyTerritoryAlert(float staySeconds, float thresholdSeconds);
    void ForceHiveDefenseTarget(Item target);
    void DetachFromDestroyedColony(Item revengeTarget, Vector2 destroyedHome);
}

#endregion
