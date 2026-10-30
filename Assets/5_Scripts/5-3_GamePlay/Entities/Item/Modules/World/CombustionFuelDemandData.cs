using System;
using UnityEngine;

// 世界时钟只计算燃料需求，实际扣减仍由燃料模块完成。
internal static class CombustionFuelDemandData
{
    #region 世界时间燃料需求

    internal struct State
    {
        public float LastWorldTime;
    }

    internal struct Input
    {
        public float CurrentWorldTime, FuelConsumptionMultiplier;
    }

    internal struct Result
    {
        public State State;
        public float ElapsedWorldTime, FuelDemand;
    }

    internal struct Sample
    {
        public State State;
        public Input Input;
        public Result Result;
    }

    internal static Result CalculateWorldTimeFuelDemand(State state, Input input)
    {
        float elapsed = input.CurrentWorldTime - state.LastWorldTime;
        var result = new Result
        {
            State = new State { LastWorldTime = input.CurrentWorldTime },
            ElapsedWorldTime = elapsed
        };
        if (elapsed <= 0f)
            return result;
        result.FuelDemand = elapsed * Mathf.Max(0f, input.FuelConsumptionMultiplier);
        return result;
    }

    internal static void CalculateWorldTimeFuelDemandBatch(Span<Sample> samples)
    {
        for (int i = 0; i < samples.Length; i++)
            samples[i].Result = CalculateWorldTimeFuelDemand(samples[i].State, samples[i].Input);
    }

    #endregion
}
