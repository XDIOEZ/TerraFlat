using System;
using UnityEngine;

// 营养消耗只计算数值，通知和其他业务仍由食物模块处理。
internal static class FoodNutritionConsumptionData
{
    #region 连续消耗数值

    internal struct State
    {
        public float Carbohydrates, Fat, Protein, Water, Vitamins;
    }

    internal struct Input
    {
        public float Seconds, NutritionSpeed, WaterSpeed;
        public float RuntimeNutritionMultiplier, MovementNutritionMultiplier, BuffNutritionMultiplier;
        public float MovementWaterMultiplier, BuffWaterMultiplier, PlayerHungerMultiplier;
        public bool IsPlayer;
    }

    internal struct Result
    {
        public State State;
        public float ConsumedEnergy;
    }

    internal struct Sample
    {
        public State State;
        public Input Input;
        public Result Result;
    }

    internal static Result CalculateNutritionConsumption(State state, Input input)
    {
        var result = new Result { State = state };
        if (input.Seconds <= 0f)
            return result;

        float seconds = input.Seconds * (input.RuntimeNutritionMultiplier *
                        input.MovementNutritionMultiplier * input.BuffNutritionMultiplier);
        if (input.IsPlayer)
            seconds *= input.PlayerHungerMultiplier;

        float remainingEnergy = Mathf.Max(0f, seconds * input.NutritionSpeed);
        result.ConsumedEnergy = Drain(ref result.State.Carbohydrates, ref remainingEnergy);
        result.ConsumedEnergy += Drain(ref result.State.Fat, ref remainingEnergy);
        result.ConsumedEnergy += Drain(ref result.State.Protein, ref remainingEnergy);
        float usedWater = seconds * input.WaterSpeed * input.MovementWaterMultiplier * input.BuffWaterMultiplier;
        result.State.Water = Mathf.Max(0f, result.State.Water - usedWater);
        float vitaminLoss = seconds * (input.IsPlayer ? 0.001f : 0.01f);
        result.State.Vitamins = Mathf.Max(0f, result.State.Vitamins - vitaminLoss);
        return result;
    }

    internal static void CalculateNutritionConsumptionBatch(Span<Sample> samples)
    {
        for (int i = 0; i < samples.Length; i++)
            samples[i].Result = CalculateNutritionConsumption(samples[i].State, samples[i].Input);
    }

    private static float Drain(ref float value, ref float amount)
    {
        float used = Mathf.Min(Mathf.Max(0f, value), amount);
        value -= used;
        amount -= used;
        return used;
    }

    #endregion
}
