using UnityEngine;

/// <summary>数量结算的通用取整工具，不依赖规则的提供来源。</summary>
public static class GameRuleMath
{
    #region 数量倍率

    public static int ScaleCount(int baseValue, float multiplier, int minimum = 0)
    {
        if (baseValue <= 0 || multiplier <= 0f)
            return minimum;
        return Mathf.Max(minimum, Mathf.RoundToInt(baseValue * multiplier));
    }

    public static int ScaleRandomizedAmount(int baseValue, float multiplier)
    {
        if (baseValue <= 0 || multiplier <= 0f)
            return 0;
        float scaled = baseValue * multiplier;
        int result = Mathf.FloorToInt(scaled);
        if (Random.value < scaled - result)
            result++;
        return Mathf.Max(0, result);
    }

    #endregion
}
