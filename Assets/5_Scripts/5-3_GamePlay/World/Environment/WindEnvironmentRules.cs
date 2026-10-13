using System.Collections.Generic;
using UnityEngine;

/// <summary>质量和星球重力决定起吹等级，达到等级后按档位增加推动速度。</summary>
public static class WindEnvironmentRules
{
    #region 风力推动规则

    public const float PushStartStrength = 0.8f;
    public const int MinimumPushWindLevel = 8;
    public const int MaximumWindLevel = 10;
    public const int UnreachableWindLevel = MaximumWindLevel + 1;
    public const double ReferenceGravity = 9.81d;
    public const float LightPushSpeed = 0.06f;
    public const float StrongPushSpeed = 0.2f;
    public const float MaximumPushSpeed = 0.5f;

    /// <summary>风强按完整等级向下取档，0.8 至不足 0.9 都属于八级风。</summary>
    public static int ResolveWindLevel(float windStrength)
    {
        if (!float.IsFinite(windStrength)) return 0;
        return Mathf.FloorToInt(Mathf.Clamp01(windStrength) * MaximumWindLevel);
    }

    /// <summary>达到起吹等级为轻推，再高一级为中推，再高两级为满推。</summary>
    public static float ResolvePushSpeed(float windStrength, int requiredWindLevel)
    {
        if (requiredWindLevel < MinimumPushWindLevel || requiredWindLevel > MaximumWindLevel) return 0f;
        int levelsAboveThreshold = ResolveWindLevel(windStrength) - requiredWindLevel;
        return levelsAboveThreshold switch
        {
            < 0 => 0f,
            0 => LightPushSpeed,
            1 => StrongPushSpeed,
            _ => MaximumPushSpeed
        };
    }

    /// <summary>重量折算到参考重力，轻重中点以内均分三档，达到中点就超过十级风的能力。</summary>
    public static int ResolveRequiredWindLevel(float mass, double surfaceGravity, float minimumMass, float maximumMass)
    {
        if (!float.IsFinite(mass) || mass <= 0f || !double.IsFinite(surfaceGravity) || surfaceGravity < 0d ||
            !float.IsFinite(minimumMass) || minimumMass <= 0f ||
            !float.IsFinite(maximumMass) || maximumMass <= minimumMass)
            return UnreachableWindLevel;

        double equivalentMass = mass * (surfaceGravity / ReferenceGravity);
        double cutoffMass = ((double)minimumMass + maximumMass) * 0.5d;
        if (!double.IsFinite(equivalentMass) || equivalentMass >= cutoffMass) return UnreachableWindLevel;
        double bandWidth = (cutoffMass - minimumMass) / 3d;
        if (equivalentMass < minimumMass + bandWidth) return MinimumPushWindLevel;
        if (equivalentMass < minimumMass + bandWidth * 2d) return MinimumPushWindLevel + 1;
        return MaximumWindLevel;
    }

    #endregion

    #region 目录质量范围缓存

    private static GameRes cachedResources;
    private static Dictionary<string, RuntimeItemDefinition> cachedDefinitions;
    private static ulong cachedRevision;
    private static float cachedMinimumMass;
    private static float cachedMaximumMass;

    /// <summary>统计完整物品与生物目录，包含 MOD；目录变更后才重新扫描。</summary>
    public static bool TryGetMassRange(out float minimumMass, out float maximumMass)
    {
        GameRes resources = GameRes.ExistingInstance;
        minimumMass = maximumMass = 0f;
        if (resources == null || !resources.isLoadFinish)
            return false;

        if (cachedResources != resources || !ReferenceEquals(cachedDefinitions, resources.ItemDefinitions) ||
            cachedRevision != resources.ItemDefinitionRevision)
        {
            cachedResources = resources;
            cachedDefinitions = resources.ItemDefinitions;
            cachedRevision = resources.ItemDefinitionRevision;
            cachedMinimumMass = float.PositiveInfinity;
            cachedMaximumMass = 0f;
            foreach (RuntimeItemDefinition definition in cachedDefinitions.Values)
            {
                float mass = definition?.UnitWeight ?? 0f;
                if (!float.IsFinite(mass) || mass <= 0f) continue;
                cachedMinimumMass = Mathf.Min(cachedMinimumMass, mass);
                cachedMaximumMass = Mathf.Max(cachedMaximumMass, mass);
            }
        }

        minimumMass = cachedMinimumMass;
        maximumMass = cachedMaximumMass;
        return float.IsFinite(minimumMass) && maximumMass > minimumMass;
    }

    #endregion
}
