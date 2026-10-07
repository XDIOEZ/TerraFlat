using System;
using UnityEngine;

/// <summary>
/// 通用容器加热能力：热源只提供库存、温度和经过时间，具体液体或坩埚材料如何处理由容器自身决定。
/// 因此高炉、熔炉、电炉等只要接入本能力，就不需要为每种容器新增热源代码。
/// </summary>
public sealed class Mod_VesselHeating : Module, IInventoryHeatTreatment
{
    public Ex_ModData ModData = new(); // 加热模块自身不保存容器内容。
    public override string CanonicalModuleId => "Mod_VesselHeating";
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;
    public override ModuleData _Data { get => ModData; set => ModData = (Ex_ModData)value; }

    protected override void OnLoad() { }
    protected override void OnSave() { }

    /// <summary>在原库存槽中处理液体；需要物品产出时先成功提交产物事务，再消耗液体。</summary>
    public bool ProcessHeat(Inventory input, Inventory output, float temperature, float seconds)
        => InventoryVesselHeating.ProcessHeat(input, output, temperature, seconds);
}

/// <summary>所有热源共用的托管容器处理器；纯数据炉体无需创建 Module。</summary>
public static class InventoryVesselHeating
{
    #region 库存加热
    public const float TemperatureTransferPerSecond = 10f;

    public static bool ProcessHeat(Inventory input, Inventory output, float temperature, float seconds)
    {
        bool handled = false;
        foreach (ItemSlot slot in input.Data.itemSlots)
        {
            if (input.IsSlotBeingDragged(slot.Index)) continue;
            if (Mod_Mortar.IsCrucibleItem(slot.itemData))
            {
                handled = true;
                Mod_Mortar.TryProcessCrucibleHeat(slot.itemData, temperature, seconds);
                input.Data.NotifyItemStateChanged(slot.itemData);
                continue;
            }

            if (!Mod_WaterVessel.TryRead(slot.itemData, out Ex_ModData_MemoryPackable storage, out LiquidContainerState state))
                continue;

            handled = true;
            if (Mod_WaterVessel.IsEmptyAmount(state.Amount) || string.IsNullOrWhiteSpace(state.LiquidId))
                continue;

            LiquidDefinition liquid = GameRes.ExistingInstance?.GetLiquidDefinition(state.LiquidId)
                ?? throw new InvalidOperationException($"液体容器引用了未注册液体：{state.LiquidId}");
            bool changed = AdvanceTemperature(state, temperature, seconds);
            LiquidHeatProcess heat = liquid.HeatProcess;
            if (heat == null)
            {
                if (changed)
                {
                    storage.WriteData(state);
                    input.Data.NotifyItemStateChanged(slot.itemData);
                }
                continue;
            }

            if (state.Temperature < heat.MinimumTemperature)
            {
                if (state.ProcessingSeconds > 0f)
                {
                    state.ProcessingSeconds = 0f;
                    changed = true;
                }
                if (changed)
                {
                    storage.WriteData(state);
                    input.Data.NotifyItemStateChanged(slot.itemData);
                }
                continue;
            }

            state.ProcessingSeconds += Math.Max(0f, seconds);
            changed = true;
            if (state.ProcessingSeconds + Mod_WaterVessel.AmountEpsilon < heat.Seconds)
            {
                storage.WriteData(state);
                input.Data.NotifyItemStateChanged(slot.itemData);
                continue;
            }

            switch (heat.Mode)
            {
                case LiquidHeatProcessMode.Transform:
                    state.LiquidId = heat.ResultLiquidId;
                    state.ProcessingSeconds = 0f;
                    break;

                case LiquidHeatProcessMode.ConsumeServing:
                    state.ProcessingSeconds = heat.Seconds;
                    if (state.Amount + Mod_WaterVessel.AmountEpsilon < heat.ConsumeAmount ||
                        !TryGrantOutput(output, heat.OutputItemId, heat.OutputAmount))
                    {
                        storage.WriteData(state);
                        input.Data.NotifyItemStateChanged(slot.itemData);
                        continue;
                    }

                    state.Amount = Mathf.Max(0f, state.Amount - heat.ConsumeAmount);
                    Mod_WaterVessel.NormalizeStoredAmount(state);
                    state.ProcessingSeconds = 0f;
                    if (Mod_WaterVessel.IsEmptyAmount(state.Amount))
                    {
                        state.Amount = 0f;
                        state.LiquidId = null;
                    }
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(heat.Mode), heat.Mode, "未知液体加热模式");
            }

            storage.WriteData(state);
            input.Data.NotifyItemStateChanged(slot.itemData);
        }
        return handled;
    }

    /// <summary>世界中的完整容器按所在格环境温度传热；无产物的液体转换可直接在原容器完成。</summary>
    public static bool ProcessWorldHeat(LiquidContainerState state, float temperature, float seconds)
    {
        if (state == null || Mod_WaterVessel.IsEmptyAmount(state.Amount) || string.IsNullOrWhiteSpace(state.LiquidId))
            return false;

        LiquidDefinition liquid = GameRes.ExistingInstance?.GetLiquidDefinition(state.LiquidId)
            ?? throw new InvalidOperationException($"液体容器引用了未注册液体：{state.LiquidId}");
        bool changed = AdvanceTemperature(state, temperature, seconds);
        LiquidHeatProcess heat = liquid.HeatProcess;
        if (heat?.Mode != LiquidHeatProcessMode.Transform)
            return changed;

        if (state.Temperature < heat.MinimumTemperature)
        {
            if (state.ProcessingSeconds > 0f)
            {
                state.ProcessingSeconds = 0f;
                changed = true;
            }
            return changed;
        }

        state.ProcessingSeconds += Mathf.Max(0f, seconds);
        changed = true;
        if (state.ProcessingSeconds + Mod_WaterVessel.AmountEpsilon < heat.Seconds)
            return changed;

        state.LiquidId = heat.ResultLiquidId;
        state.ProcessingSeconds = 0f;
        return true;
    }

    /// <summary>容器液体以每秒 10℃ 向热源温度靠拢，热源只提供目标温度而不瞬间覆写液温。</summary>
    private static bool AdvanceTemperature(LiquidContainerState state, float temperature, float seconds)
    {
        float before = state.Temperature;
        float target = Mathf.Max(0f, temperature);
        state.Temperature = ThermalRuntime.AdvanceTowards(
            state.Temperature,
            target,
            TemperatureTransferPerSecond,
            seconds);
        return !Mathf.Approximately(before, state.Temperature);
    }

    /// <summary>无产物规则直接视为成功；有产物时使用制作事务保证满输出不消耗液体。</summary>
    private static bool TryGrantOutput(Inventory output, string itemId, int amount)
    {
        if (string.IsNullOrWhiteSpace(itemId) || amount <= 0)
            return true;

        ItemData item = GameRes.Instance.CreateItemData(itemId);
        item.Stack.Amount = amount;
        if (!CraftingTransaction.TryCreateGrant(output, new[] { item }, out CraftingTransaction transaction, out _) ||
            !transaction.Commit(out _))
        {
            return false;
        }

        transaction.Complete();
        return true;
    }
    #endregion
}
