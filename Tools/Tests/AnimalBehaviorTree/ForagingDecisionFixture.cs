using System;
using System.Collections.Generic;

// 正式觅食决策只从替身读取营养、候选和距离，阈值与分支由生产方法执行。
public sealed partial class ForagingDecisionFixture
{
    #region 觅食输入替身
    private readonly AnimalBehaviorTreeContext context = new();
    private readonly ForagingInput foraging = new();
    private readonly List<string> edibleTags = new();
    private bool GrassMealDue, grassIndependentOfHunger;
    private float unavailableRemaining, unavailableCooldown = 2f;
    private float enterHunger = .35f, exitHunger = .85f, eatDistance = 1.2f, grassRadius = 8f, searchDelay = .8f;

    private sealed class ForagingInput
    {
        public bool HasGrass, HasFood;
        public float GrassTargetDistance, FoodTargetDistance;
        public bool TryAcquireGrassTarget(bool due, float distance, float radius, float delay) => HasGrass;
        public bool TryFindFoodTarget(List<string> tags, bool refreshDetector) => HasFood;
    }
    #endregion

    #region 正式决策回归
    public static void Verify()
    {
        int checks = 0;
        void Check(bool value, string detail)
        {
            checks++;
            if (!value) throw new InvalidOperationException("觅食阈值错误：" + detail);
        }
        var test = new ForagingDecisionFixture();
        test.foraging.HasFood = true;
        test.foraging.FoodTargetDistance = 2f;
        test.context.Nutrition = .35f;
        Check(test.TryChooseMeal(false, out bool within) && !within, "进入阈值可追逐远处食物");
        test.context.Nutrition = .351f;
        Check(!test.TryChooseMeal(false, out _), "未进食且高于进入阈值不追食物");
        test.context.Nutrition = .6f;
        test.foraging.FoodTargetDistance = 1f;
        Check(test.TryChooseMeal(true, out within) && within, "进食中保持到退出阈值");
        test.foraging.FoodTargetDistance = 2f;
        Check(!test.TryChooseMeal(true, out within) && !within, "中间饱食度食物移远后停止追逐");
        test.context.Nutrition = .35f;
        Check(test.TryChooseMeal(true, out within) && !within, "重新饿到进入阈值时继续追逐");
        test.context.Nutrition = .849f;
        test.foraging.FoodTargetDistance = 1f;
        Check(test.TryChooseMeal(true, out within) && within, "低于退出阈值仍进食");
        test.context.Nutrition = .85f;
        Check(!test.TryChooseMeal(true, out _), "到达退出阈值停止进食");

        test = new ForagingDecisionFixture { GrassMealDue = true };
        test.foraging.HasGrass = true;
        test.foraging.GrassTargetDistance = 1f;
        test.context.Nutrition = .6f;
        Check(test.TryChooseMeal(true, out within) && within, "吃草中保持到退出阈值");
        test.foraging.GrassTargetDistance = 2f;
        Check(!test.TryChooseMeal(true, out within) && !within, "中间饱食度草目标移远后停止追逐");
        test.context.Nutrition = .35f;
        Check(test.TryChooseMeal(false, out within) && !within, "重新饿到进入阈值时继续追草");
        test.context.Nutrition = .95f;
        test.grassIndependentOfHunger = true;
        Check(test.TryChooseMeal(false, out within) && !within, "独立于饱食度的草食仍可追草");
        test.foraging.GrassTargetDistance = 1f;
        Check(test.TryChooseMeal(false, out within) && within, "独立草食到距离内可进食");
        test = new ForagingDecisionFixture();
        test.context.Nutrition = .1f;
        Check(!test.TryChooseMeal(false, out _) && test.unavailableRemaining == 2f, "无目标启用失败冷却");
        test.foraging.HasFood = true;
        Check(!test.TryChooseMeal(false, out _), "失败冷却期间不立即重试");
        Console.WriteLine($"PASS {checks} production foraging hunger, distance and independent-grass checks");
    }
    #endregion
}
