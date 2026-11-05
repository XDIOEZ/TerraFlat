using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class ActorHaulingOrder
{
    public string WorldKey;
    public ContainerPortAddress Destination;
    public float Radius = 10f;
}

/// <summary>搬运步骤只执行移动和正式库存事务，暂停释放预约但不清空随身货物。</summary>
public sealed class ActorHaulingJobDriver : IDisposable
{
    #region 工作实例与预约
    private sealed class Reservation
    {
        public Item Actor;
        public uint Generation;
    }
    private static readonly Dictionary<(string World, DroppedItemHandle Drop), Reservation> reservations = new();
    private readonly Item actor;
    private readonly Mod_Mover_AI mover;
    private readonly IItemTransferPort cargo;
    private readonly List<DroppedItemHandle> candidates = new();
    private readonly Dictionary<int, float> unreachableUntil = new();
    private DroppedItemHandle selectedDrop;
    private (string World, DroppedItemHandle Drop) reservationKey;
    private bool hasReservation;
    private float elapsed, nextSearch, nextDeliveryAttempt, travelElapsed;
    private MachineEntity destination;
    private IItemTransferPort destinationPort;
    private ContainerPortAddress resolvedAddress;
    private Vector2 approachPoint;
    private bool approaching;
    public string Status { get; private set; } = "等待工作";

