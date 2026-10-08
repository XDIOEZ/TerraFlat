using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using UnityEngine;

[Serializable]
public sealed class ContainerPortAddress
{
    public int ItemGuid;
    public string ItemId;
    public int MachineId;
    public string PortId;
    public bool ColdItem;
    public MachineInventoryAddress Inventory;
    public int Slot = -1;
    public long LiquidRevision = -1;
}

[Serializable]
public sealed class ContainerTransferIntent
{
    public int Version = ContainerTransferService.ContractVersion;
    public string Operation;
    public ContainerPortAddress Source, Target;
    public float Amount;
    public string ContentId, Tag;
    public int ExpectedContentGuid;
    public int WorldCellX, WorldCellY, HorizontalCellOffset;
}

/// <summary>联机只上传地址和意图，服务端重新解析真实端口及原槽位内容。</summary>
public static class ContainerTransferCommands
{
    #region 容器命令
    public static event Func<ContainerTransferIntent, Player, bool> RemoteRequested;
    public static event Action<ContainerTransferResult> Completed;
    public static void CompleteRemote(ContainerTransferResult result) => Completed?.Invoke(result);
    public static bool Request(ContainerTransferIntent intent, Player actor)
    {
        if (intent == null || actor == null) return false;
        if (!GameNetwork.HasStateAuthority) return RemoteRequested?.Invoke(intent, actor) == true;
        ContainerTransferResult result = Execute(intent, actor, new ContainerTransferContext(actor, ContainerAccessKind.Manual, "container-command"));
        Completed?.Invoke(result); return result.Success;
    }
    public static bool RequestLiquidTransfer(ILiquidVessel source, ILiquidVessel target, Item actor, float servings, string sourcePortId = null, string targetPortId = null)
    {
        if (actor is not Player player || !TryAddress(player, source, ContainerPortDirection.Output, out var from, sourcePortId) ||
            !TryAddress(player, target, ContainerPortDirection.Input, out var to, targetPortId)) return false;
        return Request(new ContainerTransferIntent { Operation = "liquid", Source = from, Target = to, Amount = servings }, player);
    }
    public static bool RequestVesselAction(ILiquidVessel target, Item actor, string operation, float amount,
        Vector2Int worldCell = default, int horizontalCellOffset = 0, string portId = null)
    {
        ContainerPortDirection direction = operation == "world-fill" ? ContainerPortDirection.Input : ContainerPortDirection.Output;
        if (actor is not Player player || !TryAddress(player, target, direction, out var address, portId)) return false;
        return Request(new ContainerTransferIntent { Operation = operation, Target = address, Amount = amount,
            WorldCellX = worldCell.x, WorldCellY = worldCell.y, HorizontalCellOffset = Math.Sign(horizontalCellOffset) }, player);
    }
    public static bool RequestInventoryFill(ILiquidVessel target, ItemData source, Item actor, Inventory inventory, float maximum, string portId = null)
    {
        if (actor is not Player player || source == null || float.IsNaN(maximum) || maximum <= 0f ||
            !TryAddress(player, target, ContainerPortDirection.Input, out var to, portId) || !MachineInventoryCommands.TryAddress(player, inventory, out var address)) return false;
        int slot = inventory.Data.itemSlots.FindIndex(candidate => ReferenceEquals(candidate?.itemData, source));
        if (slot < 0) return false;
        long revision = Mod_WaterVessel.TryRead(source, out _, out var liquid) ? liquid.Revision : -1;
        return Request(new ContainerTransferIntent
        {
            Operation = "fill-inventory", Target = to, Amount = Mathf.Min(source.Stack.Amount, maximum),
            Source = new ContainerPortAddress { ColdItem = true, Inventory = address, Slot = slot, ItemGuid = source.Guid, ItemId = source.IDName, LiquidRevision = revision }
        }, player);
    }
    public static bool TryAddress(Player actor, ILiquidVessel vessel, ContainerPortDirection direction, out ContainerPortAddress address, string portId = null)
    {
        address = null;
        ILiquidTransferPort port = vessel == null ? null : LiquidVesselOperations.Port(vessel, direction, portId);
        if (port == null || !port.IsValid || !vessel.CanOperate(actor)) return false;
        address = new ContainerPortAddress { PortId = port.Reference.PortId, LiquidRevision = vessel.Data.Revision,
            ItemGuid = vessel.ItemData.Guid, ItemId = vessel.ItemData.IDName };
        if (vessel is ColdInventoryLiquidVessel cold)
        {
            if (!MachineInventoryCommands.TryAddress(actor, cold.Inventory, out address.Inventory)) { address = null; return false; }
            address.ColdItem = true; address.Slot = cold.Inventory.Data.itemSlots.FindIndex(slot => ReferenceEquals(slot?.itemData, cold.ItemData));
            return address.Slot >= 0;
        }
        address.MachineId = vessel.Machine?.Id ?? 0;
        return address.MachineId != 0 || vessel.Item != null;
    }
    public static ILiquidVessel ResolveVessel(Player actor, ContainerPortAddress address)
    {
        if (actor == null || address == null) return null;
        if (address.ColdItem)
        {
            ItemData data = ResolveColdItem(actor, address, out Inventory inventory);
            return data != null && Mod_WaterVessel.TryRead(data, out _, out _) ? new ColdInventoryLiquidVessel(data, inventory, actor) : null;
        }
        if (address.MachineId != 0)
        {
            MachineEntity entity = MachineWorld.GetById(address.MachineId);
            if (entity?.Snapshot?.Guid != address.ItemGuid || entity.Snapshot.IDName != address.ItemId) return null;
            MachineWorld.WakeForInteraction(entity); return entity.Logic as ILiquidVessel;
        }
        Item item = ItemMgr.Instance?.GetItemByGuid(address.ItemGuid);
        return item != null && item.itemData.IDName == address.ItemId ? item.itemMods?.GetMod_ByID<Mod_WaterVessel>(Mod_WaterVessel.ModuleId) : null;
    }
    public static IContainerPort Resolve(Player actor, ContainerPortAddress address)
    {
        if (address == null || string.IsNullOrWhiteSpace(address.PortId)) return null;
        var ports = new List<IContainerPort>();
        if (address.ColdItem)
        { if (ResolveVessel(actor, address) is IContainerPortProvider provider) provider.CollectContainerPorts(ports); }
        else if (address.MachineId != 0)
        {
            MachineEntity entity = MachineWorld.GetById(address.MachineId);
            if (entity?.Snapshot?.Guid != address.ItemGuid || entity.Snapshot.IDName != address.ItemId) return null;
            MachineWorld.WakeForInteraction(entity);
            if (entity.Logic is IContainerPortProvider provider) provider.CollectContainerPorts(ports);
        }
        else
        {
            Item owner = ItemMgr.Instance?.GetItemByGuid(address.ItemGuid);
            if (owner == null || owner.itemData.IDName != address.ItemId) return null;
            ports = ContainerTransferService.Discover(owner);
        }
        foreach (var port in ports) if (port.IsValid && port.Reference.PortId == address.PortId) return port;
        return null;
    }
    public static ItemData ResolveColdItem(Player actor, ContainerPortAddress address, out Inventory inventory)
    {
        inventory = address == null ? null : MachineInventoryCommands.Resolve(actor, address.Inventory);
        ItemData item = inventory != null && (uint)address.Slot < (uint)inventory.Data.itemSlots.Count ? inventory.Data.itemSlots[address.Slot]?.itemData : null;
        return item?.Guid == address?.ItemGuid && item?.IDName == address?.ItemId ? item : null;
    }
    public static ContainerTransferResult Execute(ContainerTransferIntent intent, Player actor, ContainerTransferContext context)
    {
        if (!GameNetwork.HasStateAuthority) return new(ContainerTransferFailure.NoAuthority);
        if (intent == null || intent.Version != ContainerTransferService.ContractVersion || actor == null || actor.DestructionHandled ||
            actor.itemMods?.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp)?.Hp <= 0f || !float.IsFinite(intent.Amount) || intent.Amount <= 0f || intent.Amount > 100000f)
            return new(ContainerTransferFailure.InvalidRequest);
        IContainerPort target = Resolve(actor, intent.Target);
        ContainerTransferFailure failure = ContainerTransferFailure.None;
        if (target == null || !target.CanAccess(context, out failure)) return new(target == null ? ContainerTransferFailure.StaleReference : failure);
        ILiquidVessel vessel = ResolveVessel(actor, intent.Target);
        if (vessel != null && intent.Target.LiquidRevision >= 0 && vessel.Data.Revision != intent.Target.LiquidRevision) return new(ContainerTransferFailure.Changed);
        if (intent.Operation is "drink" or "pour" or "world-fill" or "fill-inventory")
        {
            if (vessel == null || !vessel.CanOperate(actor)) return new(ContainerTransferFailure.AccessDenied);
            bool input = intent.Operation is "world-fill" or "fill-inventory";
            if ((target.Configuration.Direction & (input ? ContainerPortDirection.Input : ContainerPortDirection.Output)) == 0) return new(ContainerTransferFailure.AccessDenied);
            if (intent.Operation == "drink") return LiquidVesselOperations.Drink(vessel, actor, target.Reference.PortId) ? new(ContainerTransferFailure.None, items: 0, servings: 1f) : new(ContainerTransferFailure.Empty, unit: "servings");
            if (intent.Operation == "pour")
            { float amount = LiquidVesselOperations.PourToGround(vessel, actor, intent.Amount, Math.Sign(intent.HorizontalCellOffset), target.Reference.PortId); return new(amount > 0f ? ContainerTransferFailure.None : ContainerTransferFailure.FilterRejected, servings: amount, unit: "servings"); }
            if (intent.Operation == "world-fill")
            {
                if (!WorldLiquidSourceResolver.TryResolve(new Vector2(intent.WorldCellX + .5f, intent.WorldCellY + .5f), out var source)) return new(ContainerTransferFailure.Empty);
                var result = ContainerTransferService.TransferLiquid(new WorldLiquidTransferPort(source, context.SceneHandle, Mathf.CeilToInt(intent.Amount), 3f), target as ILiquidTransferPort, context, intent.Amount);
                return result;
            }
            ItemData item = ResolveColdItem(actor, intent.Source, out _);
            if (item == null) return new(ContainerTransferFailure.Changed);
            if (intent.Source.LiquidRevision >= 0 && (!Mod_WaterVessel.TryRead(item, out _, out var state) || state.Revision != intent.Source.LiquidRevision)) return new(ContainerTransferFailure.Changed);
            float before = vessel.Data.Amount;
            return LiquidVesselOperations.TransferFromInventory(vessel, item, actor, intent.Amount, target.Reference.PortId)
                ? new(ContainerTransferFailure.None, servings: vessel.Data.Amount - before) : new(ContainerTransferFailure.FilterRejected, unit: "servings");
        }
        IContainerPort from = Resolve(actor, intent.Source);
        if (from == null) return new(ContainerTransferFailure.StaleReference);
        if (intent.Operation == "liquid" && from is ILiquidTransferPort liquidSource && target is ILiquidTransferPort liquidTarget)
        {
            ILiquidVessel sourceVessel = ResolveVessel(actor, intent.Source);
            if (sourceVessel != null && intent.Source.LiquidRevision >= 0 && sourceVessel.Data.Revision != intent.Source.LiquidRevision) return new(ContainerTransferFailure.Changed);
            return ContainerTransferService.TransferLiquid(liquidSource, liquidTarget, context, intent.Amount);
        }
        if (intent.Operation == "items" && from is IItemTransferPort itemSource && target is IItemTransferPort itemTarget)
        {
            ItemData item = itemSource.PeekItem(intent.ContentId, intent.Tag);
            if (item == null || intent.ExpectedContentGuid != 0 && item.Guid != intent.ExpectedContentGuid) return new(ContainerTransferFailure.Changed);
            return ContainerTransferService.TransferItems(itemSource, itemTarget, context, Mathf.FloorToInt(intent.Amount), intent.ContentId, intent.Tag);
        }
        return new(ContainerTransferFailure.InvalidRequest);
    }
    #endregion
}
