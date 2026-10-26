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
    public static bool ProcessHeat(Inventory input, Inventory output, float temperature, float seconds,
        float heatingRateMultiplier = 1f, float initialMaterialTemperature = TemperatureMgr.DefaultAmbientTemperature)
    {
        if (input?.Data?.itemSlots == null || !float.IsFinite(temperature) || !float.IsFinite(seconds) || seconds <= 0f ||
            !float.IsFinite(heatingRateMultiplier) || heatingRateMultiplier < 0f ||
            !float.IsFinite(initialMaterialTemperature)) return false;
        bool handled = false;
        foreach (ItemSlot slot in input.Data.itemSlots)
        {
            if (input.IsSlotBeingDragged(slot.Index)) continue;
            if (Mod_Mortar.IsCrucibleItem(slot.itemData))
            { handled = true; Mod_Mortar.TryProcessCrucibleHeat(slot.itemData, temperature, seconds, heatingRateMultiplier, initialMaterialTemperature); input.Data.NotifyItemStateChanged(slot.itemData); continue; }
            if (!Mod_WaterVessel.TryRead(slot.itemData, out var storage, out var state)) continue;
            handled = true;
            void Persist()
            {
                // 制作事务可能重建同 GUID 的冷物品，重新找到真实槽位里的模块再写入。
                if (!Mod_WaterVessel.TryRead(slot.itemData, out var currentStorage, out _)) throw new InvalidOperationException("加热容器已离开来源库存。");
                currentStorage.WriteData(state); input.Data.NotifyItemStateChanged(slot.itemData);
            }
            if (ProcessContainedLiquidHeat(state, output, temperature, seconds, Persist, heatingRateMultiplier)) Persist();
        }
        return handled;
    }
    public static bool ProcessWorldHeat(LiquidContainerState state, float temperature, float seconds)
        => ProcessComponents(state, temperature, seconds, null, null, ambientTransfer: true);
    // 坩埚与普通容器复用同一液体热加工事务，产物继承实际液温。
    public static bool ProcessContainedLiquidHeat(LiquidContainerState state, Inventory output,
        float temperature, float seconds, Action persist, float heatingRateMultiplier = 1f)
        => ProcessComponents(state, temperature, seconds,
            (id, amount) => PrepareOutput(output, id, amount, state.Temperature), persist,
            heatingRateMultiplier: heatingRateMultiplier);
    private static bool ProcessComponents(LiquidContainerState state, float temperature, float seconds, Func<string, int, CraftingTransaction> prepare, Action persist, bool ambientTransfer = false, float heatingRateMultiplier = 1f)
    {
        if (state == null || state.Amount <= Mod_WaterVessel.AmountEpsilon || !float.IsFinite(temperature) || !float.IsFinite(seconds) || seconds <= 0f) return false;
        MixedLiquidContents.Ensure(state);
        float beforeTemperature = state.Temperature;
        // 只缩放主动升温，液体加工计时和自然冷却继续使用真实秒数。
        float temperatureSeconds = ThermalRuntime.ResolveHeatingSeconds(
            state.Temperature, temperature, seconds, heatingRateMultiplier);
        state.Temperature = ambientTransfer
            ? ThermalRuntime.AdvanceAmbient(state.Temperature, Mathf.Max(-273.15f, temperature), seconds)
            : ThermalRuntime.AdvanceTowards(state.Temperature, Mathf.Max(-273.15f, temperature), TemperatureTransferPerSecond, temperatureSeconds);
        bool changed = beforeTemperature != state.Temperature;
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
                try
                {
                    if (!MixedLiquidContents.RemoveSpecies(state, id, heat.ConsumeAmount)) continue;
                    if (!transaction.Commit(out _)) { MixedLiquidContents.Restore(state, before); continue; }
                    persist?.Invoke(); transaction.Complete();
                }
                catch (Exception processingFailure)
                {
                    // 持久化或通知失败时同时撤回产物与液体，恢复写入失败也保留原始异常。
                    var failures = new List<Exception> { processingFailure };
                    try { transaction.Rollback(); } catch (Exception error) { failures.Add(error); }
                    try { MixedLiquidContents.Restore(state, before); } catch (Exception error) { failures.Add(error); }
                    try { persist?.Invoke(); } catch (Exception error) { failures.Add(error); }
                    if (failures.Count > 1) throw new AggregateException("液体热加工失败且恢复时发生异常。", failures);
                    throw;
                }
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
    private static CraftingTransaction PrepareOutput(Inventory output, string itemId, int amount, float temperature)
    {
        if (string.IsNullOrWhiteSpace(itemId) || amount <= 0 || output == null) return null;
        ItemData item = GameRes.Instance.CreateItemData(itemId); item.Stack.Amount = amount;
        ItemMatterRuntime.EnsureInitialized(item, temperature);
        return CraftingTransaction.TryCreateGrant(output, new[] { item }, out CraftingTransaction transaction, out _) ? transaction : null;
    }
    #endregion
}
