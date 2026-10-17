using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static partial class GameplayTestSystemProbe
{
    #region 光源登记与实时采样回归

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterLightSamplingRules()
    {
        Register("environment", "light_registry", LightRegistryLifecycle);
        Register("environment", "light_sampling", LightSamplingValues);
    }

    // 夹具光源不接收任何排序层，只采正式登记与数值接口，预期数值由 Lua 判断。
    private static Light2D CreateProbeLight(string name, Light2D.LightType type = Light2D.LightType.Point)
    {
        var owner = new GameObject("LightSamplingProbe_" + name) { hideFlags = HideFlags.HideAndDontSave };
        owner.SetActive(false);
        Light2D light = owner.AddComponent<Light2D>();
        light.lightType = Light2D.LightType.Point;
        Light2DSortingLayerUtility.SetLightLayers(light, Array.Empty<int>());
        light.lightType = type;
        light.color = Color.white;
        light.intensity = 0.8f;
        light.pointLightInnerRadius = 2f;
        light.pointLightOuterRadius = 6f;
        owner.SetActive(true);
        return light;
    }

    private static bool ContainsRegisteredProbeLight(Light2D target)
    {
        if (!WorldLightRegistry.TryGetActiveLights(out IReadOnlyList<Light2D> lights))
            return false;
        for (int i = 0; i < lights.Count; i++)
            if (ReferenceEquals(lights[i], target))
                return true;
        return false;
    }

    private static JObject LightRegistryLifecycle(JObject args)
    {
        Light2D light = null;
        Light2D recreated = null;
        try
        {
            bool available = WorldLightRegistry.TryGetActiveLights(out _);
            light = CreateProbeLight("Registry");
            bool created = ContainsRegisteredProbeLight(light);
            light.enabled = false;
            bool disabled = !ContainsRegisteredProbeLight(light);
            light.enabled = true;
            bool reenabled = ContainsRegisteredProbeLight(light);
            light.gameObject.SetActive(false);
            bool inactive = !ContainsRegisteredProbeLight(light);
            light.gameObject.SetActive(true);
            bool reactivated = ContainsRegisteredProbeLight(light);
            bool sourceBeforeMarker = WorldLightRegistry.IsGameplaySource(light);
            WorldTopologyLightProxy marker = light.gameObject.AddComponent<WorldTopologyLightProxy>();
            bool proxyExcluded = sourceBeforeMarker && !WorldLightRegistry.IsGameplaySource(light);
            UnityEngine.Object.DestroyImmediate(marker);
            bool proxyRemoved = WorldLightRegistry.IsGameplaySource(light);
            UnityEngine.Object.DestroyImmediate(light.gameObject);
            bool destroyed = !ContainsRegisteredProbeLight(light);
            recreated = CreateProbeLight("RegistryRecreated");
            return new JObject
            {
                ["registryAvailable"] = available,
                ["createdRegistered"] = created, ["disabledRemoved"] = disabled,
                ["reenabledRegistered"] = reenabled, ["inactiveRemoved"] = inactive,
                ["reactivatedRegistered"] = reactivated, ["destroyedRemoved"] = destroyed,
                ["recreatedRegistered"] = ContainsRegisteredProbeLight(recreated),
                ["proxyExcluded"] = proxyExcluded, ["proxyRemovedRestored"] = proxyRemoved
            };
        }
        finally
        {
            if (light != null) UnityEngine.Object.DestroyImmediate(light.gameObject);
            if (recreated != null) UnityEngine.Object.DestroyImmediate(recreated.gameObject);
        }
    }

    private static JObject LightSamplingValues(JObject args)
    {
        var owners = new List<GameObject>();
        try
        {
            Light2D Add(string name, Light2D.LightType type = Light2D.LightType.Point)
            {
                Light2D light = CreateProbeLight(name, type);
                owners.Add(light.gameObject);
                return light;
            }
            Light2D point = Add("Point");
            Light2D proxy = Add("Proxy");
            proxy.intensity = 0.4f;
            proxy.gameObject.AddComponent<WorldTopologyLightProxy>();
            Light2D time = Add("Time", Light2D.LightType.Global);
            Light2D extraGlobal = Add("ExtraGlobal", Light2D.LightType.Global);
            extraGlobal.intensity = 0.1f;
            Light2D[] points = { point };
            float At(Vector2 position, WorldTopologyDomain domain = default)
                => LightLayerMgr.EvaluateLights(points, position, false, null, 0f, domain);
            var result = new JObject
            {
                ["dark"] = LightLayerMgr.EvaluateLights(Array.Empty<Light2D>(), Vector2.zero,
                    false, null, 0f, default),
                ["timeOnlyOnce"] = LightLayerMgr.EvaluateLights(new[] { time, extraGlobal }, Vector2.zero,
                    true, time, 0.3f, default),
                ["center"] = At(Vector2.zero), ["innerBoundary"] = At(new Vector2(2f, 0f)),
                ["falloff"] = At(new Vector2(4f, 0f)), ["outerBoundary"] = At(new Vector2(6f, 0f))
            };
            point.color = new Color(0.5f, 0.25f, 0.1f, 0.5f);
            result["changedColor"] = At(Vector2.zero);
            point.intensity = 0f;
            result["zeroIntensity"] = At(Vector2.zero);
            point.color = Color.white;
            point.intensity = 0.8f;
            point.enabled = false;
            result["disabled"] = At(Vector2.zero);
            point.enabled = true;
            result["reenabled"] = At(Vector2.zero);
            point.transform.position = new Vector3(10f, 0f, 0f);
            result["movedOutside"] = At(Vector2.zero);
            point.pointLightInnerRadius = 0f;
            point.pointLightOuterRadius = 12f;
            result["changedRadius"] = At(Vector2.zero);
            point.pointLightInnerRadius = 2f;
            point.pointLightOuterRadius = 6f;
            point.transform.position = new Vector3(15f, 0f, 0f);
            var wrapped = new WorldTopologyDomain(new int2(-16, -16), new int2(32, 32), true);
            result["wrappedSeam"] = At(new Vector2(-15f, 0f), wrapped);
            result["wrappedEquivalent"] = At(new Vector2(17f, 0f), wrapped);
            result["infiniteFar"] = At(new Vector2(-15f, 0f));
            point.transform.position = Vector3.zero;
            point.intensity = 0.4f;
            result["proxyIgnored"] = LightLayerMgr.EvaluateLights(new[] { point, proxy },
                Vector2.zero, false, null, 0f, default);
            point.lightType = Light2D.LightType.Freeform;
            result["unsupportedType"] = At(Vector2.zero);
            point.lightType = Light2D.LightType.Point;
            point.intensity = 2f;
            result["saturated"] = At(Vector2.zero);
            point.intensity = 0.4f;
            At(Vector2.zero);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 256; i++)
                At(Vector2.zero);
            result["sampleAllocatedBytes"] = GC.GetAllocatedBytesForCurrentThread() - before;
            UnityEngine.Object.DestroyImmediate(point.gameObject);
            result["destroyed"] = At(Vector2.zero);
            return result;
        }
        finally
        {
            foreach (GameObject owner in owners)
                if (owner != null) UnityEngine.Object.DestroyImmediate(owner);
        }
    }

    #endregion
}
