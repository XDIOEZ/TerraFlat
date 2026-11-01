using System;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Mathematics;

namespace FlatWorld.WorldModel
{
    /// <summary>邻火按经过时间判定点燃概率，已燃格按游戏秒扣除燃料。</summary>
    [BurstCompile]
    public static unsafe class WorldFireSimulationKernel
    {
        #region 连续输入与输出
        public struct Input
        {
            public WorldFireCellState State;
            public WorldCombustibilityValue Combustibility;
            public uint RandomSeed;
            public int HasBurningNeighbour;
        }

        public struct Output
        {
            public WorldFireCellState State;
            public float ConsumedFuelSeconds;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void AdvanceDelegate(Input* input, Output* output, int count, float seconds);
        private static readonly Lazy<AdvanceDelegate> compiled = new(() =>
            BurstCompiler.CompileFunctionPointer<AdvanceDelegate>(AdvanceBatchNative).Invoke);
        private static readonly bool runningInUnity = Type.GetType("UnityEngine.Application, UnityEngine.CoreModule") != null;

        public static void Advance(ReadOnlySpan<Input> inputs, Span<Output> outputs, float seconds)
        {
            if (outputs.Length < inputs.Length) throw new ArgumentException("火场输出缓冲区不足。", nameof(outputs));
            if (inputs.IsEmpty || seconds <= 0f) return;
            fixed (Input* input = inputs)
            fixed (Output* output = outputs)
                (runningInUnity ? compiled.Value : AdvanceBatchNative)(input, output, inputs.Length, seconds);
        }
        #endregion

        #region 火焰传播与燃料倒计时
        [BurstCompile(DisableDirectCall = true)]
#if UNITY_5_3_OR_NEWER
        [AOT.MonoPInvokeCallback(typeof(AdvanceDelegate))]
#endif
        private static void AdvanceBatchNative(Input* inputs, Output* outputs, int count, float seconds)
        {
            for (int i = 0; i < count; i++)
            {
                Input input = inputs[i];
                Output output = default;
                output.State.CellIndex = input.State.CellIndex;
                if (input.Combustibility.FuelSeconds <= 0f) { outputs[i] = output; continue; }
                if (input.State.Intensity > 0f)
                {
                    output.ConsumedFuelSeconds = math.min(seconds, input.Combustibility.FuelSeconds);
                    output.State.Intensity = input.Combustibility.FuelSeconds > seconds ? 1f : 0f;
                    output.State.SurfaceFuelBurnSeconds = input.State.SurfaceFuelBurnSeconds + output.ConsumedFuelSeconds;
                }
                else if (input.HasBurningNeighbour != 0)
                {
                    uint random = input.RandomSeed;
                    random ^= random >> 16; random *= 0x7feb352du;
                    random ^= random >> 15; random *= 0x846ca68bu; random ^= random >> 16;
                    float sample = (random & 0x00ffffffu) * (1f / 16777216f);
                    // 保留原 60 Hz 点燃概率在 10 Hz 环境步中的等效时间概率。
                    float probability = math.saturate(input.Combustibility.IgnitionProbability);
                    if (math.abs(seconds - 1f / 60f) > .000001f)
                        probability = 1f - math.pow(1f - probability, seconds * 60f);
                    if (sample < probability) output.State.Intensity = 1f;
                }
                outputs[i] = output;
            }
        }
        #endregion
    }
}
