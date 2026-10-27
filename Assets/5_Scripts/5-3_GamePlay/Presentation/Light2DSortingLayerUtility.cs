using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// URP 14 的 2D 光照接收层适配器：用排序层名称配置 Light2D 与 ShadowCaster2D，空列表表示所有层。
/// 排序层仅决定接收对象，不代表物理高度，也不是 Physics Layer；反射集中在此，避免升级管线时散落私有字段依赖。
/// </summary>
public static class Light2DSortingLayerUtility
{
    #region URP 14 字段适配

    private static readonly FieldInfo LightLayersField = ResolveLayersField(typeof(Light2D));
    private static readonly FieldInfo ShadowLayersField = ResolveLayersField(typeof(ShadowCaster2D));
    private sealed class ProjectedLightLayers
    {
        internal int[] Layers;
        internal int Owners;
    }
    private static readonly Dictionary<Light2D, ProjectedLightLayers> projectedLights = new();
    private static readonly Dictionary<ShadowCaster2D, ProjectedLightLayers> projectedShadows = new();

    /// <summary>解析接收层；拼写错误直接报错，避免不存在的名称悄悄变成 Default。</summary>
    public static int[] ResolveLayerIds(string[] layerNames)
    {
        SortingLayer[] availableLayers = SortingLayer.layers;
        if (layerNames == null || layerNames.Length == 0)
        {
            var allLayers = new int[availableLayers.Length];
            for (int i = 0; i < availableLayers.Length; i++)
                allLayers[i] = availableLayers[i].id;
            return allLayers;
        }

        var resolved = new int[layerNames.Length];
        for (int i = 0; i < layerNames.Length; i++)
        {
            bool found = false;
            for (int j = 0; j < availableLayers.Length; j++)
            {
                if (!string.Equals(layerNames[i], availableLayers[j].name, StringComparison.Ordinal))
                    continue;
                resolved[i] = availableLayers[j].id;
                found = true;
                break;
            }

            if (!found)
                throw new ArgumentException($"[Light2D] 不存在的目标排序层：{layerNames[i]}");
        }

        // 统一保存顺序，让解析结果不依赖名称列表的填写顺序。
        Array.Sort(resolved);
        return resolved;
    }

    /// <summary>读取光源原有的接收层作为恢复基线；返回数组只允许读取，不允许原地修改。</summary>
    public static int[] GetLightLayers(Light2D light)
        => light == null ? null : projectedLights.TryGetValue(light, out var projected)
            ? projected.Layers : (int[])LightLayersField.GetValue(light);

    /// <summary>设置局部光源的接收层；Global Light 另有管线缓存，不能从此入口修改。</summary>
    public static void SetLightLayers(Light2D light, int[] layerIds)
    {
        if (light == null || light.lightType == Light2D.LightType.Global)
            return;
        if (projectedLights.TryGetValue(light, out var projected))
        {
            projected.Layers = layerIds;
            return;
        }
        LightLayersField.SetValue(light, layerIds);
    }

    // 让物理世界保留逻辑光照层，实际接收层交给可见世界中的投影副本。
    internal static void BeginProjectedLight(Light2D light)
    {
        if (projectedLights.TryGetValue(light, out var projected)) { projected.Owners++; return; }
        projectedLights.Add(light, new ProjectedLightLayers
        { Layers = (int[])LightLayersField.GetValue(light), Owners = 1 });
        LightLayersField.SetValue(light, Array.Empty<int>());
    }

    internal static void EndProjectedLight(Light2D light)
    {
        if (ReferenceEquals(light, null) || !projectedLights.TryGetValue(light, out var projected)) return;
        if (--projected.Owners != 0) return;
        projectedLights.Remove(light);
        if (light != null) LightLayersField.SetValue(light, projected.Layers);
    }

    /// <summary>设置建筑阴影影响的接收层，不改变建筑 Sprite 的绘制顺序。</summary>
    public static void SetShadowLayers(ShadowCaster2D caster, int[] layerIds)
    {
        if (caster == null) return;
        if (projectedShadows.TryGetValue(caster, out var projected)) projected.Layers = layerIds;
        else ShadowLayersField.SetValue(caster, layerIds);
    }

    internal static int[] GetShadowLayers(ShadowCaster2D caster)
        => caster == null ? null : projectedShadows.TryGetValue(caster, out var projected)
            ? projected.Layers : (int[])ShadowLayersField.GetValue(caster);

    internal static void BeginProjectedShadow(ShadowCaster2D caster)
    {
        if (projectedShadows.TryGetValue(caster, out var projected)) { projected.Owners++; return; }
        projectedShadows.Add(caster, new ProjectedLightLayers
        { Layers = (int[])ShadowLayersField.GetValue(caster), Owners = 1 });
        ShadowLayersField.SetValue(caster, Array.Empty<int>());
    }

    internal static void EndProjectedShadow(ShadowCaster2D caster)
    {
        if (ReferenceEquals(caster, null) || !projectedShadows.TryGetValue(caster, out var projected)) return;
        if (--projected.Owners != 0) return;
        projectedShadows.Remove(caster);
        if (caster != null) ShadowLayersField.SetValue(caster, projected.Layers);
    }

    internal static void CopyShadowLayers(ShadowCaster2D source, ShadowCaster2D destination)
    {
        if (source == null || destination == null) return;
        object layers = GetShadowLayers(source);
        if (!ReferenceEquals(layers, ShadowLayersField.GetValue(destination))) ShadowLayersField.SetValue(destination, layers);
    }

    /// <summary>只有光和阴影影响同一接收层时，才需要避让自身光源。</summary>
    public static bool SharesShadowLayers(Light2D light, ShadowCaster2D caster)
    {
        int[] lightLayers = GetLightLayers(light);
        int[] shadowLayers = GetShadowLayers(caster);
        if (lightLayers == null || shadowLayers == null)
            return false;
        for (int i = 0; i < lightLayers.Length; i++)
        {
            if (Array.IndexOf(shadowLayers, lightLayers[i]) >= 0)
                return true;
        }
        return false;
    }

    /// <summary>环绕世界镜像必须继承源光的接收层；数组只读共享，不逐帧复制。</summary>
    public static void CopyLightLayers(Light2D source, Light2D destination)
    {
        if (source == null || destination == null)
            return;
        object layers = GetLightLayers(source);
        if (!ReferenceEquals(layers, LightLayersField.GetValue(destination)))
            LightLayersField.SetValue(destination, layers);
    }

    /// <summary>只适配当前管线的序列化字段，缺失时明确暴露版本不兼容。</summary>
    private static FieldInfo ResolveLayersField(Type type)
        => type.GetField("m_ApplyToSortingLayers", BindingFlags.Instance | BindingFlags.NonPublic)
           ?? throw new MissingFieldException(type.FullName, "m_ApplyToSortingLayers");

    #endregion
}
