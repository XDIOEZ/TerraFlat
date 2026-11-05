using System.Collections.Generic;
using FlatWorld.Networking;
using UnityEngine;

public sealed partial class Mod_ServitorController
{
    #region 寻线与用电租约
    private readonly List<MachineEntity> chargingCandidates = new();
    private readonly Dictionary<int, float> chargingRetryAfter = new();
    private MachineEntity chargingWire;
    private ElectricalExternalConsumer chargeLease;
    private float chargingClock, nextChargingSearch, chargingTravelStarted;
    private bool approachingChargingWire;

    public void TickRecharge(float seconds)
    {
        if (!Ready || !GameNetwork.HasStateAuthority) return;
        if (!NeedsRecharge) { StopCharging(); return; }
        if (chargingWire != null && (!MachineWorld.IsWireEnergized(chargingWire) || chargingWire.ScopeKey != boundWorld))
            ForgetChargingTarget();
        if (chargingWire == null)
        {
            if (chargingClock < nextChargingSearch) { Status = "等待可用电线"; return; }
            nextChargingSearch = chargingClock + 2f;
            FindChargingWire();
            if (chargingWire == null)
            {
                Status = state.Battery.StoredJoules <= 0f ? "电量耗尽，请在机仆旁接通电线" : "附近没有可用的通电电线";
                mover.StopMovement(); return;
            }
        }
        if (WorldTopologyRuntime.SqrDistance(item.transform.position, chargingWire.Position) > ChargingReach * ChargingReach)
        {
            DisconnectCharger();
            if (state.Battery.StoredJoules <= 0f)
            {
                // 耗尽后释放远处目标，让新接到身边的电线仍能重新唤醒机仆。
                ForgetChargingTarget(); nextChargingSearch = 0f;
                Status = "电量耗尽，请在机仆旁接通电线"; return;
            }
            Status = "前往通电电线";
            if (!approachingChargingWire || mover.DestinationResult == WorldNavigationDestinationResult.Reached)
            {
                approachingChargingWire = true; chargingTravelStarted = chargingClock;
                if (!ActorInteractionApproach.TrySubmitDestination(item, mover, chargingWire.Position, ChargingReach))
                { mover.StopMovement(); chargingTravelStarted = chargingClock - 20f; }
            }
            if (mover.DestinationResult is WorldNavigationDestinationResult.Failed or WorldNavigationDestinationResult.RejectedByPathCost ||
                chargingClock - chargingTravelStarted >= 20f)
            {
                chargingRetryAfter[chargingWire.Id] = chargingClock + 15f;
                ForgetChargingTarget(); nextChargingSearch = 0f;
            }
            return;
        }
        approachingChargingWire = false;
        mover.StopMovement();
        if (!MachineWorld.IsExternalElectricalConsumerConnected(chargeLease))
        {
            DisconnectCharger();
            chargeLease = MachineWorld.ConnectExternalElectricalConsumer(chargingWire, RequestChargingPower, ReceiveChargingEnergy);
        }
        Status = chargeLease?.LastSuppliedWatts > 0f ? "充电中" : "已接线，等待供电";
    }

    private void FindChargingWire()
    {
        MachineWorld.CollectNodes(chargingCandidates);
        float nearest = ChargingSearchRadius * ChargingSearchRadius;
        foreach (MachineEntity candidate in chargingCandidates)
        {
            if (!MachineWorld.IsWireEnergized(candidate) || candidate.ScopeKey != boundWorld ||
                chargingRetryAfter.TryGetValue(candidate.Id, out float retry) && chargingClock < retry) continue;
            float distance = WorldTopologyRuntime.SqrDistance(item.transform.position, candidate.Position);
            if (distance > nearest || state.Battery.StoredJoules <= 0f && distance > ChargingReach * ChargingReach) continue;
            nearest = distance; chargingWire = candidate;
        }
        chargingCandidates.Clear();
        chargingTravelStarted = chargingClock;
        if (chargingRetryAfter.Count > 128) chargingRetryAfter.Clear();
    }

    private bool IsChargingContactValid() => Ready && item.RuntimeGeneration == generation && NeedsRecharge &&
        boundWorld == GameplayWorldRegistry.GetItemWorldKey(item) && MachineWorld.WorldKey == boundWorld &&
        chargingWire != null && ReferenceEquals(MachineWorld.GetById(chargingWire.Id), chargingWire) &&
        WorldTopologyRuntime.SqrDistance(item.transform.position, chargingWire.Position) <= ChargingReach * ChargingReach;

    private float RequestChargingPower(float seconds)
        => IsChargingContactValid() ? state.Battery.RequestChargingWatts(seconds, BatteryCapacityJoules) : 0f;

    private void ReceiveChargingEnergy(float joules)
    { if (IsChargingContactValid() && GameNetwork.HasStateAuthority) state.Battery.ReceiveEnergy(joules, BatteryCapacityJoules); }

    private void DisconnectCharger() { chargeLease?.Dispose(); chargeLease = null; }
    private void ForgetChargingTarget()
    { DisconnectCharger(); chargingWire = null; approachingChargingWire = false; mover?.StopMovement(); }
    public void StopCharging()
    { ForgetChargingTarget(); nextChargingSearch = 0f; chargingRetryAfter.Clear(); chargingCandidates.Clear(); }
    #endregion
}
