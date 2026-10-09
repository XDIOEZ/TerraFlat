using System;
using System.Runtime.CompilerServices;
using FlatWorld.Networking;
using UnityEngine;

public interface ILiquidVessel
{
    LiquidContainerState Data { get; }
    int Capacity { get; }
    LiquidDefinition CurrentLiquid { get; }
    ItemData ItemData { get; }
    Item Item { get; }
    MachineEntity Machine { get; }
    IVesselContents ContentsSource { get; }
    event Action Changed;
    bool CanOperate(Item actor);
    bool Drink(Item actor);
    float PourToGround(Item actor, float amount);
    bool TransferFromInventoryItem(ItemData source, Item actor, float maximumItemAmount = float.PositiveInfinity);
    void CommitVessel();
}
public interface IVesselContents { Inventory Contents { get; } event Action Changed; }

/// <summary>混合液体操作只按稳定批次提交，饮用和倾倒保留各自世界副作用。</summary>
public static class LiquidVesselOperations
{
    #region 液体守恒
    public static bool SameLiquid(string left, string right) => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
    public static float Quantize(float amount) => float.IsFinite(amount) && amount > Mod_WaterVessel.AmountEpsilon
        ? Mathf.Max(0f, Mathf.Floor(amount + Mod_WaterVessel.AmountEpsilon)) : 0f;
    public static void AddState(ILiquidVessel target, string id, float amount, float temperature = 0f)
    { MixedLiquidContents.Add(target.Data, id.Trim(), amount, temperature); Mod_WaterVessel.Validate(target.Data, target.Capacity); }
    public static void RemoveState(LiquidContainerState state, float amount)
    {
        float remaining = amount;
        while (remaining > 0f)
        {
            if (!MixedLiquidContents.Reserve(state, "remove", out var batch)) break;
            float moved = Mathf.Min(remaining, batch.Servings);
            if (!MixedLiquidContents.ConsumeBatch(state, "remove", batch, moved)) break;
            remaining -= moved;
        }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static float Add(ILiquidVessel target, string id, float amount)
    {
        if (!GameNetwork.HasStateAuthority || target == null || !float.IsFinite(amount) || amount <= 0f || GameRes.ExistingInstance?.GetLiquidDefinition(id) == null) return 0f;
        float moved = Mathf.Min(amount, Mathf.Max(0f, target.Capacity - target.Data.Amount));
        if (moved <= 0f) return 0f;
        AddState(target, id, moved, target.Data.Temperature); target.CommitVessel(); return moved;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static float Remove(ILiquidVessel target, float amount)
    {
        if (!GameNetwork.HasStateAuthority || target == null || !float.IsFinite(amount) || amount <= 0f) return 0f;
        float before = target.Data.Amount; RemoveState(target.Data, amount); float removed = before - target.Data.Amount;
        if (removed > 0f) target.CommitVessel(); return removed;
    }
    public static ILiquidTransferPort Port(ILiquidVessel vessel, ContainerPortDirection direction = ContainerPortDirection.Both, string portId = null)
    {
        var ports = new System.Collections.Generic.List<IContainerPort>();
        if (vessel is IContainerPortProvider provider) provider.CollectContainerPorts(ports);
        else if (vessel.Item != null) ports = ContainerTransferService.Discover(vessel.Item);
        foreach (var port in ports)
        {
            if (port is ILiquidTransferPort liquid && (portId == null || port.Reference.PortId == portId) && (port.Configuration.Access & ContainerAccessKind.Manual) != 0 && (port.Configuration.Direction & direction) != 0) return liquid;
        }
        return null;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool Transfer(ILiquidVessel source, ILiquidVessel target, Item actor, string sourcePortId = null, string targetPortId = null)
    {
        if (source == null || target == null || ReferenceEquals(source, target) || !source.CanOperate(actor) || !target.CanOperate(actor)) return false;
        if (!GameNetwork.HasStateAuthority) return ContainerTransferCommands.RequestLiquidTransfer(source, target, actor, 1f, sourcePortId, targetPortId);
        return ContainerTransferService.TransferLiquid(Port(source, ContainerPortDirection.Output, sourcePortId), Port(target, ContainerPortDirection.Input, targetPortId),
            new ContainerTransferContext(actor, ContainerAccessKind.Manual, "manual-liquid-transfer"), 1f).Success;
    }
    #endregion

    #region 玩家液体操作
    public static float FillFromWorld(ILiquidVessel target, Item actor, WorldLiquidSourceTarget source)
    {
        if (target == null || !target.CanOperate(actor)) return 0f;
        float requested = Mathf.Max(0f, target.Capacity - target.Data.Amount);
        if (requested <= 0) return 0f;
        if (!GameNetwork.HasStateAuthority) return ContainerTransferCommands.RequestVesselAction(target, actor, "world-fill", requested, source.WorldCell) ? requested : 0f;
        var result = ContainerTransferService.TransferLiquid(new WorldLiquidTransferPort(source, actor.gameObject.scene.handle, Mathf.CeilToInt(requested), 3f),
            Port(target, ContainerPortDirection.Input), new ContainerTransferContext(actor, ContainerAccessKind.Manual, "fill-world-liquid"), requested);
        return result.LiquidServings;
    }
    public static bool CanDrink(ILiquidVessel target)
    {
        var port = target == null ? null : Port(target, ContainerPortDirection.Output);
        return port != null && port.PeekLiquid(out var batch) && batch.Servings >= 1f && port.Configuration.AllowsLiquid(batch.LiquidId) &&
            GameRes.ExistingInstance?.GetLiquidDefinition(batch.LiquidId)?.Drinkable == true;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool Drink(ILiquidVessel target, Item actor, string portId = null)
    {
        if (target == null || !target.CanOperate(actor)) return false;
        if (!GameNetwork.HasStateAuthority) return ContainerTransferCommands.RequestVesselAction(target, actor, "drink", 1f, portId: portId);
        var port = Port(target, ContainerPortDirection.Output, portId);
        if (port == null || !port.CanAccess(new ContainerTransferContext(actor, ContainerAccessKind.Manual, "drink"), out _)) return false;
        Mod_Food food = actor.itemMods.GetMod_ByID<Mod_Food>(ModText.Food);
        if (food == null || !port.ReserveLiquid(out var batch)) return false;
        LiquidDefinition liquid = GameRes.ExistingInstance.GetLiquidDefinition(batch.LiquidId);
        if (!port.IsValid || !target.CanOperate(actor) || liquid?.Drinkable != true || batch.Servings < 1f || !port.Configuration.AllowsLiquid(batch.LiquidId) || !port.ExtractLiquid(batch, 1f)) return false;
        port.PublishState();
        food.DrinkWater(liquid.HydrationPerServing, target.Item); LiquidDrinkEffectProcessor.Apply(actor, liquid); return true;
    }
    public static float PourToGround(ILiquidVessel target, Item actor, float amount) => PourToGround(target, actor, amount, 0);
    public static float PourToGround(ILiquidVessel target, Item actor, float amount, int horizontalCellOffset, string portId = null)
    {
        if (target == null || !target.CanOperate(actor) || !float.IsFinite(amount) || amount <= 0f) return 0f;
        if (!GameNetwork.HasStateAuthority) return ContainerTransferCommands.RequestVesselAction(target, actor, "pour", amount, horizontalCellOffset: horizontalCellOffset, portId: portId) ? Mathf.Min(amount, target.Data.Amount) : 0f;
        var port = Port(target, ContainerPortDirection.Output, portId);
        if (port == null || !port.CanAccess(new ContainerTransferContext(actor, ContainerAccessKind.Manual, "pour"), out _)) return 0f;
        Vector2 position = actor.transform.position;
        if (horizontalCellOffset < 0) position += Vector2.left;
        else if (horizontalCellOffset > 0) position += Vector2.right;
        float removed = 0f, remaining = Mathf.Min(amount, target.Data.Amount);
        while (remaining > 0f)
        {
            if (!port.ReserveLiquid(out var batch) || !port.Configuration.AllowsLiquid(batch.LiquidId)) break;
            float servings = Mathf.Min(remaining, batch.Servings);
            if (port.Configuration.WholeBatch && servings < Mathf.Min(1f, remaining)) break;
            LiquidDefinition liquid = GameRes.ExistingInstance.GetLiquidDefinition(batch.LiquidId);
            if (liquid?.WorldWater == null || ChunkMgr.ExistingInstance == null ||
                !ChunkMgr.ExistingInstance.TryGetRuntimeTerrainTile(position, out var sample)) break;
            float beforeDepth = sample.Terrain.GetLiquidDepth(sample.LocalCell.x, sample.LocalCell.y);
            string beforeId = sample.Terrain.GetLiquidId(sample.LocalCell.x, sample.LocalCell.y);
            float requestedDepth = liquid.WorldWater.DepthPerServing * servings;
            if (!WorldLiquidSystem.TryPour(position, liquid.Id, requestedDepth, out float added) ||
                Mathf.Abs(added - requestedDepth) > .000001f) break;
            if (!port.ExtractLiquid(batch, servings))
            { WorldLiquidSystem.TrySet(sample, beforeId, beforeDepth); break; }
            removed += servings; remaining -= servings; port.PublishState();
        }
        return removed;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool TransferFromInventory(ILiquidVessel target, ItemData source, Item actor, float maximum, string targetPortId = null)
    {
        if (target == null || !target.CanOperate(actor) || source == null ||
            ReferenceEquals(source, target.ItemData) || source.Guid != 0 && source.Guid == target.ItemData.Guid ||
            !InventoryContextResolver.TryResolveContainingInventory(actor, source, out Inventory inventory)) return false;
        source = inventory.Data.itemSlots.Find(slot => slot?.itemData != null && (ReferenceEquals(slot.itemData, source) || slot.itemData.Guid == source.Guid && slot.itemData.IDName == source.IDName))?.itemData;
        if (source == null) return false;
        if (!GameNetwork.HasStateAuthority) return ContainerTransferCommands.RequestInventoryFill(target, source, actor, inventory, maximum, targetPortId);
        Item held = actor.itemMods?.GetMod_ByID<Mod_HotBar>(ModText.Hotbar)?.CurentSelectItem;
        if (held != null && (ReferenceEquals(held.itemData, source) || source.Guid != 0 && held.itemData?.Guid == source.Guid))
        { var vessel = held.itemMods.GetMod_ByID<Mod_WaterVessel>(Mod_WaterVessel.ModuleId); if (vessel != null) return Transfer(vessel, target, actor, targetPortId: targetPortId); }
        if (Mod_WaterVessel.TryRead(source, out _, out _))
            return Transfer(new ColdInventoryLiquidVessel(source, inventory, actor), target, actor, targetPortId: targetPortId);
        return false;
    }
    #endregion
}
