using FlatWorld.Gameplay.Progress;
using FlatWorld.Networking;
using UnityEngine;

/// <summary>组合设备复用炉体的燃料、点火和吸收事务，动力结算按实际负载扣燃值。</summary>
public sealed class MachineSolidFuelBurner
{
    #region 燃烧状态与事务
    private readonly MachineEntity entity;
    private readonly Inventory inventory;
    private readonly float absorptionSeconds;
    private Player ignitionActor;
    public FurnaceRuntimeState State { get; private set; }
    public FuelData Fuel => State.Fuel;
    public bool IsBurning => !MachineWorld.GetFluidState(entity).Ruptured &&
        State.Smelting.IsSmelting && !State.AwaitingIgnition && Fuel.Fuel.x > .00001f;
    public bool IsIgnited => State.Smelting.IsSmelting;
    public bool CanAct => IsIgnited || MachineWorld.CanSustainCombustion(entity);
    public float ConsumptionRate => Mathf.Max(0f, GameRuleConfiguration.Current.Production.FuelConsumptionMultiplier);

    public MachineSolidFuelBurner(MachineEntity entity, Inventory inventory, FurnaceRuntimeState state)
    {
        this.entity = entity;
        this.inventory = inventory;
        absorptionSeconds = entity.Definition.Fluid.SolidFuelAbsorptionSeconds;
        ApplyState(state);
    }

    public void ApplyState(FurnaceRuntimeState state)
    {
        State = state;
        State.Fuel ??= new FuelData { Fuel = Vector2.zero };
        State.Smelting ??= new ModSmeltingData();
        State.Absorption ??= new FuelAbsorptionState();
        Fuel.Fuel = new Vector2(Mathf.Max(0, Fuel.Fuel.x), entity.Definition.Fluid.SolidFuelCapacity);
    }

    private ItemSlot CurrentSlot()
    {
        int index = State.Absorption.SlotIndex;
        if (index < 0 || index >= inventory.Data.itemSlots.Count || inventory.IsSlotBeingDragged(index)) return null;
        ItemSlot slot = inventory.Data.itemSlots[index];
        return State.Absorption.Matches(slot) ? slot : null;
    }

    private bool FindFuel(bool tinder, out ItemSlot selected)
    {
        selected = null;
        foreach (ItemSlot slot in inventory.Data.itemSlots)
            if (!inventory.IsSlotBeingDragged(slot.Index) && FurnaceFuelRules.IsTinder(slot.itemData) == tinder &&
                FurnaceFuelRules.TryResolve(slot.itemData, tinder, 8f, 180f, out _, out _))
            { selected = slot; return true; }
        return false;
    }

    public bool Toggle(Player actor)
    {
        if (!GameNetwork.HasStateAuthority || MachineWorld.GetFluidState(entity).Ruptured) return false;
        if (IsIgnited) { Extinguish(); return true; }
        if (!MachineWorld.CanSustainCombustion(entity)) return false;
        if (!FindFuel(true, out ItemSlot source) &&
            (!FurnaceFuelRules.HasHeldIgnition(actor) || !FindFuel(false, out source))) return false;
        State.Absorption.Reset();
        State.Absorption.Observe(source);
        State.AwaitingIgnition = true;
        State.Smelting.IsSmelting = true;
        ignitionActor = actor;
        return true;
    }

    public void Extinguish()
    {
        State.Smelting.IsSmelting = false;
        State.AwaitingIgnition = false;
        State.Absorption.Reset();
        ignitionActor = null;
    }

    public void Tick(float seconds)
    {
        if (!MachineDefinition.Positive(seconds)) return;
        if (!MachineWorld.CanSustainCombustion(entity)) Extinguish();
        if (!IsIgnited) { State.Absorption.Reset(); return; }
        float remaining = seconds;
        while (remaining > .00001f)
        {
            if (!State.AwaitingIgnition && Fuel.Fuel.x + .01f >= Fuel.Fuel.y)
            { State.Absorption.Reset(); break; }
            ItemSlot source = CurrentSlot();
            if (source == null)
            {
                State.Absorption.Reset();
                if (State.AwaitingIgnition || !FindFuel(false, out source) && !FindFuel(true, out source))
                { if (!IsBurning) Extinguish(); break; }
                State.Absorption.Observe(source);
            }
            float step = Mathf.Min(remaining, State.Absorption.RemainingSeconds(absorptionSeconds));
            State.Absorption.Advance(step, absorptionSeconds);
            remaining -= step;
            if (State.Absorption.RemainingSeconds(absorptionSeconds) > .00001f) break;
            if (!FurnaceFuelRules.TryResolve(source.itemData, FurnaceFuelRules.IsTinder(source.itemData), 8f, 180f,
                out float value, out float temperature) || CurrentSlot() != source ||
                !State.Absorption.TryConsumeCompleted(inventory.Data, absorptionSeconds, out _))
            { State.Absorption.Reset(); if (State.AwaitingIgnition) Extinguish(); break; }
            Fuel.Fuel.x += value;
            Fuel.MaxTemperature = temperature;
            bool igniting = State.AwaitingIgnition;
            State.AwaitingIgnition = false;
            if (igniting) GameplayProgressEvents.PublishFurnaceIgnited(ignitionActor, entity.Definition.Id);
        }
    }

    public float Progress(int index) => State.Absorption.SlotIndex == index && CurrentSlot() != null
        ? State.Absorption.Progress01(absorptionSeconds) : 0;

    public double AvailableFullLoadSeconds => IsBurning && MachineWorld.CanSustainCombustion(entity)
        ? ConsumptionRate > 0 ? Fuel.Fuel.x / ConsumptionRate : double.PositiveInfinity : 0;

    public void ConsumeWork(double fullLoadSeconds)
    {
        // 耗水和耗燃料共用实际机械负载，空转不会凭空消耗燃值。
        Fuel.Fuel.x = Mathf.Max(0, Fuel.Fuel.x - (float)fullLoadSeconds * ConsumptionRate);
        if (Fuel.Fuel.x <= .00001f && !FindFuel(false, out _) && !FindFuel(true, out _)) Extinguish();
    }
    #endregion
}
