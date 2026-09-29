using System;
using System.Runtime.CompilerServices;
using FlatWorld.Networking;
using UnityEngine;

/// <summary>整个容器的领域契约；手持模块与落地机器共用液体算法和正式面板。</summary>
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

public interface IVesselContents
{
    Inventory Contents { get; }
    event Action Changed;
}

/// <summary>液体份数、库存投料与转移的共享托管实现，任何容器载体均不得绕过来源库存。</summary>
public static class LiquidVesselOperations
{
    #region 液体守恒
    public static bool SameLiquid(string left, string right)
        => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    public static float Quantize(float amount)
        => MachineDefinition.Positive(amount)
            ? Mathf.Floor((amount + Mod_WaterVessel.AmountEpsilon) / Mod_WaterVessel.AmountStep) * Mod_WaterVessel.AmountStep : 0f;

    public static void AddState(ILiquidVessel target, string id, float amount, float temperature = 0f)
    {
        if (Mod_WaterVessel.IsEmptyAmount(target.Data.Amount)) target.Data.LiquidId = id.Trim();
        target.Data.Amount += amount;
        target.Data.Temperature = Mathf.Max(target.Data.Temperature, temperature);
        target.Data.ProcessingSeconds = 0f;
        Mod_WaterVessel.NormalizeStoredAmount(target.Data);
        Mod_WaterVessel.Validate(target.Data, target.Capacity);
    }

