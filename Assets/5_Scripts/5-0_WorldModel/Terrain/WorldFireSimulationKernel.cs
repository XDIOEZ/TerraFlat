using System;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Mathematics;

namespace FlatWorld.WorldModel
{
    /// <summary>火势、灭火和蔓延只对冻结输入批量计算，结果在整轮结束后统一提交。</summary>
    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High)]
    public static unsafe class WorldFireSimulationKernel
    {
        #region 连续输入与输出
        public struct Parameters
        {
            public float InitialIntensity, GrowthPerSecond, DecayPerSecond, RainLossPerSecond;
            public float SpreadRatePerSecond, GrassBurnDurationSeconds;
            public static Parameters Default => new()
            {
                InitialIntensity = .25f, GrowthPerSecond = .0625f, DecayPerSecond = .125f,
                RainLossPerSecond = .25f, SpreadRatePerSecond = .08f, GrassBurnDurationSeconds = 10f
            };
        }

        public struct Input
        {
            public WorldFireCellState State;
            public float Rain, Moisture, IgnitionPressure, SurfaceFuelDurationSeconds;
            public uint RandomSeed;
            public int HasFuel, HasGrass, WaterContact, SupportsCombustion;
        }

        public struct Output
        {
            public WorldFireCellState State;
            public float ExposureSeconds;
            public int ConsumedSurfaceFuel;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void AdvanceDelegate(Parameters* parameters, Input* input, Output* output, int count, float seconds);
        private static readonly Lazy<AdvanceDelegate> compiled = new(() =>
            BurstCompiler.CompileFunctionPointer<AdvanceDelegate>(AdvanceBatchNative).Invoke);
        private static readonly bool runningInUnity = Type.GetType("UnityEngine.Application, UnityEngine.CoreModule") != null;

        public static void Advance(in Parameters settings, ReadOnlySpan<Input> inputs, Span<Output> outputs, float seconds)
        {
            if (outputs.Length < inputs.Length) throw new ArgumentException("火场输出缓冲区不足。", nameof(outputs));
            if (inputs.IsEmpty || seconds <= 0f) return;
            Parameters parameters = settings;
            fixed (Input* input = inputs)
            fixed (Output* output = outputs)
                (runningInUnity ? compiled.Value : AdvanceBatchNative)(&parameters, input, output, inputs.Length, seconds);
        }
        #endregion

        #region 火势数值核
        [BurstCompile]
#if UNITY_5_3_OR_NEWER
        [AOT.MonoPInvokeCallback(typeof(AdvanceDelegate))]
#endif
        private static void AdvanceBatchNative(Parameters* parameters, Input* inputs, Output* outputs, int count, float seconds)
        {
            for (int i = 0; i < count; i++)
            {
                Input input = inputs[i];
                Output output = default;
                output.State.CellIndex = input.State.CellIndex;
                // 水和缺氧先消灭本格，不能在本轮继续造成伤害或点燃邻格。
                if (input.WaterContact != 0 || input.SupportsCombustion == 0)
                { outputs[i] = output; continue; }
                float intensity = input.State.Intensity;
                if (intensity <= 0f)
                {
                    float pressure = math.max(0f, input.IgnitionPressure) * parameters->SpreadRatePerSecond *
                        (1f - math.saturate(input.Moisture)) * (1f - math.saturate(input.Rain));
                    uint random = input.RandomSeed;
                    random ^= random >> 16; random *= 0x7feb352du;
                    random ^= random >> 15; random *= 0x846ca68bu; random ^= random >> 16;
                    float sample = (random & 0x00ffffffu) * (1f / 16777216f);
                    if (input.HasFuel != 0 && sample < 1f - math.exp(-pressure * seconds))
                        output.State.Intensity = parameters->InitialIntensity;
                    outputs[i] = output;
                    continue;
                }
                float growth = input.HasFuel != 0 ? parameters->GrowthPerSecond : -parameters->DecayPerSecond;
                output.State.Intensity = math.saturate(intensity +
                    (growth - parameters->RainLossPerSecond * math.saturate(input.Rain)) * seconds);
                output.ExposureSeconds = (intensity + output.State.Intensity) * .5f * seconds;
                output.State.SurfaceFuelBurnSeconds = input.SurfaceFuelDurationSeconds > 0f
                    ? input.State.SurfaceFuelBurnSeconds + output.ExposureSeconds : 0f;
                if (input.SurfaceFuelDurationSeconds > 0f && output.State.SurfaceFuelBurnSeconds >= input.SurfaceFuelDurationSeconds)
                {
                    output.ConsumedSurfaceFuel = 1;
                    output.State.SurfaceFuelBurnSeconds = 0f;
                }
                if (output.State.Intensity <= 0f) output.State.SurfaceFuelBurnSeconds = 0f;
                outputs[i] = output;
            }
        }
        #endregion
    }
}
