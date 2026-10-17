using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>共用 URP 已启用灯登记表，按灯实例缓存纯表现代理分类，不持有已释放世界。</summary>
internal static class WorldLightRegistry
{
    #region 活跃灯登记表

    private static readonly Func<List<Light2D>> ReadRegisteredLights = CreateRegistryGetter();
    private static readonly ProfilerMarker RegistryMarker = new("FlatWorld.Lighting.ReadRegistry");
    private static readonly ProfilerMarker ClassifyMarker = new("FlatWorld.Lighting.ClassifySource");
    private static ConditionalWeakTable<Light2D, SourceRecord> sources = new();
    private static bool registryFailed;

    internal static bool TryGetActiveLights(out IReadOnlyList<Light2D> lights)
    {
        using var scope = RegistryMarker.Auto();
        lights = null;
        if (ReadRegisteredLights == null || registryFailed)
            return false;
        try
        {
            lights = ReadRegisteredLights();
            return lights != null;
        }
        catch (Exception)
        {
            registryFailed = true;
            return false;
        }
    }

    // 强类型委托只在初始化时适配一次，热路径沿用 URP 自己的启停登记结果。
    private static Func<List<Light2D>> CreateRegistryGetter()
    {
        try
        {
            Type manager = typeof(Light2D).Assembly.GetType("UnityEngine.Rendering.Universal.Light2DManager");
            MethodInfo getter = manager?.GetProperty("lights", BindingFlags.Static | BindingFlags.Public)
                ?.GetGetMethod();
            return getter == null ? null :
                (Func<List<Light2D>>)Delegate.CreateDelegate(typeof(Func<List<Light2D>>), getter);
        }
        catch (Exception)
        {
            return null;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        sources = new ConditionalWeakTable<Light2D, SourceRecord>();
        registryFailed = false;
    }

    #endregion

    #region 真实来源与环绕代理分类

    private sealed class SourceRecord
    {
        internal bool IsProxy;
        internal Transform Transform;
    }

    internal static bool IsGameplaySource(Light2D light)
        => light != null && !GetSource(light).IsProxy;

    internal static Transform GetSourceTransform(Light2D light)
    {
        SourceRecord source = GetSource(light);
        if (source.Transform == null)
            source.Transform = light.transform;
        return source.Transform;
    }

    internal static void RegisterProxy(Light2D light)
    {
        if (light != null)
            GetSource(light).IsProxy = true;
    }

    internal static void ForgetSource(Light2D light)
    {
        if (!ReferenceEquals(light, null))
            sources.Remove(light);
    }

    private static SourceRecord GetSource(Light2D light)
    {
        if (sources.TryGetValue(light, out SourceRecord source))
            return source;
        using var scope = ClassifyMarker.Auto();
        source = new SourceRecord { IsProxy = light.TryGetComponent<WorldTopologyLightProxy>(out _) };
        sources.Add(light, source);
        return source;
    }

    #endregion
}
