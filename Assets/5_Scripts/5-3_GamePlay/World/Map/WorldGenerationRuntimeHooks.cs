using System;
using UnityEngine;

/// <summary>
/// Optional runtime configuration seam invoked after a Map instance has inherited
/// its dimension settings and before generation starts. Normal gameplay has no
/// subscribers; automation, diagnostics, or a server host may temporarily adjust
/// the real generator components without modifying Prefab assets.
/// </summary>
public static class WorldGenerationRuntimeHooks
{
    public static event Action<Map> BeforeMapGeneration;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        BeforeMapGeneration = null;
    }

    public static void ApplyBeforeMapGeneration(Map map)
    {
        if (map == null)
            throw new ArgumentNullException(nameof(map));
        BeforeMapGeneration?.Invoke(map);
    }
}
