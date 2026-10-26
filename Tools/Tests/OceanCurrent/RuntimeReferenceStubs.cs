using UnityEngine;

#region 独立 CLR 未使用接口类型壳

// 仅补齐非水流入口的类型依赖，受测水流算法直接编译正式源文件。
public sealed class ItemStack
{
    public float CurrentWeight { get; set; }
    public float Weight { get; set; }
    public float Volume { get; set; }
}

public sealed class OceanCurrentUnusedLiquidDefinition
{
    public float BuoyancyThresholdMultiplier { get; set; }
}

public readonly struct WorldLiquidSourceTarget
{
    public OceanCurrentUnusedLiquidDefinition Liquid => null;
}

public static class WorldLiquidSourceResolver
{
    public static bool TryResolve(Vector2 position, out WorldLiquidSourceTarget target)
    {
        target = default;
        return false;
    }
}

#endregion
