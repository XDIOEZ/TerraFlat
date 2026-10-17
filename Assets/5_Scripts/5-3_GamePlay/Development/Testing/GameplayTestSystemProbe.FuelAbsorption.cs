using System.Collections.Generic;
using MemoryPack;
using Newtonsoft.Json.Linq;

public static partial class GameplayTestSystemProbe
{
    #region 燃料吸收的真实槽位与扣料采样
    private static JObject WorldFuelAbsorption(JObject args)
    {
        const float duration = 3f;
        Data_GeneralItem fuel = EntityProbeData("test:absorption-fuel", 3f);
        fuel.Guid = 101;
        var slot = new ItemSlot(0) { itemData = fuel };
        var inventory = new Inventory_Data(new List<ItemSlot> { slot }, "独立燃料槽");
        var state = new FuelAbsorptionState();
        state.Observe(slot);
        state.Advance(2.9f, duration);
        bool premature = state.TryConsumeCompleted(inventory, duration, out _);
        float beforeAmount = fuel.Stack.Amount, beforeProgress = state.Progress01(duration);
        state.Observe(slot);
        float unchangedElapsed = state.ElapsedSeconds;
        state.Advance(.1f, duration);
        bool completed = state.TryConsumeCompleted(inventory, duration, out ItemData consumed);
        float afterAmount = fuel.Stack.Amount, afterProgress = state.Progress01(duration);
        bool duplicate = state.TryConsumeCompleted(inventory, duration, out _);

        state.Observe(slot);
        state.Advance(2f, duration);
        slot.itemData = null;
        state.Observe(slot);
        bool removedConsumed = state.TryConsumeCompleted(inventory, duration, out _);
        slot.itemData = fuel;
        state.Observe(slot);
        float returnedElapsed = state.ElapsedSeconds;

        state.Advance(2f, duration);
        Data_GeneralItem replacement = EntityProbeData(fuel.IDName, 4f);
        replacement.Guid = 202;
        slot.itemData = replacement;
        state.Observe(slot);
        float replacedElapsed = state.ElapsedSeconds;
        state.Advance(duration, duration);
        slot.itemData = fuel;
        bool staleConsumed = state.TryConsumeCompleted(inventory, duration, out _);

        state.Observe(slot);
        state.Advance(0f, duration);
        state.Advance(-1f, duration);
        state.Advance(float.NaN, duration);
        state.Advance(float.PositiveInfinity, duration);
        float invalidElapsed = state.ElapsedSeconds;
        state.Advance(1.5f, duration);
        FuelAbsorptionState restored = MemoryPackSerializer.Deserialize<FuelAbsorptionState>(MemoryPackSerializer.Serialize(state));
        float restoredElapsed = restored.ElapsedSeconds;
        bool restoredMatches = restored.Matches(slot);
        restored.Advance(1.5f, duration);
        bool restoredConsumed = restored.TryConsumeCompleted(inventory, duration, out _);

        state.Reset();
        state.Observe(slot);
        state.Advance(6f, duration);
        float clampedProgress = state.Progress01(duration);
        bool longStepConsumed = state.TryConsumeCompleted(inventory, duration, out _);
        return new JObject
        {
            ["premature"] = premature, ["beforeAmount"] = beforeAmount, ["beforeProgress"] = beforeProgress,
            ["unchangedElapsed"] = unchangedElapsed, ["completed"] = completed,
            ["consumedId"] = consumed?.IDName, ["afterAmount"] = afterAmount, ["afterProgress"] = afterProgress,
            ["duplicate"] = duplicate, ["removedConsumed"] = removedConsumed, ["returnedElapsed"] = returnedElapsed,
            ["replacedElapsed"] = replacedElapsed, ["staleConsumed"] = staleConsumed,
            ["replacementAmount"] = replacement.Stack.Amount, ["invalidElapsed"] = invalidElapsed,
            ["restoredElapsed"] = restoredElapsed, ["restoredMatches"] = restoredMatches,
            ["restoredConsumed"] = restoredConsumed, ["clampedProgress"] = clampedProgress,
            ["longStepConsumed"] = longStepConsumed, ["finalAmount"] = fuel.Stack.Amount,
            ["finalSlotEmpty"] = slot.itemData == null
        };
    }
    #endregion
}
