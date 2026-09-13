using System;
using UnityEngine;

/// <summary>
/// 通用液体加热能力：炉体只提供温度和经过时间，具体液体如何转化、消耗以及产出什么物品全部读取 LiquidDefinition。
/// 因此 MOD 新液体可以直接声明 heatProcess，不需要为每种液体新增炉体代码。
/// </summary>
public sealed class Mod_VesselHeating : Module, IInventoryHeatTreatment
{
    public Ex_ModData ModData = new(); // 加热模块自身不保存容器内容。
    public override string CanonicalModuleId => "Mod_VesselHeating";
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;
    public override ModuleData _Data { get => ModData; set => ModData = (Ex_ModData)value; }

    public override void Load() { }
    public override void Save() { }

    /// <summary>在原库存槽中处理液体；需要物品产出时先成功提交产物事务，再消耗液体。</summary>
    public bool ProcessHeat(Inventory input, Inventory output, float temperature, float seconds)
    {
        bool handled = false;
        foreach (ItemSlot slot in input.Data.itemSlots)
        {
            if (!Mod_WaterVessel.TryRead(slot.itemData, out Ex_ModData_MemoryPackable storage, out LiquidContainerState state))
                continue;

            handled = true;
            if (Mod_WaterVessel.IsEmptyAmount(state.Amount) || string.IsNullOrWhiteSpace(state.LiquidId))
                continue;

            LiquidDefinition liquid = GameRes.ExistingInstance?.GetLiquidDefinition(state.LiquidId)
                ?? throw new InvalidOperationException($"液体容器引用了未注册液体：{state.LiquidId}");
            LiquidHeatProcess heat = liquid.HeatProcess;
            if (heat == null || temperature < heat.MinimumTemperature)
                continue;

            state.ProcessingSeconds += Math.Max(0f, seconds);
            if (state.ProcessingSeconds < heat.Seconds)
            {
                storage.WriteData(state);
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
}
