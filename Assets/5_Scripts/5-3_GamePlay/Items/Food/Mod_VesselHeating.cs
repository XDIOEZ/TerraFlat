using System;

/// <summary>水容器批量加热能力：脏淡水达到 100℃持续 20 秒后变为干净饮用水；海水每 12 秒蒸发一份并产出一份盐。</summary>
public sealed class Mod_VesselHeating : Module, IInventoryHeatTreatment
{
    public Ex_ModData ModData = new(); // 加热配置，不重复保存容器状态。
    public float boilingTemperature = 100f; // 加工温度。
    public float boilingSeconds = 20f; // 一罐淡水处理时间。
    public float saltSecondsPerServing = 12f; // 每份海水制盐时间。
    public string saltItemId = "Salt"; // 盐产物。
    public override string CanonicalModuleId => "Mod_VesselHeating";
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;
    public override ModuleData _Data { get => ModData; set => ModData = (Ex_ModData)value; }
    /// <summary>加工时间必须为正，配方和水量不依赖炉体隐藏默认值。</summary>
    public override void Load()
    {
        if (boilingSeconds <= 0f || saltSecondsPerServing <= 0f)
            throw new InvalidOperationException("水处理时长必须大于零。");
    }
    /// <summary>处理状态存放在水容器自身。</summary>
    public override void Save() { }
    /// <summary>在原槽中处理水；盐的原子发放成功后才减少海水，输出满时保持待结算状态。</summary>
    public bool ProcessHeat(Inventory input, Inventory output, float temperature, float seconds)
    {
        bool handled = false;
        foreach (ItemSlot slot in input.Data.itemSlots)
        {
            if (!Mod_WaterVessel.TryRead(slot.itemData, out Ex_ModData_MemoryPackable storage, out WaterVesselState state))
                continue;
            handled = true;
            if (temperature < boilingTemperature || state.Quality is VesselWaterQuality.Empty or VesselWaterQuality.Drinkable)
                continue;
            state.ProcessingSeconds += seconds;
            if (state.Quality == VesselWaterQuality.Dirty && state.ProcessingSeconds >= boilingSeconds)
            {
                state.Quality = VesselWaterQuality.Drinkable;
                state.ProcessingSeconds = 0f;
            }
            else if (state.Quality == VesselWaterQuality.Sea && state.ProcessingSeconds >= saltSecondsPerServing)
            {
                state.ProcessingSeconds = saltSecondsPerServing;
                ItemData salt = GameRes.Instance.CreateItemData(saltItemId);
                salt.Stack.Amount = 1;
                if (CraftingTransaction.TryCreateGrant(output, new[] { salt }, out CraftingTransaction transaction, out _) &&
                    transaction.Commit(out _))
                {
                    state.Amount--;
                    state.ProcessingSeconds = 0f;
                    if (state.Amount == 0) state.Quality = VesselWaterQuality.Empty;
                    storage.WriteData(state);
                    transaction.Complete();
                }
            }
            storage.WriteData(state);
        }
        return handled;
    }
}
