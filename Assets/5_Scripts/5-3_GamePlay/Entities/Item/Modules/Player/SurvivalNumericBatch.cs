using System;
using UnityEngine;

// 生存数值按阶段连续计算，环境采样和回调仍由模块按原顺序负责。
internal static class SurvivalNumericBatch
{
    #region 氧气消耗与缺氧

    internal struct OxygenConsumption
    {
        public float Current, Maximum, ConsumePerSecond, Seconds;
        public float Next;
        public bool HasFiniteChange;
    }

    internal static void Advance(ref OxygenConsumption step)
    {
        float change = -step.ConsumePerSecond * step.Seconds;
        step.HasFiniteChange = float.IsFinite(change);
        step.Next = step.HasFiniteChange
            ? ClampOxygen(step.Current + change, step.Maximum)
            : step.Current;
    }

    internal static float ClampOxygen(float value, float maximum)
        => Mathf.Clamp(value, 0f, Mathf.Max(0f, maximum));

    internal static void Advance(Span<OxygenConsumption> steps)
    {
        for (int i = 0; i < steps.Length; i++) Advance(ref steps[i]);
    }

    internal struct OxygenBreathing
    {
        public float Supplied, ConsumePerSecond, Seconds;
        public bool Blocked;
    }

    internal static void Advance(ref OxygenBreathing step)
        => step.Blocked = step.Supplied + .0001f < step.ConsumePerSecond * step.Seconds;

    internal static void Advance(Span<OxygenBreathing> steps)
    {
        for (int i = 0; i < steps.Length; i++) Advance(ref steps[i]);
    }

    internal struct OxygenHypoxia
    {
        public float BeforeConsumption, Current, ConsumePerSecond, Seconds, DamageClock;
    }

    internal static void Advance(ref OxygenHypoxia step)
    {
        if (step.Current > 0f) { step.DamageClock = 0f; return; }
        float depletedSeconds = step.BeforeConsumption <= 0f ? step.Seconds : step.ConsumePerSecond > 0f
            ? Mathf.Max(0f, step.Seconds - step.BeforeConsumption / step.ConsumePerSecond) : 0f;
        step.DamageClock += depletedSeconds;
    }

    internal static void Advance(Span<OxygenHypoxia> steps)
    {
        for (int i = 0; i < steps.Length; i++) Advance(ref steps[i]);
    }

    #endregion

    #region 气压危险与伤害计时

    internal struct PressureExposure
    {
        public double EnvironmentPressure;
        public float SafeMinimum, SafeMaximum, Buffer, BufferMaximum, RecoveryPerSecond, Seconds;
        public int Danger;
        public float LowDamageClock, HighDamageClock;
        public bool HasExposure, NotifyChanged;
    }

    internal static void Advance(ref PressureExposure step)
    {
        int next = step.EnvironmentPressure < step.SafeMinimum ? -1 : step.EnvironmentPressure > step.SafeMaximum ? 1 : 0;
        if (next != step.Danger) step.LowDamageClock = step.HighDamageClock = 0f;
        step.Danger = next;
        float before = step.Buffer;
        step.Buffer = Mathf.Clamp(before + (next == 0 ? step.Seconds * step.RecoveryPerSecond : -step.Seconds),
            0f, step.BufferMaximum);
        step.HasExposure = false;
        step.NotifyChanged = next == 0;
        if (next == 0) { step.LowDamageClock = step.HighDamageClock = 0f; return; }
        float exposure = Mathf.Max(0f, step.Seconds - before);
        if (exposure <= 0f) return;
        step.HasExposure = true;
        step.NotifyChanged = true;
        if (next < 0) step.LowDamageClock += exposure;
        else step.HighDamageClock += exposure;
    }

    internal static void Advance(Span<PressureExposure> steps)
    {
        for (int i = 0; i < steps.Length; i++) Advance(ref steps[i]);
    }

    #endregion

    #region 体温传热与危险缓冲

    internal struct TemperatureWaterCooling
    {
        public float NaturalTemperature, TargetTemperature, CoolingSpeed, Protection, Seconds;
        public bool HasTarget, InWater, WaterExitExpired;
        public bool ClearTarget, ApplyTemperature;
        public float NextTemperature;
    }

    internal static void Advance(ref TemperatureWaterCooling step)
    {
        step.ClearTarget = step.ApplyTemperature = false;
        step.NextTemperature = step.NaturalTemperature;
        if (!step.HasTarget) return;
        if (!step.InWater) { step.ClearTarget = step.WaterExitExpired; return; }
        if (step.NaturalTemperature <= step.TargetTemperature) { step.ClearTarget = true; return; }
        float multiplier = Mathf.Clamp01(1f - step.Protection);
        if (multiplier <= 0f) return;
        step.ApplyTemperature = true;
        step.NextTemperature = ThermalRuntime.AdvanceTowards(step.NaturalTemperature, step.TargetTemperature,
            step.CoolingSpeed, step.Seconds, multiplier);
    }

    internal static void Advance(Span<TemperatureWaterCooling> steps)
    {
        for (int i = 0; i < steps.Length; i++) Advance(ref steps[i]);
    }

    internal struct TemperatureAmbientTransfer
    {
        public float NaturalTemperature, AmbientTemperature, Seconds, NextTemperature;
    }

    internal static void Advance(ref TemperatureAmbientTransfer step)
        => step.NextTemperature = ThermalRuntime.AdvanceAmbient(step.NaturalTemperature, step.AmbientTemperature, step.Seconds);

    internal static void Advance(Span<TemperatureAmbientTransfer> steps)
    {
        for (int i = 0; i < steps.Length; i++) Advance(ref steps[i]);
    }

    internal struct TemperatureSafety
    {
        public float Temperature, SafeMinimum, SafeMaximum, Buffer, BufferMaximum, RecoveryPerSecond, Seconds;
        public bool TooCold, TooHot;
    }

    internal static void Advance(ref TemperatureSafety step)
    {
        step.TooCold = step.Temperature < step.SafeMinimum;
        step.TooHot = step.Temperature > step.SafeMaximum;
        float seconds = Mathf.Max(0f, step.Seconds);
        step.Buffer = Mathf.Clamp(step.Buffer +
            (step.TooCold || step.TooHot ? -seconds : seconds * step.RecoveryPerSecond), 0f, step.BufferMaximum);
    }

    internal static void Advance(Span<TemperatureSafety> steps)
    {
        for (int i = 0; i < steps.Length; i++) Advance(ref steps[i]);
    }

    #endregion
}
