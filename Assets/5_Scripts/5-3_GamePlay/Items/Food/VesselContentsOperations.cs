using FlatWorld.Networking;
using UnityEngine;

/// <summary>容器开口只提交真实槽位的出料意图，生成和扣量由共享掉落事务完成。</summary>
public static class VesselContentsOperations
{
    #region 固体出料

    public static bool TrySpill(ILiquidVessel vessel, Item actor, int slotIndex, int expectedGuid, int direction)
    {
        Inventory inventory = vessel?.ContentsSource?.Contents;
        if (vessel == null || !vessel.CanOperate(actor) || inventory?.Data?.itemSlots == null ||
            !inventory.CanAccessRuntime || (uint)slotIndex >= (uint)inventory.Data.itemSlots.Count ||
            inventory.IsSlotBeingDragged(slotIndex)) return false;
        ItemSlot slot = inventory.Data.itemSlots[slotIndex];
        if (slot?.itemData?.Guid != expectedGuid || slot.Amount <= 0) return false;
        if (!GameNetwork.HasStateAuthority)
            return ContainerTransferCommands.RequestContentsSpill(vessel, actor, slotIndex, expectedGuid, direction);
        if (actor is Player { IsLocalProfile: true } &&
            ItemNetworkStateSerialization.HasPendingLocalInventoryTransaction()) return false;

        Vector2 origin = vessel.Machine != null ? vessel.Machine.Position
            : vessel.Item?.InHand == true ? (Vector2)actor.transform.position : (Vector2)vessel.Item.transform.position;
        int side = direction < 0 ? -1 : 1;
        Vector2 destination = origin + new Vector2(side * 1.1f, -.25f);
        Mod_DiscardItem discard = actor.GetComponentInChildren<Mod_DiscardItem>(true);
        float duration = discard != null ? discard.baseDropDuration +
            WorldTopologyRuntime.Distance(origin, destination) * discard.distanceSensitivity : .5f;
        float bezier = discard?.defaultMoveMode == Mod_BaseDroper.MoveMode.BezierCurve ? discard.bezierOffset : 0f;
        bool committed = InventoryWorldDrop.TryDrop(slot, slot.Amount, actor, origin, destination, duration,
            bezier, discard != null ? discard.arcHeight : 1f, inventory.Data);
        if (committed) vessel.CommitVessel();
        return committed;
    }

    // 只有重力朝向开口且真实物品中心抵达嘴沿，才允许离开容器。
    public static bool ReachesOpenMouth(float tiltDegrees, float centerY, float mouthY)
        => float.IsFinite(tiltDegrees) && float.IsFinite(centerY) && float.IsFinite(mouthY) &&
           Mathf.Cos(tiltDegrees * Mathf.Deg2Rad) < -.001f && centerY >= mouthY;

    #endregion
}