    public static void RemoveState(LiquidContainerState state, float amount)
    {
        state.Amount = Mathf.Max(0f, state.Amount - amount);
        state.ProcessingSeconds = 0f;
        Mod_WaterVessel.NormalizeStoredAmount(state);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static float Add(ILiquidVessel target, string id, float amount)
    {
        if (!GameNetwork.HasStateAuthority || !MachineDefinition.Positive(amount) || GameRes.Instance.GetLiquidDefinition(id) == null ||
            !Mod_WaterVessel.IsEmptyAmount(target.Data.Amount) && !SameLiquid(target.Data.LiquidId, id)) return 0f;
        float moved = Quantize(Mathf.Min(amount, Mathf.Max(0f, target.Capacity - target.Data.Amount)));
        if (moved <= 0f) return 0f;
        AddState(target, id, moved);
        target.CommitVessel();
        return moved;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static float Remove(ILiquidVessel target, float amount)
    {
        if (!GameNetwork.HasStateAuthority || !MachineDefinition.Positive(amount)) return 0f;
        float removed = Quantize(Mathf.Min(amount, target.Data.Amount));
        if (removed <= 0f) return 0f;
        RemoveState(target.Data, removed);
        target.CommitVessel();
        return removed;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool Transfer(ILiquidVessel source, ILiquidVessel target, Item actor)
    {
        if (!GameNetwork.HasStateAuthority) return false;
        if (source == null || target == null || ReferenceEquals(source, target) ||
            !source.CanOperate(actor) || !target.CanOperate(actor) ||
            !Mod_WaterVessel.IsEmptyAmount(target.Data.Amount) && !SameLiquid(source.Data.LiquidId, target.Data.LiquidId)) return false;
        float moved = Quantize(Mathf.Min(source.Data.Amount, Mathf.Max(0f, target.Capacity - target.Data.Amount)));
        if (moved <= 0f) return false;
        AddState(target, source.Data.LiquidId, moved, source.Data.Temperature);
        RemoveState(source.Data, moved);
        source.CommitVessel();
        target.CommitVessel();
        return true;
    }
    #endregion

    #region 玩家液体操作
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool Drink(ILiquidVessel target, Item actor)
    {
        if (!GameNetwork.HasStateAuthority) return false;
        LiquidDefinition liquid = target.CurrentLiquid;
        if (!target.CanOperate(actor) || liquid?.Drinkable != true || Mod_WaterVessel.IsEmptyAmount(target.Data.Amount)) return false;
        Mod_Food food = actor.itemMods.GetMod_ByID<Mod_Food>(ModText.Food);
        if (food == null) return false;
        float amount = Mathf.Min(1f, target.Data.Amount);
        food.DrinkWater(liquid.HydrationPerServing * amount, target.Item);
        LiquidDrinkEffectProcessor.Apply(actor, liquid);
        Remove(target, amount);
        return true;
    }

    public static float PourToGround(ILiquidVessel target, Item actor, float amount)
    {
        if (!target.CanOperate(actor)) return 0f;
        LiquidDefinition liquid = target.CurrentLiquid;
        float removed = Remove(target, amount);
        if (removed > 0f && liquid?.Category == "water") FarmlandSystem.TryAddGroundWater(actor.transform.position, removed);
        return removed;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool TransferFromInventory(ILiquidVessel target, ItemData source, Item actor, float maximum)
    {
        if (!GameNetwork.HasStateAuthority) return false;
        if (!target.CanOperate(actor) || source == null || ReferenceEquals(source, target.ItemData) ||
            source.Guid != 0 && source.Guid == target.ItemData.Guid ||
            !InventoryContextResolver.TryResolveContainingInventory(actor, source, out Inventory inventory)) return false;
        Item held = actor.itemMods?.GetMod_ByID<Inventory_HotBar>(ModText.Hotbar)?.CurentSelectItem;
        if (held?.itemData != null && (ReferenceEquals(held.itemData, source) || source.Guid != 0 && held.itemData.Guid == source.Guid))
        {
            var vessel = held.itemMods.GetMod_ByID<Mod_WaterVessel>(Mod_WaterVessel.ModuleId);
            if (vessel != null) return Transfer(vessel, target, actor);
        }
        if (!Mod_WaterVessel.TryRead(source, out var storage, out var state))
            return FillFromIngredient(target, source, actor, inventory, maximum);
        if (Mod_WaterVessel.IsEmptyAmount(state.Amount) ||
            !Mod_WaterVessel.IsEmptyAmount(target.Data.Amount) && !SameLiquid(state.LiquidId, target.Data.LiquidId)) return false;
        float moved = Quantize(Mathf.Min(state.Amount, Mathf.Max(0f, target.Capacity - target.Data.Amount)));
        if (moved <= 0f) return false;
        AddState(target, state.LiquidId, moved, state.Temperature);
        RemoveState(state, moved);
        storage.WriteData(state);
        target.CommitVessel();
        inventory.Data.NotifyItemStateChanged(source);
        ItemNetworkStateSerialization.NotifyRuntimeStateChanged(actor);
        return true;
    }

    private static bool FillFromIngredient(ILiquidVessel target, ItemData source, Item actor, Inventory inventory, float maximum)
    {
        if (float.IsNaN(maximum) || maximum <= 0f) return false;
        LiquidDefinition liquid = null;
        foreach (LiquidDefinition candidate in GameRes.Instance.LiquidDefinitions.Values)
            if (candidate.SourceItemId == source.IDName) { liquid = candidate; break; }
        if (liquid == null || !Mod_WaterVessel.IsEmptyAmount(target.Data.Amount) && !SameLiquid(target.Data.LiquidId, liquid.Id)) return false;
        foreach (ModuleData module in source.ModuleDataDic.Values)
            if (module is ModData_FoodData food && FoodObserverStateStore.ReadFloat(
                FoodObserverStateStore.Find(food, FoodObserverStateStore.ConsumptionStateKey), "EatingProgress", 0f) > 0f) return false;
        ItemSlot slot = inventory.Data.itemSlots.Find(value => ReferenceEquals(value?.itemData, source) || source.Guid != 0 && value?.itemData?.Guid == source.Guid);
        int amount = Mathf.FloorToInt(Mathf.Min(Mathf.Min(slot?.itemData?.Stack?.Amount ?? 0f, maximum),
            Mathf.Max(0f, target.Capacity - target.Data.Amount) + Mod_WaterVessel.AmountEpsilon));
        if (amount <= 0 || !inventory.Data.TryConsumeFromSlot(slot, amount, out _)) return false;
        AddState(target, liquid.Id, amount);
        target.CommitVessel();
        ItemNetworkStateSerialization.NotifyRuntimeStateChanged(actor);
        return true;
    }
    #endregion
}
