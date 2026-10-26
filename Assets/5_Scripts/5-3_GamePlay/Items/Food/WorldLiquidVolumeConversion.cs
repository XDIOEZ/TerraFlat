using System;
using FlatWorld.WorldModel;

/// <summary>地表整份按十升结算，饮用与配方继续使用液体定义自己的份体积。</summary>
public static class WorldLiquidVolumeConversion
{
    #region 地表与容器体积换算
    public const float ReferenceServingLiters = .25f;
    public static float GetContainerCapacityLiters(int capacity) => capacity * ReferenceServingLiters;
    public static float WorldUnitsToLiquidServings(int units, float litersPerServing)
        => units > 0 && float.IsFinite(litersPerServing) && litersPerServing > 0f
            ? (float)((double)units * LiquidCellValue.LitersPerUnit / litersPerServing) : 0f;
    public static int LiquidServingsToWholeWorldUnits(float servings, float litersPerServing)
    {
        if (!float.IsFinite(servings) || servings <= 0f || !float.IsFinite(litersPerServing) || litersPerServing <= 0f) return 0;
        int units = (int)Math.Min(int.MaxValue, Math.Ceiling((double)servings * litersPerServing / LiquidCellValue.LitersPerUnit));
        // 用同一个浮点表示回验候选整份，保留0.3升等份体积的往返精度，同时拒绝真正不足的尾量。
        while (units > 0 && WorldUnitsToLiquidServings(units, litersPerServing) > servings) units--;
        return units;
    }
    public static float QuantizeWholeWorldLiquidServings(float requested, float available, float litersPerServing)
        => float.IsFinite(requested) && float.IsFinite(available)
            ? WorldUnitsToLiquidServings(LiquidServingsToWholeWorldUnits(Math.Min(requested, available), litersPerServing), litersPerServing) : 0f;
    #endregion
}
