using System;
using MemoryPack;

[Serializable, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class RadioBatteryState
{
    #region 电池自身状态
    [MemoryPackOrder(0)] public int Version = 1;
    [MemoryPackOrder(1)] public double RemainingJoules = RadioBatteryStorage.CapacityJoules;
    #endregion
}

public sealed class Mod_RadioBattery : Module
{
    #region 电池物品模块
    public const string ModuleId = "无线电电池模块";
    public override string CanonicalModuleId => ModuleId;
    public Ex_ModData_MemoryPackable modData = new() { ID = ModuleId };
    public override ModuleData _Data { get => modData; set => modData = (Ex_ModData_MemoryPackable)value; }
    protected override void OnLoad() { }
    protected override void OnSave() { }
    #endregion
}

/// <summary>直接操作电池实例的冷数据，不依赖电池槽创建运行时物品。</summary>
public static class RadioBatteryStorage
{
    #region 冷电池能量
    public const double CapacityJoules = 3600d;

    public static bool TryGetRemainingJoules(ItemData battery, out double joules)
    {
        joules = 0d;
        if (battery?.Stack?.Amount != 1f ||
            FluidTankStorage.FindBinary(battery, Mod_RadioBattery.ModuleId) is not Ex_ModData_MemoryPackable binary ||
            !binary.Enabled) return false;
        RadioBatteryState state = binary.ReadRuntimeState<RadioBatteryState>() ?? new RadioBatteryState();
        if (state.Version != 1 || !double.IsFinite(state.RemainingJoules) ||
            state.RemainingJoules < 0d || state.RemainingJoules > CapacityJoules) return false;
        joules = state.RemainingJoules;
        return true;
    }

    public static bool TryConsumeExactJoules(ItemData battery, double joules)
    {
        if (!double.IsFinite(joules) || joules < 0d || !TryGetRemainingJoules(battery, out double remaining) || remaining < joules)
            return false;
        var binary = FluidTankStorage.FindBinary(battery, Mod_RadioBattery.ModuleId);
        RadioBatteryState state = binary.ReadRuntimeState<RadioBatteryState>() ?? new RadioBatteryState();
        state.RemainingJoules = remaining - joules;
        binary.WriteRuntimeState(state);
        return true;
    }
    #endregion
}
