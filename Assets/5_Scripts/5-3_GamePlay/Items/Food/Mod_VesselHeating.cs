using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>热源只提供温度和时间，各液体组分沿原有规则独立加工。</summary>
public sealed class Mod_VesselHeating : Module, IInventoryHeatTreatment
{
    public Ex_ModData ModData = new();
    public override string CanonicalModuleId => "Mod_VesselHeating";
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;
    public override ModuleData _Data { get => ModData; set => ModData = (Ex_ModData)value; }
    protected override void OnLoad() { }
    protected override void OnSave() { }
    public bool ProcessHeat(Inventory input, Inventory output, float temperature, float seconds)
        => InventoryVesselHeating.ProcessHeat(input, output, temperature, seconds);
}

public static class InventoryVesselHeating
{
    #region 混合液体热加工
    public const float TemperatureTransferPerSecond = 10f;
    public static bool ProcessHeat(Inventory input, Inventory output, float temperature, float seconds)
    {
        bool handled = false;
        foreach (ItemSlot slot in input.Data.itemSlots)
        {
            if (input.IsSlotBeingDragged(slot.Index)) continue;
            if (Mod_Mortar.IsCrucibleItem(slot.itemData))
            { handled = true; Mod_Mortar.TryProcessCrucibleHeat(slot.itemData, temperature, seconds); input.Data.NotifyItemStateChanged(slot.itemData); continue; }
            if (!Mod_WaterVessel.TryRead(slot.itemData, out var storage, out var state)) continue;
            handled = true;
            void Persist()
            {
                // 制作事务可能重建同 GUID 的冷物品，重新找到真实槽位里的模块再写入。
                if (!Mod_WaterVessel.TryRead(slot.itemData, out var currentStorage, out _)) throw new InvalidOperationException("加热容器已离开来源库存。");
                currentStorage.WriteData(state); input.Data.NotifyItemStateChanged(slot.itemData);
            }
            if (ProcessComponents(state, temperature, seconds, (id, amount) => PrepareOutput(output, id, amount), Persist)) Persist();
        }
        return handled;
    }
    public static bool ProcessWorldHeat(LiquidContainerState state, float temperature, float seconds)
        => ProcessComponents(state, temperature, seconds, null, null);
    private static bool ProcessComponents(LiquidContainerState state, float temperature, float seconds, Func<string, int, CraftingTransaction> prepare, Action persist)
    {
        if (state == null || state.Amount <= Mod_WaterVessel.AmountEpsilon || !float.IsFinite(temperature) || !float.IsFinite(seconds) || seconds <= 0f) return false;
        MixedLiquidContents.Ensure(state);
        float beforeTemperature = state.Temperature;
        state.Temperature = ThermalRuntime.AdvanceTowards(state.Temperature, Mathf.Max(-273.15f, temperature), TemperatureTransferPerSecond, seconds);
        bool changed = !Mathf.Approximately(beforeTemperature, state.Temperature);
        var ids = new List<string>(state.Composition.Keys);
        foreach (string id in ids)
        {
            LiquidDefinition liquid = GameRes.ExistingInstance?.GetLiquidDefinition(id) ?? throw new InvalidOperationException($"液体定义不存在：{id}");
            LiquidHeatProcess heat = liquid.HeatProcess;
            if (heat == null || heat.Mode == LiquidHeatProcessMode.ConsumeServing && prepare == null) continue;
            float available = MixedLiquidContents.GetAmount(state, id) - MixedLiquidContents.Reserved(state, id);
            if (available < 1f - Mod_WaterVessel.AmountEpsilon) continue;
            state.ComponentProcessingSeconds.TryGetValue(id, out float elapsed);
            if (state.Temperature < heat.MinimumTemperature)
            { if (elapsed > 0f) { state.ComponentProcessingSeconds[id] = 0f; changed = true; } continue; }
            elapsed = Mathf.Min(heat.Seconds, elapsed + seconds); state.ComponentProcessingSeconds[id] = elapsed; changed = true;
            if (elapsed + Mod_WaterVessel.AmountEpsilon < heat.Seconds) continue;
            if (heat.Mode == LiquidHeatProcessMode.Transform)
            { MixedLiquidContents.TransformSpecies(state, id, heat.ResultLiquidId); }
            else if (heat.Mode == LiquidHeatProcessMode.ConsumeServing)
            {
                if (available + Mod_WaterVessel.AmountEpsilon < heat.ConsumeAmount) continue;
                CraftingTransaction transaction = prepare(heat.OutputItemId, heat.OutputAmount);
                if (transaction == null) continue;
                var before = MixedLiquidContents.Clone(state);
                if (!MixedLiquidContents.RemoveSpecies(state, id, heat.ConsumeAmount)) continue;
                if (!transaction.Commit(out _)) { MixedLiquidContents.Restore(state, before); continue; }
                persist?.Invoke(); transaction.Complete();
            }
            else throw new ArgumentOutOfRangeException(nameof(heat.Mode));
            state.ComponentProcessingSeconds[id] = 0f;
        }
        float progress = 0f;
        foreach (float value in state.ComponentProcessingSeconds.Values) progress = Mathf.Max(progress, value);
        state.ProcessingSeconds = progress;
        if (changed) { state.Revision++; MixedLiquidContents.SynchronizeAmounts(state); }
        return changed;
    }
    private static CraftingTransaction PrepareOutput(Inventory output, string itemId, int amount)
    {
        if (string.IsNullOrWhiteSpace(itemId) || amount <= 0 || output == null) return null;
        ItemData item = GameRes.Instance.CreateItemData(itemId); item.Stack.Amount = amount;
        return CraftingTransaction.TryCreateGrant(output, new[] { item }, out CraftingTransaction transaction, out _) ? transaction : null;
    }
    #endregion
}
