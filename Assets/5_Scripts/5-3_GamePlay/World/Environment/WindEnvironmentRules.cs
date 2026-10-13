using System.Collections.Generic;
using UnityEngine;

/// <summary>风力与当前质量共同决定物体的被动推动速度。</summary>
public static class WindEnvironmentRules
{
    #region 风力推动规则

    public const float PushStartStrength = 0.8f;
    public const float StrongPushStrength = 0.9f;
    public const float MaximumPushStrength = 1f;
    public const float LightPushSpeed = 0.06f;
    public const float StrongPushSpeed = 0.2f;
    public const float MaximumPushSpeed = 0.5f;

    /// <summary>八级前不推动，八至九级、九至十级之间连续增强。</summary>
    public static float ResolvePushSpeed(float windStrength, float mass, float minimumMass, float maximumMass)
        => ResolveWindSpeed(windStrength) * ResolveMassFactor(mass, minimumMass, maximumMass);

    private static float ResolveWindSpeed(float windStrength)
    {
        if (!float.IsFinite(windStrength) || windStrength < PushStartStrength)
            return 0f;

        if (windStrength < StrongPushStrength)
            return Mathf.Lerp(LightPushSpeed, StrongPushSpeed,
                Mathf.InverseLerp(PushStartStrength, StrongPushStrength, windStrength));

        return Mathf.Lerp(StrongPushSpeed, MaximumPushSpeed,
            Mathf.InverseLerp(StrongPushStrength, MaximumPushStrength, windStrength));
    }

    /// <summary>最轻物体系数为一，质量达到目录轻重中点时归零。</summary>
    public static float ResolveMassFactor(float mass, float minimumMass, float maximumMass)
    {
        if (!float.IsFinite(mass) || mass < 0f || !float.IsFinite(minimumMass) || minimumMass < 0f ||
            !float.IsFinite(maximumMass) || maximumMass <= minimumMass)
            return 0f;

        float cutoffMass = minimumMass * 0.5f + maximumMass * 0.5f;
        return Mathf.InverseLerp(cutoffMass, minimumMass, mass);
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