    public ActorHaulingJobDriver(Item actor, Mod_Mover_AI mover, IItemTransferPort cargo)
    { this.actor = actor; this.mover = mover; this.cargo = cargo; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetReservations() => reservations.Clear();

    private bool TryReserve(DroppedItemHandle drop, string world)
    {
        var key = (world, drop);
        if (reservations.TryGetValue(key, out Reservation existing) && existing.Actor != null &&
            !existing.Actor.DestructionHandled && existing.Actor.RuntimeGeneration == existing.Generation && existing.Actor != actor)
            return false;
        ReleaseReservation();
        reservations[key] = new Reservation { Actor = actor, Generation = actor.RuntimeGeneration };
        reservationKey = key; selectedDrop = drop; hasReservation = true;
        return true;
    }

    private bool ReservedByOther(DroppedItemHandle drop, string world)
        => reservations.TryGetValue((world, drop), out Reservation reservation) && reservation.Actor != null &&
           !reservation.Actor.DestructionHandled && reservation.Actor.RuntimeGeneration == reservation.Generation && reservation.Actor != actor;

    private void ReleaseReservation()
    {
        if (hasReservation && reservations.TryGetValue(reservationKey, out Reservation owner) && owner.Actor == actor)
            reservations.Remove(reservationKey);
        hasReservation = false; selectedDrop = default;
    }
    #endregion

    #region 搬运步骤
    public void Tick(ActorHaulingOrder order, float seconds)
    {
        elapsed += Mathf.Max(0f, seconds);
        if (!ResolveCurrentDestination(order))
        { Suspend(); Status = "请指定当前世界的目标容器"; return; }
        ItemData carried = cargo.PeekItem();
        if (carried != null)
        {
            ReleaseReservation();
            // 失败后保留货物和状态，等待重试窗口再重新申请路径。
            if (elapsed < nextDeliveryAttempt) return;
            if (destinationPort.GetReceivableItems(carried, 1) <= 0)
            { StopApproach(); Status = "目标容器已满或拒收，保留货物"; return; }
            Status = "运送到目标容器";
            float reach = Mathf.Min(1.5f, destinationPort.Configuration.Reach);
            if (!Approach(destination.Position, reach, seconds))
            {
                if (travelElapsed >= 20f || mover.DestinationResult is WorldNavigationDestinationResult.Failed or WorldNavigationDestinationResult.RejectedByPathCost)
                { StopApproach(); nextDeliveryAttempt = elapsed + 3f; Status = "目标容器不可达，保留货物"; }
                return;
            }
            ContainerTransferResult delivery = ContainerTransferService.TransferItems(cargo, destinationPort,
                new ContainerTransferContext(actor, ContainerAccessKind.Machine, "actor-haul-deliver"),
                Inventory_Data.GetWholeStackAmount(carried.Stack.Amount));
            StopApproach();
            nextDeliveryAttempt = delivery.Success ? 0f : elapsed + 1f;
            Status = delivery.Success ? "交付完成" : "交付受阻，保留货物";
            return;
        }

        if (!hasReservation)
        {
            Status = "寻找待搬运物品";
            if (elapsed < nextSearch) return;
            nextSearch = elapsed + .5f;
            QueryAndReserve(order);
            if (!hasReservation) { StopApproach(); Status = "范围内没有可搬运物品"; return; }
        }
        if (!DroppedItemService.TryGetPickablePosition(selectedDrop, out Vector2 point))
        { ReleaseReservation(); StopApproach(); return; }
        if (WorldTopologyRuntime.SqrDistance(point, destination.Position) > order.Radius * order.Radius)
        { ReleaseReservation(); StopApproach(); return; }
        Status = "前往拾取物品";
        if (!Approach(point, 1.25f, seconds))
        {
            if (travelElapsed >= 20f || mover.DestinationResult is WorldNavigationDestinationResult.Failed or WorldNavigationDestinationResult.RejectedByPathCost)
            { unreachableUntil[selectedDrop.Id] = elapsed + 10f; ReleaseReservation(); StopApproach(); }
            return;
        }
        if (DroppedItemService.TryGetItemTransferOutput(selectedDrop, out IItemTransferPort source))
        {
            ItemData item = source.PeekItem();
            int amount = item == null ? 0 : Math.Min(cargo.GetReceivableItems(item, 20), destinationPort.GetReceivableItems(item, 20));
            if (amount > 0)
                ContainerTransferService.TransferItems(source, cargo,
                    new ContainerTransferContext(actor, ContainerAccessKind.Machine, "actor-haul-pickup"), amount);
        }
        ReleaseReservation(); StopApproach();
    }

    private void QueryAndReserve(ActorHaulingOrder order)
    {
        DroppedItemService.QueryNearbyPickableDrops(destination.Position, order.Radius, candidates);
        float best = float.PositiveInfinity;
        DroppedItemHandle selected = default;
        foreach (DroppedItemHandle candidate in candidates)
        {
            if (ReservedByOther(candidate, order.WorldKey) ||
                unreachableUntil.TryGetValue(candidate.Id, out float retry) && elapsed < retry ||
                !DroppedItemService.TryGetPickablePosition(candidate, out Vector2 position) ||
                !DroppedItemService.TryGetItemTransferOutput(candidate, out IItemTransferPort port)) continue;
            float distance = WorldTopologyRuntime.SqrDistance(actor.transform.position, position);
            if (distance >= best) continue;
            ItemData data = port.PeekItem();
            if (data == null || cargo.GetReceivableItems(data, 1) <= 0 || destinationPort.GetReceivableItems(data, 1) <= 0) continue;
            best = distance; selected = candidate;
        }
        candidates.Clear();
        if (selected.IsValid) TryReserve(selected, order.WorldKey);
        if (unreachableUntil.Count > 128) unreachableUntil.Clear();
    }

    private bool Approach(Vector2 target, float reach, float seconds)
    {
        if (WorldTopologyRuntime.SqrDistance(actor.transform.position, target) <= reach * reach)
        { mover.StopMovement(); return true; }
        if (!approaching || mover.DestinationResult == WorldNavigationDestinationResult.Reached ||
            WorldTopologyRuntime.SqrDistance(approachPoint, target) > .25f)
        {
            approachPoint = target; approaching = true; travelElapsed = 0f;
            if (!ActorInteractionApproach.TrySubmitDestination(actor, mover, target, reach))
            { mover.StopMovement(); travelElapsed = 20f; return false; }
        }
        travelElapsed += seconds;
        return false;
    }

    public static bool TryResolveDestination(ActorHaulingOrder order, out MachineEntity node, out IItemTransferPort port)
    {
        node = null; port = null;
        ContainerPortAddress address = order?.Destination;
        if (address == null || address.ColdItem || address.MachineId == 0 || order.WorldKey != MachineWorld.WorldKey) return false;
        node = MachineWorld.GetById(address.MachineId);
        if (node?.Snapshot?.Guid != address.ItemGuid || node.Snapshot.IDName != address.ItemId || node.Definition.Kind != "storage") return false;
        port = ContainerTransferCommands.Resolve(null, address) as IItemTransferPort;
        return node.Logic is StorageLogic && port?.IsValid == true && !port.Configuration.OwnerOnly &&
               (port.Configuration.Direction & ContainerPortDirection.Input) != 0 &&
               (port.Configuration.Access & ContainerAccessKind.Machine) != 0;
    }

    // 每次校验真实身份，只有端口失效或订单改变才重新解析并唤醒容器。
    private bool ResolveCurrentDestination(ActorHaulingOrder order)
    {
        ContainerPortAddress address = order?.Destination;
        if (address != null && ReferenceEquals(address, resolvedAddress) && order.WorldKey == MachineWorld.WorldKey &&
            ReferenceEquals(MachineWorld.GetById(address.MachineId), destination) &&
            destination?.Snapshot?.Guid == address.ItemGuid && destination.Snapshot.IDName == address.ItemId &&
            destinationPort?.IsValid == true) return true;
        bool found = TryResolveDestination(order, out destination, out destinationPort);
        resolvedAddress = found ? address : null;
        return found;
    }

    public void Suspend() { ReleaseReservation(); StopApproach(); nextSearch = nextDeliveryAttempt = 0f; }
    private void StopApproach() { approaching = false; travelElapsed = 0f; mover?.StopMovement(); }
    public void Dispose() { Suspend(); candidates.Clear(); unreachableUntil.Clear(); destination = null; destinationPort = null; resolvedAddress = null; }
    #endregion
}
