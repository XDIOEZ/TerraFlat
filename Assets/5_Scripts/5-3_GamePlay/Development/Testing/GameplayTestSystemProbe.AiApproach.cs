using System.Collections.Generic;
using FlatWorld.Navigation;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 追近距离与动作距离
    /// <summary>只采样正式共用规则，Lua 断言范围、计时与步长；不接触运行世界。</summary>
    private static JObject EntityAiApproachRanges(JObject args)
    {
        float range = (float)Number(args, "actionRange", .9d);
        float preferred = (float)Number(args, "stopDistance", .3d);
        float stop = AIApproachRange.ResolveStopDistance(preferred, range);
        float distance = range * .8f;
        return new JObject
        {
            ["stopDistance"] = stop,
            ["actionRange"] = range,
            ["movingTimer"] = AIApproachRange.AdvanceActionTimer(.2f, .1f, distance * distance, range),
            ["outsideTimer"] = AIApproachRange.AdvanceActionTimer(.7f, .1f, range * range * 4f, range),
            ["clampedStep"] = AIApproachRange.ClampMovementStep(range, range * 10f, stop),
            ["innerStep"] = AIApproachRange.ClampMovementStep(stop * .5f, 1f, stop),
            ["invalidStop"] = AIApproachRange.ResolveStopDistance(float.NaN, range),
            ["invalidAction"] = AIApproachRange.IsInActionRange(float.NaN, range)
        };
    }

    private static JObject EntityAiForagingEmptyTargets(JObject args)
    {
        GameObject root = new("[AnimalForagingEmptyTargets]") { hideFlags = HideFlags.HideAndDontSave };
        int refreshCalls = 0, movementCalls = 0, stopCalls = 0, grassConsumptionCalls = 0;
        try
        {
            var foraging = new AIAnimalForaging(
                root.transform,
                detector: null,
                food: null,
                refreshDetector: () => refreshCalls++,
                moveToInteractionTarget: (target, range) => movementCalls++,
                distanceTo: _ => 0f,
                stopMovement: () => stopCalls++,
                onGrassConsumed: _ => grassConsumptionCalls++);
            var edibleTags = new List<string> { "Food" };

            bool noFoodTarget = !foraging.TryFindFoodTarget(edibleTags);
            bool noGrassTarget = !foraging.TryAcquireGrassTarget(false, 1.2f, 8f, 0.8f);
            foraging.TickForage(false, edibleTags, 1.2f, 8f, 0.8f);
            foraging.TickEat(false, edibleTags, 1.2f, 8f, 0.8f, 1f, 1f);
            foraging.ClearTargets();
            foraging.ClearTargets();

            return new JObject
            {
                ["noFoodTarget"] = noFoodTarget,
                ["noGrassTarget"] = noGrassTarget,
                ["foodTargetEmptyAfterClear"] = foraging.FoodTarget == null,
                ["refreshCalls"] = refreshCalls,
                ["movementCalls"] = movementCalls,
                ["stopCalls"] = stopCalls,
                ["grassConsumptionCalls"] = grassConsumptionCalls
            };
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }
    #endregion
}
