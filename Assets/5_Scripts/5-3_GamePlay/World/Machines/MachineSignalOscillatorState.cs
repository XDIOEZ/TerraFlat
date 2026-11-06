using System;
using MemoryPack;

/// <summary>只保存振荡配置，相位按所属世界交付的模拟秒数推进。</summary>
[Serializable, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class MachineSignalOscillatorState
{
    #region 配置与非持久时钟
    public const int MaximumHalfPeriodSteps = 100;
    [MemoryPackOrder(0)] public bool Enabled = true;
    [MemoryPackOrder(1)] public int HalfPeriodSteps = 10;
    [MemoryPackIgnore] public bool Output { get; private set; }
    private double phaseSeconds;

    public float GetFrequencyHz(float simulationStepSeconds) => 1f / (2f * HalfPeriodSteps * simulationStepSeconds);

    public bool TrySetFrequencyHz(float frequency, float simulationStepSeconds)
    {
        if (!float.IsFinite(frequency) || frequency < 1f / (2f * MaximumHalfPeriodSteps * simulationStepSeconds) ||
            frequency > 1f / (2f * simulationStepSeconds)) return false;
        int steps = (int)Math.Round(1d / (2d * frequency * simulationStepSeconds), MidpointRounding.AwayFromZero);
        if (HalfPeriodSteps != steps) { HalfPeriodSteps = steps; ResetPhase(); }
        return true;
    }

    public void AdvanceSimulationSeconds(float seconds, float simulationStepSeconds)
    {
        if (!Enabled) { ResetPhase(); return; }
        if (seconds <= 0f) return;
        double halfPeriod = HalfPeriodSteps * (double)simulationStepSeconds;
        phaseSeconds += seconds;
        double crossings = Math.Floor((phaseSeconds + .0000001d) / halfPeriod);
        if (crossings <= 0d) return;
        phaseSeconds = Math.Max(0d, phaseSeconds - crossings * halfPeriod);
        if (crossings % 2d == 1d) Output = !Output;
    }

    public void ResetPhase() { phaseSeconds = 0d; Output = false; }
    #endregion
}
