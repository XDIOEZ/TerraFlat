using System;
using System.Globalization;
using FlatWorld.Localization;
using UnityEngine;

/// <summary>振荡器组合既有工作状态信号源，配置与输出分别沿机器快照和信号增量同步。</summary>
public sealed class MachineSignalOscillatorLogic : MachineLogic
{
    #region 时钟、命令与持久化
    public const string PersistenceKey = "signal-oscillator";
    public MachineSignalOscillatorState State { get; private set; }
    public float FrequencyHz => State.GetFrequencyHz(MachineCatalog.Settings.TickSeconds);
    public override float TickInterval => 0f;
    public override bool IsActuallyWorking => Entity.Active && State.Enabled && State.Output;
    public override bool SupportsUnchangedSaveSnapshotReuse => true;
    public override GameObject PanelPrefab => GameRes.Instance.GetPrefab("UI_IndustrialFluid");
    public override string Status => FlatWorldLocalizationService.GetUiFormat(
        "频率 {0:0.###} Hz · 占空比 50%\n输出 {1} · {2}", FrequencyHz, Entity.SignalOutput ? 1 : 0,
        FlatWorldLocalizationService.GetUiText(State.Enabled ? "已启用" : "已禁用"));

    public MachineSignalOscillatorLogic(MachineEntity entity) : base(entity) => RestoreConfiguration(entity.Snapshot);

    private void RestoreConfiguration(ItemData snapshot)
    {
        State = MachinePersistence.Read<MachineSignalOscillatorState>(snapshot, PersistenceKey) ?? new MachineSignalOscillatorState();
        if (State.HalfPeriodSteps < 1 || State.HalfPeriodSteps > MachineSignalOscillatorState.MaximumHalfPeriodSteps)
            throw new InvalidOperationException("信号振荡器半周期配置无效。");
    }

    public override void Tick(float seconds) => State.AdvanceSimulationSeconds(seconds, MachineCatalog.Settings.TickSeconds);
    public override bool Execute(string operation, string argument, Item actor)
    {
        if (operation == "begin-interaction") return true;
        if (operation == "work") { State.Enabled = !State.Enabled; State.ResetPhase(); }
        else if (operation == "oscillator.frequency")
        {
            if (!float.TryParse(argument, NumberStyles.Float, CultureInfo.InvariantCulture, out float frequency) ||
                !State.TrySetFrequencyHz(frequency, MachineCatalog.Settings.TickSeconds)) return false;
        }
        else return false;
        Capture(); NotifyChanged(); return true;
    }

    public override void Capture() => MachinePersistence.Write(Entity.Snapshot, PersistenceKey, State);
    public override bool ApplyRemoteSnapshot(ItemData snapshot)
    { RestoreConfiguration(snapshot); NotifyRemoteChanged(); return true; }
    #endregion
}
