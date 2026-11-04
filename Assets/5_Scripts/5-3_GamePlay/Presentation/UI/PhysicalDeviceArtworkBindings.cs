using System;
using UnityEngine;
using static PhysicalDeviceArtworkView;

/// <summary>现有会话向器物视图提供类型化读数，不解析文案或推进设备。</summary>
public static class PhysicalDeviceArtworkBindings
{
    #region 既有会话的表现接入
    public static void BindPhysicalDeviceArtwork(this BasePanel panel, string itemId, Func<OperatingState> read)
    {
        PhysicalDeviceArtworkView artwork = panel.GetComponentInChildren<PhysicalDeviceArtworkView>(true);
        if (artwork != null) artwork.Bind(itemId, read);
    }

    public static void BindPhysicalDeviceArtwork(this BasePanel panel, MachineEntity entity)
    {
        MechanicalPanelView gauges = panel.GetComponent<MechanicalPanelView>();
        panel.BindPhysicalDeviceArtwork(entity.Definition.Id, () => ReadMachine(entity, gauges));
    }

    public static OperatingState ReadHeating(MaterialHeatingProcessor heater)
    {
        ItemData material = heater.Input.Data.GetItemSlot(0)?.itemData;
        ItemData output = heater.Output.Data.GetItemSlot(0)?.itemData;
        return new OperatingState
        {
            Heat = material?.MatterState?.Initialized == true ? heater.Progress01 : 0f,
            Progress = heater.Progress01,
            FireSeed = output?.Tags?.Contains(Tag.CombustionTinder) == true
        };
    }

    public static Sprite ReadInstalledToolSprite(Inventory inventory)
        => ReadInventoryContentsSprite(inventory, 0);

    private static Sprite ReadInventoryContentsSprite(Inventory inventory, int index)
    {
        return inventory?.Data.GetItemSlot(index)?.itemData != null && inventory.itemSlot_UI.Count > index
            ? inventory.itemSlot_UI[index].image.sprite : null;
    }

    public static OperatingState ReadGauges(MechanicalPanelView view)
    {
        return new OperatingState
        {
            Liquid = view?.WaterCapacity != null ? view.WaterCapacity.normalizedValue : 0f,
            Gas = view?.GasCapacity != null ? view.GasCapacity.normalizedValue : 0f,
            Charge = view?.BatteryCharge != null ? view.BatteryCharge.normalizedValue : 0f,
            Fuel = view?.FuelRemaining != null ? view.FuelRemaining.normalizedValue : 0f
        };
    }
    #endregion

    #region 数据机器的只读状态投影
    public static OperatingState ReadMachine(MachineEntity entity, MechanicalPanelView gauges)
    {
        OperatingState result = ReadGauges(gauges);
        result.SignedRpm = entity.Rpm;
        result.Open = entity.Engaged;
        result.Working = entity.SpeedRpm > 0f || entity.ElectricalSuppliedWatts > 0f;
        result.Progress = entity.Processor?.Progress01 ?? entity.Logic?.Progress01 ?? 0f;
        switch (entity.Logic)
        {
            case ManualProcessingLogic manual:
                result.InputAttachment = ReadInstalledToolSprite(manual.ToolInventory);
                break;
            case FireDrillLogic drill:
                return ReadHeating(drill.Heater);
            case FurnaceLogic furnace:
                result.Heat = Mathf.InverseLerp(20f, 1000f, furnace.Data.Temperature);
                result.Burning = furnace.IsBurning;
                result.Fuel = furnace.FuelRatio;
                break;
            case InductionStoveLogic stove:
                result.Heat = Mathf.InverseLerp(20f, 1000f, stove.Temperature);
                break;
            case FluidMachineLogic fluid:
                result.Open = fluid.State.ValveOpen;
                result.Burning = fluid.IsBurning;
                result.Heat = fluid.IsBurning ? 1f : 0f;
                result.Fuel = fluid.FuelRatio;
                if (fluid.State.Ruptured || entity.Definition.Fluid.Kind is "gas-pump" or "liquid-pump" &&
                    string.IsNullOrEmpty(fluid.State.LastFlowPhase))
                { result.SignedRpm = 0f; result.Working = false; }
                break;
            case DryingRackLogic rack:
                for (int i = 0; i < Mathf.Min(4, rack.Inventory.Data.itemSlots.Count); i++)
                    result.SetContents(i, ReadInventoryContentsSprite(rack.Inventory, i));
                for (int i = 0; i < rack.Inventory.Data.itemSlots.Count; i++)
                    result.Progress = Mathf.Max(result.Progress, rack.GetProgress(i));
                break;
            case CompostLogic compost:
                for (int i = 0; i < Mathf.Min(4, compost.Inventory.Data.itemSlots.Count); i++)
                    result.SetContents(i, ReadInventoryContentsSprite(compost.Inventory, i));
                for (int i = 0; i < compost.State.Elapsed.Count; i++)
                    result.Progress = Mathf.Max(result.Progress, compost.State.Elapsed[i] / compost.CompostSeconds);
                break;
            case CultivatorLogic cultivator:
                for (int i = 0; i < Mathf.Min(4, cultivator.State.Plots.Count); i++)
                {
                    cultivator.TryGetPlotVisual(i, out Sprite sprite, out _, out float scale);
                    result.SetContents(i, sprite, scale);
                }
                foreach (CultivatorPlotState plot in cultivator.State.Plots)
                    result.Progress = Mathf.Max(result.Progress, plot.CropId != null ? plot.Growth : 0f);
                break;
        }
        return result;
    }
    #endregion
}
