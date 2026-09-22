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

    public override void Load() { }
    public override void Save() { }

    /// <summary>在原库存槽中处理液体；需要物品产出时先成功提交产物事务，再消耗液体。</summary>
    public bool ProcessHeat(Inventory input, Inventory output, float temperature, float seconds)
    {
        bool handled = false;
        foreach (ItemSlot slot in input.Data.itemSlots)
        {
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
