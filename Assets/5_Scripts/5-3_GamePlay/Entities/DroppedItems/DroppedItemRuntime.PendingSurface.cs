using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using UnityEngine;

// 活动世界接收冷记录时先结清未完成地表运动，再创建正式运行体。
internal sealed partial class DroppedItemRuntime
{
    #region 冷掉落运动交接

    private readonly List<DroppedItemSaveRecord> pendingSurfaceRestores = new();
    private PendingSurfaceSettlement pendingSurfaceSettlement;

    private sealed class PendingSurfaceSettlement : IGameplayFixedStepSimulationSystem
    {
        private readonly WeakReference<DroppedItemRuntime> owner;
        public PendingSurfaceSettlement(DroppedItemRuntime owner) => this.owner = new WeakReference<DroppedItemRuntime>(owner);
        public void SimulateFixedStep(float deltaTime)
        {
            if (!owner.TryGetTarget(out DroppedItemRuntime runtime) || !runtime.simulation.IsCreated)
            { GameplayFixedStepSimulation.Unregister(this); return; }
            runtime.AdvancePendingSurfaceRestores(deltaTime, this);
        }
    }

    private bool DeferRestoringPendingSurfaceMotion(DroppedItemSaveRecord record)
    {
        if (record == null || record.PendingSurfaceMotionSeconds <= 0f && record.PendingSurfaceDriftSeconds <= 0f) return false;
        unresolved.Add(record);
        pendingSurfaceRestores.Add(record);
        if (pendingSurfaceSettlement == null)
        {
            pendingSurfaceSettlement = new PendingSurfaceSettlement(this);
            GameplayFixedStepSimulation.Register(pendingSurfaceSettlement, GameplayFixedStepSimulationPhase.WorldSimulation);
        }
        return true;
    }

    private void AdvancePendingSurfaceRestores(float deltaTime, PendingSurfaceSettlement settlement)
    {
        if (!GameNetwork.HasStateAuthority || deltaTime <= 0f ||
            !GameplayWorldRegistry.TryGet(matterWorldKey, out _)) return;
        using var scope = GameplayWorldRegistry.UseWorld(matterWorldKey);
        domain = WorldTopologyRuntime.GetActiveDomain();
        for (int i = pendingSurfaceRestores.Count - 1; i >= 0; i--)
        {
            DroppedItemSaveRecord record = pendingSurfaceRestores[i];
            if (!DroppedItemService.AdvanceDeferredColdDrop(matterWorldKey, record, deltaTime, domain)) continue;
            pendingSurfaceRestores.RemoveAt(i);
            unresolved.Remove(record);
            Restore(record);
        }
        if (pendingSurfaceRestores.Count == 0)
        {
            GameplayFixedStepSimulation.Unregister(settlement);
            pendingSurfaceSettlement = null;
        }
    }

    #endregion
}
